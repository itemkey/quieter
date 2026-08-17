using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Quieter.ProfileService.Contracts;

namespace Quieter.ProfileService.Data;

public sealed class ProfileStore(ProfileDbContext database)
{
    public async Task<WorldResponse> GetOrCreateWorldAsync(CancellationToken cancellationToken)
    {
        var world = await database.Worlds.SingleOrDefaultAsync(cancellationToken);
        if (world is null)
        {
            world = CreateWorld();
            if (database.Database.IsRelational())
            {
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO worlds
                        ("Id", "Seed", "GeneratorVersion", "ChunkCountX", "ChunkCountZ",
                         "ChunkSize", "SamplesPerSide", "HeightStep", "CreatedAtUtc")
                    VALUES
                        ({world.Id}, {world.Seed}, {(int)world.GeneratorVersion},
                         {(int)world.ChunkCountX}, {(int)world.ChunkCountZ}, {(int)world.ChunkSize},
                         {(int)world.SamplesPerSide}, {world.HeightStep}, {world.CreatedAtUtc})
                    ON CONFLICT ("Id") DO NOTHING
                    """, cancellationToken);
                world = await database.Worlds.AsNoTracking().SingleAsync(cancellationToken);
            }
            else
            {
                database.Worlds.Add(world);
                await database.SaveChangesAsync(cancellationToken);
            }
        }

        return ToResponse(world);
    }

    public async Task<PlayerProfileResponse> LoginAsync(
        PlayerLoginRequest request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(request.SteamId);
        var player = await database.Players.FindAsync([steamId], cancellationToken);
        var now = DateTime.UtcNow;
        if (player is null)
        {
            player = new PlayerEntity
            {
                SteamId = steamId,
                DisplayName = SanitizeDisplayName(request.DisplayName),
                PositionX = FiniteOrDefault(request.DefaultX),
                PositionY = FiniteOrDefault(request.DefaultY),
                PositionZ = FiniteOrDefault(request.DefaultZ),
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
            };
            database.Players.Add(player);
        }
        else
        {
            player.DisplayName = SanitizeDisplayName(request.DisplayName);
            player.LastSeenAtUtc = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(player);
    }

    public async Task<bool> SavePositionAsync(
        string steamIdText,
        PositionRequest request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (database.Database.IsRelational())
        {
            var x = FiniteOrDefault(request.X);
            var y = FiniteOrDefault(request.Y);
            var z = FiniteOrDefault(request.Z);
            var now = DateTime.UtcNow;
            var updated = await database.Players
                .Where(player => player.SteamId == steamId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(player => player.PositionX, x)
                    .SetProperty(player => player.PositionY, y)
                    .SetProperty(player => player.PositionZ, z)
                    .SetProperty(player => player.LastSeenAtUtc, now), cancellationToken);
            return updated == 1;
        }

        var player = await database.Players.FindAsync([steamId], cancellationToken);
        if (player is null)
        {
            return false;
        }

        player.PositionX = FiniteOrDefault(request.X);
        player.PositionY = FiniteOrDefault(request.Y);
        player.PositionZ = FiniteOrDefault(request.Z);
        player.LastSeenAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static WorldResponse ToResponse(WorldEntity world) => new(
        world.Id,
        world.Seed,
        world.GeneratorVersion,
        world.ChunkCountX,
        world.ChunkCountZ,
        world.ChunkSize,
        world.SamplesPerSide,
        world.HeightStep);

    private static WorldEntity CreateWorld()
    {
        var seedBytes = RandomNumberGenerator.GetBytes(sizeof(long));
        return new WorldEntity
        {
            Id = 1,
            Seed = BitConverter.ToInt64(seedBytes),
            GeneratorVersion = 1,
            ChunkCountX = 32,
            ChunkCountZ = 32,
            ChunkSize = 64,
            SamplesPerSide = 33,
            HeightStep = 0.25f,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    private static PlayerProfileResponse ToResponse(PlayerEntity player) => new(
        decimal.Truncate(player.SteamId).ToString(System.Globalization.CultureInfo.InvariantCulture),
        player.DisplayName,
        player.PositionX,
        player.PositionY,
        player.PositionZ,
        player.CreatedAtUtc,
        player.LastSeenAtUtc);

    private static decimal ParseSteamId(string value)
    {
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
        {
            throw new ArgumentException("SteamId must be a positive unsigned 64-bit integer.");
        }

        return parsed;
    }

    private static string SanitizeDisplayName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Steam Player" : value.Trim();
        return value.Length <= 32 ? value : value[..32];
    }

    private static float FiniteOrDefault(float value) => float.IsFinite(value) ? value : 0f;
}
