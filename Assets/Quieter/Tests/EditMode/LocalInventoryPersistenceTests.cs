using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.Persistence;
using Quieter.Survival;
using Quieter.World;
using UnityEngine;

namespace Quieter.Tests
{
    public sealed class LocalInventoryPersistenceTests
    {
        private string path;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), $"quieter-inventory-{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }

        [Test]
        public async Task InventoryAndSelectedHotbar_SurviveRepositoryRestart()
        {
            const ulong steamId = 76561198000000101;
            var first = new LocalJsonRepository(path);
            await first.LoginAsync(steamId, "Collector", Vector3.up * 8f);
            await first.SaveInventoryAsync(steamId, new[]
            {
                new StoredInventorySlot { SlotIndex = 0, ItemId = 1, Quantity = 20 },
                new StoredInventorySlot
                {
                    SlotIndex = 24,
                    ItemId = 5,
                    Quantity = 1,
                    Condition = 83,
                    Quality = (byte)ResourceQuality.High,
                    HiddenItemId = 7,
                    SourceNodeId = "18446744073709551000",
                    RevealAtPercent = 50,
                    SampleId = "18446744073709550001",
                },
                new StoredInventorySlot
                {
                    SlotIndex = 2,
                    ItemId = 39,
                    Quantity = 1,
                    ItemInstanceId = "18446744073709550002",
                    Wetness = 2300,
                    Cleanliness = 8100,
                    Equipped = true,
                },
            }, new[]
            {
                new StoredInventorySlot
                {
                    ItemId = 6,
                    Quantity = 1,
                    HiddenItemId = 7,
                    SourceNodeId = "9991",
                    RevealAtPercent = 50,
                    SampleId = "9992",
                },
            }, 4);

            var reopened = new LocalJsonRepository(path);
            var profile = await reopened.LoginAsync(steamId, "Collector", Vector3.zero);

            Assert.That(profile.SelectedHotbarIndex, Is.EqualTo(4));
            Assert.That(profile.InventorySlots, Has.Count.EqualTo(3));
            Assert.That(profile.InventorySlots[0].Quantity, Is.EqualTo(20));
            Assert.That(profile.InventorySlots[1].ItemId, Is.EqualTo(5));
            Assert.That(profile.InventorySlots[1].Condition, Is.EqualTo(83));
            Assert.That(profile.InventorySlots[1].Quality, Is.EqualTo((byte)ResourceQuality.High));
            Assert.That(profile.InventorySlots[1].HiddenItemId, Is.EqualTo(7));
            Assert.That(profile.InventorySlots[1].SourceNodeId,
                Is.EqualTo("18446744073709551000"));
            Assert.That(profile.InventorySlots[1].RevealAtPercent, Is.EqualTo(50));
            Assert.That(profile.InventorySlots[1].SampleId,
                Is.EqualTo("18446744073709550001"));
            Assert.That(profile.InventorySlots[2].ItemId, Is.EqualTo(39));
            Assert.That(profile.InventorySlots[2].Wetness, Is.EqualTo(2300));
            Assert.That(profile.InventorySlots[2].Cleanliness, Is.EqualTo(8100));
            Assert.That(profile.InventorySlots[2].Equipped, Is.True);
            Assert.That(profile.PendingItems, Has.Count.EqualTo(1));
            Assert.That(profile.PendingItems[0].SampleId, Is.EqualTo("9992"));
        }

