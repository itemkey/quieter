using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Quieter.ProfileService.Contracts;
using Quieter.ProfileService.Data;
using Xunit;

namespace Quieter.ProfileService.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public async Task World_IsCreatedOnce_AndKeepsSeed()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var first = await store.GetOrCreateWorldAsync(default);
        var second = await store.GetOrCreateWorldAsync(default);

        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(1, second.WorldId);
        Assert.Equal(ProfileStore.CurrentGeneratorVersion, second.GeneratorVersion);
        Assert.Equal((ushort)32, second.ChunkCountX);
        Assert.Equal((ushort)64, second.ChunkSize);
    }

    [Fact]
    public async Task OldWorld_IsRejectedWithoutBeingSilentlyUpgraded()
    {
        await using var database = CreateDatabase();
        database.Worlds.Add(new WorldEntity
        {
            Id = 1,
            Seed = 42,
            GeneratorVersion = 5,
            ChunkCountX = 32,
            ChunkCountZ = 32,
            ChunkSize = 64,
            SamplesPerSide = 33,
            HeightStep = 0.25f,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProfileStore(database).GetOrCreateWorldAsync(default));
        Assert.Contains("explicit survival-world reset", error.Message);
        Assert.Equal((ushort)5, (await database.Worlds.SingleAsync()).GeneratorVersion);
    }

    [Fact]
    public async Task Login_CreatesProfile_AndPositionSurvivesReconnect()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var created = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000001", "Player", 0f, 8f, 0f),
            default);
        Assert.Equal(8f, created.PositionY);

        var saved = await store.SavePositionAsync(
            created.SteamId,
            new PositionRequest(12f, 9f, -4f),
            default);
        Assert.True(saved);

        var reconnected = await store.LoginAsync(
            new PlayerLoginRequest(created.SteamId, "Renamed", 0f, 8f, 0f),
            default);
        Assert.Equal(12f, reconnected.PositionX);
        Assert.Equal(-4f, reconnected.PositionZ);
        Assert.Equal("Renamed", reconnected.DisplayName);
    }

    [Fact]
    public async Task CharacterAggregate_IsSeparateAndRejectsStaleRevision()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var created = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000021", "Survivor", 0f, 8f, 0f),
            default);

        Assert.True(Guid.TryParse(created.CharacterId, out _));
        Assert.Equal(0, created.SurvivalRevision);
        Assert.True(await store.SaveSurvivalAsync(
            created.SteamId,
            new SurvivalRequest("{\"characterName\":\"Survivor\",\"hydration\":0.72}", 5),
            default));
        Assert.True(await store.SaveSurvivalAsync(
            created.SteamId,
            new SurvivalRequest("{\"characterName\":\"stale\"}", 4),
            default));

        var restored = await store.LoginAsync(
            new PlayerLoginRequest(created.SteamId, "Survivor", 0f, 8f, 0f),
            default);
        Assert.Equal(5, restored.SurvivalRevision);
        Assert.Contains("0.72", restored.SurvivalJson);
        Assert.DoesNotContain("stale", restored.SurvivalJson);
        Assert.Equal(created.CharacterId, restored.CharacterId);
    }

    [Fact]
    public async Task ConcurrentPositionUpdates_AreAtomicAndKeepACompletePosition()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using (var setup = CreateDatabase(databaseName, root))
        {
            await new ProfileStore(setup).LoginAsync(
                new PlayerLoginRequest("76561198000000002", "Concurrent", 0f, 8f, 0f),
                default);
        }

        await using var firstDatabase = CreateDatabase(databaseName, root);
        await using var secondDatabase = CreateDatabase(databaseName, root);
        var first = new ProfileStore(firstDatabase).SavePositionAsync(
            "76561198000000002",
            new PositionRequest(10f, 11f, 12f),
            default);
        var second = new ProfileStore(secondDatabase).SavePositionAsync(
            "76561198000000002",
            new PositionRequest(-10f, -11f, -12f),
            default);
        Assert.All(await Task.WhenAll(first, second), Assert.True);

        await using var verification = CreateDatabase(databaseName, root);
        var player = await verification.Players.SingleAsync();
        var position = (player.PositionX, player.PositionY, player.PositionZ);
        Assert.True(
            position == (10f, 11f, 12f) || position == (-10f, -11f, -12f),
            $"Position was torn: {position}");
    }

    [Fact]
    public async Task Inventory_IsSavedAndRestoredForExistingProfile()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var created = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000003", "Crafter", 0f, 8f, 0f),
            default);
        Assert.Empty(created.InventorySlots);

        Assert.True(await store.SaveInventoryAsync(
            created.SteamId,
            new InventoryRequest(3, new[]
            {
                new InventorySlotResponse(0, 1, 20),
                new InventorySlotResponse(
                    24, 5, 1, 83, 4, 7, "18446744073709551000", 50,
                    "18446744073709550000"),
                new InventorySlotResponse(
                    2, 39, 1, ItemInstanceId: "18446744073709550002",
                    Wetness: 2300, Cleanliness: 8100, Equipped: true),
            }, new[]
            {
                new InventorySlotResponse(
                    0, 6, 1, 0, 4, 7, "10001", 50, "10002"),
            }),
            default));

        var restored = await store.LoginAsync(
            new PlayerLoginRequest(created.SteamId, "Crafter", 0f, 8f, 0f),
            default);
        Assert.Equal((byte)3, restored.SelectedHotbarIndex);
        Assert.Collection(restored.InventorySlots,
            slot => Assert.Equal(new InventorySlotResponse(0, 1, 20), slot),
            slot => Assert.Equal(new InventorySlotResponse(
                2, 39, 1, ItemInstanceId: "18446744073709550002",
                Wetness: 2300, Cleanliness: 8100, Equipped: true), slot),
            slot => Assert.Equal(new InventorySlotResponse(
                24, 5, 1, 83, 4, 7, "18446744073709551000", 50,
                "18446744073709550000"), slot));
        Assert.Collection(restored.PendingItems,
            item => Assert.Equal(new InventorySlotResponse(
                0, 6, 1, 0, 4, 7, "10001", 50, "10002"), item));
    }

    [Fact]
    public async Task PlacedResearchTableAndInput_AreSavedAndRestored()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var world = await store.GetOrCreateWorldAsync(default);
        var created = DateTime.UtcNow.AddMinutes(-2);
        var updated = DateTime.UtcNow;
        Assert.True(await store.SavePlacedObjectsAsync(
            world.WorldId,
            new PlacedObjectListResponse(new[]
            {
                new PlacedObjectResponse(
                    "18446744073709550001",
                    24,
                    12.5f,
                    4.25f,
                    -8.75f,
                    45f,
                    new InventorySlotResponse(0, 6, 1, 0, 4, 11, "445566", 50, "778899"),
                    created,
                    updated),
            }),
            default));

        var restored = await store.LoadPlacedObjectsAsync(world.WorldId, default);
        var table = Assert.Single(restored.Objects);
        Assert.Equal("18446744073709550001", table.ObjectId);
        Assert.Equal((ushort)24, table.ItemId);
        Assert.Equal(45f, table.Yaw);
        Assert.Equal("778899", table.Input?.SampleId);
        Assert.Equal("445566", table.Input?.SourceNodeId);
    }

    [Fact]
    public async Task ResourceState_IsShared_AndKnowledgeRemainsPlayerScoped()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var world = await store.GetOrCreateWorldAsync(default);
        var first = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000005", "First", 0f, 8f, 0f), default);
        var second = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000006", "Second", 0f, 8f, 0f), default);
        const string nodeId = "18446744073709551001";

        Assert.True(await store.SaveResourceNodeStatesAsync(
            world.WorldId,
            new ResourceNodeStateListResponse(new[]
            {
                new ResourceNodeStateResponse(nodeId, 17, DateTime.UtcNow.AddMinutes(10)),
            }),
            default));
        Assert.True(await store.SaveDepositKnowledgeAsync(
            first.SteamId,
            world.WorldId,
            new DepositKnowledgeListResponse(new[]
            {
                new DepositKnowledgeResponse(nodeId, 7500, DateTime.UtcNow, world.WorldId),
            }),
            default));

        var nodes = await store.LoadResourceNodeStatesAsync(world.WorldId, default);
        var firstRestored = await store.LoginAsync(
            new PlayerLoginRequest(first.SteamId, "First", 0f, 8f, 0f), default);
        var secondRestored = await store.LoginAsync(
            new PlayerLoginRequest(second.SteamId, "Second", 0f, 8f, 0f), default);

        Assert.Single(nodes.Nodes);
        Assert.Equal((ushort)17, nodes.Nodes[0].RemainingReserves);
        Assert.Single(firstRestored.DepositKnowledge);
        Assert.Equal(world.WorldId, firstRestored.DepositKnowledge[0].WorldId);
        Assert.Empty(secondRestored.DepositKnowledge);
    }

    [Fact]
    public async Task MapNotes_AreSanitizedClampedEditableAndFollowPhysicalMap()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var world = await store.GetOrCreateWorldAsync(default);
        var owner = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000007", "Cartographer", 0f, 8f, 0f),
            default);
        var stranger = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000008", "Stranger", 0f, 8f, 0f),
            default);
        const string mapItemInstanceId = "18446744073709551001";
        Assert.True(await store.SaveInventoryAsync(
            owner.SteamId,
            new InventoryRequest(0, new[]
            {
                new InventorySlotResponse(
                    0, 36, 1, ItemInstanceId: mapItemInstanceId),
            }),
            default));
        var created = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(await store.SaveMapNotesAsync(
            owner.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                new MapNoteResponse(
                    "18446744073709551003",
                    float.MaxValue,
                    float.MinValue,
                    "  Богатая\tглина\nвозле\u0001 холма  ",
                    created,
                    created,
                    world.WorldId,
                    mapItemInstanceId),
            }),
            default));

        var restoredOwner = await store.LoginAsync(
            new PlayerLoginRequest(owner.SteamId, "Cartographer", 0f, 8f, 0f), default);
        var restoredStranger = await store.LoginAsync(
            new PlayerLoginRequest(stranger.SteamId, "Stranger", 0f, 8f, 0f), default);
        var note = Assert.Single(restoredOwner.MapNotes);
        Assert.Equal("Богатая глина возле холма", note.Text);
        Assert.Equal(world.ChunkCountX * world.ChunkSize * 0.5f, note.X);
        Assert.Equal(-world.ChunkCountZ * world.ChunkSize * 0.5f, note.Z);
        Assert.Equal(created, note.CreatedAtUtc);
        Assert.Empty(restoredStranger.MapNotes);

        Assert.True(await store.SaveMapNotesAsync(
            owner.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                note with { Text = "Глина у лагеря", X = 42f },
            }),
            default));
        var edited = await store.LoginAsync(
            new PlayerLoginRequest(owner.SteamId, "Cartographer", 0f, 8f, 0f), default);
        Assert.Equal("Глина у лагеря", Assert.Single(edited.MapNotes).Text);
        Assert.Equal(42f, edited.MapNotes[0].X);

        Assert.True(await store.SaveInventoryAsync(
            owner.SteamId,
            new InventoryRequest(0, Array.Empty<InventorySlotResponse>()),
            default));
        Assert.True(await store.SaveInventoryAsync(
            stranger.SteamId,
            new InventoryRequest(0, new[]
            {
                new InventorySlotResponse(
                    0, 36, 1, ItemInstanceId: mapItemInstanceId),
            }),
            default));
        var formerOwner = await store.LoginAsync(
            new PlayerLoginRequest(owner.SteamId, "Cartographer", 0f, 8f, 0f), default);
        var mapHolder = await store.LoginAsync(
            new PlayerLoginRequest(stranger.SteamId, "Stranger", 0f, 8f, 0f), default);
        Assert.Empty(formerOwner.MapNotes);
        Assert.Equal("Глина у лагеря", Assert.Single(mapHolder.MapNotes).Text);

        Assert.True(await store.SaveMapNotesAsync(
            stranger.SteamId,
            world.WorldId,
            new MapNoteListResponse([]),
            default));
        var deleted = await store.LoginAsync(
            new PlayerLoginRequest(stranger.SteamId, "Stranger", 0f, 8f, 0f), default);
        Assert.Empty(deleted.MapNotes);
    }

    [Fact]
    public async Task MapNotes_RejectInvalidTextDuplicateIdsAndLimitOverflow()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var world = await store.GetOrCreateWorldAsync(default);
        var player = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000009", "Guarded", 0f, 8f, 0f), default);
        const string mapItemInstanceId = "18446744073709551002";
        Assert.True(await store.SaveInventoryAsync(
            player.SteamId,
            new InventoryRequest(0, new[]
            {
                new InventorySlotResponse(
                    0, 36, 1, ItemInstanceId: mapItemInstanceId),
            }),
            default));
        var now = DateTime.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMapNotesAsync(
            player.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                new MapNoteResponse(
                    "1", 0f, 0f, " ", now, now,
                    MapItemInstanceId: mapItemInstanceId),
            }),
            default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMapNotesAsync(
            player.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                new MapNoteResponse(
                    "1", 0f, 0f, "Первая", now, now,
                    MapItemInstanceId: mapItemInstanceId),
                new MapNoteResponse(
                    "1", 1f, 1f, "Вторая", now, now,
                    MapItemInstanceId: mapItemInstanceId),
            }),
            default));

        var tooMany = Enumerable.Range(1, 65)
            .Select(index => new MapNoteResponse(
                index.ToString(), 0f, 0f, $"Заметка {index}", now, now,
                MapItemInstanceId: mapItemInstanceId))
            .ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMapNotesAsync(
            player.SteamId,
            world.WorldId,
            new MapNoteListResponse(tooMany),
            default));
    }

    [Fact]
    public async Task Inventory_RejectsDuplicateAndInvalidSlots()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var player = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000004", "Guarded", 0f, 8f, 0f),
            default);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveInventoryAsync(
            player.SteamId,
            new InventoryRequest(0, new[]
            {
                new InventorySlotResponse(0, 1, 1),
                new InventorySlotResponse(0, 2, 1),
            }),
            default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveInventoryAsync(
            player.SteamId,
            new InventoryRequest(6, []),
            default));
    }

    [Fact]
    public void DatabaseMigrations_AreDiscoverable()
    {
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=quieter_test;Username=test;Password=test")
            .Options;
        using var database = new ProfileDbContext(options);
        var migrations = database.Database.GetMigrations().ToArray();

        Assert.Contains("202608160001_InitialCreate", migrations);
        Assert.Contains("202608280001_UpdateWorldGeneratorVersion", migrations);
        Assert.Contains("202608290001_AddInventory", migrations);
        Assert.Contains("202608300001_AddResourceExtraction", migrations);
        Assert.Contains("202608310001_AddNonOreAndMapNotes", migrations);
        Assert.Contains("202609010001_AddResearchTables", migrations);
        Assert.Contains("202609020001_AddCharacterSurvival", migrations);
        Assert.Contains("202609020002_MakeMapNotesPhysical", migrations);
        Assert.Contains("202609020003_AddEquippedItemState", migrations);
        Assert.Contains("202609070001_AddCharacterItems", migrations);
    }

    [Fact]
    public async Task Snapshot_SavesOneRevisionAndRejectsStaleBodyResurrectionAndDuplicateItems()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var player = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000251", "Survivor", 0f, 8f, 0f), default);
        var alive = Snapshot(player.CharacterId, 4, 20f, 30);
        Assert.True(await store.SaveSnapshotAsync(player.SteamId, alive, default));
        var stale = Snapshot(player.CharacterId, 3, 900f, 40);
        Assert.True(await store.SaveSnapshotAsync(player.SteamId, stale, default));
        database.ChangeTracker.Clear();
        var restored = await store.LoginAsync(
            new PlayerLoginRequest(player.SteamId, "Survivor", 0f, 8f, 0f), default);
        Assert.Equal(20f, restored.PositionX);
        Assert.Equal(4, restored.SurvivalRevision);
        Assert.Equal((ushort)30, Assert.Single(restored.InventorySlots).ItemId);
        var duplicate = alive with
        {
            Survival = alive.Survival with { Revision = 5 },
            Inventory = new InventoryRequest(0, new[]
            {
                new InventorySlotResponse(0, 30, 1, ItemInstanceId: "700"),
                new InventorySlotResponse(1, 30, 1, ItemInstanceId: "700"),
            }),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveSnapshotAsync(player.SteamId, duplicate, default));
        var dead = Snapshot(player.CharacterId, 6, 22f, 30, dead: true);
        Assert.True(await store.SaveSnapshotAsync(player.SteamId, dead, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveSnapshotAsync(
            player.SteamId, alive with { Survival = alive.Survival with { Revision = 7 } }, default));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.SaveSnapshotAsync(
            player.SteamId, Snapshot(Guid.NewGuid().ToString(), 8, 0f, 40), default));
        Assert.Equal((byte)4, (await database.Characters.SingleAsync()).LifeState);
    }

    [Fact]
    public async Task Snapshot_ConcurrentConflictRollsBackInventoryAndPositionInRelationalTransaction()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ProfileDbContext>().UseSqlite(connection).Options;
        await using var setup = new ProfileDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        var player = await new ProfileStore(setup).LoginAsync(
            new PlayerLoginRequest("76561198000000252", "CAS", 0f, 8f, 0f), default);
        await using var staleContext = new ProfileDbContext(options);
        await staleContext.Players.Include(p => p.CurrentCharacter).SingleAsync();
        await using var winnerContext = new ProfileDbContext(options);
        Assert.True(await new ProfileStore(winnerContext).SaveSnapshotAsync(
            player.SteamId, Snapshot(player.CharacterId, 2, 40f, 30), default));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new ProfileStore(staleContext).SaveSnapshotAsync(
            player.SteamId, Snapshot(player.CharacterId, 1, 900f, 40), default));
        await using var verify = new ProfileDbContext(options);
        Assert.Equal(40f, (await verify.Players.SingleAsync()).PositionX);
        Assert.Equal(2, (await verify.Characters.SingleAsync()).Revision);
        Assert.Equal((ushort)30, (await verify.CharacterItems.SingleAsync()).ItemId);
    }

    [Fact]
    public async Task CharacterItems_StayWithTheirBodyWhenAccountChangesCharacter()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var profile = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000253", "First", 0f, 8f, 0f), default);
        await store.SaveSnapshotAsync(profile.SteamId, Snapshot(profile.CharacterId, 1, 5f, 30), default);
        var oldId = Guid.Parse(profile.CharacterId);
        var heir = new CharacterEntity
        {
            CharacterId = Guid.NewGuid(), Name = "Independent", SurvivalJson = "{}",
            Items = { new CharacterItemEntity { StorageArea = 0, SlotIndex = 0, ItemId = 40,
                Quantity = 1, ItemInstanceId = 999, LiquidKind = 5, LiquidMilliliters = 1700 } },
        };
        database.Characters.Add(heir);
        var account = await database.Players.SingleAsync();
        account.CurrentCharacter = heir;
        account.CurrentCharacterId = heir.CharacterId;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var controlled = await store.LoginAsync(
            new PlayerLoginRequest(profile.SteamId, "First", 0f, 8f, 0f), default);
        Assert.Equal(heir.CharacterId.ToString("D"), controlled.CharacterId);
        Assert.Equal((ushort)40, Assert.Single(controlled.InventorySlots).ItemId);
        Assert.Equal((ushort)1700, controlled.InventorySlots[0].LiquidMilliliters);
        Assert.Equal((ushort)30, (await database.CharacterItems.SingleAsync(item => item.CharacterId == oldId)).ItemId);
        Assert.Empty(await database.PlayerInventorySlots.ToArrayAsync());
    }

    [Fact]
    public async Task LegacyInventory_MovesToCharacterWithoutLosingInstanceOrContamination()
    {
        await using var database = CreateDatabase();
        var store = new ProfileStore(database);
        var profile = await store.LoginAsync(
            new PlayerLoginRequest("76561198000000254", "Legacy", 0f, 8f, 0f), default);
        database.PlayerInventorySlots.Add(new PlayerInventorySlotEntity
        {
            SteamId = decimal.Parse(profile.SteamId), SlotIndex = 0, ItemId = 30, Quantity = 1,
            ItemInstanceId = 8123, LiquidMilliliters = 1500, BiologicalContamination = 3210,
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var migrated = await store.LoginAsync(
            new PlayerLoginRequest(profile.SteamId, "Legacy", 0f, 8f, 0f), default);
        Assert.Equal("8123", Assert.Single(migrated.InventorySlots).ItemInstanceId);
        Assert.Equal((ushort)3210, migrated.InventorySlots[0].BiologicalContamination);
        Assert.Equal((ushort)1500, migrated.InventorySlots[0].LiquidMilliliters);
        Assert.Empty(await database.PlayerInventorySlots.ToArrayAsync());
        Assert.Single(await database.CharacterItems.ToArrayAsync());
    }

    private static PlayerSnapshotRequest Snapshot(string characterId, long revision, float x, ushort itemId, bool dead = false)
        => new(characterId, new PositionRequest(x, 8f, 0f),
            new InventoryRequest(0, new[] { new InventorySlotResponse(0, itemId, 1, ItemInstanceId: "701") }),
            new SurvivalRequest(System.Text.Json.JsonSerializer.Serialize(new
            {
                CharacterId = characterId,
                Physiology = new { LifeState = dead ? 4 : 0, DeathCause = dead ? 1 : 0 },
            }), revision));

    private static ProfileDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ProfileDbContext(options);
    }

    private static ProfileDbContext CreateDatabase(string name, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseInMemoryDatabase(name, root)
            .Options;
        return new ProfileDbContext(options);
    }
}
