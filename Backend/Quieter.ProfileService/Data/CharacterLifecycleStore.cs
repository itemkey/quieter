using Microsoft.EntityFrameworkCore;
using Quieter.ProfileService.Contracts;

namespace Quieter.ProfileService.Data;

public sealed partial class ProfileStore
{
    public async Task<PlayerProfileResponse> CreateWorldNpcAsync(
        CreateWorldNpcRequest request, CancellationToken cancellationToken)
    {
        if (request.Snapshot is null
            || !Guid.TryParse(request.Snapshot.CharacterId, out var characterId))
            throw new ArgumentException("Invalid NPC snapshot.");
        ValidatePairSnapshot(request.Snapshot, characterId);
        var life = ReadLifeState(
            request.Snapshot.Survival.SurvivalJson, true, characterId);
        var controlKind = ReadControlKind(request.Snapshot.Survival.SurvivalJson);
        if (life.LifeState == 4 || controlKind is < 1 or > 3)
            throw new ArgumentException("A new NPC must be alive and NPC-controlled.");

        var existing = await database.Characters.Include(character => character.Items)
            .Include(character => character.DepositKnowledge)
            .Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == characterId,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.ControllingPlayer is not null
                || ReadControlKind(existing.SurvivalJson) is < 1 or > 3)
                throw new DbUpdateConcurrencyException(
                    "Character identifier already belongs to a player-controlled life.");
            return ToCharacterProfile(existing, []);
        }

        var now = DateTime.UtcNow;
        var character = new CharacterEntity
        {
            CharacterId = characterId,
            Name = SanitizeDisplayName(request.Name),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        database.Characters.Add(character);
        ApplyPairState(character, request.Snapshot, life);
        await ReplaceCharacterProjectionsAsync(
            character.CharacterId, character.SurvivalJson, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return ToCharacterProfile(character, []);
    }

    // The current server owns one persistent world. Unbound characters remain in
    // this collection: account login is never the trigger that makes a body exist.
    public async Task<WorldCharacterListResponse> LoadWorldCharactersAsync(CancellationToken cancellationToken)
    {
        var characters = await database.Characters.AsNoTracking()
            .Include(character => character.Items)
            .Include(character => character.DepositKnowledge)
            .Include(character => character.ControllingPlayer)
            .Where(character => character.Revision > 0)
            .OrderBy(character => character.CharacterId).ToArrayAsync(cancellationToken);
        var mapIds = characters.SelectMany(character => character.Items)
            .Where(item => item.ItemId == 36 && item.ItemInstanceId.HasValue)
            .Select(item => item.ItemInstanceId!.Value).Distinct().ToArray();
        var notes = await database.PlayerMapNotes.AsNoTracking()
            .Where(note => mapIds.Contains(note.MapItemInstanceId)).ToArrayAsync(cancellationToken);
        return new WorldCharacterListResponse(characters.Select(character =>
        {
            var ids = character.Items.Where(item => item.ItemId == 36 && item.ItemInstanceId.HasValue)
                .Select(item => item.ItemInstanceId!.Value).ToHashSet();
            return ToCharacterProfile(character, notes.Where(note => ids.Contains(note.MapItemInstanceId)).ToArray());
        }).ToArray());
    }

    public async Task<bool> SaveDetachedCharacterAsync(
        PlayerSnapshotRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.CharacterId, out var id) || request.Position is null
            || request.Inventory is null || request.Survival is null || request.Survival.Revision <= 0
            || !float.IsFinite(request.Position.X) || !float.IsFinite(request.Position.Y)
            || !float.IsFinite(request.Position.Z)) throw new ArgumentException("Invalid body snapshot.");
        ValidateInventory(request.Inventory);
        var life = ReadLifeState(request.Survival.SurvivalJson, true, id);
        var character = await database.Characters.Include(entry => entry.Items)
            .Include(entry => entry.ControllingPlayer)
            .SingleOrDefaultAsync(entry => entry.CharacterId == id, cancellationToken);
        if (character is null) return false;
        if (character.ControllingPlayer is not null)
            throw new DbUpdateConcurrencyException("This character is still account-controlled.");
        if (request.Survival.Revision <= character.Revision) return true;
        if (character.LifeState == 4 && life.LifeState != 4)
            throw new ArgumentException("Cannot resurrect an irreversibly dead character.");
        character.PositionX = request.Position.X;
        character.PositionY = request.Position.Y;
        character.PositionZ = request.Position.Z;
        character.SurvivalJson = request.Survival.SurvivalJson;
        character.Revision = request.Survival.Revision;
        character.LifeState = life.LifeState;
        character.DeathCause = life.DeathCause;
        if (life.LifeState == 4) character.DiedAtUtc ??= DateTime.UtcNow;
        character.UpdatedAtUtc = DateTime.UtcNow;
        ApplyCharacterInventory(character, request.Inventory);
        await ReplaceCharacterProjectionsAsync(
            character.CharacterId, character.SurvivalJson, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PlayerProfileResponse> CreateNewStrangerAsync(
        string steamIdText, NewStrangerRequest request, CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (!Guid.TryParse(request.OperationId, out var operationId) || operationId == Guid.Empty
            || !Guid.TryParse(request.PreviousCharacterId, out var previousId)
            || request.ExpectedRevision <= 0 || request.Position is null
            || !float.IsFinite(request.Position.X) || !float.IsFinite(request.Position.Y)
            || !float.IsFinite(request.Position.Z)) throw new ArgumentException("Invalid new-life request.");
        var player = await database.Players.Include(entry => entry.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .Include(entry => entry.CurrentCharacter)
                .ThenInclude(character => character!.DepositKnowledge)
            .SingleOrDefaultAsync(entry => entry.SteamId == steamId, cancellationToken)
            ?? throw new ArgumentException("Account not found.");
        var receipt = await database.CharacterReplacements.FindAsync([operationId], cancellationToken);
        if (receipt is not null)
        {
            if (receipt.SteamId != steamId || receipt.PreviousCharacterId != previousId
                || player.CurrentCharacterId != receipt.NewCharacterId)
                throw new DbUpdateConcurrencyException("New-life operation belongs to another transition.");
            return ToResponse(player, []);
        }
        var previous = player.CurrentCharacter;
        if (previous is null || previous.CharacterId != previousId || previous.Revision != request.ExpectedRevision)
            throw new DbUpdateConcurrencyException("Character changed before new life was accepted.");
        var previousControlKind = ReadControlKind(previous.SurvivalJson);
        if ((previous.LifeState != 4 || previous.DeathCause == 0) && previousControlKind != 3)
            throw new ArgumentException(
                "Only an irreversibly dead or captured character may be abandoned.");

        var now = DateTime.UtcNow;
        var next = new CharacterEntity
        {
            CharacterId = Guid.NewGuid(), Name = player.DisplayName,
            PositionX = request.Position.X, PositionY = request.Position.Y, PositionZ = request.Position.Z,
            CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        database.Characters.Add(next);
        // Touch the old revision as well as account binding: in-flight body saves
        // must fail, and cannot move its inventory into the next life.
        previous.Revision++;
        previous.UpdatedAtUtc = now;
        previous.DiedAtUtc ??= now;
        player.CurrentCharacter = next;
        player.CurrentCharacterId = next.CharacterId;
        player.PositionX = next.PositionX;
        player.PositionY = next.PositionY;
        player.PositionZ = next.PositionZ;
        player.SelectedHotbarIndex = 0;
        player.LastSeenAtUtc = now;
        player.RegisteredHeirCharacterId = null;
        player.HeirRegisteredAtUtc = null;
        player.EstateRevision++;
        database.CharacterReplacements.Add(new CharacterReplacementEntity
        {
            OperationId = operationId, SteamId = steamId, PreviousCharacterId = previousId,
            NewCharacterId = next.CharacterId, CreatedAtUtc = now,
        });
        // One transaction owns the receipt, binding and loss of memory. The old
        // inventory and physical map documents are deliberately untouched.
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(player, []);
    }

    public async Task<bool> SaveCharacterPairAsync(
        CharacterPairSnapshotRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.OperationId, out var operationId) || operationId == Guid.Empty
            || request.Source is null || request.Destination is null
            || !Guid.TryParse(request.Source.CharacterId, out var sourceId)
            || !Guid.TryParse(request.Destination.CharacterId, out var destinationId)
            || sourceId == destinationId)
            throw new ArgumentException("Invalid character transfer request.");
        var destinationSteamId = ParseSteamId(request.DestinationSteamId);
        ValidatePairSnapshot(request.Source, sourceId);
        ValidatePairSnapshot(request.Destination, destinationId);
        var allInstanceIds = (request.Source.Inventory.Slots ?? [])
            .Concat(request.Source.Inventory.PendingItems ?? [])
            .Concat(request.Destination.Inventory.Slots ?? [])
            .Concat(request.Destination.Inventory.PendingItems ?? [])
            .Select(item => ParseOptionalInstanceId(item.ItemInstanceId))
            .Where(id => id.HasValue).ToArray();
        if (allInstanceIds.Distinct().Count() != allInstanceIds.Length)
            throw new ArgumentException("An item instance cannot exist in both character snapshots.");

        var receipt = await database.CharacterTransfers.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.OperationId == operationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.SourceCharacterId != sourceId || receipt.DestinationCharacterId != destinationId)
                throw new DbUpdateConcurrencyException("Transfer receipt belongs to other characters.");
            return true;
        }
        var source = await database.Characters.Include(character => character.Items)
            .Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == sourceId, cancellationToken);
        var player = await database.Players.Include(entry => entry.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .SingleOrDefaultAsync(entry => entry.SteamId == destinationSteamId, cancellationToken);
        var destination = player?.CurrentCharacter;
        if (source is null || destination is null) return false;
        if (source.LifeState != 4 || player!.CurrentCharacterId != destinationId
            || destination.CharacterId != destinationId
            || source.ControllingPlayer?.SteamId == destinationSteamId)
            throw new DbUpdateConcurrencyException("Character ownership changed during transfer.");
        if (request.Source.Survival.Revision <= source.Revision
            || request.Destination.Survival.Revision <= destination.Revision)
            throw new DbUpdateConcurrencyException("Character transfer used stale revisions.");
        var sourceLife = ReadLifeState(request.Source.Survival.SurvivalJson, true, sourceId);
        var destinationLife = ReadLifeState(request.Destination.Survival.SurvivalJson, true, destinationId);
        if (sourceLife.LifeState != 4 || sourceLife.DeathCause == 0
            || destinationLife.LifeState == 4)
            throw new ArgumentException("Items can only move from a dead body to a living character.");

        ApplyPairState(source, request.Source, sourceLife);
        ApplyPairState(destination, request.Destination, destinationLife);
        await ReplaceCharacterProjectionsAsync(
            source.CharacterId, source.SurvivalJson, cancellationToken);
        await ReplaceCharacterProjectionsAsync(
            destination.CharacterId, destination.SurvivalJson, cancellationToken);
        player.PositionX = request.Destination.Position.X;
        player.PositionY = request.Destination.Position.Y;
        player.PositionZ = request.Destination.Position.Z;
        player.SelectedHotbarIndex = request.Destination.Inventory.SelectedHotbarIndex;
        player.LastSeenAtUtc = DateTime.UtcNow;
        if (source.ControllingPlayer is { } sourcePlayer)
        {
            sourcePlayer.PositionX = request.Source.Position.X;
            sourcePlayer.PositionY = request.Source.Position.Y;
            sourcePlayer.PositionZ = request.Source.Position.Z;
            sourcePlayer.SelectedHotbarIndex = request.Source.Inventory.SelectedHotbarIndex;
            sourcePlayer.LastSeenAtUtc = DateTime.UtcNow;
        }
        database.CharacterTransfers.Add(new CharacterTransferEntity
        {
            OperationId = operationId, SourceCharacterId = sourceId,
            DestinationCharacterId = destinationId, CreatedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ValidatePairSnapshot(PlayerSnapshotRequest snapshot, Guid characterId)
    {
        if (snapshot.Position is null || snapshot.Inventory is null || snapshot.Survival is null
            || snapshot.Survival.Revision <= 0 || !float.IsFinite(snapshot.Position.X)
            || !float.IsFinite(snapshot.Position.Y) || !float.IsFinite(snapshot.Position.Z))
            throw new ArgumentException("Invalid character snapshot in transfer.");
        ValidateInventory(snapshot.Inventory);
        _ = ReadLifeState(snapshot.Survival.SurvivalJson, true, characterId);
    }

    private void ApplyPairState(
        CharacterEntity character, PlayerSnapshotRequest snapshot,
        (byte LifeState, byte DeathCause) life)
    {
        character.PositionX = snapshot.Position.X;
        character.PositionY = snapshot.Position.Y;
        character.PositionZ = snapshot.Position.Z;
        character.SurvivalJson = snapshot.Survival.SurvivalJson;
        character.Revision = snapshot.Survival.Revision;
        character.LifeState = life.LifeState;
        character.DeathCause = life.DeathCause;
        character.UpdatedAtUtc = DateTime.UtcNow;
        if (life.LifeState == 4) character.DiedAtUtc ??= DateTime.UtcNow;
        ApplyCharacterInventory(character, snapshot.Inventory);
    }

    private static PlayerProfileResponse ToCharacterProfile(
        CharacterEntity character, IReadOnlyList<PlayerMapNoteEntity> notes)
    {
        var owner = character.ControllingPlayer;
        return ToResponse(new PlayerEntity
        {
            SteamId = owner?.SteamId ?? 0, DisplayName = character.Name,
            PositionX = character.PositionX, PositionY = character.PositionY, PositionZ = character.PositionZ,
            CreatedAtUtc = character.CreatedAtUtc, LastSeenAtUtc = character.UpdatedAtUtc,
            CurrentCharacter = character, CurrentCharacterId = character.CharacterId,
            SelectedHotbarIndex = character.SelectedHotbarIndex,
        }, notes);
    }
}