        [Test]
        public async Task AtomicSnapshot_KeepsBodyInventoryPositionTogetherAndRejectsResurrection()
        {
            const ulong steamId = 76561198000000251;
            var repository = new LocalJsonRepository(path);
            var profile = await repository.LoginAsync(steamId, "Survivor", Vector3.up * 8f);
            var body = profile.Survival;
            body.Revision = 2;
            body.Physiology.Hydration = 0.3f;
            var items = new[] { new StoredInventorySlot { SlotIndex = 0, ItemId = 30,
                Quantity = 1, ItemInstanceId = "990", LiquidKind = 1, LiquidMilliliters = 1200 } };
            await repository.SaveSnapshotAsync(steamId, new Vector3(20f, 8f, 0f), items,
                Array.Empty<StoredInventorySlot>(), 0, body);
            body.Revision = 1;
            body.Physiology.Hydration = 1f;
            await repository.SaveSnapshotAsync(steamId, Vector3.zero, Array.Empty<StoredInventorySlot>(),
                Array.Empty<StoredInventorySlot>(), 0, body);
            var restored = await new LocalJsonRepository(path).LoginAsync(steamId, "Survivor", Vector3.zero);
            Assert.That(restored.Position.x, Is.EqualTo(20f));
            Assert.That(restored.Survival.Physiology.Hydration, Is.EqualTo(0.3f));
            Assert.That(restored.InventorySlots[0].LiquidMilliliters, Is.EqualTo(1200));
            body.Revision = 3;
            body.Physiology.LifeState = CharacterLifeState.Dead;
            body.Physiology.DeathCause = DeathCause.BloodLoss;
            await repository.SaveSnapshotAsync(steamId, restored.Position, items,
                Array.Empty<StoredInventorySlot>(), 0, body);
            body.Revision = 4;
            body.Physiology.LifeState = CharacterLifeState.Conscious;
            body.Physiology.DeathCause = DeathCause.None;
            Assert.ThrowsAsync<InvalidOperationException>(async () => await repository.SaveSnapshotAsync(
                steamId, Vector3.zero, Array.Empty<StoredInventorySlot>(),
                Array.Empty<StoredInventorySlot>(), 0, body));
        }

        [Test]
        public async Task PlacedResearchTableAndItsSample_SurviveRepositoryRestart()
        {
            var repository = new LocalJsonRepository(path);
            var world = await repository.GetOrCreateWorldAsync();
            await repository.SavePlacedObjectsAsync(world.WorldId, new[]
            {
                new StoredPlacedObject
                {
                    WorldId = world.WorldId,
                    ObjectId = "18446744073709550002",
                    OwnerAccountId = "76561198000000103",
                    ItemId = 24,
                    X = 12.5f,
                    Y = 4.25f,
                    Z = -8.75f,
                    Yaw = 45f,
                    Locked = false,
                    Input = new StoredInventorySlot
                    {
                        ItemId = 6,
                        Quantity = 1,
                        HiddenItemId = 11,
                        SourceNodeId = "445566",
                        RevealAtPercent = 50,
                        SampleId = "778899",
                    },
                    CreatedAtUtc = "2026-09-01T00:00:00.0000000Z",
                    UpdatedAtUtc = "2026-09-01T00:01:00.0000000Z",
                },
            });

            var reopened = new LocalJsonRepository(path);
            var objects = await reopened.LoadPlacedObjectsAsync(world.WorldId);
            Assert.That(objects, Has.Count.EqualTo(1));
            Assert.That(objects[0].ObjectId, Is.EqualTo("18446744073709550002"));
            Assert.That(objects[0].Yaw, Is.EqualTo(45f));
            Assert.That(objects[0].OwnerAccountId, Is.EqualTo("76561198000000103"));
            Assert.That(objects[0].Input.ItemId, Is.EqualTo(6));
            Assert.That(objects[0].Input.HiddenItemId, Is.EqualTo(11));
            Assert.That(objects[0].Input.SourceNodeId, Is.EqualTo("445566"));
            Assert.That(objects[0].Input.SampleId, Is.EqualTo("778899"));
        }

        [Test]
        public async Task ResourceNodesAndPersonalKnowledge_SurviveRestartAndStayWorldScoped()
        {
            const ulong steamId = 76561198000000103;
            const string nodeId = "18446744073709551001";
            var first = new LocalJsonRepository(path);
            var world = await first.GetOrCreateWorldAsync();
            var original = await first.LoginAsync(steamId, "Prospector", Vector3.up * 8f);
            await first.SaveResourceNodeStatesAsync(world.WorldId, new[]
            {
                new StoredResourceNodeState
                {
                    WorldId = world.WorldId,
                    InstanceId = nodeId,
                    RemainingReserves = 17,
                    AvailableAtUtc = "2026-08-30T18:00:00.0000000Z",
                },
            });
            await first.SaveDepositKnowledgeAsync(steamId, original.Survival.CharacterId,
                world.WorldId, new[]
            {
                new StoredDepositKnowledge
                {
                    WorldId = world.WorldId,
                    InstanceId = nodeId,
                    StudyBasisPoints = 7500,
                    DiscoveredAtUtc = "2026-08-30T17:00:00.0000000Z",
                },
            });

            var reopened = new LocalJsonRepository(path);
            var nodes = await reopened.LoadResourceNodeStatesAsync(world.WorldId);
            var profile = await reopened.LoginAsync(steamId, "Prospector", Vector3.zero);

            Assert.That(nodes, Has.Count.EqualTo(1));
            Assert.That(nodes[0].RemainingReserves, Is.EqualTo(17));
            Assert.That(nodes[0].WorldId, Is.EqualTo(world.WorldId));
            Assert.That(profile.DepositKnowledge, Has.Count.EqualTo(1));
            Assert.That(profile.DepositKnowledge[0].StudyBasisPoints, Is.EqualTo(7500));
            Assert.That(profile.DepositKnowledge[0].WorldId, Is.EqualTo(world.WorldId));
            Assert.That(await reopened.LoadResourceNodeStatesAsync(world.WorldId + 1), Is.Empty);
        }

