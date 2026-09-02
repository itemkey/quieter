using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.World;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.Tests.EditMode
{
    public sealed class ResourceExtractionTests
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
        public void DefaultWorld_GuaranteesEveryBaseDepositAndStarterMaterials()
        {
            var catalog = ScriptableObject.CreateInstance<WorldObjectCatalog>();
            catalog.ConfigureDefaults();
            assets.Add(catalog);
            var generator = new DeterministicChunkGenerator(catalog);
            var definition = WorldDefinition.CreateDefault(9384750293847);
            var resources = new HashSet<ushort>();
            var depositCounts = new Dictionary<ushort, int>();
            var richnessToReserves = new Dictionary<DepositRichness, HashSet<DepositReserveSize>>();
            var totalDeposits = 0;
            var starterStone = 0;
            var starterWood = 0;

            for (var z = 0; z < definition.ChunkCountZ; z++)
            {
                for (var x = 0; x < definition.ChunkCountX; x++)
                {
                    var chunkSpawns = generator.GenerateObjectsForMap(
                        definition, new ChunkCoord(x, z));
                    foreach (var spawn in chunkSpawns)
                    {
                        if (spawn.Resource.Kind == WorldObjectKind.Deposit)
                        {
                            resources.Add(spawn.Resource.ResourceItemId);
                            totalDeposits++;
                            depositCounts.TryGetValue(spawn.Resource.ResourceItemId, out var count);
                            depositCounts[spawn.Resource.ResourceItemId] = count + 1;
                            if (!richnessToReserves.TryGetValue(
                                    spawn.Resource.Richness, out var reserveSizes))
                            {
                                reserveSizes = new HashSet<DepositReserveSize>();
                                richnessToReserves[spawn.Resource.Richness] = reserveSizes;
                            }
                            reserveSizes.Add(spawn.Resource.ReserveSize);
                        }
                        var radiusSquared = spawn.Position.x * spawn.Position.x
                            + spawn.Position.z * spawn.Position.z;
                        if (spawn.Resource.Kind == WorldObjectKind.LoosePickup
                            && radiusSquared < 38f * 38f)
                        {
                            if (spawn.Resource.ResourceItemId == 1) starterStone++;
                            if (spawn.Resource.ResourceItemId == 2) starterWood++;
                        }
                    }

                    foreach (var nonOre in chunkSpawns.Where(spawn =>
                                 spawn.Resource.Kind == WorldObjectKind.Deposit
                                 && spawn.Resource.ResourceItemId is >= 17 and <= 21))
                    {
                        Assert.That(chunkSpawns.Where(other =>
                                    other.InstanceId != nonOre.InstanceId
                                    && other.Resource.Kind != WorldObjectKind.Tree
                                    && other.Resource.Kind != WorldObjectKind.FiberPlant)
                                .All(other => Vector2.Distance(
                                    new Vector2(nonOre.Position.x, nonOre.Position.z),
                                    new Vector2(other.Position.x, other.Position.z)) >= 6f),
                            Is.True,
                            $"Non-ore deposit {nonOre.InstanceId} spawned too close to another object.");
                    }
                }
            }

            Assert.That(resources, Is.SupersetOf(Enumerable.Range(7, 15).Select(value => (ushort)value)));
            Assert.That(starterStone, Is.GreaterThanOrEqualTo(24));
            Assert.That(starterWood, Is.GreaterThanOrEqualTo(16));
            Assert.That(totalDeposits, Is.InRange(380, 500));
            Assert.That(depositCounts[7], Is.GreaterThan(depositCounts[12]));
            Assert.That(depositCounts[8], Is.GreaterThan(depositCounts[16]));
            Assert.That(depositCounts[13], Is.GreaterThan(depositCounts[12]));
            var countSummary = string.Join(", ", Enumerable.Range(17, 5)
                .Select(itemId => $"{itemId}={depositCounts[(ushort)itemId]}"));
            Assert.That(depositCounts[17], Is.InRange(30, 65), countSummary);
            Assert.That(depositCounts[18], Is.InRange(25, 60), countSummary);
            Assert.That(depositCounts[19], Is.InRange(8, 35), countSummary);
            Assert.That(depositCounts[20], Is.InRange(12, 45), countSummary);
            Assert.That(depositCounts[21], Is.InRange(2, 18), countSummary);
            Assert.That(richnessToReserves.Values.Any(values => values.Count >= 3), Is.True,
                "Richness and reserve size must vary independently.");

            foreach (var itemId in Enumerable.Range(7, 15).Select(value => (ushort)value))
            {
                Assert.That(catalog.TryGetResource(itemId, out var definitionEntry), Is.True);
                Assert.That(definitionEntry.requiredTool, Is.EqualTo(
                    itemId == 18 ? ToolKind.Shovel : ToolKind.Pickaxe));
            }
        }

        [Test]
        public void NonOreStream_DoesNotChangeExistingDepositPlacementOrCharacteristics()
        {
            var catalog = ScriptableObject.CreateInstance<WorldObjectCatalog>();
            catalog.ConfigureDefaults();
            assets.Add(catalog);
            var definition = WorldDefinition.CreateDefault(7733991155);
            var originalGenerator = new DeterministicChunkGenerator(catalog);
            var original = GenerateBaseDepositSignatures(originalGenerator, definition);

            foreach (var resource in catalog.Resources.Where(entry =>
                         entry.generationPool == DepositGenerationPool.NonOre))
            {
                resource.worldWeight += 500;
                resource.minimumHardness = 5;
                resource.maximumHardness = 5;
                resource.requiredTool = ToolKind.Shovel;
                resource.stronglyRegional = !resource.stronglyRegional;
            }

            var changedGenerator = new DeterministicChunkGenerator(catalog);
            var afterNonOreChange = GenerateBaseDepositSignatures(changedGenerator, definition);

            Assert.That(afterNonOreChange, Is.EqualTo(original));
        }

        [Test]
        public void MapNotes_NormalizeTextAndClampCoordinates()
        {
            var normalized = MapNoteRules.NormalizeText(
                "  Богатая\tглина\nвозле\u0001 холма  " + new string('я', 100));
            var definition = WorldDefinition.CreateDefault(10);
            var clamped = MapNoteRules.ClampToWorld(
                new Vector2(float.MaxValue, float.MinValue), definition);

            Assert.That(normalized, Does.StartWith("Богатая глина возле холма"));
            Assert.That(normalized.Length, Is.LessThanOrEqualTo(MapNoteRules.MaximumTextLength));
            Assert.That(normalized.Any(char.IsControl), Is.False);
            Assert.That(clamped.x, Is.EqualTo(definition.WorldMaximum.x));
            Assert.That(clamped.y, Is.EqualTo(definition.WorldMinimum.z));
            Assert.That(MapNoteRules.NormalizeText(" \t\n "), Is.Empty);
        }

        [Test]
        public void ResourceDescriptors_AreStableAndKeepRichnessSeparateFromReserves()
        {
            var definition = WorldDefinition.CreateDefault(123456789);
            var generator = new DeterministicChunkGenerator();
            var first = generator.GenerateObjectsForMap(definition, new ChunkCoord(7, 11));
            var second = generator.GenerateObjectsForMap(definition, new ChunkCoord(7, 11));

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (var index = 0; index < first.Count; index++)
            {
                Assert.That(second[index].InstanceId, Is.EqualTo(first[index].InstanceId));
                Assert.That(second[index].Resource, Is.EqualTo(first[index].Resource));
            }
            Assert.That(ResourceBalance.ReserveUnits[(int)DepositReserveSize.Huge], Is.EqualTo(480));
            Assert.That(ResourceBalance.UsefulChancePercent[(int)DepositRichness.Exceptional], Is.EqualTo(90));

            var serviceObject = new GameObject("Resource roll test");
            assets.Add(serviceObject);
            var service = serviceObject.AddComponent<ResourceWorldService>();
            service.InitializeClient(definition);
            var firstRoll = service.CalculateExtractionRoll(99887766UL, 14, 2);
            Assert.That(service.CalculateExtractionRoll(99887766UL, 14, 2), Is.EqualTo(firstRoll));
            Assert.That(service.CalculateExtractionRoll(99887766UL, 15, 2), Is.Not.EqualTo(firstRoll));
        }

        [Test]
        public void DurablePickaxe_CraftsFullAndHiddenSamplesRevealBySource()
        {
            var stone = CreateItem(1, "Камень", 20);
            var wood = CreateItem(2, "Дерево", 20);
            var pickaxe = CreateItem(5, "Примитивная кирка", 1,
                ItemKind.Tool, ToolKind.Pickaxe, 120);
            var unknown = CreateItem(6, "Неопознанный образец", 1, ItemKind.HiddenSample);
            var iron = CreateItem(7, "Железная руда", 20);
            var recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.Configure(2, "Примитивная кирка", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 3 },
            }, pickaxe, 1);
            assets.Add(recipe);
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.Configure(new[] { stone, wood, pickaxe, unknown, iron }, new[] { recipe });
            assets.Add(catalog);
            var model = new InventoryModel(catalog);
            model.SetSlot(new InventorySlotReference(InventorySlotArea.Workbench, 0),
                new ItemStackState(wood.ItemId, 2));
            model.SetSlot(new InventorySlotReference(InventorySlotArea.Workbench, 1),
                new ItemStackState(stone.ItemId, 3));

            Assert.That(model.TryCraft(recipe), Is.True);
            Assert.That(model.ActiveStack.ItemId, Is.EqualTo(pickaxe.ItemId));
            Assert.That(model.ActiveStack.Condition, Is.EqualTo(120));
            Assert.That(model.DamageActiveTool(ToolKind.Pickaxe, 3, out var broke), Is.True);
            Assert.That(broke, Is.False);
            Assert.That(model.ActiveStack.Condition, Is.EqualTo(117));

            const ulong source = 99887766;
            var sample = new ItemStackState(
                unknown.ItemId, 1, 0, ResourceQuality.High, iron.ItemId, source, 50, 101);
            var replicated = sample.ForReplication();
            Assert.That(replicated.ItemId, Is.EqualTo(unknown.ItemId));
            Assert.That(replicated.Quantity, Is.EqualTo(1));
            Assert.That(replicated.HiddenItemId, Is.Zero);
            Assert.That(replicated.SourceNodeId, Is.EqualTo(source));
            Assert.That(replicated.Quality, Is.EqualTo(ResourceQuality.None));
            Assert.That(model.AutoInsert(sample, unknown.PickupPriority), Is.Zero);
            Assert.That(model.AutoInsert(new ItemStackState(
                unknown.ItemId, 1, 0, ResourceQuality.High, iron.ItemId, source, 50, 102),
                unknown.PickupPriority), Is.Zero);
            Assert.That(model.RevealSamples(source, 49), Is.Zero);
            Assert.That(model.RevealSamples(source, 50), Is.Zero);
            Assert.That(model.RevealSamples(source, 100), Is.EqualTo(2));
            Assert.That(model.Inventory.Any(stack => stack.ItemId == iron.ItemId
                && stack.Quantity == 2 && stack.Quality == ResourceQuality.High), Is.True);
            Assert.That(model.AutoInsert(
                new ItemStackState(iron.ItemId, 1, 0, ResourceQuality.Low),
                iron.PickupPriority), Is.Zero);
            Assert.That(model.Inventory.Count(stack => stack.ItemId == iron.ItemId), Is.EqualTo(2));
            Assert.That(model.DamageActiveTool(ToolKind.Pickaxe, 117, out broke), Is.True);
            Assert.That(broke, Is.True);
            Assert.That(model.ActiveStack.IsEmpty, Is.True);
        }

        [Test]
        public void ServerMiningWork_TreeCompletesOnceOnEighthAcceptedHit()
        {
            var managerObject = new GameObject("Mining rules network manager");
            assets.Add(managerObject);
            var manager = managerObject.AddComponent<NetworkManager>();
            var roleProperty = manager.LocalClient.GetType().GetProperty(
                "IsServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(roleProperty, Is.Not.Null);
            roleProperty.SetValue(manager.LocalClient, true);

            var streamerObject = new GameObject("Mining rules streamer");
            assets.Add(streamerObject);
            var streamer = streamerObject.AddComponent<WorldStreamer>();
            var serviceObject = new GameObject("Mining rules service");
            assets.Add(serviceObject);
            var service = serviceObject.AddComponent<ResourceWorldService>();
            service.Configure(manager, streamer);
            var treeObject = new GameObject("Tree mining node");
            assets.Add(treeObject);
            var tree = treeObject.AddComponent<ResourceNodeView>();
            tree.Initialize(12345, new ResourceNodeDescriptor(
                WorldObjectKind.Tree,
                2,
                ResourceCategory.Forage,
                DepositRichness.Ordinary,
                DepositReserveSize.VerySmall,
                1,
                ResourceQuality.None,
                1,
                requiredTool: ToolKind.Axe));

            for (var hit = 1; hit < ResourceBalance.TreeHitsRequired; hit++)
            {
                Assert.That(service.ApplyMiningHit(
                    tree, out var completed, out _, out var state), Is.True);
                Assert.That(completed, Is.False, $"Tree completed on hit {hit}.");
                Assert.That(state.RemainingReserves, Is.EqualTo(1));
            }
            Assert.That(service.ApplyMiningHit(
                tree, out var finalCompleted, out var extractionIndex, out var finalState),
                Is.True);
            Assert.That(finalCompleted, Is.True);
            Assert.That(extractionIndex, Is.EqualTo(1));
            Assert.That(finalState.RemainingReserves, Is.Zero);
            Assert.That(service.ApplyMiningHit(tree, out _, out _, out _), Is.False,
                "An exhausted tree must not grant a second yield.");
        }

        [Test]
        public void ServerInteractionValidation_RejectsFarBehindAndOccludedDeposit()
        {
            var playerObject = new GameObject("Interaction validation player");
            assets.Add(playerObject);
            var interaction = playerObject.AddComponent<PlayerResourceInteraction>();
            var depositObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            depositObject.name = "Interaction validation deposit";
            assets.Add(depositObject);
            Object.DestroyImmediate(depositObject.GetComponent<MeshRenderer>());
            depositObject.transform.position = new Vector3(0f, 1.1f, 2f);
            var deposit = depositObject.AddComponent<ResourceNodeView>();
            deposit.Initialize(7788, new ResourceNodeDescriptor(
                WorldObjectKind.Deposit,
                7,
                ResourceCategory.MetallicOre,
                DepositRichness.Ordinary,
                DepositReserveSize.Small,
                60,
                ResourceQuality.Normal,
                2,
                requiredTool: ToolKind.Pickaxe));
            var validate = typeof(PlayerResourceInteraction).GetMethod(
                "ValidateInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);

            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(interaction, new object[] { deposit }), Is.True);
            depositObject.transform.position = new Vector3(0f, 1.1f, -2f);
            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(interaction, new object[] { deposit }), Is.False);
            depositObject.transform.position = new Vector3(0f, 1.1f, 4f);
            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(interaction, new object[] { deposit }), Is.False);

            depositObject.transform.position = new Vector3(0f, 1.1f, 2f);
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Interaction validation obstacle";
            assets.Add(obstacle);
            obstacle.transform.position = new Vector3(0f, 1.1f, 1f);
            obstacle.transform.localScale = new Vector3(1f, 2f, 0.25f);
            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(interaction, new object[] { deposit }), Is.False);
        }

        [Test]
        public void ServerPlacementValidation_UsesAimRangeAndRejectsObstaclesAndSlope()
        {
            var playerObject = new GameObject("Placement validation player");
            assets.Add(playerObject);
            var interaction = playerObject.AddComponent<PlayerResourceInteraction>();
            var placedObject = new GameObject("Placement validation service");
            assets.Add(placedObject);
            var placed = placedObject.AddComponent<PlacedObjectWorldService>();
            placed.InitializeClient(WorldDefinition.CreateDefault(556677));
            var placedField = typeof(PlayerResourceInteraction).GetField(
                "placedObjects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(placedField, Is.Not.Null);
            placedField.SetValue(interaction, placed);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Placement validation ground";
            assets.Add(ground);
            ground.AddComponent<WorldChunkView>();
            ground.transform.position = new Vector3(0f, -0.1f, 5f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 20f);
            var validate = typeof(PlayerResourceInteraction).GetMethod(
                "ValidatePlacementServer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);

            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(
                interaction, new object[] { new Vector3(0f, 0f, 5f), 0f }), Is.True);
            Assert.That((bool)validate.Invoke(
                interaction, new object[] { new Vector3(0f, 0f, 11f), 0f }), Is.False,
                "Placement farther than the ten-metre aim range must be rejected.");

            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Placement validation obstacle";
            assets.Add(obstacle);
            obstacle.transform.position = new Vector3(0f, 0.9f, 2.5f);
            obstacle.transform.localScale = new Vector3(1.2f, 1.8f, 0.25f);
            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(
                interaction, new object[] { new Vector3(0f, 0f, 5f), 0f }), Is.False,
                "Placement through an obstacle must be rejected.");

            Object.DestroyImmediate(obstacle);
            assets.Remove(obstacle);
            ground.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            Physics.SyncTransforms();
            Assert.That((bool)validate.Invoke(
                interaction, new object[] { new Vector3(0f, 0f, 5f), 0f }), Is.False,
                "A slope outside the supported limit must be rejected.");
        }

        private ItemDefinition CreateItem(
            ushort id,
            string name,
            ushort maximum,
            ItemKind kind = ItemKind.Resource,
            ToolKind tool = ToolKind.None,
            ushort durability = 0)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.Configure(id, name, maximum,
                tool == ToolKind.None
                    ? PickupPlacementPriority.InventoryFirst
                    : PickupPlacementPriority.HotbarFirst,
                Color.white, kind: kind, configuredTool: tool,
                configuredToolTier: tool == ToolKind.None ? (byte)0 : (byte)1,
                configuredMaximumDurability: durability);
            assets.Add(item);
            return item;
        }

        private static List<string> GenerateBaseDepositSignatures(
            DeterministicChunkGenerator generator,
            WorldDefinition definition)
        {
            var signatures = new List<string>();
            for (var z = 0; z < definition.ChunkCountZ; z++)
            {
                for (var x = 0; x < definition.ChunkCountX; x++)
                {
                    foreach (var spawn in generator.GenerateObjectsForMap(
                                 definition, new ChunkCoord(x, z)))
                    {
                        if (spawn.Resource.Kind != WorldObjectKind.Deposit
                            || spawn.Resource.ResourceItemId > 16) continue;
                        signatures.Add($"{spawn.InstanceId}:{spawn.Position.x:R}:"
                            + $"{spawn.Position.z:R}:{spawn.Resource.ResourceItemId}:"
                            + $"{(byte)spawn.Resource.Richness}:{spawn.Resource.InitialReserves}:"
                            + $"{(byte)spawn.Resource.Quality}:{spawn.Resource.Hardness}:"
                            + $"{spawn.Resource.ImpurityItemId}:"
                            + $"{spawn.Resource.ImpurityChancePercent}");
                    }
                }
            }
            return signatures;
        }
    }
}
