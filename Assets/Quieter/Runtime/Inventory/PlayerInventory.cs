using System;
using System.Collections.Generic;
using System.Collections;
using Quieter.Player;
using Quieter.UI;
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

        private ItemCatalog catalog;
        private GameObject worldItemPrefab;
        private InventoryModel model;
        private NetworkPlayer player;
        private GameObject heldVisual;
        private NetworkWorldItem focusedPickup;
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

            return default;
        }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
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

            if (ResourceMapView.IsOpen) return;

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

            var wheel = Mouse.current?.scroll.ReadValue().y ?? 0f;
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
            byte selectedIndex)
        {
            if (!IsServer || catalog == null) return;
            model = new InventoryModel(catalog);
            model.Load(storedSlots, selectedIndex);
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
            interfaceOpen = open;
            InventoryView.SetMode(open, showWorkbench);
            player.SetInventoryInterfaceOpen(open);
            if (!open)
            {
                CloseInterfaceServerRpc();
            }
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
            var moved = model != null && model.MoveStack(
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
                || Vector3.Distance(transform.position, pickup.transform.position) > 3.25f
                || !HasLineOfSight(pickup))
            {
                return;
            }

            if (pickup.TryCollectServer(model, out _))
            {
                SynchronizeAll();
                ServerInventoryChanged?.Invoke();
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
            SynchronizeList(inventory, model.Inventory);
            SynchronizeList(workbench, model.Workbench);
            cursor.Value = model.Cursor.ForReplication();
            selectedHotbar.Value = model.SelectedHotbarIndex;
            activeItemId.Value = model.ActiveStack.IsEmpty ? (ushort)0 : model.ActiveStack.ItemId;
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
            var direction = pickup.transform.position - origin;
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
                return hit.transform == pickup.transform || hit.transform.IsChildOf(pickup.transform);
            }

            return false;
        }

        private void UpdateFocusedPickup()
        {
            focusedPickup = null;
            var camera = player.OwnerCamera;
            if (camera == null) return;
            var hits = Physics.RaycastAll(
                camera.transform.position,
                camera.transform.forward,
                3f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                focusedPickup = hit.collider.GetComponentInParent<NetworkWorldItem>();
                break;
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
