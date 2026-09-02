using System;
using System.Collections.Generic;
using System.Collections;
using Quieter.Player;
using Quieter.UI;
using Quieter.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quieter.Inventory
{
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class PlayerInventory : NetworkBehaviour
    {
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
        private readonly List<ItemStackState> serverPendingItems = new();

        private ItemCatalog catalog;
        private GameObject worldItemPrefab;
        private InventoryModel model;
        private NetworkPlayer player;
        private GameObject heldVisual;
        private NetworkWorldItem focusedPickup;
        private PlayerResourceInteraction resourceInteraction;
        private bool interfaceOpen;
        private uint nextMoveRequestId;
        private Coroutine toolSwingRoutine;

        public event Action Changed;
        public event Action ServerInventoryChanged;

        public ItemStackState CursorStack => cursor.Value;
        public byte SelectedHotbarIndex => selectedHotbar.Value;
        public ushort ActiveItemId => activeItemId.Value;
        public ItemCatalog Catalog => catalog;
        public bool IsInterfaceOpen => interfaceOpen;
        public NetworkWorldItem FocusedPickup => focusedPickup;
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

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            resourceInteraction = GetComponent<PlayerResourceInteraction>();
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
            if (!IsSpawned || !IsOwner) return;
            UpdateFocusedPickup();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (ResourceMapView.IsOpen || ResourceMapView.IsDepositOpen) return;

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
            var moved = source.Area == InventorySlotArea.ResearchTable
                    || destination.Area == InventorySlotArea.ResearchTable
                ? resourceInteraction != null && resourceInteraction.TryMoveResearchTableStackServer(
                    source, destination, expectedItemId, quantity)
                : model != null && model.MoveStack(
                    source,
                    destination,
                    expectedItemId,
                    quantity);
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
            var dropped = TryDropStackServer(source, expectedItemId, quantity);
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
            if (model == null || index >= InventoryLayout.HotbarSlotCount) return;
            model.SetSelectedHotbar(index);
            SynchronizeAll();
            ServerInventoryChanged?.Invoke();
        }

        [ServerRpc]
        private void CraftServerRpc(ushort recipeId)
        {
            if (model == null || catalog == null || !catalog.TryGetRecipe(recipeId, out var recipe)) return;
            Mutate(() => model.TryCraft(recipe));
        }

        [ServerRpc]
        private void CloseInterfaceServerRpc()
        {
            Mutate(() => model.NormalizeTemporaryStorage());
        }

        [ServerRpc]
        private void PickupServerRpc(NetworkObjectReference pickupReference)
        {
            if (model == null || !pickupReference.TryGet(out var networkObject)
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

        private void Mutate(Func<bool> mutation)
        {
            if (!IsServer || model == null || mutation == null || !mutation()) return;
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
                sampleId);
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
            var camera = player.OwnerCamera;
            if (!WorldInteractionRaycast.TryGetClosest(
                    camera, transform, ResourceBalance.InteractionDistance, out var hit)) return;
            focusedPickup = hit.collider.GetComponentInParent<NetworkWorldItem>();
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
