using System;
using System.Collections.Generic;
using Quieter.Inventory;
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
        private readonly List<GenerationResource> baseResources;
        private readonly List<GenerationResource> nonOreResources;

        private readonly struct GenerationResource
        {
            public readonly ushort ItemId;
            public readonly ResourceCategory Category;
            public readonly int Weight;
            public readonly byte MinimumHardness;
            public readonly byte MaximumHardness;
            public readonly ushort PrimaryImpurity;
            public readonly ushort RareImpurity;
            public readonly bool PrefersHighGround;
            public readonly bool PrefersLowGround;
            public readonly bool StronglyRegional;
            public readonly DepositGenerationPool Pool;
            public readonly ToolKind RequiredTool;

            public GenerationResource(WorldObjectCatalog.ResourceEntry entry)
                : this(entry.itemId, entry.category, entry.worldWeight,
                    entry.minimumHardness, entry.maximumHardness,
                    entry.primaryImpurityItemId, entry.rareImpurityItemId,
                    entry.prefersHighGround, entry.prefersLowGround, entry.stronglyRegional,
                    entry.generationPool, entry.requiredTool)
            {
            }

            public GenerationResource(
                ushort itemId,
                ResourceCategory category,
                int weight,
                byte minimumHardness,
                byte maximumHardness,
                ushort primaryImpurity = 0,
                ushort rareImpurity = 0,
                bool prefersHighGround = false,
                bool prefersLowGround = false,
                bool stronglyRegional = false,
                DepositGenerationPool pool = DepositGenerationPool.Base,
                ToolKind requiredTool = ToolKind.Pickaxe)
            {
                ItemId = itemId;
                Category = category;
                Weight = weight;
                MinimumHardness = minimumHardness;
                MaximumHardness = maximumHardness;
                PrimaryImpurity = primaryImpurity;
                RareImpurity = rareImpurity;
                PrefersHighGround = prefersHighGround;
                PrefersLowGround = prefersLowGround;
                StronglyRegional = stronglyRegional;
                Pool = pool;
                RequiredTool = requiredTool;
            }
        }

        public DeterministicChunkGenerator(WorldObjectCatalog catalog = null)
        {
            baseResources = new List<GenerationResource>();
            nonOreResources = new List<GenerationResource>();
            if (catalog?.Resources != null)
            {
                foreach (var entry in catalog.Resources)
                {
                    if (entry != null && entry.itemId != 0 && entry.worldWeight > 0)
                    {
                        var resource = new GenerationResource(entry);
                        (resource.Pool == DepositGenerationPool.NonOre
                            ? nonOreResources
                            : baseResources).Add(resource);
                    }
                }
            }

            if (baseResources.Count == 0)
            {
                AddFallbackBaseResources(baseResources);
            }
            if (nonOreResources.Count == 0)
            {
                AddFallbackNonOreResources(nonOreResources);
            }
        }

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

        public IReadOnlyList<WorldObjectSpawn> GenerateObjectsForMap(
            WorldDefinition definition,
            ChunkCoord coordinate) => GenerateObjects(definition, coordinate);

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
                hash = (hash ^ unchecked((uint)BitConverter.SingleToInt32Bits(spawn.Scale.x))) * prime;
                hash = (hash ^ unchecked((uint)BitConverter.SingleToInt32Bits(spawn.Scale.y))) * prime;
                hash = (hash ^ unchecked((uint)BitConverter.SingleToInt32Bits(spawn.Scale.z))) * prime;
                hash = (hash ^ (byte)spawn.Resource.Kind) * prime;
                hash = (hash ^ spawn.Resource.ResourceItemId) * prime;
                hash = (hash ^ (byte)spawn.Resource.Richness) * prime;
                hash = (hash ^ spawn.Resource.InitialReserves) * prime;
                hash = (hash ^ (byte)spawn.Resource.Quality) * prime;
                hash = (hash ^ spawn.Resource.Hardness) * prime;
                hash = (hash ^ spawn.Resource.ImpurityItemId) * prime;
                hash = (hash ^ (byte)spawn.Resource.RequiredTool) * prime;
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

        private List<WorldObjectSpawn> GenerateObjects(
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
                var geometryHash = Hash64(
                    definition.Seed ^ 0x517CC1B727220A95L,
                    coordinate.X,
                    coordinate.Z,
                    slot);
                var heightWeight = (geometryHash & 0xFFFFUL) / 65535f;
                var widthWeight = ((geometryHash >> 16) & 0xFFFFUL) / 65535f;
                var depthWeight = ((geometryHash >> 32) & 0xFFFFUL) / 65535f;
                var height = type.Value == 1
                    ? Mathf.Lerp(0.45f, 0.9f, heightWeight)
                    : Mathf.Lerp(1.6f, 2.25f, heightWeight);
                var width = type.Value == 1
                    ? Mathf.Lerp(1.1f, 1.8f, widthWeight)
                    : Mathf.Lerp(1.1f, 2f, widthWeight);
                var depth = type.Value == 1
                    ? Mathf.Lerp(1.1f, 1.8f, depthWeight)
                    : Mathf.Lerp(1.1f, 2f, depthWeight);

                result.Add(new WorldObjectSpawn(
                    hash,
                    type,
                    new Vector3(worldX, worldY + height * 0.5f, worldZ),
                    Quaternion.Euler(0f, (hash & 0xFF) / 255f * 360f, 0f),
                    new Vector3(width, height, depth)));
            }

            GenerateStarterForage(definition, coordinate, result);
            GenerateLooseForage(definition, coordinate, result);
            GenerateStoneOutcrop(definition, coordinate, result);
            GenerateDeposit(definition, coordinate, result);
            GenerateNonOreDeposit(definition, coordinate, result);

            return result;
        }

        private static void GenerateStarterForage(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ICollection<WorldObjectSpawn> result)
        {
            const int stoneCount = 24;
            const int branchCount = 16;
            var total = stoneCount + branchCount;
            for (var index = 0; index < total; index++)
            {
                var angle = index * 137.50776f * Mathf.Deg2Rad;
                var radius = 14f + (index % 5) * 4f + ((index * 17) % 7) * 0.25f;
                var worldX = Mathf.Cos(angle) * radius;
                var worldZ = Mathf.Sin(angle) * radius;
                if (!WorldPointBelongsToChunk(definition, coordinate, worldX, worldZ)) continue;
                var itemId = index < stoneCount ? (ushort)1 : (ushort)2;
                var instanceId = Hash64(
                    definition.Seed ^ 0x61C8864680B583EBL,
                    index,
                    itemId,
                    0);
                AddLoosePickup(definition, result, instanceId, itemId, worldX, worldZ);
            }
        }

        private static void GenerateLooseForage(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ICollection<WorldObjectSpawn> result)
        {
            var hash = Hash64(definition.Seed ^ 0x13579BDF2468ACE0L,
                coordinate.X, coordinate.Z, 0);
            if (hash % 100UL >= 42UL) return;
            var minimum = definition.WorldMinimum;
            var worldX = minimum.x + coordinate.X * definition.ChunkSize
                + 3f + ((hash >> 8) & 0xFFFFUL) / 65535f * (definition.ChunkSize - 6f);
            var worldZ = minimum.z + coordinate.Z * definition.ChunkSize
                + 3f + ((hash >> 24) & 0xFFFFUL) / 65535f * (definition.ChunkSize - 6f);
            if (worldX * worldX + worldZ * worldZ < 38f * 38f) return;
            var itemId = ((hash >> 48) & 0xFFUL) < 154UL ? (ushort)1 : (ushort)2;
            AddLoosePickup(definition, result, hash, itemId, worldX, worldZ);
        }

        private static void AddLoosePickup(
            WorldDefinition definition,
            ICollection<WorldObjectSpawn> result,
            ulong instanceId,
            ushort itemId,
            float worldX,
            float worldZ)
        {
            var worldY = SampleWorldHeight(definition, worldX, worldZ);
            var descriptor = new ResourceNodeDescriptor(
                WorldObjectKind.LoosePickup,
                itemId,
                ResourceCategory.Forage,
                DepositRichness.Ordinary,
                DepositReserveSize.VerySmall,
                1,
                ResourceQuality.None,
                1,
                respawnSeconds: 600);
            var scale = itemId == 1
                ? new Vector3(0.34f, 0.22f, 0.3f)
                : new Vector3(0.62f, 0.12f, 0.12f);
            result.Add(new WorldObjectSpawn(
                instanceId,
                new WorldObjectTypeId(0),
                new Vector3(worldX, worldY + scale.y * 0.5f, worldZ),
                Quaternion.Euler(itemId == 2 ? 8f : 0f, (instanceId & 0xFFUL) / 255f * 360f, itemId == 2 ? 82f : 0f),
                scale,
                descriptor));
        }

        private static void GenerateStoneOutcrop(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ICollection<WorldObjectSpawn> result)
        {
            var hash = Hash64(definition.Seed ^ 0x243F6A8885A308D3L,
                coordinate.X, coordinate.Z, 0);
            if (hash % 100UL >= 15UL) return;
            var position = CandidatePosition(definition, coordinate, hash, 5f);
            if (position.x * position.x + position.z * position.z < 38f * 38f) return;
            position.y = SampleWorldHeight(definition, position.x, position.z);
            var size = (DepositReserveSize)((hash >> 42) % 4UL);
            var reserves = ResourceBalance.ReserveUnits[(int)size];
            var scaleValue = 0.95f + (int)size * 0.18f + ((hash >> 50) & 0xFFUL) / 255f * 0.25f;
            var descriptor = new ResourceNodeDescriptor(
                WorldObjectKind.StoneOutcrop,
                1,
                ResourceCategory.Stone,
                DepositRichness.Exceptional,
                size,
                reserves,
                ResourceQuality.None,
                2);
            result.Add(new WorldObjectSpawn(
                hash,
                new WorldObjectTypeId(0),
                position + Vector3.up * scaleValue * 0.42f,
                Quaternion.Euler(0f, (hash & 0xFFUL) / 255f * 360f, 0f),
                new Vector3(scaleValue * 1.2f, scaleValue * 0.84f, scaleValue),
                descriptor));
        }

        private void GenerateDeposit(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ICollection<WorldObjectSpawn> result)
        {
            var hash = Hash64(definition.Seed ^ unchecked((long)0xA4093822299F31D0UL),
                coordinate.X, coordinate.Z, 0);
            var guaranteed = GuaranteedResourceIndex(definition, coordinate);
            if (guaranteed < 0 && hash % 1000UL >= 300UL) return;
            var position = CandidatePosition(definition, coordinate, hash ^ 0x9E3779B97F4A7C15UL, 7f);
            if (position.x * position.x + position.z * position.z < 38f * 38f)
            {
                if (guaranteed < 0) return;
                position = GuaranteedPositionOutsideStarterRing(definition, coordinate, hash);
            }
            position.y = SampleWorldHeight(definition, position.x, position.z);
            var resourceIndex = guaranteed >= 0
                ? guaranteed
                : SelectResourceIndex(definition, coordinate, position.y, hash >> 12);
            var resource = baseResources[Mathf.Clamp(resourceIndex, 0, baseResources.Count - 1)];
            var richness = SelectFiveLevel((hash >> 22) & 0xFFUL);
            var reserveSize = (DepositReserveSize)(byte)SelectFiveLevel((hash >> 30) & 0xFFUL);
            var quality = (ResourceQuality)(1 + SelectFiveLevel((hash >> 38) & 0xFFUL));
            var hardnessSpan = Mathf.Max(0, resource.MaximumHardness - resource.MinimumHardness);
            var hardness = (byte)(resource.MinimumHardness
                + (hardnessSpan == 0 ? 0 : ((hash >> 46) % (ulong)(hardnessSpan + 1))));
            var impurity = (ushort)0;
            var impurityChance = (byte)0;
            var impurityRoll = (hash >> 53) & 0x7FUL;
            if (resource.RareImpurity != 0 && impurityRoll < 8UL)
            {
                impurity = resource.RareImpurity;
            }
            else if (resource.PrimaryImpurity != 0 && impurityRoll < 43UL)
            {
                impurity = resource.PrimaryImpurity;
            }

            if (impurity != 0)
            {
                impurityChance = impurityRoll % 10UL < 5UL ? (byte)1
                    : impurityRoll % 10UL < 9UL ? (byte)3 : (byte)5;
            }

            var descriptor = new ResourceNodeDescriptor(
                WorldObjectKind.Deposit,
                resource.ItemId,
                resource.Category,
                richness,
                reserveSize,
                ResourceBalance.ReserveUnits[(int)reserveSize],
                quality,
                hardness,
                impurity,
                impurityChance,
                requiredTool: resource.RequiredTool);
            var noisySize = 0.92f + (int)reserveSize * 0.22f
                + ((hash >> 8) & 0xFFUL) / 255f * 0.35f;
            result.Add(new WorldObjectSpawn(
                hash,
                new WorldObjectTypeId(0),
                position + Vector3.up * noisySize * 0.48f,
                Quaternion.Euler(0f, (hash & 0xFFUL) / 255f * 360f, 0f),
                new Vector3(noisySize * 1.25f, noisySize, noisySize * 1.08f),
                descriptor));
        }

        private void GenerateNonOreDeposit(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ICollection<WorldObjectSpawn> result)
        {
            var hash = Hash64(definition.Seed ^ unchecked((long)0xD1B54A32D192ED03UL),
                coordinate.X, coordinate.Z, 0);
            var guaranteed = GuaranteedNonOreResourceIndex(definition, coordinate);
            if (guaranteed < 0 && hash % 1000UL >= 128UL) return;

            var positionHash = hash ^ 0x94D049BB133111EBUL;
            var position = default(Vector3);
            var foundPosition = false;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var candidateHash = Hash64((long)positionHash, coordinate.X, coordinate.Z, attempt);
                position = CandidatePosition(definition, coordinate, candidateHash, 7f);
                if (position.x * position.x + position.z * position.z < 38f * 38f) continue;
                if (!IsFarEnoughFromObjects(position, result, 6f)) continue;
                foundPosition = true;
                break;
            }
            if (!foundPosition && guaranteed >= 0)
            {
                var minimum = definition.WorldMinimum;
                var chunkMinimumX = minimum.x + coordinate.X * definition.ChunkSize;
                var chunkMinimumZ = minimum.z + coordinate.Z * definition.ChunkSize;
                var start = (int)((hash >> 7) & 0xFFUL);
                for (var attempt = 0; attempt < 256; attempt++)
                {
                    // 73 is coprime with 256, so every cell is visited exactly once.
                    var slot = (start + attempt * 73) & 0xFF;
                    var cellX = slot & 15;
                    var cellZ = slot >> 4;
                    position = new Vector3(
                        chunkMinimumX + (cellX + 0.5f) * definition.ChunkSize / 16f,
                        0f,
                        chunkMinimumZ + (cellZ + 0.5f) * definition.ChunkSize / 16f);
                    if (position.x * position.x + position.z * position.z < 38f * 38f) continue;
                    if (!IsFarEnoughFromObjects(position, result, 6f)) continue;
                    foundPosition = true;
                    break;
                }
            }
            if (!foundPosition) return;

            position.y = SampleWorldHeight(definition, position.x, position.z);
            var resourceIndex = guaranteed >= 0
                ? guaranteed
                : SelectNonOreResourceIndex(
                    definition, coordinate, position.y, hash >> 12);
            var resource = nonOreResources[Mathf.Clamp(
                resourceIndex, 0, nonOreResources.Count - 1)];
            var richness = SelectFiveLevel((hash >> 22) & 0xFFUL);
            var reserveSize = (DepositReserveSize)(byte)SelectFiveLevel((hash >> 30) & 0xFFUL);
            var quality = (ResourceQuality)(1 + SelectFiveLevel((hash >> 38) & 0xFFUL));
            var hardnessSpan = Mathf.Max(0, resource.MaximumHardness - resource.MinimumHardness);
            var hardness = (byte)(resource.MinimumHardness
                + (hardnessSpan == 0 ? 0 : ((hash >> 46) % (ulong)(hardnessSpan + 1))));
            var impurity = (ushort)0;
            var impurityChance = (byte)0;
            var impurityRoll = (hash >> 53) & 0x7FUL;
            if (resource.RareImpurity != 0 && impurityRoll < 8UL)
            {
                impurity = resource.RareImpurity;
            }
            else if (resource.PrimaryImpurity != 0 && impurityRoll < 43UL)
            {
                impurity = resource.PrimaryImpurity;
            }
            if (impurity != 0)
            {
                impurityChance = impurityRoll % 10UL < 5UL ? (byte)1
                    : impurityRoll % 10UL < 9UL ? (byte)3 : (byte)5;
            }

            var descriptor = new ResourceNodeDescriptor(
                WorldObjectKind.Deposit,
                resource.ItemId,
                resource.Category,
                richness,
                reserveSize,
                ResourceBalance.ReserveUnits[(int)reserveSize],
                quality,
                hardness,
                impurity,
                impurityChance,
                requiredTool: resource.RequiredTool);
            var noisySize = 0.86f + (int)reserveSize * 0.2f
                + ((hash >> 8) & 0xFFUL) / 255f * 0.3f;
            var scale = resource.ItemId switch
            {
                17 => new Vector3(noisySize * 1.45f, noisySize * 0.72f, noisySize * 1.08f),
                18 => new Vector3(noisySize * 1.55f, noisySize * 0.44f, noisySize * 1.35f),
                19 => new Vector3(noisySize, noisySize * 1.12f, noisySize),
                20 => new Vector3(noisySize * 1.2f, noisySize * 0.78f, noisySize),
                21 => new Vector3(noisySize * 1.28f, noisySize, noisySize * 1.12f),
                _ => Vector3.one * noisySize,
            };
            result.Add(new WorldObjectSpawn(
                hash,
                new WorldObjectTypeId(0),
                position + Vector3.up * scale.y * 0.48f,
                Quaternion.Euler(0f, (hash & 0xFFUL) / 255f * 360f, 0f),
                scale,
                descriptor));
        }

        private int SelectResourceIndex(
            WorldDefinition definition,
            ChunkCoord coordinate,
            float worldHeight,
            ulong roll) => SelectResourceIndex(
                definition, coordinate, worldHeight, roll, baseResources,
                0x082EFA98EC4E6C89L);

        private static int SelectResourceIndex(
            WorldDefinition definition,
            ChunkCoord coordinate,
            float worldHeight,
            ulong roll,
            IReadOnlyList<GenerationResource> choices,
            long regionSalt)
        {
            var preferred = (int)(Hash64(
                definition.Seed ^ regionSalt,
                coordinate.X / 4,
                coordinate.Z / 4,
                0) % (ulong)choices.Count);
            var weights = new int[choices.Count];
            var total = 0;
            for (var index = 0; index < choices.Count; index++)
            {
                var resource = choices[index];
                var weight = resource.Weight;
                if (index == preferred)
                {
                    weight *= resource.StronglyRegional ? 5 : 2;
                }
                if (resource.PrefersHighGround)
                {
                    weight = worldHeight >= 18f ? weight * 3 : Mathf.Max(1, weight / 3);
                }
                if (resource.PrefersLowGround)
                {
                    weight = worldHeight <= 12f ? weight * 3 : Mathf.Max(1, weight / 3);
                }
                weights[index] = Mathf.Max(1, weight);
                total += weights[index];
            }

            var selected = (int)(roll % (ulong)total);
            for (var index = 0; index < weights.Length; index++)
            {
                if (selected < weights[index]) return index;
                selected -= weights[index];
            }
            return 0;
        }

        private int SelectNonOreResourceIndex(
            WorldDefinition definition,
            ChunkCoord coordinate,
            float worldHeight,
            ulong roll)
        {
            var preferred = (int)(Hash64(
                definition.Seed ^ 0x632BE59BD9B4E019L,
                coordinate.X / 4,
                coordinate.Z / 4,
                0) % (ulong)nonOreResources.Count);
            var weights = new int[nonOreResources.Count];
            var total = 0;
            for (var index = 0; index < nonOreResources.Count; index++)
            {
                var resource = nonOreResources[index];
                var weight = resource.Weight;
                if (index == preferred)
                {
                    weight *= resource.StronglyRegional ? 5 : 2;
                }

                // New deposits should prefer suitable geology without disappearing
                // from the rest of the map. This keeps the requested world totals
                // stable while still forming lowland and mountain concentrations.
                if (resource.PrefersHighGround && worldHeight >= 10f) weight *= 2;
                if (resource.PrefersLowGround && worldHeight <= 7f) weight *= 2;
                weights[index] = Mathf.Max(1, weight);
                total += weights[index];
            }

            var selected = (int)(roll % (ulong)total);
            for (var index = 0; index < weights.Length; index++)
            {
                if (selected < weights[index]) return index;
                selected -= weights[index];
            }
            return 0;
        }

        private int GuaranteedResourceIndex(WorldDefinition definition, ChunkCoord coordinate)
        {
            var totalChunks = definition.ChunkCountX * definition.ChunkCountZ;
            var occupied = new HashSet<int>();
            for (var resourceIndex = 0; resourceIndex < baseResources.Count; resourceIndex++)
            {
                var candidate = (int)(Hash64(
                    definition.Seed ^ 0x452821E638D01377L,
                    resourceIndex,
                    0,
                    0) % (ulong)totalChunks);
                var step = 97 + resourceIndex * 2;
                while (!occupied.Add(candidate))
                {
                    candidate = (candidate + step) % totalChunks;
                }
                if (candidate % definition.ChunkCountX == coordinate.X
                    && candidate / definition.ChunkCountX == coordinate.Z)
                {
                    return resourceIndex;
                }
            }
            return -1;
        }

        private int GuaranteedNonOreResourceIndex(
            WorldDefinition definition,
            ChunkCoord coordinate)
        {
            var totalChunks = definition.ChunkCountX * definition.ChunkCountZ;
            var occupied = new HashSet<int>();
            for (var resourceIndex = 0; resourceIndex < nonOreResources.Count; resourceIndex++)
            {
                var candidate = (int)(Hash64(
                    definition.Seed ^ unchecked((long)0xE7037ED1A0B428DBUL),
                    resourceIndex, 0, 0) % (ulong)totalChunks);
                var step = 83 + resourceIndex * 2;
                while (!occupied.Add(candidate)) candidate = (candidate + step) % totalChunks;
                if (candidate % definition.ChunkCountX == coordinate.X
                    && candidate / definition.ChunkCountX == coordinate.Z)
                {
                    return resourceIndex;
                }
            }
            return -1;
        }

        private static bool IsFarEnoughFromObjects(
            Vector3 candidate,
            IEnumerable<WorldObjectSpawn> existing,
            float minimumDistance)
        {
            var minimumSquared = minimumDistance * minimumDistance;
            foreach (var spawn in existing)
            {
                var dx = candidate.x - spawn.Position.x;
                var dz = candidate.z - spawn.Position.z;
                if (dx * dx + dz * dz < minimumSquared) return false;
            }
            return true;
        }

        private static Vector3 GuaranteedPositionOutsideStarterRing(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ulong hash)
        {
            var minimum = definition.WorldMinimum;
            var chunkMinimumX = minimum.x + coordinate.X * definition.ChunkSize;
            var chunkMinimumZ = minimum.z + coordinate.Z * definition.ChunkSize;
            var chooseMaximumX = ((hash >> 5) & 1UL) != 0UL;
            var chooseMaximumZ = ((hash >> 6) & 1UL) != 0UL;
            return new Vector3(
                chunkMinimumX + (chooseMaximumX ? definition.ChunkSize - 7f : 7f),
                0f,
                chunkMinimumZ + (chooseMaximumZ ? definition.ChunkSize - 7f : 7f));
        }

        private static DepositRichness SelectFiveLevel(ulong roll)
        {
            var value = roll % 100UL;
            return value < 10UL ? DepositRichness.Scant
                : value < 35UL ? DepositRichness.Poor
                : value < 75UL ? DepositRichness.Ordinary
                : value < 95UL ? DepositRichness.Rich
                : DepositRichness.Exceptional;
        }

        private static Vector3 CandidatePosition(
            WorldDefinition definition,
            ChunkCoord coordinate,
            ulong hash,
            float margin)
        {
            var minimum = definition.WorldMinimum;
            return new Vector3(
                minimum.x + coordinate.X * definition.ChunkSize + margin
                    + ((hash >> 8) & 0xFFFFUL) / 65535f * (definition.ChunkSize - margin * 2f),
                0f,
                minimum.z + coordinate.Z * definition.ChunkSize + margin
                    + ((hash >> 24) & 0xFFFFUL) / 65535f * (definition.ChunkSize - margin * 2f));
        }

        private static float SampleWorldHeight(
            WorldDefinition definition,
            float worldX,
            float worldZ)
        {
            var minimum = definition.WorldMinimum;
            var sampleX = Mathf.RoundToInt((worldX - minimum.x) / definition.SampleSpacing);
            var sampleZ = Mathf.RoundToInt((worldZ - minimum.z) / definition.SampleSpacing);
            return SampleQuantizedHeight(definition, sampleX, sampleZ) * definition.HeightStep;
        }

        private static bool WorldPointBelongsToChunk(
            WorldDefinition definition,
            ChunkCoord coordinate,
            float worldX,
            float worldZ)
        {
            var minimum = definition.WorldMinimum;
            var chunkX = Mathf.FloorToInt((worldX - minimum.x) / definition.ChunkSize);
            var chunkZ = Mathf.FloorToInt((worldZ - minimum.z) / definition.ChunkSize);
            return chunkX == coordinate.X && chunkZ == coordinate.Z;
        }

        private static void AddFallbackBaseResources(ICollection<GenerationResource> target)
        {
            target.Add(new GenerationResource(7, ResourceCategory.MetallicOre, 70, 2, 5));
            target.Add(new GenerationResource(8, ResourceCategory.MetallicOre, 50, 2, 4, 11, 12));
            target.Add(new GenerationResource(9, ResourceCategory.MetallicOre, 30, 2, 4, 8));
            target.Add(new GenerationResource(10, ResourceCategory.MetallicOre, 30, 2, 4, 11));
            target.Add(new GenerationResource(11, ResourceCategory.MetallicOre, 15, 3, 5, 10, 12, true));
            target.Add(new GenerationResource(12, ResourceCategory.MetallicOre, 4, 3, 5, 11, 0, true, false, true));
            target.Add(new GenerationResource(13, ResourceCategory.Coal, 55, 1, 3, 15, 0, false, false, true));
            target.Add(new GenerationResource(14, ResourceCategory.Salt, 24, 1, 3, 0, 0, false, true, true));
            target.Add(new GenerationResource(15, ResourceCategory.Mineral, 18, 1, 3, 0, 16, true));
            target.Add(new GenerationResource(16, ResourceCategory.MetallicOre, 6, 2, 4, 15, 0, true, false, true));
        }

        private static void AddFallbackNonOreResources(ICollection<GenerationResource> target)
        {
            target.Add(new GenerationResource(17, ResourceCategory.NonOre, 45, 2, 4, 20, 0,
                false, false, true, DepositGenerationPool.NonOre));
            target.Add(new GenerationResource(18, ResourceCategory.NonOre, 40, 1, 2, 0, 0,
                false, true, true, DepositGenerationPool.NonOre, ToolKind.Shovel));
            target.Add(new GenerationResource(19, ResourceCategory.NonOre, 18, 1, 3, 0, 15,
                false, false, true, DepositGenerationPool.NonOre));
            target.Add(new GenerationResource(20, ResourceCategory.NonOre, 25, 2, 4, 0, 0,
                false, false, true, DepositGenerationPool.NonOre));
            target.Add(new GenerationResource(21, ResourceCategory.NonOre, 8, 3, 5, 0, 0,
                true, false, true, DepositGenerationPool.NonOre));
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
