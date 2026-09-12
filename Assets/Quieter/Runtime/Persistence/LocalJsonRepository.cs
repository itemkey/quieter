using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using Quieter.Inventory;
using Quieter.Survival;
using UnityEngine;

namespace Quieter.Persistence
{
    public sealed class LocalJsonRepository : IWorldRepository, IPersistentCharacterRepository,
        IInheritanceRepository
    {
        [Serializable]
        private sealed class State
        {
            public bool HasWorld;
            public WorldDefinition World;
            public List<StoredPlayer> Players = new();
            public List<StoredResourceNodeState> ResourceNodes = new();
            public List<StoredPlacedObject> PlacedObjects = new();
            public List<StoredMapNote> MapNotes = new();
            public List<StoredCharacter> Characters = new();
            public List<StoredReplacement> Replacements = new();
            public List<StoredTransfer> Transfers = new();
            public List<StoredInheritance> Inheritances = new();
            public List<StoredHeirOffer> HeirOffers = new();
        }

        [Serializable]
        private sealed class StoredPlayer
        {
            public string SteamId;
            public string DisplayName;
            public Vector3 Position;
            public string CreatedAtUtc;
            public string LastSeenAtUtc;
            public List<StoredInventorySlot> InventorySlots = new();
            public List<StoredInventorySlot> PendingItems = new();
            public byte SelectedHotbarIndex;
            // Legacy v1 field. EnsureLoaded migrates it once to the controlled character.
            public List<StoredDepositKnowledge> DepositKnowledge = new();
            public List<StoredMapNote> MapNotes = new();
            public CharacterSurvivalState Survival = new();
            public string CurrentCharacterId;
            public string RegisteredHeirCharacterId;
            public long EstateRevision;
        }

        [Serializable]
        private sealed class StoredCharacter
        {
            public string CharacterId;
            public string Name;
            public Vector3 Position;
            public string CreatedAtUtc;
            public string UpdatedAtUtc;
            public string ControllingSteamId;
            public List<StoredInventorySlot> InventorySlots = new();
            public List<StoredInventorySlot> PendingItems = new();
            public byte SelectedHotbarIndex;
            public List<StoredDepositKnowledge> DepositKnowledge = new();
            public CharacterSurvivalState Survival = new();
        }

        [Serializable]
        private sealed class StoredReplacement
        {
            public string OperationId;
            public string SteamId;
            public string PreviousCharacterId;
            public string NewCharacterId;
        }

        [Serializable]
        private sealed class StoredTransfer
        {
            public string OperationId;
            public string SourceCharacterId;
            public string DestinationCharacterId;
        }

        [Serializable]
        private sealed class StoredInheritance
        {
            public string OperationId;
            public string SteamId;
            public string DeceasedCharacterId;
            public string HeirCharacterId;
        }

        [Serializable]
        private sealed class StoredHeirOffer
        {
            public string OfferId;
            public string OperationId;
            public string DonorSteamId;
            public string RecipientSteamId;
            public string DeceasedCharacterId;
            public string HeirCharacterId;
            public string OfferedAtUtc;
            public string AcceptanceStartedAtUtc;
            public string HardExpiresAtUtc;
            public string AcceptedAtUtc;
            public byte Status;
        }

        private readonly string path;
        private readonly object sync = new();
        private State state;

        public LocalJsonRepository(string customPath = null)
        {
            path = customPath ?? Path.Combine(Application.persistentDataPath, "quieter-local-state.json");
        }

        public Task<WorldDefinition> GetOrCreateWorldAsync(CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                if (!state.HasWorld)
                {
                    var seedBytes = Guid.NewGuid().ToByteArray();
                    state.World = WorldDefinition.CreateDefault(BitConverter.ToInt64(seedBytes, 0));
                    state.HasWorld = true;
                    Save();
                }
                else
                {
                    var changed = false;
                    if (state.World.WorldId == 0)
                    {
                        state.World.WorldId = 1;
                        changed = true;
                    }

                    if (state.World.GeneratorVersion != Core.QuieterConstants.GeneratorVersion)
                    {
                        throw new InvalidDataException(
                            $"Мир создан генератором v{state.World.GeneratorVersion}, "
                            + $"а этой сборке нужен v{Core.QuieterConstants.GeneratorVersion}. "
                            + "Сделайте резервную копию и выполните явный сброс мира; "
                            + "автоматическое преобразование отключено.");
                    }

                    if (changed)
                    {
                        Save();
                    }
                }

                return Task.FromResult(state.World);
            }
        }

