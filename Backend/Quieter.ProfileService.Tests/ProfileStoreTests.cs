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
                    24, 5, 1, 83, 4, 7, "18446744073709551000", 50),
            }),
            default));

        var restored = await store.LoginAsync(
            new PlayerLoginRequest(created.SteamId, "Crafter", 0f, 8f, 0f),
            default);
        Assert.Equal((byte)3, restored.SelectedHotbarIndex);
        Assert.Collection(restored.InventorySlots,
            slot => Assert.Equal(new InventorySlotResponse(0, 1, 20), slot),
            slot => Assert.Equal(new InventorySlotResponse(
                24, 5, 1, 83, 4, 7, "18446744073709551000", 50), slot));
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
    public async Task MapNotes_AreSanitizedClampedEditableAndPlayerScoped()
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
                    world.WorldId),
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

        Assert.True(await store.SaveMapNotesAsync(
            owner.SteamId,
            world.WorldId,
            new MapNoteListResponse([]),
            default));
        var deleted = await store.LoginAsync(
            new PlayerLoginRequest(owner.SteamId, "Cartographer", 0f, 8f, 0f), default);
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
        var now = DateTime.UtcNow;

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMapNotesAsync(
            player.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                new MapNoteResponse("1", 0f, 0f, " ", now, now),
            }),
            default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveMapNotesAsync(
            player.SteamId,
            world.WorldId,
            new MapNoteListResponse(new[]
            {
                new MapNoteResponse("1", 0f, 0f, "Первая", now, now),
                new MapNoteResponse("1", 1f, 1f, "Вторая", now, now),
            }),
            default));

        var tooMany = Enumerable.Range(1, 65)
            .Select(index => new MapNoteResponse(
                index.ToString(), 0f, 0f, $"Заметка {index}", now, now))
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
    }

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
