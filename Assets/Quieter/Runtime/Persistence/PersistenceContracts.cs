using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Quieter.Inventory;
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
        public List<StoredInventorySlot> InventorySlots = new();
        public List<StoredInventorySlot> PendingItems = new();
        public byte SelectedHotbarIndex;
        public List<StoredDepositKnowledge> DepositKnowledge = new();
        public List<StoredMapNote> MapNotes = new();
    }

    public interface IWorldRepository
    {
        Task<WorldDefinition> GetOrCreateWorldAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StoredResourceNodeState>> LoadResourceNodeStatesAsync(
            int worldId,
            CancellationToken cancellationToken = default);

        Task SaveResourceNodeStatesAsync(
            int worldId,
            IReadOnlyList<StoredResourceNodeState> states,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StoredPlacedObject>> LoadPlacedObjectsAsync(
            int worldId,
            CancellationToken cancellationToken = default);

        Task SavePlacedObjectsAsync(
            int worldId,
            IReadOnlyList<StoredPlacedObject> objects,
            CancellationToken cancellationToken = default);
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

        Task SaveInventoryAsync(
            ulong steamId,
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedHotbarIndex,
            CancellationToken cancellationToken = default);

        Task SaveDepositKnowledgeAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredDepositKnowledge> knowledge,
            CancellationToken cancellationToken = default);

        Task SaveMapNotesAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredMapNote> notes,
            CancellationToken cancellationToken = default);
    }
}
