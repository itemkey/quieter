using System;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using UnityEngine;

namespace Quieter.Persistence
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public ulong SteamId;
        public string DisplayName;
        public Vector3 Position;
        public DateTime CreatedAtUtc;
        public DateTime LastSeenAtUtc;
    }

    public interface IWorldRepository
    {
        Task<WorldDefinition> GetOrCreateWorldAsync(CancellationToken cancellationToken = default);
    }

    public interface IPlayerProfileRepository
    {
        Task<PlayerProfile> LoginAsync(
            ulong steamId,
            string displayName,
            Vector3 defaultSpawn,
            CancellationToken cancellationToken = default);

        Task SavePositionAsync(
            ulong steamId,
            Vector3 position,
            CancellationToken cancellationToken = default);
    }
}
