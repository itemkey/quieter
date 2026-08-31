using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.Inventory
{
    public enum CraftingCategory : byte
    {
        Tools = 0,
    }

    public static class CraftingCategoryNames
    {
        public static string DisplayName(CraftingCategory category) => category switch
        {
            CraftingCategory.Tools => "Инструменты",
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

        public ushort RecipeId => recipeId;
        public string DisplayName => displayName;
        public CraftingCategory Category => category;
        public IReadOnlyList<Ingredient> Ingredients => ingredients;
        public ItemDefinition Output => output;
        public ushort OutputQuantity => outputQuantity;

#if UNITY_EDITOR
        public void Configure(ushort id, string recipeName,
            IReadOnlyList<Ingredient> required, ItemDefinition result, ushort resultQuantity,
            CraftingCategory recipeCategory = CraftingCategory.Tools)
        {
            recipeId = id;
            displayName = recipeName;
            category = recipeCategory;
            ingredients = new List<Ingredient>(required);
            output = result;
            outputQuantity = (ushort)Mathf.Max(1, resultQuantity);
        }
#endif
    }
}
