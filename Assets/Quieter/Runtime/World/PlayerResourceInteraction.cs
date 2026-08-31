using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Core;
using Quieter.Inventory;
using Quieter.Persistence;
using Quieter.Player;
using Quieter.UI;
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
        public ulong NoteId;
        public Vector2 Position;
        public FixedString512Bytes Text;

        public MapNoteNetworkState(ulong noteId, Vector2 position, string text)
        {
            NoteId = noteId;
            Position = position;
            Text = MapNoteRules.NormalizeText(text);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NoteId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Text);
        }

        public bool Equals(MapNoteNetworkState other) => NoteId == other.NoteId
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

        private NetworkPlayer player;
        private PlayerInventory inventory;
        private ResourceWorldService resourceWorld;
        private IPlayerProfileRepository repository;
        private ResourceNodeView focusedNode;
        private ulong localResearchTarget;
        private ulong serverResearchTarget;
        private ulong steamId;
        private int worldId;
        private double nextMineAt;
        private float researchAccumulator;
        private float nextKnowledgeSaveAt;
        private bool knowledgeDirty;
        private bool saveRunning;
        private bool revealingSamples;
        private uint knowledgeRevision;
        private uint noteRevision;
        private bool notesDirty;
        private float nextNoteMutationAt;
        private Vector3 lastServerResearchPosition;
        private string lastFeedback = string.Empty;
        private float feedbackExpiresAt;

        public event Action Changed;
        public ResourceNodeView FocusedNode => focusedNode;
        public string LastFeedback => Time.unscaledTime <= feedbackExpiresAt ? lastFeedback : string.Empty;
        public int KnowledgeCount => replicatedKnowledge.Count;
        public DepositKnowledgeNetworkState GetKnowledge(int index) =>
            index >= 0 && index < replicatedKnowledge.Count
                ? replicatedKnowledge[index]
                : default;
        public int MapNoteCount => replicatedMapNotes.Count;
        public MapNoteNetworkState GetMapNote(int index) =>
            index >= 0 && index < replicatedMapNotes.Count
                ? replicatedMapNotes[index]
                : default;

        public void RequestCreateMapNote(Vector2 position, string text)
        {
            if (!IsOwner) return;
            var normalized = MapNoteRules.NormalizeText(text);
            if (normalized.Length == 0) return;
            CreateMapNoteServerRpc(position, new FixedString512Bytes(normalized));
        }

        public void RequestUpdateMapNote(ulong noteId, Vector2 position, string text)
        {
            if (!IsOwner || noteId == 0) return;
            var normalized = MapNoteRules.NormalizeText(text);
            if (normalized.Length == 0) return;
            UpdateMapNoteServerRpc(noteId, position, new FixedString512Bytes(normalized));
        }

        public void RequestDeleteMapNote(ulong noteId)
        {
            if (IsOwner && noteId != 0) DeleteMapNoteServerRpc(noteId);
        }

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            inventory = GetComponent<PlayerInventory>();
        }

        public override void OnNetworkSpawn()
        {
            resourceWorld = QuieterRuntimeBootstrap.Instance?.Session?.ResourceWorld;
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
            if (IsOwner && localResearchTarget != 0)
            {
                localResearchTarget = 0;
            }
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
                    if (serverMapNotes.Count >= MapNoteRules.MaximumNotesPerWorld) break;
                    if (entry == null || !ulong.TryParse(entry.NoteId, out var noteId)
                        || noteId == 0 || (entry.WorldId != 0 && entry.WorldId != playerWorldId))
                    {
                        continue;
                    }
                    if (serverMapNotes.ContainsKey(noteId)) continue;
                    var text = MapNoteRules.NormalizeText(entry.Text);
                    if (text.Length == 0) continue;
                    var position = ClampNotePosition(new Vector2(entry.X, entry.Z));
                    var state = new MapNoteNetworkState(noteId, position, text);
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

        public async Task FlushKnowledgeAsync(CancellationToken cancellationToken)
        {
            if (!IsServer || repository == null || steamId == 0 || saveRunning
                || (!knowledgeDirty && !notesDirty))
            {
                return;
            }
            saveRunning = true;
            var savedRevision = knowledgeRevision;
            var savedNoteRevision = noteRevision;
            var snapshot = CreateStoredKnowledgeSnapshot();
            var noteSnapshot = CreateStoredMapNoteSnapshot();
            try
            {
                if (knowledgeDirty)
                {
                    await repository.SaveDepositKnowledgeAsync(
                        steamId, worldId, snapshot, cancellationToken);
                }
                if (notesDirty)
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
            }
        }

        [ServerRpc]
        private void CreateMapNoteServerRpc(Vector2 position, FixedString512Bytes requestedText)
        {
            if (!CanMutateMapNotes()) return;
            if (serverMapNotes.Count >= MapNoteRules.MaximumNotesPerWorld)
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
            var state = new MapNoteNetworkState(noteId, ClampNotePosition(position), text);
            serverMapNotes[noteId] = state;
            noteCreatedAt[noteId] = now;
            noteUpdatedAt[noteId] = now;
            replicatedMapNotes.Add(state);
            MarkNotesDirty();
        }

        [ServerRpc]
        private void UpdateMapNoteServerRpc(
            ulong noteId,
            Vector2 position,
            FixedString512Bytes requestedText)
        {
            if (!CanMutateMapNotes() || !serverMapNotes.ContainsKey(noteId)) return;
            var text = MapNoteRules.NormalizeText(requestedText.ToString());
            if (text.Length == 0)
            {
                SendMapNoteFeedbackClientRpc("Введите текст заметки.");
                return;
            }
            var state = new MapNoteNetworkState(noteId, ClampNotePosition(position), text);
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
        private void DeleteMapNoteServerRpc(ulong noteId)
        {
            if (!CanMutateMapNotes() || !serverMapNotes.Remove(noteId)) return;
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

        private bool CanMutateMapNotes()
        {
            if (!IsServer || resourceWorld == null) return false;
            if (Time.unscaledTime < nextNoteMutationAt) return false;
            nextNoteMutationAt = Time.unscaledTime + MapNoteRules.MutationCooldownSeconds;
            return true;
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
            if (IsOwner) UpdateOwnerInput();
            if (IsServer)
            {
                UpdateServerResearch();
                if ((knowledgeDirty || notesDirty) && !saveRunning
                    && Time.unscaledTime >= nextKnowledgeSaveAt)
                {
                    _ = FlushWithLoggingAsync();
                }
            }
        }

        private void UpdateOwnerInput()
        {
            UpdateFocusedNode();
            if (inventory.IsInterfaceOpen || ResourceMapView.IsOpen)
            {
                SetLocalResearchTarget(0);
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (focusedNode != null && focusedNode.Descriptor.IsLoosePickup
                && keyboard.eKey.wasPressedThisFrame)
            {
                SetLocalResearchTarget(0);
                CollectLooseServerRpc(focusedNode.InstanceId);
                return;
            }

            var researchTarget = focusedNode != null
                && focusedNode.Descriptor.IsResearchable
                && keyboard.eKey.isPressed
                ? focusedNode.InstanceId
                : 0UL;
            SetLocalResearchTarget(researchTarget);

            if (researchTarget == 0 && focusedNode != null
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
            var camera = player.OwnerCamera;
            if (camera == null) return;
            var hits = Physics.RaycastAll(
                camera.transform.position,
                camera.transform.forward,
                ResourceBalance.InteractionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                focusedNode = hit.collider.GetComponentInParent<ResourceNodeView>();
                break;
            }
        }

        private void SetLocalResearchTarget(ulong instanceId)
        {
            if (localResearchTarget == instanceId) return;
            localResearchTarget = instanceId;
            SetResearchTargetServerRpc(instanceId);
        }

        [ServerRpc]
        private void SetResearchTargetServerRpc(ulong instanceId)
        {
            serverResearchTarget = instanceId;
            researchAccumulator = 0f;
            lastServerResearchPosition = transform.position;
            if (instanceId != 0 && resourceWorld.TryGetNode(instanceId, out var node)
                && node.Descriptor.IsResearchable && ValidateInteraction(node))
            {
                EnsureKnowledge(instanceId);
            }
        }

        private void UpdateServerResearch()
        {
            if (serverResearchTarget == 0 || resourceWorld == null
                || !resourceWorld.TryGetNode(serverResearchTarget, out var node)
                || !node.Descriptor.IsResearchable || !ValidateInteraction(node))
            {
                researchAccumulator = 0f;
                serverResearchTarget = 0;
                return;
            }
            var movement = Vector3.ProjectOnPlane(
                transform.position - lastServerResearchPosition, Vector3.up).magnitude;
            lastServerResearchPosition = transform.position;
            if (movement > 0.025f)
            {
                researchAccumulator = 0f;
                serverResearchTarget = 0;
                return;
            }
            researchAccumulator += ResourceBalance.ResearchBasisPointsPerSecond * Time.deltaTime;
            var whole = Mathf.FloorToInt(researchAccumulator);
            if (whole < 10) return;
            researchAccumulator -= whole;
            AddStudyProgress(node.InstanceId, whole);
        }

        [ServerRpc]
        private void CollectLooseServerRpc(ulong instanceId)
        {
            if (resourceWorld == null || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsLoosePickup || !ValidateInteraction(node)
                || !resourceWorld.TryCollectLoose(node, out _))
            {
                return;
            }
            GiveStack(new ItemStackState(node.Descriptor.ResourceItemId, 1), node.transform.position);
            SendFeedbackClientRpc(node.Descriptor.ResourceItemId, 1, false, ToolKind.None);
        }

        [ServerRpc]
        private void MineServerRpc(ulong instanceId)
        {
            if (resourceWorld == null || NetworkManager.ServerTime.Time < nextMineAt
                || !resourceWorld.TryGetNode(instanceId, out var node)
                || !node.Descriptor.IsMineable || !ValidateInteraction(node))
            {
                return;
            }
            var active = inventory.ServerActiveStack;
            if (active.IsEmpty || inventory.Catalog == null
                || !inventory.Catalog.TryGetItem(active.ItemId, out var tool)
                || tool.Tool != node.Descriptor.RequiredTool || active.Condition == 0)
            {
                SendFeedbackClientRpc(0, 0, false, node.Descriptor.RequiredTool);
                return;
            }
            nextMineAt = NetworkManager.ServerTime.Time + ResourceBalance.MiningCooldownSeconds;
            serverResearchTarget = 0;
            var durabilityCost = ResourceBalance.DurabilityCost[node.Descriptor.Hardness - 1];
            if (!inventory.TryDamageActiveToolServer(
                    node.Descriptor.RequiredTool, durabilityCost, out var broke))
            {
                return;
            }
            if (!resourceWorld.ApplyMiningHit(
                    node, out var completed, out var extractionIndex, out _))
            {
                return;
            }
            if (node.Descriptor.IsResearchable)
            {
                EnsureKnowledge(instanceId);
                AddStudyProgress(instanceId, ResourceBalance.MiningResearchBasisPoints);
            }
            ushort awardedItem = 0;
            var awardedQuantity = 0;
            if (completed)
            {
                (awardedItem, awardedQuantity) = AwardExtraction(node, extractionIndex);
            }
            SendFeedbackClientRpc(
                awardedItem, awardedQuantity, broke, node.Descriptor.RequiredTool);
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
            ushort realItemId,
            ResourceQuality quality,
            int revealAtPercent,
            int currentStudyPercent)
        {
            if (revealAtPercent > 0 && currentStudyPercent < revealAtPercent)
            {
                return new ItemStackState(
                    ResourceBalance.UnknownSampleItemId,
                    1,
                    0,
                    quality,
                    realItemId,
                    sourceNodeId,
                    (byte)revealAtPercent);
            }
            return new ItemStackState(realItemId, 1, 0, quality);
        }

        private void GiveStack(ItemStackState stack, Vector3 nearPosition)
        {
            var remainder = inventory.InsertStackServer(stack);
            if (remainder > 0)
            {
                inventory.SpawnOverflowServer(stack.WithQuantity(remainder), nearPosition);
            }
        }

        private bool ValidateInteraction(ResourceNodeView node)
        {
            if (node == null || !node.IsAvailable
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
            if ((previousPercent < 50 && currentPercent >= 50)
                || (previousPercent < 100 && currentPercent >= 100))
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
                    if (pair.Value >= 5000)
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
            ushort itemId,
            int quantity,
            bool broke,
            ToolKind requiredTool)
        {
            if (!IsOwner) return;
            if (itemId == 0 && !broke && requiredTool != ToolKind.None)
            {
                SetFeedback($"Нужна {ResourceBalance.ToolName(requiredTool)} в активной ячейке.");
            }
            else if (broke)
            {
                SetFeedback($"{Capitalize(ResourceBalance.ToolName(requiredTool))} сломалась.");
            }
            else if (itemId != 0 && inventory.Catalog != null
                && inventory.Catalog.TryGetItem(itemId, out var item))
            {
                SetFeedback($"Получено: {item.DisplayName} × {quantity}");
            }
        }

        private static string Capitalize(string value) => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value.Substring(1);

        private void SetFeedback(string value)
        {
            lastFeedback = value;
            feedbackExpiresAt = Time.unscaledTime + 2.5f;
            Changed?.Invoke();
        }

        private void OnKnowledgeListChanged(
            NetworkListEvent<DepositKnowledgeNetworkState> change) => Changed?.Invoke();

        private void OnMapNoteListChanged(
            NetworkListEvent<MapNoteNetworkState> change) => Changed?.Invoke();
    }
}
