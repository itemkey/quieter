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
}
