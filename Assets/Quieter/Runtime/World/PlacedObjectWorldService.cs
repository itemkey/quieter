using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Inventory;
using Quieter.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.World
{
    public sealed class PlacedObjectWorldService : MonoBehaviour
    {
        private const string SnapshotMessage = "quieter.placed-objects";

        private sealed class RuntimeObject
        {
            public ulong ObjectId;
            public ushort ItemId;
            public Vector3 Position;
            public float Yaw;
            public ItemStackState Input;
            public ulong BusyClientId = ulong.MaxValue;
            public DateTime CreatedAtUtc;
            public DateTime UpdatedAtUtc;

            public bool IsBusy => BusyClientId != ulong.MaxValue;
        }

        private readonly Dictionary<ulong, RuntimeObject> objects = new();
        private readonly Dictionary<ulong, ResearchTableView> views = new();
        private readonly HashSet<ulong> dirtyObjects = new();
        private readonly SemaphoreSlim flushGate = new(1, 1);
        private NetworkManager networkManager;
        private IWorldRepository repository;
        private WorldDefinition definition;
        private Transform viewRoot;
        private bool configured;
        private bool messagingRegistered;
        private bool flushRunning;
        private bool fullSaveRequired;
        private float nextFlushAt;
        private uint objectNonce;
        private uint persistenceRevision;

        public event Action Changed;
        public WorldDefinition Definition => definition;

        public void Configure(NetworkManager manager)
        {
            if (configured) return;
            networkManager = manager;
            configured = true;
            EnsureMessagingRegistered();
        }

        public async Task InitializeServerAsync(
            WorldDefinition worldDefinition,
            IWorldRepository worldRepository,
            CancellationToken cancellationToken)
        {
            definition = worldDefinition;
            repository = worldRepository;
            ClearObjects();
            var stored = await repository.LoadPlacedObjectsAsync(
                definition.WorldId, cancellationToken);
            foreach (var entry in stored)
            {
                if (entry == null || entry.ItemId != ResourceBalance.ResearchTableItemId
                    || !ulong.TryParse(entry.ObjectId, out var objectId) || objectId == 0)
                {
                    continue;
                }
                var runtime = new RuntimeObject
                {
                    ObjectId = objectId,
                    ItemId = entry.ItemId,
                    Position = new Vector3(entry.X, entry.Y, entry.Z),
                    Yaw = entry.Yaw,
                    Input = FromStoredStack(entry.Input),
                    CreatedAtUtc = ParseDate(entry.CreatedAtUtc),
                    UpdatedAtUtc = ParseDate(entry.UpdatedAtUtc),
                };
                objects[objectId] = runtime;
                CreateOrUpdateView(runtime, networkManager != null && networkManager.IsClient);
            }
            nextFlushAt = Time.unscaledTime + 5f;
        }

        public void InitializeClient(WorldDefinition worldDefinition)
        {
            definition = worldDefinition;
            EnsureMessagingRegistered();
            if (networkManager != null && networkManager.IsServer)
            {
                RebuildViews(renderVisuals: true);
                return;
            }
            ClearObjects();
        }

        public bool TryGetView(ulong objectId, out ResearchTableView view) =>
            views.TryGetValue(objectId, out view) && view != null;

        public bool TryGetState(
            ulong objectId,
            out Vector3 position,
            out ItemStackState input,
            out bool busy)
        {
            if (objects.TryGetValue(objectId, out var state))
            {
                position = state.Position;
                input = networkManager != null && networkManager.IsServer
                    ? state.Input
                    : state.Input.ForReplication();
                busy = state.IsBusy;
                return true;
            }
            position = default;
            input = default;
            busy = false;
            return false;
        }

        public bool TryPlace(Vector3 position, float yaw, out ulong objectId)
        {
            objectId = 0;
            if (networkManager == null || !networkManager.IsServer || definition.WorldId == 0
                || !IsFinite(position) || !float.IsFinite(yaw))
            {
                return false;
            }
            position.x = Mathf.Clamp(position.x, definition.WorldMinimum.x + 1f,
                definition.WorldMaximum.x - 1f);
            position.z = Mathf.Clamp(position.z, definition.WorldMinimum.z + 1f,
                definition.WorldMaximum.z - 1f);
            objectId = CreateObjectId(position);
            var now = DateTime.UtcNow;
            var state = new RuntimeObject
            {
                ObjectId = objectId,
                ItemId = ResourceBalance.ResearchTableItemId,
                Position = position,
                Yaw = Mathf.Repeat(yaw, 360f),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            objects.Add(objectId, state);
            CreateOrUpdateView(state, networkManager.IsClient);
            MarkDirty(objectId, fullSave: false);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryDismantle(ulong objectId, out Vector3 position)
        {
            position = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.IsBusy || !state.Input.IsEmpty)
            {
                return false;
            }
            position = state.Position;
            objects.Remove(objectId);
            if (views.Remove(objectId, out var view) && view != null) Destroy(view.gameObject);
            MarkDirty(objectId, fullSave: true);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryInsertInput(ulong objectId, ItemStackState sample)
        {
            if (networkManager == null || !networkManager.IsServer
                || sample.ItemId != ResourceBalance.UnknownSampleItemId
                || sample.Quantity != 1 || sample.HiddenItemId == 0
                || sample.SourceNodeId == 0 || sample.SampleId == 0
                || !objects.TryGetValue(objectId, out var state)
                || state.IsBusy || !state.Input.IsEmpty)
            {
                return false;
            }
            state.Input = sample;
            Touch(state);
            return true;
        }

        public bool TryTakeInput(ulong objectId, out ItemStackState sample)
        {
            sample = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.IsBusy || state.Input.IsEmpty)
            {
                return false;
            }
            sample = state.Input;
            state.Input = default;
            Touch(state);
            return true;
        }

        public bool TryBeginResearch(
            ulong objectId,
            ulong clientId,
            out ItemStackState sample,
            out Vector3 position)
        {
            sample = default;
            position = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.IsBusy || state.Input.IsEmpty
                || state.Input.ItemId != ResourceBalance.UnknownSampleItemId)
            {
                return false;
            }
            state.BusyClientId = clientId;
            sample = state.Input;
            position = state.Position;
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
            return true;
        }

        public bool TryCompleteResearch(
            ulong objectId,
            ulong clientId,
            ulong expectedSampleId,
            out ItemStackState consumed)
        {
            consumed = default;
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.BusyClientId != clientId || state.Input.IsEmpty
                || state.Input.SampleId != expectedSampleId)
            {
                return false;
            }
            consumed = state.Input;
            state.Input = default;
            state.BusyClientId = ulong.MaxValue;
            Touch(state);
            return true;
        }

        public void CancelResearch(ulong objectId, ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer
                || !objects.TryGetValue(objectId, out var state)
                || state.BusyClientId != clientId)
            {
                return;
            }
            state.BusyClientId = ulong.MaxValue;
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
        }

        public void SendSnapshot(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer) return;
            SendSnapshotTo(new[] { clientId });
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (repository == null) return;
            await flushGate.WaitAsync(cancellationToken);
            try
            {
                if (!fullSaveRequired && dirtyObjects.Count == 0) return;
                flushRunning = true;
                var savedRevision = persistenceRevision;
                var snapshot = CreateStoredSnapshot();
                await repository.SavePlacedObjectsAsync(
                    definition.WorldId, snapshot, cancellationToken);
                if (persistenceRevision == savedRevision)
                {
                    dirtyObjects.Clear();
                    fullSaveRequired = false;
                }
            }
            finally
            {
                flushRunning = false;
                nextFlushAt = Time.unscaledTime + 5f;
                flushGate.Release();
            }
        }

        private void Update()
        {
            if (networkManager == null || !networkManager.IsServer || flushRunning
                || (!fullSaveRequired && dirtyObjects.Count == 0)
                || Time.unscaledTime < nextFlushAt)
            {
                return;
            }
            _ = FlushWithLoggingAsync();
        }

        private async Task FlushWithLoggingAsync()
        {
            try
            {
                await FlushAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save placed objects: {exception.Message}");
            }
        }

        private void Touch(RuntimeObject state)
        {
            state.UpdatedAtUtc = DateTime.UtcNow;
            MarkDirty(state.ObjectId, fullSave: false);
            ApplyViewState(state);
            BroadcastSnapshot();
            Changed?.Invoke();
        }

        private void MarkDirty(ulong objectId, bool fullSave)
        {
            dirtyObjects.Add(objectId);
            fullSaveRequired |= fullSave;
            persistenceRevision++;
            if (nextFlushAt <= Time.unscaledTime)
            {
                nextFlushAt = Time.unscaledTime + 5f;
            }
        }

        private List<StoredPlacedObject> CreateStoredSnapshot()
        {
            var result = new List<StoredPlacedObject>(objects.Count);
            foreach (var state in objects.Values)
            {
                result.Add(new StoredPlacedObject
                {
                    WorldId = definition.WorldId,
                    ObjectId = state.ObjectId.ToString(),
                    ItemId = state.ItemId,
                    X = state.Position.x,
                    Y = state.Position.y,
                    Z = state.Position.z,
                    Yaw = state.Yaw,
                    Input = ToStoredStack(state.Input),
                    CreatedAtUtc = state.CreatedAtUtc.ToString("O"),
                    UpdatedAtUtc = state.UpdatedAtUtc.ToString("O"),
                });
            }
            return result;
        }

        private void BroadcastSnapshot()
        {
            if (networkManager == null || !networkManager.IsServer) return;
            SendSnapshotTo(null);
        }

        private void SendSnapshotTo(IReadOnlyList<ulong> targets)
        {
            EnsureMessagingRegistered();
            if (!messagingRegistered || networkManager?.CustomMessagingManager == null) return;
            var writer = new FastBufferWriter(
                Mathf.Max(128, 4 + objects.Count * 72), Allocator.Temp, 131072);
            try
            {
                writer.WriteValueSafe(objects.Count);
                foreach (var state in objects.Values)
                {
                    writer.WriteValueSafe(state.ObjectId);
                    writer.WriteValueSafe(state.ItemId);
                    writer.WriteValueSafe(state.Position);
                    writer.WriteValueSafe(state.Yaw);
                    writer.WriteValueSafe(state.IsBusy);
                    WriteSafeStack(ref writer, state.Input.ForReplication());
                }
                if (targets == null)
                {
                    networkManager.CustomMessagingManager.SendNamedMessageToAll(
                        SnapshotMessage, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    return;
                }
                foreach (var target in targets)
                {
                    networkManager.CustomMessagingManager.SendNamedMessage(
                        SnapshotMessage, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void OnSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsClient || networkManager.IsServer) return;
            reader.ReadValueSafe(out int count);
            var received = new HashSet<ulong>();
            for (var index = 0; index < count; index++)
            {
                reader.ReadValueSafe(out ulong objectId);
                reader.ReadValueSafe(out ushort itemId);
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out float yaw);
                reader.ReadValueSafe(out bool busy);
                var input = ReadSafeStack(ref reader);
                var state = new RuntimeObject
                {
                    ObjectId = objectId,
                    ItemId = itemId,
                    Position = position,
                    Yaw = yaw,
                    Input = input,
                    BusyClientId = busy ? 0UL : ulong.MaxValue,
                };
                objects[objectId] = state;
                received.Add(objectId);
                CreateOrUpdateView(state, renderVisuals: true);
            }
            var removed = new List<ulong>();
            foreach (var id in objects.Keys)
            {
                if (!received.Contains(id)) removed.Add(id);
            }
            foreach (var id in removed)
            {
                objects.Remove(id);
                if (views.Remove(id, out var view) && view != null) Destroy(view.gameObject);
            }
            Changed?.Invoke();
        }

        private void EnsureMessagingRegistered()
        {
            if (messagingRegistered || networkManager?.CustomMessagingManager == null) return;
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                SnapshotMessage, OnSnapshotMessage);
            messagingRegistered = true;
        }

        private void CreateOrUpdateView(RuntimeObject state, bool renderVisuals)
        {
            if (!views.TryGetValue(state.ObjectId, out var view) || view == null)
            {
                viewRoot ??= new GameObject("PlacedObjects").transform;
                viewRoot.SetParent(transform, false);
                view = ResearchTableView.Create(
                    state.ObjectId, state.Position, state.Yaw, viewRoot, renderVisuals);
                views[state.ObjectId] = view;
            }
            else
            {
                view.transform.SetPositionAndRotation(
                    state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            }
            view.ApplyState(state.Input.ForReplication(), state.IsBusy);
        }

        private void ApplyViewState(RuntimeObject state)
        {
            if (views.TryGetValue(state.ObjectId, out var view) && view != null)
            {
                view.ApplyState(state.Input.ForReplication(), state.IsBusy);
            }
        }

        private void RebuildViews(bool renderVisuals)
        {
            foreach (var view in views.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            views.Clear();
            foreach (var state in objects.Values) CreateOrUpdateView(state, renderVisuals);
        }

        private void ClearObjects()
        {
            objects.Clear();
            dirtyObjects.Clear();
            fullSaveRequired = false;
            persistenceRevision = 0;
            foreach (var view in views.Values)
            {
                if (view != null) Destroy(view.gameObject);
            }
            views.Clear();
        }

        private ulong CreateObjectId(Vector3 position)
        {
            unchecked
            {
                ulong value;
                do
                {
                    objectNonce++;
                    value = (ulong)definition.Seed ^ ((ulong)(uint)definition.WorldId << 32);
                    value ^= (uint)Mathf.RoundToInt(position.x * 100f) * 0x9E3779B9UL;
                    value ^= (uint)Mathf.RoundToInt(position.z * 100f) * 0x85EBCA6BUL;
                    value ^= objectNonce * 0xC2B2AE35UL;
                    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                    value ^= value >> 31;
                } while (value == 0 || objects.ContainsKey(value));
                return value;
            }
        }

        private static void WriteSafeStack(ref FastBufferWriter writer, ItemStackState stack)
        {
            writer.WriteValueSafe(stack.ItemId);
            writer.WriteValueSafe(stack.Quantity);
            writer.WriteValueSafe(stack.SourceNodeId);
            writer.WriteValueSafe(stack.RevealAtPercent);
            writer.WriteValueSafe(stack.SampleId);
        }

        private static ItemStackState ReadSafeStack(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort itemId);
            reader.ReadValueSafe(out ushort quantity);
            reader.ReadValueSafe(out ulong sourceNodeId);
            reader.ReadValueSafe(out byte revealAtPercent);
            reader.ReadValueSafe(out ulong sampleId);
            return new ItemStackState(itemId, quantity, sourceNodeId: sourceNodeId,
                revealAtPercent: revealAtPercent, sampleId: sampleId);
        }

        private static StoredInventorySlot ToStoredStack(ItemStackState stack) => stack.IsEmpty
            ? null
            : new StoredInventorySlot
            {
                ItemId = stack.ItemId,
                Quantity = stack.Quantity,
                Condition = stack.Condition,
                Quality = (byte)stack.Quality,
                HiddenItemId = stack.HiddenItemId,
                SourceNodeId = stack.SourceNodeId.ToString(),
                RevealAtPercent = stack.RevealAtPercent,
                SampleId = stack.SampleId.ToString(),
            };

        private static ItemStackState FromStoredStack(StoredInventorySlot stack)
        {
            if (stack == null || stack.ItemId == 0 || stack.Quantity == 0) return default;
            ulong.TryParse(stack.SourceNodeId, out var sourceNodeId);
            ulong.TryParse(stack.SampleId, out var sampleId);
            return new ItemStackState(
                stack.ItemId,
                stack.Quantity,
                stack.Condition,
                (ResourceQuality)stack.Quality,
                stack.HiddenItemId,
                sourceNodeId,
                stack.RevealAtPercent,
                sampleId);
        }

        private static DateTime ParseDate(string value) => DateTime.TryParse(
            value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.UtcNow;

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);

        private void OnDestroy()
        {
            if (messagingRegistered && networkManager?.CustomMessagingManager != null)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
            }
            ClearObjects();
        }
    }
}
