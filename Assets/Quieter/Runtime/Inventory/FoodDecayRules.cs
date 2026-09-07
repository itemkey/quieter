using UnityEngine;

namespace Quieter.Inventory
{
    public static class FoodDecayRules
    {
        public const float RealSecondsPerGameHour = 300f;

        public static ItemStackState Advance(
            ItemStackState stack,
            ItemDefinition item,
            float elapsedRealSeconds,
            float ambientTemperatureC,
            float humidity)
        {
            if (stack.IsEmpty || item == null || item.Kind != ItemKind.Food
                || item.ShelfLifeGameHours <= 0f || elapsedRealSeconds <= 0f)
            {
                return stack;
            }

            var temperatureFactor = TemperatureFactor(ambientTemperatureC);
            var humidityFactor = Mathf.Lerp(0.85f, 1.25f, Mathf.Clamp01(humidity));
            var shelfLifeSeconds = item.ShelfLifeGameHours * RealSecondsPerGameHour;
            var decay = elapsedRealSeconds / shelfLifeSeconds
                * temperatureFactor * humidityFactor;
            var freshness = Mathf.Clamp01(stack.Freshness / 10000f - decay);
            stack.Freshness = (ushort)Mathf.RoundToInt(freshness * 10000f);

            // Spoilage is observable before every pathogen is: the contamination
            // becomes dangerous progressively instead of flipping at one deadline.
            var spoiled = Mathf.InverseLerp(0.55f, 0f, freshness);
            var biologicalTarget = spoiled * spoiled * 0.92f;
            stack.BiologicalContamination = (ushort)Mathf.Max(
                stack.BiologicalContamination,
                Mathf.RoundToInt(biologicalTarget * 10000f));
            return stack;
        }

        public static float TemperatureFactor(float ambientTemperatureC)
        {
            if (!float.IsFinite(ambientTemperatureC)) return 1f;
            if (ambientTemperatureC <= -2f) return 0.08f;
            if (ambientTemperatureC <= 4f)
                return Mathf.Lerp(0.08f, 0.32f, Mathf.InverseLerp(-2f, 4f, ambientTemperatureC));
            if (ambientTemperatureC <= 22f)
                return Mathf.Lerp(0.32f, 1f, Mathf.InverseLerp(4f, 22f, ambientTemperatureC));
            return Mathf.Lerp(1f, 2.2f, Mathf.InverseLerp(22f, 38f, ambientTemperatureC));
        }
    }
}
