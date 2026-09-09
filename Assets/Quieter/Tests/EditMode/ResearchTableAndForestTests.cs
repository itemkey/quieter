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
            var food = new List<WorldObjectSpawn>();
            var forage = new List<WorldObjectSpawn>();
            var springs = new List<WorldObjectSpawn>();
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
                    food.AddRange(first.Where(item =>
                        ResourceBalance.IsWildFood(item.Resource.ResourceItemId)));
                    forage.AddRange(first.Where(item =>
                        ResourceBalance.IsWildForage(item.Resource.ResourceItemId)));
                    springs.AddRange(first.Where(item => item.Resource.IsWaterSource));
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
            Assert.That(food.Count(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z < 38f * 38f),
                Is.GreaterThanOrEqualTo(12));
            Assert.That(food.Count, Is.InRange(450, 750));
            Assert.That(food.Select(item => item.Resource.ResourceItemId).Distinct(),
                Is.EquivalentTo(new[]
                {
                    ResourceBalance.WildBerriesItemId,
                    ResourceBalance.EdibleRootsItemId,
                    ResourceBalance.WildMushroomsItemId,
                    ResourceBalance.WildNutsItemId,
                }));
            Assert.That(forage.Any(item => item.Resource.ResourceItemId
                == ResourceBalance.MedicinalHerbsItemId), Is.True);
            Assert.That(forage.Count(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z < 38f * 38f
                    && item.Resource.ResourceItemId == ResourceBalance.MedicinalHerbsItemId),
                Is.GreaterThanOrEqualTo(3));
            Assert.That(springs.Count, Is.InRange(70, 120));
            Assert.That(springs.Count(item => item.Position.x * item.Position.x
                    + item.Position.z * item.Position.z < 38f * 38f),
                Is.GreaterThanOrEqualTo(1));
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
        public void HearthCooking_KillsBiologicalContaminationButPreservesPlantToxins()
        {
            var raw = new ItemStackState(
                ResourceBalance.WildMushroomsItemId,
                2,
                itemInstanceId: 991,
                freshness: 7200,
                biologicalContamination: 5000,
                toxinContamination: 6800,
                cleanliness: 8400);

            var cooked = ResourceBalance.CookSolidFood(
                raw, ResourceBalance.CookedMushroomsItemId);

            Assert.That(cooked.ItemId, Is.EqualTo(ResourceBalance.CookedMushroomsItemId));
            Assert.That(cooked.Quantity, Is.EqualTo(2));
            Assert.That(cooked.ItemInstanceId, Is.EqualTo(991));
            Assert.That(cooked.Freshness, Is.EqualTo(7200));
            Assert.That(cooked.BiologicalContamination, Is.EqualTo(100));
            Assert.That(cooked.ToxinContamination, Is.EqualTo(6800));
            Assert.That(ResourceBalance.CookSolidFood(
                raw, ResourceBalance.CookedRootsItemId).IsEmpty, Is.True);
        }

        [Test]
        public void PlacementAndResearchNetworking_UseUpdatedStableContracts()
        {
            Assert.That(ResourceBalance.PlacementDistance, Is.EqualTo(10f));
            Assert.That(ResourceBalance.ResearchDurationSeconds, Is.EqualTo(6f));
            Assert.That(QuieterConstants.ProtocolVersion, Is.EqualTo(15));
            Assert.That(QuieterConstants.GeneratorVersion, Is.EqualTo(7));
            var catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            Assert.That(catalog.TryGetRecipe(6, out var leanTo), Is.True);
            Assert.That(catalog.TryGetRecipe(7, out var hearth), Is.True);
            Assert.That(catalog.TryGetRecipe(8, out var bucket), Is.True);
            Assert.That(catalog.TryGetRecipe(9, out var wastePit), Is.True);
            Assert.That(leanTo.Output.ItemId, Is.EqualTo(SurvivalStructureRules.LeanToItemId));
            Assert.That(hearth.Output.ItemId, Is.EqualTo(SurvivalStructureRules.HearthItemId));
            Assert.That(bucket.Output.ItemId, Is.EqualTo(38));
            Assert.That(wastePit.Output.ItemId,
                Is.EqualTo(SurvivalStructureRules.UnlinedWastePitItemId));
        }

        [Test]
        public void BaseBuildingCatalog_ContainsEveryPhysicalStructureRecipe()
        {
            var catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            Assert.That(catalog, Is.Not.Null);
            for (ushort itemId = SurvivalStructureRules.FloorItemId;
                 itemId <= SurvivalStructureRules.LinedWastePitItemId;
                 itemId++)
            {
                Assert.That(catalog.TryGetItem(itemId, out var item), Is.True,
                    $"Missing building item {itemId}");
                Assert.That(item.Kind, Is.EqualTo(ItemKind.Placeable));
            }
            for (ushort recipeId = 17; recipeId <= 29; recipeId++)
            {
                Assert.That(catalog.TryGetRecipe(recipeId, out var recipe), Is.True,
                    $"Missing building recipe {recipeId}");
                Assert.That(recipe.Category, Is.EqualTo(CraftingCategory.Structures));
            }
        }

        [Test]
        public void ModularBuildingParts_SnapToHalfMeterGridAndCardinalRotation()
        {
            var snapped = SurvivalStructureRules.SnapPlacement(
                new Vector3(12.24f, 3.2f, -5.76f), SurvivalStructureRules.WallItemId);
            Assert.That(snapped, Is.EqualTo(new Vector3(12f, 3.2f, -6f)));
            Assert.That(SurvivalStructureRules.SnapYaw(
                137f, SurvivalStructureRules.WallItemId), Is.EqualTo(180f));
            Assert.That(SurvivalStructureRules.AllowsModularOverlap(
                SurvivalStructureRules.WallItemId,
                SurvivalStructureRules.FloorItemId), Is.True);
            Assert.That(SurvivalStructureRules.AllowsModularOverlap(
                SurvivalStructureRules.WallItemId,
                SurvivalStructureRules.WallItemId), Is.False);
        }

        [Test]
        public void SanitationConnections_RequireNearbyNonRisingDrainOrPit()
        {
            var fixture = new Vector3(0f, 10f, 0f);
            Assert.That(SurvivalStructureRules.CanConnectSanitation(
                SurvivalStructureRules.LatrineItemId, fixture,
                SurvivalStructureRules.DrainItemId, new Vector3(2f, 9.8f, 0f)), Is.True);
            Assert.That(SurvivalStructureRules.CanConnectSanitation(
                SurvivalStructureRules.DrainItemId, new Vector3(2f, 9.8f, 0f),
                SurvivalStructureRules.LinedWastePitItemId,
                new Vector3(4.5f, 9.4f, 0f)), Is.True);
            Assert.That(SurvivalStructureRules.CanConnectSanitation(
                SurvivalStructureRules.LatrineItemId, fixture,
                SurvivalStructureRules.DrainItemId, new Vector3(2f, 10.5f, 0f)), Is.False);
            Assert.That(SurvivalStructureRules.CanConnectSanitation(
                SurvivalStructureRules.LatrineItemId, fixture,
                SurvivalStructureRules.DrainItemId, new Vector3(3f, 9.8f, 0f)), Is.False);
        }

        [Test]
        public void LinedPitLeaksFarLessAndWaterStorageHasBoundedCapacity()
        {
            Assert.That(SurvivalStructureRules.WasteLeakageMultiplier(
                SurvivalStructureRules.UnlinedWastePitItemId), Is.EqualTo(1f));
            Assert.That(SurvivalStructureRules.WasteLeakageMultiplier(
                SurvivalStructureRules.LinedWastePitItemId), Is.EqualTo(0.08f));
            Assert.That(SurvivalStructureRules.WaterStorageCapacity(
                SurvivalStructureRules.WashBasinItemId), Is.EqualTo(6000));
            Assert.That(SurvivalStructureRules.WaterStorageCapacity(
                SurvivalStructureRules.BarrelItemId), Is.EqualTo(60000));
        }

        [Test]
        public void HearthFuel_BurnsAcrossUnitsAndCharcoalLastsLonger()
        {
            var wood = SurvivalStructureRules.AddFuel(default, 2);
            wood = SurvivalStructureRules.AddFuel(wood, 2);
            var afterFirstUnit = SurvivalStructureRules.BurnFuel(wood, 600f);
            var charcoal = SurvivalStructureRules.AddFuel(default, 37);

            Assert.That(afterFirstUnit.Quantity, Is.EqualTo(1));
            Assert.That(afterFirstUnit.Condition, Is.EqualTo(10000));
            Assert.That(SurvivalStructureRules.BurnFuel(afterFirstUnit, 600f).IsEmpty,
                Is.True);
            Assert.That(SurvivalStructureRules.BurnFuel(charcoal, 600f).IsEmpty,
                Is.False);
            Assert.That(SurvivalStructureRules.AddFuel(wood, 37), Is.EqualTo(wood));
        }

        [Test]
        public void WastePit_RunoffRespectsSlopeRainDistanceAndContents()
        {
            var pit = new Vector3(0f, 20f, 0f);
            var downhill = new Vector3(20f, 8f, 0f);
            var dry = SurvivalStructureRules.CalculatePitLeakage(pit, downhill, 60f, 1f, 0.5f, 0f);
            var rain = SurvivalStructureRules.CalculatePitLeakage(pit, downhill, 60f, 1f, 0.5f, 1f);
            Assert.That(dry.Biological, Is.GreaterThan(0f));
            Assert.That(rain.Biological, Is.GreaterThan(dry.Biological));
            Assert.That(rain.Toxins, Is.GreaterThan(dry.Toxins));
            Assert.That(SurvivalStructureRules.CalculatePitLeakage(
                pit, new Vector3(20f, 21f, 0f), 60f, 1f, 1f, 1f), Is.EqualTo((0f, 0f)));
            Assert.That(SurvivalStructureRules.CalculatePitLeakage(
                pit, new Vector3(200f, 0f, 0f), 60f, 1f, 1f, 1f), Is.EqualTo((0f, 0f)));
            Assert.That(SurvivalStructureRules.CalculatePitLeakage(
                pit, downhill, 0f, 1f, 1f, 1f), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public void HeadlessLeanTo_HasWalkableEntranceAndSolidRoofAndBack()
        {
            var position = new Vector3(4000f, 1000f, 4000f);
            var shelter = SurvivalStructureView.Create(15,
                SurvivalStructureRules.LeanToItemId, position, 0f, null, false);
            assets.Add(shelter.gameObject);
            Physics.SyncTransforms();
            var interior = Physics.OverlapCapsule(position + Vector3.up * 0.4f,
                position + Vector3.up * 1.55f, 0.3f);
            Assert.That(interior.Any(c => c.GetComponentInParent<SurvivalStructureView>() == shelter), Is.False);
            Assert.That(Physics.Raycast(position + new Vector3(0f, 1.3f, 2f),
                Vector3.back, out var back, 5f), Is.True);
            Assert.That(back.collider.transform.localPosition.z, Is.LessThan(-1f));
            Assert.That(Physics.Raycast(position + Vector3.up, Vector3.up,
                out var roof, 3f), Is.True);
            Assert.That(roof.collider.GetComponentInParent<SurvivalStructureView>(), Is.SameAs(shelter));
        }

        [Test]
        public void HoldingCell_UsesRotatedInteriorAndLockableDoorCollider()
        {
            var position = new Vector3(5000f, 1000f, 5000f);
            Assert.That(SurvivalStructureRules.IsInsideHoldingCell(
                position + new Vector3(1.5f, 1f, 0f), position, 90f), Is.True);
            Assert.That(SurvivalStructureRules.IsInsideHoldingCell(
                position + new Vector3(0f, 1f, 1.9f), position, 90f), Is.False);

            var cell = SurvivalStructureView.Create(16,
                SurvivalStructureRules.HoldingCellItemId, position, 0f, null, false);
            assets.Add(cell.gameObject);
            var door = cell.transform.Find("DoorCollision").GetComponent<Collider>();
            cell.ApplyState(default, false);
            Assert.That(door.enabled, Is.False);
            cell.ApplyState(default, true);
            Assert.That(door.enabled, Is.True);
        }

        [Test]
        public void SurvivalCrafts_RequireTimeAndPotteryRequiresFire()
        {
            var catalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            for (ushort recipeId = 10; recipeId <= 14; recipeId++)
            {
                Assert.That(catalog.TryGetRecipe(recipeId, out var recipe), Is.True);
                Assert.That(recipe.WorkSeconds, Is.GreaterThanOrEqualTo(20f));
                Assert.That(recipe.RequiresBurningHearth, Is.EqualTo(recipeId <= 11));
                var model = new InventoryModel(catalog);
                var index = 0;
                foreach (var ingredient in recipe.Ingredients)
                    model.SetSlot(new InventorySlotReference(InventorySlotArea.Workbench, index++),
                        new ItemStackState(ingredient.Item.ItemId, ingredient.Quantity));
                Assert.That(model.TryCraft(recipe), Is.True);
                Assert.That(model.Inventory.Any(s => s.ItemId == recipe.Output.ItemId), Is.True);
                Assert.That(model.TryCraft(recipe), Is.False, "A second completion must not duplicate output.");
            }
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
