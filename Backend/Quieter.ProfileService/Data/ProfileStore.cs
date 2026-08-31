using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Quieter.ProfileService.Contracts;

namespace Quieter.ProfileService.Data;

public sealed class ProfileStore(ProfileDbContext database)
{
    public const ushort CurrentGeneratorVersion = 4;

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
        var player = await database.Players
            .Include(candidate => candidate.InventorySlots)
            .Include(candidate => candidate.DepositKnowledge)
            .Include(candidate => candidate.MapNotes)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
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

    public async Task<ResourceNodeStateListResponse> LoadResourceNodeStatesAsync(
        int worldId,
        CancellationToken cancellationToken)
    {
        var nodes = await database.WorldResourceNodes
            .AsNoTracking()
            .Where(node => node.WorldId == worldId)
            .OrderBy(node => node.InstanceId)
            .Select(node => new ResourceNodeStateResponse(
                decimal.Truncate(node.InstanceId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                node.RemainingReserves,
                node.AvailableAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ResourceNodeStateListResponse(nodes);
    }

    public async Task<bool> SaveResourceNodeStatesAsync(
        int worldId,
        ResourceNodeStateListResponse request,
        CancellationToken cancellationToken)
    {
        if (!await database.Worlds.AnyAsync(world => world.Id == worldId, cancellationToken))
        {
            return false;
        }

        var incoming = request.Nodes ?? [];
        if (incoming.Count > 2048)
        {
            throw new ArgumentException("Too many resource node states.");
        }

        var ids = incoming.Select(node => ParseInstanceId(node.InstanceId)).ToArray();
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException("Resource node states contain duplicate instance ids.");
        }

        var existing = await database.WorldResourceNodes
            .Where(node => node.WorldId == worldId && ids.Contains(node.InstanceId))
            .ToDictionaryAsync(node => node.InstanceId, cancellationToken);
        var now = DateTime.UtcNow;
        for (var index = 0; index < incoming.Count; index++)
        {
            var source = incoming[index];
            var id = ids[index];
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new WorldResourceNodeEntity { WorldId = worldId, InstanceId = id };
                database.WorldResourceNodes.Add(entity);
            }
            entity.RemainingReserves = source.RemainingReserves;
            entity.AvailableAtUtc = source.AvailableAtUtc?.ToUniversalTime();
            entity.UpdatedAtUtc = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveInventoryAsync(
        string steamIdText,
        InventoryRequest request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (request.SelectedHotbarIndex >= 6)
        {
            throw new ArgumentException("Selected hotbar index must be between 0 and 5.");
        }

        var slots = request.Slots ?? [];
        if (slots.Count > 30 || slots.Select(slot => slot.SlotIndex).Distinct().Count() != slots.Count)
        {
            throw new ArgumentException("Inventory contains duplicate or too many slots.");
        }

        foreach (var slot in slots)
        {
            if (slot.SlotIndex >= 30 || slot.ItemId == 0 || slot.Quantity == 0)
            {
                throw new ArgumentException("Inventory slot is invalid.");
            }
        }

        var player = await database.Players
            .Include(candidate => candidate.InventorySlots)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
        if (player is null) return false;

        database.PlayerInventorySlots.RemoveRange(player.InventorySlots);
        player.InventorySlots = slots.Select(slot => new PlayerInventorySlotEntity
        {
            SteamId = steamId,
            SlotIndex = slot.SlotIndex,
                ItemId = slot.ItemId,
                Quantity = slot.Quantity,
                Condition = slot.Condition,
                Quality = slot.Quality,
                HiddenItemId = slot.HiddenItemId,
                SourceNodeId = ParseOptionalInstanceId(slot.SourceNodeId),
                RevealAtPercent = slot.RevealAtPercent,
        }).ToList();
        player.SelectedHotbarIndex = request.SelectedHotbarIndex;
        player.LastSeenAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveDepositKnowledgeAsync(
        string steamIdText,
        int worldId,
        DepositKnowledgeListResponse request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        var playerExists = await database.Players.AnyAsync(
            player => player.SteamId == steamId, cancellationToken);
        if (!playerExists) return false;

        var incoming = request.Knowledge ?? [];
        if (incoming.Count > 2048)
        {
            throw new ArgumentException("Too many deposit knowledge entries.");
        }
        var parsed = incoming.Select(entry => new
        {
            Entry = entry,
            InstanceId = ParseInstanceId(entry.InstanceId),
        }).ToArray();
        if (parsed.Select(entry => entry.InstanceId).Distinct().Count() != parsed.Length
            || parsed.Any(entry => entry.Entry.StudyBasisPoints > 10000))
        {
            throw new ArgumentException("Deposit knowledge is invalid.");
        }

        var existing = await database.PlayerDepositKnowledge
            .Where(entry => entry.SteamId == steamId && entry.WorldId == worldId)
            .ToListAsync(cancellationToken);
        database.PlayerDepositKnowledge.RemoveRange(existing);
        var now = DateTime.UtcNow;
        foreach (var source in parsed)
        {
            database.PlayerDepositKnowledge.Add(new PlayerDepositKnowledgeEntity
            {
                SteamId = steamId,
                WorldId = worldId,
                InstanceId = source.InstanceId,
                StudyBasisPoints = source.Entry.StudyBasisPoints,
                DiscoveredAtUtc = source.Entry.DiscoveredAtUtc == default
                    ? now
                    : source.Entry.DiscoveredAtUtc.ToUniversalTime(),
                UpdatedAtUtc = now,
            });
        }
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveMapNotesAsync(
        string steamIdText,
        int worldId,
        MapNoteListResponse request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        var playerExists = await database.Players.AnyAsync(
            player => player.SteamId == steamId, cancellationToken);
        var world = await database.Worlds.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == worldId, cancellationToken);
        if (!playerExists || world is null) return false;

        var incoming = request.Notes ?? [];
        if (incoming.Count > 64) throw new ArgumentException("Too many map notes.");
        var parsed = incoming.Select(note => new
        {
            Entry = note,
            NoteId = ParseInstanceId(note.NoteId),
            Text = NormalizeNoteText(note.Text),
        }).ToArray();
        if (parsed.Select(note => note.NoteId).Distinct().Count() != parsed.Length
            || parsed.Any(note => note.Text.Length == 0))
        {
            throw new ArgumentException("Map notes are invalid.");
        }

        var existing = await database.PlayerMapNotes
            .Where(note => note.SteamId == steamId && note.WorldId == worldId)
            .ToListAsync(cancellationToken);
        database.PlayerMapNotes.RemoveRange(existing);
        var now = DateTime.UtcNow;
        var halfWidth = world.ChunkCountX * world.ChunkSize * 0.5f;
        var halfDepth = world.ChunkCountZ * world.ChunkSize * 0.5f;
        foreach (var source in parsed)
        {
            database.PlayerMapNotes.Add(new PlayerMapNoteEntity
            {
                SteamId = steamId,
                WorldId = worldId,
                NoteId = source.NoteId,
                X = Math.Clamp(FiniteOrDefault(source.Entry.X), -halfWidth, halfWidth),
                Z = Math.Clamp(FiniteOrDefault(source.Entry.Z), -halfDepth, halfDepth),
                Text = source.Text,
                CreatedAtUtc = source.Entry.CreatedAtUtc == default
                    ? now : source.Entry.CreatedAtUtc.ToUniversalTime(),
                UpdatedAtUtc = source.Entry.UpdatedAtUtc == default
                    ? now : source.Entry.UpdatedAtUtc.ToUniversalTime(),
            });
        }
        await database.SaveChangesAsync(cancellationToken);
        return true;
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
            GeneratorVersion = CurrentGeneratorVersion,
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
        player.LastSeenAtUtc,
        player.InventorySlots
            .OrderBy(slot => slot.SlotIndex)
            .Select(slot => new InventorySlotResponse(
                slot.SlotIndex,
                slot.ItemId,
                slot.Quantity,
                slot.Condition,
                slot.Quality,
                slot.HiddenItemId,
                slot.SourceNodeId.HasValue
                    ? decimal.Truncate(slot.SourceNodeId.Value).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : null,
                slot.RevealAtPercent))
            .ToArray(),
        player.SelectedHotbarIndex,
        player.DepositKnowledge
            .OrderBy(entry => entry.InstanceId)
            .Select(entry => new DepositKnowledgeResponse(
                decimal.Truncate(entry.InstanceId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                entry.StudyBasisPoints,
                entry.DiscoveredAtUtc,
                entry.WorldId))
            .ToArray(),
        player.MapNotes
            .OrderBy(note => note.NoteId)
            .Select(note => new MapNoteResponse(
                decimal.Truncate(note.NoteId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                note.X,
                note.Z,
                note.Text,
                note.CreatedAtUtc,
                note.UpdatedAtUtc,
                note.WorldId))
            .ToArray());

    private static decimal ParseSteamId(string value)
    {
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
        {
            throw new ArgumentException("SteamId must be a positive unsigned 64-bit integer.");
        }

        return parsed;
    }

    private static decimal? ParseOptionalInstanceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
        {
            throw new ArgumentException("SourceNodeId must be an unsigned 64-bit integer.");
        }

        return parsed;
    }

    private static string NormalizeNoteText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var characters = new List<char>(Math.Min(value.Length, 80));
        var previousWhitespace = false;
        foreach (var character in value)
        {
            if (characters.Count >= 80) break;
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                if (characters.Count > 0 && !previousWhitespace) characters.Add(' ');
                previousWhitespace = true;
                continue;
            }
            characters.Add(character);
            previousWhitespace = false;
        }
        return new string(characters.ToArray()).Trim();
    }

    private static decimal ParseInstanceId(string value)
    {
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
        {
            throw new ArgumentException("InstanceId must be an unsigned 64-bit integer.");
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
