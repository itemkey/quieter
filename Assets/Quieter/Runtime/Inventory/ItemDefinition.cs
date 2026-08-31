using System;
using UnityEngine;

namespace Quieter.Inventory
{
    public enum ItemKind : byte
    {
        Resource = 0,
        Tool = 1,
        HiddenSample = 2,
    }

    public enum ToolKind : byte
    {
        None = 0,
        Axe = 1,
        Pickaxe = 2,
        Shovel = 3,
    }

    [CreateAssetMenu(menuName = "Quieter/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private ushort itemId = 1;
        [SerializeField] private string displayName = "Предмет";
        [SerializeField, TextArea(2, 5)] private string description = string.Empty;
        [SerializeField, Min(1)] private ushort maximumStack = 10;
        [SerializeField] private PickupPlacementPriority pickupPriority;
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject worldPrefab;
        [SerializeField] private GameObject heldPrefab;
        [SerializeField] private Color placeholderColor = Color.gray;
        [SerializeField] private ItemKind itemKind;
        [SerializeField] private ToolKind toolKind;
        [SerializeField, Min(0)] private byte toolTier;
        [SerializeField, Min(0)] private ushort maximumDurability;

        public ushort ItemId => itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? $"Предмет {itemId}"
            : displayName;
        public string Description => description ?? string.Empty;
        public ushort MaximumStack => (ushort)Mathf.Max(1, maximumStack);
        public PickupPlacementPriority PickupPriority => pickupPriority;
        public Sprite Icon => icon;
        public GameObject WorldPrefab => worldPrefab;
        public GameObject HeldPrefab => heldPrefab;
        public Color PlaceholderColor => placeholderColor;
        public ItemKind Kind => itemKind;
        public ToolKind Tool => toolKind;
        public byte ToolTier => toolTier;
        public ushort MaximumDurability => maximumDurability;
        public bool IsDurable => maximumDurability > 0;

#if UNITY_EDITOR
        public void Configure(
            ushort id,
            string itemName,
            ushort maxStack,
            PickupPlacementPriority priority,
            Color color,
            GameObject worldModel = null,
            GameObject heldModel = null,
            string itemDescription = null,
            ItemKind kind = ItemKind.Resource,
            ToolKind configuredTool = ToolKind.None,
            byte configuredToolTier = 0,
            ushort configuredMaximumDurability = 0)
        {
            itemId = id;
            displayName = itemName;
            maximumStack = (ushort)Mathf.Max(1, maxStack);
            pickupPriority = priority;
            placeholderColor = color;
            worldPrefab = worldModel;
            heldPrefab = heldModel;
            description = itemDescription ?? string.Empty;
            itemKind = kind;
            toolKind = configuredTool;
            toolTier = configuredToolTier;
            maximumDurability = configuredMaximumDurability;
        }
#endif
    }

}
