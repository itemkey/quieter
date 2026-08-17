using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.World
{
    /// <summary>
    /// Integer value noise keeps Linux server and Windows client chunk data identical.
    /// All interpolation happens in fixed-point before the height is converted to metres.
    /// </summary>
    public sealed class DeterministicChunkGenerator : IChunkGenerator
    {
        private const int FixedOne = 1 << 16;
        private const int SpawnSlotsPerChunk = 8;

        public ChunkData Generate(WorldDefinition definition, ChunkCoord coordinate)
        {
            if (!definition.Contains(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate), coordinate, "Chunk is outside the world.");
            }

            var side = definition.SamplesPerSide;
            var heights = new int[side * side];
            var samplesPerChunk = side - 1;

            for (var z = 0; z < side; z++)
            {
                for (var x = 0; x < side; x++)
                {
                    var globalX = coordinate.X * samplesPerChunk + x;
                    var globalZ = coordinate.Z * samplesPerChunk + z;
                    heights[z * side + x] = SampleQuantizedHeight(definition, globalX, globalZ);
                }
            }

            var objects = GenerateObjects(definition, coordinate);
            return new ChunkData(coordinate, side, heights, objects);
        }

        public float SampleHeight(WorldDefinition definition, float worldX, float worldZ)
        {
            var minimum = definition.WorldMinimum;
            var sampleX = Mathf.Clamp(
                Mathf.RoundToInt((worldX - minimum.x) / definition.SampleSpacing),
                0,
                definition.ChunkCountX * (definition.SamplesPerSide - 1));
            var sampleZ = Mathf.Clamp(
                Mathf.RoundToInt((worldZ - minimum.z) / definition.SampleSpacing),
                0,
                definition.ChunkCountZ * (definition.SamplesPerSide - 1));
            return SampleQuantizedHeight(definition, sampleX, sampleZ) * definition.HeightStep;
        }

        public ulong CalculateHash(ChunkData data)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;

            hash = (hash ^ (uint)data.Coordinate.X) * prime;
            hash = (hash ^ (uint)data.Coordinate.Z) * prime;
            foreach (var height in data.QuantizedHeights)
            {
                hash = (hash ^ (uint)height) * prime;
            }

            foreach (var spawn in data.Objects)
            {
                hash = (hash ^ spawn.InstanceId) * prime;
                hash = (hash ^ spawn.TypeId.Value) * prime;
            }

            return hash;
        }

        private static int SampleQuantizedHeight(WorldDefinition definition, int globalX, int globalZ)
        {
            var height = 32 * FixedOne;
            height += ValueNoise(definition.Seed, globalX, globalZ, 64) * 20;
            height += ValueNoise(definition.Seed ^ 0x6A09E667F3BCC909L, globalX, globalZ, 24) * 7;
            height += ValueNoise(definition.Seed ^ 0x3C6EF372FE94F82BL, globalX, globalZ, 8) * 2;
            height /= FixedOne;

            var worldSampleWidth = definition.ChunkCountX * (definition.SamplesPerSide - 1);
            var worldSampleDepth = definition.ChunkCountZ * (definition.SamplesPerSide - 1);
            var dx = globalX - worldSampleWidth / 2;
            var dz = globalZ - worldSampleDepth / 2;
            var distanceSquared = dx * dx + dz * dz;
            const int flatRadiusSamples = 18;

            if (distanceSquared < flatRadiusSamples * flatRadiusSamples)
            {
                var radiusSquared = flatRadiusSamples * flatRadiusSamples;
                height = 24 + (height - 24) * distanceSquared / radiusSquared;
            }

            return Mathf.Clamp(height, 4, 192);
        }

        private static int ValueNoise(long seed, int x, int z, int cellSize)
        {
            var cellX = FloorDiv(x, cellSize);
            var cellZ = FloorDiv(z, cellSize);
            var localX = FloorMod(x, cellSize) * FixedOne / cellSize;
            var localZ = FloorMod(z, cellSize) * FixedOne / cellSize;
            var smoothX = Smooth(localX);
            var smoothZ = Smooth(localZ);

            var a = HashSigned(seed, cellX, cellZ);
            var b = HashSigned(seed, cellX + 1, cellZ);
            var c = HashSigned(seed, cellX, cellZ + 1);
            var d = HashSigned(seed, cellX + 1, cellZ + 1);
            return Lerp(Lerp(a, b, smoothX), Lerp(c, d, smoothX), smoothZ);
        }

        private static List<WorldObjectSpawn> GenerateObjects(
            WorldDefinition definition,
            ChunkCoord coordinate)
        {
            var result = new List<WorldObjectSpawn>(SpawnSlotsPerChunk);
            var minimum = definition.WorldMinimum;

            for (var slot = 0; slot < SpawnSlotsPerChunk; slot++)
            {
                var hash = Hash64(definition.Seed, coordinate.X, coordinate.Z, slot);
                if ((hash & 3UL) == 0UL)
                {
                    continue;
                }

                var normalizedX = ((hash >> 8) & 0xFFFF) / 65535f;
                var normalizedZ = ((hash >> 24) & 0xFFFF) / 65535f;
                var worldX = minimum.x + coordinate.X * definition.ChunkSize
                    + 2f + normalizedX * (definition.ChunkSize - 4f);
                var worldZ = minimum.z + coordinate.Z * definition.ChunkSize
                    + 2f + normalizedZ * (definition.ChunkSize - 4f);

                if (worldX * worldX + worldZ * worldZ < 38f * 38f)
                {
                    continue;
                }

                var heightSampleX = Mathf.RoundToInt((worldX - minimum.x) / definition.SampleSpacing);
                var heightSampleZ = Mathf.RoundToInt((worldZ - minimum.z) / definition.SampleSpacing);
                var worldY = SampleQuantizedHeight(definition, heightSampleX, heightSampleZ)
                    * definition.HeightStep;
                var type = new WorldObjectTypeId((ushort)(1 + ((hash >> 48) & 1UL)));
                var scaleValue = 0.75f + ((hash >> 40) & 0xFF) / 255f * 1.5f;

                result.Add(new WorldObjectSpawn(
                    hash,
                    type,
                    new Vector3(worldX, worldY + scaleValue * 0.5f, worldZ),
                    Quaternion.Euler(0f, (hash & 0xFF) / 255f * 360f, 0f),
                    new Vector3(scaleValue, scaleValue, scaleValue)));
            }

            return result;
        }

        private static int Smooth(int value)
        {
            var squared = (long)value * value / FixedOne;
            return (int)(squared * (3L * FixedOne - 2L * value) / FixedOne);
        }

        private static int Lerp(int a, int b, int amount)
        {
            return a + (int)((long)(b - a) * amount / FixedOne);
        }

        private static int HashSigned(long seed, int x, int z)
        {
            var value = Hash64(seed, x, z, 0);
            return (int)(value & 0x1FFFFUL) - 0x10000;
        }

        private static ulong Hash64(long seed, int x, int z, int salt)
        {
            unchecked
            {
                var value = (ulong)seed;
                value ^= (ulong)(x * 0x9E3779B9);
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value ^= (ulong)(z * 0x85EBCA6B);
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= (uint)salt * 0xC2B2AE35UL;
                return value ^ (value >> 31);
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            var quotient = value / divisor;
            var remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int FloorMod(int value, int divisor)
        {
            var remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }
    }
}
