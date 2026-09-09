namespace Quieter.ProfileService.Contracts;

public sealed record WorldResponse(
    int WorldId,
    long Seed,
    ushort GeneratorVersion,
    ushort ChunkCountX,
    ushort ChunkCountZ,
    ushort ChunkSize,
    ushort SamplesPerSide,
    float HeightStep);

public sealed record PlayerLoginRequest(
    string SteamId,
    string DisplayName,
    float DefaultX,
    float DefaultY,
    float DefaultZ);

public sealed record PlayerProfileResponse(
    string SteamId,
    string DisplayName,
    float PositionX,
    float PositionY,
    float PositionZ,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    IReadOnlyList<InventorySlotResponse> InventorySlots,
    IReadOnlyList<InventorySlotResponse> PendingItems,
    byte SelectedHotbarIndex,
    IReadOnlyList<DepositKnowledgeResponse> DepositKnowledge,
    IReadOnlyList<MapNoteResponse> MapNotes,
    string CharacterId,
    string SurvivalJson,
    long SurvivalRevision,
    string? RegisteredHeirCharacterId = null,
    long EstateRevision = 0);

public sealed record PositionRequest(float X, float Y, float Z);

public sealed record InventorySlotResponse(
    byte SlotIndex,
    ushort ItemId,
    ushort Quantity,
    ushort Condition = 0,
    byte Quality = 0,
    ushort HiddenItemId = 0,
    string? SourceNodeId = null,
    byte RevealAtPercent = 0,
    string? SampleId = null,
    string? ItemInstanceId = null,
    ushort Freshness = 10000,
    ushort BiologicalContamination = 0,
    ushort ToxinContamination = 0,
    ushort Wetness = 0,
    ushort Cleanliness = 10000,
    ushort LiquidMilliliters = 0,
    byte LiquidKind = 0,
    bool Equipped = false);

public sealed record InventoryRequest(
    byte SelectedHotbarIndex,
    IReadOnlyList<InventorySlotResponse> Slots,
    IReadOnlyList<InventorySlotResponse>? PendingItems = null);

public sealed record SurvivalRequest(string SurvivalJson, long Revision);

public sealed record PlayerSnapshotRequest(
    string CharacterId, PositionRequest Position, InventoryRequest Inventory, SurvivalRequest Survival);

public sealed record WorldCharacterListResponse(IReadOnlyList<PlayerProfileResponse> Characters);

public sealed record NewStrangerRequest(
    string OperationId, string PreviousCharacterId, long ExpectedRevision, PositionRequest Position);

public sealed record CharacterPairSnapshotRequest(
    string OperationId, PlayerSnapshotRequest Source,
    string DestinationSteamId, PlayerSnapshotRequest Destination);

public sealed record CreateWorldNpcRequest(string Name, PlayerSnapshotRequest Snapshot);

public sealed record RegisterHeirRequest(string HeirCharacterId, long ExpectedEstateRevision);

public sealed record AssumeHeirRequest(
    string OperationId, string DeceasedCharacterId, long ExpectedRevision);

public sealed record CreateHeirOfferRequest(
    string OperationId,
    string RecipientSteamId,
    string DeceasedCharacterId,
    long ExpectedDonorEstateRevision);

public sealed record AcceptHeirOfferRequest(
    string OperationId,
    string DeceasedCharacterId,
    long ExpectedRevision);

public sealed record HeirOfferResponse(
    string OfferId,
    string DonorSteamId,
    string DonorDisplayName,
    string RecipientSteamId,
    string DeceasedCharacterId,
    string HeirCharacterId,
    string HeirName,
    DateTime OfferedAtUtc,
    DateTime AcceptanceStartedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record PlacedObjectResponse(
    string ObjectId,
    ushort ItemId,
    float X,
    float Y,
    float Z,
    float Yaw,
    InventorySlotResponse? Input,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? OwnerAccountId = null,
    bool Locked = false,
    string? AssignedCharacterId = null);

public sealed record PlacedObjectListResponse(
    IReadOnlyList<PlacedObjectResponse> Objects);

public sealed record ResourceNodeStateResponse(
    string InstanceId,
    ushort RemainingReserves,
    DateTime? AvailableAtUtc);

public sealed record ResourceNodeStateListResponse(
    IReadOnlyList<ResourceNodeStateResponse> Nodes);

public sealed record DepositKnowledgeResponse(
    string InstanceId,
    ushort StudyBasisPoints,
    DateTime DiscoveredAtUtc,
    int WorldId = 1);

public sealed record DepositKnowledgeListResponse(
    IReadOnlyList<DepositKnowledgeResponse> Knowledge);

public sealed record MapNoteResponse(
    string NoteId,
    float X,
    float Z,
    string Text,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int WorldId = 1,
    string MapItemInstanceId = "");

public sealed record MapNoteListResponse(IReadOnlyList<MapNoteResponse> Notes);
