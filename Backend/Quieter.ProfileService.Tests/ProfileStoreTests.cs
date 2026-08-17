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
    public void InitialMigration_IsDiscoverable()
    {
        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=quieter_test;Username=test;Password=test")
            .Options;
        using var database = new ProfileDbContext(options);
        var migrations = database.Database.GetMigrations().ToArray();

        Assert.Contains("202608160001_InitialCreate", migrations);
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
