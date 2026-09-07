using System;
using UnityEngine;

namespace Quieter.Inventory
{
    public enum ItemKind : byte
    {
        Resource = 0,
        Tool = 1,
        HiddenSample = 2,
        Placeable = 3,
        Food = 4,
        LiquidContainer = 5,
        Clothing = 6,
        Medical = 7,
        Document = 8,
        Weapon = 9,
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
        [SerializeField, Min(0f)] private float unitMassKg = 0.2f;
        [SerializeField, Min(0f)] private float unitVolumeLiters = 0.25f;
        [SerializeField, Min(0f)] private float caloriesPerUnit;
        [SerializeField, Min(0f)] private float proteinGramsPerUnit;
        [SerializeField, Min(0f)] private float micronutrientsPerUnit;
        [SerializeField, Min(0f)] private float waterLitersPerUnit;
        [SerializeField, Min(0)] private ushort liquidCapacityMilliliters;
        [SerializeField, Range(0f, 1f)] private float insulation;
        [SerializeField, Range(0f, 1f)] private float waterResistance;
        [SerializeField, Min(0f)] private float shelfLifeGameHours;

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
        public float UnitMassKg => Mathf.Max(0f, unitMassKg);
        public float UnitVolumeLiters => Mathf.Max(0f, unitVolumeLiters);
        public float CaloriesPerUnit => Mathf.Max(0f, caloriesPerUnit);
        public float ProteinGramsPerUnit => Mathf.Max(0f, proteinGramsPerUnit);
        public float MicronutrientsPerUnit => Mathf.Max(0f, micronutrientsPerUnit);
        public float WaterLitersPerUnit => Mathf.Max(0f, waterLitersPerUnit);
        public ushort LiquidCapacityMilliliters => liquidCapacityMilliliters;
        public float Insulation => Mathf.Clamp01(insulation);
        public float WaterResistance => Mathf.Clamp01(waterResistance);
        public float ShelfLifeGameHours => Mathf.Max(0f, shelfLifeGameHours);
        public bool RequiresInstanceId => maximumStack == 1
            || itemKind == ItemKind.LiquidContainer
            || itemKind == ItemKind.Clothing;

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
            ushort configuredMaximumDurability = 0,
            float configuredUnitMassKg = 0.2f,
            float configuredUnitVolumeLiters = 0.25f,
            float configuredCaloriesPerUnit = 0f,
            float configuredProteinGramsPerUnit = 0f,
            float configuredMicronutrientsPerUnit = 0f,
            float configuredWaterLitersPerUnit = 0f,
            ushort configuredLiquidCapacityMilliliters = 0,
            float configuredInsulation = 0f,
            float configuredWaterResistance = 0f,
            float configuredShelfLifeGameHours = 0f)
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
            unitMassKg = Mathf.Max(0f, configuredUnitMassKg);
            unitVolumeLiters = Mathf.Max(0f, configuredUnitVolumeLiters);
            caloriesPerUnit = Mathf.Max(0f, configuredCaloriesPerUnit);
            proteinGramsPerUnit = Mathf.Max(0f, configuredProteinGramsPerUnit);
            micronutrientsPerUnit = Mathf.Max(0f, configuredMicronutrientsPerUnit);
            waterLitersPerUnit = Mathf.Max(0f, configuredWaterLitersPerUnit);
            liquidCapacityMilliliters = configuredLiquidCapacityMilliliters;
            insulation = Mathf.Clamp01(configuredInsulation);
            waterResistance = Mathf.Clamp01(configuredWaterResistance);
            shelfLifeGameHours = Mathf.Max(0f, configuredShelfLifeGameHours);
        }
#endif
    }

}
