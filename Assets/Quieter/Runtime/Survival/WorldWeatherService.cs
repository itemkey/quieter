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
        public float DayFraction => (float)((GameSeconds % 86400d) / 86400d);
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

        public WorldWeatherSnapshot Current { get; private set; }

        public void Initialize(long seed)
        {
            worldSeed = seed;
            initialized = true;
            Current = Sample(Vector3.zero, UtcGameSeconds());
        }

        private void Update()
        {
            if (initialized)
            {
                Current = Sample(Vector3.zero, UtcGameSeconds());
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
                false);
        }

        public WorldWeatherSnapshot Sample(Vector3 worldPosition, double gameSeconds)
        {
            var seedOffsetX = (worldSeed & 0xffff) / 977f;
            var seedOffsetY = ((worldSeed >> 16) & 0xffff) / 991f;
            var gameDays = (float)(gameSeconds / 86400d);
            var dayFraction = (float)((gameSeconds % 86400d) / 86400d);
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

        private static double UtcGameSeconds()
            => (DateTime.UtcNow.Ticks - EpochTicks) / (double)TimeSpan.TicksPerSecond
                * GameSecondsPerRealSecond;
    }
}
