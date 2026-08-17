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
    DateTime LastSeenAtUtc);

public sealed record PositionRequest(float X, float Y, float Z);
