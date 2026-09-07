using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Core;
using Quieter.Inventory;
using Quieter.Persistence;
using Quieter.Player;
using Quieter.UI;
using Quieter.Survival;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Quieter.World
{
    [Serializable]
    public struct DepositKnowledgeNetworkState : INetworkSerializable, IEquatable<DepositKnowledgeNetworkState>
    {
        public ulong InstanceId;
        public ushort StudyBasisPoints;

        public DepositKnowledgeNetworkState(ulong instanceId, int basisPoints)
        {
            InstanceId = instanceId;
            StudyBasisPoints = (ushort)Mathf.Clamp(basisPoints, 0, 10000);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref StudyBasisPoints);
        }

        public bool Equals(DepositKnowledgeNetworkState other) =>
            InstanceId == other.InstanceId && StudyBasisPoints == other.StudyBasisPoints;
    }

    [Serializable]
    public struct MapNoteNetworkState : INetworkSerializable, IEquatable<MapNoteNetworkState>
    {
        public ulong MapItemInstanceId;
        public ulong NoteId;
        public Vector2 Position;
        public FixedString512Bytes Text;

        public MapNoteNetworkState(
            ulong mapItemInstanceId,
            ulong noteId,
            Vector2 position,
            string text)
        {
            MapItemInstanceId = mapItemInstanceId;
            NoteId = noteId;
            Position = position;
            Text = MapNoteRules.NormalizeText(text);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MapItemInstanceId);
            serializer.SerializeValue(ref NoteId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Text);
        }

        public bool Equals(MapNoteNetworkState other) => MapItemInstanceId == other.MapItemInstanceId
            && NoteId == other.NoteId
            && Position == other.Position && Text.Equals(other.Text);
    }

    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerInventory))]
    public sealed class PlayerResourceInteraction : NetworkBehaviour
    {
        private readonly NetworkList<DepositKnowledgeNetworkState> replicatedKnowledge = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<MapNoteNetworkState> replicatedMapNotes = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly Dictionary<ulong, ushort> serverKnowledge = new();
        private readonly Dictionary<ulong, DateTime> discoveredAt = new();
        private readonly Dictionary<ulong, MapNoteNetworkState> serverMapNotes = new();
        private readonly Dictionary<ulong, DateTime> noteCreatedAt = new();
        private readonly Dictionary<ulong, DateTime> noteUpdatedAt = new();
        private readonly Dictionary<ulong, ResourceNodeDescriptor> sampleDescriptorCache = new();
        private readonly Dictionary<ulong, WorldObjectSpawn> depositSpawnCache = new();
        private readonly SemaphoreSlim persistenceGate = new(1, 1);
        private bool depositSpawnCacheBuilt;

        private NetworkPlayer player;
        private PlayerInventory inventory;
        private PlayerSurvival survival;
        private ResourceWorldService resourceWorld;
        private PlacedObjectWorldService placedObjects;
        private IPlayerProfileRepository repository;
        private ResourceNodeView focusedNode;
        private ResearchTableView focusedTable;
        private SurvivalStructureView focusedStructure;
        private ulong localOpenTableId;
        private ulong serverOpenTableId;
        private ulong serverResearchTableId;
        private ulong serverResearchSampleId;
        private uint serverResearchAttemptId;
        private double serverResearchStartedAt;
        private double serverResearchCompletesAt;
        private uint nextLocalResearchAttemptId;
        private uint localResearchAttemptId;
        private bool localResearchPointerHeld;
        private bool localResearchConfirmed;
        private double localResearchStartedAt;
        private double localResearchCompletesAt;
        private string localResearchResult = string.Empty;
        private ResearchResultTone localResearchResultTone;
        private ulong steamId;
        private int worldId;
        private double nextMineAt;
        private double nextWaterGatherAt;
        private ulong serverBoilHearthId;
        private ushort serverBoilItemId;
        private ulong serverBoilItemInstanceId;
        private double serverBoilCompletesAt;
        private ItemStackState serverBoilContents;
        private ulong serverRinseSourceId;
        private ItemStackState serverRinseItem;
        private double serverRinseCompletesAt;
        private float nextKnowledgeSaveAt;
        private bool knowledgeDirty;
        private bool saveRunning;
        private bool revealingSamples;
        private uint knowledgeRevision;
        private uint noteRevision;
        private bool notesDirty;
        private float nextNoteMutationAt;
        private GameObject placementPreview;
        private ushort placementItemId;
        private Vector3 placementPosition;
        private float placementYaw;
        private bool placementValid;
        private bool placementHasSurface;
        private bool placementTintValid;
        private bool placementSuppressed;
        private string lastFeedback = string.Empty;
        private float feedbackExpiresAt;

        public event Action Changed;
        public ResourceNodeView FocusedNode => focusedNode;
        public ResearchTableView FocusedTable => focusedTable;
        public SurvivalStructureView FocusedStructure => focusedStructure;
        public bool HasServerManualWork => serverBoilHearthId != 0 || serverRinseSourceId != 0;
        public ulong CurrentResearchTableId => localOpenTableId;
        public bool IsPlacementMode => placementPreview != null;
        public bool ConsumesPrimaryAction => IsPlacementMode
            || focusedNode != null && focusedNode.Descriptor.IsMineable;
        public bool PlacementHasSurface => placementHasSurface;
        public ItemStackState CurrentResearchTableInput =>
            placedObjects != null && localOpenTableId != 0
                && placedObjects.TryGetState(localOpenTableId, out _, out var input, out _)
                    ? input
                    : default;
        public bool CurrentResearchTableBusy =>
            placedObjects != null && localOpenTableId != 0
                && placedObjects.TryGetState(localOpenTableId, out _, out _, out var busy)
                && busy;
        public bool IsLocalResearchHolding => localResearchAttemptId != 0
            && localResearchPointerHeld;
        public bool IsLocalResearchConfirmed => IsLocalResearchHolding
            && localResearchConfirmed;
        public float CurrentResearchHoldProgress
        {
            get
            {
                if (!IsLocalResearchConfirmed || localResearchCompletesAt <= localResearchStartedAt
                    || NetworkManager == null)
                {
                    return 0f;
                }
                var elapsed = NetworkManager.ServerTime.Time - localResearchStartedAt;
                var duration = localResearchCompletesAt - localResearchStartedAt;
                return Mathf.Clamp01((float)(elapsed / duration));
            }
        }
        public string CurrentResearchResult => localResearchResult;
        public ResearchResultTone CurrentResearchResultTone => localResearchResultTone;
        public string LastFeedback => Time.unscaledTime <= feedbackExpiresAt ? lastFeedback : string.Empty;
        public int KnowledgeCount => replicatedKnowledge.Count;
        public DepositKnowledgeNetworkState GetKnowledge(int index) =>
            index >= 0 && index < replicatedKnowledge.Count
                ? replicatedKnowledge[index]
                : default;
        public bool TryGetDepositKnowledgePresentation(
            ulong instanceId,
            out DepositKnowledgePresentation presentation)
        {
            if (instanceId == 0 || !TryResolveDepositSpawn(instanceId, out var spawn))
            {
                presentation = default;
                return false;
            }
            presentation = new DepositKnowledgePresentation(
                instanceId,
                GetStudyBasisPoints(instanceId),
                spawn.Position,
                spawn.Resource.Category,
                spawn.Resource.ResourceItemId);
            return true;
        }
        public int MapNoteCount => replicatedMapNotes.Count;
        public MapNoteNetworkState GetMapNote(int index) =>
            index >= 0 && index < replicatedMapNotes.Count
                ? replicatedMapNotes[index]
                : default;

        public void SetLocalFeedback(string value, float durationSeconds = 2.5f)
        {
            if (IsOwner) SetFeedback(value, durationSeconds);
        }

        public int CountMapNotes(ulong mapItemInstanceId)
        {
            if (mapItemInstanceId == 0) return 0;
            var count = 0;
            foreach (var note in replicatedMapNotes)
            {
                if (note.MapItemInstanceId == mapItemInstanceId) count++;
            }
            return count;
        }

        public void RequestCreateMapNote(ulong mapItemInstanceId, Vector2 position, string text)
        {
            if (!IsOwner || mapItemInstanceId == 0) return;
            var normalized = MapNoteRules.NormalizeText(text);
            if (normalized.Length == 0) return;
            CreateMapNoteServerRpc(
                mapItemInstanceId, position, new FixedString512Bytes(normalized));
        }

        public void RequestUpdateMapNote(
            ulong mapItemInstanceId,
            ulong noteId,
            Vector2 position,
            string text)
        {
            if (!IsOwner || mapItemInstanceId == 0 || noteId == 0) return;
            var normalized = MapNoteRules.NormalizeText(text);
            if (normalized.Length == 0) return;
            UpdateMapNoteServerRpc(
                mapItemInstanceId, noteId, position, new FixedString512Bytes(normalized));
        }

        public void RequestDeleteMapNote(ulong mapItemInstanceId, ulong noteId)
        {
            if (IsOwner && mapItemInstanceId != 0 && noteId != 0)
            {
                DeleteMapNoteServerRpc(mapItemInstanceId, noteId);
            }
        }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            inventory = GetComponent<PlayerInventory>();
            survival = GetComponent<PlayerSurvival>();
        }

        public override void OnNetworkSpawn()
        {
            resourceWorld = QuieterRuntimeBootstrap.Instance?.Session?.ResourceWorld;
            placedObjects = QuieterRuntimeBootstrap.Instance?.Session?.PlacedObjects;
            if (placedObjects != null) placedObjects.Changed += OnPlacedObjectsChanged;
            replicatedKnowledge.OnListChanged += OnKnowledgeListChanged;
            replicatedMapNotes.OnListChanged += OnMapNoteListChanged;
            if (IsServer) inventory.ServerInventoryChanged += OnServerInventoryChanged;
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            replicatedKnowledge.OnListChanged -= OnKnowledgeListChanged;
            replicatedMapNotes.OnListChanged -= OnMapNoteListChanged;
            if (inventory != null) inventory.ServerInventoryChanged -= OnServerInventoryChanged;
            if (placedObjects != null) placedObjects.Changed -= OnPlacedObjectsChanged;
            if (IsServer && serverResearchTableId != 0)
            {
                placedObjects?.CancelResearch(serverResearchTableId, OwnerClientId);
            }
            if (placementPreview != null) Destroy(placementPreview);
        }

        public void InitializeServer(
            IEnumerable<StoredDepositKnowledge> stored,
            IEnumerable<StoredMapNote> storedNotes,
            ulong playerSteamId,
            int playerWorldId,
            IPlayerProfileRepository profileRepository)
        {
            if (!IsServer) return;
            steamId = playerSteamId;
            worldId = playerWorldId;
            repository = profileRepository;
            serverKnowledge.Clear();
            discoveredAt.Clear();
            replicatedKnowledge.Clear();
            serverMapNotes.Clear();
            noteCreatedAt.Clear();
            noteUpdatedAt.Clear();
            replicatedMapNotes.Clear();
            if (stored != null)
            {
                foreach (var entry in stored)
                {
                    if (entry == null || !ulong.TryParse(entry.InstanceId, out var instanceId)
                        || instanceId == 0
                        || (entry.WorldId != 0 && entry.WorldId != playerWorldId))
                    {
                        continue;
                    }
                    var progress = (ushort)Mathf.Clamp(entry.StudyBasisPoints, 0, 10000);
                    serverKnowledge[instanceId] = progress;
                    discoveredAt[instanceId] = DateTime.TryParse(
                        entry.DiscoveredAtUtc,
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var parsed)
                        ? parsed.ToUniversalTime()
                        : DateTime.UtcNow;
                    replicatedKnowledge.Add(new DepositKnowledgeNetworkState(instanceId, progress));
                }
            }
            if (storedNotes != null)
            {
                foreach (var entry in storedNotes)
                {
                    if (entry == null
                        || !ulong.TryParse(entry.MapItemInstanceId, out var mapItemInstanceId)
                        || mapItemInstanceId == 0
                        || !inventory.ContainsServerItemInstance(36, mapItemInstanceId)
                        || !ulong.TryParse(entry.NoteId, out var noteId)
                        || noteId == 0 || (entry.WorldId != 0 && entry.WorldId != playerWorldId))
                    {
                        continue;
                    }
                    if (serverMapNotes.ContainsKey(noteId)) continue;
                    if (CountServerMapNotes(mapItemInstanceId) >= MapNoteRules.MaximumNotesPerMap)
                    {
                        continue;
                    }
                    var text = MapNoteRules.NormalizeText(entry.Text);
                    if (text.Length == 0) continue;
                    var position = ClampNotePosition(new Vector2(entry.X, entry.Z));
                    var state = new MapNoteNetworkState(
                        mapItemInstanceId, noteId, position, text);
                    serverMapNotes[noteId] = state;
                    noteCreatedAt[noteId] = ParseStoredDate(entry.CreatedAtUtc);
                    noteUpdatedAt[noteId] = ParseStoredDate(entry.UpdatedAtUtc);
                    replicatedMapNotes.Add(state);
                }
            }
            RevealAllKnownSamples();
        }

        public ushort GetStudyBasisPoints(ulong instanceId)
        {
            if (IsServer && serverKnowledge.TryGetValue(instanceId, out var serverValue))
            {
                return serverValue;
            }
            foreach (var entry in replicatedKnowledge)
            {
                if (entry.InstanceId == instanceId) return entry.StudyBasisPoints;
            }
            return 0;
        }

        public string GetSampleDisplayName(ItemStackState sample)
        {
            if (sample.ItemId != ResourceBalance.UnknownSampleItemId || sample.SourceNodeId == 0)
            {
                return inventory?.Catalog?.GetItem(sample.ItemId)?.DisplayName
                    ?? "Неопознанный образец";
            }
            var study = GetStudyBasisPoints(sample.SourceNodeId);
            if (study < sample.RevealAtPercent * 100
                || !TryResolveSampleDescriptor(sample.SourceNodeId, out var descriptor))
            {
                return "Неопознанный образец";
            }
            var revealedItemId = sample.RevealAtPercent >= 100
                ? descriptor.ImpurityItemId
                : descriptor.ResourceItemId;
            if (revealedItemId == 0 || inventory?.Catalog == null
                || !inventory.Catalog.TryGetItem(revealedItemId, out var item))
            {
                return "Опознанный образец";
            }
            return $"Образец: {item.DisplayName.ToLowerInvariant()}";
        }

        private bool TryResolveSampleDescriptor(
            ulong instanceId,
            out ResourceNodeDescriptor descriptor)
        {
            if (sampleDescriptorCache.TryGetValue(instanceId, out descriptor)) return true;
            if (!TryResolveDepositSpawn(instanceId, out var spawn))
            {
                descriptor = default;
                return false;
            }
            descriptor = spawn.Resource;
            sampleDescriptorCache[instanceId] = descriptor;
            return descriptor.IsResearchable;
        }

        private bool TryResolveDepositSpawn(ulong instanceId, out WorldObjectSpawn spawn)
        {
            if (depositSpawnCache.TryGetValue(instanceId, out spawn)) return true;
            if (resourceWorld == null || resourceWorld.Definition.ChunkCountX == 0)
            {
                spawn = default;
                return false;
            }
            if (resourceWorld.TryGetNode(instanceId, out var loaded))
            {
                spawn = new WorldObjectSpawn(
                    instanceId,
                    default,
                    loaded.transform.position,
                    loaded.transform.rotation,
                    loaded.transform.localScale,
                    loaded.Descriptor);
                depositSpawnCache[instanceId] = spawn;
                sampleDescriptorCache[instanceId] = loaded.Descriptor;
                return loaded.Descriptor.IsResearchable;
            }
            EnsureDepositSpawnCache();
            return depositSpawnCache.TryGetValue(instanceId, out spawn);
        }

        private void EnsureDepositSpawnCache()
        {
            if (depositSpawnCacheBuilt || resourceWorld == null) return;
            var worldCatalog = Resources.Load<WorldObjectCatalog>("Quieter/WorldObjectCatalog");
            if (worldCatalog == null) return;
            var definition = resourceWorld.Definition;
            var generator = new DeterministicChunkGenerator(worldCatalog);
            for (var z = 0; z < definition.ChunkCountZ; z++)
            {
                for (var x = 0; x < definition.ChunkCountX; x++)
                {
                    foreach (var spawn in generator.GenerateObjectsForMap(
                                 definition, new ChunkCoord(x, z)))
                    {
                        if (!spawn.Resource.IsResearchable) continue;
                        depositSpawnCache[spawn.InstanceId] = spawn;
                        sampleDescriptorCache[spawn.InstanceId] = spawn.Resource;
                    }
                }
            }
            depositSpawnCacheBuilt = true;
        }

        public List<StoredDepositKnowledge> CreateStoredKnowledgeSnapshot()
        {
            var result = new List<StoredDepositKnowledge>();
            foreach (var pair in serverKnowledge)
            {
                result.Add(new StoredDepositKnowledge
                {
                    WorldId = worldId,
                    InstanceId = pair.Key.ToString(),
                    StudyBasisPoints = pair.Value,
                    DiscoveredAtUtc = (discoveredAt.TryGetValue(pair.Key, out var date)
                        ? date
                        : DateTime.UtcNow).ToUniversalTime().ToString("O"),
                });
            }
            return result;
        }

        public List<StoredMapNote> CreateStoredMapNoteSnapshot()
        {
            var result = new List<StoredMapNote>(serverMapNotes.Count);
            foreach (var pair in serverMapNotes)
            {
                result.Add(new StoredMapNote
                {
                    WorldId = worldId,
                    MapItemInstanceId = pair.Value.MapItemInstanceId.ToString(),
                    NoteId = pair.Key.ToString(),
                    X = pair.Value.Position.x,
                    Z = pair.Value.Position.y,
                    Text = pair.Value.Text.ToString(),
                    CreatedAtUtc = (noteCreatedAt.TryGetValue(pair.Key, out var created)
                        ? created : DateTime.UtcNow).ToUniversalTime().ToString("O"),
                    UpdatedAtUtc = (noteUpdatedAt.TryGetValue(pair.Key, out var updated)
                        ? updated : DateTime.UtcNow).ToUniversalTime().ToString("O"),
                });
            }
            return result;
        }

        public List<MapNoteNetworkState> ServerTakeMapDocument(ulong mapItemInstanceId)
        {
            var result = new List<MapNoteNetworkState>();
            if (!IsServer || mapItemInstanceId == 0) return result;
            var removedIds = new List<ulong>();
            foreach (var pair in serverMapNotes)
            {
                if (pair.Value.MapItemInstanceId != mapItemInstanceId) continue;
                result.Add(pair.Value);
                removedIds.Add(pair.Key);
            }
            foreach (var noteId in removedIds)
            {
                serverMapNotes.Remove(noteId);
                noteCreatedAt.Remove(noteId);
                noteUpdatedAt.Remove(noteId);
                for (var index = replicatedMapNotes.Count - 1; index >= 0; index--)
                {
                    if (replicatedMapNotes[index].NoteId == noteId)
                    {
                        replicatedMapNotes.RemoveAt(index);
                    }
                }
            }
            if (removedIds.Count > 0) MarkNotesDirty();
            return result;
        }

        public void ServerReceiveMapDocument(
            ulong mapItemInstanceId,
            IReadOnlyList<MapNoteNetworkState> notes)
        {
            if (!IsServer || mapItemInstanceId == 0 || notes == null) return;
            var now = DateTime.UtcNow;
            foreach (var source in notes)
            {
                if (CountServerMapNotes(mapItemInstanceId) >= MapNoteRules.MaximumNotesPerMap)
                    break;
                var noteId = source.NoteId;
                if (noteId == 0 || serverMapNotes.ContainsKey(noteId)) noteId = CreateMapNoteId();
                var state = new MapNoteNetworkState(
                    mapItemInstanceId,
                    noteId,
                    ClampNotePosition(source.Position),
                    source.Text.ToString());
                serverMapNotes[noteId] = state;
                noteCreatedAt[noteId] = now;
                noteUpdatedAt[noteId] = now;
                replicatedMapNotes.Add(state);
            }
            MarkNotesDirty();
        }

        public Task FlushKnowledgeAsync(CancellationToken cancellationToken)
        {
            if (!IsServer || repository == null || steamId == 0)
            {
                return Task.CompletedTask;
            }
            return FlushKnowledgeCoreAsync(cancellationToken);
        }

        private async Task FlushKnowledgeCoreAsync(CancellationToken cancellationToken)
        {
            await persistenceGate.WaitAsync(cancellationToken);
            try
            {
                if (!knowledgeDirty && !notesDirty) return;
                saveRunning = true;
                var savedRevision = knowledgeRevision;
                var savedNoteRevision = noteRevision;
                var saveKnowledge = knowledgeDirty;
                var saveNotes = notesDirty;
                var snapshot = CreateStoredKnowledgeSnapshot();
                var noteSnapshot = CreateStoredMapNoteSnapshot();
                if (saveKnowledge)
                {
                    await repository.SaveDepositKnowledgeAsync(
                        steamId, worldId, snapshot, cancellationToken);
                }
                if (saveNotes)
                {
                    await repository.SaveMapNotesAsync(
                        steamId, worldId, noteSnapshot, cancellationToken);
                }
                if (knowledgeRevision == savedRevision) knowledgeDirty = false;
                if (noteRevision == savedNoteRevision) notesDirty = false;
            }
            finally
            {
                saveRunning = false;
                nextKnowledgeSaveAt = Time.unscaledTime + 5f;
                persistenceGate.Release();
            }
        }

        [ServerRpc]
        private void CreateMapNoteServerRpc(
            ulong mapItemInstanceId,
            Vector2 position,
            FixedString512Bytes requestedText)
        {
            if (!CanMutateMapNotes(mapItemInstanceId)) return;
            if (CountServerMapNotes(mapItemInstanceId) >= MapNoteRules.MaximumNotesPerMap)
            {
                SendMapNoteFeedbackClientRpc("Достигнут лимит: 64 заметки.");
                return;
            }
            var text = MapNoteRules.NormalizeText(requestedText.ToString());
            if (text.Length == 0)
            {
                SendMapNoteFeedbackClientRpc("Введите текст заметки.");
                return;
            }
            var noteId = CreateMapNoteId();
            var now = DateTime.UtcNow;
            var state = new MapNoteNetworkState(
                mapItemInstanceId, noteId, ClampNotePosition(position), text);
            serverMapNotes[noteId] = state;
            noteCreatedAt[noteId] = now;
            noteUpdatedAt[noteId] = now;
            replicatedMapNotes.Add(state);
            survival?.ServerRegisterPractice(
                SkillId.Cartography, 8f, 0.2f, 1f, 0f);
            MarkNotesDirty();
        }

        [ServerRpc]
        private void UpdateMapNoteServerRpc(
            ulong mapItemInstanceId,
            ulong noteId,
            Vector2 position,
            FixedString512Bytes requestedText)
        {
            if (!CanMutateMapNotes(mapItemInstanceId)
                || !serverMapNotes.TryGetValue(noteId, out var existing)
                || existing.MapItemInstanceId != mapItemInstanceId)
            {
                return;
            }
            var text = MapNoteRules.NormalizeText(requestedText.ToString());
            if (text.Length == 0)
            {
                SendMapNoteFeedbackClientRpc("Введите текст заметки.");
                return;
            }
            var state = new MapNoteNetworkState(
                mapItemInstanceId, noteId, ClampNotePosition(position), text);
            serverMapNotes[noteId] = state;
            noteUpdatedAt[noteId] = DateTime.UtcNow;
            for (var index = 0; index < replicatedMapNotes.Count; index++)
            {
                if (replicatedMapNotes[index].NoteId != noteId) continue;
                replicatedMapNotes[index] = state;
                break;
            }
            MarkNotesDirty();
        }

        [ServerRpc]
        private void DeleteMapNoteServerRpc(ulong mapItemInstanceId, ulong noteId)
        {
            if (!CanMutateMapNotes(mapItemInstanceId)
                || !serverMapNotes.TryGetValue(noteId, out var existing)
                || existing.MapItemInstanceId != mapItemInstanceId
                || !serverMapNotes.Remove(noteId))
            {
                return;
            }
            noteCreatedAt.Remove(noteId);
            noteUpdatedAt.Remove(noteId);
            for (var index = replicatedMapNotes.Count - 1; index >= 0; index--)
            {
                if (replicatedMapNotes[index].NoteId == noteId)
                {
                    replicatedMapNotes.RemoveAt(index);
                    break;
                }
            }
            MarkNotesDirty();
        }

        private bool CanMutateMapNotes(ulong mapItemInstanceId)
        {
            if (!IsServer || resourceWorld == null) return false;
            if (!inventory.ContainsServerItemInstance(36, mapItemInstanceId)) return false;
            if (Time.unscaledTime < nextNoteMutationAt) return false;
            nextNoteMutationAt = Time.unscaledTime + MapNoteRules.MutationCooldownSeconds;
            return true;
        }

        private int CountServerMapNotes(ulong mapItemInstanceId)
        {
            var count = 0;
            foreach (var note in serverMapNotes.Values)
            {
                if (note.MapItemInstanceId == mapItemInstanceId) count++;
            }
            return count;
        }

        private Vector2 ClampNotePosition(Vector2 position) => resourceWorld == null
            ? Vector2.zero
            : MapNoteRules.ClampToWorld(position, resourceWorld.Definition);

        private ulong CreateMapNoteId()
        {
            ulong noteId;
            do
            {
                noteId = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            } while (noteId == 0 || serverMapNotes.ContainsKey(noteId));
            return noteId;
        }

        private static DateTime ParseStoredDate(string value) => DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.UtcNow;

        private void MarkNotesDirty()
        {
            notesDirty = true;
            noteRevision++;
            if (nextKnowledgeSaveAt <= Time.unscaledTime)
            {
                nextKnowledgeSaveAt = Time.unscaledTime + 5f;
            }
            Changed?.Invoke();
        }

        [ClientRpc]
        private void SendMapNoteFeedbackClientRpc(FixedString512Bytes message)
        {
            if (!IsOwner) return;
            SetFeedback(message.ToString());
            Changed?.Invoke();
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                UpdateLocalResearchHold();
                UpdateOwnerInput();
            }
            if (IsServer)
            {
                UpdateServerResearch();
                UpdateServerBoiling();
                UpdateServerRinsing();
                if ((knowledgeDirty || notesDirty) && !saveRunning
                    && Time.unscaledTime >= nextKnowledgeSaveAt)
                {
                    _ = FlushWithLoggingAsync();
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && IsSpawned && IsOwner && IsLocalResearchHolding)
            {
                RequestCancelResearchHold();
            }
        }

        private void UpdateLocalResearchHold()
        {
            if (!IsLocalResearchHolding) return;
            if (!InventoryView.ResearchTableMode
                || Mouse.current == null
                || !Mouse.current.leftButton.isPressed)
            {
                RequestCancelResearchHold();
            }
        }

        private void UpdateOwnerInput()
        {
            if (SurvivalView.IsCreationOpen || SurvivalView.IsBodyOpen
                || SurvivalView.IsProgressionOpen)
            {
                focusedNode = null;
                focusedTable = null;
                focusedStructure = null;
                DestroyPlacementPreview();
                return;
            }
            if (UpdatePlacement())
            {
                focusedNode = null;
                focusedTable = null;
                focusedStructure = null;
                return;
            }
            UpdateFocusedNode();
            if (inventory.IsInterfaceOpen || ResourceMapView.IsOpen
                || ResourceMapView.IsDepositOpen)
            {
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (focusedNode != null && focusedNode.Descriptor.IsWaterSource
                && keyboard.rKey.wasPressedThisFrame)
            {
                BeginRinsingServerRpc(focusedNode.InstanceId);
                return;
            }

            if (focusedNode != null && focusedNode.Descriptor.IsLoosePickup
                && keyboard.eKey.wasPressedThisFrame)
            {
                CollectLooseServerRpc(focusedNode.InstanceId);
                return;
            }

            if (focusedNode != null && focusedNode.Descriptor.IsWaterSource
                && keyboard.eKey.wasPressedThisFrame)
            {
                GatherWaterServerRpc(focusedNode.InstanceId);
                return;
            }

            if (focusedTable != null && keyboard.eKey.wasPressedThisFrame)
            {
                OpenResearchTableServerRpc(focusedTable.ObjectId);
                return;
            }

            if (focusedStructure != null
                && focusedStructure.ItemId == SurvivalStructureRules.HearthItemId
                && keyboard.eKey.wasPressedThisFrame)
            {
                var active = inventory.GetReplicatedSlot(new InventorySlotReference(
                    InventorySlotArea.Inventory,
                    InventoryLayout.FirstHotbarSlot + inventory.SelectedHotbarIndex));
                if (!active.IsEmpty && inventory.Catalog != null
                    && inventory.Catalog.TryGetItem(active.ItemId, out var item)
                    && item.Kind == ItemKind.LiquidContainer
                    && active.LiquidKind == LiquidKind.Water
                    && active.LiquidMilliliters > 0)
                {
                    BeginBoilingServerRpc(focusedStructure.ObjectId);
                }
                else
                {
                    FuelHearthServerRpc(focusedStructure.ObjectId);
                }
                return;
            }

            if (focusedStructure != null
                && focusedStructure.ItemId == SurvivalStructureRules.UnlinedWastePitItemId
                && keyboard.eKey.wasPressedThisFrame)
            {
                DumpWasteServerRpc(focusedStructure.ObjectId);
                return;
            }

            if (focusedNode != null && focusedNode.Descriptor.IsResearchable
                && keyboard.eKey.wasPressedThisFrame)
            {
                OpenDepositInfoServerRpc(focusedNode.InstanceId);
                return;
            }

            if (focusedNode != null
                && focusedNode.Descriptor.IsMineable
                && Mouse.current?.leftButton.wasPressedThisFrame == true)
            {
                inventory.PlayActiveToolSwing();
                MineServerRpc(focusedNode.InstanceId);
            }
        }

        private void UpdateFocusedNode()
        {
            focusedNode = null;
            focusedTable = null;
            focusedStructure = null;
            var camera = player.OwnerCamera;
            if (!WorldInteractionRaycast.TryGetClosest(
                    camera, transform, ResourceBalance.InteractionDistance, out var hit)) return;
            var node = hit.collider.GetComponentInParent<ResourceNodeView>();
            if (node != null)
            {
                focusedNode = node;
                return;
            }
            focusedTable = hit.collider.GetComponentInParent<ResearchTableView>();
            if (focusedTable == null)
            {
                focusedStructure = hit.collider.GetComponentInParent<SurvivalStructureView>();
            }
        }

        private bool UpdatePlacement()
        {
            var active = inventory.GetReplicatedSlot(new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + inventory.SelectedHotbarIndex));
            if (active.IsEmpty || inventory.Catalog == null
                || !inventory.Catalog.TryGetItem(active.ItemId, out var activeItem)
                || activeItem.Kind != ItemKind.Placeable
                || !SurvivalStructureRules.SupportsPlacement(active.ItemId))
            {
                placementSuppressed = false;
                DestroyPlacementPreview();
                placementItemId = 0;
                return false;
            }
            if (placementItemId != active.ItemId)
            {
                placementSuppressed = false;
                DestroyPlacementPreview();
                placementItemId = active.ItemId;
            }
            if (inventory.IsInterfaceOpen || ResourceMapView.IsOpen
                || ResourceMapView.IsDepositOpen)
            {
                DestroyPlacementPreview();
                return true;
            }
            if (placementSuppressed) return true;
            if (placementPreview == null)
            {
                placementPreview = placementItemId == ResourceBalance.ResearchTableItemId
                    ? ResearchTableView.CreatePreview(transform)
                    : SurvivalStructureView.CreatePreview(placementItemId, transform);
                ResearchTableView.Tint(placementPreview, new Color(0.2f, 0.86f, 0.38f, 0.52f));
                placementTintValid = true;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    placementYaw = Mathf.Repeat(
                        placementYaw + (wheel > 0f ? 15f : -15f), 360f);
                }
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    placementSuppressed = true;
                    DestroyPlacementPreview();
                    return true;
                }
            }
            RefreshPlacementCandidate();
            if (mouse?.leftButton.wasPressedThisFrame == true && placementValid)
            {
                PlaceObjectServerRpc(placementPosition, placementYaw, placementItemId);
            }
            return true;
        }

        public bool TryCancelPlacement()
        {
            if (placementPreview == null && !placementSuppressed) return false;
            placementSuppressed = true;
            DestroyPlacementPreview();
            return true;
        }

        private void RefreshPlacementCandidate()
        {
            var camera = player.OwnerCamera;
            placementValid = false;
            placementHasSurface = false;
            if (camera == null || placementPreview == null) return;
            var hits = Physics.RaycastAll(
                camera.transform.position,
                camera.transform.forward,
                ResourceBalance.PlacementDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)
                    || hit.transform.IsChildOf(placementPreview.transform))
                {
                    continue;
                }
                if (hit.collider.gameObject.GetComponent<WorldChunkView>() == null) break;
                placementPosition = hit.point;
                placementHasSurface = true;
                placementValid = hit.normal.y >= 0.94f
                    && Vector3.Distance(camera.transform.position, placementPosition)
                        <= ResourceBalance.PlacementDistance + 0.25f
                    && IsInsideWorld(placementPosition)
                    && HasPlacementLineOfSight(placementPosition)
                    && !HasPlacementOverlap(placementPosition, placementYaw, placementItemId);
                break;
            }
            placementPreview.SetActive(placementHasSurface);
            if (!placementHasSurface) return;
            placementPreview.transform.SetPositionAndRotation(
                placementPosition, Quaternion.Euler(0f, placementYaw, 0f));
            if (placementTintValid != placementValid)
            {
                ResearchTableView.Tint(
                    placementPreview,
                    placementValid
                        ? new Color(0.2f, 0.86f, 0.38f, 0.52f)
                        : new Color(0.92f, 0.2f, 0.18f, 0.52f));
                placementTintValid = placementValid;
            }
        }

        private bool HasPlacementOverlap(Vector3 position, float yaw, ushort itemId)
        {
            var extents = SurvivalStructureRules.PlacementHalfExtents(itemId);
            var hits = Physics.OverlapBox(
                position + Vector3.up * extents.y,
                extents,
                Quaternion.Euler(0f, yaw, 0f),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == null || hit.transform.IsChildOf(transform)
                    || hit.transform.IsChildOf(placementPreview.transform)
                    || hit.gameObject.GetComponent<WorldChunkView>() != null)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private bool IsInsideWorld(Vector3 position)
        {
            if (placedObjects == null) return false;
            var definition = placedObjects.Definition;
            return position.x >= definition.WorldMinimum.x + 1f
                && position.x <= definition.WorldMaximum.x - 1f
                && position.z >= definition.WorldMinimum.z + 1f
                && position.z <= definition.WorldMaximum.z - 1f;
        }

        private void DestroyPlacementPreview()
        {
            if (placementPreview != null) Destroy(placementPreview);
            placementPreview = null;
            placementValid = false;
            placementHasSurface = false;
        }

        [ServerRpc]
        private void PlaceObjectServerRpc(Vector3 position, float yaw, ushort itemId)
        {
            if (!ValidateObjectPlacementServer(position, yaw, itemId)
                || inventory.ServerActiveStack.ItemId != itemId
                || !SurvivalStructureRules.SupportsPlacement(itemId)
                || !placedObjects.TryPlace(position, yaw, itemId, out var objectId))
            {
                SendPlacementResultClientRpc(false, 0, itemId);
                return;
            }
            if (!inventory.TryConsumeActiveItemServer(itemId))
            {
                placedObjects.TryDismantle(objectId, out _);
                SendPlacementResultClientRpc(false, 0, itemId);
                return;
            }
            survival?.ServerRegisterPractice(
                SkillId.Construction, 8f, 0.22f, 1f, 0.25f);
            SendPlacementResultClientRpc(true, objectId, itemId);
        }

        private bool ValidatePlacementServer(Vector3 position, float yaw) =>
            ValidateObjectPlacementServer(
                position, yaw, ResourceBalance.ResearchTableItemId);

        private bool ValidateObjectPlacementServer(
            Vector3 position,
            float yaw,
            ushort itemId)
        {
            if (survival != null && !survival.CanPerformServerAction()
                || placedObjects == null || !float.IsFinite(position.x)
                || !float.IsFinite(position.y) || !float.IsFinite(position.z)
                || !float.IsFinite(yaw)
                || Vector3.Distance(transform.position + Vector3.up * 1.45f, position)
                    > ResourceBalance.PlacementDistance + 0.35f
                || !IsInsideWorld(position)
                || !SurvivalStructureRules.SupportsPlacement(itemId))
            {
                return false;
            }
            var hits = Physics.RaycastAll(
                position + Vector3.up * 2f,
                Vector3.down,
                4f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            RaycastHit ground = default;
            var foundGround = false;
            foreach (var hit in hits)
            {
                if (hit.collider.gameObject.GetComponent<WorldChunkView>() == null) continue;
                ground = hit;
                foundGround = true;
                break;
            }
            if (!foundGround || ground.normal.y < 0.94f
                || Mathf.Abs(ground.point.y - position.y) > 0.35f
                || !HasPlacementLineOfSight(position))
            {
                return false;
            }
            var extents = SurvivalStructureRules.PlacementHalfExtents(itemId);
            var overlaps = Physics.OverlapBox(
                position + Vector3.up * extents.y,
                extents,
                Quaternion.Euler(0f, yaw, 0f),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (var overlap in overlaps)
            {
                if (overlap == null || overlap.transform.IsChildOf(transform)
                    || overlap.gameObject.GetComponent<WorldChunkView>() != null)
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private bool HasPlacementLineOfSight(Vector3 position)
        {
            var origin = transform.position + Vector3.up * 1.45f;
            var target = position + Vector3.up * 0.42f;
            var direction = target - origin;
            if (direction.sqrMagnitude < 0.01f) return true;
            var hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                direction.magnitude - 0.08f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)
                    || placementPreview != null && hit.transform.IsChildOf(placementPreview.transform))
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        [ClientRpc]
        private void SendPlacementResultClientRpc(
            bool success,
            ulong objectId,
            ushort itemId)
        {
            if (!IsOwner) return;
            var name = inventory.Catalog != null
                && inventory.Catalog.TryGetItem(itemId, out var item)
                    ? item.DisplayName
                    : "постройка";
            if (success)
            {
                placementSuppressed = false;
                DestroyPlacementPreview();
                SetFeedback($"Установлено: {name}.");
            }
            else
            {
                SetFeedback($"Здесь нельзя установить: {name}.");
            }
        }

        [ServerRpc]
        private void FuelHearthServerRpc(ulong objectId)
        {
            if (placedObjects == null || inventory == null || survival == null
                || !survival.CanPerformServerAction()
                || !placedObjects.CanInteract(objectId, transform, 3.5f)
                || !placedObjects.TryGetState(objectId, out var position, out _, out _)
                || Vector3.Distance(transform.position, position)
                    > ResourceBalance.InteractionDistance + 0.5f)
            {
                SendFeedbackClientRpc(
                    HarvestFeedbackCode.TargetUnavailable, 0, 0, ToolKind.None);
                return;
            }
            var active = inventory.ServerActiveStack;
            if (active.IsEmpty || !SurvivalStructureRules.IsFuel(active.ItemId))
            {
                SendHearthFeedbackClientRpc("Для очага нужны ветки или древесный уголь.");
                return;
            }
            if (!placedObjects.CanAcceptFuel(objectId, active.ItemId))
            {
                SendHearthFeedbackClientRpc("Очаг заполнен или в нём лежит другое топливо.");
                return;
            }
            var slot = new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + inventory.GetServerSelectedHotbarIndex());
            if (!inventory.TryRemoveStackServer(slot, active.ItemId, 1, out var removed))
                return;
            if (!placedObjects.TryAddFuel(objectId, active.ItemId))
            {
                var remainder = inventory.InsertStackServer(removed);
                if (remainder > 0) inventory.SpawnOverflowServer(
                    removed.WithQuantity(remainder), transform.position);
                return;
            }
            survival.ServerRegisterPractice(SkillId.Firekeeping, 4f, 0.2f, 1f, 0f);
            SendHearthFeedbackClientRpc("Топливо добавлено. Очаг разгорелся.");
        }

        [ClientRpc]
        private void SendHearthFeedbackClientRpc(string message)
        {
            if (IsOwner) SetFeedback(message);
        }

        [ServerRpc]
        private void BeginBoilingServerRpc(ulong objectId)
        {
            var active = inventory?.ServerActiveStack ?? default;
            if (HasServerManualWork || inventory == null || inventory.IsCrafting
                || placedObjects == null || survival == null
                || !survival.CanPerformServerAction()
                || !placedObjects.CanInteract(objectId, transform, 3.5f)
                || !placedObjects.TryGetBurningHearth(objectId, out var position)
                || Vector3.Distance(transform.position, position)
                    > ResourceBalance.InteractionDistance + 0.5f
                || active.IsEmpty || active.ItemId != 30 || active.ItemInstanceId == 0
                || active.LiquidKind != LiquidKind.Water
                || active.LiquidMilliliters == 0)
            {
                SendHearthFeedbackClientRpc(
                    "Для кипячения держите котелок с водой у горящего очага.");
                return;
            }
            serverBoilHearthId = objectId;
            serverBoilItemId = active.ItemId;
            serverBoilItemInstanceId = active.ItemInstanceId;
            serverBoilContents = active;
            serverBoilCompletesAt = NetworkManager.ServerTime.Time
                + 8d + active.LiquidMilliliters / 250d;
            SendHearthFeedbackClientRpc(
                "Вода нагревается. Оставайтесь у очага и держите сосуд в руках.");
        }

        private void UpdateServerBoiling()
        {
            if (serverBoilHearthId == 0) return;
            var active = inventory?.ServerActiveStack ?? default;
            if (placedObjects == null || survival == null || !survival.CanPerformServerAction()
                || inventory.IsCrafting
                || !placedObjects.CanInteract(serverBoilHearthId, transform, 3.5f)
                || !placedObjects.TryGetBurningHearth(
                    serverBoilHearthId, out var hearthPosition)
                || Vector3.Distance(transform.position, hearthPosition)
                    > ResourceBalance.InteractionDistance + 0.5f
                || active.ItemId != serverBoilItemId
                || active.ItemInstanceId != serverBoilItemInstanceId
                || active.LiquidKind != LiquidKind.Water
                || active.LiquidMilliliters != serverBoilContents.LiquidMilliliters
                || active.BiologicalContamination != serverBoilContents.BiologicalContamination
                || active.ToxinContamination != serverBoilContents.ToxinContamination
                || active.Cleanliness != serverBoilContents.Cleanliness)
            {
                CancelServerBoiling("Кипячение прервано.");
                return;
            }
            if (NetworkManager.ServerTime.Time < serverBoilCompletesAt) return;
            var succeeded = inventory.TryBoilActiveLiquidServer(
                serverBoilItemId, serverBoilItemInstanceId);
            serverBoilHearthId = 0;
            serverBoilItemId = 0;
            serverBoilItemInstanceId = 0;
            serverBoilCompletesAt = 0d;
            if (!succeeded)
            {
                SendHearthFeedbackClientRpc("Кипячение не удалось.");
                return;
            }
            survival?.ServerRegisterPractice(SkillId.WaterSafety, 10f, 0.28f, 1f, 0f);
            survival?.ServerRegisterPractice(SkillId.Cooking, 10f, 0.2f, 1f, 0f);
            SendHearthFeedbackClientRpc(
                "Вода прокипячена. Биологическое заражение уничтожено; токсины могли остаться.");
        }

        private void CancelServerBoiling(string message)
        {
            serverBoilHearthId = 0;
            serverBoilItemId = 0;
            serverBoilItemInstanceId = 0;
            serverBoilCompletesAt = 0d;
            SendHearthFeedbackClientRpc(message);
        }

        [ServerRpc]
        private void OpenDepositInfoServerRpc(ulong instanceId)
        {
            if (resourceWorld == null || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsResearchable || !ValidateInteraction(node))
            {
                return;
            }
            EnsureKnowledge(instanceId);
            OpenDepositInfoClientRpc(instanceId);
        }

        [ClientRpc]
        private void OpenDepositInfoClientRpc(ulong instanceId)
        {
            if (!IsOwner || resourceWorld == null
                || !resourceWorld.TryGetNode(instanceId, out var node))
            {
                return;
            }
            inventory.SetInterfaceOpen(false);
            ResourceMapView.OpenDeposit(node, GetStudyBasisPoints(instanceId));
        }

        [ServerRpc]
        private void OpenResearchTableServerRpc(ulong objectId)
        {
            if (placedObjects == null || !placedObjects.TryGetView(objectId, out var table)
                || !ValidateTableInteraction(table))
            {
                return;
            }
            if (serverResearchTableId != 0)
            {
                CancelServerResearch(ResearchCancelReason.InterfaceClosed, false);
            }
            serverOpenTableId = objectId;
            OpenResearchTableClientRpc(objectId);
        }

        [ClientRpc]
        private void OpenResearchTableClientRpc(ulong objectId)
        {
            if (!IsOwner) return;
            ResourceMapView.TryClose();
            ResetLocalResearchAttempt();
            ClearLocalResearchResult();
            localOpenTableId = objectId;
            inventory.SetResearchTableInterfaceOpen(true);
            Changed?.Invoke();
        }

        public void OnLocalInventoryClosed()
        {
            if (!IsOwner || localOpenTableId == 0) return;
            if (IsLocalResearchHolding)
            {
                RequestCancelResearchHold(false);
            }
            ClearLocalResearchResult();
            localOpenTableId = 0;
            CloseResearchTableServerRpc();
            Changed?.Invoke();
        }

        [ServerRpc]
        private void CloseResearchTableServerRpc()
        {
            if (serverResearchTableId != 0)
            {
                CancelServerResearch(ResearchCancelReason.InterfaceClosed, false);
            }
            serverOpenTableId = 0;
        }

        public void RequestBeginResearchHold()
        {
            if (!IsOwner || localOpenTableId == 0 || IsLocalResearchHolding) return;
            nextLocalResearchAttemptId++;
            if (nextLocalResearchAttemptId == 0) nextLocalResearchAttemptId = 1;
            localResearchAttemptId = nextLocalResearchAttemptId;
            localResearchPointerHeld = true;
            localResearchConfirmed = false;
            localResearchStartedAt = 0;
            localResearchCompletesAt = 0;
            ClearLocalResearchResult();
            BeginResearchHoldServerRpc(localOpenTableId, localResearchAttemptId);
            Changed?.Invoke();
        }

        [ServerRpc]
        private void BeginResearchHoldServerRpc(ulong objectId, uint attemptId)
        {
            if (attemptId == 0 || objectId == 0 || objectId != serverOpenTableId
                || serverResearchTableId != 0
                || placedObjects == null || !placedObjects.TryGetView(objectId, out var table)
                || !ValidateTableInteraction(table)
                || !placedObjects.TryBeginResearch(
                    objectId, OwnerClientId, out var sample, out _))
            {
                SendResearchStartClientRpc(attemptId, false, 0, 0);
                return;
            }
            serverResearchTableId = objectId;
            serverResearchSampleId = sample.SampleId;
            serverResearchAttemptId = attemptId;
            serverResearchStartedAt = NetworkManager.ServerTime.Time;
            serverResearchCompletesAt = serverResearchStartedAt
                + ResourceBalance.ResearchDurationSeconds;
            SendResearchStartClientRpc(
                attemptId, true, serverResearchStartedAt, serverResearchCompletesAt);
        }

        [ClientRpc]
        private void SendResearchStartClientRpc(
            uint attemptId, bool started, double startedAt, double completesAt)
        {
            if (!IsOwner) return;
            if (attemptId != localResearchAttemptId || !localResearchPointerHeld) return;
            if (!started)
            {
                ResetLocalResearchAttempt();
                SetLocalResearchResult(
                    ResearchResultTone.Failure,
                    "Не удалось начать исследование. Проверьте образец и не занят ли стол.");
                Changed?.Invoke();
                return;
            }
            localResearchConfirmed = true;
            localResearchStartedAt = startedAt;
            localResearchCompletesAt = completesAt;
            Changed?.Invoke();
        }

        public void RequestCancelResearchHold(bool showResult = true)
        {
            if (!IsOwner || localResearchAttemptId == 0) return;
            var attemptId = localResearchAttemptId;
            var tableId = localOpenTableId;
            ResetLocalResearchAttempt();
            if (showResult)
            {
                SetLocalResearchResult(
                    ResearchResultTone.Cancelled,
                    "Исследование прервано. Образец сохранён.");
            }
            if (tableId != 0) CancelResearchHoldServerRpc(tableId, attemptId);
            Changed?.Invoke();
        }

        [ServerRpc]
        private void CancelResearchHoldServerRpc(ulong objectId, uint attemptId)
        {
            if (objectId != serverResearchTableId || attemptId != serverResearchAttemptId) return;
            CancelServerResearch(ResearchCancelReason.Released, false);
        }

        public void RequestDismantleResearchTable()
        {
            if (IsOwner && localOpenTableId != 0)
            {
                DismantleResearchTableServerRpc(localOpenTableId);
            }
        }

        [ServerRpc]
        private void DismantleResearchTableServerRpc(ulong objectId)
        {
            if (objectId != serverOpenTableId || placedObjects == null
                || !placedObjects.TryGetView(objectId, out var table)
                || !ValidateTableInteraction(table)
                || !placedObjects.TryDismantle(objectId, out var position))
            {
                SendDismantleResultClientRpc(false);
                return;
            }
            serverOpenTableId = 0;
            GiveStack(new ItemStackState(ResourceBalance.ResearchTableItemId, 1), position);
            SendDismantleResultClientRpc(true);
        }

        [ClientRpc]
        private void SendDismantleResultClientRpc(bool success)
        {
            if (!IsOwner) return;
            if (success)
            {
                localOpenTableId = 0;
                inventory.SetResearchTableInterfaceOpen(false);
                SetFeedback("Исследовательский стол разобран.");
            }
            else
            {
                SetFeedback("Стол можно разобрать только пустым и неактивным.");
            }
        }

        public bool TryMoveResearchTableStackServer(
            InventorySlotReference source,
            InventorySlotReference destination,
            ushort expectedItemId,
            int quantity)
        {
            if (!IsServer || serverOpenTableId == 0 || quantity != 1
                || placedObjects == null
                || !placedObjects.TryGetView(serverOpenTableId, out var table)
                || !ValidateTableInteraction(table))
            {
                return false;
            }
            if (destination.Area == InventorySlotArea.ResearchTable
                && source.Area == InventorySlotArea.Inventory)
            {
                var sample = inventory.GetServerSlot(source);
                if (sample.ItemId != expectedItemId || sample.Quantity < 1
                    || expectedItemId != ResourceBalance.UnknownSampleItemId
                    || sample.HiddenItemId == 0 || sample.SampleId == 0
                    || !placedObjects.TryInsertInput(serverOpenTableId, sample.WithQuantity(1)))
                {
                    return false;
                }
                if (inventory.TryRemoveStackServer(source, expectedItemId, 1, out _)) return true;
                placedObjects.TryTakeInput(serverOpenTableId, out _);
                return false;
            }
            if (source.Area == InventorySlotArea.ResearchTable
                && destination.Area == InventorySlotArea.Inventory
                && placedObjects.TryTakeInput(serverOpenTableId, out var removed))
            {
                if (removed.ItemId == expectedItemId
                    && inventory.TryPlaceStackServer(destination, removed))
                {
                    return true;
                }
                placedObjects.TryInsertInput(serverOpenTableId, removed);
            }
            return false;
        }

        private void CancelServerResearch(ResearchCancelReason reason, bool notifyOwner)
        {
            var attemptId = serverResearchAttemptId;
            if (serverResearchTableId != 0)
            {
                placedObjects?.CancelResearch(serverResearchTableId, OwnerClientId);
            }
            ResetServerResearchState();
            if (notifyOwner && attemptId != 0)
            {
                SendResearchCancelledClientRpc(attemptId, reason);
            }
        }

        private void ResetServerResearchState()
        {
            serverResearchTableId = 0;
            serverResearchSampleId = 0;
            serverResearchAttemptId = 0;
            serverResearchStartedAt = 0;
            serverResearchCompletesAt = 0;
        }

        [ClientRpc]
        private void SendResearchCancelledClientRpc(uint attemptId, ResearchCancelReason reason)
        {
            if (!IsOwner || attemptId != localResearchAttemptId) return;
            ResetLocalResearchAttempt();
            var message = reason == ResearchCancelReason.InteractionLost
                ? "Исследование прервано: подойдите ближе и сохраняйте видимость стола. Образец сохранён."
                : "Исследование прервано. Образец сохранён.";
            SetLocalResearchResult(ResearchResultTone.Cancelled, message);
            Changed?.Invoke();
        }

        private bool ValidateTableInteraction(ResearchTableView table)
        {
            if (survival != null && !survival.CanPerformServerAction()
                || table == null || Vector3.Distance(transform.position, table.transform.position)
                    > ResourceBalance.InteractionDistance + 0.35f)
            {
                return false;
            }
            var origin = transform.position + Vector3.up * 1.45f;
            var target = table.transform.position + Vector3.up * 0.65f;
            var direction = target - origin;
            var hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                direction.magnitude + 0.25f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                return hit.collider.GetComponentInParent<ResearchTableView>() == table;
            }
            return false;
        }

        [ClientRpc]
        private void SendResearchResultClientRpc(
            uint attemptId, bool success, int added, ushort current)
        {
            if (!IsOwner || attemptId != localResearchAttemptId) return;
            ResetLocalResearchAttempt();
            var result = success
                ? $"Исследование успешно. Образец израсходован. Изученность: +{added / 100f:0.#}%, всего {current / 100f:0.#}%."
                : "Исследование не удалось. Образец израсходован. Изученность не изменилась.";
            SetLocalResearchResult(
                success ? ResearchResultTone.Success : ResearchResultTone.Failure,
                result);
            SetFeedback(success ? "УСПЕХ" : "НЕУДАЧА", 5f);
            Changed?.Invoke();
        }

        private void OnPlacedObjectsChanged()
        {
            if (IsOwner && localOpenTableId != 0
                && (placedObjects == null
                    || !placedObjects.TryGetState(localOpenTableId, out _, out _, out _)))
            {
                localOpenTableId = 0;
                inventory.SetResearchTableInterfaceOpen(false);
            }
            Changed?.Invoke();
        }

        private void UpdateServerResearch()
        {
            if (serverResearchTableId == 0 || placedObjects == null) return;
            if (!placedObjects.TryGetView(serverResearchTableId, out var table)
                || !ValidateTableInteraction(table))
            {
                CancelServerResearch(ResearchCancelReason.InteractionLost, true);
                return;
            }
            if (NetworkManager.ServerTime.Time < serverResearchCompletesAt) return;
            var tableId = serverResearchTableId;
            var sampleId = serverResearchSampleId;
            var attemptId = serverResearchAttemptId;
            if (!placedObjects.TryCompleteResearch(
                    tableId, OwnerClientId, sampleId, out var sample))
            {
                CancelServerResearch(ResearchCancelReason.InteractionLost, true);
                return;
            }
            ResetServerResearchState();
            var roll = ResourceBalance.CalculateResearchRoll(
                placedObjects.Definition.Seed, sample.SampleId);
            var success = roll % 100UL < ResourceBalance.ResearchSuccessPercent;
            var added = success
                ? ResourceBalance.ResearchMinimumBasisPoints
                    + (int)((roll >> 9) % (ulong)(ResourceBalance.ResearchMaximumBasisPoints
                        - ResourceBalance.ResearchMinimumBasisPoints + 1))
                : 0;
            if (success) AddStudyProgress(sample.SourceNodeId, added);
            var current = serverKnowledge.TryGetValue(sample.SourceNodeId, out var progress)
                ? progress
                : (ushort)0;
            SendResearchResultClientRpc(attemptId, success, added, current);
        }

        private void ResetLocalResearchAttempt()
        {
            localResearchAttemptId = 0;
            localResearchPointerHeld = false;
            localResearchConfirmed = false;
            localResearchStartedAt = 0;
            localResearchCompletesAt = 0;
        }

        private void ClearLocalResearchResult()
        {
            localResearchResult = string.Empty;
            localResearchResultTone = ResearchResultTone.None;
        }

        private void SetLocalResearchResult(ResearchResultTone tone, string message)
        {
            localResearchResultTone = tone;
            localResearchResult = message ?? string.Empty;
        }

        [ServerRpc]
        private void CollectLooseServerRpc(ulong instanceId)
        {
            if (resourceWorld == null || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsLoosePickup)
            {
                SendLoosePickupResultClientRpc(PickupResultCode.Unavailable, 0, 0);
                return;
            }
            var validation = ValidateLoosePickup(node);
            if (validation != PickupResultCode.Collected)
            {
                SendLoosePickupResultClientRpc(validation, node.Descriptor.ResourceItemId, 0);
                return;
            }
            if (!resourceWorld.TryCollectLoose(node, out _))
            {
                SendLoosePickupResultClientRpc(
                    PickupResultCode.Unavailable, node.Descriptor.ResourceItemId, 0);
                return;
            }
            var quantity = node.Descriptor.Kind == WorldObjectKind.FiberPlant ? 2 : 1;
            var collectedStack = ResourceBalance.IsWildFood(node.Descriptor.ResourceItemId)
                ? ResourceBalance.CreateWildFoodStack(
                    node.Descriptor.ResourceItemId, node.InstanceId)
                : new ItemStackState(node.Descriptor.ResourceItemId, quantity);
            var overflow = GiveStack(collectedStack, node.transform.position);
            SendLoosePickupResultClientRpc(
                overflow > 0 ? PickupResultCode.CollectedWithOverflow : PickupResultCode.Collected,
                node.Descriptor.ResourceItemId,
                quantity);
            survival?.ServerRegisterPractice(
                SkillId.Foraging,
                2f,
                node.Descriptor.Kind == WorldObjectKind.FiberPlant ? 0.12f : 0.2f,
                1f,
                0f);
        }

        [ServerRpc]
        private void GatherWaterServerRpc(ulong instanceId)
        {
            if (NetworkManager.ServerTime.Time < nextWaterGatherAt) return;
            if (resourceWorld == null || inventory == null || survival == null
                || !survival.CanPerformServerAction()
                || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsWaterSource
                || !ValidateInteraction(node))
            {
                SendWaterResultClientRpc(false, 0, "Источник недоступен.");
                return;
            }
            ResourceBalance.SampleSpringWater(
                instanceId, out var biological, out var toxins);
            var weather = FindAnyObjectByType<WorldWeatherService>();
            var rain = weather != null
                ? weather.GetEnvironment(node.transform.position).Precipitation
                : 0f;
            placedObjects?.ApplyWasteContamination(
                node.transform.position, rain, ref biological, ref toxins);
            if (!inventory.TryFillActiveContainerServer(
                    1000, biological, toxins, out var filled))
            {
                SendWaterResultClientRpc(
                    false, 0, "Возьмите в руку неполный сосуд без другой жидкости.");
                return;
            }
            nextWaterGatherAt = NetworkManager.ServerTime.Time + 2.5;
            survival.ServerRegisterPractice(SkillId.WaterSafety, 3f, 0.18f, 1f, 0f);
            SendWaterResultClientRpc(
                true, filled, "На вид качество воды не определить. Кипячение снижает риск инфекции, но не удаляет токсины.");
        }

        [ServerRpc]
        private void BeginRinsingServerRpc(ulong sourceId)
        {
            if (serverRinseSourceId != 0 || serverBoilHearthId != 0 || inventory == null
                || inventory.IsCrafting || survival == null || !survival.CanPerformServerAction()
                || resourceWorld == null || !resourceWorld.TryGetNode(sourceId, out var node)
                || !node.Descriptor.IsWaterSource || !ValidateInteraction(node)) return;
            var active = inventory.ServerActiveStack;
            if (inventory.Catalog == null || !inventory.Catalog.TryGetItem(active.ItemId, out var item)
                || !ItemHygieneRules.CanRinse(active, item))
            {
                SendHearthFeedbackClientRpc("Для мытья возьмите пустой сосуд, инструмент или снятую одежду.");
                return;
            }
            serverRinseSourceId = sourceId;
            serverRinseItem = active;
            serverRinseCompletesAt = NetworkManager.ServerTime.Time + 8d;
            SendHearthFeedbackClientRpc("Моете предмет. Сохраняйте доступ к источнику 8 секунд.");
        }

        private void UpdateServerRinsing()
        {
            if (serverRinseSourceId == 0) return;
            if (inventory == null || inventory.IsCrafting
                || survival == null || !survival.CanPerformServerAction()
                || resourceWorld == null || !resourceWorld.TryGetNode(serverRinseSourceId, out var node)
                || !ValidateInteraction(node) || !inventory.ServerActiveStack.Equals(serverRinseItem))
            {
                serverRinseSourceId = 0;
                SendHearthFeedbackClientRpc("Мытьё прервано.");
                return;
            }
            if (NetworkManager.ServerTime.Time < serverRinseCompletesAt) return;
            ResourceBalance.SampleSpringWater(serverRinseSourceId, out var biological, out var toxins);
            var weather = FindAnyObjectByType<WorldWeatherService>();
            var rain = weather != null ? weather.GetEnvironment(node.transform.position).Precipitation : 0f;
            placedObjects?.ApplyWasteContamination(node.transform.position, rain, ref biological, ref toxins);
            serverRinseSourceId = 0;
            if (!inventory.TryRinseActiveItemServer(serverRinseItem, biological, toxins)) return;
            survival.ServerRegisterPractice(SkillId.Sanitation, 8f, 0.22f, 1f, 0f);
            SendHearthFeedbackClientRpc("Видимая грязь смыта. Мытьё сырой водой не обеззараживает предмет.");
        }

        [ClientRpc]
        private void SendWaterResultClientRpc(bool success, ushort milliliters, string message)
        {
            if (!IsOwner) return;
            SetFeedback(success ? $"Набрано {milliliters} мл. {message}" : message, 4f);
        }

        [ServerRpc]
        private void DumpWasteServerRpc(ulong objectId)
        {
            if (placedObjects == null || inventory == null || survival == null
                || !survival.CanPerformServerAction()
                || !placedObjects.CanInteract(objectId, transform, 3.5f)
                || !placedObjects.TryGetWastePitSpace(
                    objectId, out var position, out var available)
                || Vector3.Distance(transform.position, position)
                    > ResourceBalance.InteractionDistance + 0.5f)
            {
                SendHearthFeedbackClientRpc("Яма недоступна или переполнена.");
                return;
            }
            var active = inventory.ServerActiveStack;
            if (active.IsEmpty || active.LiquidKind != LiquidKind.Waste
                || active.LiquidMilliliters == 0)
            {
                SendHearthFeedbackClientRpc("Держите в руках сосуд с отходами.");
                return;
            }
            var amount = (ushort)Mathf.Min(active.LiquidMilliliters, available);
            if (!inventory.TryDepositActiveWasteServer(
                    placedObjects, objectId, amount, out var drained))
            {
                SendHearthFeedbackClientRpc("Не удалось опорожнить сосуд.");
                return;
            }
            survival.ServerRegisterPractice(SkillId.Sanitation, 5f, 0.24f, 1f, 0f);
            SendHearthFeedbackClientRpc(
                $"В яму слито {drained} мл. Сосуд остаётся грязным.");
        }

        [ClientRpc]
        private void SendLoosePickupResultClientRpc(
            PickupResultCode code, ushort itemId, int quantity)
        {
            if (!IsOwner) return;
            if ((code == PickupResultCode.Collected
                    || code == PickupResultCode.CollectedWithOverflow)
                && itemId != 0 && inventory.Catalog != null
                && inventory.Catalog.TryGetItem(itemId, out var item))
            {
                var overflow = code == PickupResultCode.CollectedWithOverflow
                    ? " Лишнее выпало рядом."
                    : string.Empty;
                SetFeedback($"Подобрано: {item.DisplayName} × {quantity}.{overflow}");
            }
            else if (code == PickupResultCode.Blocked)
            {
                SetFeedback("Предмет перекрыт препятствием.");
            }
            else
            {
                SetFeedback("Предмет уже недоступен.");
            }
        }

        [ServerRpc]
        private void MineServerRpc(ulong instanceId)
        {
            if (NetworkManager.ServerTime.Time < nextMineAt) return;
            if (resourceWorld == null || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsMineable || !ValidateInteraction(node))
            {
                SendFeedbackClientRpc(
                    HarvestFeedbackCode.TargetUnavailable, 0, 0, ToolKind.None);
                return;
            }
            var active = inventory.ServerActiveStack;
            if (active.IsEmpty || inventory.Catalog == null
                || !inventory.Catalog.TryGetItem(active.ItemId, out var tool)
                || tool.Tool != node.Descriptor.RequiredTool || active.Condition == 0)
            {
                SendFeedbackClientRpc(
                    HarvestFeedbackCode.WrongTool,
                    0,
                    0,
                    node.Descriptor.RequiredTool);
                return;
            }
            if (!resourceWorld.ApplyMiningHit(
                    node, out var completed, out var extractionIndex, out _))
            {
                SendFeedbackClientRpc(
                    HarvestFeedbackCode.TargetUnavailable, 0, 0, ToolKind.None);
                return;
            }
            nextMineAt = NetworkManager.ServerTime.Time + ResourceBalance.MiningCooldownSeconds;
            var durabilityCost = ResourceBalance.DurabilityCost[node.Descriptor.Hardness - 1];
            if (!inventory.TryDamageActiveToolServer(
                    node.Descriptor.RequiredTool, durabilityCost, out var broke))
            {
                return;
            }
            if (node.Descriptor.IsResearchable) EnsureKnowledge(instanceId);
            var workSkill = node.Descriptor.IsTree
                ? SkillId.Woodcutting
                : node.Descriptor.RequiredTool == ToolKind.Shovel
                    ? SkillId.Excavation
                    : SkillId.Mining;
            var challenge = Mathf.Clamp01(node.Descriptor.Hardness / 5f);
            survival?.ServerRegisterPractice(
                workSkill,
                ResourceBalance.MiningCooldownSeconds,
                challenge,
                completed ? 1f : 0.65f,
                0f);
            survival?.ServerRegisterPhysicalLoad(
                CharacterAttributeId.MuscularEndurance,
                ResourceBalance.MiningCooldownSeconds,
                challenge);
            survival?.ServerRegisterPhysicalLoad(
                CharacterAttributeId.Strength,
                ResourceBalance.MiningCooldownSeconds,
                challenge * 0.75f);
            ushort awardedItem = 0;
            var awardedQuantity = 0;
            if (completed)
            {
                if (node.Descriptor.IsTree)
                {
                    awardedItem = 2;
                    awardedQuantity = ResourceBalance.TreeMinimumYield
                        + (int)(resourceWorld.CalculateExtractionRoll(
                            node.InstanceId, extractionIndex, 7)
                            % (ulong)(ResourceBalance.TreeMaximumYield
                                - ResourceBalance.TreeMinimumYield + 1));
                    GiveStack(
                        new ItemStackState(awardedItem, awardedQuantity),
                        node.transform.position);
                }
                else
                {
                    (awardedItem, awardedQuantity) = AwardExtraction(node, extractionIndex);
                }
            }
            SendFeedbackClientRpc(
                broke
                    ? HarvestFeedbackCode.ToolBroken
                    : awardedItem != 0
                        ? HarvestFeedbackCode.YieldReceived
                        : HarvestFeedbackCode.Accepted,
                awardedItem,
                awardedQuantity,
                node.Descriptor.RequiredTool);
        }

        private (ushort ItemId, int Quantity) AwardExtraction(
            ResourceNodeView node,
            int extractionIndex)
        {
            var descriptor = node.Descriptor;
            var study = GetStudyBasisPoints(node.InstanceId) / 100;
            var useful = descriptor.Kind == WorldObjectKind.StoneOutcrop
                || resourceWorld.CalculateExtractionRoll(node.InstanceId, extractionIndex, 1) % 100UL
                    < ResourceBalance.UsefulChancePercent[(int)descriptor.Richness];
            var realItem = useful ? descriptor.ResourceItemId : (ushort)1;
            var stack = CreateYieldStack(
                node.InstanceId,
                extractionIndex,
                11,
                realItem,
                useful ? descriptor.Quality : ResourceQuality.None,
                useful && descriptor.IsResearchable ? 50 : 0,
                study);
            GiveStack(stack, node.transform.position);

            if (descriptor.ImpurityItemId != 0
                && resourceWorld.CalculateExtractionRoll(node.InstanceId, extractionIndex, 2) % 100UL
                    < descriptor.ImpurityChancePercent)
            {
                var impurity = CreateYieldStack(
                    node.InstanceId,
                    extractionIndex,
                    29,
                    descriptor.ImpurityItemId,
                    descriptor.Quality,
                    100,
                    study);
                GiveStack(impurity, node.transform.position + Vector3.right * 0.2f);
            }
            return (stack.ItemId, stack.Quantity);
        }

        private static ItemStackState CreateYieldStack(
            ulong sourceNodeId,
            int extractionIndex,
            int sampleSalt,
            ushort realItemId,
            ResourceQuality quality,
            int revealAtPercent,
            int currentStudyPercent)
        {
            if (revealAtPercent > 0 && currentStudyPercent < 100)
            {
                var sampleId = CreateSampleId(sourceNodeId, extractionIndex, sampleSalt);
                return new ItemStackState(
                    ResourceBalance.UnknownSampleItemId,
                    1,
                    0,
                    quality,
                    realItemId,
                    sourceNodeId,
                    (byte)revealAtPercent,
                    sampleId);
            }
            return new ItemStackState(realItemId, 1, 0, quality);
        }

        private static ulong CreateSampleId(ulong sourceNodeId, int extractionIndex, int salt)
        {
            unchecked
            {
                var value = sourceNodeId ^ ((ulong)(uint)extractionIndex << 17);
                value ^= (uint)salt * 0x9E3779B9UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value == 0 ? 1UL : value;
            }
        }

        private int GiveStack(ItemStackState stack, Vector3 nearPosition)
        {
            var remainder = inventory.InsertStackServer(stack);
            if (remainder > 0)
            {
                inventory.SpawnOverflowServer(stack.WithQuantity(remainder), nearPosition);
            }
            return remainder;
        }

        private PickupResultCode ValidateLoosePickup(ResourceNodeView node)
        {
            if (node == null || !node.IsAvailable) return PickupResultCode.Unavailable;
            var origin = transform.position + Vector3.up * 1.45f;
            var colliders = node.GetComponentsInChildren<Collider>(true);
            var inRange = false;
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                var rangePoint = collider.ClosestPoint(transform.position);
                if (Vector3.Distance(transform.position, rangePoint)
                    <= ResourceBalance.InteractionDistance + 0.35f)
                {
                    inRange = true;
                }
                var target = collider.ClosestPoint(origin);
                if ((target - origin).sqrMagnitude < 0.0001f) target = collider.bounds.center;
                var direction = target - origin;
                if (direction.sqrMagnitude < 0.0001f) return PickupResultCode.Collected;
                var hits = Physics.RaycastAll(
                    origin,
                    direction.normalized,
                    direction.magnitude + 0.25f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide);
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (var hit in hits)
                {
                    if (hit.transform.IsChildOf(transform)) continue;
                    if (hit.collider.GetComponentInParent<ResourceNodeView>() == node
                        && inRange)
                    {
                        return PickupResultCode.Collected;
                    }
                    break;
                }
            }
            return inRange ? PickupResultCode.Blocked : PickupResultCode.Unavailable;
        }

        private bool ValidateInteraction(ResourceNodeView node)
        {
            if (survival != null && !survival.CanPerformServerAction()
                || node == null || !node.IsAvailable
                || Vector3.Distance(transform.position, node.transform.position)
                    > ResourceBalance.InteractionDistance + 0.35f)
            {
                return false;
            }
            var planarDirection = Vector3.ProjectOnPlane(
                node.transform.position - transform.position, Vector3.up).normalized;
            var planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (planarDirection.sqrMagnitude > 0.01f
                && Vector3.Dot(planarDirection, planarForward) < 0.25f)
            {
                return false;
            }
            var origin = transform.position + Vector3.up * 1.45f;
            var direction = node.transform.position - origin;
            var hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                direction.magnitude + 0.35f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                return hit.collider.GetComponentInParent<ResourceNodeView>() == node;
            }
            return false;
        }

        private void EnsureKnowledge(ulong instanceId)
        {
            if (serverKnowledge.ContainsKey(instanceId)) return;
            serverKnowledge[instanceId] = 0;
            discoveredAt[instanceId] = DateTime.UtcNow;
            replicatedKnowledge.Add(new DepositKnowledgeNetworkState(instanceId, 0));
            MarkKnowledgeDirty();
        }

        private void AddStudyProgress(ulong instanceId, int amount)
        {
            EnsureKnowledge(instanceId);
            var previous = serverKnowledge[instanceId];
            var current = (ushort)Mathf.Clamp(previous + amount, 0, 10000);
            if (current == previous) return;
            serverKnowledge[instanceId] = current;
            for (var index = 0; index < replicatedKnowledge.Count; index++)
            {
                if (replicatedKnowledge[index].InstanceId != instanceId) continue;
                replicatedKnowledge[index] = new DepositKnowledgeNetworkState(instanceId, current);
                break;
            }
            var previousPercent = previous / 100;
            var currentPercent = current / 100;
            if (previousPercent < 100 && currentPercent >= 100)
            {
                inventory.RevealSamplesServer(instanceId, currentPercent);
            }
            MarkKnowledgeDirty();
        }

        private void MarkKnowledgeDirty()
        {
            knowledgeDirty = true;
            knowledgeRevision++;
            if (nextKnowledgeSaveAt <= Time.unscaledTime)
            {
                nextKnowledgeSaveAt = Time.unscaledTime + 5f;
            }
        }

        private void OnServerInventoryChanged()
        {
            if (!revealingSamples) RevealAllKnownSamples();
        }

        private void RevealAllKnownSamples()
        {
            if (!IsServer || inventory == null) return;
            revealingSamples = true;
            try
            {
                foreach (var pair in serverKnowledge)
                {
                    if (pair.Value >= 10000)
                    {
                        inventory.RevealSamplesServer(pair.Key, pair.Value / 100);
                    }
                }
            }
            finally
            {
                revealingSamples = false;
            }
        }

        private async Task FlushWithLoggingAsync()
        {
            try
            {
                await FlushKnowledgeAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save deposit knowledge: {exception.Message}");
            }
        }

        [ClientRpc]
        private void SendFeedbackClientRpc(
            HarvestFeedbackCode code,
            ushort itemId,
            int quantity,
            ToolKind requiredTool)
        {
            if (!IsOwner) return;
            if (code == HarvestFeedbackCode.WrongTool)
            {
                SetFeedback($"Нужна {ResourceBalance.ToolName(requiredTool)} в активной ячейке.");
            }
            else if (code == HarvestFeedbackCode.ToolBroken)
            {
                var yieldText = itemId != 0 && inventory.Catalog != null
                    && inventory.Catalog.TryGetItem(itemId, out var yielded)
                        ? $" Получено: {yielded.DisplayName} × {quantity}."
                        : string.Empty;
                var verb = requiredTool == ToolKind.Axe ? "сломался" : "сломалась";
                SetFeedback($"{Capitalize(ResourceBalance.ToolName(requiredTool))} {verb}.{yieldText}");
            }
            else if (code == HarvestFeedbackCode.YieldReceived
                && itemId != 0 && inventory.Catalog != null
                && inventory.Catalog.TryGetItem(itemId, out var item))
            {
                SetFeedback($"Получено: {item.DisplayName} × {quantity}");
            }
            else if (code == HarvestFeedbackCode.Accepted)
            {
                SetFeedback(string.Empty);
            }
            else if (code == HarvestFeedbackCode.TargetUnavailable)
            {
                SetFeedback("Цель недоступна.");
            }
        }

        private static string Capitalize(string value) => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value.Substring(1);

        private void SetFeedback(string value, float durationSeconds = 2.5f)
        {
            lastFeedback = value;
            feedbackExpiresAt = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
            Changed?.Invoke();
        }

        private void OnKnowledgeListChanged(
            NetworkListEvent<DepositKnowledgeNetworkState> change) => Changed?.Invoke();

        private void OnMapNoteListChanged(
            NetworkListEvent<MapNoteNetworkState> change) => Changed?.Invoke();
    }

    internal static class WorldInteractionRaycast
    {
        public static bool TryGetClosest(
            Camera camera,
            Transform playerRoot,
            float distance,
            out RaycastHit closest)
        {
            closest = default;
            if (camera == null || playerRoot == null || distance <= 0f) return false;
            var hits = Physics.RaycastAll(
                camera.transform.position,
                camera.transform.forward,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(playerRoot)) continue;
                closest = hit;
                return true;
            }
            return false;
        }
    }
}
