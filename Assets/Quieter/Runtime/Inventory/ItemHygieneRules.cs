using UnityEngine;

namespace Quieter.Inventory
{
    public static class ItemHygieneRules
    {
        public static bool CanRinse(ItemStackState stack, ItemDefinition item) =>
            item != null && !stack.IsEmpty && !stack.Equipped
            && stack.LiquidMilliliters == 0
            && (item.Kind == ItemKind.LiquidContainer || item.Kind == ItemKind.Clothing
                || item.Kind == ItemKind.Tool || stack.ItemId is 32 or 33 or 34);

        public static ItemStackState Rinse(
            ItemStackState stack, ItemDefinition item, float waterBiological, float waterToxins)
        {
            if (!CanRinse(stack, item) || !float.IsFinite(waterBiological)
                || !float.IsFinite(waterToxins)) return stack;
            // Rinsing removes visible dirt and part of the residue. It is not
            // sterilization; source contamination remains on the washed surface.
            stack.Cleanliness = (ushort)Mathf.Max(stack.Cleanliness, 7200);
            stack.BiologicalContamination = (ushort)Mathf.RoundToInt(10000f * Mathf.Max(
                Mathf.Clamp01(waterBiological), stack.BiologicalContamination / 10000f * 0.18f));
            stack.ToxinContamination = (ushort)Mathf.RoundToInt(10000f * Mathf.Max(
                Mathf.Clamp01(waterToxins), stack.ToxinContamination / 10000f * 0.2f));
            stack.LiquidKind = LiquidKind.None;
            if (item.Kind == ItemKind.Clothing || stack.ItemId == 32) stack.Wetness = 10000;
            return stack;
        }

        public static float MedicalMaterialCleanliness(ItemStackState stack) => Mathf.Min(
            stack.Cleanliness / 10000f,
            1f - Mathf.Max(stack.BiologicalContamination, stack.ToxinContamination) / 10000f);
    }
}
