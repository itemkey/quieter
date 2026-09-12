using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Inventory;
using Quieter.Persistence;
using Quieter.Survival;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.World
{
    public sealed class PlacedObjectWorldService : MonoBehaviour
    {
        private const string SnapshotMessage = "quieter.placed-objects";

        private sealed class RuntimeObject
        {
            public ulong ObjectId;
            public string OwnerAccountId = string.Empty;
            public string AssignedCharacterId = string.Empty;
            public ushort ItemId;
            public Vector3 Position;
            public float Yaw;
            public bool Locked;
            public ItemStackState Input;
            public ulong BusyClientId = ulong.MaxValue;
            public DateTime CreatedAtUtc;
            public DateTime UpdatedAtUtc;

            public bool IsBusy => BusyClientId != ulong.MaxValue;
        }

        private readonly Dictionary<ulong, RuntimeObject> objects = new();
        private readonly Dictionary<ulong, ResearchTableView> views = new();
        private readonly Dictionary<ulong, SurvivalStructureView> structureViews = new();
        private readonly HashSet<ulong> dirtyObjects = new();
        private readonly SemaphoreSlim flushGate = new(1, 1);
        private NetworkManager networkManager;
        private IWorldRepository repository;
        private WorldDefinition definition;
        private Transform viewRoot;
        private bool configured;
        private bool messagingRegistered;
        private bool flushRunning;
        private bool fullSaveRequired;
        private float nextFlushAt;
        private uint objectNonce;
        private uint persistenceRevision;
        private float lastFuelTickAt;
        private float nextFuelTickAt;

        public event Action Changed;
        public WorldDefinition Definition => definition;

        public void Configure(NetworkManager manager)
        {
            if (configured) return;
            networkManager = manager;
            configured = true;
            EnsureMessagingRegistered();
        }

        public async Task InitializeServerAsync(
            WorldDefinition worldDefinition,
            IWorldRepository worldRepository,
            CancellationToken cancellationToken)
        {
            definition = worldDefinition;
            repository = worldRepository;
            ClearObjects();
            var stored = await repository.LoadPlacedObjectsAsync(
                definition.WorldId, cancellationToken);
            foreach (var entry in stored)
            {
                if (entry == null || !SurvivalStructureRules.SupportsPlacement(entry.ItemId)
                    || !ulong.TryParse(entry.ObjectId, out var objectId) || objectId == 0)
                {
                    continue;
                }
                var runtime = new RuntimeObject
                {
                    ObjectId = objectId,
                    OwnerAccountId = entry.OwnerAccountId ?? string.Empty,
                    AssignedCharacterId = entry.AssignedCharacterId ?? string.Empty,
                    ItemId = entry.ItemId,
                    Position = new Vector3(entry.X, entry.Y, entry.Z),
                    Yaw = entry.Yaw,
                    Locked = entry.Locked && SurvivalStructureRules.SupportsLock(entry.ItemId),
                    Input = FromStoredStack(entry.Input),
                    CreatedAtUtc = ParseDate(entry.CreatedAtUtc),
                    UpdatedAtUtc = ParseDate(entry.UpdatedAtUtc),
                };
                if (runtime.ItemId == SurvivalStructureRules.HearthItemId
                    && !runtime.Input.IsEmpty)
                {
                    runtime.Input = SurvivalStructureRules.BurnFuel(
                        runtime.Input,
                        Mathf.Max(0f, (float)(DateTime.UtcNow - runtime.UpdatedAtUtc).TotalSeconds));
                    runtime.UpdatedAtUtc = DateTime.UtcNow;
                    MarkDirty(objectId, fullSave: false);
                }
                objects[objectId] = runtime;
                CreateOrUpdateView(runtime, networkManager != null && networkManager.IsClient);
            }
            nextFlushAt = Time.unscaledTime + 5f;
            lastFuelTickAt = Time.unscaledTime;
            nextFuelTickAt = lastFuelTickAt + 5f;
        }

        public void InitializeClient(WorldDefinition worldDefinition)
        {
            definition = worldDefinition;
            EnsureMessagingRegistered();
            if (networkManager != null && networkManager.IsServer)
            {
                RebuildViews(renderVisuals: true);
                return;
            }
            ClearObjects();
        }

        public bool TryGetView(ulong objectId, out ResearchTableView view) =>
            views.TryGetValue(objectId, out view) && view != null;

        public bool TryGetStructureView(ulong objectId, out SurvivalStructureView view) =>
            structureViews.TryGetValue(objectId, out view) && view != null;

        public bool TryGetState(
            ulong objectId,
            out Vector3 position,
            out ItemStackState input,
            out bool busy)
        {
            if (objects.TryGetValue(objectId, out var state))
            {
                position = state.Position;
                input = networkManager != null && networkManager.IsServer
                    ? state.Input
                    : state.Input.ForReplication();
                busy = state.IsBusy;
                return true;
            }
            position = default;
            input = default;
            busy = false;
            return false;
        }

        public bool TryGetStructureSecurity(
            ulong objectId, out bool locked, out bool ownedBySomeone)
        {
            if (objects.TryGetValue(objectId, out var state))
            {
                locked = state.Locked;
                ownedBySomeone = !string.IsNullOrWhiteSpace(state.OwnerAccountId);
                return true;
            }
            locked = false;
            ownedBySomeone = false;
            return false;
        }

        public bool IsResearchTable(ulong objectId) => objects.TryGetValue(objectId, out var state)
            && state.ItemId == ResourceBalance.ResearchTableItemId;

        public bool TryPlace(
            Vector3 position,
            float yaw,
            ushort itemId,
            string ownerAccountId,
            out ulong objectId)
        {
            objectId = 0;
            if (networkManager == null || !networkManager.IsServer || definition.WorldId == 0
                || !IsFinite(position) || !float.IsFinite(yaw)
                || !SurvivalStructureRules.SupportsPlacement(itemId))
            {
                return false;
            }
            position.x = Mathf.Clamp(position.x, definition.WorldMinimum.x + 1f,
                definition.WorldMaximum.x - 1f);
            position.z = Mathf.Clamp(position.z, definition.WorldMinimum.z + 1f,
                definition.WorldMaximum.z - 1f);
            objectId = CreateObjectId(position);
            var now = DateTime.UtcNow;
            var state = new RuntimeObject
            {
                ObjectId = objectId,
                OwnerAccountId = ownerAccountId ?? string.Empty,
                ItemId = itemId,
                Position = position,
                Yaw = Mathf.Repeat(yaw, 360f),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            objects.Add(objectId, state);
            CreateOrUpdateView(state, networkManager.IsClient);
            MarkDirty(objectId, fullSave: false);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryPlace(
            Vector3 position, float yaw, ushort itemId, out ulong objectId) =>
            TryPlace(position, yaw, itemId, string.Empty, out objectId);

        public bool TryPlace(Vector3 position, float yaw, out ulong objectId) => TryPlace(
            position, yaw, ResourceBalance.ResearchTableItemId, string.Empty, out objectId);

        public bool TryAssignNearestOwnedBed(
            Vector3 position, string ownerAccountId, string characterId,
            out ulong objectId, out string error)
        {
            objectId = 0;
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer
                || string.IsNullOrWhiteSpace(ownerAccountId)
                || string.IsNullOrWhiteSpace(characterId))
            {
                error = "Кровать нельзя назначить.";
                return false;
            }
            RuntimeObject closest = null;
            var closestSquared = 12f * 12f;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.BedItemId
                    || !string.Equals(state.OwnerAccountId, ownerAccountId,
                        StringComparison.Ordinal)
                    || !string.IsNullOrWhiteSpace(state.AssignedCharacterId)
                        && !string.Equals(state.AssignedCharacterId, characterId,
                            StringComparison.OrdinalIgnoreCase)) continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                closest = state;
            }
            if (closest == null)
            {
                error = "Рядом нет свободной принадлежащей вам кровати.";
                return false;
            }
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.BedItemId
                    || state.ObjectId == closest.ObjectId
                    || !string.Equals(state.OwnerAccountId, ownerAccountId,
                        StringComparison.Ordinal)
                    || !string.Equals(state.AssignedCharacterId, characterId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                state.AssignedCharacterId = string.Empty;
                Touch(state);
            }
            closest.AssignedCharacterId = characterId;
            Touch(closest);
            objectId = closest.ObjectId;
            return true;
        }

        public bool HasOwnedBedAssigned(string ownerAccountId, string characterId)
        {
            if (string.IsNullOrWhiteSpace(ownerAccountId)
                || string.IsNullOrWhiteSpace(characterId)) return false;
            foreach (var state in objects.Values)
            {
                if (state.ItemId == SurvivalStructureRules.BedItemId
                    && string.Equals(state.OwnerAccountId, ownerAccountId,
                        StringComparison.Ordinal)
                    && string.Equals(state.AssignedCharacterId, characterId,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool TryGetAssignedBedHygiene(
            Vector3 position, string characterId, out float hygiene)
        {
            hygiene = 1f;
            if (string.IsNullOrWhiteSpace(characterId)) return false;
            RuntimeObject closest = null;
            var closestSquared = 3f * 3f;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.BedItemId
                    || !string.Equals(state.AssignedCharacterId, characterId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                closest = state;
            }
            if (closest == null) return false;
            hygiene = closest.Input.IsEmpty
                ? 1f
                : Mathf.Min(closest.Input.Cleanliness / 10000f,
                    1f - closest.Input.BiologicalContamination / 10000f);
            return true;
        }

        public bool TrySoilAssignedBed(
            Vector3 position, string characterId, bool bowelAccident)
        {
            if (networkManager == null || !networkManager.IsServer
                || string.IsNullOrWhiteSpace(characterId)) return false;
            RuntimeObject closest = null;
            var closestSquared = 3f * 3f;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.BedItemId
                    || !string.Equals(state.AssignedCharacterId, characterId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                closest = state;
            }
            if (closest == null) return false;
            var bedding = closest.Input.IsEmpty
                ? new ItemStackState(
                    SurvivalStructureRules.BedItemId, 1, cleanliness: 10000)
                : closest.Input;
            closest.Input = ItemHygieneRules.ApplyEliminationSoiling(
                bedding, bowelAccident);
            Touch(closest);
            return true;
        }

        public bool TrySetHoldingCellLocked(
            ulong objectId, string actorAccountId, bool locked, out string error)
        {
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.HoldingCellItemId)
            {
                error = "Камера больше не существует.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(actorAccountId)
                || !string.Equals(state.OwnerAccountId, actorAccountId, StringComparison.Ordinal))
            {
                error = "Замок принадлежит другому владельцу.";
                return false;
            }
            if (state.Locked == locked) return true;
            state.Locked = locked;
            Touch(state);
            return true;
        }

        public bool TrySetOwnedStructureLocked(
            ulong objectId, string actorAccountId, bool locked, out string error)
        {
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || !SurvivalStructureRules.SupportsLock(state.ItemId))
            {
                error = "Замок больше не существует.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(actorAccountId)
                || !string.Equals(state.OwnerAccountId, actorAccountId, StringComparison.Ordinal))
            {
                error = "Замок принадлежит другому владельцу.";
                return false;
            }
            if (state.Locked == locked) return true;
            state.Locked = locked;
            Touch(state);
            return true;
        }

        public bool TryInsertChestItem(
            ulong objectId, string actorAccountId, ItemStackState stack, out string error)
        {
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer || stack.IsEmpty
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.ChestItemId)
            {
                error = "Сундук недоступен.";
                return false;
            }
            if (state.Locked && !string.Equals(
                    state.OwnerAccountId, actorAccountId ?? string.Empty, StringComparison.Ordinal))
            {
                error = "Сундук заперт владельцем.";
                return false;
            }
            if (!state.Input.IsEmpty)
            {
                error = "В этом простом сундуке уже занят отсек.";
                return false;
            }
            state.Input = stack;
            Touch(state);
            return true;
        }

        public bool TryTakeChestItem(
            ulong objectId, string actorAccountId, out ItemStackState stack, out string error)
        {
            stack = default;
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.ChestItemId)
            {
                error = "Сундук недоступен.";
                return false;
            }
            if (state.Locked && !string.Equals(
                    state.OwnerAccountId, actorAccountId ?? string.Empty, StringComparison.Ordinal))
            {
                error = "Сундук заперт владельцем.";
                return false;
            }
            if (state.Input.IsEmpty)
            {
                error = "Сундук пуст.";
                return false;
            }
            stack = state.Input;
            state.Input = default;
            Touch(state);
            return true;
        }

        public bool TrySabotageChestItem(
            ulong objectId, string employerAccountId, out ushort damagedItemId)
        {
            damagedItemId = 0;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.ChestItemId
                || !string.Equals(state.OwnerAccountId,
                    employerAccountId ?? string.Empty, StringComparison.Ordinal)
                || state.Input.IsEmpty)
                return false;
            damagedItemId = state.Input.ItemId;
            if (state.Input.Quantity <= 1)
                state.Input = default;
            else
                state.Input.Quantity--;
            Touch(state);
            return true;
        }

        public bool TryFindOwnedLockedHoldingCell(
            Vector3 position, string ownerAccountId, out ulong objectId)
        {
            objectId = 0;
            if (string.IsNullOrWhiteSpace(ownerAccountId)) return false;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.HoldingCellItemId || !state.Locked
                    || !string.Equals(state.OwnerAccountId, ownerAccountId, StringComparison.Ordinal)
                    || !SurvivalStructureRules.IsInsideHoldingCell(
                        position, state.Position, state.Yaw)) continue;
                objectId = state.ObjectId;
                return true;
            }
            return false;
        }

        public bool IsInsideOwnedLockedHoldingCell(
            ulong objectId, Vector3 position, string ownerAccountId)
        {
            return objectId != 0 && objects.TryGetValue(objectId, out var state)
                && state.ItemId == SurvivalStructureRules.HoldingCellItemId && state.Locked
                && string.Equals(state.OwnerAccountId, ownerAccountId, StringComparison.Ordinal)
                && SurvivalStructureRules.IsInsideHoldingCell(position, state.Position, state.Yaw);
        }

        public bool TryRemoveJustPlaced(ulong objectId, string ownerAccountId)
        {
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state) || state.IsBusy
                || !state.Input.IsEmpty
                || !string.Equals(state.OwnerAccountId, ownerAccountId ?? string.Empty,
                    StringComparison.Ordinal)) return false;
            objects.Remove(objectId);
            if (views.Remove(objectId, out var view) && view != null) Destroy(view.gameObject);
            if (structureViews.Remove(objectId, out var structure) && structure != null)
                Destroy(structure.gameObject);
            MarkDirty(objectId, fullSave: true);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool CanAcceptFuel(ulong objectId, ushort itemId)
        {
            if (networkManager == null || !networkManager.IsServer
                || !SurvivalStructureRules.IsFuel(itemId)
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.HearthItemId)
            {
                return false;
            }
            return state.Input.IsEmpty
                || state.Input.ItemId == itemId
                && state.Input.Quantity < SurvivalStructureRules.MaximumFuelUnits;
        }

        public bool TryAddFuel(ulong objectId, ushort itemId)
        {
            if (!CanAcceptFuel(objectId, itemId)) return false;
            var state = objects[objectId];
            var next = SurvivalStructureRules.AddFuel(state.Input, itemId);
            if (next.Equals(state.Input)) return false;
            state.Input = next;
            Touch(state);
            return true;
        }

        public bool TryGetBurningHearth(ulong objectId, out Vector3 position)
        {
            position = default;
            if (!objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.HearthItemId
                || state.Input.IsEmpty)
            {
                return false;
            }
            position = state.Position;
            return true;
        }

        public bool TryFindBurningHearth(Vector3 position, float radius, out ulong objectId)
        {
            objectId = 0;
            var closestSquared = radius * radius;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.HearthItemId || state.Input.IsEmpty)
                    continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                objectId = state.ObjectId;
            }
            return objectId != 0;
        }

        public bool TryFindClosestOwnedStructure(
            ushort itemId, Vector3 position, float radius, string ownerAccountId,
            out ulong objectId, out Vector3 structurePosition)
        {
            objectId = 0;
            structurePosition = default;
            var closestSquared = Mathf.Max(0f, radius) * Mathf.Max(0f, radius);
            foreach (var state in objects.Values)
            {
                if (state.ItemId != itemId
                    || !string.Equals(state.OwnerAccountId, ownerAccountId ?? string.Empty,
                        StringComparison.Ordinal)) continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                objectId = state.ObjectId;
                structurePosition = state.Position;
            }
            return objectId != 0;
        }

        public bool TryFindClosestOwnedWastePit(
            Vector3 position, float radius, string ownerAccountId,
            out ulong objectId, out Vector3 pitPosition)
        {
            objectId = 0;
            pitPosition = default;
            var closestSquared = Mathf.Max(0f, radius) * Mathf.Max(0f, radius);
            foreach (var state in objects.Values)
            {
                if (!SurvivalStructureRules.IsWastePit(state.ItemId)
                    || !string.Equals(state.OwnerAccountId, ownerAccountId ?? string.Empty,
                        StringComparison.Ordinal)) continue;
                var squared = (position - state.Position).sqrMagnitude;
                if (squared > closestSquared) continue;
                closestSquared = squared;
                objectId = state.ObjectId;
                pitPosition = state.Position;
            }
            return objectId != 0;
        }

        public bool CanPlaceStructure(Vector3 position, ushort itemId)
        {
            if (!SurvivalStructureRules.SupportsPlacement(itemId) || definition.WorldId == 0
                || position.x < definition.WorldMinimum.x + 1f
                || position.x > definition.WorldMaximum.x - 1f
                || position.z < definition.WorldMinimum.z + 1f
                || position.z > definition.WorldMaximum.z - 1f) return false;
            var radius = Mathf.Max(
                SurvivalStructureRules.PlacementHalfExtents(itemId).x,
                SurvivalStructureRules.PlacementHalfExtents(itemId).z);
            foreach (var state in objects.Values)
            {
                var other = SurvivalStructureRules.PlacementHalfExtents(state.ItemId);
                var otherRadius = Mathf.Max(other.x, other.z);
                var delta = state.Position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < Mathf.Pow(radius + otherRadius + 0.35f, 2f))
                    return false;
            }
            return true;
        }

        public bool CanInteract(ulong objectId, Transform actor, float maximumDistance)
        {
            if (actor == null || !objects.TryGetValue(objectId, out var state)
                || Vector3.Distance(actor.position, state.Position) > maximumDistance) return false;
            var origin = actor.position + Vector3.up * 1.45f;
            var direction = state.Position + Vector3.up * 0.2f - origin;
            var hits = Physics.RaycastAll(origin, direction.normalized, direction.magnitude + 0.25f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(actor)) continue;
                var structure = hit.collider.GetComponentInParent<SurvivalStructureView>();
                return structure != null && structure.ObjectId == objectId;
            }
            return false;
        }

        public bool TryGetWastePitSpace(
            ulong objectId,
            out Vector3 position,
            out ushort availableMilliliters)
        {
            position = default;
            availableMilliliters = 0;
            if (!objects.TryGetValue(objectId, out var state)
                || !SurvivalStructureRules.IsWastePit(state.ItemId))
            {
                return false;
            }
            position = state.Position;
            var contents = state.Input.LiquidKind == LiquidKind.Waste
                ? state.Input.LiquidMilliliters
                : 0;
            availableMilliliters = (ushort)Mathf.Max(
                0, SurvivalStructureRules.WastePitCapacityMilliliters - contents);
            return availableMilliliters > 0;
        }

        public bool TryRouteSanitaryWaste(
            ulong fixtureObjectId,
            ushort milliliters,
            float biologicalLoad,
            float toxinLoad,
            out ulong destinationPitId,
            out string error)
        {
            destinationPitId = 0;
            error = string.Empty;
            if (networkManager == null || !networkManager.IsServer || milliliters == 0
                || !objects.TryGetValue(fixtureObjectId, out var fixture)
                || !SurvivalStructureRules.IsSanitaryFixture(fixture.ItemId))
            {
                error = "Санитарный узел недоступен.";
                return false;
            }

            var frontier = new Queue<RuntimeObject>();
            var visited = new HashSet<ulong> { fixture.ObjectId };
            frontier.Enqueue(fixture);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                foreach (var candidate in objects.Values)
                {
                    if (visited.Contains(candidate.ObjectId)
                        || !string.Equals(candidate.OwnerAccountId, fixture.OwnerAccountId,
                            StringComparison.Ordinal)
                        || !SurvivalStructureRules.CanConnectSanitation(
                            current.ItemId, current.Position,
                            candidate.ItemId, candidate.Position)) continue;
                    visited.Add(candidate.ObjectId);
                    if (SurvivalStructureRules.IsWastePit(candidate.ItemId))
                    {
                        if (!TryDepositWaste(candidate.ObjectId, milliliters,
                                biologicalLoad, toxinLoad))
                        {
                            error = "Выгребная яма переполнена.";
                            return false;
                        }
                        destinationPitId = candidate.ObjectId;
                        return true;
                    }
                    if (candidate.ItemId == SurvivalStructureRules.DrainItemId)
                        frontier.Enqueue(candidate);
                }
            }
            error = "Нет непрерывного уклона от санитарного узла к выгребной яме.";
            return false;
        }

        public bool TryStoreWater(
            ulong objectId,
            ushort milliliters,
            float biologicalLoad,
            float toxinLoad)
        {
            if (networkManager == null || !networkManager.IsServer || milliliters == 0
                || !float.IsFinite(biologicalLoad) || !float.IsFinite(toxinLoad)
                || !objects.TryGetValue(objectId, out var state)
                || !SurvivalStructureRules.StoresWater(state.ItemId)) return false;
            var capacity = SurvivalStructureRules.WaterStorageCapacity(state.ItemId);
            var oldVolume = state.Input.LiquidKind == LiquidKind.Water
                ? state.Input.LiquidMilliliters : 0;
            if (oldVolume + milliliters > capacity
                || !state.Input.IsEmpty && state.Input.LiquidKind is not (
                    LiquidKind.None or LiquidKind.Water))
                return false;
            var newVolume = oldVolume + milliliters;
            var mixedBiological = (state.Input.BiologicalContamination / 10000f
                    * oldVolume + Mathf.Clamp01(biologicalLoad) * milliliters)
                / newVolume;
            var mixedToxins = (state.Input.ToxinContamination / 10000f
                    * oldVolume + Mathf.Clamp01(toxinLoad) * milliliters)
                / newVolume;
            state.Input = new ItemStackState(
                30, 1, freshness: 10000,
                biologicalContamination: (ushort)Mathf.RoundToInt(
                    mixedBiological * 10000f),
                toxinContamination: (ushort)Mathf.RoundToInt(mixedToxins * 10000f),
                cleanliness: 10000,
                liquidMilliliters: (ushort)newVolume,
                liquidKind: LiquidKind.Water);
            Touch(state);
            return true;
        }

        public bool TryGetWaterStorage(
            ulong objectId,
            out Vector3 position,
            out ushort storedMilliliters,
            out ushort availableMilliliters)
        {
            position = default;
            storedMilliliters = 0;
            availableMilliliters = 0;
            if (!objects.TryGetValue(objectId, out var state)
                || !SurvivalStructureRules.StoresWater(state.ItemId)) return false;
            position = state.Position;
            storedMilliliters = state.Input.LiquidKind == LiquidKind.Water
                ? state.Input.LiquidMilliliters : (ushort)0;
            availableMilliliters = (ushort)Mathf.Max(0,
                SurvivalStructureRules.WaterStorageCapacity(state.ItemId)
                - storedMilliliters);
            return true;
        }

        public bool TryDrawStoredWater(
            ulong objectId,
            ushort requestedMilliliters,
            Func<ushort, float, float, bool> receiver,
            out ushort transferredMilliliters)
        {
            transferredMilliliters = 0;
            if (networkManager == null || !networkManager.IsServer
                || requestedMilliliters == 0 || receiver == null
                || !objects.TryGetValue(objectId, out var state)
                || !SurvivalStructureRules.StoresWater(state.ItemId)
                || state.Input.LiquidKind != LiquidKind.Water
                || state.Input.LiquidMilliliters == 0) return false;
            var amount = (ushort)Mathf.Min(requestedMilliliters,
                state.Input.LiquidMilliliters);
            if (!receiver(amount,
                    state.Input.BiologicalContamination / 10000f,
                    state.Input.ToxinContamination / 10000f)) return false;
            state.Input.LiquidMilliliters -= amount;
            if (state.Input.LiquidMilliliters == 0) state.Input.LiquidKind = LiquidKind.None;
            transferredMilliliters = amount;
            Touch(state);
            return true;
        }

        public bool TryConsumeBasinWater(
            ulong objectId,
            ushort milliliters,
            out float biologicalLoad,
            out float toxinLoad)
        {
            biologicalLoad = 0f;
            toxinLoad = 0f;
            if (networkManager == null || !networkManager.IsServer || milliliters == 0
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.WashBasinItemId
                || state.Input.LiquidKind != LiquidKind.Water
                || state.Input.LiquidMilliliters < milliliters) return false;
            biologicalLoad = state.Input.BiologicalContamination / 10000f;
            toxinLoad = state.Input.ToxinContamination / 10000f;
            state.Input.LiquidMilliliters -= milliliters;
            state.Input.BiologicalContamination = (ushort)Mathf.Max(
                state.Input.BiologicalContamination, 800);
            state.Input.Cleanliness = (ushort)Mathf.Max(0, state.Input.Cleanliness - 250);
            if (state.Input.LiquidMilliliters == 0) state.Input.LiquidKind = LiquidKind.None;
            Touch(state);
            return true;
        }

        public bool TrySampleWellWater(
            ulong objectId,
            float rainIntensity,
            out float biologicalLoad,
            out float toxinLoad)
        {
            biologicalLoad = 0f;
            toxinLoad = 0f;
            if (!objects.TryGetValue(objectId, out var state)
                || state.ItemId != SurvivalStructureRules.WellItemId) return false;
            ResourceBalance.SampleSpringWater(
                objectId ^ 0x7F4A7C15UL, out biologicalLoad, out toxinLoad);
            ApplyWasteContamination(
                state.Position, rainIntensity, ref biologicalLoad, ref toxinLoad);
            return true;
        }

        public bool TryDepositWaste(
            ulong objectId,
            ushort milliliters,
            float biologicalLoad,
            float toxinLoad)
        {
            if (networkManager == null || !networkManager.IsServer || milliliters == 0
                || !float.IsFinite(biologicalLoad) || !float.IsFinite(toxinLoad)
                || !TryGetWastePitSpace(objectId, out _, out var available)
                || milliliters > available)
            {
                return false;
            }
            var state = objects[objectId];
            var oldVolume = state.Input.LiquidKind == LiquidKind.Waste
                ? state.Input.LiquidMilliliters
                : 0;
            var newVolume = oldVolume + milliliters;
            var mixedBiological = (state.Input.BiologicalContamination / 10000f
                    * oldVolume + Mathf.Clamp01(biologicalLoad) * milliliters)
                / newVolume;
            var mixedToxins = (state.Input.ToxinContamination / 10000f
                    * oldVolume + Mathf.Clamp01(toxinLoad) * milliliters)
                / newVolume;
            state.Input = new ItemStackState(
                40,
                1,
                freshness: 10000,
                biologicalContamination: (ushort)Mathf.RoundToInt(
                    mixedBiological * 10000f),
                toxinContamination: (ushort)Mathf.RoundToInt(mixedToxins * 10000f),
                cleanliness: 0,
                liquidMilliliters: (ushort)newVolume,
                liquidKind: LiquidKind.Waste);
            Touch(state);
            return true;
        }

        public void ApplyWasteContamination(
            Vector3 waterPosition,
            float rainIntensity,
            ref float biologicalLoad,
            ref float toxinLoad)
        {
            foreach (var state in objects.Values)
            {
                if (!SurvivalStructureRules.IsWastePit(state.ItemId)
                    || state.Input.LiquidKind != LiquidKind.Waste
                    || state.Input.LiquidMilliliters == 0)
                {
                    continue;
                }
                var leakage = SurvivalStructureRules.CalculatePitLeakage(
                    state.Position,
                    waterPosition,
                    state.Input.LiquidMilliliters / 1000f,
                    state.Input.BiologicalContamination / 10000f,
                    state.Input.ToxinContamination / 10000f,
                    rainIntensity);
                var lining = SurvivalStructureRules.WasteLeakageMultiplier(state.ItemId);
                leakage = (leakage.Biological * lining, leakage.Toxins * lining);
                biologicalLoad = Mathf.Clamp01(biologicalLoad + leakage.Biological);
                toxinLoad = Mathf.Clamp01(toxinLoad + leakage.Toxins);
            }
        }

        public SurvivalEnvironment ApplyEnvironmentalInfluence(
            Vector3 position,
            SurvivalEnvironment environment)
        {
            var rainProtection = 0f;
            var windProtection = 0f;
            var externalHeat = environment.ExternalHeat;
            var smokeConcentration = environment.SmokeConcentration;
            var shelterParts = new List<ShelterPartState>();
            foreach (var state in objects.Values)
            {
                if (state.ItemId == SurvivalStructureRules.LeanToItemId)
                {
                    var local = Quaternion.Euler(0f, -state.Yaw, 0f)
                        * (position - state.Position);
                    if (Mathf.Abs(local.x) <= 1.95f && Mathf.Abs(local.z) <= 1.42f
                        && local.y >= -0.4f && local.y <= 2.8f)
                    {
                        rainProtection = Mathf.Max(rainProtection, 0.82f);
                        windProtection = Mathf.Max(windProtection, 0.48f);
                    }
                }
                if (SurvivalStructureRules.IsModularBuildingPart(state.ItemId))
                    shelterParts.Add(new ShelterPartState(
                        state.ItemId, state.Position, state.Yaw, state.Locked));
            }
            var shelter = SurvivalStructureRules.CalculateShelterCoverage(
                position, shelterParts);
            rainProtection = Mathf.Max(rainProtection, shelter.RainProtection);
            windProtection = Mathf.Max(windProtection, shelter.WindProtection);
            // Resolve shelter first so smoke does not depend on placement/load order.
            foreach (var state in objects.Values)
            {
                if (state.ItemId == SurvivalStructureRules.HearthItemId
                    && !state.Input.IsEmpty)
                {
                    var distance = Vector3.Distance(position, state.Position);
                    if (distance <= 6f)
                    {
                        var proximity = 1f - distance / 6f;
                        externalHeat = Mathf.Max(
                            externalHeat,
                            1.15f * proximity);
                        smokeConcentration = Mathf.Max(
                            smokeConcentration,
                            proximity * (shelter.HasRoof
                                ? Mathf.Lerp(0.045f, 0.355f, shelter.SmokeRetention)
                                : rainProtection > 0f ? 0.13f : 0.045f)
                                * Mathf.Lerp(1f, 0.45f,
                                    Mathf.InverseLerp(
                                        0f, 8f, environment.WindMetersPerSecond)));
                    }
                }
            }
            return new SurvivalEnvironment(
                environment.AmbientTemperatureC,
                environment.WindMetersPerSecond * (1f - windProtection),
                environment.Humidity,
                environment.Precipitation * (1f - rainProtection),
                environment.Insulation,
                externalHeat,
                environment.Sheltered || shelter.Enclosed || rainProtection >= 0.95f,
                smokeConcentration,
                environment.DayFraction);
        }

        public bool TryDismantle(ulong objectId, out Vector3 position)
        {
            position = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != ResourceBalance.ResearchTableItemId
                || state.IsBusy || !state.Input.IsEmpty)
            {
                return false;
            }
            position = state.Position;
            objects.Remove(objectId);
            if (views.Remove(objectId, out var view) && view != null) Destroy(view.gameObject);
            if (structureViews.Remove(objectId, out var structureView)
                && structureView != null) Destroy(structureView.gameObject);
            MarkDirty(objectId, fullSave: true);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryInsertInput(ulong objectId, ItemStackState sample)
        {
            if (networkManager == null || !networkManager.IsServer
                || sample.ItemId != ResourceBalance.UnknownSampleItemId
                || sample.Quantity != 1 || sample.HiddenItemId == 0
                || sample.SourceNodeId == 0 || sample.SampleId == 0
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != ResourceBalance.ResearchTableItemId
                || state.IsBusy || !state.Input.IsEmpty)
            {
                return false;
            }
            state.Input = sample;
            Touch(state);
            return true;
        }

        public bool TryTakeInput(ulong objectId, out ItemStackState sample)
        {
            sample = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != ResourceBalance.ResearchTableItemId
                || state.IsBusy || state.Input.IsEmpty)
            {
                return false;
            }
            sample = state.Input;
            state.Input = default;
            Touch(state);
            return true;
        }

        public bool TryBeginResearch(
            ulong objectId,
            ulong clientId,
            out ItemStackState sample,
            out Vector3 position)
        {
            sample = default;
            position = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != ResourceBalance.ResearchTableItemId
                || state.IsBusy || state.Input.IsEmpty
                || state.Input.ItemId != ResourceBalance.UnknownSampleItemId)
            {
                return false;
            }
            state.BusyClientId = clientId;
            sample = state.Input;
            position = state.Position;
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryCompleteResearch(
            ulong objectId,
            ulong clientId,
            ulong expectedSampleId,
            out ItemStackState consumed)
        {
            consumed = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.ItemId != ResourceBalance.ResearchTableItemId
                || state.BusyClientId != clientId || state.Input.IsEmpty
                || state.Input.SampleId != expectedSampleId)
            {
                return false;
            }
            consumed = state.Input;
            state.Input = default;
            state.BusyClientId = ulong.MaxValue;
            Touch(state);
            return true;
        }

        public void CancelResearch(ulong objectId, ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.BusyClientId != clientId)
            {
                return;
            }
            state.BusyClientId = ulong.MaxValue;
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
        }

        public void SendSnapshot(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer) return;
            SendSnapshotTo(new[] { clientId });
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (repository == null) return;
            await flushGate.WaitAsync(cancellationToken);
            try
            {
                if (!fullSaveRequired && dirtyObjects.Count == 0) return;
                flushRunning = true;
                var savedRevision = persistenceRevision;
                var snapshot = CreateStoredSnapshot();
                await repository.SavePlacedObjectsAsync(
                    definition.WorldId, snapshot, cancellationToken);
                if (persistenceRevision == savedRevision)
                {
                    dirtyObjects.Clear();
                    fullSaveRequired = false;
                }
            }
            finally
            {
                flushRunning = false;
                nextFlushAt = Time.unscaledTime + 5f;
                flushGate.Release();
            }
        }

        private void Update()
        {
            if (networkManager == null || !networkManager.IsServer) return;
            TickHearthFuel();
            if (flushRunning
                || (!fullSaveRequired && dirtyObjects.Count == 0)
                || Time.unscaledTime < nextFlushAt)
            {
                return;
            }
            _ = FlushWithLoggingAsync();
        }

        private void TickHearthFuel()
        {
            if (Time.unscaledTime < nextFuelTickAt) return;
            var now = Time.unscaledTime;
            var elapsed = Mathf.Max(0f, now - lastFuelTickAt);
            lastFuelTickAt = now;
            nextFuelTickAt = now + 5f;
            foreach (var state in objects.Values)
            {
                if (state.ItemId != SurvivalStructureRules.HearthItemId
                    || state.Input.IsEmpty) continue;
                var burned = SurvivalStructureRules.BurnFuel(state.Input, elapsed);
                if (burned.Equals(state.Input)) continue;
                state.Input = burned;
                Touch(state);
            }
        }

        private async Task FlushWithLoggingAsync()
        {
            try
            {
                await FlushAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save placed objects: {exception.Message}");
            }
        }

        private void Touch(RuntimeObject state)
        {
            state.UpdatedAtUtc = DateTime.UtcNow;
            MarkDirty(state.ObjectId, fullSave: false);
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
        }

        private void MarkDirty(ulong objectId, bool fullSave)
        {
            dirtyObjects.Add(objectId);
            fullSaveRequired |= fullSave;
            persistenceRevision++;
            if (nextFlushAt <= Time.unscaledTime)
            {
                nextFlushAt = Time.unscaledTime + 5f;
            }
        }

        private List<StoredPlacedObject> CreateStoredSnapshot()
        {
            var result = new List<StoredPlacedObject>(objects.Count);
            foreach (var state in objects.Values)
            {
                result.Add(new StoredPlacedObject
                {
                    WorldId = definition.WorldId,
                    ObjectId = state.ObjectId.ToString(),
                    OwnerAccountId = state.OwnerAccountId,
                    AssignedCharacterId = state.AssignedCharacterId,
                    ItemId = state.ItemId,
                    X = state.Position.x,
                    Y = state.Position.y,
                    Z = state.Position.z,
                    Yaw = state.Yaw,
                    Locked = state.Locked,
                    Input = ToStoredStack(state.Input),
                    CreatedAtUtc = state.CreatedAtUtc.ToString("O"),
                    UpdatedAtUtc = state.UpdatedAtUtc.ToString("O"),
                });
            }
            return result;
        }

        private void BroadcastSnapshot()
        {
            if (networkManager == null || !networkManager.IsServer) return;
            SendSnapshotTo(null);
        }

        private void SendSnapshotTo(IReadOnlyList<ulong> targets)
        {
            EnsureMessagingRegistered();
            if (!messagingRegistered || networkManager?.CustomMessagingManager == null) return;
            var writer = new FastBufferWriter(
                Mathf.Max(128, 4 + objects.Count * 72), Allocator.Temp, 131072);
            try
            {
                writer.WriteValueSafe(objects.Count);
                foreach (var state in objects.Values)
                {
                    writer.WriteValueSafe(state.ObjectId);
                    writer.WriteValueSafe(state.ItemId);
                    writer.WriteValueSafe(state.Position);
                    writer.WriteValueSafe(state.Yaw);
                    writer.WriteValueSafe(state.IsBusy);
                    writer.WriteValueSafe(state.Locked);
                    writer.WriteValueSafe(new FixedString64Bytes(state.AssignedCharacterId));
                    WriteSafeStack(ref writer, state.Input.ForReplication());
                }
                if (targets == null)
                {
                    networkManager.CustomMessagingManager.SendNamedMessageToAll(
                        SnapshotMessage, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    return;
                }
                foreach (var target in targets)
                {
                    networkManager.CustomMessagingManager.SendNamedMessage(
                        SnapshotMessage, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void OnSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsClient || networkManager.IsServer) return;
            reader.ReadValueSafe(out int count);
            var received = new HashSet<ulong>();
            for (var index = 0; index < count; index++)
            {
                reader.ReadValueSafe(out ulong objectId);
                reader.ReadValueSafe(out ushort itemId);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out float yaw);
                reader.ReadValueSafe(out bool busy);
                reader.ReadValueSafe(out bool locked);
                reader.ReadValueSafe(out FixedString64Bytes assignedCharacterId);
                var input = ReadSafeStack(ref reader);
                var state = new RuntimeObject
                {
                    ObjectId = objectId,
                    ItemId = itemId,
                    Position = position,
                    Yaw = yaw,
                    Locked = locked,
                    AssignedCharacterId = assignedCharacterId.ToString(),
                    Input = input,
                    BusyClientId = busy ? 0UL : ulong.MaxValue,
                };
                objects[objectId] = state;
                received.Add(objectId);
                CreateOrUpdateView(state, renderVisuals: true);
            }
            var removed = new List<ulong>();
            foreach (var id in objects.Keys)
            {
                if (!received.Contains(id)) removed.Add(id);
            }
            foreach (var id in removed)
            {
                objects.Remove(id);
                if (views.Remove(id, out var view) && view != null) Destroy(view.gameObject);
                if (structureViews.Remove(id, out var structureView)
                    && structureView != null) Destroy(structureView.gameObject);
            }
            Changed?.Invoke();
        }

        private void EnsureMessagingRegistered()
        {
            if (messagingRegistered || networkManager?.CustomMessagingManager == null) return;
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                SnapshotMessage, OnSnapshotMessage);
            messagingRegistered = true;
        }

        private void CreateOrUpdateView(RuntimeObject state, bool renderVisuals)
        {
            if (state.ItemId != ResourceBalance.ResearchTableItemId)
            {
                if (!structureViews.TryGetValue(state.ObjectId, out var structure)
                    || structure == null)
                {
                    viewRoot ??= new GameObject("PlacedObjects").transform;
                    viewRoot.SetParent(transform, false);
                    structure = SurvivalStructureView.Create(
                        state.ObjectId,
                        state.ItemId,
                        state.Position,
                        state.Yaw,
                        viewRoot,
                        renderVisuals);
                    structureViews[state.ObjectId] = structure;
                }
                else
                {
                    structure.transform.SetPositionAndRotation(
                        state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
                }
                structure.ApplyState(state.Input.ForReplication(), state.Locked);
                return;
            }
            if (!views.TryGetValue(state.ObjectId, out var view) || view == null)
            {
                viewRoot ??= new GameObject("PlacedObjects").transform;
                viewRoot.SetParent(transform, false);
                view = ResearchTableView.Create(
                    state.ObjectId, state.Position, state.Yaw, viewRoot, renderVisuals);
                views[state.ObjectId] = view;
            }
            else
            {
                view.transform.SetPositionAndRotation(
                    state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            }
            view.ApplyState(state.Input.ForReplication(), state.IsBusy);
        }

        private void ApplyViewState(RuntimeObject state)
        {
            if (structureViews.TryGetValue(state.ObjectId, out var structure)
                && structure != null)
            {
                structure.ApplyState(state.Input.ForReplication(), state.Locked);
                return;
            }
            if (views.TryGetValue(state.ObjectId, out var view) && view != null)
            {
                view.ApplyState(state.Input.ForReplication(), state.IsBusy);
            }
        }

        private void RebuildViews(bool renderVisuals)
        {
            foreach (var view in views.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            views.Clear();
            foreach (var view in structureViews.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            structureViews.Clear();
            foreach (var state in objects.Values) CreateOrUpdateView(state, renderVisuals);
        }

        private void ClearObjects()
        {
            objects.Clear();
            dirtyObjects.Clear();
            fullSaveRequired = false;
            persistenceRevision = 0;
            foreach (var view in views.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            views.Clear();
            foreach (var view in structureViews.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            structureViews.Clear();
        }

        private ulong CreateObjectId(Vector3 position)
        {
            unchecked
            {
                ulong value;
                do
                {
                    objectNonce++;
                    value = (ulong)definition.Seed ^ ((ulong)(uint)definition.WorldId << 32);
                    value ^= (uint)Mathf.RoundToInt(position.x * 100f) * 0x9E3779B9UL;
                    value ^= (uint)Mathf.RoundToInt(position.z * 100f) * 0x85EBCA6BUL;
                    value ^= objectNonce * 0xC2B2AE35UL;
                    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                    value ^= value >> 31;
                } while (value == 0 || objects.ContainsKey(value));
                return value;
            }
        }

        private static void WriteSafeStack(ref FastBufferWriter writer, ItemStackState stack)
        {
            writer.WriteValueSafe(stack.ItemId);
            writer.WriteValueSafe(stack.Quantity);
            writer.WriteValueSafe(stack.SourceNodeId);
            writer.WriteValueSafe(stack.RevealAtPercent);
            writer.WriteValueSafe(stack.SampleId);
            writer.WriteValueSafe(stack.ItemInstanceId);
            writer.WriteValueSafe(stack.Freshness);
            writer.WriteValueSafe(stack.BiologicalContamination);
            writer.WriteValueSafe(stack.ToxinContamination);
            writer.WriteValueSafe(stack.Wetness);
            writer.WriteValueSafe(stack.Cleanliness);
            writer.WriteValueSafe(stack.LiquidMilliliters);
            writer.WriteValueSafe(stack.LiquidKind);
            writer.WriteValueSafe(stack.Equipped);
        }

        private static ItemStackState ReadSafeStack(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort itemId);
            reader.ReadValueSafe(out ushort quantity);
            reader.ReadValueSafe(out ulong sourceNodeId);
            reader.ReadValueSafe(out byte revealAtPercent);
            reader.ReadValueSafe(out ulong sampleId);
            reader.ReadValueSafe(out ulong itemInstanceId);
            reader.ReadValueSafe(out ushort freshness);
            reader.ReadValueSafe(out ushort biologicalContamination);
            reader.ReadValueSafe(out ushort toxinContamination);
            reader.ReadValueSafe(out ushort wetness);
            reader.ReadValueSafe(out ushort cleanliness);
            reader.ReadValueSafe(out ushort liquidMilliliters);
            reader.ReadValueSafe(out LiquidKind liquidKind);
            reader.ReadValueSafe(out bool equipped);
            return new ItemStackState(itemId, quantity, sourceNodeId: sourceNodeId,
                revealAtPercent: revealAtPercent, sampleId: sampleId,
                itemInstanceId: itemInstanceId, freshness: freshness,
                biologicalContamination: biologicalContamination,
                toxinContamination: toxinContamination, wetness: wetness,
                cleanliness: cleanliness, liquidMilliliters: liquidMilliliters,
                liquidKind: liquidKind, equipped: equipped);
        }

        private static StoredInventorySlot ToStoredStack(ItemStackState stack) => stack.IsEmpty
            ? null
            : new StoredInventorySlot
            {
                ItemId = stack.ItemId,
                Quantity = stack.Quantity,
                Condition = stack.Condition,
                Quality = (byte)stack.Quality,
                HiddenItemId = stack.HiddenItemId,
                SourceNodeId = stack.SourceNodeId.ToString(),
                RevealAtPercent = stack.RevealAtPercent,
                SampleId = stack.SampleId.ToString(),
                ItemInstanceId = stack.ItemInstanceId.ToString(),
                Freshness = stack.Freshness,
                BiologicalContamination = stack.BiologicalContamination,
                ToxinContamination = stack.ToxinContamination,
                Wetness = stack.Wetness,
                Cleanliness = stack.Cleanliness,
                LiquidMilliliters = stack.LiquidMilliliters,
                LiquidKind = (byte)stack.LiquidKind,
                Equipped = stack.Equipped,
            };

        private static ItemStackState FromStoredStack(StoredInventorySlot stack)
        {
            if (stack == null || stack.ItemId == 0 || stack.Quantity == 0) return default;
            ulong.TryParse(stack.SourceNodeId, out var sourceNodeId);
            ulong.TryParse(stack.SampleId, out var sampleId);
            ulong.TryParse(stack.ItemInstanceId, out var itemInstanceId);
            return new ItemStackState(
                stack.ItemId,
                stack.Quantity,
                stack.Condition,
                (ResourceQuality)stack.Quality,
                stack.HiddenItemId,
                sourceNodeId,
                stack.RevealAtPercent,
                sampleId,
                itemInstanceId,
                stack.Freshness,
                stack.BiologicalContamination,
                stack.ToxinContamination,
                stack.Wetness,
                stack.Cleanliness,
                stack.LiquidMilliliters,
                (LiquidKind)stack.LiquidKind,
                stack.Equipped);
        }

        private static DateTime ParseDate(string value) => DateTime.TryParse(
            value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.UtcNow;

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);

        private void OnDestroy()
        {
            if (messagingRegistered && networkManager?.CustomMessagingManager != null)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
            }
            ClearObjects();
        }
    }
}
