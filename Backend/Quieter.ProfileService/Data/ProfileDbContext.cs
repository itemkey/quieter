using Microsoft.EntityFrameworkCore;

namespace Quieter.ProfileService.Data;

public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<WorldEntity> Worlds => Set<WorldEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<PlayerInventorySlotEntity> PlayerInventorySlots => Set<PlayerInventorySlotEntity>();
    public DbSet<PlayerPendingItemEntity> PlayerPendingItems => Set<PlayerPendingItemEntity>();
    public DbSet<WorldResourceNodeEntity> WorldResourceNodes => Set<WorldResourceNodeEntity>();
    public DbSet<PlayerDepositKnowledgeEntity> PlayerDepositKnowledge => Set<PlayerDepositKnowledgeEntity>();
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
            entity.HasMany(player => player.InventorySlots)
                .WithOne(slot => slot.Player)
                .HasForeignKey(slot => slot.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(player => player.PendingItems)
                .WithOne(item => item.Player)
                .HasForeignKey(item => item.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerInventorySlotEntity>(entity =>
        {
            entity.ToTable("player_inventory_slots");
            entity.HasKey(slot => new { slot.SteamId, slot.SlotIndex });
            entity.Property(slot => slot.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
            entity.Property(slot => slot.SourceNodeId).HasPrecision(20, 0);
            entity.Property(slot => slot.SampleId).HasPrecision(20, 0);
        });

        modelBuilder.Entity<PlayerPendingItemEntity>(entity =>
        {
            entity.ToTable("player_pending_items");
            entity.HasKey(item => new { item.SteamId, item.ItemIndex });
            entity.Property(item => item.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
            entity.Property(item => item.SourceNodeId).HasPrecision(20, 0);
            entity.Property(item => item.SampleId).HasPrecision(20, 0);
        });

        modelBuilder.Entity<WorldPlacedObjectEntity>(entity =>
        {
            entity.ToTable("world_placed_objects");
            entity.HasKey(item => new { item.WorldId, item.ObjectId });
            entity.Property(item => item.ObjectId).HasPrecision(20, 0);
            entity.Property(item => item.InputSourceNodeId).HasPrecision(20, 0);
            entity.Property(item => item.InputSampleId).HasPrecision(20, 0);
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

        modelBuilder.Entity<PlayerDepositKnowledgeEntity>(entity =>
        {
            entity.ToTable("player_deposit_knowledge");
            entity.HasKey(entry => new { entry.SteamId, entry.WorldId, entry.InstanceId });
            entity.Property(entry => entry.SteamId).HasPrecision(20, 0);
            entity.Property(entry => entry.InstanceId).HasPrecision(20, 0);
            entity.HasOne(entry => entry.Player)
                .WithMany(player => player.DepositKnowledge)
                .HasForeignKey(entry => entry.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(entry => entry.World)
                .WithMany()
                .HasForeignKey(entry => entry.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerMapNoteEntity>(entity =>
        {
            entity.ToTable("player_map_notes");
            entity.HasKey(note => new { note.SteamId, note.WorldId, note.NoteId });
            entity.Property(note => note.SteamId).HasPrecision(20, 0);
            entity.Property(note => note.NoteId).HasPrecision(20, 0);
            entity.Property(note => note.Text).HasMaxLength(80);
            entity.HasOne(note => note.Player)
                .WithMany(player => player.MapNotes)
                .HasForeignKey(note => note.SteamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(note => note.World)
                .WithMany()
                .HasForeignKey(note => note.WorldId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