        public Task<PlayerProfile> LoginAsync(
            ulong steamId,
            string displayName,
            Vector3 defaultSpawn,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var id = steamId.ToString();
                var player = state.Players.Find(candidate => candidate.SteamId == id);
                var now = DateTime.UtcNow;

                if (player == null)
                {
                    player = new StoredPlayer
                    {
                        SteamId = id,
                        DisplayName = SanitizeName(displayName),
                        Position = defaultSpawn,
                        CreatedAtUtc = now.ToString("O"),
                    };
                    state.Players.Add(player);
                }

                player.DisplayName = SanitizeName(displayName);
                var character = GetCurrentCharacter(player);
                if (character == null)
                {
                    character = CreateCharacter(player, defaultSpawn, now);
                    state.Characters.Add(character);
                    player.CurrentCharacterId = character.CharacterId;
                }
                player.LastSeenAtUtc = now.ToString("O");
                Save();
                return Task.FromResult(ToProfile(player, character));
            }
        }

        public Task<IReadOnlyList<PlayerProfile>> LoadWorldCharactersAsync(
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var result = new List<PlayerProfile>();
                foreach (var character in state.Characters)
                {
                    if (character?.Survival == null || character.Survival.Revision <= 0) continue;
                    var player = state.Players.Find(candidate =>
                        candidate.SteamId == character.ControllingSteamId
                        && candidate.CurrentCharacterId == character.CharacterId);
                    result.Add(ToProfile(player, character));
                }
                return Task.FromResult<IReadOnlyList<PlayerProfile>>(result);
            }
        }

        public Task<IReadOnlyList<StoredResourceNodeState>> LoadResourceNodeStatesAsync(
            int worldId,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                return Task.FromResult<IReadOnlyList<StoredResourceNodeState>>(
                    CloneNodeStates(state.ResourceNodes.FindAll(
                        candidate => candidate != null && candidate.WorldId == worldId)));
            }
        }

        public Task SaveResourceNodeStatesAsync(
            int worldId,
            IReadOnlyList<StoredResourceNodeState> states,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var byId = new Dictionary<string, StoredResourceNodeState>();
                foreach (var existing in state.ResourceNodes)
                {
                    if (existing != null && !string.IsNullOrWhiteSpace(existing.InstanceId))
                    {
                        byId[$"{existing.WorldId}:{existing.InstanceId}"] = existing;
                    }
                }
                if (states != null)
                {
                    foreach (var updated in states)
                    {
                        if (updated == null || string.IsNullOrWhiteSpace(updated.InstanceId)) continue;
                        var clone = CloneNodeState(updated);
                        clone.WorldId = worldId;
                        byId[$"{worldId}:{updated.InstanceId}"] = clone;
                    }
                }
                state.ResourceNodes = new List<StoredResourceNodeState>(byId.Values);
                Save();
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredPlacedObject>> LoadPlacedObjectsAsync(
            int worldId,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                return Task.FromResult<IReadOnlyList<StoredPlacedObject>>(
                    ClonePlacedObjects(state.PlacedObjects.FindAll(
                        candidate => candidate != null && candidate.WorldId == worldId)));
            }
        }

        public Task SavePlacedObjectsAsync(
            int worldId,
            IReadOnlyList<StoredPlacedObject> objects,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                state.PlacedObjects.RemoveAll(entry => entry == null || entry.WorldId == worldId);
                var replacements = ClonePlacedObjects(objects);
                foreach (var entry in replacements) entry.WorldId = worldId;
                state.PlacedObjects.AddRange(replacements);
                Save();
            }
            return Task.CompletedTask;
        }

        public Task SavePositionAsync(
            ulong steamId,
            Vector3 position,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var id = steamId.ToString();
                var player = state.Players.Find(candidate => candidate.SteamId == id);
                if (player != null)
                {
                    player.Position = position;
                    var character = GetCurrentCharacter(player);
                    if (character != null) character.Position = position;
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }

            return Task.CompletedTask;
        }

        public Task SaveInventoryAsync(
            ulong steamId,
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedHotbarIndex,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var id = steamId.ToString();
                var player = state.Players.Find(candidate => candidate.SteamId == id);
                if (player != null)
                {
                    var character = GetCurrentCharacter(player);
                    if (character == null) return Task.CompletedTask;
                    player.InventorySlots = CloneSlots(slots);
                    player.PendingItems = CloneSlots(pendingItems);
                    player.SelectedHotbarIndex = (byte)Math.Min(
                        (int)selectedHotbarIndex,
                        InventoryLayout.HotbarSlotCount - 1);
                    character.InventorySlots = CloneSlots(slots);
                    character.PendingItems = CloneSlots(pendingItems);
                    character.SelectedHotbarIndex = player.SelectedHotbarIndex;
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }

            return Task.CompletedTask;
        }

        public Task SaveDepositKnowledgeAsync(
            ulong steamId,
            string characterId,
            int worldId,
            IReadOnlyList<StoredDepositKnowledge> knowledge,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(candidate => candidate.SteamId == steamId.ToString());
                var character = GetCurrentCharacter(player);
                if (character != null)
                {
                    if (character.CharacterId != characterId)
                        throw new InvalidOperationException(
                            "Deposit knowledge belongs to a life the account no longer controls.");
                    character.DepositKnowledge.RemoveAll(entry => entry == null
                        || entry.WorldId == worldId);
                    var currentWorld = CloneKnowledge(knowledge);
                    foreach (var entry in currentWorld) entry.WorldId = worldId;
                    character.DepositKnowledge.AddRange(currentWorld);
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }
            return Task.CompletedTask;
        }

        public Task SaveMapNotesAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredMapNote> notes,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(candidate => candidate.SteamId == steamId.ToString());
                if (player != null)
                {
                    var carriedMapIds = new HashSet<string>(StringComparer.Ordinal);
                    var character = GetCurrentCharacter(player);
                    foreach (var slot in character?.InventorySlots ?? player.InventorySlots)
                    {
                        if (slot != null && slot.ItemId == 36
                            && !string.IsNullOrWhiteSpace(slot.ItemInstanceId))
                        {
                            carriedMapIds.Add(slot.ItemInstanceId);
                        }
                    }
                    state.MapNotes.RemoveAll(entry => entry == null
                        || entry.WorldId == worldId
                        && carriedMapIds.Contains(entry.MapItemInstanceId));
                    var currentWorld = CloneMapNotes(notes);
                    foreach (var entry in currentWorld)
                    {
                        if (!carriedMapIds.Contains(entry.MapItemInstanceId)) continue;
                        entry.WorldId = worldId;
                        state.MapNotes.Add(entry);
                    }
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }
            return Task.CompletedTask;
        }

        public Task SaveSnapshotAsync(
            ulong steamId, Vector3 position,
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedHotbarIndex, CharacterSurvivalState survival,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (survival == null || survival.Physiology == null || survival.Revision <= 0
                || string.IsNullOrWhiteSpace(survival.CharacterId)
                || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)
                || selectedHotbarIndex >= InventoryLayout.HotbarSlotCount
                || (survival.Physiology.LifeState == CharacterLifeState.Dead)
                    != (survival.Physiology.DeathCause != DeathCause.None))
                throw new ArgumentException("Invalid character snapshot.");
            lock (sync)
            {
                EnsureLoaded();
                var index = state.Players.FindIndex(candidate => candidate.SteamId == steamId.ToString());
                if (index < 0) throw new InvalidOperationException("Account not found.");
                var previous = state.Players[index];
                var character = GetCurrentCharacter(previous);
                if (character == null || character.CharacterId != survival.CharacterId)
                    throw new InvalidOperationException("Account now controls another character.");
                if (survival.Revision <= character.Survival.Revision) return Task.CompletedTask;
                if (character.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    && survival.Physiology.LifeState != CharacterLifeState.Dead)
                    throw new InvalidOperationException("Cannot resurrect an irreversibly dead character.");
                if (character.Survival.ControlKind == CharacterControlKind.ForcedNpc
                    && survival.ControlKind != CharacterControlKind.ForcedNpc)
                    throw new InvalidOperationException("A captured body cannot restore player control.");
                var oldPlayerJson = JsonUtility.ToJson(previous);
                var oldCharacterJson = JsonUtility.ToJson(character);
                character.Position = position;
                character.InventorySlots = CloneSlots(slots);
                character.PendingItems = CloneSlots(pendingItems);
                character.SelectedHotbarIndex = selectedHotbarIndex;
                character.Survival = CloneSurvival(survival);
                character.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                SyncLegacyPlayer(previous, character);
                try { Save(); }
                catch
                {
                    state.Players[index] = JsonUtility.FromJson<StoredPlayer>(oldPlayerJson);
                    state.Characters[state.Characters.IndexOf(character)] =
                        JsonUtility.FromJson<StoredCharacter>(oldCharacterJson);
                    throw;
                }
            }
            return Task.CompletedTask;
        }

        public Task SaveSurvivalAsync(
            ulong steamId,
            CharacterSurvivalState survival,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(candidate => candidate.SteamId == steamId.ToString());
                if (player != null && survival != null)
                {
                    var character = GetCurrentCharacter(player);
                    if (character == null) return Task.CompletedTask;
                    if (character.Survival.Physiology.LifeState == CharacterLifeState.Dead
                        && survival.Physiology.LifeState != CharacterLifeState.Dead)
                        throw new InvalidOperationException("Cannot resurrect an irreversibly dead character.");
                    if (character.Survival.ControlKind == CharacterControlKind.ForcedNpc
                        && survival.ControlKind != CharacterControlKind.ForcedNpc)
                        throw new InvalidOperationException(
                            "A captured body cannot restore player control.");
                    character.Survival = CloneSurvival(survival);
                    player.Survival = CloneSurvival(survival);
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }
            return Task.CompletedTask;
        }

        public Task SaveDetachedCharacterAsync(
            Vector3 position, IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems, byte selectedHotbarIndex,
            CharacterSurvivalState survival, CancellationToken cancellationToken = default)
        {
            ValidateSnapshot(position, selectedHotbarIndex, survival);
            lock (sync)
            {
                EnsureLoaded();
                var character = state.Characters.Find(entry => entry.CharacterId == survival.CharacterId)
                    ?? throw new InvalidOperationException("Body not found.");
                if (!string.IsNullOrWhiteSpace(character.ControllingSteamId))
                    throw new InvalidOperationException("This character is still account-controlled.");
                if (survival.Revision <= character.Survival.Revision) return Task.CompletedTask;
                if (character.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    && survival.Physiology.LifeState != CharacterLifeState.Dead)
                    throw new InvalidOperationException("Cannot resurrect an irreversibly dead character.");
                var previousJson = JsonUtility.ToJson(state);
                character.Position = position;
                character.InventorySlots = CloneSlots(slots);
                character.PendingItems = CloneSlots(pendingItems);
                character.SelectedHotbarIndex = selectedHotbarIndex;
                character.Survival = CloneSurvival(survival);
                character.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                try { Save(); }
                catch { state = JsonUtility.FromJson<State>(previousJson); throw; }
                return Task.CompletedTask;
            }
        }

        public Task<PlayerProfile> CreateNewStrangerAsync(
            ulong steamId, string operationId, string previousCharacterId,
            long expectedRevision, Vector3 spawn,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(operationId, out _) || !Guid.TryParse(previousCharacterId, out _)
                || expectedRevision <= 0 || !IsFinite(spawn))
                throw new ArgumentException("Invalid new-life request.");
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(entry => entry.SteamId == steamId.ToString())
                    ?? throw new InvalidOperationException("Account not found.");
                var receipt = state.Replacements.Find(entry => entry.OperationId == operationId);
                if (receipt != null)
                {
                    if (receipt.SteamId != player.SteamId || receipt.PreviousCharacterId != previousCharacterId
                        || player.CurrentCharacterId != receipt.NewCharacterId)
                        throw new InvalidOperationException("New-life operation belongs to another transition.");
                    return Task.FromResult(ToProfile(player, GetCurrentCharacter(player)));
                }
                var previous = GetCurrentCharacter(player);
                if (previous == null || previous.CharacterId != previousCharacterId
                    || previous.Survival.Revision != expectedRevision)
                    throw new InvalidOperationException("Character changed before new life was accepted.");
                if (previous.Survival.Physiology.LifeState != CharacterLifeState.Dead
                    && previous.Survival.ControlKind != CharacterControlKind.ForcedNpc)
                    throw new InvalidOperationException(
                        "Only the dead or captured may start another life.");
                if (state.Replacements.Exists(entry => entry.PreviousCharacterId == previousCharacterId))
                    throw new InvalidOperationException("This life was already replaced.");
                var previousJson = JsonUtility.ToJson(state);
                previous.ControllingSteamId = string.Empty;
                previous.Survival.Revision++;
                previous.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                var next = CreateCharacter(player, spawn, DateTime.UtcNow);
                state.Characters.Add(next);
                player.CurrentCharacterId = next.CharacterId;
                player.RegisteredHeirCharacterId = string.Empty;
                player.EstateRevision++;
                SyncLegacyPlayer(player, next);
                state.Replacements.Add(new StoredReplacement
                {
                    OperationId = operationId, SteamId = player.SteamId,
                    PreviousCharacterId = previousCharacterId, NewCharacterId = next.CharacterId,
                });
                try { Save(); }
                catch { state = JsonUtility.FromJson<State>(previousJson); throw; }
                return Task.FromResult(ToProfile(player, next));
            }
        }

        public Task SaveCharacterPairAsync(
            string operationId, CharacterPersistenceSnapshot source,
            ulong destinationSteamId, CharacterPersistenceSnapshot destination,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(operationId, out _) || source == null || destination == null)
                throw new ArgumentException("Invalid character transfer.");
            ValidateSnapshot(source.Position, source.SelectedHotbarIndex, source.Survival);
            ValidateSnapshot(destination.Position, destination.SelectedHotbarIndex, destination.Survival);
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in EnumerateSlots(source, destination))
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.ItemInstanceId)) continue;
                if (!instanceIds.Add(slot.ItemInstanceId))
                    throw new ArgumentException("An item instance cannot exist in both character snapshots.");
            }
            lock (sync)
            {
                EnsureLoaded();
                var receipt = state.Transfers.Find(entry => entry.OperationId == operationId);
                if (receipt != null)
                {
                    if (receipt.SourceCharacterId != source.Survival.CharacterId
                        || receipt.DestinationCharacterId != destination.Survival.CharacterId)
                        throw new InvalidOperationException("Transfer receipt belongs to other characters.");
                    return Task.CompletedTask;
                }
                var sourceCharacter = state.Characters.Find(
                    entry => entry.CharacterId == source.Survival.CharacterId);
                var player = state.Players.Find(entry => entry.SteamId == destinationSteamId.ToString());
                var destinationCharacter = GetCurrentCharacter(player);
                var sourcePlayer = state.Players.Find(entry =>
                    entry.SteamId == sourceCharacter?.ControllingSteamId
                    && entry.CurrentCharacterId == sourceCharacter?.CharacterId);
                if (sourceCharacter == null || destinationCharacter == null
                    || sourcePlayer == player
                    || destinationCharacter.CharacterId != destination.Survival.CharacterId
                    || sourceCharacter.Survival.Physiology.LifeState != CharacterLifeState.Dead
                    || destination.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    || source.Survival.Revision <= sourceCharacter.Survival.Revision
                    || destination.Survival.Revision <= destinationCharacter.Survival.Revision)
                    throw new InvalidOperationException("Character state changed during item transfer.");
                var previousJson = JsonUtility.ToJson(state);
                ApplyCharacterSnapshot(sourceCharacter, source);
                ApplyCharacterSnapshot(destinationCharacter, destination);
                if (sourcePlayer != null) SyncLegacyPlayer(sourcePlayer, sourceCharacter);
                SyncLegacyPlayer(player, destinationCharacter);
                state.Transfers.Add(new StoredTransfer
                {
                    OperationId = operationId,
                    SourceCharacterId = sourceCharacter.CharacterId,
                    DestinationCharacterId = destinationCharacter.CharacterId,
                });
                try { Save(); }
                catch { state = JsonUtility.FromJson<State>(previousJson); throw; }
                return Task.CompletedTask;
            }
        }

        public Task<PlayerProfile> CreateWorldNpcAsync(
            string name, CharacterPersistenceSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ValidateSnapshot(snapshot.Position, snapshot.SelectedHotbarIndex, snapshot.Survival);
            if (snapshot.Survival.Physiology.LifeState == CharacterLifeState.Dead
                || snapshot.Survival.ControlKind is < CharacterControlKind.FreeNpc
                    or > CharacterControlKind.ForcedNpc)
                throw new ArgumentException("A new NPC must be alive and NPC-controlled.");
            lock (sync)
            {
                EnsureLoaded();
                var existing = state.Characters.Find(entry =>
                    entry.CharacterId == snapshot.Survival.CharacterId);
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(existing.ControllingSteamId)
                        || existing.Survival.ControlKind is < CharacterControlKind.FreeNpc
                            or > CharacterControlKind.ForcedNpc)
                        throw new InvalidOperationException(
                            "Character identifier is already controlled by a player.");
                    return Task.FromResult(ToProfile(null, existing));
                }
                var now = DateTime.UtcNow.ToString("O");
                var character = new StoredCharacter
                {
                    CharacterId = snapshot.Survival.CharacterId,
                    Name = SanitizeName(name),
                    Position = snapshot.Position,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    InventorySlots = CloneSlots(snapshot.Slots),
                    PendingItems = CloneSlots(snapshot.PendingItems),
                    SelectedHotbarIndex = snapshot.SelectedHotbarIndex,
                    Survival = CloneSurvival(snapshot.Survival),
                };
                state.Characters.Add(character);
                Save();
                return Task.FromResult(ToProfile(null, character));
            }
        }

        public Task<PlayerProfile> RegisterHeirAsync(
            ulong steamId, string heirCharacterId, long expectedEstateRevision,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(entry => entry.SteamId == steamId.ToString())
                    ?? throw new InvalidOperationException("Player does not exist.");
                if (player.EstateRevision != expectedEstateRevision)
                    throw new InvalidOperationException("The estate was already changed.");
                var owner = GetCurrentCharacter(player)
                    ?? throw new InvalidOperationException("The account has no identity.");
                var heir = state.Characters.Find(entry => entry.CharacterId == heirCharacterId)
                    ?? throw new InvalidOperationException("Heir does not exist.");
                if (!string.IsNullOrWhiteSpace(heir.ControllingSteamId)
                    || heir.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    || heir.Survival.ControlKind != CharacterControlKind.ContractedNpc)
                    throw new InvalidOperationException("The heir must be a living voluntary worker.");
                if (state.HeirOffers.Exists(entry => entry != null
                        && entry.HeirCharacterId == heirCharacterId))
                    throw new InvalidOperationException(
                        "The heir is already reserved by an irrevocable offer.");
                var contract = heir.Survival.WorkerContract;
                RelationshipState relationship = null;
                foreach (var candidate in heir.Survival.Relationships)
                {
                    if (candidate != null
                        && candidate.TargetCharacterId == owner.CharacterId)
                    {
                        relationship = candidate;
                        break;
                    }
                }
                var hasBed = state.PlacedObjects.Exists(entry => entry != null
                    && entry.ItemId == SurvivalStructureRules.BedItemId
                    && entry.OwnerAccountId == player.SteamId
                    && string.Equals(entry.AssignedCharacterId, heirCharacterId,
                        StringComparison.OrdinalIgnoreCase));
                var hasDeed = owner.InventorySlots.Exists(entry => entry != null
                    && entry.ItemId == 49 && entry.Quantity > 0);
                var eligibility = new InheritanceEligibility(
                    true, hasDeed, hasBed, contract?.FulfilledContractGameSeconds ?? 0d,
                    relationship?.PersonalRequestsCompleted ?? 0,
                    contract?.Active == true && contract.EmployerAccountId == player.SteamId,
                    contract?.Voluntary == true && relationship?.VoluntaryLoyalty == true);
                if (!LivingWorldSimulation.CanRegisterHeir(eligibility, out var error))
                    throw new InvalidOperationException(error);
                player.RegisteredHeirCharacterId = heirCharacterId;
                player.EstateRevision++;
                Save();
                return Task.FromResult(ToProfile(player, owner));
            }
        }

        public Task<PlayerProfile> AssumeRegisteredHeirAsync(
            ulong steamId, string operationId, string deceasedCharacterId,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                state.Inheritances ??= new List<StoredInheritance>();
                var player = state.Players.Find(entry => entry.SteamId == steamId.ToString())
                    ?? throw new InvalidOperationException("Player does not exist.");
                var receipt = state.Inheritances.Find(entry => entry.OperationId == operationId);
                if (receipt != null)
                {
                    if (receipt.SteamId != player.SteamId
                        || receipt.DeceasedCharacterId != deceasedCharacterId
                        || player.CurrentCharacterId != receipt.HeirCharacterId)
                        throw new InvalidOperationException("Inheritance operation was reused.");
                    return Task.FromResult(ToProfile(player, GetCurrentCharacter(player)));
                }
                var deceased = GetCurrentCharacter(player);
                if (deceased == null || deceased.CharacterId != deceasedCharacterId
                    || deceased.Survival.Revision != expectedRevision)
                    throw new InvalidOperationException("The deceased character changed.");
                if (deceased.Survival.Physiology.LifeState != CharacterLifeState.Dead
                    && deceased.Survival.ControlKind != CharacterControlKind.ForcedNpc)
                    throw new InvalidOperationException(
                        "Inheritance begins only after death or completed capture.");
                var heir = state.Characters.Find(entry =>
                    entry.CharacterId == player.RegisteredHeirCharacterId)
                    ?? throw new InvalidOperationException("The registered heir no longer exists.");
                if (!string.IsNullOrWhiteSpace(heir.ControllingSteamId)
                    || heir.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    || heir.Survival.ControlKind != CharacterControlKind.ContractedNpc)
                    throw new InvalidOperationException("The registered heir is no longer eligible.");
                deceased.ControllingSteamId = string.Empty;
                heir.ControllingSteamId = player.SteamId;
                heir.Survival.ControlKind = CharacterControlKind.Player;
                heir.Survival.Offline = false;
                heir.Survival.Revision++;
                if (heir.Survival.WorkerContract != null)
                    heir.Survival.WorkerContract.Active = false;
                if (heir.Survival.Npc != null)
                {
                    heir.Survival.Npc.EmployerAccountId = string.Empty;
                    heir.Survival.Npc.EmployerCharacterId = string.Empty;
                    heir.Survival.Npc.Activity = NpcActivityKind.Idle;
                }
                player.CurrentCharacterId = heir.CharacterId;
                player.Position = heir.Position;
                player.SelectedHotbarIndex = heir.SelectedHotbarIndex;
                player.RegisteredHeirCharacterId = string.Empty;
                player.EstateRevision++;
                state.Inheritances.Add(new StoredInheritance
                {
                    OperationId = operationId,
                    SteamId = player.SteamId,
                    DeceasedCharacterId = deceasedCharacterId,
                    HeirCharacterId = heir.CharacterId,
                });
                Save();
                return Task.FromResult(ToProfile(player, heir));
            }
        }

        public Task<PendingHeirOffer> OfferRegisteredHeirAsync(
            ulong donorSteamId, ulong recipientSteamId, string operationId,
            string deceasedCharacterId, long expectedDonorEstateRevision,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                if (donorSteamId == 0 || recipientSteamId == 0
                    || donorSteamId == recipientSteamId
                    || !Guid.TryParse(operationId, out _))
                    throw new InvalidOperationException("Invalid heir offer identity.");
                var prior = state.HeirOffers.Find(entry => entry.OperationId == operationId);
                if (prior != null)
                {
                    if (prior.DonorSteamId != donorSteamId.ToString()
                        || prior.RecipientSteamId != recipientSteamId.ToString()
                        || prior.DeceasedCharacterId != deceasedCharacterId)
                        throw new InvalidOperationException("Heir offer operation was reused.");
                    return Task.FromResult(ToPendingHeirOffer(prior));
                }
                var donor = state.Players.Find(entry => entry.SteamId == donorSteamId.ToString())
                    ?? throw new InvalidOperationException("Donor does not exist.");
                var recipient = state.Players.Find(entry => entry.SteamId == recipientSteamId.ToString())
                    ?? throw new InvalidOperationException("Recipient does not exist.");
                if (donor.EstateRevision != expectedDonorEstateRevision)
                    throw new InvalidOperationException("The donor estate was already changed.");
                if (string.IsNullOrWhiteSpace(donor.RegisteredHeirCharacterId))
                    throw new InvalidOperationException("The donor has no registered heir.");
                var deceased = GetCurrentCharacter(recipient);
                if (deceased == null || deceased.CharacterId != deceasedCharacterId
                    || !IsLifeLost(deceased))
                    throw new InvalidOperationException("The recipient is not waiting after this life.");
                var lostAt = deceased.Survival.Corpse?.DiedAtUtcTicks > 0
                    ? new DateTime(deceased.Survival.Corpse.DiedAtUtcTicks, DateTimeKind.Utc)
                    : ParseDate(deceased.UpdatedAtUtc).ToUniversalTime();
                if (DateTime.UtcNow - lostAt > TimeSpan.FromHours(2))
                    throw new InvalidOperationException("The 24 game-hour donation window has ended.");
                var pendingReservation = state.HeirOffers.Find(entry => entry != null
                    && entry.HeirCharacterId == donor.RegisteredHeirCharacterId
                    && entry.Status == 0
                    && ParseDate(entry.HardExpiresAtUtc).ToUniversalTime() > DateTime.UtcNow);
                if (pendingReservation != null)
                    throw new InvalidOperationException(
                        "The heir is already reserved by another offer.");
                var heir = state.Characters.Find(entry =>
                    entry.CharacterId == donor.RegisteredHeirCharacterId)
                    ?? throw new InvalidOperationException("The registered heir no longer exists.");
                if (!string.IsNullOrWhiteSpace(heir.ControllingSteamId)
                    || heir.Survival.Physiology.LifeState == CharacterLifeState.Dead
                    || heir.Survival.ControlKind != CharacterControlKind.ContractedNpc)
                    throw new InvalidOperationException("The registered heir is no longer eligible.");
                var now = DateTime.UtcNow;
                var offer = new StoredHeirOffer
                {
                    OfferId = Guid.NewGuid().ToString("D"),
                    OperationId = operationId,
                    DonorSteamId = donor.SteamId,
                    RecipientSteamId = recipient.SteamId,
                    DeceasedCharacterId = deceasedCharacterId,
                    HeirCharacterId = heir.CharacterId,
                    OfferedAtUtc = now.ToString("O"),
                    HardExpiresAtUtc = now.AddDays(7).ToString("O"),
                };
                state.HeirOffers.Add(offer);
                donor.RegisteredHeirCharacterId = string.Empty;
                donor.EstateRevision++;
                Save();
                return Task.FromResult(ToPendingHeirOffer(offer));
            }
        }

        public Task<PendingHeirOffer> GetPendingHeirOfferAsync(
            ulong recipientSteamId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(entry => entry.SteamId == recipientSteamId.ToString());
                var deceased = GetCurrentCharacter(player);
                if (deceased == null || !IsLifeLost(deceased))
                    return Task.FromResult<PendingHeirOffer>(null);
                var now = DateTime.UtcNow;
                StoredHeirOffer selected = null;
                foreach (var offer in state.HeirOffers)
                {
                    if (offer == null || offer.RecipientSteamId != recipientSteamId.ToString()
                        || offer.Status != 0) continue;
                    if (offer.DeceasedCharacterId != deceased.CharacterId
                        || now >= EffectiveOfferExpiry(offer))
                    {
                        offer.Status = 2;
                        continue;
                    }
                    if (selected == null || ParseDate(offer.OfferedAtUtc)
                            < ParseDate(selected.OfferedAtUtc)) selected = offer;
                }
                if (selected != null && string.IsNullOrWhiteSpace(selected.AcceptanceStartedAtUtc))
                    selected.AcceptanceStartedAtUtc = now.ToString("O");
                Save();
                return Task.FromResult(selected == null ? null : ToPendingHeirOffer(selected));
            }
        }

        public Task<PlayerProfile> AcceptHeirOfferAsync(
            ulong recipientSteamId, string offerId, string operationId,
            string deceasedCharacterId, long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                state.Inheritances ??= new List<StoredInheritance>();
                var player = state.Players.Find(entry => entry.SteamId == recipientSteamId.ToString())
                    ?? throw new InvalidOperationException("Recipient does not exist.");
                var receipt = state.Inheritances.Find(entry => entry.OperationId == operationId);
                if (receipt != null)
                {
                    if (receipt.SteamId != player.SteamId
                        || receipt.DeceasedCharacterId != deceasedCharacterId
                        || player.CurrentCharacterId != receipt.HeirCharacterId)
                        throw new InvalidOperationException("Inheritance operation was reused.");
                    return Task.FromResult(ToProfile(player, GetCurrentCharacter(player)));
                }
                var offer = state.HeirOffers.Find(entry => entry.OfferId == offerId)
                    ?? throw new InvalidOperationException("Heir offer does not exist.");
                if (offer.RecipientSteamId != player.SteamId
                    || offer.DeceasedCharacterId != deceasedCharacterId || offer.Status != 0
                    || string.IsNullOrWhiteSpace(offer.AcceptanceStartedAtUtc)
                    || DateTime.UtcNow >= EffectiveOfferExpiry(offer))
                    throw new InvalidOperationException("Heir offer is no longer available.");
                var deceased = GetCurrentCharacter(player);
                if (deceased == null || deceased.CharacterId != deceasedCharacterId
                    || deceased.Survival.Revision != expectedRevision || !IsLifeLost(deceased))
                    throw new InvalidOperationException("The lost character changed.");
                var heir = state.Characters.Find(entry => entry.CharacterId == offer.HeirCharacterId)
                    ?? throw new InvalidOperationException("The donated heir no longer exists.");
                if (!string.IsNullOrWhiteSpace(heir.ControllingSteamId)
                    || heir.Survival.Physiology.LifeState == CharacterLifeState.Dead)
                    throw new InvalidOperationException("The donated heir is no longer available.");

                deceased.ControllingSteamId = string.Empty;
                PrepareHeirForControl(heir, player.SteamId);
                player.CurrentCharacterId = heir.CharacterId;
                player.Position = heir.Position;
                player.SelectedHotbarIndex = heir.SelectedHotbarIndex;
                player.RegisteredHeirCharacterId = string.Empty;
                player.EstateRevision++;
                offer.Status = 1;
                offer.AcceptedAtUtc = DateTime.UtcNow.ToString("O");
                foreach (var other in state.HeirOffers)
                {
                    if (other != offer && other.RecipientSteamId == player.SteamId
                        && other.Status == 0) other.Status = 3;
                }
                state.Inheritances.Add(new StoredInheritance
                {
                    OperationId = operationId,
                    SteamId = player.SteamId,
                    DeceasedCharacterId = deceasedCharacterId,
                    HeirCharacterId = heir.CharacterId,
                });
                Save();
                return Task.FromResult(ToProfile(player, heir));
            }
        }

        private void EnsureLoaded()
        {
            if (state != null)
            {
                return;
            }

            if (File.Exists(path))
            {
                try
                {
                    state = JsonUtility.FromJson<State>(File.ReadAllText(path));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not read local persistence state: {exception.Message}");
                }
            }

            state ??= new State();
            state.Players ??= new List<StoredPlayer>();
            state.ResourceNodes ??= new List<StoredResourceNodeState>();
            state.PlacedObjects ??= new List<StoredPlacedObject>();
            state.HeirOffers ??= new List<StoredHeirOffer>();
            state.MapNotes ??= new List<StoredMapNote>();
            state.Characters ??= new List<StoredCharacter>();
            state.Replacements ??= new List<StoredReplacement>();
            state.Transfers ??= new List<StoredTransfer>();
            state.Inheritances ??= new List<StoredInheritance>();
            var defaultWorldId = state.World.WorldId == 0 ? 1 : state.World.WorldId;
            foreach (var node in state.ResourceNodes)
            {
                if (node != null && node.WorldId == 0) node.WorldId = defaultWorldId;
            }
            foreach (var placed in state.PlacedObjects)
            {
                if (placed != null && placed.WorldId == 0) placed.WorldId = defaultWorldId;
            }
            foreach (var note in state.MapNotes)
            {
                if (note != null && note.WorldId == 0) note.WorldId = defaultWorldId;
            }
            foreach (var player in state.Players)
            {
                player.InventorySlots ??= new List<StoredInventorySlot>();
                player.DepositKnowledge ??= new List<StoredDepositKnowledge>();
                player.MapNotes ??= new List<StoredMapNote>();
                player.Survival ??= new CharacterSurvivalState();
                player.Survival.EnsureInitialized();
                if (string.IsNullOrWhiteSpace(player.Survival.CharacterId))
                    player.Survival.CharacterId = Guid.NewGuid().ToString("D");
                if (string.IsNullOrWhiteSpace(player.CurrentCharacterId))
                    player.CurrentCharacterId = player.Survival.CharacterId;
                var character = state.Characters.Find(
                    candidate => candidate.CharacterId == player.CurrentCharacterId);
                if (character == null)
                {
                    character = new StoredCharacter
                    {
                        CharacterId = player.CurrentCharacterId,
                        Name = string.IsNullOrWhiteSpace(player.Survival.CharacterName)
                            ? player.DisplayName : player.Survival.CharacterName,
                        Position = player.Position,
                        CreatedAtUtc = player.CreatedAtUtc,
                        UpdatedAtUtc = player.LastSeenAtUtc,
                        ControllingSteamId = player.SteamId,
                        InventorySlots = CloneSlots(player.InventorySlots),
                        PendingItems = CloneSlots(player.PendingItems),
                        SelectedHotbarIndex = player.SelectedHotbarIndex,
                        DepositKnowledge = CloneKnowledge(player.DepositKnowledge),
                        Survival = CloneSurvival(player.Survival),
                    };
                    state.Characters.Add(character);
                }
                else
                {
                    character.ControllingSteamId = player.SteamId;
                    character.InventorySlots ??= new List<StoredInventorySlot>();
                    character.PendingItems ??= new List<StoredInventorySlot>();
                    character.DepositKnowledge ??= new List<StoredDepositKnowledge>();
                    character.Survival ??= new CharacterSurvivalState();
                    character.Survival.EnsureInitialized();
                }
                if (character.DepositKnowledge.Count == 0 && player.DepositKnowledge.Count > 0)
                    character.DepositKnowledge.AddRange(CloneKnowledge(player.DepositKnowledge));
                player.DepositKnowledge.Clear();
                foreach (var entry in character.DepositKnowledge)
                {
                    if (entry != null && entry.WorldId == 0) entry.WorldId = defaultWorldId;
                }
                foreach (var note in player.MapNotes)
                {
                    if (note != null && note.WorldId == 0) note.WorldId = defaultWorldId;
                }
            }
        }

        private void Save()
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        private PlayerProfile ToProfile(StoredPlayer player, StoredCharacter character)
        {
            if (character == null) throw new InvalidOperationException("Character not found.");
            var carriedMapIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in character.InventorySlots)
            {
                if (slot != null && slot.ItemId == 36
                    && !string.IsNullOrWhiteSpace(slot.ItemInstanceId))
                {
                    carriedMapIds.Add(slot.ItemInstanceId);
                }
            }
            return new PlayerProfile
            {
                SteamId = player != null && ulong.TryParse(player.SteamId, out var parsedSteamId)
                    ? parsedSteamId : 0,
                DisplayName = string.IsNullOrWhiteSpace(character.Name)
                    ? player?.DisplayName : character.Name,
                Position = character.Position,
                CreatedAtUtc = ParseDate(character.CreatedAtUtc),
                LastSeenAtUtc = ParseDate(character.UpdatedAtUtc),
                InventorySlots = CloneSlots(character.InventorySlots),
                PendingItems = CloneSlots(character.PendingItems),
                SelectedHotbarIndex = character.SelectedHotbarIndex,
                DepositKnowledge = CloneKnowledge(character.DepositKnowledge),
                MapNotes = CloneMapNotes(state.MapNotes.FindAll(note => note != null
                    && carriedMapIds.Contains(note.MapItemInstanceId))),
                Survival = CloneSurvival(character.Survival),
                RegisteredHeirCharacterId = player?.RegisteredHeirCharacterId,
                EstateRevision = player?.EstateRevision ?? 0,
            };
        }

        private PendingHeirOffer ToPendingHeirOffer(StoredHeirOffer offer)
        {
            var donor = state.Players.Find(entry => entry.SteamId == offer.DonorSteamId);
            var heir = state.Characters.Find(entry => entry.CharacterId == offer.HeirCharacterId);
            return new PendingHeirOffer
            {
                OfferId = offer.OfferId,
                DonorSteamId = ulong.TryParse(offer.DonorSteamId, out var donorId) ? donorId : 0,
                DonorDisplayName = donor?.DisplayName ?? "Другой игрок",
                RecipientSteamId = ulong.TryParse(offer.RecipientSteamId, out var recipientId)
                    ? recipientId : 0,
                DeceasedCharacterId = offer.DeceasedCharacterId,
                HeirCharacterId = offer.HeirCharacterId,
                HeirName = heir?.Name ?? "Наследник",
                OfferedAtUtc = ParseDate(offer.OfferedAtUtc),
                AcceptanceStartedAtUtc = string.IsNullOrWhiteSpace(offer.AcceptanceStartedAtUtc)
                    ? ParseDate(offer.OfferedAtUtc) : ParseDate(offer.AcceptanceStartedAtUtc),
                ExpiresAtUtc = EffectiveOfferExpiry(offer),
            };
        }

        private static DateTime EffectiveOfferExpiry(StoredHeirOffer offer)
        {
            var hard = ParseDate(offer.HardExpiresAtUtc).ToUniversalTime();
            if (string.IsNullOrWhiteSpace(offer.AcceptanceStartedAtUtc)) return hard;
            var seen = ParseDate(offer.AcceptanceStartedAtUtc).ToUniversalTime().AddHours(2);
            return seen < hard ? seen : hard;
        }

        private static bool IsLifeLost(StoredCharacter character)
            => character?.Survival?.Physiology?.LifeState == CharacterLifeState.Dead
                || character?.Survival?.ControlKind == CharacterControlKind.ForcedNpc;

        private static void PrepareHeirForControl(StoredCharacter heir, string steamId)
        {
            heir.ControllingSteamId = steamId;
            heir.Survival.ControlKind = CharacterControlKind.Player;
            heir.Survival.Offline = false;
            heir.Survival.Revision++;
            if (heir.Survival.WorkerContract != null) heir.Survival.WorkerContract.Active = false;
            if (heir.Survival.Npc != null)
            {
                heir.Survival.Npc.EmployerAccountId = string.Empty;
                heir.Survival.Npc.EmployerCharacterId = string.Empty;
                heir.Survival.Npc.Activity = NpcActivityKind.Idle;
            }
            heir.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        }

        private StoredCharacter GetCurrentCharacter(StoredPlayer player) => player == null
            ? null : state.Characters.Find(entry => entry.CharacterId == player.CurrentCharacterId);

        private static StoredCharacter CreateCharacter(
            StoredPlayer player, Vector3 position, DateTime now)
        {
            var survival = new CharacterSurvivalState
            {
                CharacterId = Guid.NewGuid().ToString("D"),
                CharacterName = string.IsNullOrWhiteSpace(player.DisplayName) ? "Чужак" : player.DisplayName,
            };
            survival.EnsureInitialized();
            return new StoredCharacter
            {
                CharacterId = survival.CharacterId,
                Name = survival.CharacterName,
                Position = position,
                CreatedAtUtc = now.ToString("O"),
                UpdatedAtUtc = now.ToString("O"),
                ControllingSteamId = player.SteamId,
                Survival = survival,
            };
        }

        private static void SyncLegacyPlayer(StoredPlayer player, StoredCharacter character)
        {
            player.Position = character.Position;
            player.InventorySlots = CloneSlots(character.InventorySlots);
            player.PendingItems = CloneSlots(character.PendingItems);
            player.SelectedHotbarIndex = character.SelectedHotbarIndex;
            player.Survival = CloneSurvival(character.Survival);
            player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
        }

        private static void ApplyCharacterSnapshot(
            StoredCharacter character, CharacterPersistenceSnapshot snapshot)
        {
            character.Position = snapshot.Position;
            character.InventorySlots = CloneSlots(snapshot.Slots);
            character.PendingItems = CloneSlots(snapshot.PendingItems);
            character.SelectedHotbarIndex = snapshot.SelectedHotbarIndex;
            character.Survival = CloneSurvival(snapshot.Survival);
            character.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        }

        private static IEnumerable<StoredInventorySlot> EnumerateSlots(
            CharacterPersistenceSnapshot first, CharacterPersistenceSnapshot second)
        {
            if (first.Slots != null) foreach (var slot in first.Slots) yield return slot;
            if (first.PendingItems != null) foreach (var slot in first.PendingItems) yield return slot;
            if (second.Slots != null) foreach (var slot in second.Slots) yield return slot;
            if (second.PendingItems != null) foreach (var slot in second.PendingItems) yield return slot;
        }

        private static void ValidateSnapshot(
            Vector3 position, byte selectedHotbarIndex, CharacterSurvivalState survival)
        {
            if (survival == null || survival.Physiology == null || survival.Revision <= 0
                || string.IsNullOrWhiteSpace(survival.CharacterId) || !IsFinite(position)
                || selectedHotbarIndex >= InventoryLayout.HotbarSlotCount
                || (survival.Physiology.LifeState == CharacterLifeState.Dead)
                    != (survival.Physiology.DeathCause != DeathCause.None))
                throw new ArgumentException("Invalid character snapshot.");
        }

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static CharacterSurvivalState CloneSurvival(CharacterSurvivalState value)
        {
            var clone = value == null
                ? new CharacterSurvivalState()
                : JsonUtility.FromJson<CharacterSurvivalState>(JsonUtility.ToJson(value));
            clone.EnsureInitialized();
            return clone;
        }

        private static List<StoredInventorySlot> CloneSlots(
            IReadOnlyList<StoredInventorySlot> slots)
        {
            var result = new List<StoredInventorySlot>();
            if (slots == null) return result;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                result.Add(new StoredInventorySlot
                {
                    SlotIndex = slot.SlotIndex,
                    ItemId = slot.ItemId,
                    Quantity = slot.Quantity,
                    Condition = slot.Condition,
                    Quality = slot.Quality,
                    HiddenItemId = slot.HiddenItemId,
                    SourceNodeId = slot.SourceNodeId,
                    RevealAtPercent = slot.RevealAtPercent,
                    SampleId = slot.SampleId,
                    ItemInstanceId = slot.ItemInstanceId,
                    Freshness = slot.Freshness,
                    BiologicalContamination = slot.BiologicalContamination,
                    ToxinContamination = slot.ToxinContamination,
                    Wetness = slot.Wetness,
                    Cleanliness = slot.Cleanliness,
                    LiquidMilliliters = slot.LiquidMilliliters,
                    LiquidKind = slot.LiquidKind,
                    Equipped = slot.Equipped,
                });
            }

            return result;
        }

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.UtcNow;
        }

        private static List<StoredResourceNodeState> CloneNodeStates(
            IReadOnlyList<StoredResourceNodeState> states)
        {
            var result = new List<StoredResourceNodeState>();
            if (states == null) return result;
            foreach (var state in states)
            {
                if (state != null) result.Add(CloneNodeState(state));
            }
            return result;
        }

        private static StoredResourceNodeState CloneNodeState(StoredResourceNodeState state) => new()
        {
            WorldId = state.WorldId,
            InstanceId = state.InstanceId,
            RemainingReserves = state.RemainingReserves,
            AvailableAtUtc = state.AvailableAtUtc,
        };

        private static List<StoredPlacedObject> ClonePlacedObjects(
            IReadOnlyList<StoredPlacedObject> objects)
        {
            var result = new List<StoredPlacedObject>();
            if (objects == null) return result;
            foreach (var entry in objects)
            {
                if (entry == null) continue;
                result.Add(new StoredPlacedObject
                {
                    WorldId = entry.WorldId,
                    ObjectId = entry.ObjectId,
                    OwnerAccountId = entry.OwnerAccountId,
                    AssignedCharacterId = entry.AssignedCharacterId,
                    ItemId = entry.ItemId,
                    X = entry.X,
                    Y = entry.Y,
                    Z = entry.Z,
                    Yaw = entry.Yaw,
                    Locked = entry.Locked,
                    Input = CloneSlot(entry.Input),
                    CreatedAtUtc = entry.CreatedAtUtc,
                    UpdatedAtUtc = entry.UpdatedAtUtc,
                });
            }
            return result;
        }

        private static StoredInventorySlot CloneSlot(StoredInventorySlot slot) => slot == null
            ? null
            : new StoredInventorySlot
            {
                SlotIndex = slot.SlotIndex,
                ItemId = slot.ItemId,
                Quantity = slot.Quantity,
                Condition = slot.Condition,
                Quality = slot.Quality,
                HiddenItemId = slot.HiddenItemId,
                SourceNodeId = slot.SourceNodeId,
                RevealAtPercent = slot.RevealAtPercent,
                SampleId = slot.SampleId,
                ItemInstanceId = slot.ItemInstanceId,
                Freshness = slot.Freshness,
                BiologicalContamination = slot.BiologicalContamination,
                ToxinContamination = slot.ToxinContamination,
                Wetness = slot.Wetness,
                Cleanliness = slot.Cleanliness,
                LiquidMilliliters = slot.LiquidMilliliters,
                LiquidKind = slot.LiquidKind,
                Equipped = slot.Equipped,
            };

        private static List<StoredDepositKnowledge> CloneKnowledge(
            IReadOnlyList<StoredDepositKnowledge> knowledge)
        {
            var result = new List<StoredDepositKnowledge>();
            if (knowledge == null) return result;
            foreach (var entry in knowledge)
            {
                if (entry == null) continue;
                result.Add(new StoredDepositKnowledge
                {
                    WorldId = entry.WorldId,
                    InstanceId = entry.InstanceId,
                    StudyBasisPoints = entry.StudyBasisPoints,
                    DiscoveredAtUtc = entry.DiscoveredAtUtc,
                });
            }
            return result;
        }

        private static List<StoredMapNote> CloneMapNotes(IReadOnlyList<StoredMapNote> notes)
        {
            var result = new List<StoredMapNote>();
            if (notes == null) return result;
            foreach (var note in notes)
            {
                if (note == null) continue;
                result.Add(new StoredMapNote
                {
                    WorldId = note.WorldId,
                    MapItemInstanceId = note.MapItemInstanceId,
                    NoteId = note.NoteId,
                    X = note.X,
                    Z = note.Z,
                    Text = note.Text,
                    CreatedAtUtc = note.CreatedAtUtc,
                    UpdatedAtUtc = note.UpdatedAtUtc,
                });
            }
            return result;
        }

        private static string SanitizeName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Steam Player" : value.Trim();
            return value.Length <= 32 ? value : value.Substring(0, 32);
        }
    }
}