        [Test]
        public async Task PhysicalMapNotes_SurviveRestartAndFollowItemBetweenPlayers()
        {
            const ulong firstSteamId = 76561198000000104;
            const ulong secondSteamId = 76561198000000105;
            const string mapItemInstanceId = "18446744073709551001";
            var first = new LocalJsonRepository(path);
            var world = await first.GetOrCreateWorldAsync();
            await first.LoginAsync(firstSteamId, "Cartographer", Vector3.up * 8f);
            await first.LoginAsync(secondSteamId, "Stranger", Vector3.up * 8f);
            await first.SaveInventoryAsync(firstSteamId, new[]
            {
                new StoredInventorySlot
                {
                    SlotIndex = 0,
                    ItemId = 36,
                    Quantity = 1,
                    ItemInstanceId = mapItemInstanceId,
                },
            }, Array.Empty<StoredInventorySlot>(), 0);
            await first.SaveMapNotesAsync(firstSteamId, world.WorldId, new[]
            {
                new StoredMapNote
                {
                    WorldId = world.WorldId,
                    MapItemInstanceId = mapItemInstanceId,
                    NoteId = "18446744073709551002",
                    X = 125.5f,
                    Z = -321.25f,
                    Text = "Большая глиняная залежь",
                    CreatedAtUtc = "2026-08-31T12:00:00.0000000Z",
                    UpdatedAtUtc = "2026-08-31T12:05:00.0000000Z",
                },
            });

            var reopened = new LocalJsonRepository(path);
            var owner = await reopened.LoginAsync(firstSteamId, "Cartographer", Vector3.zero);
            var stranger = await reopened.LoginAsync(secondSteamId, "Stranger", Vector3.zero);

            Assert.That(owner.MapNotes, Has.Count.EqualTo(1));
            Assert.That(owner.MapNotes[0].NoteId, Is.EqualTo("18446744073709551002"));
            Assert.That(owner.MapNotes[0].Text, Is.EqualTo("Большая глиняная залежь"));
            Assert.That(owner.MapNotes[0].X, Is.EqualTo(125.5f));
            Assert.That(owner.MapNotes[0].WorldId, Is.EqualTo(world.WorldId));
            Assert.That(stranger.MapNotes, Is.Empty);

            await reopened.SaveInventoryAsync(
                firstSteamId,
                Array.Empty<StoredInventorySlot>(),
                Array.Empty<StoredInventorySlot>(),
                0);
            await reopened.SaveInventoryAsync(secondSteamId, new[]
            {
                new StoredInventorySlot
                {
                    SlotIndex = 0,
                    ItemId = 36,
                    Quantity = 1,
                    ItemInstanceId = mapItemInstanceId,
                },
            }, Array.Empty<StoredInventorySlot>(), 0);
            var previousOwner = await reopened.LoginAsync(
                firstSteamId, "Cartographer", Vector3.zero);
            var newOwner = await reopened.LoginAsync(
                secondSteamId, "Stranger", Vector3.zero);
            Assert.That(previousOwner.MapNotes, Is.Empty);
            Assert.That(newOwner.MapNotes, Has.Count.EqualTo(1));
            Assert.That(newOwner.MapNotes[0].Text, Is.EqualTo("Большая глиняная залежь"));
        }

