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
    public Guid? RegisteredHeirCharacterId { get; set; }
    public DateTime? HeirRegisteredAtUtc { get; set; }
    public long EstateRevision { get; set; }
    public CharacterEntity? CurrentCharacter { get; set; }
    public List<PlayerInventorySlotEntity> InventorySlots { get; set; } = [];
    public List<PlayerPendingItemEntity> PendingItems { get; set; } = [];
}

public sealed class CharacterEntity
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SurvivalJson { get; set; } = "{}";
    public long Revision { get; set; }
    public byte LifeState { get; set; }
    public byte DeathCause { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public byte SelectedHotbarIndex { get; set; }
    public DateTime? DiedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public PlayerEntity? ControllingPlayer { get; set; }
    public List<CharacterItemEntity> Items { get; set; } = [];
    public List<CharacterDepositKnowledgeEntity> DepositKnowledge { get; set; } = [];
}

public sealed class CharacterReplacementEntity
{
    public Guid OperationId { get; set; }
    public decimal SteamId { get; set; }
    public Guid PreviousCharacterId { get; set; }
    public Guid NewCharacterId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public PlayerEntity Player { get; set; } = null!;
}

public sealed class CharacterTransferEntity
{
    public Guid OperationId { get; set; }
    public Guid SourceCharacterId { get; set; }
    public Guid DestinationCharacterId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class InheritanceTransitionEntity
{
    public Guid OperationId { get; set; }
    public decimal SteamId { get; set; }
    public Guid DeceasedCharacterId { get; set; }
    public Guid HeirCharacterId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public PlayerEntity Player { get; set; } = null!;
}

public sealed class HeirOfferEntity
{
    public Guid OfferId { get; set; }
    public Guid OperationId { get; set; }
    public decimal DonorSteamId { get; set; }
    public decimal RecipientSteamId { get; set; }
    public Guid DeceasedCharacterId { get; set; }
    public Guid HeirCharacterId { get; set; }
    public DateTime OfferedAtUtc { get; set; }
    public DateTime? AcceptanceStartedAtUtc { get; set; }
    public DateTime HardExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public byte Status { get; set; }
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

public sealed class CharacterDepositKnowledgeEntity
{
    public Guid CharacterId { get; set; }
    public int WorldId { get; set; }
    public decimal InstanceId { get; set; }
    public ushort StudyBasisPoints { get; set; }
    public DateTime DiscoveredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public CharacterEntity Character { get; set; } = null!;
    public WorldEntity World { get; set; } = null!;
}

public sealed class CharacterPhysiologyEntity
{
    public Guid CharacterId { get; set; }
    public float AcuteStamina { get; set; }
    public float Oxygenation { get; set; }
    public float BloodVolume { get; set; }
    public float Hydration { get; set; }
    public float ElectrolyteBalance { get; set; }
    public float EnergyReserve { get; set; }
    public float ProteinReserve { get; set; }
    public float FatReserve { get; set; }
    public float MicronutrientReserve { get; set; }
    public float MineralReserve { get; set; }
    public float SleepDebt { get; set; }
    public float CircadianFatigue { get; set; }
    public float SleepNoiseBurden { get; set; }
    public float CoreTemperatureC { get; set; }
    public float Pain { get; set; }
    public float Stress { get; set; }
    public float Consciousness { get; set; }
    public float SystemicInfection { get; set; }
    public float ToxinLoad { get; set; }
    public float FoodborneInfection { get; set; }
    public float WaterborneInfection { get; set; }
    public float ParasiteLoad { get; set; }
    public float RespiratoryInfection { get; set; }
    public float BrainFunction { get; set; }
    public float HeartFunction { get; set; }
    public float LeftLungFunction { get; set; }
    public float RightLungFunction { get; set; }
    public float LiverFunction { get; set; }
    public float KidneyFunction { get; set; }
    public float GutFunction { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterTraitEntity
{
    public Guid CharacterId { get; set; }
    public byte TraitId { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterAttributeEntity
{
    public Guid CharacterId { get; set; }
    public byte AttributeId { get; set; }
    public float Value { get; set; }
    public float TrainingLoad { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterSkillEntity
{
    public Guid CharacterId { get; set; }
    public byte SkillId { get; set; }
    public float PracticeHours { get; set; }
    public float RelevantPracticeHours { get; set; }
    public float PendingConsolidationHours { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterWoundEntity
{
    public Guid CharacterId { get; set; }
    public long WoundId { get; set; }
    public byte BodyRegion { get; set; }
    public byte InjuryType { get; set; }
    public float Severity { get; set; }
    public float TissueDamage { get; set; }
    public float Contamination { get; set; }
    public float Infection { get; set; }
    public float Bleeding { get; set; }
    public float InternalBleedingSeverity { get; set; }
    public float Pain { get; set; }
    public float PermanentImpairment { get; set; }
    public bool PressureApplied { get; set; }
    public bool Washed { get; set; }
    public bool Disinfected { get; set; }
    public bool Sutured { get; set; }
    public bool Bandaged { get; set; }
    public bool Splinted { get; set; }
    public bool Healed { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterRelationshipEntity
{
    public Guid CharacterId { get; set; }
    public Guid TargetCharacterId { get; set; }
    public float Trust { get; set; }
    public float Fear { get; set; }
    public float Resentment { get; set; }
    public float Loyalty { get; set; }
    public bool VoluntaryLoyalty { get; set; }
    public int PersonalRequestsCompleted { get; set; }
    public int PersuasionAttempts { get; set; }
    public int IntimidationAttempts { get; set; }
    public long NextSocialAttemptUtcTicks { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterWorkerContractEntity
{
    public Guid CharacterId { get; set; }
    public Guid ContractId { get; set; }
    public decimal EmployerAccountId { get; set; }
    public decimal? AssignedBedObjectId { get; set; }
    public bool Active { get; set; }
    public bool Voluntary { get; set; }
    public float DailyRationCalories { get; set; }
    public float PromisedSafety { get; set; }
    public float WorkdayStartHour { get; set; }
    public float WorkdayEndHour { get; set; }
    public ushort PaymentItemId { get; set; }
    public ushort PaymentQuantity { get; set; }
    public double FulfilledContractGameSeconds { get; set; }
    public int ConsecutiveBreaches { get; set; }
    public float WorkZoneX { get; set; }
    public float WorkZoneY { get; set; }
    public float WorkZoneZ { get; set; }
    public float WorkZoneRadius { get; set; }
    public float StorageX { get; set; }
    public float StorageY { get; set; }
    public float StorageZ { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterNpcRuntimeEntity
{
    public Guid CharacterId { get; set; }
    public byte Disposition { get; set; }
    public byte Activity { get; set; }
    public byte ActiveJob { get; set; }
    public byte WorkbookSelectedJob { get; set; }
    public float HomeX { get; set; }
    public float HomeY { get; set; }
    public float HomeZ { get; set; }
    public float DestinationX { get; set; }
    public float DestinationY { get; set; }
    public float DestinationZ { get; set; }
    public decimal? EmployerAccountId { get; set; }
    public Guid? EmployerCharacterId { get; set; }
    public float Motivation { get; set; }
    public float WorkProgressSeconds { get; set; }
    public long NextDecisionUtcTicks { get; set; }
    public long LastNeedsActionUtcTicks { get; set; }
    public long LastWorkCompletedUtcTicks { get; set; }
    public long ContractEvaluationGameDay { get; set; }
    public float RationCaloriesCurrentDay { get; set; }
    public byte PersonalRequest { get; set; }
    public bool PersonalRequestPending { get; set; }
    public long PersonalRequestGameDay { get; set; }
    public long LastPersonalRequestCompletedGameDay { get; set; }
    public int PendingSabotageActions { get; set; }
    public long FleeUntilUtcTicks { get; set; }
    public byte LastLeadershipAction { get; set; }
    public long LastLeadershipPracticeUtcTicks { get; set; }
    public int LeadershipInstructions { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterWorkerJobEntity
{
    public Guid CharacterId { get; set; }
    public byte JobId { get; set; }
    public bool Allowed { get; set; }
    public byte Priority { get; set; }
    public int CompletedTasks { get; set; }
    public CharacterEntity Character { get; set; } = null!;
}

public sealed class CharacterNpcLessonEntity
{
    public Guid CharacterId { get; set; }
    public byte SkillId { get; set; }
    public int LessonsReceived { get; set; }
    public CharacterEntity Character { get; set; } = null!;
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
    public decimal? OwnerAccountId { get; set; }
    public Guid? AssignedCharacterId { get; set; }
    public ushort ItemId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public bool Locked { get; set; }
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
    public decimal LastEditorSteamId { get; set; }
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
