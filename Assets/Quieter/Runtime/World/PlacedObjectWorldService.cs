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
            public ushort ItemId;
            public Vector3 Position;
            public float Yaw;
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
                    ItemId = entry.ItemId,
                    Position = new Vector3(entry.X, entry.Y, entry.Z),
                    Yaw = entry.Yaw,
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

        public bool IsResearchTable(ulong objectId) => objects.TryGetValue(objectId, out var state)
            && state.ItemId == ResourceBalance.ResearchTableItemId;

        public bool TryPlace(
            Vector3 position,
            float yaw,
            ushort itemId,
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

        public bool TryPlace(Vector3 position, float yaw, out ulong objectId) => TryPlace(
            position, yaw, ResourceBalance.ResearchTableItemId, out objectId);

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
                || state.ItemId != SurvivalStructureRules.UnlinedWastePitItemId)
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
                if (state.ItemId != SurvivalStructureRules.UnlinedWastePitItemId
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
            }
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
                            proximity * (rainProtection > 0f ? 0.13f : 0.045f)
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
                environment.Sheltered || rainProtection >= 0.95f,
                smokeConcentration);
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
                    ItemId = state.ItemId,
                    X = state.Position.x,
                    Y = state.Position.y,
                    Z = state.Position.z,
                    Yaw = state.Yaw,
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
                var input = ReadSafeStack(ref reader);
                var state = new RuntimeObject
                {
                    ObjectId = objectId,
                    ItemId = itemId,
                    Position = position,
                    Yaw = yaw,
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
                structure.ApplyState(state.Input.ForReplication());
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
                structure.ApplyState(state.Input.ForReplication());
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
