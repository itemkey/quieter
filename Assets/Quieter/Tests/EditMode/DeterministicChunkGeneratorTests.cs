using System.IO;
using System.Linq;
using NUnit.Framework;
using Quieter.Core;
using Quieter.Persistence;
using Quieter.World;
using UnityEditor;
using UnityEngine;

namespace Quieter.Tests
{
    public sealed class DeterministicChunkGeneratorTests
    {
        private readonly DeterministicChunkGenerator generator = new();

        [Test]
        public void SameSeedAndCoordinate_HaveStableHashAndObjectIds()
        {
            var definition = WorldDefinition.CreateDefault(0x1234567890ABCDE);
            var first = generator.Generate(definition, new ChunkCoord(12, 19));
            var second = generator.Generate(definition, new ChunkCoord(12, 19));

            Assert.That(generator.CalculateHash(second), Is.EqualTo(generator.CalculateHash(first)));
            Assert.That(
                second.Objects.Select(item => item.InstanceId),
                Is.EqualTo(first.Objects.Select(item => item.InstanceId)));
            var decorations = second.Objects
                .Where(item => item.Resource.Kind == WorldObjectKind.Decoration)
                .ToArray();
            Assert.That(decorations.All(item => item.TypeId.Value is 1 or 2), Is.True);
            foreach (var item in decorations)
            {
                var ground = generator.SampleHeight(
                    definition,
                    item.Position.x,
                    item.Position.z);
                Assert.That(
                    item.Position.y - ground,
                    Is.EqualTo(item.Scale.y * 0.5f).Within(0.0001f));
                Assert.That(item.Scale.x, Is.GreaterThanOrEqualTo(1.1f));
                Assert.That(item.Scale.z, Is.GreaterThanOrEqualTo(1.1f));
                if (item.TypeId.Value == 1)
                {
                    Assert.That(item.Scale.y, Is.InRange(0.45f, 0.9f));
                    Assert.That(item.Scale.x, Is.InRange(1.1f, 1.8f));
                    Assert.That(item.Scale.z, Is.InRange(1.1f, 1.8f));
                }
                else
                {
                    Assert.That(item.Scale.y, Is.InRange(1.6f, 2.25f));
                    Assert.That(item.Scale.x, Is.InRange(1.1f, 2f));
                    Assert.That(item.Scale.z, Is.InRange(1.1f, 2f));
                }
            }
        }

        [Test]
        public void DifferentSeed_ChangesChunkHash()
        {
            var first = generator.Generate(
                WorldDefinition.CreateDefault(123),
                new ChunkCoord(10, 10));
            var second = generator.Generate(
                WorldDefinition.CreateDefault(124),
                new ChunkCoord(10, 10));

            Assert.That(generator.CalculateHash(second), Is.Not.EqualTo(generator.CalculateHash(first)));
        }

        [Test]
        public void ScreenshotReferenceCube_IsTheHighestReachableNearbyCube()
        {
            const long screenshotWorldSeed = 4999677592911300101L;
            const ulong reachableCubeId = 1697769688016525957UL;
            const ulong nextHigherCubeId = 1697769707074391986UL;
            var chunk = generator.Generate(
                WorldDefinition.CreateDefault(screenshotWorldSeed),
                new ChunkCoord(16, 15));
            var reachable = chunk.Objects.Single(item => item.InstanceId == reachableCubeId);
            var nextHigher = chunk.Objects.Single(item => item.InstanceId == nextHigherCubeId);

            Assert.That(reachable.TypeId.Value, Is.EqualTo(2));
            Assert.That(nextHigher.TypeId.Value, Is.EqualTo(2));
            Assert.That(reachable.Scale.y, Is.InRange(1.62f, 1.64f));
            Assert.That(nextHigher.Scale.y, Is.InRange(1.67f, 1.69f));
            Assert.That(reachable.Scale.y, Is.LessThan(nextHigher.Scale.y));
        }

        [Test]
        public void AdjacentChunks_HaveNoHeightSeams()
        {
            var definition = WorldDefinition.CreateDefault(-91234567890123456);
            var center = generator.Generate(definition, new ChunkCoord(15, 15));
            var east = generator.Generate(definition, new ChunkCoord(16, 15));
            var north = generator.Generate(definition, new ChunkCoord(15, 16));
            var last = definition.SamplesPerSide - 1;

            for (var sample = 0; sample <= last; sample++)
            {
                Assert.That(center.HeightAt(last, sample), Is.EqualTo(east.HeightAt(0, sample)));
                Assert.That(center.HeightAt(sample, last), Is.EqualTo(north.HeightAt(sample, 0)));
            }
        }

        [Test]
        public void DefaultWorld_IsExactlyTwoKilometresAndRejectsOutsideChunks()
        {
            var definition = WorldDefinition.CreateDefault(1);

            Assert.That(definition.Width, Is.EqualTo(2048f));
            Assert.That(definition.Depth, Is.EqualTo(2048f));
            Assert.That(definition.Contains(new ChunkCoord(0, 0)), Is.True);
            Assert.That(definition.Contains(new ChunkCoord(31, 31)), Is.True);
            Assert.That(definition.Contains(new ChunkCoord(-1, 0)), Is.False);
            Assert.That(definition.Contains(new ChunkCoord(32, 31)), Is.False);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                generator.Generate(definition, new ChunkCoord(32, 0)));
        }

