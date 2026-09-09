using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Quieter.ProfileService.Contracts;

namespace Quieter.ProfileService.Data;

public sealed partial class ProfileStore(ProfileDbContext database)
{
    public const ushort CurrentGeneratorVersion = 7;

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

        if (world.GeneratorVersion != CurrentGeneratorVersion)
        {
            throw new InvalidOperationException(
                $"World generator v{world.GeneratorVersion} is incompatible with "
                + $"required v{CurrentGeneratorVersion}. Back up PostgreSQL and run "
                + "the explicit survival-world reset operation.");
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
            .Include(candidate => candidate.PendingItems)
            .Include(candidate => candidate.DepositKnowledge)
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
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

        if (player.CurrentCharacter is null)
        {
            var character = new CharacterEntity
            {
                CharacterId = Guid.NewGuid(),
                Name = player.DisplayName,
                SurvivalJson = "{}",
                PositionX = player.PositionX,
                PositionY = player.PositionY,
                PositionZ = player.PositionZ,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            database.Characters.Add(character);
            player.CurrentCharacter = character;
            player.CurrentCharacterId = character.CharacterId;
        }

        if (player.CurrentCharacter.Items.Count == 0
            && (player.InventorySlots.Count > 0 || player.PendingItems.Count > 0))
            ApplyInventory(player, ReadLegacyInventory(player));
        await database.SaveChangesAsync(cancellationToken);
        var carriedMapIds = player.CurrentCharacter.Items.Where(slot => slot.StorageArea == 0)
            .Where(slot => slot.ItemId == 36 && slot.ItemInstanceId.HasValue)
            .Select(slot => slot.ItemInstanceId!.Value)
            .Distinct()
            .ToArray();
        var mapNotes = carriedMapIds.Length == 0
            ? []
            : await database.PlayerMapNotes.AsNoTracking()
                .Where(note => carriedMapIds.Contains(note.MapItemInstanceId))
                .ToArrayAsync(cancellationToken);
        return ToResponse(player, mapNotes);
    }

    public async Task<bool> SaveSnapshotAsync(
        string steamIdText, PlayerSnapshotRequest request, CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (!Guid.TryParse(request.CharacterId, out var characterId)
            || request.Position is null || request.Inventory is null || request.Survival is null
            || !float.IsFinite(request.Position.X) || !float.IsFinite(request.Position.Y)
            || !float.IsFinite(request.Position.Z) || request.Survival.Revision <= 0)
            throw new ArgumentException("Player snapshot is invalid.");
        ValidateInventory(request.Inventory);
        var physiology = ReadLifeState(request.Survival.SurvivalJson, strict: true, characterId);

        var player = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .Include(candidate => candidate.InventorySlots)
            .Include(candidate => candidate.PendingItems)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
        if (player?.CurrentCharacter is null) return false;
        var character = player.CurrentCharacter;
        if (character.CharacterId != characterId)
            throw new DbUpdateConcurrencyException("The account now controls another character.");
        if (request.Survival.Revision <= character.Revision) return true;
        var previous = ReadLifeState(character.SurvivalJson, strict: false);
        if ((character.LifeState == 4 || previous.LifeState == 4) && physiology.LifeState != 4)
            throw new ArgumentException("An irreversibly dead character cannot be resurrected.");
        if (ReadControlKind(character.SurvivalJson) == 3
            && ReadControlKind(request.Survival.SurvivalJson) != 3)
            throw new ArgumentException("A captured body cannot restore player control.");

        character.SurvivalJson = request.Survival.SurvivalJson;
        character.Revision = request.Survival.Revision;
        character.LifeState = physiology.LifeState;
        character.DeathCause = physiology.DeathCause;
        if (physiology.LifeState == 4) character.DiedAtUtc ??= DateTime.UtcNow;
        character.PositionX = request.Position.X;
        character.PositionY = request.Position.Y;
        character.PositionZ = request.Position.Z;
        character.UpdatedAtUtc = DateTime.UtcNow;
        player.PositionX = request.Position.X;
        player.PositionY = request.Position.Y;
        player.PositionZ = request.Position.Z;
        ApplyInventory(player, request.Inventory);
        // EF commits all rows in one transaction. Revision and account binding
        // are concurrency tokens, so a conflicting save rolls the entire batch back.
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static (byte LifeState, byte DeathCause) ReadLifeState(
        string json, bool strict, Guid? expectedCharacterId = null)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1_000_000)
            throw new ArgumentException("Survival aggregate is missing or too large.");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new ArgumentException("Survival aggregate must be an object.");
            if (expectedCharacterId.HasValue
                && (!root.TryGetProperty("CharacterId", out var id)
                    || id.ValueKind != System.Text.Json.JsonValueKind.String
                    || !Guid.TryParse(id.GetString(), out var parsed) || parsed != expectedCharacterId))
                throw new ArgumentException("Snapshot character identifiers disagree.");
            if (!root.TryGetProperty("Physiology", out var physiology))
            {
                if (strict) throw new ArgumentException("Snapshot physiology is missing.");
                return (0, 0);
            }
            if (physiology.ValueKind != System.Text.Json.JsonValueKind.Object
                || !physiology.TryGetProperty("LifeState", out var life)
                || life.ValueKind != System.Text.Json.JsonValueKind.Number
                || !life.TryGetByte(out var lifeState) || lifeState > 4
                || !physiology.TryGetProperty("DeathCause", out var cause)
                || cause.ValueKind != System.Text.Json.JsonValueKind.Number
                || !cause.TryGetByte(out var deathCause) || deathCause > 13
                || (lifeState == 4) != (deathCause != 0))
                throw new ArgumentException("Life state and physiological death cause are inconsistent.");
            return (lifeState, deathCause);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("Survival aggregate is not valid JSON.", exception);
        }
    }

    private static byte ReadControlKind(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("ControlKind", out var control)) return 0;
            if (control.ValueKind != System.Text.Json.JsonValueKind.Number
                || !control.TryGetByte(out var value) || value > 5)
                throw new ArgumentException("Character control state is invalid.");
            return value;
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("Survival aggregate is not valid JSON.", exception);
        }
    }

    public async Task<bool> SaveSurvivalAsync(
        string steamIdText,
        SurvivalRequest request,
        CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (request.Revision < 0)
        {
            throw new ArgumentException("Survival revision cannot be negative.");
        }
        if (string.IsNullOrWhiteSpace(request.SurvivalJson)
            || request.SurvivalJson.Length > 1_000_000)
        {
            throw new ArgumentException("Survival aggregate is missing or too large.");
        }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(request.SurvivalJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new ArgumentException("Survival aggregate must be a JSON object.");
            }
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("Survival aggregate is not valid JSON.", exception);
        }

        var player = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
        if (player?.CurrentCharacter is null) return false;
        if (request.Revision <= player.CurrentCharacter.Revision)
        {
            return true;
        }

        var physiology = ReadLifeState(request.SurvivalJson, strict: false);
        var previous = ReadLifeState(player.CurrentCharacter.SurvivalJson, strict: false);
        if ((player.CurrentCharacter.LifeState == 4 || previous.LifeState == 4)
            && physiology.LifeState != 4)
            throw new ArgumentException("An irreversibly dead character cannot be resurrected.");
        if (ReadControlKind(player.CurrentCharacter.SurvivalJson) == 3
            && ReadControlKind(request.SurvivalJson) != 3)
            throw new ArgumentException("A captured body cannot restore player control.");

        player.CurrentCharacter.SurvivalJson = request.SurvivalJson;
        player.CurrentCharacter.LifeState = physiology.LifeState;
        player.CurrentCharacter.DeathCause = physiology.DeathCause;
        if (physiology.LifeState == 4) player.CurrentCharacter.DiedAtUtc ??= DateTime.UtcNow;
        player.CurrentCharacter.Revision = request.Revision;
        player.CurrentCharacter.UpdatedAtUtc = DateTime.UtcNow;
        player.LastSeenAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
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

    public async Task<PlacedObjectListResponse> LoadPlacedObjectsAsync(
        int worldId,
        CancellationToken cancellationToken)
    {
        var objects = await database.WorldPlacedObjects
            .AsNoTracking()
            .Where(entry => entry.WorldId == worldId)
            .OrderBy(entry => entry.ObjectId)
            .ToArrayAsync(cancellationToken);
        return new PlacedObjectListResponse(objects.Select(entry => new PlacedObjectResponse(
            FormatId(entry.ObjectId),
            entry.ItemId,
            entry.X,
            entry.Y,
            entry.Z,
            entry.Yaw,
            entry.InputItemId == 0 ? null : new InventorySlotResponse(
                0,
                entry.InputItemId,
                entry.InputQuantity,
                entry.InputCondition,
                entry.InputQuality,
                entry.InputHiddenItemId,
                FormatOptionalId(entry.InputSourceNodeId),
                entry.InputRevealAtPercent,
                FormatOptionalId(entry.InputSampleId),
                FormatOptionalId(entry.InputItemInstanceId),
                entry.InputFreshness,
                entry.InputBiologicalContamination,
                entry.InputToxinContamination,
                entry.InputWetness,
                entry.InputCleanliness,
                entry.InputLiquidMilliliters,
                entry.InputLiquidKind,
                entry.InputEquipped),
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc,
            FormatOptionalId(entry.OwnerAccountId),
            entry.Locked,
            entry.AssignedCharacterId?.ToString("D"))).ToArray());
    }

    public async Task<bool> SavePlacedObjectsAsync(
        int worldId,
        PlacedObjectListResponse request,
        CancellationToken cancellationToken)
    {
        if (!await database.Worlds.AnyAsync(world => world.Id == worldId, cancellationToken))
        {
            return false;
        }
        var incoming = request.Objects ?? [];
        if (incoming.Count > 2048) throw new ArgumentException("Too many placed objects.");
        var parsed = incoming.Select(entry => new
        {
            Entry = entry,
            ObjectId = ParseInstanceId(entry.ObjectId),
        }).ToArray();
        if (parsed.Select(entry => entry.ObjectId).Distinct().Count() != parsed.Length
            || parsed.Any(entry => entry.Entry.ItemId == 0
                || !float.IsFinite(entry.Entry.X)
                || !float.IsFinite(entry.Entry.Y)
                || !float.IsFinite(entry.Entry.Z)
                || !float.IsFinite(entry.Entry.Yaw)))
        {
            throw new ArgumentException("Placed objects are invalid.");
        }
        foreach (var source in parsed)
        {
            var input = source.Entry.Input;
            if (input is not null && (input.ItemId == 0 || input.Quantity == 0))
            {
                throw new ArgumentException("Placed object input is invalid.");
            }
            _ = ParseOptionalInstanceId(input?.SourceNodeId);
            _ = ParseOptionalInstanceId(input?.SampleId);
            _ = ParseOptionalInstanceId(input?.ItemInstanceId);
            _ = ParseOptionalInstanceId(source.Entry.OwnerAccountId);
            if (source.Entry.AssignedCharacterId is not null
                && (!Guid.TryParse(source.Entry.AssignedCharacterId, out _)
                    || source.Entry.ItemId != 48))
                throw new ArgumentException("Only a bed can be assigned to a character.");
            if (source.Entry.Locked && source.Entry.ItemId is not (47 or 54 or 55))
                throw new ArgumentException("Only a cell, door or chest can be locked.");
        }
        var existing = await database.WorldPlacedObjects
            .Where(entry => entry.WorldId == worldId)
            .ToListAsync(cancellationToken);
        database.WorldPlacedObjects.RemoveRange(existing);
        var now = DateTime.UtcNow;
        foreach (var source in parsed)
        {
            var input = source.Entry.Input;
            database.WorldPlacedObjects.Add(new WorldPlacedObjectEntity
            {
                WorldId = worldId,
                ObjectId = source.ObjectId,
                OwnerAccountId = ParseOptionalInstanceId(source.Entry.OwnerAccountId),
                AssignedCharacterId = string.IsNullOrWhiteSpace(source.Entry.AssignedCharacterId)
                    ? null : Guid.Parse(source.Entry.AssignedCharacterId),
                ItemId = source.Entry.ItemId,
                X = source.Entry.X,
                Y = source.Entry.Y,
                Z = source.Entry.Z,
                Yaw = source.Entry.Yaw,
                Locked = source.Entry.Locked,
                InputItemId = input?.ItemId ?? 0,
                InputQuantity = input?.Quantity ?? 0,
                InputCondition = input?.Condition ?? 0,
                InputQuality = input?.Quality ?? 0,
                InputHiddenItemId = input?.HiddenItemId ?? 0,
                InputSourceNodeId = ParseOptionalInstanceId(input?.SourceNodeId),
                InputRevealAtPercent = input?.RevealAtPercent ?? 0,
                InputSampleId = ParseOptionalInstanceId(input?.SampleId),
                InputItemInstanceId = ParseOptionalInstanceId(input?.ItemInstanceId),
                InputFreshness = input?.Freshness ?? 10000,
                InputBiologicalContamination = input?.BiologicalContamination ?? 0,
                InputToxinContamination = input?.ToxinContamination ?? 0,
                InputWetness = input?.Wetness ?? 0,
                InputCleanliness = input?.Cleanliness ?? 10000,
                InputLiquidMilliliters = input?.LiquidMilliliters ?? 0,
                InputLiquidKind = input?.LiquidKind ?? 0,
                InputEquipped = input?.Equipped ?? false,
                CreatedAtUtc = source.Entry.CreatedAtUtc == default
                    ? now : source.Entry.CreatedAtUtc.ToUniversalTime(),
                UpdatedAtUtc = source.Entry.UpdatedAtUtc == default
                    ? now : source.Entry.UpdatedAtUtc.ToUniversalTime(),
            });
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
        ValidateInventory(request);
        var player = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .Include(candidate => candidate.InventorySlots)
            .Include(candidate => candidate.PendingItems)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
        if (player?.CurrentCharacter is null) return false;
        ApplyInventory(player, request);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ValidateInventory(InventoryRequest request)
    {
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
            _ = ParseOptionalInstanceId(slot.SourceNodeId);
            _ = ParseOptionalInstanceId(slot.SampleId);
            _ = ParseOptionalInstanceId(slot.ItemInstanceId);
        }

        var pendingItems = request.PendingItems ?? [];
        if (pendingItems.Count > 256 || pendingItems.Any(item => item.ItemId == 0
                || item.Quantity == 0))
        {
            throw new ArgumentException("Pending inventory items are invalid.");
        }
        foreach (var item in pendingItems)
        {
            _ = ParseOptionalInstanceId(item.SourceNodeId);
            _ = ParseOptionalInstanceId(item.SampleId);
            _ = ParseOptionalInstanceId(item.ItemInstanceId);
        }
        var instanceIds = slots.Concat(pendingItems)
            .Select(slot => ParseOptionalInstanceId(slot.ItemInstanceId))
            .Where(id => id.HasValue).ToArray();
        if (instanceIds.Distinct().Count() != instanceIds.Length)
            throw new ArgumentException("An item instance cannot occupy multiple slots.");
    }

    private void ApplyInventory(PlayerEntity player, InventoryRequest request)
    {
        var character = player.CurrentCharacter
            ?? throw new InvalidOperationException("Account has no character.");
        ApplyCharacterInventory(character, request);
        database.PlayerInventorySlots.RemoveRange(player.InventorySlots);
        database.PlayerPendingItems.RemoveRange(player.PendingItems);
        player.InventorySlots.Clear();
        player.PendingItems.Clear();
        player.SelectedHotbarIndex = request.SelectedHotbarIndex;
        player.LastSeenAtUtc = DateTime.UtcNow;
    }

    private void ApplyCharacterInventory(CharacterEntity character, InventoryRequest request)
    {
        database.CharacterItems.RemoveRange(character.Items);
        character.Items = (request.Slots ?? [])
            .Select(slot => ToCharacterItem(character.CharacterId, 0, slot.SlotIndex, slot))
            .Concat((request.PendingItems ?? [])
                .Select((slot, index) => ToCharacterItem(character.CharacterId, 1, (ushort)index, slot)))
            .ToList();
        character.SelectedHotbarIndex = request.SelectedHotbarIndex;
    }

    private static CharacterItemEntity ToCharacterItem(
        Guid characterId, byte area, ushort index, InventorySlotResponse slot) => new()
    {
        CharacterId = characterId,
        StorageArea = area,
        SlotIndex = index,
        ItemId = slot.ItemId,
        Quantity = slot.Quantity,
        Condition = slot.Condition,
        Quality = slot.Quality,
        HiddenItemId = slot.HiddenItemId,
        SourceNodeId = ParseOptionalInstanceId(slot.SourceNodeId),
        RevealAtPercent = slot.RevealAtPercent,
        SampleId = ParseOptionalInstanceId(slot.SampleId),
        ItemInstanceId = ParseOptionalInstanceId(slot.ItemInstanceId),
        Freshness = slot.Freshness,
        BiologicalContamination = slot.BiologicalContamination,
        ToxinContamination = slot.ToxinContamination,
        Wetness = slot.Wetness,
        Cleanliness = slot.Cleanliness,
        LiquidMilliliters = slot.LiquidMilliliters,
        LiquidKind = slot.LiquidKind,
        Equipped = slot.Equipped,
    };

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
        var player = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken);
        var world = await database.Worlds.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == worldId, cancellationToken);
        if (player?.CurrentCharacter is null || world is null) return false;

        var carriedMapIds = player.CurrentCharacter.Items.Where(slot => slot.StorageArea == 0)
            .Where(slot => slot.ItemId == 36 && slot.ItemInstanceId.HasValue)
            .Select(slot => slot.ItemInstanceId!.Value)
            .Distinct()
            .ToArray();

        var incoming = request.Notes ?? [];
        var parsed = incoming.Select(note => new
        {
            Entry = note,
            MapItemInstanceId = ParseInstanceId(note.MapItemInstanceId),
            NoteId = ParseInstanceId(note.NoteId),
            Text = NormalizeNoteText(note.Text),
        }).ToArray();
        if (parsed.GroupBy(note => note.MapItemInstanceId).Any(group => group.Count() > 64)
            || parsed.Select(note => (note.MapItemInstanceId, note.NoteId))
                .Distinct().Count() != parsed.Length
            || parsed.Any(note => note.Text.Length == 0
                || !carriedMapIds.Contains(note.MapItemInstanceId)))
        {
            throw new ArgumentException("Map notes are invalid.");
        }

        var existing = await database.PlayerMapNotes
            .Where(note => note.WorldId == worldId
                && carriedMapIds.Contains(note.MapItemInstanceId))
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
                MapItemInstanceId = source.MapItemInstanceId,
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
            if (updated == 1)
            {
                await database.Characters
                    .Where(character => character.ControllingPlayer != null
                        && character.ControllingPlayer.SteamId == steamId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(character => character.PositionX, x)
                        .SetProperty(character => character.PositionY, y)
                        .SetProperty(character => character.PositionZ, z)
                        .SetProperty(character => character.UpdatedAtUtc, now), cancellationToken);
            }
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
        if (player.CurrentCharacterId.HasValue)
        {
            var character = await database.Characters.FindAsync([player.CurrentCharacterId.Value], cancellationToken);
            if (character != null)
            {
                character.PositionX = player.PositionX;
                character.PositionY = player.PositionY;
                character.PositionZ = player.PositionZ;
                character.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
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

    private static InventoryRequest ReadLegacyInventory(PlayerEntity player) => new(
        player.SelectedHotbarIndex,
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
                slot.RevealAtPercent,
                FormatOptionalId(slot.SampleId),
                FormatOptionalId(slot.ItemInstanceId),
                slot.Freshness,
                slot.BiologicalContamination,
                slot.ToxinContamination,
                slot.Wetness,
                slot.Cleanliness,
                slot.LiquidMilliliters,
                slot.LiquidKind,
                slot.Equipped))
            .ToArray(),
        player.PendingItems
            .OrderBy(item => item.ItemIndex)
            .Select(item => new InventorySlotResponse(
                (byte)Math.Min(item.ItemIndex, byte.MaxValue),
                item.ItemId,
                item.Quantity,
                item.Condition,
                item.Quality,
                item.HiddenItemId,
                FormatOptionalId(item.SourceNodeId),
                item.RevealAtPercent,
                FormatOptionalId(item.SampleId),
                FormatOptionalId(item.ItemInstanceId),
                item.Freshness,
                item.BiologicalContamination,
                item.ToxinContamination,
                item.Wetness,
                item.Cleanliness,
                item.LiquidMilliliters,
                item.LiquidKind,
                item.Equipped))
            .ToArray());

    private static PlayerProfileResponse ToResponse(
        PlayerEntity player,
        IReadOnlyList<PlayerMapNoteEntity> mapNotes) => new(
        decimal.Truncate(player.SteamId).ToString(System.Globalization.CultureInfo.InvariantCulture),
        player.DisplayName,
        player.PositionX,
        player.PositionY,
        player.PositionZ,
        player.CreatedAtUtc,
        player.LastSeenAtUtc,
        (player.CurrentCharacter?.Items ?? []).Where(slot => slot.StorageArea == 0)
            .OrderBy(slot => slot.SlotIndex)
            .Select(slot => new InventorySlotResponse(
                (byte)slot.SlotIndex,
                slot.ItemId,
                slot.Quantity,
                slot.Condition,
                slot.Quality,
                slot.HiddenItemId,
                slot.SourceNodeId.HasValue
                    ? decimal.Truncate(slot.SourceNodeId.Value).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : null,
                slot.RevealAtPercent,
                FormatOptionalId(slot.SampleId),
                FormatOptionalId(slot.ItemInstanceId),
                slot.Freshness,
                slot.BiologicalContamination,
                slot.ToxinContamination,
                slot.Wetness,
                slot.Cleanliness,
                slot.LiquidMilliliters,
                slot.LiquidKind,
                slot.Equipped))
            .ToArray(),
        (player.CurrentCharacter?.Items ?? []).Where(item => item.StorageArea == 1)
            .OrderBy(item => item.SlotIndex)
            .Select(item => new InventorySlotResponse(
                (byte)Math.Min(item.SlotIndex, byte.MaxValue),
                item.ItemId,
                item.Quantity,
                item.Condition,
                item.Quality,
                item.HiddenItemId,
                FormatOptionalId(item.SourceNodeId),
                item.RevealAtPercent,
                FormatOptionalId(item.SampleId),
                FormatOptionalId(item.ItemInstanceId),
                item.Freshness,
                item.BiologicalContamination,
                item.ToxinContamination,
                item.Wetness,
                item.Cleanliness,
                item.LiquidMilliliters,
                item.LiquidKind,
                item.Equipped))
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
        mapNotes
            .OrderBy(note => note.NoteId)
            .Select(note => new MapNoteResponse(
                decimal.Truncate(note.NoteId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                note.X,
                note.Z,
                note.Text,
                note.CreatedAtUtc,
                note.UpdatedAtUtc,
                note.WorldId,
                decimal.Truncate(note.MapItemInstanceId).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray(),
        player.CurrentCharacter?.CharacterId.ToString("D") ?? string.Empty,
        player.CurrentCharacter?.SurvivalJson ?? "{}",
        player.CurrentCharacter?.Revision ?? 0,
        player.RegisteredHeirCharacterId?.ToString("D"),
        player.EstateRevision);

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

    private static string FormatId(decimal value) => decimal.Truncate(value).ToString(
        System.Globalization.CultureInfo.InvariantCulture);

    private static string? FormatOptionalId(decimal? value) => value.HasValue
        ? FormatId(value.Value)
        : null;

    private static string SanitizeDisplayName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Steam Player" : value.Trim();
        return value.Length <= 32 ? value : value[..32];
    }

    private static float FiniteOrDefault(float value) => float.IsFinite(value) ? value : 0f;
}
