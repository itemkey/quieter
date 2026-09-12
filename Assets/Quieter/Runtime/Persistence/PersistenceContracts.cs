using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Quieter.Inventory;
using Quieter.World;
using Quieter.Survival;
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
        public CharacterSurvivalState Survival = new();
        public string RegisteredHeirCharacterId;
        public long EstateRevision;
    }

    public sealed class CharacterPersistenceSnapshot
    {
        public Vector3 Position;
        public IReadOnlyList<StoredInventorySlot> Slots;
        public IReadOnlyList<StoredInventorySlot> PendingItems;
        public byte SelectedHotbarIndex;
        public CharacterSurvivalState Survival;
    }

    [Serializable]
    public sealed class PendingHeirOffer
    {
        public string OfferId;
        public ulong DonorSteamId;
        public string DonorDisplayName;
        public ulong RecipientSteamId;
        public string DeceasedCharacterId;
        public string HeirCharacterId;
        public string HeirName;
        public DateTime OfferedAtUtc;
        public DateTime AcceptanceStartedAtUtc;
        public DateTime ExpiresAtUtc;
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
            string characterId,
            int worldId,
            IReadOnlyList<StoredDepositKnowledge> knowledge,
            CancellationToken cancellationToken = default);

        Task SaveMapNotesAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredMapNote> notes,
            CancellationToken cancellationToken = default);

        Task SaveSurvivalAsync(
            ulong steamId,
            CharacterSurvivalState survival,
            CancellationToken cancellationToken = default);
    }

    public interface IAtomicPlayerProfileRepository : IPlayerProfileRepository
    {
        Task SaveSnapshotAsync(
            ulong steamId, Vector3 position,
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedHotbarIndex, CharacterSurvivalState survival,
            CancellationToken cancellationToken = default);
    }

    public interface IPersistentCharacterRepository : IAtomicPlayerProfileRepository
    {
        Task<IReadOnlyList<PlayerProfile>> LoadWorldCharactersAsync(
            CancellationToken cancellationToken = default);

        Task SaveDetachedCharacterAsync(
            Vector3 position, IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems, byte selectedHotbarIndex,
            CharacterSurvivalState survival, CancellationToken cancellationToken = default);

        Task<PlayerProfile> CreateNewStrangerAsync(
            ulong steamId, string operationId, string previousCharacterId,
            long expectedRevision, Vector3 spawn,
            CancellationToken cancellationToken = default);

        Task SaveCharacterPairAsync(
            string operationId, CharacterPersistenceSnapshot source,
            ulong destinationSteamId, CharacterPersistenceSnapshot destination,
            CancellationToken cancellationToken = default);

        Task<PlayerProfile> CreateWorldNpcAsync(
            string name, CharacterPersistenceSnapshot snapshot,
            CancellationToken cancellationToken = default);
    }

    public interface IInheritanceRepository
    {
        Task<PlayerProfile> RegisterHeirAsync(
            ulong steamId, string heirCharacterId, long expectedEstateRevision,
            CancellationToken cancellationToken = default);

        Task<PlayerProfile> AssumeRegisteredHeirAsync(
            ulong steamId, string operationId, string deceasedCharacterId,
            long expectedRevision, CancellationToken cancellationToken = default);

        Task<PendingHeirOffer> OfferRegisteredHeirAsync(
            ulong donorSteamId, ulong recipientSteamId, string operationId,
            string deceasedCharacterId, long expectedDonorEstateRevision,
            CancellationToken cancellationToken = default);

        Task<PendingHeirOffer> GetPendingHeirOfferAsync(
            ulong recipientSteamId,
            CancellationToken cancellationToken = default);

        Task<PlayerProfile> AcceptHeirOfferAsync(
            ulong recipientSteamId, string offerId, string operationId,
            string deceasedCharacterId, long expectedRevision,
            CancellationToken cancellationToken = default);
    }
}