        [Test]
        public async Task OldProfileWithoutInventory_LoadsWithEmptySlots()
        {
            const ulong steamId = 76561198000000102;
            File.WriteAllText(path, "{\"HasWorld\":false,\"Players\":[{"
                + $"\"SteamId\":\"{steamId}\",\"DisplayName\":\"Old\","
                + "\"Position\":{\"x\":1,\"y\":8,\"z\":2},"
                + "\"CreatedAtUtc\":\"2026-08-01T00:00:00.0000000Z\","
                + "\"LastSeenAtUtc\":\"2026-08-01T00:00:00.0000000Z\"}]}");

            var repository = new LocalJsonRepository(path);
            var profile = await repository.LoginAsync(steamId, "Old", Vector3.zero);

            Assert.That(profile.InventorySlots, Is.Empty);
            Assert.That(profile.PendingItems, Is.Empty);
            Assert.That(profile.MapNotes, Is.Empty);
            Assert.That(profile.SelectedHotbarIndex, Is.Zero);
            Assert.That(profile.Position, Is.EqualTo(new Vector3(1f, 8f, 2f)));
        }

        [Test]
        public async Task NewStranger_KeepsOldBodyAndItemsAcrossRepositoryRestart()
        {
            const ulong steamId = 76561198000000122;
            var repository = new LocalJsonRepository(path);
            var first = await repository.LoginAsync(steamId, "Mortal", new Vector3(0f, 8f, 0f));
            first.Survival.Revision = 5;
            first.Survival.Physiology.LifeState = CharacterLifeState.Dead;
            first.Survival.Physiology.DeathCause = DeathCause.BloodLoss;
            var bodyItem = new[]
            {
                new StoredInventorySlot
                {
                    SlotIndex = 0, ItemId = 36, Quantity = 1, ItemInstanceId = "901",
                },
            };
            await repository.SaveSnapshotAsync(
                steamId, new Vector3(14f, 7f, -9f), bodyItem,
                Array.Empty<StoredInventorySlot>(), 0, first.Survival);
            var operationId = Guid.NewGuid().ToString("D");
            var replacement = await repository.CreateNewStrangerAsync(
                steamId, operationId, first.Survival.CharacterId, 5,
                new Vector3(0f, 8f, 0f));
            var retry = await repository.CreateNewStrangerAsync(
                steamId, operationId, first.Survival.CharacterId, 5,
                new Vector3(100f, 8f, 100f));

            Assert.That(retry.Survival.CharacterId, Is.EqualTo(replacement.Survival.CharacterId));
            Assert.That(replacement.InventorySlots, Is.Empty);
            Assert.That(replacement.Survival.CreationCompleted, Is.False);

            var reopened = new LocalJsonRepository(path);
            var bodies = await reopened.LoadWorldCharactersAsync();
            Assert.That(bodies, Has.Count.EqualTo(1));
            Assert.That(bodies[0].SteamId, Is.Zero);
            Assert.That(bodies[0].Position, Is.EqualTo(new Vector3(14f, 7f, -9f)));
            Assert.That(bodies[0].InventorySlots, Has.Count.EqualTo(1));
            Assert.That(bodies[0].InventorySlots[0].ItemInstanceId, Is.EqualTo("901"));
            var loggedIn = await reopened.LoginAsync(steamId, "Mortal", Vector3.zero);
            Assert.That(loggedIn.Survival.CharacterId, Is.EqualTo(replacement.Survival.CharacterId));
        }

