using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.Inventory
{
    [CreateAssetMenu(menuName = "Quieter/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<ItemDefinition> items = new();
        [SerializeField] private List<CraftingRecipe> recipes = new();
        private Dictionary<ushort, ItemDefinition> itemLookup;
        private Dictionary<ushort, CraftingRecipe> recipeLookup;
        private Dictionary<CraftingCategory, List<CraftingRecipe>> recipesByCategory;

        public IReadOnlyList<ItemDefinition> Items => items;
        public IReadOnlyList<CraftingRecipe> Recipes => recipes;

        public bool TryGetItem(ushort itemId, out ItemDefinition definition)
        {
            BuildLookup();
            return itemLookup.TryGetValue(itemId, out definition);
        }

        public bool TryGetRecipe(ushort recipeId, out CraftingRecipe recipe)
        {
            BuildLookup();
            return recipeLookup.TryGetValue(recipeId, out recipe);
        }

        public IReadOnlyList<CraftingRecipe> GetRecipes(CraftingCategory category)
        {
            BuildLookup();
            return recipesByCategory.TryGetValue(category, out var matches)
                ? matches
                : Array.Empty<CraftingRecipe>();
        }

        public ItemDefinition GetItem(ushort itemId) =>
            TryGetItem(itemId, out var definition) ? definition : null;

        private void BuildLookup()
        {
            if (itemLookup != null && recipeLookup != null && recipesByCategory != null) return;
            itemLookup = new Dictionary<ushort, ItemDefinition>();
            foreach (var item in items)
            {
                if (item != null && item.ItemId != 0) itemLookup[item.ItemId] = item;
            }

            recipeLookup = new Dictionary<ushort, CraftingRecipe>();
            recipesByCategory = new Dictionary<CraftingCategory, List<CraftingRecipe>>();
            foreach (var recipe in recipes)
            {
                if (recipe == null || recipe.RecipeId == 0) continue;
                recipeLookup[recipe.RecipeId] = recipe;
                if (!recipesByCategory.TryGetValue(recipe.Category, out var categoryRecipes))
                {
                    categoryRecipes = new List<CraftingRecipe>();
                    recipesByCategory[recipe.Category] = categoryRecipes;
                }
                categoryRecipes.Add(recipe);
            }
        }

#if UNITY_EDITOR
        public void Configure(IReadOnlyList<ItemDefinition> definitions,
            IReadOnlyList<CraftingRecipe> craftingRecipes)
        {
            items = new List<ItemDefinition>(definitions);
            recipes = new List<CraftingRecipe>(craftingRecipes);
            itemLookup = null;
            recipeLookup = null;
            recipesByCategory = null;
        }
#endif
    }
}
