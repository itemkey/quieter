using System;
using System.Collections.Generic;
using Quieter.Survival;
using UnityEngine;

namespace Quieter.Inventory
{
    public enum CraftingCategory : byte
    {
        Tools = 0,
        Materials = 1,
        Structures = 2,
    }

    public static class CraftingCategoryNames
    {
        public static string DisplayName(CraftingCategory category) => category switch
        {
            CraftingCategory.Tools => "Инструменты",
            CraftingCategory.Materials => "Материалы",
            CraftingCategory.Structures => "Постройки",
            _ => "Другое",
        };
    }

    [CreateAssetMenu(menuName = "Quieter/Crafting Recipe", fileName = "CraftingRecipe")]
    public sealed class CraftingRecipe : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            public ItemDefinition Item;
            [Min(1)] public ushort Quantity;
        }

        [SerializeField, Min(1)] private ushort recipeId = 1;
        [SerializeField] private string displayName = "Рецепт";
        [SerializeField] private CraftingCategory category = CraftingCategory.Tools;
        [SerializeField] private List<Ingredient> ingredients = new();
        [SerializeField] private ItemDefinition output;
        [SerializeField, Min(1)] private ushort outputQuantity = 1;
        [SerializeField, Min(0f)] private float workSeconds;
        [SerializeField] private bool requiresBurningHearth;
        [SerializeField] private SkillId practiceSkill = SkillId.Toolmaking;
        [SerializeField, Min(0)] private ushort requiredWaterMilliliters;
        [SerializeField, Range(0, 10000)] private ushort outputWetness;

        public ushort RecipeId => recipeId;
        public string DisplayName => displayName;
        public CraftingCategory Category => category;
        public IReadOnlyList<Ingredient> Ingredients => ingredients;
        public ItemDefinition Output => output;
        public ushort OutputQuantity => outputQuantity;
        public float WorkSeconds => Mathf.Max(0f, workSeconds);
        public bool RequiresBurningHearth => requiresBurningHearth;
        public SkillId PracticeSkill => practiceSkill;
        public ushort RequiredWaterMilliliters => requiredWaterMilliliters;
        public ushort OutputWetness => (ushort)Mathf.Min(10000, outputWetness);

#if UNITY_EDITOR
        public void Configure(ushort id, string recipeName,
            IReadOnlyList<Ingredient> required, ItemDefinition result, ushort resultQuantity,
            CraftingCategory recipeCategory = CraftingCategory.Tools,
            float durationSeconds = 0f, bool needsHearth = false,
            SkillId skill = SkillId.Toolmaking,
            ushort waterMilliliters = 0,
            ushort resultWetness = 0)
        {
            recipeId = id;
            displayName = recipeName;
            category = recipeCategory;
            ingredients = new List<Ingredient>(required);
            output = result;
            outputQuantity = (ushort)Mathf.Max(1, resultQuantity);
            workSeconds = Mathf.Max(0f, durationSeconds);
            requiresBurningHearth = needsHearth;
            practiceSkill = skill;
            requiredWaterMilliliters = waterMilliliters;
            outputWetness = (ushort)Mathf.Min(10000, resultWetness);
        }
#endif
    }
}
