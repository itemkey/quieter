using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.Persistence;
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
                },
            }, 4);

            var reopened = new LocalJsonRepository(path);
            var profile = await reopened.LoginAsync(steamId, "Collector", Vector3.zero);

            Assert.That(profile.SelectedHotbarIndex, Is.EqualTo(4));
            Assert.That(profile.InventorySlots, Has.Count.EqualTo(2));
            Assert.That(profile.InventorySlots[0].Quantity, Is.EqualTo(20));
            Assert.That(profile.InventorySlots[1].ItemId, Is.EqualTo(5));
            Assert.That(profile.InventorySlots[1].Condition, Is.EqualTo(83));
            Assert.That(profile.InventorySlots[1].Quality, Is.EqualTo((byte)ResourceQuality.High));
            Assert.That(profile.InventorySlots[1].HiddenItemId, Is.EqualTo(7));
            Assert.That(profile.InventorySlots[1].SourceNodeId,
                Is.EqualTo("18446744073709551000"));
            Assert.That(profile.InventorySlots[1].RevealAtPercent, Is.EqualTo(50));
        }

        [Test]
        public async Task ResourceNodesAndPersonalKnowledge_SurviveRestartAndStayWorldScoped()
        {
            const ulong steamId = 76561198000000103;
            const string nodeId = "18446744073709551001";
            var first = new LocalJsonRepository(path);
            var world = await first.GetOrCreateWorldAsync();
            await first.LoginAsync(steamId, "Prospector", Vector3.up * 8f);
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
            await first.SaveDepositKnowledgeAsync(steamId, world.WorldId, new[]
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
        public async Task PersonalMapNotes_SurviveRestartAndRemainPlayerAndWorldScoped()
        {
            const ulong firstSteamId = 76561198000000104;
            const ulong secondSteamId = 76561198000000105;
            var first = new LocalJsonRepository(path);
            var world = await first.GetOrCreateWorldAsync();
            await first.LoginAsync(firstSteamId, "Cartographer", Vector3.up * 8f);
            await first.LoginAsync(secondSteamId, "Stranger", Vector3.up * 8f);
            await first.SaveMapNotesAsync(firstSteamId, world.WorldId, new[]
            {
                new StoredMapNote
                {
                    WorldId = world.WorldId,
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

            await reopened.SaveMapNotesAsync(firstSteamId, world.WorldId + 1, new[]
            {
                new StoredMapNote
                {
                    WorldId = world.WorldId + 1,
                    NoteId = "77",
                    Text = "Другой мир",
                },
            });
            var withSecondWorld = await reopened.LoginAsync(
                firstSteamId, "Cartographer", Vector3.zero);
            Assert.That(withSecondWorld.MapNotes, Has.Count.EqualTo(2));
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
            Assert.That(profile.MapNotes, Is.Empty);
            Assert.That(profile.SelectedHotbarIndex, Is.Zero);
            Assert.That(profile.Position, Is.EqualTo(new Vector3(1f, 8f, 2f)));
        }
    }
}
