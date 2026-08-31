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
    byte SelectedHotbarIndex,
    IReadOnlyList<DepositKnowledgeResponse> DepositKnowledge,
    IReadOnlyList<MapNoteResponse> MapNotes);

public sealed record PositionRequest(float X, float Y, float Z);

public sealed record InventorySlotResponse(
    byte SlotIndex,
    ushort ItemId,
    ushort Quantity,
    ushort Condition = 0,
    byte Quality = 0,
    ushort HiddenItemId = 0,
    string? SourceNodeId = null,
    byte RevealAtPercent = 0);

public sealed record InventoryRequest(
    byte SelectedHotbarIndex,
    IReadOnlyList<InventorySlotResponse> Slots);

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
    int WorldId = 1);

public sealed record MapNoteListResponse(IReadOnlyList<MapNoteResponse> Notes);
