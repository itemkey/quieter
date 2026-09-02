using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Quieter.Core;
using Quieter.Inventory;
using Quieter.World;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class ResearchTableAndForestTests
    {
        private readonly List<Object> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in assets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            assets.Clear();
        }

        [Test]
        public void ForestStream_IsDeterministicAndMeetsWorldAndStarterTargets()
        {
            var worldCatalog = ScriptableObject.CreateInstance<WorldObjectCatalog>();
            worldCatalog.ConfigureDefaults();
            assets.Add(worldCatalog);
            var generator = new DeterministicChunkGenerator(worldCatalog);
            var definition = WorldDefinition.CreateDefault(8493027501);
            var trees = new List<WorldObjectSpawn>();
            var plants = new List<WorldObjectSpawn>();
            for (var z = 0; z < definition.ChunkCountZ; z++)
            {
                for (var x = 0; x < definition.ChunkCountX; x++)
                {
                    var first = generator.GenerateObjectsForMap(
                        definition, new ChunkCoord(x, z));
                    var second = generator.GenerateObjectsForMap(
                        definition, new ChunkCoord(x, z));
                    Assert.That(first.Select(item => item.InstanceId),
                        Is.EqualTo(second.Select(item => item.InstanceId)));
                    trees.AddRange(first.Where(item => item.Resource.Kind == WorldObjectKind.Tree));
                    plants.AddRange(first.Where(
                        item => item.Resource.Kind == WorldObjectKind.FiberPlant));
                }
            }

            Assert.That(trees.Count, Is.InRange(500, 750));
            Assert.That(trees.Count(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z < 38f * 38f),
                Is.GreaterThanOrEqualTo(4));
            Assert.That(plants.Count(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z < 38f * 38f),
                Is.GreaterThanOrEqualTo(8));
            Assert.That(trees.All(item => item.Resource.RequiredTool == ToolKind.Axe), Is.True);
            Assert.That(plants.All(item => item.Resource.ResourceItemId
                == ResourceBalance.PlantFiberItemId), Is.True);
        }

        [Test]
        public void ResearchRoll_IsStableAndUsesPromisedSuccessAndProgressRanges()
        {
            var successes = 0;
            for (ulong sampleId = 1; sampleId <= 1000; sampleId++)
            {
                var first = ResourceBalance.CalculateResearchRoll(3456678, sampleId);
                var second = ResourceBalance.CalculateResearchRoll(3456678, sampleId);
                Assert.That(second, Is.EqualTo(first));
                if (first % 100UL >= ResourceBalance.ResearchSuccessPercent) continue;
                successes++;
                var progress = ResourceBalance.ResearchMinimumBasisPoints
                    + (int)((first >> 9) % (ulong)(ResourceBalance.ResearchMaximumBasisPoints
                        - ResourceBalance.ResearchMinimumBasisPoints + 1));
                Assert.That(progress, Is.InRange(1500, 3500));
            }
            Assert.That(successes, Is.InRange(650, 750));
        }

        [Test]
        public void PlacementAndResearchNetworking_UseUpdatedStableContracts()
        {
            Assert.That(ResourceBalance.PlacementDistance, Is.EqualTo(10f));
            Assert.That(ResourceBalance.ResearchDurationSeconds, Is.EqualTo(6f));
            Assert.That(QuieterConstants.ProtocolVersion, Is.EqualTo(10));
            Assert.That(QuieterConstants.GeneratorVersion, Is.EqualTo(5));
        }

        [Test]
        public void SampleKnowledge_StaysHiddenUntilItsOwnRevealThreshold()
        {
            var first = new ItemStackState(
                ResourceBalance.UnknownSampleItemId,
                1,
                hiddenItemId: 17,
                sourceNodeId: 1001,
                revealAtPercent: 50,
                sampleId: 501);
            var second = new ItemStackState(
                ResourceBalance.UnknownSampleItemId,
                1,
                hiddenItemId: 17,
                sourceNodeId: 2002,
                revealAtPercent: 50,
                sampleId: 502);

            Assert.That(ResourceBalance.CanShowSampleKnowledge(first, 4999), Is.False);
            Assert.That(ResourceBalance.CanShowSampleKnowledge(first, 5000), Is.True);
            Assert.That(first.SourceNodeId, Is.Not.EqualTo(second.SourceNodeId));
            Assert.That(first.CanStackWith(second), Is.False);

            var firstPresentation = new DepositKnowledgePresentation(
                first.SourceNodeId, 5500, new Vector3(-135f, 0f, 266f),
                ResourceCategory.NonOre, 17);
            var secondPresentation = new DepositKnowledgePresentation(
                second.SourceNodeId, 7200, new Vector3(412f, 0f, -80f),
                ResourceCategory.NonOre, 17);
            Assert.That(firstPresentation.InstanceId,
                Is.Not.EqualTo(secondPresentation.InstanceId));
            Assert.That(firstPresentation.Position, Is.Not.EqualTo(secondPresentation.Position));
        }

        [Test]
        public void LegacySampleStack_SplitsWithoutStackingAndKeepsOverflowPending()
        {
            var filler = CreateItem(1, "Камень", 20, ItemKind.Resource);
            var unknown = CreateItem(6, "Неопознанный образец", 1, ItemKind.HiddenSample);
            var iron = CreateItem(7, "Железная руда", 20, ItemKind.Resource);
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.Configure(new[] { filler, unknown, iron }, new CraftingRecipe[0]);
            assets.Add(catalog);

            var stored = new List<StoredInventorySlot>
            {
                new()
                {
                    SlotIndex = 0,
                    ItemId = 6,
                    Quantity = 3,
                    HiddenItemId = 7,
                    SourceNodeId = "999",
                    RevealAtPercent = 50,
                },
            };
            for (byte index = 1; index < InventoryLayout.InventorySlotCount; index++)
            {
                stored.Add(new StoredInventorySlot
                {
                    SlotIndex = index,
                    ItemId = 1,
                    Quantity = 20,
                });
            }

            var model = new InventoryModel(catalog);
            var pending = model.Load(stored, 0, 7654321);
            var inventorySamples = model.Inventory.Where(stack => stack.ItemId == 6).ToArray();
            Assert.That(inventorySamples, Has.Length.EqualTo(1));
            Assert.That(pending, Has.Count.EqualTo(2));
            Assert.That(pending.All(stack => stack.Quantity == 1 && stack.SampleId != 0), Is.True);
            Assert.That(pending[0].SampleId, Is.Not.EqualTo(pending[1].SampleId));
            Assert.That(inventorySamples[0].CanStackWith(pending[0]), Is.False);
            Assert.That(inventorySamples[0].CanStackWith(inventorySamples[0]), Is.False);
        }

        [Test]
        public void Catalog_HasStableResearchItemsRecipesAndMigratesOldAxeDurability()
        {
            var catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryGetItem(23, out var fiber), Is.True);
            Assert.That(fiber.MaximumStack, Is.EqualTo(20));
            Assert.That(catalog.TryGetItem(24, out var table), Is.True);
            Assert.That(table.Kind, Is.EqualTo(ItemKind.Placeable));
            Assert.That(catalog.TryGetRecipe(4, out var ropeRecipe), Is.True);
            Assert.That(ropeRecipe.Category, Is.EqualTo(CraftingCategory.Materials));
            Assert.That(ropeRecipe.Ingredients.Single().Item.ItemId, Is.EqualTo(23));
            Assert.That(ropeRecipe.Ingredients.Single().Quantity, Is.EqualTo(3));
            Assert.That(catalog.TryGetRecipe(5, out var tableRecipe), Is.True);
            Assert.That(tableRecipe.Category, Is.EqualTo(CraftingCategory.Structures));

            var model = new InventoryModel(catalog);
            model.Load(new[]
            {
                new StoredInventorySlot
                {
                    SlotIndex = InventoryLayout.FirstHotbarSlot,
                    ItemId = 4,
                    Quantity = 1,
                    Condition = 0,
                },
            }, 0);
            Assert.That(model.ActiveStack.ItemId, Is.EqualTo(4));
            Assert.That(model.ActiveStack.Condition, Is.EqualTo(100));
        }

        private ItemDefinition CreateItem(
            ushort id,
            string name,
            ushort maximum,
            ItemKind kind)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.Configure(id, name, maximum, PickupPlacementPriority.InventoryFirst,
                Color.white, kind: kind);
            assets.Add(item);
            return item;
        }
    }
}
