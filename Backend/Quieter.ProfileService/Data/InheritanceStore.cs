using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Quieter.ProfileService.Contracts;

namespace Quieter.ProfileService.Data;

public sealed partial class ProfileStore
{
    private const double RequiredHeirContractGameSeconds = 172800d;
    private static readonly TimeSpan HeirDonationWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan HeirOfferOfflineLifetime = TimeSpan.FromDays(7);

    public async Task<PlayerProfileResponse> RegisterHeirAsync(
        string steamIdText, RegisterHeirRequest request, CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (!Guid.TryParse(request.HeirCharacterId, out var heirId))
            throw new ArgumentException("Invalid heir character id.");
        var player = await database.Players
            .Include(candidate => candidate.DepositKnowledge)
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken)
            ?? throw new ArgumentException("Player does not exist.");
        if (player.CurrentCharacter is null)
            throw new ArgumentException("The account has no living identity.");
        if (player.EstateRevision != request.ExpectedEstateRevision)
            throw new DbUpdateConcurrencyException("The estate was already changed.");
        var heir = await database.Characters.Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == heirId, cancellationToken)
            ?? throw new ArgumentException("Heir does not exist.");
        ValidateHeir(player, heir);
        var hasAssignedBed = await database.WorldPlacedObjects.AnyAsync(entry =>
            entry.ItemId == 48 && entry.OwnerAccountId == steamId
            && entry.AssignedCharacterId == heirId, cancellationToken);
        if (!hasAssignedBed) throw new ArgumentException("The heir has no assigned owned bed.");
        if (!player.CurrentCharacter.Items.Any(item => item.StorageArea == 0
                && item.ItemId == 49 && item.Quantity > 0))
            throw new ArgumentException("An inheritance deed must be carried.");
        ValidateHeirRelationship(player, heir);

        player.RegisteredHeirCharacterId = heirId;
        player.HeirRegisteredAtUtc = DateTime.UtcNow;
        player.EstateRevision++;
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(player, []);
    }

    public async Task<PlayerProfileResponse> AssumeRegisteredHeirAsync(
        string steamIdText, AssumeHeirRequest request, CancellationToken cancellationToken)
    {
        var steamId = ParseSteamId(steamIdText);
        if (!Guid.TryParse(request.OperationId, out var operationId)
            || !Guid.TryParse(request.DeceasedCharacterId, out var deceasedId))
            throw new ArgumentException("Invalid inheritance transition identity.");

        var previousReceipt = await database.InheritanceTransitions.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.OperationId == operationId, cancellationToken);
        var player = await database.Players
            .Include(candidate => candidate.DepositKnowledge)
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == steamId, cancellationToken)
            ?? throw new ArgumentException("Player does not exist.");
        if (previousReceipt is not null)
        {
            if (previousReceipt.SteamId != steamId
                || previousReceipt.DeceasedCharacterId != deceasedId
                || player.CurrentCharacterId != previousReceipt.HeirCharacterId)
                throw new DbUpdateConcurrencyException("Inheritance operation was reused.");
            return ToResponse(player, await LoadCurrentMapNotesAsync(player, cancellationToken));
        }
        if (player.CurrentCharacter is null || player.CurrentCharacterId != deceasedId
            || player.CurrentCharacter.Revision != request.ExpectedRevision)
            throw new DbUpdateConcurrencyException("The deceased character changed.");
        if (player.CurrentCharacter.LifeState != 4
            && ReadControlKind(player.CurrentCharacter.SurvivalJson) != 3)
            throw new ArgumentException(
                "Inheritance begins only after irreversible death or completed capture.");
        if (!player.RegisteredHeirCharacterId.HasValue)
            throw new ArgumentException("No heir is registered.");
        var heirId = player.RegisteredHeirCharacterId.Value;
        var heir = await database.Characters.Include(character => character.Items)
            .Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == heirId, cancellationToken)
            ?? throw new ArgumentException("The registered heir no longer exists.");
        ValidateHeir(player, heir);

        PrepareHeirForControl(heir);
        var deceased = player.CurrentCharacter;
        deceased.ControllingPlayer = null;
        player.CurrentCharacter = heir;
        player.CurrentCharacterId = heirId;
        player.PositionX = heir.PositionX;
        player.PositionY = heir.PositionY;
        player.PositionZ = heir.PositionZ;
        player.SelectedHotbarIndex = heir.SelectedHotbarIndex;
        player.RegisteredHeirCharacterId = null;
        player.HeirRegisteredAtUtc = null;
        player.EstateRevision++;
        player.LastSeenAtUtc = DateTime.UtcNow;
        database.PlayerDepositKnowledge.RemoveRange(player.DepositKnowledge);
        player.DepositKnowledge.Clear();
        database.InheritanceTransitions.Add(new InheritanceTransitionEntity
        {
            OperationId = operationId,
            SteamId = steamId,
            DeceasedCharacterId = deceasedId,
            HeirCharacterId = heirId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(player, await LoadCurrentMapNotesAsync(player, cancellationToken));
    }

    public async Task<HeirOfferResponse> CreateHeirOfferAsync(
        string donorSteamIdText,
        CreateHeirOfferRequest request,
        CancellationToken cancellationToken)
    {
        var donorSteamId = ParseSteamId(donorSteamIdText);
        var recipientSteamId = ParseSteamId(request.RecipientSteamId);
        if (donorSteamId == recipientSteamId
            || !Guid.TryParse(request.OperationId, out var operationId)
            || !Guid.TryParse(request.DeceasedCharacterId, out var deceasedId))
            throw new ArgumentException("Invalid heir offer identity.");

        var previous = await database.HeirOffers.SingleOrDefaultAsync(
            entry => entry.OperationId == operationId, cancellationToken);
        if (previous is not null)
        {
            if (previous.DonorSteamId != donorSteamId
                || previous.RecipientSteamId != recipientSteamId
                || previous.DeceasedCharacterId != deceasedId)
                throw new DbUpdateConcurrencyException("Heir offer operation was reused.");
            return await ToHeirOfferResponseAsync(previous, cancellationToken);
        }

        var donor = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == donorSteamId, cancellationToken)
            ?? throw new ArgumentException("Donor does not exist.");
        var recipient = await database.Players
            .Include(candidate => candidate.CurrentCharacter)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == recipientSteamId, cancellationToken)
            ?? throw new ArgumentException("Recipient does not exist.");
        if (donor.EstateRevision != request.ExpectedDonorEstateRevision)
            throw new DbUpdateConcurrencyException("The donor estate was already changed.");
        if (!donor.RegisteredHeirCharacterId.HasValue)
            throw new ArgumentException("The donor has no registered heir to give away.");
        if (recipient.CurrentCharacter is null
            || recipient.CurrentCharacterId != deceasedId
            || !IsLifeLost(recipient.CurrentCharacter))
            throw new ArgumentException("The recipient is not waiting after this lost life.");
        var lostAt = recipient.CurrentCharacter.DiedAtUtc
            ?? recipient.CurrentCharacter.UpdatedAtUtc;
        if (DateTime.UtcNow - lostAt > HeirDonationWindow)
            throw new ArgumentException("The 24 game-hour donation window has ended.");

        var heirId = donor.RegisteredHeirCharacterId.Value;
        var heir = await database.Characters.Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == heirId, cancellationToken)
            ?? throw new ArgumentException("The registered heir no longer exists.");
        ValidateHeir(donor, heir);

        var now = DateTime.UtcNow;
        var offer = new HeirOfferEntity
        {
            OfferId = Guid.NewGuid(),
            OperationId = operationId,
            DonorSteamId = donorSteamId,
            RecipientSteamId = recipientSteamId,
            DeceasedCharacterId = deceasedId,
            HeirCharacterId = heirId,
            OfferedAtUtc = now,
            HardExpiresAtUtc = now + HeirOfferOfflineLifetime,
            Status = 0,
        };
        database.HeirOffers.Add(offer);
        donor.RegisteredHeirCharacterId = null;
        donor.HeirRegisteredAtUtc = null;
        donor.EstateRevision++;
        await database.SaveChangesAsync(cancellationToken);
        return await ToHeirOfferResponseAsync(offer, cancellationToken);
    }

    public async Task<HeirOfferResponse?> GetPendingHeirOfferAsync(
        string recipientSteamIdText,
        CancellationToken cancellationToken)
    {
        var recipientSteamId = ParseSteamId(recipientSteamIdText);
        var player = await database.Players.Include(candidate => candidate.CurrentCharacter)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == recipientSteamId,
                cancellationToken);
        if (player?.CurrentCharacter is null || !IsLifeLost(player.CurrentCharacter)) return null;

        var now = DateTime.UtcNow;
        var offers = await database.HeirOffers
            .Where(entry => entry.RecipientSteamId == recipientSteamId && entry.Status == 0)
            .OrderBy(entry => entry.OfferedAtUtc)
            .ToListAsync(cancellationToken);
        HeirOfferEntity? selected = null;
        foreach (var offer in offers)
        {
            var expires = EffectiveOfferExpiry(offer);
            if (offer.DeceasedCharacterId != player.CurrentCharacterId || now >= expires)
            {
                offer.Status = 2;
                continue;
            }
            selected ??= offer;
        }
        if (selected is null)
        {
            if (database.ChangeTracker.HasChanges()) await database.SaveChangesAsync(cancellationToken);
            return null;
        }
        selected.AcceptanceStartedAtUtc ??= now;
        await database.SaveChangesAsync(cancellationToken);
        return await ToHeirOfferResponseAsync(selected, cancellationToken);
    }

    public async Task<PlayerProfileResponse> AcceptHeirOfferAsync(
        string recipientSteamIdText,
        Guid offerId,
        AcceptHeirOfferRequest request,
        CancellationToken cancellationToken)
    {
        var recipientSteamId = ParseSteamId(recipientSteamIdText);
        if (!Guid.TryParse(request.OperationId, out var operationId)
            || !Guid.TryParse(request.DeceasedCharacterId, out var deceasedId))
            throw new ArgumentException("Invalid heir acceptance identity.");

        var receipt = await database.InheritanceTransitions.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.OperationId == operationId, cancellationToken);
        var player = await database.Players
            .Include(candidate => candidate.DepositKnowledge)
            .Include(candidate => candidate.CurrentCharacter)
                .ThenInclude(character => character!.Items)
            .SingleOrDefaultAsync(candidate => candidate.SteamId == recipientSteamId,
                cancellationToken)
            ?? throw new ArgumentException("Recipient does not exist.");
        if (receipt is not null)
        {
            if (receipt.SteamId != recipientSteamId
                || receipt.DeceasedCharacterId != deceasedId
                || player.CurrentCharacterId != receipt.HeirCharacterId)
                throw new DbUpdateConcurrencyException("Inheritance operation was reused.");
            return ToResponse(player, await LoadCurrentMapNotesAsync(player, cancellationToken));
        }

        var offer = await database.HeirOffers.SingleOrDefaultAsync(
            entry => entry.OfferId == offerId, cancellationToken)
            ?? throw new ArgumentException("Heir offer does not exist.");
        if (offer.RecipientSteamId != recipientSteamId
            || offer.DeceasedCharacterId != deceasedId || offer.Status != 0)
            throw new DbUpdateConcurrencyException("Heir offer is no longer available.");
        if (offer.AcceptanceStartedAtUtc is null
            || DateTime.UtcNow >= EffectiveOfferExpiry(offer))
        {
            offer.Status = 2;
            await database.SaveChangesAsync(cancellationToken);
            throw new ArgumentException("Heir offer has expired.");
        }
        if (player.CurrentCharacter is null || player.CurrentCharacterId != deceasedId
            || player.CurrentCharacter.Revision != request.ExpectedRevision
            || !IsLifeLost(player.CurrentCharacter))
            throw new DbUpdateConcurrencyException("The lost character changed.");

        var heir = await database.Characters.Include(character => character.Items)
            .Include(character => character.ControllingPlayer)
            .SingleOrDefaultAsync(character => character.CharacterId == offer.HeirCharacterId,
                cancellationToken)
            ?? throw new ArgumentException("The donated heir no longer exists.");
        if (heir.ControllingPlayer is not null || heir.LifeState == 4)
            throw new ArgumentException("The donated heir is no longer available.");
        PrepareHeirForControl(heir);

        var deceased = player.CurrentCharacter;
        deceased.ControllingPlayer = null;
        player.CurrentCharacter = heir;
        player.CurrentCharacterId = heir.CharacterId;
        player.PositionX = heir.PositionX;
        player.PositionY = heir.PositionY;
        player.PositionZ = heir.PositionZ;
        player.SelectedHotbarIndex = heir.SelectedHotbarIndex;
        player.RegisteredHeirCharacterId = null;
        player.HeirRegisteredAtUtc = null;
        player.EstateRevision++;
        player.LastSeenAtUtc = DateTime.UtcNow;
        database.PlayerDepositKnowledge.RemoveRange(player.DepositKnowledge);
        player.DepositKnowledge.Clear();
        offer.Status = 1;
        offer.AcceptedAtUtc = DateTime.UtcNow;
        var otherOffers = await database.HeirOffers.Where(entry =>
                entry.RecipientSteamId == recipientSteamId && entry.Status == 0
                && entry.OfferId != offerId)
            .ToListAsync(cancellationToken);
        foreach (var other in otherOffers) other.Status = 3;
        database.InheritanceTransitions.Add(new InheritanceTransitionEntity
        {
            OperationId = operationId,
            SteamId = recipientSteamId,
            DeceasedCharacterId = deceasedId,
            HeirCharacterId = heir.CharacterId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(player, await LoadCurrentMapNotesAsync(player, cancellationToken));
    }

    private static bool IsLifeLost(CharacterEntity character)
        => character.LifeState == 4 || ReadControlKind(character.SurvivalJson) == 3;

    private static DateTime EffectiveOfferExpiry(HeirOfferEntity offer)
    {
        var seenExpiry = offer.AcceptanceStartedAtUtc.HasValue
            ? offer.AcceptanceStartedAtUtc.Value + HeirDonationWindow
            : offer.HardExpiresAtUtc;
        return seenExpiry < offer.HardExpiresAtUtc ? seenExpiry : offer.HardExpiresAtUtc;
    }

    private async Task<HeirOfferResponse> ToHeirOfferResponseAsync(
        HeirOfferEntity offer,
        CancellationToken cancellationToken)
    {
        var donorName = await database.Players.AsNoTracking()
            .Where(player => player.SteamId == offer.DonorSteamId)
            .Select(player => player.DisplayName)
            .SingleAsync(cancellationToken);
        var heirName = await database.Characters.AsNoTracking()
            .Where(character => character.CharacterId == offer.HeirCharacterId)
            .Select(character => character.Name)
            .SingleAsync(cancellationToken);
        return new HeirOfferResponse(
            offer.OfferId.ToString("D"),
            decimal.Truncate(offer.DonorSteamId).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            donorName,
            decimal.Truncate(offer.RecipientSteamId).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            offer.DeceasedCharacterId.ToString("D"),
            offer.HeirCharacterId.ToString("D"),
            heirName,
            offer.OfferedAtUtc,
            offer.AcceptanceStartedAtUtc ?? offer.OfferedAtUtc,
            EffectiveOfferExpiry(offer));
    }

    private static void PrepareHeirForControl(CharacterEntity heir)
    {
        var heirJson = JsonNode.Parse(heir.SurvivalJson)?.AsObject()
            ?? throw new ArgumentException("The heir aggregate is invalid.");
        heirJson["ControlKind"] = 0;
        heirJson["Offline"] = false;
        if (heirJson["WorkerContract"] is JsonObject contract) contract["Active"] = false;
        if (heirJson["Npc"] is JsonObject npc)
        {
            npc["EmployerAccountId"] = string.Empty;
            npc["EmployerCharacterId"] = string.Empty;
            npc["Activity"] = 0;
        }
        heir.Revision++;
        heir.SurvivalJson = heirJson.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        heir.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidateHeir(PlayerEntity player, CharacterEntity heir)
    {
        if (heir.ControllingPlayer is not null)
            throw new ArgumentException("Another account already controls the heir.");
        if (heir.LifeState == 4 || ReadControlKind(heir.SurvivalJson) != 2)
            throw new ArgumentException("The heir must be a living voluntary worker.");
        if (heir.CharacterId == player.CurrentCharacterId)
            throw new ArgumentException("A character cannot inherit from itself.");
    }

    private static void ValidateHeirRelationship(PlayerEntity player, CharacterEntity heir)
    {
        using var document = JsonDocument.Parse(heir.SurvivalJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("WorkerContract", out var contract)
            || contract.ValueKind != JsonValueKind.Object
            || !contract.TryGetProperty("Active", out var active) || !active.GetBoolean()
            || !contract.TryGetProperty("Voluntary", out var voluntary) || !voluntary.GetBoolean()
            || !contract.TryGetProperty("EmployerAccountId", out var employer)
            || employer.GetString() != decimal.Truncate(player.SteamId).ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            || !contract.TryGetProperty("FulfilledContractGameSeconds", out var fulfilled)
            || fulfilled.GetDouble() < RequiredHeirContractGameSeconds)
            throw new ArgumentException("The voluntary contract has not been fulfilled for two days.");
        if (!root.TryGetProperty("Relationships", out var relationships)
            || relationships.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("The heir has no proven voluntary relationship.");
        foreach (var relationship in relationships.EnumerateArray())
        {
            if (relationship.TryGetProperty("TargetCharacterId", out var target)
                && target.GetString() == player.CurrentCharacterId?.ToString("D")
                && relationship.TryGetProperty("VoluntaryLoyalty", out var loyal)
                && loyal.GetBoolean()
                && relationship.TryGetProperty("PersonalRequestsCompleted", out var requests)
                && requests.GetInt32() >= 3) return;
        }
        throw new ArgumentException("Three personal requests and voluntary loyalty are required.");
    }

    private async Task<IReadOnlyList<PlayerMapNoteEntity>> LoadCurrentMapNotesAsync(
        PlayerEntity player, CancellationToken cancellationToken)
    {
        var mapIds = (player.CurrentCharacter?.Items ?? [])
            .Where(item => item.StorageArea == 0 && item.ItemId == 36
                && item.ItemInstanceId.HasValue)
            .Select(item => item.ItemInstanceId!.Value).Distinct().ToArray();
        return mapIds.Length == 0 ? [] : await database.PlayerMapNotes.AsNoTracking()
            .Where(note => mapIds.Contains(note.MapItemInstanceId))
            .ToArrayAsync(cancellationToken);
    }
}
