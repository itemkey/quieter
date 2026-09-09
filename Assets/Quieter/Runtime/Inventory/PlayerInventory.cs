using System;
using System.Collections.Generic;
using System.Collections;
using Quieter.Player;
using Quieter.Core;
using Quieter.UI;
using Quieter.World;
using Quieter.Survival;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quieter.Inventory
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class PlayerInventory : NetworkBehaviour
    {
        private const float PerishableTickSeconds = 5f;

        private readonly NetworkList<ItemStackState> inventory = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<ItemStackState> workbench = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemStackState> cursor = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> selectedHotbar = new(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ushort> activeItemId = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly Dictionary<uint, Action<bool>> pendingMoveCallbacks = new();
        private readonly NetworkVariable<double> craftCompletesAt = new(
            0d, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly List<ItemStackState> serverPendingItems = new();

        private ItemCatalog catalog;
        private GameObject worldItemPrefab;
        private InventoryModel model;
        private NetworkPlayer player;
        private GameObject heldVisual;
        private NetworkWorldItem focusedPickup;
        private PlayerInventory focusedCorpseInventory;
        private PlayerResourceInteraction resourceInteraction;
        private PlayerSurvival playerSurvival;
        private WorldWeatherService weatherService;
        private bool interfaceOpen;
        private uint nextMoveRequestId;
        private Coroutine toolSwingRoutine;
        private float lastPerishableTickAt;
        private float nextPerishableTickAt;
        private bool CanUseInventory => IsServer && model != null
            && !serverPersistenceLocked
            && playerSurvival != null && playerSurvival.CanPerformServerAction();
        private bool serverPersistenceLocked;
        private CraftingRecipe serverCraftRecipe;
        private Vector3 serverCraftPosition;
        private ulong serverCraftHearthId;
        private PlacedObjectWorldService placedObjects;

        public bool IsCrafting => craftCompletesAt.Value > 0d;
        public float CraftSecondsRemaining => !IsCrafting || NetworkManager == null ? 0f
            : Mathf.Max(0f, (float)(craftCompletesAt.Value - NetworkManager.ServerTime.Time));

        public event Action Changed;
        public event Action ServerInventoryChanged;

        public ItemStackState CursorStack => cursor.Value;
        public byte SelectedHotbarIndex => selectedHotbar.Value;
        public ushort ActiveItemId => activeItemId.Value;
        public ItemCatalog Catalog => catalog;
        public bool IsInterfaceOpen => interfaceOpen;
        public NetworkWorldItem FocusedPickup => focusedPickup;
        public bool HasFocusedCorpse => focusedCorpseInventory != null;
        public bool FocusedBodyIsDead => focusedCorpseInventory != null
            && focusedCorpseInventory.GetComponent<PlayerSurvival>()?.PublicSymptoms.LifeState
                == CharacterLifeState.Dead;
        public PublicSymptomState FocusedBodySymptoms => focusedCorpseInventory == null
            ? default
            : focusedCorpseInventory.GetComponent<PlayerSurvival>()?.PublicSymptoms ?? default;
        public CorpseDecayStage FocusedCorpseStage => focusedCorpseInventory == null
            ? CorpseDecayStage.Fresh
            : focusedCorpseInventory.GetComponent<PlayerSurvival>()?.PublicSymptoms.CorpseStage
                ?? CorpseDecayStage.Fresh;
        public bool FocusedCorpseItemsSealed => focusedCorpseInventory != null
            && focusedCorpseInventory.GetComponent<PlayerSurvival>()?.PublicSymptoms.CorpseStage
                == CorpseDecayStage.Buried;
        public bool ServerPersistenceLocked => serverPersistenceLocked;
        public ItemStackState ServerActiveStack => model?.ActiveStack ?? default;

        public ItemStackState GetReplicatedSlot(InventorySlotReference reference)
        {
            if (reference.Area == InventorySlotArea.Inventory)
            {
                return reference.Index < inventory.Count ? inventory[reference.Index] : default;
            }

            if (reference.Area == InventorySlotArea.Workbench)
            {
                return reference.Index < workbench.Count ? workbench[reference.Index] : default;
            }

            if (reference.Area == InventorySlotArea.ResearchTable)
            {
                return resourceInteraction?.CurrentResearchTableInput ?? default;
            }

            return default;
        }

        public bool ContainsReplicatedItem(ushort itemId)
        {
            if (itemId == 0) return false;
            for (var index = 0; index < inventory.Count; index++)
            {
                var stack = inventory[index];
                if (!stack.IsEmpty && stack.ItemId == itemId) return true;
            }
            return false;
        }

        public bool TryGetReplicatedItemInstance(ushort itemId, out ulong instanceId)
        {
            instanceId = 0;
            if (itemId == 0) return false;

            var activeIndex = InventoryLayout.FirstHotbarSlot + selectedHotbar.Value;
            if (activeIndex >= 0 && activeIndex < inventory.Count)
            {
                var active = inventory[activeIndex];
                if (!active.IsEmpty && active.ItemId == itemId && active.ItemInstanceId != 0)
                {
                    instanceId = active.ItemInstanceId;
                    return true;
                }
            }

            for (var index = 0; index < inventory.Count; index++)
            {
                var stack = inventory[index];
                if (stack.IsEmpty || stack.ItemId != itemId || stack.ItemInstanceId == 0) continue;
                instanceId = stack.ItemInstanceId;
                return true;
            }
            return false;
        }

        public bool ContainsReplicatedItemInstance(ushort itemId, ulong instanceId)
        {
            if (itemId == 0 || instanceId == 0) return false;
            for (var index = 0; index < inventory.Count; index++)
            {
                var stack = inventory[index];
                if (!stack.IsEmpty && stack.ItemId == itemId
                    && stack.ItemInstanceId == instanceId)
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsServerItemInstance(ushort itemId, ulong instanceId)
        {
            if (!IsServer || model == null || itemId == 0 || instanceId == 0) return false;
            foreach (var stack in model.Inventory)
            {
                if (!stack.IsEmpty && stack.ItemId == itemId
                    && stack.ItemInstanceId == instanceId)
                {
                    return true;
                }
            }
            return false;
        }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            resourceInteraction = GetComponent<PlayerResourceInteraction>();
            playerSurvival = GetComponent<PlayerSurvival>();
            catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            worldItemPrefab = Resources.Load<GameObject>("Quieter/NetworkWorldItem");
        }

        public override void OnNetworkSpawn()
        {
            if (catalog == null)
            {
                catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            }

            inventory.OnListChanged += OnInventoryListChanged;
            workbench.OnListChanged += OnWorkbenchListChanged;
            cursor.OnValueChanged += OnCursorChanged;
            selectedHotbar.OnValueChanged += OnSelectedHotbarChanged;
            activeItemId.OnValueChanged += OnActiveItemChanged;
            if (IsServer)
            {
                lastPerishableTickAt = Time.unscaledTime;
                nextPerishableTickAt = lastPerishableTickAt + PerishableTickSeconds;
            }
            RebuildHeldVisual(activeItemId.Value);
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            inventory.OnListChanged -= OnInventoryListChanged;
            workbench.OnListChanged -= OnWorkbenchListChanged;
            cursor.OnValueChanged -= OnCursorChanged;
            selectedHotbar.OnValueChanged -= OnSelectedHotbarChanged;
            activeItemId.OnValueChanged -= OnActiveItemChanged;
            var callbacks = new List<Action<bool>>(pendingMoveCallbacks.Values);
            pendingMoveCallbacks.Clear();
            foreach (var callback in callbacks) callback?.Invoke(false);
            if (IsOwner) SetInterfaceOpen(false);
            if (heldVisual != null) Destroy(heldVisual);
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                TickServerPerishables();
                TickServerCrafting();
            }
            if (!IsOwner) return;
            UpdateFocusedPickup();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (ResourceMapView.IsOpen || ResourceMapView.IsDepositOpen
                || SurvivalView.IsBodyOpen || SurvivalView.IsProgressionOpen
                || SurvivalView.IsWorkerBookOpen
                || SurvivalView.IsCreationOpen || SurvivalView.IsDeathOpen) return;

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                SetInterfaceOpen(!interfaceOpen || InventoryView.WorkbenchMode, false);
                return;
            }

            if (keyboard.qKey.wasPressedThisFrame)
            {
                SetInterfaceOpen(!interfaceOpen || !InventoryView.WorkbenchMode, true);
                return;
            }

            if (interfaceOpen) return;

            for (var index = 0; index < InventoryLayout.HotbarSlotCount; index++)
            {
                if (Keyboard.current[(Key)((int)Key.Digit1 + index)].wasPressedThisFrame)
                {
                    SelectHotbarServerRpc((byte)index);
                    break;
                }
            }

            var wheel = resourceInteraction != null && resourceInteraction.IsPlacementMode
                ? 0f
                : Mouse.current?.scroll.ReadValue().y ?? 0f;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                var direction = wheel > 0f ? -1 : 1;
                var next = (selectedHotbar.Value + direction + InventoryLayout.HotbarSlotCount)
                    % InventoryLayout.HotbarSlotCount;
                SelectHotbarServerRpc((byte)next);
            }

            if (keyboard.eKey.wasPressedThisFrame && focusedPickup != null)
            {
                PickupServerRpc(new NetworkObjectReference(focusedPickup.NetworkObject));
            }
            else if (keyboard.eKey.wasPressedThisFrame && focusedCorpseInventory != null)
            {
                LootCorpseServerRpc(
                    new NetworkObjectReference(focusedCorpseInventory.NetworkObject));
            }

            if (keyboard.bKey.wasPressedThisFrame && focusedCorpseInventory != null
                && FocusedBodyIsDead)
            {
                BuryCorpseServerRpc(
                    new NetworkObjectReference(focusedCorpseInventory.NetworkObject));
            }
            if (keyboard.cKey.wasPressedThisFrame && focusedCorpseInventory != null
                && FocusedBodyIsDead)
            {
                CremateCorpseServerRpc(
                    new NetworkObjectReference(focusedCorpseInventory.NetworkObject));
            }

            if (keyboard.fKey.wasPressedThisFrame)
            {
                UseActiveItemServerRpc();
            }
        }

        private void TickServerPerishables()
        {
            if (model == null || catalog == null || Time.unscaledTime < nextPerishableTickAt)
                return;
            var now = Time.unscaledTime;
            var elapsed = Mathf.Max(0f, now - lastPerishableTickAt);
            lastPerishableTickAt = now;
            nextPerishableTickAt = now + PerishableTickSeconds;
            weatherService ??= FindAnyObjectByType<WorldWeatherService>();
            var environment = weatherService != null
                ? weatherService.GetEnvironment(transform.position)
                : SurvivalEnvironment.Temperate;
            var changed = model.SimulatePerishables(
                elapsed, environment.AmbientTemperatureC, environment.Humidity);
            for (var index = 0; index < serverPendingItems.Count; index++)
            {
                var previous = serverPendingItems[index];
                if (previous.IsEmpty || !catalog.TryGetItem(previous.ItemId, out var item))
                    continue;
                var current = FoodDecayRules.Advance(
                    previous,
                    item,
                    elapsed,
                    environment.AmbientTemperatureC,
                    environment.Humidity);
                if (current.Equals(previous)) continue;
                serverPendingItems[index] = current;
                changed = true;
            }
            if (!changed) return;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
        }

        public void InitializeServer(
            IEnumerable<StoredInventorySlot> storedSlots,
            IEnumerable<StoredInventorySlot> storedPendingItems,
            byte selectedIndex,
            ulong sampleOwnerSalt)
        {
            if (!IsServer || catalog == null) return;
            model = new InventoryModel(catalog);
            serverPendingItems.Clear();
            serverPendingItems.AddRange(model.Load(storedSlots, selectedIndex, sampleOwnerSalt));
            if (storedPendingItems != null)
            {
                var ordinal = 0;
                foreach (var stored in storedPendingItems)
                {
                    if (TryRestorePendingStack(stored, sampleOwnerSalt, ordinal++, out var stack))
                    {
                        serverPendingItems.Add(stack);
                    }
                }
            }
            SynchronizeAll();
        }

        public List<StoredInventorySlot> PrepareAndCreateStoredSlots()
        {
            if (!IsServer || model == null) return new List<StoredInventorySlot>();
            if (!model.NormalizeTemporaryStorage())
            {
                throw new InvalidOperationException(
                    "Temporary inventory could not be returned without losing items.");
            }
            SynchronizeAll();
            return model.CreateStoredSlots();
        }

        public List<StoredInventorySlot> CreateStoredPendingItemsSnapshot()
        {
            var result = new List<StoredInventorySlot>(serverPendingItems.Count);
            for (var index = 0; index < serverPendingItems.Count; index++)
            {
                var stack = serverPendingItems[index];
                if (stack.IsEmpty) continue;
                result.Add(new StoredInventorySlot
                {
                    SlotIndex = (byte)Math.Min(index, byte.MaxValue),
                    ItemId = stack.ItemId,
                    Quantity = stack.Quantity,
                    Condition = stack.Condition,
                    Quality = (byte)stack.Quality,
                    HiddenItemId = stack.HiddenItemId,
                    SourceNodeId = stack.SourceNodeId == 0 ? null : stack.SourceNodeId.ToString(),
                    RevealAtPercent = stack.RevealAtPercent,
                    SampleId = stack.SampleId == 0 ? null : stack.SampleId.ToString(),
                    ItemInstanceId = stack.ItemInstanceId == 0
                        ? null : stack.ItemInstanceId.ToString(),
                    Freshness = stack.Freshness,
                    BiologicalContamination = stack.BiologicalContamination,
                    ToxinContamination = stack.ToxinContamination,
                    Wetness = stack.Wetness,
                    Cleanliness = stack.Cleanliness,
                    LiquidMilliliters = stack.LiquidMilliliters,
                    LiquidKind = (byte)stack.LiquidKind,
                    Equipped = stack.Equipped,
                });
            }
            return result;
        }

        public List<StoredInventorySlot> CreateStoredSlotsSnapshot()
        {
            if (!IsServer || model == null) return new List<StoredInventorySlot>();
            if (!model.TryCreateNormalizedStoredSlots(out var slots))
            {
                throw new InvalidOperationException(
                    "Inventory snapshot could not include all temporary items.");
            }

            return slots;
        }

        public byte GetServerSelectedHotbarIndex() => model?.SelectedHotbarIndex ?? (byte)0;

        public void ServerSetPersistenceLocked(bool value)
        {
            if (IsServer) serverPersistenceLocked = value;
        }

        public void ServerRestorePersistenceSnapshot(
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedIndex,
            ulong sampleOwnerSalt)
        {
            if (!IsServer) return;
            InitializeServer(slots, pendingItems, selectedIndex, sampleOwnerSalt);
            ServerInventoryChanged?.Invoke();
        }

        public void ServerSendUseFeedback(string message)
        {
            if (IsServer) SendUseFeedbackClientRpc(message ?? string.Empty);
        }

        public float GetServerCarriedMassKg() => IsServer && model != null
            ? model.CalculateCarriedMassKg()
            : 0f;

        public float GetServerClothingInsulation()
        {
            if (!IsServer || model == null || catalog == null) return 0f;
            var insulation = 0f;
            foreach (var stack in model.Inventory)
            {
                if (stack.IsEmpty || !stack.Equipped
                    || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.Clothing)
                {
                    continue;
                }
                var dryFactor = Mathf.Lerp(1f, 0.2f, stack.Wetness / 10000f);
                insulation += item.Insulation * dryFactor;
            }
            return Mathf.Clamp01(insulation);
        }

        public float GetServerClothingRainProtection()
        {
            if (!IsServer || model == null || catalog == null) return 0f;
            var remainingPenetration = 1f;
            foreach (var stack in model.Inventory)
            {
                if (stack.IsEmpty || !stack.Equipped
                    || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.Clothing)
                {
                    continue;
                }
                var saturation = stack.Wetness / 10000f;
                var effectiveResistance = item.WaterResistance
                    * Mathf.Lerp(1f, 0.15f, saturation);
                remainingPenetration *= 1f - Mathf.Clamp01(effectiveResistance);
            }
            return Mathf.Clamp01(1f - remainingPenetration);
        }

        public float GetServerClothingHygieneBurden()
        {
            if (!IsServer || model == null || catalog == null) return 0f;
            var burden = 0f;
            foreach (var stack in model.Inventory)
            {
                if (stack.IsEmpty || !stack.Equipped
                    || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.Clothing) continue;
                var dirt = 1f - stack.Cleanliness / 10000f;
                var contamination = stack.BiologicalContamination / 10000f;
                burden = Mathf.Max(burden, Mathf.Max(dirt, contamination));
            }
            return Mathf.Clamp01(burden);
        }

        public void ServerUpdateEquippedClothingWetness(
            float seconds,
            float precipitation,
            float humidity,
            bool sheltered,
            float externalHeat,
            float exertion,
            float bodyCleanliness)
        {
            if (!IsServer || model == null || catalog == null || seconds <= 0f) return;
            var changed = false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || !stack.Equipped
                    || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.Clothing)
                {
                    continue;
                }
                var previous = stack.Wetness;
                var previousCleanliness = stack.Cleanliness;
                var previousContamination = stack.BiologicalContamination;
                var wet = previous / 10000f;
                if (!sheltered)
                {
                    wet += Mathf.Clamp01(precipitation)
                        * (1f - item.WaterResistance) * seconds / 240f;
                }
                wet -= (1f - Mathf.Clamp01(humidity))
                    * (0.08f + Mathf.Max(0f, externalHeat)) * seconds / 900f;
                stack.Wetness = (ushort)Mathf.RoundToInt(Mathf.Clamp01(wet) * 10000f);
                var soiling = seconds * (0.06f + Mathf.Clamp01(exertion) * 0.28f);
                stack.Cleanliness = (ushort)Mathf.Max(
                    0, stack.Cleanliness - Mathf.RoundToInt(soiling));
                var transferredBiological = (1f - Mathf.Clamp01(bodyCleanliness))
                    * seconds * 0.18f;
                stack.BiologicalContamination = (ushort)Mathf.Clamp(
                    stack.BiologicalContamination
                        + Mathf.RoundToInt(transferredBiological), 0, 10000);
                if (stack.Wetness == previous
                    && stack.Cleanliness == previousCleanliness
                    && stack.BiologicalContamination == previousContamination) continue;
                model.SetSlot(
                    new InventorySlotReference(InventorySlotArea.Inventory, index),
                    stack);
                changed = true;
            }
            if (!changed) return;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
        }

        public bool TryDamageActiveToolServer(ToolKind tool, int amount, out bool broke)
        {
            broke = false;
            if (!IsServer || model == null || !model.DamageActiveTool(tool, amount, out broke))
            {
                return false;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public ItemStackState GetServerSlot(InventorySlotReference reference) =>
            IsServer && model != null ? model.GetSlot(reference) : default;

        public bool TryRemoveStackServer(
            InventorySlotReference source,
            ushort expectedItemId,
            int quantity,
            out ItemStackState removed)
        {
            removed = default;
            if (!IsServer || model == null
                || !model.RemoveStack(source, expectedItemId, quantity, out removed))
            {
                return false;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryPlaceStackServer(InventorySlotReference destination, ItemStackState stack)
        {
            if (!IsServer || model == null || stack.IsEmpty
                || destination.Area != InventorySlotArea.Inventory
                || !model.GetSlot(destination).IsEmpty || !model.SetSlot(destination, stack))
            {
                return false;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryConsumeActiveItemServer(ushort expectedItemId)
        {
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            return TryRemoveStackServer(slot, expectedItemId, 1, out _);
        }

        public bool TryRemoveActiveItemServer(
            ushort expectedItemId, out ItemStackState removed)
        {
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            return TryRemoveStackServer(slot, expectedItemId, 1, out removed);
        }

        public bool TryFillActiveContainerServer(
            ushort requestedMilliliters,
            float biologicalContamination,
            float toxinContamination,
            out ushort filledMilliliters)
        {
            filledMilliliters = 0;
            if (!IsServer || model == null || catalog == null || requestedMilliliters == 0)
                return false;
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            var stack = model.GetSlot(slot);
            if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var item)
                || item.Kind != ItemKind.LiquidContainer
                || item.LiquidCapacityMilliliters == 0
                || stack.LiquidKind != LiquidKind.None
                    && stack.LiquidKind != LiquidKind.Water)
            {
                return false;
            }

            var available = item.LiquidCapacityMilliliters - stack.LiquidMilliliters;
            if (available <= 0) return false;
            filledMilliliters = (ushort)Mathf.Min(requestedMilliliters, available);
            var oldVolume = stack.LiquidMilliliters;
            var newVolume = oldVolume + filledMilliliters;
            var vesselContamination = 1f - stack.Cleanliness / 10000f;
            var incomingBiological = Mathf.Clamp01(
                biologicalContamination + vesselContamination * 0.35f);
            var mixedBiological = (stack.BiologicalContamination / 10000f * oldVolume
                + incomingBiological * filledMilliliters) / newVolume;
            var mixedToxins = (stack.ToxinContamination / 10000f * oldVolume
                + Mathf.Clamp01(toxinContamination) * filledMilliliters) / newVolume;
            // Empty vessels retain a film of the previous contents. Refilling a
            // contaminated pot must not silently erase its non-biological residue.
            if (oldVolume == 0)
            {
                var residueFraction = Mathf.Clamp01(25f / newVolume);
                mixedBiological = Mathf.Max(mixedBiological,
                    stack.BiologicalContamination / 10000f * residueFraction);
                mixedToxins = Mathf.Max(mixedToxins,
                    stack.ToxinContamination / 10000f * residueFraction);
            }
            stack.LiquidKind = LiquidKind.Water;
            stack.LiquidMilliliters = (ushort)newVolume;
            stack.BiologicalContamination = (ushort)Mathf.RoundToInt(
                Mathf.Clamp01(mixedBiological) * 10000f);
            stack.ToxinContamination = (ushort)Mathf.RoundToInt(
                Mathf.Clamp01(mixedToxins) * 10000f);
            if (!model.SetSlot(slot, stack)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryFillAnyContainerServer(
            ushort requestedMilliliters, float biologicalContamination,
            float toxinContamination, out ushort filledMilliliters)
        {
            filledMilliliters = 0;
            if (!IsServer || model == null || catalog == null || requestedMilliliters == 0)
                return false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var slot = new InventorySlotReference(InventorySlotArea.Inventory, index);
                var stack = model.Inventory[index];
                if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.LiquidContainer
                    || item.LiquidCapacityMilliliters == 0
                    || stack.LiquidKind != LiquidKind.None
                        && stack.LiquidKind != LiquidKind.Water) continue;
                var available = item.LiquidCapacityMilliliters - stack.LiquidMilliliters;
                if (available <= 0) continue;
                filledMilliliters = (ushort)Mathf.Min(requestedMilliliters, available);
                var oldVolume = stack.LiquidMilliliters;
                var newVolume = oldVolume + filledMilliliters;
                var incomingBiological = Mathf.Clamp01(biologicalContamination
                    + (1f - stack.Cleanliness / 10000f) * 0.35f);
                stack.LiquidKind = LiquidKind.Water;
                stack.LiquidMilliliters = (ushort)newVolume;
                stack.BiologicalContamination = (ushort)Mathf.RoundToInt(Mathf.Clamp01(
                    (stack.BiologicalContamination / 10000f * oldVolume
                        + incomingBiological * filledMilliliters) / newVolume) * 10000f);
                stack.ToxinContamination = (ushort)Mathf.RoundToInt(Mathf.Clamp01(
                    (stack.ToxinContamination / 10000f * oldVolume
                        + Mathf.Clamp01(toxinContamination) * filledMilliliters) / newVolume)
                    * 10000f);
                if (!model.SetSlot(slot, stack)) return false;
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool TryBoilAnyWaterServer()
        {
            if (!IsServer || model == null) return false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.LiquidKind != LiquidKind.Water
                    || stack.LiquidMilliliters == 0
                    || stack.BiologicalContamination == 0) continue;
                stack.BiologicalContamination = 0;
                if (!model.SetSlot(
                        new InventorySlotReference(InventorySlotArea.Inventory, index), stack))
                    return false;
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool HasServerWaste()
        {
            if (!IsServer || model == null) return false;
            foreach (var stack in model.Inventory)
                if (!stack.IsEmpty && stack.LiquidKind == LiquidKind.Waste
                    && stack.LiquidMilliliters > 0) return true;
            return false;
        }

        public bool TryDepositAnyWasteServer(
            PlacedObjectWorldService destination, ulong objectId,
            ushort requestedMilliliters, out ushort drainedMilliliters)
        {
            drainedMilliliters = 0;
            if (!IsServer || model == null || destination == null) return false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var slot = new InventorySlotReference(InventorySlotArea.Inventory, index);
                if (!model.TryDrainLiquid(slot, LiquidKind.Waste, requestedMilliliters,
                        (stack, amount) => destination.TryDepositWaste(objectId, amount,
                            stack.BiologicalContamination / 10000f,
                            stack.ToxinContamination / 10000f), out drainedMilliliters)) continue;
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool TryCollectWorldItemServer(NetworkWorldItem worldItem, out int collected)
        {
            collected = 0;
            if (!IsServer || model == null || worldItem == null
                || !worldItem.TryCollectServer(model, out collected)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryBoilActiveLiquidServer(
            ushort expectedItemId,
            ulong expectedItemInstanceId,
            LiquidKind resultKind = LiquidKind.Water,
            ushort ingredientItemId = 0)
        {
            if (!IsServer || model == null || expectedItemId != 30
                || expectedItemInstanceId == 0
                || resultKind is not (LiquidKind.Water
                    or LiquidKind.Broth or LiquidKind.HerbalInfusion)
                || ingredientItemId != 0 && !HasServerItem(ingredientItemId))
            {
                return false;
            }
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            var stack = model.GetSlot(slot);
            if (stack.IsEmpty || stack.ItemId != expectedItemId
                || stack.ItemInstanceId != expectedItemInstanceId
                || stack.LiquidKind != LiquidKind.Water
                || stack.LiquidMilliliters == 0)
            {
                return false;
            }
            var boiled = LivingWorldSimulation.BoilLiquid(
                stack.BiologicalContamination / 10000f,
                stack.ToxinContamination / 10000f,
                reachedBoil: true);
            stack.BiologicalContamination = (ushort)Mathf.RoundToInt(
                boiled.Biological * 10000f);
            stack.ToxinContamination = (ushort)Mathf.RoundToInt(
                boiled.Toxins * 10000f);
            if (ingredientItemId != 0 && !TryConsumeAnyItemServer(ingredientItemId))
                return false;
            stack.LiquidKind = resultKind;
            if (!model.SetSlot(slot, stack)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryCookActiveSolidFoodServer(
            ItemStackState expected,
            ushort cookedItemId)
        {
            if (!IsServer || model == null || catalog == null || expected.IsEmpty
                || !model.ActiveStack.Equals(expected)
                || !ResourceBalance.TryGetCookedFoodItemId(
                    expected.ItemId, out var expectedCookedItemId)
                || expectedCookedItemId != cookedItemId
                || !catalog.TryGetItem(cookedItemId, out var cookedDefinition)
                || cookedDefinition.Kind != ItemKind.Food)
            {
                return false;
            }
            var cooked = ResourceBalance.CookSolidFood(expected, cookedItemId);
            if (cooked.IsEmpty) return false;
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + model.SelectedHotbarIndex);
            if (!model.SetSlot(slot, cooked)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryDepositActiveWasteServer(
            PlacedObjectWorldService destination,
            ulong objectId,
            ushort requestedMilliliters,
            out ushort drainedMilliliters)
        {
            drainedMilliliters = 0;
            if (!IsServer || model == null || destination == null) return false;
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            if (!model.TryDrainLiquid(slot, LiquidKind.Waste, requestedMilliliters,
                    (stack, amount) => destination.TryDepositWaste(objectId, amount,
                        stack.BiologicalContamination / 10000f,
                        stack.ToxinContamination / 10000f), out drainedMilliliters))
                return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryDepositActiveWaterServer(
            PlacedObjectWorldService destination,
            ulong objectId,
            ushort requestedMilliliters,
            out ushort drainedMilliliters)
        {
            drainedMilliliters = 0;
            if (!IsServer || model == null || destination == null
                || requestedMilliliters == 0) return false;
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + GetServerSelectedHotbarIndex());
            if (!model.TryDrainLiquid(slot, LiquidKind.Water, requestedMilliliters,
                    (stack, amount) => destination.TryStoreWater(
                        objectId, amount,
                        stack.BiologicalContamination / 10000f,
                        stack.ToxinContamination / 10000f), out drainedMilliliters))
                return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryGetServerItemCleanliness(ushort itemId, out float cleanliness)
        {
            cleanliness = 0f;
            if (!IsServer || model == null || itemId == 0) return false;
            var found = false;
            foreach (var stack in model.Inventory)
            {
                if (stack.IsEmpty || stack.ItemId != itemId) continue;
                found = true;
                cleanliness = Mathf.Max(cleanliness, ItemHygieneRules.MedicalMaterialCleanliness(stack));
            }
            return found;
        }

        public bool TryRinseActiveItemServer(
            ItemStackState expected, float biologicalLoad, float toxinLoad)
        {
            if (!CanUseInventory || catalog == null || !model.ActiveStack.Equals(expected)
                || !catalog.TryGetItem(expected.ItemId, out var item)
                || !ItemHygieneRules.CanRinse(expected, item)) return false;
            var next = ItemHygieneRules.Rinse(expected, item, biologicalLoad, toxinLoad);
            if (next.Equals(expected)) return false;
            var slot = new InventorySlotReference(InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + model.SelectedHotbarIndex);
            if (!model.SetSlot(slot, next)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryConsumeAnyItemServer(ushort itemId)
        {
            if (!IsServer || model == null || itemId == 0) return false;
            var bestIndex = -1;
            var bestCleanliness = -1f;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.ItemId != itemId) continue;
                var cleanliness = ItemHygieneRules.MedicalMaterialCleanliness(stack);
                if (cleanliness <= bestCleanliness) continue;
                bestCleanliness = cleanliness;
                bestIndex = index;
            }
            return bestIndex >= 0 && TryRemoveStackServer(
                new InventorySlotReference(InventorySlotArea.Inventory, bestIndex), itemId, 1, out _);
        }

        public bool HasServerItem(ushort itemId)
        {
            if (!IsServer || model == null || itemId == 0) return false;
            foreach (var stack in model.Inventory)
            {
                if (!stack.IsEmpty && stack.ItemId == itemId) return true;
            }
            return false;
        }

        public int GetServerItemQuantity(ushort itemId)
        {
            if (!IsServer || model == null || itemId == 0) return 0;
            var total = 0;
            foreach (var stack in model.Inventory)
                if (!stack.IsEmpty && stack.ItemId == itemId) total += stack.Quantity;
            return total;
        }

        public bool TryTransferAnyItemServer(PlayerInventory destination, ushort itemId)
        {
            if (!IsServer || destination == null || !destination.IsServer
                || model == null || destination.model == null || itemId == 0
                || destination.catalog == null) return false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var slot = new InventorySlotReference(InventorySlotArea.Inventory, index);
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.ItemId != itemId
                    || !model.RemoveStack(slot, itemId, 1, out var removed)) continue;
                var remainder = destination.InsertStackServer(removed);
                if (remainder == 0)
                {
                    SynchronizeAll();
                    ServerInventoryChanged?.Invoke();
                    return true;
                }
                model.SetSlot(slot, stack);
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return false;
            }
            return false;
        }

        public bool HasServerFood()
        {
            if (!IsServer || model == null || catalog == null) return false;
            foreach (var stack in model.Inventory)
            {
                if (!stack.IsEmpty && catalog.TryGetItem(stack.ItemId, out var item)
                    && item.Kind == ItemKind.Food)
                    return true;
            }
            return false;
        }

        public bool TryConsumeServerFood(
            out ItemDefinition definition,
            out ItemStackState consumed)
        {
            definition = null;
            consumed = default;
            if (!IsServer || model == null || catalog == null) return false;
            var bestIndex = -1;
            var bestScore = float.MinValue;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Kind != ItemKind.Food)
                    continue;
                var score = stack.Freshness
                    - stack.BiologicalContamination * 0.75f
                    - stack.ToxinContamination * 2f;
                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = index;
                definition = item;
            }
            if (bestIndex < 0 || !model.RemoveStack(
                    new InventorySlotReference(InventorySlotArea.Inventory, bestIndex),
                    model.Inventory[bestIndex].ItemId, 1, out consumed))
            {
                definition = null;
                consumed = default;
                return false;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool HasServerDrink(ushort requiredMilliliters = 200)
        {
            return TryFindServerDrink(requiredMilliliters, out _, out _);
        }

        public bool TryConsumeServerDrink(
            ushort requiredMilliliters,
            out ItemStackState consumed)
        {
            consumed = default;
            if (!TryFindServerDrink(requiredMilliliters, out var slot, out var stack))
                return false;
            consumed = stack;
            consumed.LiquidMilliliters = requiredMilliliters;
            stack.LiquidMilliliters -= requiredMilliliters;
            if (stack.LiquidMilliliters == 0) stack.LiquidKind = LiquidKind.None;
            if (!model.SetSlot(slot, stack))
            {
                consumed = default;
                return false;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool HasServerTool(ToolKind tool)
        {
            if (!IsServer || model == null || catalog == null || tool == ToolKind.None)
                return false;
            foreach (var stack in model.Inventory)
            {
                if (!stack.IsEmpty && stack.Condition > 0
                    && catalog.TryGetItem(stack.ItemId, out var item)
                    && item.Tool == tool)
                    return true;
            }
            return false;
        }

        public bool TryDamageServerTool(ToolKind tool, int amount, out bool broke)
        {
            broke = false;
            if (!IsServer || model == null || catalog == null
                || tool == ToolKind.None || amount <= 0)
                return false;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.Condition == 0
                    || !catalog.TryGetItem(stack.ItemId, out var item)
                    || item.Tool != tool || !item.IsDurable)
                    continue;
                if (stack.Condition <= amount)
                {
                    stack.Clear();
                    broke = true;
                }
                else
                {
                    stack.Condition -= (ushort)amount;
                }
                if (!model.SetSlot(
                        new InventorySlotReference(InventorySlotArea.Inventory, index), stack))
                    return false;
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool TryGiveServerItem(ushort itemId, ushort quantity = 1)
        {
            if (!IsServer || model == null || quantity == 0
                || !catalog.TryGetItem(itemId, out var definition)) return false;
            var stack = new ItemStackState(itemId, quantity);
            var remainder = model.AutoInsert(stack, definition.PickupPriority);
            if (remainder != 0) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryGiveServerStack(ItemStackState stack)
        {
            if (!IsServer || model == null || stack.IsEmpty || catalog == null
                || !catalog.TryGetItem(stack.ItemId, out var definition)) return false;
            var remainder = model.AutoInsert(stack, definition.PickupPriority);
            if (remainder != 0) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public List<ulong> GetServerItemInstances(ushort itemId, int maximum)
        {
            var result = new List<ulong>();
            if (!IsServer || model == null || itemId == 0 || maximum <= 0) return result;
            foreach (var stack in model.Inventory)
            {
                if (stack.IsEmpty || stack.ItemId != itemId || stack.ItemInstanceId == 0)
                    continue;
                result.Add(stack.ItemInstanceId);
                if (result.Count >= maximum) break;
            }
            return result;
        }

        public bool TryGetServerCleanWater(
            ushort requiredMilliliters,
            out float cleanliness)
        {
            return TryFindServerCleanWater(
                requiredMilliliters, out _, out _, out cleanliness);
        }

        public bool TryConsumeServerCleanWater(
            ushort requiredMilliliters,
            out float cleanliness)
        {
            cleanliness = 0f;
            if (!TryFindServerCleanWater(
                    requiredMilliliters, out var slot, out var stack, out cleanliness))
            {
                return false;
            }

            stack.LiquidMilliliters -= requiredMilliliters;
            if (stack.LiquidMilliliters == 0) stack.LiquidKind = LiquidKind.None;
            if (!model.SetSlot(slot, stack)) return false;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return true;
        }

        public bool TryDepositWasteServer(ushort milliliters)
        {
            if (!IsServer || model == null || catalog == null || milliliters == 0
                || !catalog.TryGetItem(40, out var chamberPot))
            {
                return false;
            }
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.ItemId != 40
                    || stack.LiquidKind != LiquidKind.None
                        && stack.LiquidKind != LiquidKind.Waste)
                {
                    continue;
                }
                var capacity = chamberPot.LiquidCapacityMilliliters;
                if (capacity < stack.LiquidMilliliters + milliliters) continue;
                stack.LiquidKind = LiquidKind.Waste;
                stack.LiquidMilliliters += milliliters;
                stack.BiologicalContamination = 10000;
                stack.Cleanliness = (ushort)Mathf.Max(0, stack.Cleanliness - 1800);
                if (!model.SetSlot(
                        new InventorySlotReference(InventorySlotArea.Inventory, index),
                        stack))
                {
                    return false;
                }
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                return true;
            }
            return false;
        }

        private bool TryFindServerCleanWater(
            ushort requiredMilliliters,
            out InventorySlotReference slot,
            out ItemStackState selected,
            out float cleanliness)
        {
            slot = InventorySlotReference.Invalid;
            selected = default;
            cleanliness = 0f;
            if (!IsServer || model == null || requiredMilliliters == 0) return false;

            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.LiquidKind != LiquidKind.Water
                    || stack.LiquidMilliliters < requiredMilliliters
                    || stack.BiologicalContamination > 1500
                    || stack.ToxinContamination > 1000)
                {
                    continue;
                }

                var candidateCleanliness = stack.Cleanliness / 10000f
                    * (1f - stack.BiologicalContamination / 10000f);
                if (slot.IsValid && candidateCleanliness <= cleanliness) continue;
                slot = new InventorySlotReference(InventorySlotArea.Inventory, index);
                selected = stack;
                cleanliness = Mathf.Clamp01(candidateCleanliness);
            }
            return slot.IsValid;
        }

        private bool TryFindServerDrink(
            ushort requiredMilliliters,
            out InventorySlotReference slot,
            out ItemStackState selected)
        {
            slot = InventorySlotReference.Invalid;
            selected = default;
            if (!IsServer || model == null || requiredMilliliters == 0) return false;
            var bestRisk = float.MaxValue;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || stack.LiquidMilliliters < requiredMilliliters
                    || stack.LiquidKind is LiquidKind.None or LiquidKind.Waste
                        or LiquidKind.SaltWater)
                    continue;
                var saltPenalty = stack.LiquidKind == LiquidKind.SaltWater ? 20000f : 0f;
                var risk = stack.BiologicalContamination
                    + stack.ToxinContamination * 2f + saltPenalty;
                if (risk >= bestRisk) continue;
                bestRisk = risk;
                slot = new InventorySlotReference(InventorySlotArea.Inventory, index);
                selected = stack;
            }
            return slot.IsValid;
        }

        public int InsertStackServer(ItemStackState stack)
        {
            if (!IsServer || model == null || stack.IsEmpty || catalog == null
                || !catalog.TryGetItem(stack.ItemId, out var item))
            {
                return stack.Quantity;
            }
            var remainder = model.AutoInsert(stack, item.PickupPriority);
            if (remainder != stack.Quantity)
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
            }
            return remainder;
        }

        public int RevealSamplesServer(ulong sourceNodeId, int studyPercent)
        {
            if (!IsServer || model == null) return 0;
            var revealed = model.RevealSamples(sourceNodeId, studyPercent);
            if (studyPercent >= 100)
            {
                for (var index = 0; index < serverPendingItems.Count; index++)
                {
                    var stack = serverPendingItems[index];
                    if (stack.ItemId != ResourceBalance.UnknownSampleItemId
                        || stack.SourceNodeId != sourceNodeId || stack.HiddenItemId == 0)
                    {
                        continue;
                    }
                    serverPendingItems[index] = new ItemStackState(
                        stack.HiddenItemId, stack.Quantity, 0, stack.Quality);
                    revealed += stack.Quantity;
                }
            }
            if (revealed > 0)
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
            }
            return revealed;
        }

        public bool SpawnOverflowServer(ItemStackState stack, Vector3 nearPosition)
        {
            if (!IsServer || stack.IsEmpty || worldItemPrefab == null) return false;
            var position = nearPosition + Vector3.up * 0.45f
                + UnityEngine.Random.insideUnitSphere * 0.18f;
            position.y = nearPosition.y + 0.45f;
            var instance = Instantiate(worldItemPrefab, position, Quaternion.identity);
            var networkObject = instance.GetComponent<NetworkObject>();
            var worldItem = instance.GetComponent<NetworkWorldItem>();
            if (networkObject == null || worldItem == null)
            {
                Destroy(instance);
                return false;
            }
            networkObject.Spawn(true);
            if (worldItem.InitializeServer(stack)) return true;
            networkObject.Despawn(true);
            return false;
        }

        public void PlayActiveToolSwing()
        {
#if !UNITY_SERVER
            if (!IsOwner || heldVisual == null) return;
            if (toolSwingRoutine != null) StopCoroutine(toolSwingRoutine);
            toolSwingRoutine = StartCoroutine(AnimateToolSwing());
#endif
        }

        private IEnumerator AnimateToolSwing()
        {
            var target = heldVisual != null ? heldVisual.transform : null;
            if (target == null) yield break;
            var startRotation = target.localRotation;
            const float duration = 0.28f;
            var elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var arc = Mathf.Sin(normalized * Mathf.PI);
                target.localRotation = startRotation * Quaternion.Euler(arc * 68f, 0f, -arc * 18f);
                yield return null;
            }
            if (target != null) target.localRotation = startRotation;
            toolSwingRoutine = null;
        }

        public void SetInterfaceOpen(bool open, bool showWorkbench = false)
        {
            if (!IsOwner) return;
            if (resourceInteraction != null && resourceInteraction.CurrentResearchTableId != 0)
            {
                resourceInteraction.OnLocalInventoryClosed();
            }
            interfaceOpen = open;
            InventoryView.SetMode(open, showWorkbench);
            player.SetInventoryInterfaceOpen(open);
            if (!open)
            {
                CloseInterfaceServerRpc();
            }
        }

        public void SetResearchTableInterfaceOpen(bool open)
        {
            if (!IsOwner) return;
            interfaceOpen = open;
            InventoryView.SetResearchTableMode(open);
            player.SetInventoryInterfaceOpen(open);
            if (!open) CloseInterfaceServerRpc();
        }

        public bool TryCloseInterface()
        {
            if (!interfaceOpen) return false;
            SetInterfaceOpen(false);
            return true;
        }

        public void RequestLeftClick(InventorySlotReference slot, int selectedQuantity)
        {
            if (IsOwner && interfaceOpen)
            {
                LeftClickServerRpc(slot, Mathf.Clamp(selectedQuantity, 0, ushort.MaxValue));
            }
        }

        public void RequestRightClick(InventorySlotReference slot)
        {
            if (IsOwner && interfaceOpen) RightClickServerRpc(slot);
        }

        public void RequestShiftClick(InventorySlotReference slot)
        {
            if (IsOwner && interfaceOpen) ShiftClickServerRpc(slot);
        }

        public void RequestAdjustCursor(int delta)
        {
            if (IsOwner && interfaceOpen && delta != 0)
            {
                AdjustCursorServerRpc(Mathf.Clamp(delta, -100, 100));
            }
        }

        public bool CanMoveStackLocally(
            InventorySlotReference source,
            InventorySlotReference destination,
            ushort expectedItemId,
            int quantity)
        {
            if (!source.IsValid || !destination.IsValid || source.Equals(destination)
                || catalog == null || !catalog.TryGetItem(expectedItemId, out var item))
            {
                return false;
            }

            if (source.Area == InventorySlotArea.ResearchTable
                || destination.Area == InventorySlotArea.ResearchTable)
            {
                if (source.Area == InventorySlotArea.ResearchTable
                    && destination.Area == InventorySlotArea.ResearchTable)
                {
                    return false;
                }
                var sourceStack = GetReplicatedSlot(source);
                var destinationStack = GetReplicatedSlot(destination);
                if (destination.Area == InventorySlotArea.ResearchTable)
                {
                    return item.Kind == ItemKind.HiddenSample && quantity == 1
                        && !sourceStack.IsEmpty && destinationStack.IsEmpty;
                }
                return destination.Area == InventorySlotArea.Inventory
                    && destinationStack.IsEmpty && quantity == 1 && !sourceStack.IsEmpty;
            }

            return InventoryModel.CanMoveStack(
                GetReplicatedSlot(source),
                GetReplicatedSlot(destination),
                expectedItemId,
                quantity,
                item.MaximumStack);
        }

        public bool RequestMoveStack(
            InventorySlotReference source,
            InventorySlotReference destination,
            ushort expectedItemId,
            int quantity,
            Action<bool> completed)
        {
            if (!IsOwner || !interfaceOpen || !source.IsValid || !destination.IsValid
                || source.Equals(destination) || expectedItemId == 0 || quantity <= 0)
            {
                completed?.Invoke(false);
                return false;
            }

            nextMoveRequestId++;
            if (nextMoveRequestId == 0) nextMoveRequestId++;
            var requestId = nextMoveRequestId;
            pendingMoveCallbacks[requestId] = completed;
            MoveStackServerRpc(
                source,
                destination,
                expectedItemId,
                Mathf.Clamp(quantity, 1, ushort.MaxValue),
                requestId);
            return true;
        }

        public bool RequestDropStack(
            InventorySlotReference source,
            ushort expectedItemId,
            int quantity,
            Action<bool> completed)
        {
            if (!IsOwner || !interfaceOpen || !source.IsValid
                || expectedItemId == 0 || quantity <= 0)
            {
                completed?.Invoke(false);
                return false;
            }

            nextMoveRequestId++;
            if (nextMoveRequestId == 0) nextMoveRequestId++;
            var requestId = nextMoveRequestId;
            pendingMoveCallbacks[requestId] = completed;
            DropStackServerRpc(
                source,
                expectedItemId,
                Mathf.Clamp(quantity, 1, ushort.MaxValue),
                requestId);
            return true;
        }

        public void RequestCraft(ushort recipeId)
        {
            if (IsOwner && interfaceOpen) CraftServerRpc(recipeId);
        }

        public bool CanCraftLocally(ushort recipeId)
        {
            if (IsCrafting) return false;
            if (catalog == null || !catalog.TryGetRecipe(recipeId, out var recipe)) return false;
            if (!cursor.Value.IsEmpty || recipe.Output == null) return false;
            var counts = new Dictionary<ushort, int>();
            foreach (var stack in workbench)
            {
                if (stack.IsEmpty) continue;
                counts.TryGetValue(stack.ItemId, out var amount);
                counts[stack.ItemId] = amount + stack.Quantity;
            }

            if (counts.Count != recipe.Ingredients.Count) return false;
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Item == null || !counts.TryGetValue(ingredient.Item.ItemId, out var amount)
                    || amount != ingredient.Quantity)
                {
                    return false;
                }
            }

            var capacity = 0;
            foreach (var stack in inventory)
            {
                if (stack.IsEmpty) capacity += recipe.Output.MaximumStack;
                else if (stack.ItemId == recipe.Output.ItemId)
                {
                    capacity += recipe.Output.MaximumStack - stack.Quantity;
                }

                if (capacity >= recipe.OutputQuantity) return true;
            }

            return false;
        }

        [ServerRpc]
        private void LeftClickServerRpc(InventorySlotReference slot, int quantity)
        {
            Mutate(() => model.LeftClick(slot, quantity));
        }

        [ServerRpc]
        private void RightClickServerRpc(InventorySlotReference slot)
        {
            Mutate(() => model.RightClick(slot));
        }

        [ServerRpc]
        private void ShiftClickServerRpc(InventorySlotReference slot)
        {
            Mutate(() => model.ShiftClick(slot));
        }

        [ServerRpc]
        private void AdjustCursorServerRpc(int delta)
        {
            Mutate(() => model.AdjustCursorFromOrigin(delta));
        }

        [ServerRpc]
        private void MoveStackServerRpc(
            InventorySlotReference source,
            InventorySlotReference destination,
            ushort expectedItemId,
            int quantity,
            uint requestId,
            ServerRpcParams rpcParams = default)
        {
            var moved = CanUseInventory && (source.Area == InventorySlotArea.ResearchTable
                    || destination.Area == InventorySlotArea.ResearchTable
                ? resourceInteraction != null && resourceInteraction.TryMoveResearchTableStackServer(
                    source, destination, expectedItemId, quantity)
                : model != null && model.MoveStack(
                    source,
                    destination,
                    expectedItemId,
                    quantity));
            if (moved)
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
            }

            var target = new[] { rpcParams.Receive.SenderClientId };
            MoveStackResultClientRpc(requestId, moved, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = target },
            });
        }

        [ServerRpc]
        private void DropStackServerRpc(
            InventorySlotReference source,
            ushort expectedItemId,
            int quantity,
            uint requestId,
            ServerRpcParams rpcParams = default)
        {
            var dropped = CanUseInventory && TryDropStackServer(source, expectedItemId, quantity);
            if (dropped)
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
            }

            var target = new[] { rpcParams.Receive.SenderClientId };
            MoveStackResultClientRpc(requestId, dropped, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = target },
            });
        }

        private bool TryDropStackServer(
            InventorySlotReference source,
            ushort expectedItemId,
            int quantity)
        {
            if (model == null || worldItemPrefab == null
                || !model.CanRemoveStack(source, expectedItemId, quantity))
            {
                return false;
            }

            GameObject instance = null;
            NetworkObject networkObject = null;
                var sourceStack = model.GetSlot(source);
                var droppedStack = sourceStack.WithQuantity(quantity);
                droppedStack.Equipped = false;
            try
            {
                instance = Instantiate(worldItemPrefab, ResolveWorldDropPosition(), Quaternion.identity);
                networkObject = instance.GetComponent<NetworkObject>();
                var worldItem = instance.GetComponent<NetworkWorldItem>();
                if (networkObject == null || worldItem == null)
                {
                    Destroy(instance);
                    return false;
                }

                networkObject.Spawn(true);
                if (!worldItem.InitializeServer(droppedStack)
                    || !model.RemoveStack(source, expectedItemId, quantity, out _))
                {
                    networkObject.Despawn(true);
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn(true);
                }
                else if (instance != null)
                {
                    Destroy(instance);
                }

                return false;
            }
        }

        private Vector3 ResolveWorldDropPosition()
        {
            var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var distance = 1.3f;
            var castOrigin = transform.position + Vector3.up * 0.8f;
            if (Physics.SphereCast(
                castOrigin,
                0.28f,
                forward,
                out var obstacle,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
                && !obstacle.transform.IsChildOf(transform))
            {
                distance = Mathf.Max(0.65f, obstacle.distance - 0.38f);
            }

            var planar = transform.position + forward * distance;
            var position = planar + Vector3.up * 0.42f;
            if (Physics.Raycast(
                planar + Vector3.up * 2.5f,
                Vector3.down,
                out var ground,
                5f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
                && !ground.transform.IsChildOf(transform))
            {
                position.y = ground.point.y + 0.42f;
            }

            return position;
        }

        public bool ServerTransferFirstInventoryStackTo(
            PlayerInventory receiver,
            out string transferredItemName)
        {
            transferredItemName = string.Empty;
            if (!IsServer || receiver == null || !receiver.IsServer
                || model == null || receiver.model == null || catalog == null)
            {
                return false;
            }

            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var source = model.Inventory[index];
                if (source.IsEmpty || !catalog.TryGetItem(source.ItemId, out var definition))
                    continue;
                var sourceReference = new InventorySlotReference(
                    InventorySlotArea.Inventory, index);
                if (!model.TryTransferTo(
                        sourceReference,
                        source.Quantity,
                        receiver.model,
                        definition.PickupPriority,
                        out var transferred)) continue;

                if (transferred.ItemId == 36 && transferred.ItemInstanceId != 0
                    && transferred.Quantity == 1)
                {
                    var document = resourceInteraction?.ServerTakeMapDocument(
                        transferred.ItemInstanceId);
                    receiver.resourceInteraction?.ServerReceiveMapDocument(
                        transferred.ItemInstanceId, document);
                }
                SynchronizeAll();
                receiver.SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                receiver.ServerInventoryChanged?.Invoke();
                transferredItemName = definition.DisplayName;
                return true;
            }
            return false;
        }

        public int ServerDestroyOrganicItemsForCremation()
        {
            if (!IsServer || model == null) return 0;
            var destroyed = 0;
            for (var index = 0; index < model.Inventory.Count; index++)
            {
                var stack = model.Inventory[index];
                if (stack.IsEmpty || !IsOrganicForCremation(stack.ItemId)) continue;
                if (stack.ItemId == 36 && stack.ItemInstanceId != 0)
                    resourceInteraction?.ServerTakeMapDocument(stack.ItemInstanceId);
                destroyed += stack.Quantity;
                model.SetSlot(
                    new InventorySlotReference(InventorySlotArea.Inventory, index), default);
            }
            for (var index = serverPendingItems.Count - 1; index >= 0; index--)
            {
                var stack = serverPendingItems[index];
                if (stack.IsEmpty || !IsOrganicForCremation(stack.ItemId)) continue;
                destroyed += stack.Quantity;
                serverPendingItems.RemoveAt(index);
            }
            if (destroyed == 0) return 0;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            return destroyed;
        }

        private static bool IsOrganicForCremation(ushort itemId) => itemId is
            2 or 3 or 23 or 25 or 26 or 27 or 28 or 29 or 31 or 32 or 34
                or 35 or 36 or 37 or 38 or 39 or 42 or 43 or 44 or 47 or 48
                or 49 or 50 or 51 or 52 or 53 or 54 or 55 or 56 or 57 or 58 or 59;

        [ServerRpc]
        private void LootCorpseServerRpc(NetworkObjectReference corpseReference)
        {
            if (!IsServer || !corpseReference.TryGet(out var networkObject)
                || networkObject == null
                || !networkObject.TryGetComponent<PlayerInventory>(out var corpseInventory)
                || corpseInventory == this
                || !networkObject.TryGetComponent<PlayerSurvival>(out var corpseSurvival)
                || !corpseSurvival.ServerCanBeSearched
                || playerSurvival == null || serverPersistenceLocked
                || !playerSurvival.CanPerformServerAction()
                || Vector3.Distance(transform.position, corpseInventory.transform.position)
                    > ResourceBalance.InteractionDistance + 0.75f)
            {
                return;
            }

            var session = QuieterRuntimeBootstrap.Instance?.Session;
            if (session == null)
                SendUseFeedbackClientRpc("Сервер не может подтвердить перенос вещи.");
            else
            {
                corpseSurvival.ServerWakeFromDanger();
                session.BeginCorpseLoot(corpseInventory, this);
            }
        }

        [ServerRpc]
        private void BuryCorpseServerRpc(NetworkObjectReference corpseReference)
        {
            if (!TryResolveCorpseAction(
                    corpseReference, out _, out var corpseSurvival)) return;
            var active = model?.ActiveStack ?? default;
            if (active.IsEmpty || catalog == null
                || !catalog.TryGetItem(active.ItemId, out var tool)
                || tool.Tool != ToolKind.Shovel)
            {
                SendUseFeedbackClientRpc("Для погребения держите в руках лопату.");
                return;
            }
            if (!corpseSurvival.ServerBuryCorpse(out var message))
            {
                SendUseFeedbackClientRpc(message);
                return;
            }
            TryDamageActiveToolServer(ToolKind.Shovel, 250, out _);
            playerSurvival?.ServerRegisterPractice(
                SkillId.Excavation, 30f, 0.5f, 1f, 0f);
            SendUseFeedbackClientRpc(message);
        }

        [ServerRpc]
        private void CremateCorpseServerRpc(NetworkObjectReference corpseReference)
        {
            if (!TryResolveCorpseAction(
                    corpseReference, out var corpseInventory, out var corpseSurvival)) return;
            placedObjects ??= FindAnyObjectByType<PlacedObjectWorldService>();
            if (placedObjects == null || !placedObjects.TryFindBurningHearth(
                    corpseInventory.transform.position, 3.5f, out _))
            {
                SendUseFeedbackClientRpc(
                    "Для сожжения нужен горящий каменный очаг рядом с телом.");
                return;
            }
            if (!corpseSurvival.ServerCremateCorpse(out var message))
            {
                SendUseFeedbackClientRpc(message);
                return;
            }
            playerSurvival?.ServerRegisterPractice(
                SkillId.Firekeeping, 30f, 0.45f, 1f, 0f);
            SendUseFeedbackClientRpc(message);
        }

        private bool TryResolveCorpseAction(
            NetworkObjectReference corpseReference,
            out PlayerInventory corpseInventory,
            out PlayerSurvival corpseSurvival)
        {
            corpseInventory = null;
            corpseSurvival = null;
            return IsServer && corpseReference.TryGet(out var networkObject)
                && networkObject != null
                && networkObject.TryGetComponent(out corpseInventory)
                && corpseInventory != this
                && networkObject.TryGetComponent(out corpseSurvival)
                && corpseSurvival.ServerIsDead
                && !corpseInventory.ServerPersistenceLocked
                && playerSurvival != null
                && playerSurvival.CanPerformServerAction()
                && Vector3.Distance(transform.position, corpseInventory.transform.position)
                    <= ResourceBalance.InteractionDistance + 0.75f;
        }

        [ClientRpc]
        private void MoveStackResultClientRpc(
            uint requestId,
            bool success,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner || !pendingMoveCallbacks.Remove(requestId, out var callback)) return;
            callback?.Invoke(success);
        }

        [ServerRpc]
        private void SelectHotbarServerRpc(byte index)
        {
            if (!CanUseInventory || index >= InventoryLayout.HotbarSlotCount) return;
            model.SetSelectedHotbar(index);
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
        }

        [ServerRpc]
        private void UseActiveItemServerRpc()
        {
            if (!CanUseInventory || catalog == null)
                return;

            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + model.SelectedHotbarIndex);
            var stack = model.ActiveStack;
            if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var item)) return;

            if (item.Kind == ItemKind.Clothing)
            {
                if (!model.ToggleEquippedClothing(slot, out var equipped)) return;
                CompleteActiveUse(equipped
                    ? $"Надето: {item.DisplayName}."
                    : $"Снято: {item.DisplayName}.");
                return;
            }

            var biological = stack.BiologicalContamination / 10000f;
            var toxins = stack.ToxinContamination / 10000f;
            if (item.Kind == ItemKind.Food)
            {
                var spoilage = 1f - stack.Freshness / 10000f;
                biological = Mathf.Clamp01(biological + spoilage * spoilage * 0.85f);
                if (!playerSurvival.CanPerformServerAction()
                    || !model.CanRemoveStack(slot, stack.ItemId, 1)
                    || !model.RemoveStack(slot, stack.ItemId, 1, out _)
                    || !playerSurvival.ServerConsumeFood(
                        item.CaloriesPerUnit,
                        item.ProteinGramsPerUnit,
                        item.MicronutrientsPerUnit,
                        item.WaterLitersPerUnit,
                        biological,
                        toxins,
                        item.FatGramsPerUnit,
                        item.MineralsPerUnit))
                {
                    return;
                }

                CompleteActiveUse($"Съедено: {item.DisplayName}.");
                return;
            }

            if (item.Kind != ItemKind.LiquidContainer || stack.LiquidMilliliters == 0
                || stack.LiquidKind == LiquidKind.None || stack.LiquidKind == LiquidKind.Waste)
            {
                SendUseFeedbackClientRpc("Этот предмет сейчас нельзя употребить.");
                return;
            }

            var milliliters = (ushort)Mathf.Min(250, stack.LiquidMilliliters);
            var hydrationEfficiency = stack.LiquidKind == LiquidKind.SaltWater ? -0.35f : 1f;
            var electrolytes = stack.LiquidKind switch
            {
                LiquidKind.Broth => 0.65f,
                LiquidKind.HerbalInfusion => 0.18f,
                LiquidKind.SaltWater => 1f,
                _ => 0.05f,
            };
            if (!playerSurvival.ServerConsumeLiquid(
                    milliliters / 1000f,
                    biological,
                    toxins,
                    electrolytes,
                    hydrationEfficiency))
            {
                return;
            }
            playerSurvival.ServerApplyPreparedLiquidEffects(
                stack.LiquidKind, milliliters / 1000f);

            stack.LiquidMilliliters -= milliliters;
            if (stack.LiquidMilliliters == 0) stack.LiquidKind = LiquidKind.None;
            if (!model.SetSlot(slot, stack)) return;
            CompleteActiveUse($"Выпито: {milliliters} мл.");
        }

        private void CompleteActiveUse(string feedback)
        {
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            SendUseFeedbackClientRpc(feedback);
        }

        [ClientRpc]
        private void SendUseFeedbackClientRpc(string feedback)
        {
            if (IsOwner) resourceInteraction?.SetLocalFeedback(feedback);
        }

        [ServerRpc]
        private void CraftServerRpc(ushort recipeId)
        {
            if (!CanUseInventory || IsCrafting || resourceInteraction?.HasServerManualWork == true
                || catalog == null
                || !catalog.TryGetRecipe(recipeId, out var recipe)
                || !model.Cursor.IsEmpty || !model.MatchesExactly(recipe)) return;
            placedObjects ??= FindAnyObjectByType<PlacedObjectWorldService>();
            serverCraftHearthId = 0;
            if (recipe.RequiresBurningHearth && (placedObjects == null
                || !placedObjects.TryFindBurningHearth(transform.position, 3.5f,
                    out serverCraftHearthId)
                || !placedObjects.CanInteract(serverCraftHearthId, transform, 3.5f)))
            {
                SendUseFeedbackClientRpc("Для обжига нужен горящий очаг рядом.");
                return;
            }
            if (recipe.WorkSeconds <= 0f)
            {
                Mutate(() => model.TryCraft(recipe));
                return;
            }
            serverCraftRecipe = recipe;
            serverCraftPosition = transform.position;
            craftCompletesAt.Value = NetworkManager.ServerTime.Time + recipe.WorkSeconds;
            SendUseFeedbackClientRpc($"Работа начата: {recipe.DisplayName}. Оставайтесь на месте.");
        }

        private void TickServerCrafting()
        {
            if (serverCraftRecipe == null) return;
            if (!CanUseInventory || Vector3.Distance(transform.position, serverCraftPosition) > 1.25f
                || !model.Cursor.IsEmpty || !model.MatchesExactly(serverCraftRecipe)
                || serverCraftRecipe.RequiresBurningHearth
                    && (placedObjects == null
                        || !placedObjects.TryGetBurningHearth(serverCraftHearthId, out var hearthPosition)
                        || !placedObjects.CanInteract(serverCraftHearthId, transform, 3.5f)
                        || Vector3.Distance(transform.position, hearthPosition) > 3.5f))
            {
                serverCraftRecipe = null;
                craftCompletesAt.Value = 0d;
                SendUseFeedbackClientRpc("Работа прервана. Материалы сохранены.");
                return;
            }
            if (NetworkManager.ServerTime.Time < craftCompletesAt.Value) return;
            var completed = serverCraftRecipe;
            serverCraftRecipe = null;
            craftCompletesAt.Value = 0d;
            if (!model.TryCraft(completed))
            {
                SendUseFeedbackClientRpc("Недостаточно места для результата. Материалы сохранены.");
                return;
            }
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
            playerSurvival.ServerRegisterPractice(
                completed.PracticeSkill, completed.WorkSeconds, 0.3f, 1f, 0f);
            SendUseFeedbackClientRpc($"Изготовлено: {completed.DisplayName}.");
        }

        [ServerRpc]
        private void CloseInterfaceServerRpc()
        {
            Mutate(() => model.NormalizeTemporaryStorage(), requiresAction: false);
        }

        [ServerRpc]
        private void PickupServerRpc(NetworkObjectReference pickupReference)
        {
            if (!CanUseInventory || !pickupReference.TryGet(out var networkObject)
                || networkObject == null || !networkObject.IsSpawned
                || !networkObject.TryGetComponent<NetworkWorldItem>(out var pickup)
                || Vector3.Distance(transform.position, pickup.transform.position) > 3.25f)
            {
                SendPickupResultClientRpc(PickupResultCode.Unavailable, 0, 0);
                return;
            }

            var itemId = pickup.Stack.ItemId;
            if (!HasLineOfSight(pickup))
            {
                SendPickupResultClientRpc(PickupResultCode.Blocked, itemId, 0);
                return;
            }

            if (pickup.TryCollectServer(model, out var collected))
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
                SendPickupResultClientRpc(PickupResultCode.Collected, itemId, collected);
                return;
            }
            SendPickupResultClientRpc(PickupResultCode.InventoryFull, itemId, 0);
        }

        [ClientRpc]
        private void SendPickupResultClientRpc(PickupResultCode code, ushort itemId, int quantity)
        {
            if (!IsOwner || resourceInteraction == null) return;
            if (code == PickupResultCode.Collected)
            {
                var itemName = catalog != null && catalog.TryGetItem(itemId, out var item)
                    ? item.DisplayName
                    : "предмет";
                resourceInteraction.SetLocalFeedback($"Подобрано: {itemName} × {quantity}");
            }
            else if (code == PickupResultCode.InventoryFull)
            {
                resourceInteraction.SetLocalFeedback("В инвентаре нет места. Предмет остался на земле.");
            }
            else if (code == PickupResultCode.Blocked)
            {
                resourceInteraction.SetLocalFeedback("Предмет перекрыт препятствием.");
            }
            else
            {
                resourceInteraction.SetLocalFeedback("Предмет уже недоступен.");
            }
        }

        private void Mutate(Func<bool> mutation, bool requiresAction = true)
        {
            if (!IsServer || model == null || mutation == null
                || requiresAction && !CanUseInventory || !mutation()) return;
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
        }

        private void SynchronizeAll()
        {
            if (!IsServer || model == null) return;
            DeliverPendingItems();
            SynchronizeList(inventory, model.Inventory);
            SynchronizeList(workbench, model.Workbench);
            cursor.Value = model.Cursor.ForReplication();
            selectedHotbar.Value = model.SelectedHotbarIndex;
            activeItemId.Value = model.ActiveStack.IsEmpty ? (ushort)0 : model.ActiveStack.ItemId;
        }

        private void DeliverPendingItems()
        {
            if (serverPendingItems.Count == 0 || model == null || catalog == null) return;
            for (var index = serverPendingItems.Count - 1; index >= 0; index--)
            {
                var stack = serverPendingItems[index];
                if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var item))
                {
                    serverPendingItems.RemoveAt(index);
                    continue;
                }
                var remainder = model.AutoInsert(stack, item.PickupPriority);
                if (remainder <= 0) serverPendingItems.RemoveAt(index);
                else serverPendingItems[index] = stack.WithQuantity(remainder);
            }
        }

        private bool TryRestorePendingStack(
            StoredInventorySlot stored,
            ulong ownerSalt,
            int ordinal,
            out ItemStackState stack)
        {
            stack = default;
            if (stored == null || stored.Quantity == 0 || catalog == null
                || !catalog.TryGetItem(stored.ItemId, out var item)
                || stored.Quantity > item.MaximumStack
                || (byte)stored.Quality > (byte)ResourceQuality.Superior
                || stored.HiddenItemId != 0 && !catalog.TryGetItem(stored.HiddenItemId, out _))
            {
                return false;
            }
            ulong.TryParse(stored.SourceNodeId, out var sourceNodeId);
            ulong.TryParse(stored.SampleId, out var sampleId);
            ulong.TryParse(stored.ItemInstanceId, out var itemInstanceId);
            if (item.Kind == ItemKind.HiddenSample && sampleId == 0)
            {
                unchecked
                {
                    sampleId = 1469598103934665603UL;
                    sampleId = (sampleId ^ ownerSalt) * 1099511628211UL;
                    sampleId = (sampleId ^ (uint)ordinal) * 1099511628211UL;
                    sampleId = (sampleId ^ sourceNodeId) * 1099511628211UL;
                    if (sampleId == 0) sampleId = 1;
                }
            }
            if (item.Kind == ItemKind.HiddenSample
                && (stored.HiddenItemId == 0 || sourceNodeId == 0 || sampleId == 0))
            {
                return false;
            }
            stack = new ItemStackState(
                stored.ItemId,
                stored.Quantity,
                stored.Condition,
                (ResourceQuality)stored.Quality,
                stored.HiddenItemId,
                sourceNodeId,
                stored.RevealAtPercent,
                sampleId,
                itemInstanceId,
                stored.Freshness,
                stored.BiologicalContamination,
                stored.ToxinContamination,
                stored.Wetness,
                stored.Cleanliness,
                stored.LiquidMilliliters,
                (LiquidKind)stored.LiquidKind,
                stored.Equipped);
            if (item.RequiresInstanceId && stack.ItemInstanceId == 0)
            {
                stack.ItemInstanceId = ItemInstanceIdFactory.Create();
            }
            return true;
        }

        private static void SynchronizeList(
            NetworkList<ItemStackState> target,
            IReadOnlyList<ItemStackState> source)
        {
            while (target.Count < source.Count) target.Add(default);
            while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
            for (var index = 0; index < source.Count; index++)
            {
                var replicated = source[index].ForReplication();
                if (!target[index].Equals(replicated)) target[index] = replicated;
            }
        }

        private bool HasLineOfSight(NetworkWorldItem pickup)
        {
            var origin = transform.position + Vector3.up * 1.5f;
            var colliders = pickup.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                var target = collider.ClosestPoint(origin);
                if ((target - origin).sqrMagnitude < 0.0001f) target = collider.bounds.center;
                var direction = target - origin;
                if (direction.sqrMagnitude < 0.0001f) return true;
                var hits = Physics.RaycastAll(
                    origin,
                    direction.normalized,
                    direction.magnitude + 0.2f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide);
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (var hit in hits)
                {
                    if (hit.transform.IsChildOf(transform)) continue;
                    if (hit.collider.GetComponentInParent<NetworkWorldItem>() == pickup) return true;
                    break;
                }
            }

            return false;
        }

        private void UpdateFocusedPickup()
        {
            focusedPickup = null;
            focusedCorpseInventory = null;
            var camera = player.OwnerCamera;
            if (!WorldInteractionRaycast.TryGetClosest(
                    camera, transform, ResourceBalance.InteractionDistance, out var hit)) return;
            focusedPickup = hit.collider.GetComponentInParent<NetworkWorldItem>();
            if (focusedPickup != null) return;
            var candidate = hit.collider.GetComponentInParent<PlayerInventory>();
            var candidateSurvival = candidate != null
                ? candidate.GetComponent<PlayerSurvival>()
                : null;
            var publicState = candidateSurvival?.PublicSymptoms ?? default;
            if (candidateSurvival != null && (publicState.LifeState == CharacterLifeState.Dead
                    || publicState.Sleeping || publicState.Bound
                    || publicState.LifeState is CharacterLifeState.Unconscious
                        or CharacterLifeState.Agonal))
            {
                focusedCorpseInventory = candidate;
            }
        }

        private void RebuildHeldVisual(ushort itemId)
        {
#if UNITY_SERVER
            return;
#else
            if (heldVisual != null) Destroy(heldVisual);
            if (itemId == 0 || catalog == null || !catalog.TryGetItem(itemId, out var item)) return;

            Transform socket;
            if (IsOwner && player.OwnerCamera != null)
            {
                socket = player.OwnerCamera.transform.Find("HeldItemSocket");
                if (socket == null)
                {
                    socket = new GameObject("HeldItemSocket").transform;
                    socket.SetParent(player.OwnerCamera.transform, false);
                    socket.localPosition = new Vector3(0.34f, -0.28f, 0.62f);
                    socket.localRotation = Quaternion.Euler(12f, -18f, 8f);
                }
            }
            else
            {
                socket = transform.Find("RemoteHeldItemSocket");
                if (socket == null)
                {
                    socket = new GameObject("RemoteHeldItemSocket").transform;
                    socket.SetParent(transform, false);
                    socket.localPosition = new Vector3(0.42f, 1.15f, 0.35f);
                    socket.localRotation = Quaternion.Euler(0f, 0f, -20f);
                }
            }

            heldVisual = item.HeldPrefab != null
                ? Instantiate(item.HeldPrefab, socket)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            heldVisual.name = $"Active {item.DisplayName}";
            heldVisual.transform.SetParent(socket, false);
            heldVisual.transform.localPosition = Vector3.zero;
            heldVisual.transform.localRotation = Quaternion.identity;
            heldVisual.transform.localScale = item.Tool switch
            {
                ToolKind.Axe => new Vector3(0.1f, 0.52f, 0.08f),
                ToolKind.Pickaxe => new Vector3(0.58f, 0.1f, 0.12f),
                ToolKind.Shovel => new Vector3(0.2f, 0.62f, 0.1f),
                _ => Vector3.one * 0.18f,
            };
            foreach (var collider in heldVisual.GetComponentsInChildren<Collider>()) Destroy(collider);
            var renderer = heldVisual.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var material = new Material(
                    Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Hidden/InternalErrorShader"));
                material.color = item.PlaceholderColor;
                renderer.material = material;
            }
#endif
        }

        private void OnInventoryListChanged(NetworkListEvent<ItemStackState> change) => Changed?.Invoke();
        private void OnWorkbenchListChanged(NetworkListEvent<ItemStackState> change) => Changed?.Invoke();
        private void OnCursorChanged(ItemStackState previous, ItemStackState current) => Changed?.Invoke();
        private void OnSelectedHotbarChanged(byte previous, byte current) => Changed?.Invoke();
        private void OnActiveItemChanged(ushort previous, ushort current) => RebuildHeldVisual(current);
    }
}
