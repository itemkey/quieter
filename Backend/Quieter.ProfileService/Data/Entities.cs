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
    public Guid? CurrentCharacterId { get; set; }
    public CharacterEntity? CurrentCharacter { get; set; }
    public List<PlayerInventorySlotEntity> InventorySlots { get; set; } = [];
    public List<PlayerPendingItemEntity> PendingItems { get; set; } = [];
    public List<PlayerDepositKnowledgeEntity> DepositKnowledge { get; set; } = [];
}

public sealed class CharacterEntity
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SurvivalJson { get; set; } = "{}";
    public long Revision { get; set; }
    public byte LifeState { get; set; }
    public byte DeathCause { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public PlayerEntity? ControllingPlayer { get; set; }
    public List<CharacterItemEntity> Items { get; set; } = [];
}

public sealed class CharacterItemEntity
{
    public Guid CharacterId { get; set; }
    public byte StorageArea { get; set; } // 0: carried inventory, 1: pending overflow.
    public ushort SlotIndex { get; set; }
    public ushort ItemId { get; set; }
    public ushort Quantity { get; set; }
    public ushort Condition { get; set; }
    public byte Quality { get; set; }
    public ushort HiddenItemId { get; set; }
    public decimal? SourceNodeId { get; set; }
    public byte RevealAtPercent { get; set; }
    public decimal? SampleId { get; set; }
    public decimal? ItemInstanceId { get; set; }
    public ushort Freshness { get; set; } = 10000;
    public ushort BiologicalContamination { get; set; }
    public ushort ToxinContamination { get; set; }
    public ushort Wetness { get; set; }
    public ushort Cleanliness { get; set; } = 10000;
    public ushort LiquidMilliliters { get; set; }
    public byte LiquidKind { get; set; }
    public bool Equipped { get; set; }
    public CharacterEntity Character { get; set; } = null!;
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
    public decimal? ItemInstanceId { get; set; }
    public ushort Freshness { get; set; } = 10000;
    public ushort BiologicalContamination { get; set; }
    public ushort ToxinContamination { get; set; }
    public ushort Wetness { get; set; }
    public ushort Cleanliness { get; set; } = 10000;
    public ushort LiquidMilliliters { get; set; }
    public byte LiquidKind { get; set; }
    public bool Equipped { get; set; }
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
    public decimal? ItemInstanceId { get; set; }
    public ushort Freshness { get; set; } = 10000;
    public ushort BiologicalContamination { get; set; }
    public ushort ToxinContamination { get; set; }
    public ushort Wetness { get; set; }
    public ushort Cleanliness { get; set; } = 10000;
    public ushort LiquidMilliliters { get; set; }
    public byte LiquidKind { get; set; }
    public bool Equipped { get; set; }
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
    public decimal? InputItemInstanceId { get; set; }
    public ushort InputFreshness { get; set; } = 10000;
    public ushort InputBiologicalContamination { get; set; }
    public ushort InputToxinContamination { get; set; }
    public ushort InputWetness { get; set; }
    public ushort InputCleanliness { get; set; } = 10000;
    public ushort InputLiquidMilliliters { get; set; }
    public byte InputLiquidKind { get; set; }
    public bool InputEquipped { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public WorldEntity World { get; set; } = null!;
}

public sealed class PlayerMapNoteEntity
{
    public decimal SteamId { get; set; }
    public int WorldId { get; set; }
    public decimal MapItemInstanceId { get; set; }
    public decimal NoteId { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public WorldEntity World { get; set; } = null!;
}
