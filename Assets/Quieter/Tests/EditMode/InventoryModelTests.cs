using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.Networking;
using UnityEngine;

namespace Quieter.Tests
{
    public sealed class InventoryModelTests
    {
        private readonly List<Object> assets = new();
        private ItemDefinition stone;
        private ItemDefinition wood;
        private ItemDefinition rope;
        private ItemDefinition axe;
        private CraftingRecipe axeRecipe;
        private ItemCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            stone = CreateItem(1, "Камень", 20, PickupPlacementPriority.InventoryFirst);
            wood = CreateItem(2, "Дерево", 20, PickupPlacementPriority.InventoryFirst);
            rope = CreateItem(3, "Верёвка", 10, PickupPlacementPriority.InventoryFirst);
            axe = CreateItem(4, "Топор", 1, PickupPlacementPriority.HotbarFirst);
            axeRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            axeRecipe.Configure(1, "Топор", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 3 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, axe, 1);
            assets.Add(axeRecipe);
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.Configure(new[] { stone, wood, rope, axe }, new[] { axeRecipe });
            assets.Add(catalog);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in assets)
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void NewItemDefinition_DefaultsToStackOfTen()
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            assets.Add(definition);
            Assert.That(definition.MaximumStack, Is.EqualTo(10));
        }

        [Test]
        public void ClothingLayers_ReplaceOnlyConflictingLayer()
        {
            var survivalCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            var model = new InventoryModel(survivalCatalog);
            Assert.That(model.SetSlot(Inventory(0),
                new ItemStackState(39, 1, itemInstanceId: 1001)), Is.True);
            Assert.That(model.SetSlot(Inventory(1),
                new ItemStackState(39, 1, itemInstanceId: 1002)), Is.True);
            Assert.That(model.SetSlot(Inventory(2),
                new ItemStackState(64, 1, itemInstanceId: 1003)), Is.True);

            Assert.That(model.ToggleEquippedClothing(Inventory(0), out var first), Is.True);
            Assert.That(first, Is.True);
            Assert.That(model.ToggleEquippedClothing(Inventory(1), out var replacement), Is.True);
            Assert.That(replacement, Is.True);
            Assert.That(model.Inventory[0].Equipped, Is.False,
                "A second mid-layer garment must replace the first one.");
            Assert.That(model.Inventory[1].Equipped, Is.True);

            Assert.That(model.ToggleEquippedClothing(Inventory(2), out var outer), Is.True);
            Assert.That(outer, Is.True);
            Assert.That(model.Inventory[1].Equipped, Is.True,
                "An outer cloak must coexist with a mid-layer tunic.");
            Assert.That(model.Inventory[2].Equipped, Is.True);
        }

        [Test]
        public void ClothingCatalog_ContainsDistinctCraftableLayers()
        {
            var survivalCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            var layers = new HashSet<ClothingLayer>();
            for (ushort itemId = 63; itemId <= 67; itemId++)
            {
                Assert.That(survivalCatalog.TryGetItem(itemId, out var item), Is.True);
                Assert.That(item.Kind, Is.EqualTo(ItemKind.Clothing));
                Assert.That(item.ClothingLayer, Is.Not.EqualTo(ClothingLayer.None));
                layers.Add(item.ClothingLayer);
            }
            for (ushort recipeId = 30; recipeId <= 34; recipeId++)
                Assert.That(survivalCatalog.TryGetRecipe(recipeId, out _), Is.True);
            Assert.That(layers.Count, Is.EqualTo(5));
        }

        [Test]
        public void LiquidTransfer_RollsBackRejectedDestinationAndConservesPartialVolume()
        {
            var survivalCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            var model = new InventoryModel(survivalCatalog);
            var slot = new InventorySlotReference(InventorySlotArea.Inventory, 0);
            var original = new ItemStackState(40, 1, itemInstanceId: 120,
                biologicalContamination: 8000, toxinContamination: 1400,
                liquidMilliliters: 2500, liquidKind: LiquidKind.Waste);
            Assert.That(model.SetSlot(slot, original), Is.True);
            Assert.That(model.TryDrainLiquid(slot, LiquidKind.Waste, 900,
                (_, _) => false, out var rejected), Is.False);
            Assert.That(rejected, Is.Zero);
            Assert.That(model.GetSlot(slot), Is.EqualTo(original));

            var destinationVolume = 0;
            Assert.That(model.TryDrainLiquid(slot, LiquidKind.Waste, 900,
                (source, amount) =>
                {
                    Assert.That(source.ItemInstanceId, Is.EqualTo(120));
                    Assert.That(source.BiologicalContamination, Is.EqualTo(8000));
                    destinationVolume += amount;
                    return true;
                }, out var transferred), Is.True);
            Assert.That(transferred, Is.EqualTo(900));
            Assert.That(model.GetSlot(slot).LiquidMilliliters + destinationVolume, Is.EqualTo(2500));
            Assert.That(model.GetSlot(slot).ItemInstanceId, Is.EqualTo(original.ItemInstanceId));
            Assert.That(model.GetSlot(slot).Cleanliness, Is.LessThanOrEqualTo(1200));
            Assert.That(model.TryDrainLiquid(slot, LiquidKind.Water, 900,
                (_, _) => throw new System.Exception("Wrong kind reached the sink"), out _), Is.False);
            Assert.That(model.TryDrainLiquid(slot, LiquidKind.Waste, 60000,
                (_, amount) => { destinationVolume += amount; return true; }, out _), Is.True);
            Assert.That(destinationVolume, Is.EqualTo(2500));
            Assert.That(model.GetSlot(slot).LiquidKind, Is.EqualTo(LiquidKind.None));
            Assert.That(model.GetSlot(slot).ToxinContamination, Is.EqualTo(1400));
        }

        [Test]
        public void Rinsing_DoesNotSterilizeWithRawWaterOrWashFilledEquippedItems()
        {
            var survivalCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            survivalCatalog.TryGetItem(40, out var pot);
            var dirty = new ItemStackState(40, 1, itemInstanceId: 90,
                biologicalContamination: 9500, toxinContamination: 6000, cleanliness: 1000);
            var rinsed = ItemHygieneRules.Rinse(dirty, pot, 0.45f, 0.08f);
            Assert.That(rinsed.ItemInstanceId, Is.EqualTo(dirty.ItemInstanceId));
            Assert.That(rinsed.BiologicalContamination, Is.GreaterThanOrEqualTo(4500));
            Assert.That(rinsed.ToxinContamination, Is.GreaterThanOrEqualTo(1200));
            Assert.That(ItemHygieneRules.MedicalMaterialCleanliness(rinsed), Is.LessThanOrEqualTo(0.55f));
            dirty.LiquidMilliliters = 100;
            dirty.LiquidKind = LiquidKind.Waste;
            Assert.That(ItemHygieneRules.Rinse(dirty, pot, 0f, 0f), Is.EqualTo(dirty));
            dirty.LiquidMilliliters = 0;
            dirty.Equipped = true;
            Assert.That(ItemHygieneRules.Rinse(dirty, pot, 0f, 0f), Is.EqualTo(dirty));
        }

        [Test]
        public void ToolRecipeCategory_ContainsConfiguredAxePickaxeAndShovelRecipes()
        {
            var projectCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            Assert.That(projectCatalog, Is.Not.Null);
            Assert.That(projectCatalog.TryGetRecipe(1, out var axe), Is.True);
            Assert.That(projectCatalog.TryGetRecipe(2, out var pickaxe), Is.True);
            Assert.That(projectCatalog.TryGetRecipe(3, out var shovel), Is.True);
            Assert.That(axe.Category, Is.EqualTo(CraftingCategory.Tools));
            Assert.That(pickaxe.Category, Is.EqualTo(CraftingCategory.Tools));
            Assert.That(shovel.Category, Is.EqualTo(CraftingCategory.Tools));

            var tools = projectCatalog.GetRecipes(CraftingCategory.Tools);
            Assert.That(tools, Does.Contain(axe));
            Assert.That(tools, Does.Contain(pickaxe));
            Assert.That(tools, Does.Contain(shovel));
            Assert.That(axe.Ingredients, Has.Count.EqualTo(3));
            Assert.That(axe.Ingredients[0].Item.ItemId, Is.EqualTo(wood.ItemId));
            Assert.That(axe.Ingredients[0].Quantity, Is.EqualTo(3));
            Assert.That(axe.Ingredients[1].Item.ItemId, Is.EqualTo(stone.ItemId));
            Assert.That(axe.Ingredients[1].Quantity, Is.EqualTo(2));
            Assert.That(axe.Ingredients[2].Item.ItemId, Is.EqualTo(rope.ItemId));
            Assert.That(axe.Ingredients[2].Quantity, Is.EqualTo(1));
            Assert.That(pickaxe.Ingredients, Has.Count.EqualTo(2));
            Assert.That(pickaxe.Ingredients[0].Item.ItemId, Is.EqualTo(wood.ItemId));
            Assert.That(pickaxe.Ingredients[0].Quantity, Is.EqualTo(2));
            Assert.That(pickaxe.Ingredients[1].Item.ItemId, Is.EqualTo(stone.ItemId));
            Assert.That(pickaxe.Ingredients[1].Quantity, Is.EqualTo(3));
            Assert.That(shovel.RecipeId, Is.EqualTo(3));
            Assert.That(shovel.Output.ItemId, Is.EqualTo(22));
            Assert.That(shovel.Output.Tool, Is.EqualTo(ToolKind.Shovel));
            Assert.That(shovel.Output.MaximumDurability, Is.EqualTo(100));
            Assert.That(shovel.Ingredients.Select(ingredient =>
                (ingredient.Item.ItemId, ingredient.Quantity)), Is.EquivalentTo(new[]
            {
                (wood.ItemId, (ushort)2),
                (stone.ItemId, (ushort)2),
                (rope.ItemId, (ushort)1),
            }));
        }

        [Test]
        public void PrimitiveShovel_CraftsAtFullDurabilityAndWrongToolDoesNotWearIt()
        {
            var projectCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            Assert.That(projectCatalog.TryGetRecipe(3, out var shovelRecipe), Is.True);
            var model = new InventoryModel(projectCatalog);
            model.SetSlot(Workbench(0), new ItemStackState(wood.ItemId, 2));
            model.SetSlot(Workbench(1), new ItemStackState(stone.ItemId, 2));
            model.SetSlot(Workbench(2), new ItemStackState(rope.ItemId, 1));

            Assert.That(model.TryCraft(shovelRecipe), Is.True);
            Assert.That(model.ActiveStack.ItemId, Is.EqualTo(22));
            Assert.That(model.ActiveStack.Condition, Is.EqualTo(100));
            Assert.That(model.DamageActiveTool(ToolKind.Pickaxe, 3, out _), Is.False);
            Assert.That(model.ActiveStack.Condition, Is.EqualTo(100));
            Assert.That(model.DamageActiveTool(ToolKind.Shovel, 100, out var broke), Is.True);
            Assert.That(broke, Is.True);
            Assert.That(model.ActiveStack.IsEmpty, Is.True);
        }

        [Test]
        public void DevelopmentLootPositions_AreRelativeToSavedPlayerAndFaceForward()
        {
            var playerPosition = new Vector3(183.4f, 6.3f, 328.8f);
            var center = SessionCoordinator.GetDevelopmentLootPlanarPosition(
                playerPosition, 0f, 1);
            var leftAtNinetyDegrees = SessionCoordinator.GetDevelopmentLootPlanarPosition(
                playerPosition, 90f, 0);

            Assert.That(center.x, Is.EqualTo(playerPosition.x).Within(0.001f));
            Assert.That(center.y, Is.EqualTo(playerPosition.z + 2.65f).Within(0.001f));
            Assert.That(leftAtNinetyDegrees.x, Is.GreaterThan(playerPosition.x + 2f));
            Assert.That(Vector2.Distance(
                new Vector2(playerPosition.x, playerPosition.z), center), Is.LessThan(3f));
        }

        [Test]
        public void DevelopmentHostPort_FallsBackWhenPreferredPortIsOccupied()
        {
            using var occupiedSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp)
            {
                ExclusiveAddressUse = true,
            };
            occupiedSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            var occupiedPort = (ushort)((IPEndPoint)occupiedSocket.LocalEndPoint).Port;

            var selectedPort = SessionCoordinator.FindAvailableDevelopmentPort(occupiedPort);

            Assert.That(selectedPort, Is.Not.Zero);
            Assert.That(selectedPort, Is.Not.EqualTo(occupiedPort));
        }

        [Test]
        public void AutoInsert_UsesIndividualLimitsAndBothAreas()
        {
            var model = new InventoryModel(catalog);
            var remainder = model.AutoInsert(stone.ItemId, 30, stone.PickupPriority);

            Assert.That(remainder, Is.Zero);
            AssertStack(model.Inventory[0], stone, 20);
            AssertStack(model.Inventory[1], stone, 10);

            for (var index = 0; index < InventoryLayout.MainSlotCount; index++)
            {
                Assert.That(model.SetSlot(Inventory(index), new ItemStackState(wood.ItemId, 20)), Is.True);
            }

            remainder = model.AutoInsert(rope.ItemId, 11, rope.PickupPriority);
            Assert.That(remainder, Is.Zero);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot], rope, 10);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot + 1], rope, 1);
        }

        [Test]
        public void AutoInsert_WhenOnlyPartialCapacityExists_ReturnsGroundRemainder()
        {
            var model = new InventoryModel(catalog);
            for (var index = 0; index < InventoryLayout.InventorySlotCount; index++)
            {
                model.SetSlot(Inventory(index), new ItemStackState(stone.ItemId, 20));
            }
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 17));

            var remainder = model.AutoInsert(stone.ItemId, 8, stone.PickupPriority);

            Assert.That(remainder, Is.EqualTo(5));
            AssertStack(model.Inventory[0], stone, 20);
        }

        [Test]
        public void LeftClick_MergesWithRemainderAndSwapsDifferentItems()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 8));
            model.SetSlot(Inventory(1), new ItemStackState(stone.ItemId, 18));
            model.SetSlot(Inventory(2), new ItemStackState(wood.ItemId, 4));

            Assert.That(model.LeftClick(Inventory(0)), Is.True);
            Assert.That(model.LeftClick(Inventory(1)), Is.True);
            AssertStack(model.Inventory[1], stone, 20);
            AssertStack(model.Cursor, stone, 6);

            Assert.That(model.LeftClick(Inventory(2)), Is.True);
            AssertStack(model.Inventory[2], stone, 6);
            AssertStack(model.Cursor, wood, 4);
            Assert.That(model.CursorOrigin.IsValid, Is.False);
        }

        [Test]
        public void RightClick_TakesLargerHalfAndPlacesOne()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 5));

            Assert.That(model.RightClick(Inventory(0)), Is.True);
            AssertStack(model.Cursor, stone, 3);
            AssertStack(model.Inventory[0], stone, 2);
            Assert.That(model.RightClick(Inventory(1)), Is.True);
            AssertStack(model.Inventory[1], stone, 1);
            AssertStack(model.Cursor, stone, 2);
        }

        [Test]
        public void CursorQuantity_ChangesAgainstItsOriginAndKeepsOneInHand()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));

            Assert.That(model.LeftClick(Inventory(0), 3), Is.True);
            AssertStack(model.Cursor, stone, 3);
            Assert.That(model.AdjustCursorFromOrigin(2), Is.True);
            AssertStack(model.Cursor, stone, 5);
            AssertStack(model.Inventory[0], stone, 5);
            Assert.That(model.AdjustCursorFromOrigin(-4), Is.True);
            AssertStack(model.Cursor, stone, 1);
            AssertStack(model.Inventory[0], stone, 9);
            Assert.That(model.AdjustCursorFromOrigin(-1), Is.False);
            AssertStack(model.Cursor, stone, 1);
            AssertStack(model.Inventory[0], stone, 9);
            Assert.That(model.AdjustCursorFromOrigin(20), Is.True);
            AssertStack(model.Cursor, stone, 10);
            Assert.That(model.Inventory[0].IsEmpty, Is.True);
            Assert.That(model.AdjustCursorFromOrigin(1), Is.False);
        }

        [Test]
        public void CursorQuantity_WorksFromInventoryHotbarAndWorkbench()
        {
            var origins = new[]
            {
                Inventory(0),
                Inventory(InventoryLayout.FirstHotbarSlot),
                Workbench(0),
            };

            foreach (var origin in origins)
            {
                var model = new InventoryModel(catalog);
                Assert.That(model.SetSlot(origin, new ItemStackState(stone.ItemId, 10)), Is.True);
                Assert.That(model.LeftClick(origin, 4), Is.True);
                Assert.That(model.AdjustCursorFromOrigin(1), Is.True);
                AssertStack(model.Cursor, stone, 5);
                AssertStack(model.GetSlot(origin), stone, 5);
            }
        }

        [Test]
        public void CursorQuantity_DoesNothingAfterSwapLosesOrigin()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));
            model.SetSlot(Inventory(1), new ItemStackState(wood.ItemId, 4));

            Assert.That(model.LeftClick(Inventory(0)), Is.True);
            Assert.That(model.LeftClick(Inventory(1)), Is.True);
            Assert.That(model.CursorOrigin.IsValid, Is.False);
            Assert.That(model.AdjustCursorFromOrigin(-1), Is.False);
            Assert.That(model.AdjustCursorFromOrigin(1), Is.False);
            AssertStack(model.Cursor, wood, 4);
            AssertStack(model.Inventory[1], stone, 10);
        }

        [Test]
        public void MoveStack_PartiallyMovesAcrossInventoryHotbarAndWorkbench()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));

            Assert.That(model.MoveStack(Inventory(0), Workbench(0), stone.ItemId, 3), Is.True);
            AssertStack(model.Inventory[0], stone, 7);
            AssertStack(model.Workbench[0], stone, 3);
            Assert.That(model.MoveStack(
                Workbench(0),
                Inventory(InventoryLayout.FirstHotbarSlot),
                stone.ItemId,
                2), Is.True);
            AssertStack(model.Workbench[0], stone, 1);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot], stone, 2);
        }

        [Test]
        public void MoveStack_MergesToMaximumAndLeavesRemainderInSource()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));
            model.SetSlot(Inventory(1), new ItemStackState(stone.ItemId, 18));

            Assert.That(model.MoveStack(Inventory(0), Inventory(1), stone.ItemId, 10), Is.True);
            AssertStack(model.Inventory[0], stone, 8);
            AssertStack(model.Inventory[1], stone, 20);
            Assert.That(model.MoveStack(Inventory(0), Inventory(1), stone.ItemId, 8), Is.False);
            AssertStack(model.Inventory[0], stone, 8);
            AssertStack(model.Inventory[1], stone, 20);
        }

        [Test]
        public void MoveStack_SwapsDifferentItemsOnlyWhenDraggingWholeStack()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));
            model.SetSlot(Workbench(0), new ItemStackState(wood.ItemId, 4));

            Assert.That(model.MoveStack(Inventory(0), Workbench(0), stone.ItemId, 3), Is.False);
            AssertStack(model.Inventory[0], stone, 10);
            AssertStack(model.Workbench[0], wood, 4);
            Assert.That(model.MoveStack(Inventory(0), Workbench(0), stone.ItemId, 10), Is.True);
            AssertStack(model.Inventory[0], wood, 4);
            AssertStack(model.Workbench[0], stone, 10);
        }

        [Test]
        public void MoveStack_RejectsStaleInvalidAndSameSlotRequestsAtomically()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));

            Assert.That(model.MoveStack(Inventory(0), Inventory(1), wood.ItemId, 4), Is.False);
            Assert.That(model.MoveStack(Inventory(0), Inventory(1), stone.ItemId, 0), Is.False);
            Assert.That(model.MoveStack(Inventory(0), Inventory(1), stone.ItemId, 11), Is.False);
            Assert.That(model.MoveStack(Inventory(0), Inventory(0), stone.ItemId, 10), Is.False);
            AssertStack(model.Inventory[0], stone, 10);
            Assert.That(model.Inventory[1].IsEmpty, Is.True);
        }

        [Test]
        public void RemoveStack_DropsSelectedQuantityWithoutChangingAnythingOnInvalidRequest()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));

            Assert.That(model.RemoveStack(Inventory(0), wood.ItemId, 3, out _), Is.False);
            Assert.That(model.RemoveStack(Inventory(0), stone.ItemId, 11, out _), Is.False);
            AssertStack(model.Inventory[0], stone, 10);
            Assert.That(model.RemoveStack(Inventory(0), stone.ItemId, 3, out var removed), Is.True);
            AssertStack(removed, stone, 3);
            AssertStack(model.Inventory[0], stone, 7);
            Assert.That(model.RemoveStack(Inventory(0), stone.ItemId, 7, out removed), Is.True);
            AssertStack(removed, stone, 7);
            Assert.That(model.Inventory[0].IsEmpty, Is.True);
        }

        [Test]
        public void TransferTo_IsAtomicAndUnequipsTransferredStack()
        {
            var source = new InventoryModel(catalog);
            var destination = new InventoryModel(catalog);
            var equippedAxe = new ItemStackState(axe.ItemId, 1, 100)
            {
                Equipped = true,
            };
            source.SetSlot(Inventory(0), equippedAxe);
            for (var index = 0; index < InventoryLayout.InventorySlotCount; index++)
            {
                destination.SetSlot(Inventory(index),
                    new ItemStackState(stone.ItemId, stone.MaximumStack));
            }

            Assert.That(source.TryTransferTo(
                Inventory(0), 1, destination, axe.PickupPriority, out _), Is.False);
            Assert.That(source.Inventory[0].ItemId, Is.EqualTo(axe.ItemId));
            Assert.That(source.Inventory[0].Equipped, Is.True);

            destination.SetSlot(Inventory(InventoryLayout.FirstHotbarSlot), default);
            Assert.That(source.TryTransferTo(
                Inventory(0), 1, destination, axe.PickupPriority,
                out var transferred), Is.True);
            Assert.That(source.Inventory[0].IsEmpty, Is.True);
            Assert.That(transferred.Equipped, Is.False);
            Assert.That(destination.Inventory[InventoryLayout.FirstHotbarSlot].ItemId,
                Is.EqualTo(axe.ItemId));
            Assert.That(destination.Inventory[InventoryLayout.FirstHotbarSlot].Equipped,
                Is.False);
        }

        [Test]
        public void PerishableFood_DecaysWithTimeTemperatureAndHumidity()
        {
            var food = ScriptableObject.CreateInstance<ItemDefinition>();
            food.Configure(
                50,
                "Тестовая пища",
                10,
                PickupPlacementPriority.InventoryFirst,
                Color.white,
                kind: ItemKind.Food,
                configuredShelfLifeGameHours: 24f);
            assets.Add(food);
            var fresh = new ItemStackState(food.ItemId, 1);

            var warm = FoodDecayRules.Advance(fresh, food, 7200f, 30f, 0.8f);
            var frozen = FoodDecayRules.Advance(fresh, food, 7200f, -5f, 0.8f);

            Assert.That(warm.Freshness, Is.Zero);
            Assert.That(warm.BiologicalContamination, Is.GreaterThan(8000));
            Assert.That(frozen.Freshness, Is.GreaterThan(8500));
            Assert.That(frozen.BiologicalContamination,
                Is.LessThan(warm.BiologicalContamination));
        }

        [Test]
        public void ShiftClick_MovesBetweenInventoryHotbarAndBack()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 8));
            model.SetSlot(Inventory(InventoryLayout.FirstHotbarSlot),
                new ItemStackState(stone.ItemId, 18));

            Assert.That(model.ShiftClick(Inventory(0)), Is.True);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot], stone, 20);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot + 1], stone, 6);
            Assert.That(model.ShiftClick(Inventory(InventoryLayout.FirstHotbarSlot + 1)), Is.True);
            AssertStack(model.Inventory[0], stone, 6);
        }

        [Test]
        public void Craft_RequiresExactContentsAndPlacesAxeInHotbar()
        {
            var model = new InventoryModel(catalog);
            FillRecipe(model);
            model.SetSlot(Workbench(3), new ItemStackState(stone.ItemId, 1));
            Assert.That(model.TryCraft(axeRecipe), Is.False);

            model.SetSlot(Workbench(3), default);
            Assert.That(model.TryCraft(axeRecipe), Is.True);
            AssertStack(model.Inventory[InventoryLayout.FirstHotbarSlot], axe, 1);
            Assert.That(model.Workbench, Is.All.Matches<ItemStackState>(stack => stack.IsEmpty));
        }

        [Test]
        public void Craft_WithNoResultSpace_IsAtomic()
        {
            var model = new InventoryModel(catalog);
            for (var index = 0; index < InventoryLayout.InventorySlotCount; index++)
            {
                model.SetSlot(Inventory(index), new ItemStackState(stone.ItemId, 20));
            }
            FillRecipe(model);

            Assert.That(model.TryCraft(axeRecipe), Is.False);
            AssertStack(model.Workbench[0], wood, 3);
            AssertStack(model.Workbench[1], stone, 2);
            AssertStack(model.Workbench[2], rope, 1);
        }

        [Test]
        public void NormalizeTemporaryStorage_ReturnsCursorThenWorkbench()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));
            model.LeftClick(Inventory(0), 3);
            model.SetSlot(Workbench(0), new ItemStackState(wood.ItemId, 3));

            Assert.That(model.NormalizeTemporaryStorage(), Is.True);
            Assert.That(model.Cursor.IsEmpty, Is.True);
            AssertStack(model.Inventory[0], stone, 10);
            AssertStack(model.Inventory[1], wood, 3);
            Assert.That(model.Workbench[0].IsEmpty, Is.True);
        }

        [Test]
        public void NormalizedSaveSnapshot_IncludesTemporaryItemsWithoutChangingOpenUiState()
        {
            var model = new InventoryModel(catalog);
            model.SetSlot(Inventory(0), new ItemStackState(stone.ItemId, 10));
            model.LeftClick(Inventory(0), 3);
            model.SetSlot(Workbench(0), new ItemStackState(wood.ItemId, 3));

            Assert.That(model.TryCreateNormalizedStoredSlots(out var slots), Is.True);
            AssertStack(model.Cursor, stone, 3);
            AssertStack(model.Inventory[0], stone, 7);
            AssertStack(model.Workbench[0], wood, 3);
            Assert.That(slots, Has.Count.EqualTo(2));
            Assert.That(slots[0].ItemId, Is.EqualTo(stone.ItemId));
            Assert.That(slots[0].Quantity, Is.EqualTo(10));
            Assert.That(slots[1].ItemId, Is.EqualTo(wood.ItemId));
            Assert.That(slots[1].Quantity, Is.EqualTo(3));
        }

        private void FillRecipe(InventoryModel model)
        {
            model.SetSlot(Workbench(0), new ItemStackState(wood.ItemId, 3));
            model.SetSlot(Workbench(1), new ItemStackState(stone.ItemId, 2));
            model.SetSlot(Workbench(2), new ItemStackState(rope.ItemId, 1));
        }

        private ItemDefinition CreateItem(
            ushort id,
            string displayName,
            ushort maximum,
            PickupPlacementPriority priority)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            definition.Configure(id, displayName, maximum, priority, Color.white);
            assets.Add(definition);
            return definition;
        }

        private static InventorySlotReference Inventory(int index) =>
            new(InventorySlotArea.Inventory, index);

        private static InventorySlotReference Workbench(int index) =>
            new(InventorySlotArea.Workbench, index);

        private static void AssertStack(ItemStackState stack, ItemDefinition item, int quantity)
        {
            Assert.That(stack.ItemId, Is.EqualTo(item.ItemId));
            Assert.That(stack.Quantity, Is.EqualTo(quantity));
        }
    }
}