        [Test]
        public void CentralSafeArea_IsFlattenedAndContainsOnlyStarterResources()
        {
            var definition = WorldDefinition.CreateDefault(424242);
            var center = generator.Generate(definition, new ChunkCoord(16, 16));
            var centerHeight = generator.SampleHeight(definition, 0f, 0f);

            Assert.That(centerHeight, Is.EqualTo(24f * definition.HeightStep));
            Assert.That(
                center.Objects.All(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z >= 38f * 38f
                    || item.Resource.Kind == WorldObjectKind.LoosePickup
                    || item.Resource.Kind == WorldObjectKind.Tree
                    || item.Resource.Kind == WorldObjectKind.FiberPlant),
                Is.True);
        }

        [Test]
        public void CatalogPrefabReplacement_ChangesOnlyPresentation()
        {
            var definition = WorldDefinition.CreateDefault(90125);
            var coordinate = new ChunkCoord(4, 7);
            var originalHash = generator.CalculateHash(generator.Generate(definition, coordinate));
            var catalog = ScriptableObject.CreateInstance<WorldObjectCatalog>();
            var replacement = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject presentation = null;
            try
            {
                catalog.ConfigureDefaults();
                var serialized = new SerializedObject(catalog);
                serialized.FindProperty("entries").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("prefab").objectReferenceValue = replacement;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var spawn = new WorldObjectSpawn(
                    0xAABBCCDD,
                    new WorldObjectTypeId(1),
                    Vector3.one,
                    Quaternion.identity,
                    new Vector3(2f, 3f, 2f));
                presentation = catalog.CreatePresentation(spawn, null);

                Assert.That(presentation.name, Is.EqualTo("WorldObject_1_AABBCCDD"));
                Assert.That(presentation.transform.localScale, Is.EqualTo(spawn.Scale));
                Assert.That(
                    generator.CalculateHash(generator.Generate(definition, coordinate)),
                    Is.EqualTo(originalHash));
            }
            finally
            {
                if (presentation != null) Object.DestroyImmediate(presentation);
                Object.DestroyImmediate(replacement);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ChunkHash_ChangesWhenObjectGeometryChanges()
        {
            var coordinate = new ChunkCoord(2, 3);
            var original = new ChunkData(
                coordinate,
                1,
                new[] { 0 },
                new[]
                {
                    new WorldObjectSpawn(
                        123,
                        new WorldObjectTypeId(1),
                        Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.1f, 0.45f, 1.1f)),
                });
            var resized = new ChunkData(
                coordinate,
                1,
                new[] { 0 },
                new[]
                {
                    new WorldObjectSpawn(
                        123,
                        new WorldObjectTypeId(1),
                        Vector3.zero,
                        Quaternion.identity,
                        new Vector3(1.1f, 0.9f, 1.1f)),
                });

            Assert.That(
                generator.CalculateHash(resized),
                Is.Not.EqualTo(generator.CalculateHash(original)));
        }

        [Test]
        public void LocalWorldMigration_PreservesSeedAndUpgradesGeneratorVersion()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"quieter-world-migration-{System.Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\n"
                    + "  \"HasWorld\": true,\n"
                    + "  \"World\": {\n"
                    + "    \"WorldId\": 1,\n"
                    + "    \"Seed\": 987654321,\n"
                    + "    \"GeneratorVersion\": 1,\n"
                    + "    \"ChunkCountX\": 32,\n"
                    + "    \"ChunkCountZ\": 32,\n"
                    + "    \"ChunkSize\": 64,\n"
                    + "    \"SamplesPerSide\": 33,\n"
                    + "    \"HeightStep\": 0.25\n"
                    + "  },\n"
                    + "  \"Players\": []\n"
                    + "}\n");

                var repository = new LocalJsonRepository(path);
                var world = repository.GetOrCreateWorldAsync().GetAwaiter().GetResult();

                Assert.That(world.Seed, Is.EqualTo(987654321));
                Assert.That(world.GeneratorVersion, Is.EqualTo(QuieterConstants.GeneratorVersion));
                StringAssert.Contains(
                    $"\"GeneratorVersion\": {QuieterConstants.GeneratorVersion}",
                    File.ReadAllText(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (File.Exists(path + ".tmp"))
                {
                    File.Delete(path + ".tmp");
                }
            }
        }

        [Test]
        public void HostWorldStreamer_CreatesVisibleChunksWithColliders()
        {
            var root = new GameObject("WorldStreamerTest");
            try
            {
                var streamer = root.AddComponent<WorldStreamer>();
                streamer.Initialize(
                    WorldDefinition.CreateDefault(73021),
                    null,
                    isServer: true,
                    shouldRenderVisuals: true);

                Assert.That(streamer.ActiveChunkCount, Is.EqualTo(49));
                Assert.That(
                    root.GetComponentsInChildren<MeshRenderer>().Length,
                    Is.EqualTo(49));
                Assert.That(
                    root.GetComponentsInChildren<MeshCollider>().Length,
                    Is.GreaterThanOrEqualTo(49));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