        [Test]
        public async Task CharacterPairTransfer_IsAtomicAndIdempotentInLocalWorld()
        {
            const ulong steamId = 76561198000000123;
            var repository = new LocalJsonRepository(path);
            var first = await repository.LoginAsync(steamId, "Looter", Vector3.up * 8f);
            first.Survival.Revision = 2;
            first.Survival.Physiology.LifeState = CharacterLifeState.Dead;
            first.Survival.Physiology.DeathCause = DeathCause.BloodLoss;
            var item = new StoredInventorySlot
            {
                SlotIndex = 0, ItemId = 36, Quantity = 1, ItemInstanceId = "902",
            };
            await repository.SaveSnapshotAsync(steamId, new Vector3(5f, 8f, 5f),
                new[] { item }, Array.Empty<StoredInventorySlot>(), 0, first.Survival);
            var next = await repository.CreateNewStrangerAsync(
                steamId, Guid.NewGuid().ToString("D"), first.Survival.CharacterId, 2,
                Vector3.up * 8f);
            var body = (await repository.LoadWorldCharactersAsync())[0];
            body.Survival.Revision++;
            next.Survival.Revision = 1;
            var operationId = Guid.NewGuid().ToString("D");
            var source = new CharacterPersistenceSnapshot
            {
                Position = body.Position, Slots = Array.Empty<StoredInventorySlot>(),
                PendingItems = Array.Empty<StoredInventorySlot>(), Survival = body.Survival,
            };
            var destination = new CharacterPersistenceSnapshot
            {
                Position = next.Position, Slots = new[] { item },
                PendingItems = Array.Empty<StoredInventorySlot>(), Survival = next.Survival,
            };

            await repository.SaveCharacterPairAsync(operationId, source, steamId, destination);
            await repository.SaveCharacterPairAsync(operationId, source, steamId, destination);

            var reopened = new LocalJsonRepository(path);
            Assert.That((await reopened.LoadWorldCharactersAsync())[0].InventorySlots, Is.Empty);
            var controlled = await reopened.LoginAsync(steamId, "Looter", Vector3.zero);
            Assert.That(controlled.InventorySlots, Has.Count.EqualTo(1));
            Assert.That(controlled.InventorySlots[0].ItemInstanceId, Is.EqualTo("902"));
        }

        [Test]
        public async Task RegisteredHeir_AtomicallyKeepsEachBodiesOwnInventory()
        {
            const ulong steamId = 76561198000000131;
            var repository = new LocalJsonRepository(path);
            var world = await repository.GetOrCreateWorldAsync();
            var owner = await repository.LoginAsync(steamId, "Owner", Vector3.up * 8f);
            owner.Survival.Revision = 1;
            await repository.SaveSnapshotAsync(steamId, owner.Position, new[]
            {
                new StoredInventorySlot
                {
                    SlotIndex = 0, ItemId = 49, Quantity = 1, ItemInstanceId = "991",
                },
            }, Array.Empty<StoredInventorySlot>(), 0, owner.Survival);
            var heirState = new CharacterSurvivalState
            {
                CharacterId = Guid.NewGuid().ToString("D"),
                CharacterName = "Heir",
                ControlKind = CharacterControlKind.ContractedNpc,
                CreationCompleted = true,
                Revision = 1,
                WorkerContract = new WorkerContractState
                {
                    Active = true,
                    Voluntary = true,
                    EmployerAccountId = steamId.ToString(),
                    FulfilledContractGameSeconds =
                        LivingWorldSimulation.RequiredHeirContractGameSeconds,
                },
            };
            heirState.EnsureInitialized();
            heirState.Relationships.Add(new RelationshipState
            {
                TargetCharacterId = owner.Survival.CharacterId,
                VoluntaryLoyalty = true,
                PersonalRequestsCompleted = 3,
            });
            await repository.CreateWorldNpcAsync("Heir", new CharacterPersistenceSnapshot
            {
                Position = new Vector3(45f, 8f, 7f),
                Slots = new[] { new StoredInventorySlot { SlotIndex = 0, ItemId = 25, Quantity = 2 } },
                PendingItems = Array.Empty<StoredInventorySlot>(),
                Survival = heirState,
            });
            await repository.SavePlacedObjectsAsync(world.WorldId, new[]
            {
                new StoredPlacedObject
                {
                    WorldId = world.WorldId, ObjectId = "992", ItemId = 48,
                    OwnerAccountId = steamId.ToString(),
                    AssignedCharacterId = heirState.CharacterId,
                },
            });

            var registered = await repository.RegisterHeirAsync(
                steamId, heirState.CharacterId, 0);
            Assert.That(registered.RegisteredHeirCharacterId,
                Is.EqualTo(heirState.CharacterId));
            owner = await repository.LoginAsync(steamId, "Owner", Vector3.zero);
            owner.Survival.Revision = 2;
            owner.Survival.Physiology.LifeState = CharacterLifeState.Dead;
            owner.Survival.Physiology.DeathCause = DeathCause.BloodLoss;
            await repository.SaveSnapshotAsync(steamId, owner.Position, owner.InventorySlots,
                owner.PendingItems, owner.SelectedHotbarIndex, owner.Survival);
            var operation = Guid.NewGuid().ToString("D");
            var assumed = await repository.AssumeRegisteredHeirAsync(
                steamId, operation, owner.Survival.CharacterId, 2);
            var retry = await repository.AssumeRegisteredHeirAsync(
                steamId, operation, owner.Survival.CharacterId, 2);

            Assert.That(assumed.Survival.CharacterId, Is.EqualTo(heirState.CharacterId));
            Assert.That(retry.Survival.CharacterId, Is.EqualTo(heirState.CharacterId));
            Assert.That(assumed.Position, Is.EqualTo(new Vector3(45f, 8f, 7f)));
            Assert.That(assumed.InventorySlots[0].ItemId, Is.EqualTo(25));
            var oldBody = (await new LocalJsonRepository(path).LoadWorldCharactersAsync())[0];
            Assert.That(oldBody.InventorySlots[0].ItemId, Is.EqualTo(49));
        }

