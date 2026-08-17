using Microsoft.EntityFrameworkCore;

namespace Quieter.ProfileService.Data;

public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<WorldEntity> Worlds => Set<WorldEntity>();
    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();

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
        });
    }
}
