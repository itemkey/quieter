using Unity.Netcode;
using UnityEngine;

namespace Quieter.Inventory
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWorldItem : NetworkBehaviour
    {
        private readonly NetworkVariable<ItemStackState> stack = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private ItemCatalog catalog;
        private GameObject visual;
        private bool beingCollected;
        private ItemStackState serverStack;

        public ItemStackState Stack => stack.Value;

        public override void OnNetworkSpawn()
        {
            catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            stack.OnValueChanged += OnStackChanged;
            RebuildVisual(stack.Value);
        }

        public override void OnNetworkDespawn()
        {
            stack.OnValueChanged -= OnStackChanged;
        }

        public bool InitializeServer(ushort itemId, int quantity)
        {
            return InitializeServer(new ItemStackState(itemId, quantity));
        }

        public bool InitializeServer(ItemStackState value)
        {
            if (!IsServer || catalog == null)
            {
                catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            }

            if (!IsServer || catalog == null || value.IsEmpty
                || !catalog.TryGetItem(value.ItemId, out var item))
            {
                return false;
            }

            if (item.IsDurable && value.Condition == 0)
            {
                value.Condition = item.MaximumDurability;
            }

            serverStack = value;
            stack.Value = value.ForReplication();
            return true;
        }

        public bool TryCollectServer(InventoryModel inventory, out int collected)
        {
            collected = 0;
            if (!IsServer || beingCollected || inventory == null || serverStack.IsEmpty
                || catalog == null || !catalog.TryGetItem(serverStack.ItemId, out var item))
            {
                return false;
            }

            beingCollected = true;
            var current = serverStack;
            var remainder = inventory.AutoInsert(current, item.PickupPriority);
            collected = current.Quantity - remainder;
            if (collected <= 0)
            {
                beingCollected = false;
                return false;
            }

            if (remainder == 0)
            {
                NetworkObject.Despawn(true);
            }
            else
            {
                serverStack = current.WithQuantity(remainder);
                stack.Value = serverStack.ForReplication();
                beingCollected = false;
            }

            return true;
        }

        private void OnStackChanged(ItemStackState previous, ItemStackState current)
        {
            RebuildVisual(current);
        }

        private void RebuildVisual(ItemStackState current)
        {
#if UNITY_SERVER
            return;
#else
            if (visual != null)
            {
                Destroy(visual);
            }

            if (current.IsEmpty || catalog == null
                || !catalog.TryGetItem(current.ItemId, out var item))
            {
                return;
            }

            visual = item.WorldPrefab != null
                ? Instantiate(item.WorldPrefab, transform)
                : GameObject.CreatePrimitive(current.ItemId == 3
                    ? PrimitiveType.Cylinder
                    : PrimitiveType.Cube);
            visual.name = $"{item.DisplayName} Visual";
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = current.ItemId switch
            {
                1 => new Vector3(0.36f, 0.24f, 0.32f),
                2 => new Vector3(0.55f, 0.18f, 0.18f),
                3 => new Vector3(0.32f, 0.1f, 0.32f),
                _ => new Vector3(0.18f, 0.55f, 0.08f),
            };
            foreach (var collider in visual.GetComponentsInChildren<Collider>())
            {
                Destroy(collider);
            }

            var renderer = visual.GetComponentInChildren<Renderer>();
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
    }
}
