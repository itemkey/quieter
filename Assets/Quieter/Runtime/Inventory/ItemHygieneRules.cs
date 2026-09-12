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

        public static ItemStackState ApplyEliminationSoiling(
            ItemStackState stack, bool bowelAccident)
        {
            if (stack.IsEmpty) return stack;
            stack.Cleanliness = (ushort)Mathf.Min(
                stack.Cleanliness, bowelAccident ? 1800 : 4300);
            stack.BiologicalContamination = (ushort)Mathf.Max(
                stack.BiologicalContamination, bowelAccident ? 9000 : 6500);
            stack.Wetness = (ushort)Mathf.Max(
                stack.Wetness, bowelAccident ? 6500 : 8500);
            return stack;
        }

        public static bool CanHeatSterilize(ItemStackState stack) => !stack.IsEmpty
            && stack.ItemId == 33
            && stack.LiquidMilliliters == 0
            && stack.Cleanliness >= 6000
            && stack.BiologicalContamination > 0;

        public static ItemStackState HeatSterilize(ItemStackState stack)
        {
            if (!CanHeatSterilize(stack)) return stack;
            // Heating kills organisms on a previously cleaned metal needle, but
            // neither removes visible dirt nor neutralizes mineral/chemical poison.
            stack.BiologicalContamination = 0;
            stack.Wetness = 0;
            return stack;
        }

        public static float ContactContamination(ItemStackState stack)
        {
            if (stack.IsEmpty) return 0f;
            return Mathf.Clamp01(Mathf.Max(
                1f - stack.Cleanliness / 10000f,
                stack.BiologicalContamination / 10000f));
        }

        public static ItemStackState SoilWithBlood(
            ItemStackState stack, float sourceBiologicalLoad)
        {
            if (stack.IsEmpty || !float.IsFinite(sourceBiologicalLoad)) return stack;
            var load = Mathf.Lerp(0.52f, 0.92f, Mathf.Clamp01(sourceBiologicalLoad));
            stack.BiologicalContamination = (ushort)Mathf.Max(
                stack.BiologicalContamination, Mathf.RoundToInt(load * 10000f));
            stack.Cleanliness = (ushort)Mathf.Min(stack.Cleanliness, 6200);
            stack.Wetness = (ushort)Mathf.Max(stack.Wetness, 1800);
            return stack;
        }
    }
}
