using Microsoft.EntityFrameworkCore;

namespace Quieter.ProfileService.Data;

public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<WorldEntity> Worlds => Set<WorldEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();
    public DbSet<PlayerInventorySlotEntity> PlayerInventorySlots => Set<PlayerInventorySlotEntity>();
    public DbSet<WorldResourceNodeEntity> WorldResourceNodes => Set<WorldResourceNodeEntity>();
    public DbSet<PlayerDepositKnowledgeEntity> PlayerDepositKnowledge => Set<PlayerDepositKnowledgeEntity>();
    public DbSet<PlayerMapNoteEntity> PlayerMapNotes => Set<PlayerMapNoteEntity>();

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
        });

        modelBuilder.Entity<PlayerInventorySlotEntity>(entity =>
        {
            entity.ToTable("player_inventory_slots");
            entity.HasKey(slot => new { slot.SteamId, slot.SlotIndex });
            entity.Property(slot => slot.SteamId).HasPrecision(20, 0).ValueGeneratedNever();
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
