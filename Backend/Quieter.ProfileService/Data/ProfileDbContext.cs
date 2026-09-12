using Microsoft.EntityFrameworkCore;

namespace Quieter.ProfileService.Data;

public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<WorldEntity> Worlds => Set<WorldEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CharacterReplacementEntity> CharacterReplacements => Set<CharacterReplacementEntity>();
    public DbSet<CharacterTransferEntity> CharacterTransfers => Set<CharacterTransferEntity>();
    public DbSet<InheritanceTransitionEntity> InheritanceTransitions => Set<InheritanceTransitionEntity>();
    public DbSet<HeirOfferEntity> HeirOffers => Set<HeirOfferEntity>();
    public DbSet<CharacterItemEntity> CharacterItems => Set<CharacterItemEntity>();
    public DbSet<PlayerInventorySlotEntity> PlayerInventorySlots => Set<PlayerInventorySlotEntity>();
    public DbSet<PlayerPendingItemEntity> PlayerPendingItems => Set<PlayerPendingItemEntity>();
    public DbSet<WorldResourceNodeEntity> WorldResourceNodes => Set<WorldResourceNodeEntity>();
    public DbSet<CharacterDepositKnowledgeEntity> CharacterDepositKnowledge => Set<CharacterDepositKnowledgeEntity>();
    public DbSet<CharacterPhysiologyEntity> CharacterPhysiology => Set<CharacterPhysiologyEntity>();
    public DbSet<CharacterTraitEntity> CharacterTraits => Set<CharacterTraitEntity>();
    public DbSet<CharacterAttributeEntity> CharacterAttributes => Set<CharacterAttributeEntity>();
    public DbSet<CharacterSkillEntity> CharacterSkills => Set<CharacterSkillEntity>();
    public DbSet<CharacterWoundEntity> CharacterWounds => Set<CharacterWoundEntity>();
    public DbSet<CharacterRelationshipEntity> CharacterRelationships => Set<CharacterRelationshipEntity>();
    public DbSet<CharacterWorkerContractEntity> CharacterWorkerContracts => Set<CharacterWorkerContractEntity>();
    public DbSet<CharacterNpcRuntimeEntity> CharacterNpcRuntime => Set<CharacterNpcRuntimeEntity>();
    public DbSet<CharacterWorkerJobEntity> CharacterWorkerJobs => Set<CharacterWorkerJobEntity>();
    public DbSet<CharacterNpcLessonEntity> CharacterNpcLessons => Set<CharacterNpcLessonEntity>();
    public DbSet<PlayerMapNoteEntity> PlayerMapNotes => Set<PlayerMapNoteEntity>();
    public DbSet<WorldPlacedObjectEntity> WorldPlacedObjects => Set<WorldPlacedObjectEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldEntity>(entity =>
        {
            entity.ToTable("worlds");
            entity.HasKey(world => world.Id);
            entity.Property(world => world.Id).ValueGeneratedNever();
            entity.Property(world => world.HeightStep).HasPrecision(6, 3);
        });

        modelBuilder.Entity<PlayerEntity>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(player => player.SteamId);
            entity.Property(player => player.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
            entity.Property(player => player.DisplayName).HasMaxLength(32);
            entity.Property(player => player.SelectedHotbarIndex).HasDefaultValue((byte)0);
            entity.Property(player => player.CurrentCharacterId).IsConcurrencyToken();
            entity.Property(player => player.EstateRevision).IsConcurrencyToken();
            entity.HasIndex(player => player.RegisteredHeirCharacterId);
            entity.HasOne(player => player.CurrentCharacter)
                .WithOne(character => character.ControllingPlayer)
                .HasForeignKey<PlayerEntity>(player => player.CurrentCharacterId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(player => player.InventorySlots)
                .WithOne(slot => slot.Player)
                .HasForeignKey(slot => slot.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(player => player.PendingItems)
                .WithOne(item => item.Player)
                .HasForeignKey(item => item.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterEntity>(entity =>
        {
            entity.ToTable("characters");
            entity.HasKey(character => character.CharacterId);
            entity.Property(character => character.Name).HasMaxLength(32);
            entity.Property(character => character.SurvivalJson).HasColumnType("jsonb");
            entity.Property(character => character.Revision).IsConcurrencyToken();
            entity.HasIndex(character => character.UpdatedAtUtc);
        });

        modelBuilder.Entity<CharacterItemEntity>(entity =>
        {
            entity.ToTable("character_items");
            entity.HasKey(item => new { item.CharacterId, item.StorageArea, item.SlotIndex });
            entity.Property(item => item.SourceNodeId).HasPrecision(20, 0);
            entity.Property(item => item.SampleId).HasPrecision(20, 0);
            entity.Property(item => item.ItemInstanceId).HasPrecision(20, 0);
            entity.HasIndex(item => item.ItemInstanceId);
            entity.HasOne(item => item.Character).WithMany(character => character.Items)
                .HasForeignKey(item => item.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterReplacementEntity>(entity =>
        {
            entity.ToTable("character_replacements");
            entity.HasKey(entry => entry.OperationId);
            entity.Property(entry => entry.SteamId).HasPrecision(20, 0);
            entity.HasIndex(entry => entry.PreviousCharacterId).IsUnique();
            entity.HasOne(entry => entry.Player).WithMany()
                .HasForeignKey(entry => entry.SteamId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterTransferEntity>(entity =>
        {
            entity.ToTable("character_transfers");
            entity.HasKey(entry => entry.OperationId);
            entity.HasIndex(entry => entry.CreatedAtUtc);
        });

        modelBuilder.Entity<InheritanceTransitionEntity>(entity =>
        {
            entity.ToTable("inheritance_transitions");
            entity.HasKey(entry => entry.OperationId);
            entity.Property(entry => entry.SteamId).HasPrecision(20, 0);
            entity.HasIndex(entry => entry.DeceasedCharacterId).IsUnique();
            entity.HasOne(entry => entry.Player).WithMany()
                .HasForeignKey(entry => entry.SteamId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HeirOfferEntity>(entity =>
        {
            entity.ToTable("heir_offers");
            entity.HasKey(entry => entry.OfferId);
            entity.HasIndex(entry => entry.OperationId).IsUnique();
            entity.HasIndex(entry => new { entry.RecipientSteamId, entry.Status });
            entity.HasIndex(entry => entry.HeirCharacterId);
            entity.Property(entry => entry.DonorSteamId).HasPrecision(20, 0);
            entity.Property(entry => entry.RecipientSteamId).HasPrecision(20, 0);
        });

        modelBuilder.Entity<PlayerInventorySlotEntity>(entity =>
        {
            entity.ToTable("player_inventory_slots");
            entity.HasKey(slot => new { slot.SteamId, slot.SlotIndex });
            entity.Property(slot => slot.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
            entity.Property(slot => slot.SourceNodeId).HasPrecision(20, 0);
            entity.Property(slot => slot.SampleId).HasPrecision(20, 0);
            entity.Property(slot => slot.ItemInstanceId).HasPrecision(20, 0);
        });

        modelBuilder.Entity<PlayerPendingItemEntity>(entity =>
        {
            entity.ToTable("player_pending_items");
            entity.HasKey(item => new { item.SteamId, item.ItemIndex });
            entity.Property(item => item.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
            entity.Property(item => item.SourceNodeId).HasPrecision(20, 0);
            entity.Property(item => item.SampleId).HasPrecision(20, 0);
            entity.Property(item => item.ItemInstanceId).HasPrecision(20, 0);
        });

        modelBuilder.Entity<WorldPlacedObjectEntity>(entity =>
        {
            entity.ToTable("world_placed_objects");
            entity.HasKey(item => new { item.WorldId, item.ObjectId });
            entity.Property(item => item.ObjectId).HasPrecision(20, 0);
            entity.Property(item => item.OwnerAccountId).HasPrecision(20, 0);
            entity.HasIndex(item => item.AssignedCharacterId);
            entity.Property(item => item.InputSourceNodeId).HasPrecision(20, 0);
            entity.Property(item => item.InputSampleId).HasPrecision(20, 0);
            entity.Property(item => item.InputItemInstanceId).HasPrecision(20, 0);
            entity.HasOne(item => item.World)
                .WithMany()
                .HasForeignKey(item => item.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorldResourceNodeEntity>(entity =>
        {
            entity.ToTable("world_resource_nodes");
            entity.HasKey(node => new { node.WorldId, node.InstanceId });
            entity.Property(node => node.InstanceId).HasPrecision(20, 0);
            entity.HasOne(node => node.World)
                .WithMany()
                .HasForeignKey(node => node.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterDepositKnowledgeEntity>(entity =>
        {
            entity.ToTable("character_deposit_knowledge");
            entity.HasKey(entry => new { entry.CharacterId, entry.WorldId, entry.InstanceId });
            entity.Property(entry => entry.InstanceId).HasPrecision(20, 0);
            entity.HasOne(entry => entry.Character)
                .WithMany(character => character.DepositKnowledge)
                .HasForeignKey(entry => entry.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(entry => entry.World)
                .WithMany()
                .HasForeignKey(entry => entry.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterPhysiologyEntity>(entity =>
        {
            entity.ToTable("character_physiology");
            entity.HasKey(entry => entry.CharacterId);
            entity.HasOne(entry => entry.Character).WithOne()
                .HasForeignKey<CharacterPhysiologyEntity>(entry => entry.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterTraitEntity>(entity =>
        {
            entity.ToTable("character_traits");
            entity.HasKey(entry => new { entry.CharacterId, entry.TraitId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterAttributeEntity>(entity =>
        {
            entity.ToTable("character_attributes");
            entity.HasKey(entry => new { entry.CharacterId, entry.AttributeId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterSkillEntity>(entity =>
        {
            entity.ToTable("character_skills");
            entity.HasKey(entry => new { entry.CharacterId, entry.SkillId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterWoundEntity>(entity =>
        {
            entity.ToTable("character_wounds");
            entity.HasKey(entry => new { entry.CharacterId, entry.WoundId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterRelationshipEntity>(entity =>
        {
            entity.ToTable("character_relationships");
            entity.HasKey(entry => new { entry.CharacterId, entry.TargetCharacterId });
            entity.HasIndex(entry => entry.TargetCharacterId);
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterWorkerContractEntity>(entity =>
        {
            entity.ToTable("character_worker_contracts");
            entity.HasKey(entry => entry.CharacterId);
            entity.Property(entry => entry.EmployerAccountId).HasPrecision(20, 0);
            entity.Property(entry => entry.AssignedBedObjectId).HasPrecision(20, 0);
            entity.HasOne(entry => entry.Character).WithOne()
                .HasForeignKey<CharacterWorkerContractEntity>(entry => entry.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterNpcRuntimeEntity>(entity =>
        {
            entity.ToTable("character_npc_runtime");
            entity.HasKey(entry => entry.CharacterId);
            entity.Property(entry => entry.EmployerAccountId).HasPrecision(20, 0);
            entity.HasIndex(entry => entry.EmployerAccountId);
            entity.HasIndex(entry => entry.EmployerCharacterId);
            entity.HasOne(entry => entry.Character).WithOne()
                .HasForeignKey<CharacterNpcRuntimeEntity>(entry => entry.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterWorkerJobEntity>(entity =>
        {
            entity.ToTable("character_worker_jobs");
            entity.HasKey(entry => new { entry.CharacterId, entry.JobId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterNpcLessonEntity>(entity =>
        {
            entity.ToTable("character_npc_lessons");
            entity.HasKey(entry => new { entry.CharacterId, entry.SkillId });
            entity.HasOne(entry => entry.Character).WithMany()
                .HasForeignKey(entry => entry.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerMapNoteEntity>(entity =>
        {
            entity.ToTable("player_map_notes");
            entity.HasKey(note => new
            {
                note.WorldId,
                note.MapItemInstanceId,
                note.NoteId,
            });
            entity.Property(note => note.LastEditorSteamId).HasPrecision(20, 0);
            entity.Property(note => note.MapItemInstanceId).HasPrecision(20, 0);
            entity.Property(note => note.NoteId).HasPrecision(20, 0);
            entity.Property(note => note.Text).HasMaxLength(80);
            entity.HasOne(note => note.World)
                .WithMany()
                .HasForeignKey(note => note.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
