namespace Quieter.ProfileService.Data;

public sealed class WorldEntity
{
    public int Id { get; set; }
    public long Seed { get; set; }
    public ushort GeneratorVersion { get; set; }
    public ushort ChunkCountX { get; set; }
    public ushort ChunkCountZ { get; set; }
    public ushort ChunkSize { get; set; }
    public ushort SamplesPerSide { get; set; }
    public float HeightStep { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PlayerEntity
{
    public decimal SteamId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public byte SelectedHotbarIndex { get; set; }
    public List<PlayerInventorySlotEntity> InventorySlots { get; set; } = [];
    public List<PlayerPendingItemEntity> PendingItems { get; set; } = [];
    public List<PlayerDepositKnowledgeEntity> DepositKnowledge { get; set; } = [];
    public List<PlayerMapNoteEntity> MapNotes { get; set; } = [];
}

public sealed class WorldResourceNodeEntity
{
    public int WorldId { get; set; }
    public decimal InstanceId { get; set; }
    public ushort RemainingReserves { get; set; }
    public DateTime? AvailableAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public WorldEntity World { get; set; } = null!;
}

public sealed class PlayerDepositKnowledgeEntity
{
    public decimal SteamId { get; set; }
    public int WorldId { get; set; }
    public decimal InstanceId { get; set; }
    public ushort StudyBasisPoints { get; set; }
    public DateTime DiscoveredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public PlayerEntity Player { get; set; } = null!;
    public WorldEntity World { get; set; } = null!;
}

public sealed class PlayerInventorySlotEntity
{
    public decimal SteamId { get; set; }
    public byte SlotIndex { get; set; }
    public ushort ItemId { get; set; }
    public ushort Quantity { get; set; }
    public ushort Condition { get; set; }
    public byte Quality { get; set; }
    public ushort HiddenItemId { get; set; }
    public decimal? SourceNodeId { get; set; }
    public byte RevealAtPercent { get; set; }
    public decimal? SampleId { get; set; }
    public PlayerEntity Player { get; set; } = null!;
}

public sealed class PlayerPendingItemEntity
{
    public decimal SteamId { get; set; }
    public ushort ItemIndex { get; set; }
    public ushort ItemId { get; set; }
    public ushort Quantity { get; set; }
    public ushort Condition { get; set; }
    public byte Quality { get; set; }
    public ushort HiddenItemId { get; set; }
    public decimal? SourceNodeId { get; set; }
    public byte RevealAtPercent { get; set; }
    public decimal? SampleId { get; set; }
    public PlayerEntity Player { get; set; } = null!;
}

public sealed class WorldPlacedObjectEntity
{
    public int WorldId { get; set; }
    public decimal ObjectId { get; set; }
    public ushort ItemId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public ushort InputItemId { get; set; }
    public ushort InputQuantity { get; set; }
    public ushort InputCondition { get; set; }
    public byte InputQuality { get; set; }
    public ushort InputHiddenItemId { get; set; }
    public decimal? InputSourceNodeId { get; set; }
    public byte InputRevealAtPercent { get; set; }
    public decimal? InputSampleId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public WorldEntity World { get; set; } = null!;
}

public sealed class PlayerMapNoteEntity
{
    public decimal SteamId { get; set; }
    public int WorldId { get; set; }
    public decimal NoteId { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public PlayerEntity Player { get; set; } = null!;
    public WorldEntity World { get; set; } = null!;
}
