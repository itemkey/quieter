using System;
using UnityEngine;

namespace Quieter.Survival
{
    [Serializable]
    public readonly struct WorldWeatherSnapshot
    {
        public WorldWeatherSnapshot(
            double gameSeconds,
            float temperatureC,
            float humidity,
            float precipitation,
            float windMetersPerSecond,
            Vector2 windDirection)
        {
            GameSeconds = gameSeconds;
            TemperatureC = temperatureC;
            Humidity = humidity;
            Precipitation = precipitation;
            WindMetersPerSecond = windMetersPerSecond;
            WindDirection = windDirection;
        }

        public double GameSeconds { get; }
        public float TemperatureC { get; }
        public float Humidity { get; }
        public float Precipitation { get; }
        public float WindMetersPerSecond { get; }
        public Vector2 WindDirection { get; }
        public float DayFraction => Mathf.Repeat((float)(GameSeconds / 86400d), 1f);
    }

    /// <summary>
    /// A continuous world clock and seed-driven weather front model. It is based
    /// on UTC epoch time, so an unattended server advances naturally across restarts.
    /// </summary>
    public sealed class WorldWeatherService : MonoBehaviour
    {
        public const double GameSecondsPerRealSecond = 12d;
        private const long EpochTicks = 638712864000000000L; // 2025-01-01 UTC

        private long worldSeed;
        private bool initialized;
        private Light sun;
        private Color initialFogColor;
        private float initialFogDensity;
        private Color initialAmbientLight;
        private bool presentationCaptured;

        public WorldWeatherSnapshot Current { get; private set; }

        public void Initialize(long seed)
        {
            worldSeed = seed;
            initialized = true;
            Current = Sample(Vector3.zero, UtcGameSeconds());
            CapturePresentation();
            ApplyPresentation(Current);
        }

        private void Update()
        {
            if (initialized)
            {
                Current = Sample(Vector3.zero, UtcGameSeconds());
                ApplyPresentation(Current);
            }
        }

        public SurvivalEnvironment GetEnvironment(Vector3 worldPosition)
        {
            if (!initialized)
            {
                return SurvivalEnvironment.Temperate;
            }

            var weather = Sample(worldPosition, UtcGameSeconds());
            return new SurvivalEnvironment(
                weather.TemperatureC,
                weather.WindMetersPerSecond,
                weather.Humidity,
                weather.Precipitation,
                0.2f,
                0f,
                false,
                dayFraction: weather.DayFraction);
        }

        public WorldWeatherSnapshot Sample(Vector3 worldPosition, double gameSeconds)
            => Sample(worldSeed, worldPosition, gameSeconds);

        public static WorldWeatherSnapshot Sample(
            long seed, Vector3 worldPosition, double gameSeconds)
        {
            var seedOffsetX = (seed & 0xffff) / 977f;
            var seedOffsetY = ((seed >> 16) & 0xffff) / 991f;
            var gameDays = (float)(gameSeconds / 86400d);
            var dayFraction = Mathf.Repeat((float)(gameSeconds / 86400d), 1f);
            var season = Mathf.Sin(gameDays / 36f * Mathf.PI * 2f);
            var solar = Mathf.Sin((dayFraction - 0.25f) * Mathf.PI * 2f);
            var front = Mathf.PerlinNoise(
                seedOffsetX + gameDays * 0.065f + worldPosition.x / 6000f,
                seedOffsetY + worldPosition.z / 6000f);
            var secondaryFront = Mathf.PerlinNoise(
                seedOffsetY + gameDays * 0.12f,
                seedOffsetX + worldPosition.x / 3500f - worldPosition.z / 4200f);
            var humidity = Mathf.Clamp01(front * 0.75f + secondaryFront * 0.35f);
            var precipitation = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 0.9f, humidity));
            var wind = Mathf.Lerp(0.4f, 11f, Mathf.Abs(front - secondaryFront));
            var temperature = 11f + season * 9f + solar * 6.5f
                - precipitation * 3f - worldPosition.y * 0.006f;
            var angle = Mathf.PerlinNoise(
                seedOffsetX + gameDays * 0.025f,
                seedOffsetY + 17f) * Mathf.PI * 2f;
            return new WorldWeatherSnapshot(
                gameSeconds,
                temperature,
                humidity,
                precipitation,
                wind,
                new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
        }

        public static float CalculateDaylight(float dayFraction, float precipitation)
        {
            var solarElevation = Mathf.Sin(
                (Mathf.Repeat(dayFraction, 1f) - 0.25f) * Mathf.PI * 2f);
            var horizonLight = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(-0.14f, 0.28f, solarElevation));
            return horizonLight * Mathf.Lerp(
                1f, 0.58f, Mathf.Clamp01(precipitation));
        }

        private void CapturePresentation()
        {
            if (presentationCaptured || Application.isBatchMode) return;
            presentationCaptured = true;
            initialFogColor = RenderSettings.fogColor;
            initialFogDensity = RenderSettings.fogDensity;
            initialAmbientLight = RenderSettings.ambientLight;
            var lights = FindObjectsByType<Light>();
            foreach (var candidate in lights)
            {
                if (candidate != null && candidate.type == LightType.Directional)
                {
                    sun = candidate;
                    break;
                }
            }
        }

        private void ApplyPresentation(WorldWeatherSnapshot weather)
        {
            if (Application.isBatchMode) return;
            CapturePresentation();
            var daylight = CalculateDaylight(
                weather.DayFraction, weather.Precipitation);
            var night = new Color(0.018f, 0.026f, 0.065f);
            var overcast = new Color(0.28f, 0.31f, 0.34f);
            var clearDay = initialAmbientLight.maxColorComponent > 0.02f
                ? initialAmbientLight
                : new Color(0.62f, 0.65f, 0.68f);
            RenderSettings.ambientLight = Color.Lerp(
                night,
                Color.Lerp(clearDay, overcast, weather.Precipitation),
                daylight);
            RenderSettings.fogColor = Color.Lerp(
                new Color(0.012f, 0.018f, 0.045f),
                Color.Lerp(initialFogColor, overcast, weather.Precipitation * 0.75f),
                daylight);
            RenderSettings.fogDensity = Mathf.Max(
                initialFogDensity,
                Mathf.Lerp(0.0015f, 0.007f, weather.Precipitation)
                    * Mathf.Lerp(1.35f, 1f, daylight));
            if (sun == null) return;
            sun.transform.rotation = Quaternion.Euler(
                weather.DayFraction * 360f - 90f,
                28f + (worldSeed & 31),
                0f);
            sun.intensity = daylight * Mathf.Lerp(0.95f, 0.62f, weather.Precipitation);
            sun.color = Color.Lerp(
                new Color(1f, 0.52f, 0.34f),
                new Color(1f, 0.95f, 0.84f),
                Mathf.SmoothStep(0f, 1f, daylight));
        }

        private static double UtcGameSeconds()
            => (DateTime.UtcNow.Ticks - EpochTicks) / (double)TimeSpan.TicksPerSecond
                * GameSecondsPerRealSecond;
    }
}