        [Test]
        public async Task CapturedLife_BecomesDetachedForcedNpcWhenNewStrangerStarts()
        {
            const ulong steamId = 76561198000000124;
            var repository = new LocalJsonRepository(path);
            var first = await repository.LoginAsync(steamId, "Captured", Vector3.up * 8f);
            first.Survival.Revision = 3;
            first.Survival.ControlKind = CharacterControlKind.ForcedNpc;
            var item = new StoredInventorySlot
            {
                SlotIndex = 0, ItemId = 30, Quantity = 1, ItemInstanceId = "903",
            };
            await repository.SaveSnapshotAsync(steamId, new Vector3(7f, 8f, 2f),
                new[] { item }, Array.Empty<StoredInventorySlot>(), 0, first.Survival);
            var restoredPlayerState = JsonUtility.FromJson<CharacterSurvivalState>(
                JsonUtility.ToJson(first.Survival));
            restoredPlayerState.Revision = 4;
            restoredPlayerState.ControlKind = CharacterControlKind.Player;
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await repository.SaveSnapshotAsync(steamId, Vector3.zero,
                    Array.Empty<StoredInventorySlot>(), Array.Empty<StoredInventorySlot>(),
                    0, restoredPlayerState));

            var next = await repository.CreateNewStrangerAsync(steamId,
                Guid.NewGuid().ToString("D"), first.Survival.CharacterId, 3,
                Vector3.up * 8f);
            Assert.That(next.Survival.CharacterId, Is.Not.EqualTo(first.Survival.CharacterId));
            var bodies = await new LocalJsonRepository(path).LoadWorldCharactersAsync();
            Assert.That(bodies, Has.Count.EqualTo(1));
            Assert.That(bodies[0].Survival.ControlKind,
                Is.EqualTo(CharacterControlKind.ForcedNpc));
            Assert.That(bodies[0].InventorySlots[0].ItemInstanceId, Is.EqualTo("903"));
        }

        [Test]
        public async Task FreeNpcCreation_IsIdempotentAndSurvivesRestart()
        {
            var repository = new LocalJsonRepository(path);
            var survival = new CharacterSurvivalState
            {
                CharacterId = Guid.NewGuid().ToString("D"),
                CharacterName = "Mira",
                ControlKind = CharacterControlKind.FreeNpc,
                CreationCompleted = true,
                Revision = 1,
            };
            survival.EnsureInitialized();
            var snapshot = new CharacterPersistenceSnapshot
            {
                Position = new Vector3(80f, 0f, -70f),
                Slots = new[] { new StoredInventorySlot { SlotIndex = 0, ItemId = 25, Quantity = 3 } },
                PendingItems = Array.Empty<StoredInventorySlot>(),
                SelectedHotbarIndex = 0,
                Survival = survival,
            };
            var first = await repository.CreateWorldNpcAsync("Mira", snapshot);
            var retry = await repository.CreateWorldNpcAsync("Changed", snapshot);
            Assert.That(retry.Survival.CharacterId, Is.EqualTo(first.Survival.CharacterId));
            var loaded = await new LocalJsonRepository(path).LoadWorldCharactersAsync();
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].SteamId, Is.Zero);
            Assert.That(loaded[0].Survival.ControlKind,
                Is.EqualTo(CharacterControlKind.FreeNpc));
        }
    }
}
