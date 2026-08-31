using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Quieter.World
{
    public sealed class ResourceWorldService : MonoBehaviour
    {
        private const string SnapshotMessage = "quieter.resource-nodes";
        private readonly Dictionary<ulong, ResourceNodeRuntimeState> states = new();
        private readonly Dictionary<ulong, int> partialMiningWork = new();
        private readonly HashSet<ulong> dirty = new();

        private NetworkManager networkManager;
        private WorldStreamer streamer;
        private IWorldRepository repository;
        private WorldDefinition definition;
        private float nextFlushAt;
        private float nextAvailabilityRefresh;
        private bool flushRunning;
        private bool configured;
        private bool messagingRegistered;

        public event Action<ulong, ResourceNodeRuntimeState> StateChanged;
        public WorldDefinition Definition => definition;

        public bool TryGetNode(ulong instanceId, out ResourceNodeView node)
        {
            node = null;
            return streamer != null && streamer.TryGetResourceNode(instanceId, out node);
        }

        public void Configure(NetworkManager manager, WorldStreamer worldStreamer)
        {
            if (configured) return;
            networkManager = manager;
            streamer = worldStreamer;
            streamer.ResourceNodeAdded += OnResourceNodeAdded;
            configured = true;
        }

        public async Task InitializeServerAsync(
            WorldDefinition worldDefinition,
            IWorldRepository worldRepository,
            CancellationToken cancellationToken)
        {
            definition = worldDefinition;
            repository = worldRepository;
            states.Clear();
            var stored = await repository.LoadResourceNodeStatesAsync(
                definition.WorldId, cancellationToken);
            foreach (var entry in stored)
            {
                if (entry == null || !ulong.TryParse(entry.InstanceId, out var instanceId)
                    || instanceId == 0
                    || (entry.WorldId != 0 && entry.WorldId != definition.WorldId))
                {
                    continue;
                }
                var availableAt = DateTime.TryParse(
                    entry.AvailableAtUtc,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed)
                    ? new DateTimeOffset(parsed.ToUniversalTime()).ToUnixTimeSeconds()
                    : 0L;
                states[instanceId] = new ResourceNodeRuntimeState(
                    instanceId, entry.RemainingReserves, availableAt);
            }
            nextFlushAt = Time.unscaledTime + 5f;
        }

        public void InitializeClient(WorldDefinition worldDefinition)
        {
            EnsureMessagingRegistered();
            definition = worldDefinition;
            states.Clear();
            partialMiningWork.Clear();
            dirty.Clear();
        }

        public ResourceNodeRuntimeState GetState(ResourceNodeView node)
        {
            if (node == null) return default;
            if (states.TryGetValue(node.InstanceId, out var state)) return state;
            return new ResourceNodeRuntimeState(
                node.InstanceId,
                node.Descriptor.InitialReserves,
                0);
        }

        public ResourceNodeRuntimeState GetState(
            ulong instanceId,
            ResourceNodeDescriptor descriptor)
        {
            return states.TryGetValue(instanceId, out var state)
                ? state
                : new ResourceNodeRuntimeState(instanceId, descriptor.InitialReserves);
        }

        public bool TryCollectLoose(ResourceNodeView node, out ResourceNodeRuntimeState state)
        {
            state = default;
            if (node == null || !node.Descriptor.IsLoosePickup
                || networkManager == null || !networkManager.IsServer)
            {
                return false;
            }
            var current = GetState(node);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!current.IsAvailable(now)) return false;
            state = new ResourceNodeRuntimeState(
                node.InstanceId,
                1,
                now + node.Descriptor.RespawnSeconds);
            SetState(state, persist: true, broadcast: true);
            return true;
        }

        public bool ApplyMiningHit(
            ResourceNodeView node,
            out bool extractionCompleted,
            out int extractionIndex,
            out ResourceNodeRuntimeState state)
        {
            extractionCompleted = false;
            extractionIndex = 0;
            state = default;
            if (node == null || !node.Descriptor.IsMineable
                || networkManager == null || !networkManager.IsServer)
            {
                return false;
            }
            var current = GetState(node);
            if (current.RemainingReserves == 0) return false;
            partialMiningWork.TryGetValue(node.InstanceId, out var work);
            work++;
            var required = ResourceBalance.WorkRequired(node.Descriptor);
            if (work < required)
            {
                partialMiningWork[node.InstanceId] = work;
                state = current;
                return true;
            }

            partialMiningWork[node.InstanceId] = 0;
            extractionCompleted = true;
            var remaining = (ushort)(current.RemainingReserves - 1);
            extractionIndex = node.Descriptor.InitialReserves - remaining;
            state = new ResourceNodeRuntimeState(node.InstanceId, remaining);
            SetState(state, persist: true, broadcast: true);
            return true;
        }

        public ulong CalculateExtractionRoll(ulong instanceId, int extractionIndex, int salt)
        {
            unchecked
            {
                var value = (ulong)definition.Seed ^ instanceId;
                value ^= (uint)extractionIndex * 0x9E3779B9UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value ^= (uint)salt * 0xC2B2AE35UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        public void SendSnapshot(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer) return;
            EnsureMessagingRegistered();
            SendStates(states.Values, new[] { clientId });
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (repository == null || dirty.Count == 0 || flushRunning) return;
            flushRunning = true;
            var ids = new List<ulong>(dirty);
            var payload = new List<StoredResourceNodeState>(ids.Count);
            foreach (var id in ids)
            {
                if (!states.TryGetValue(id, out var state)) continue;
                payload.Add(new StoredResourceNodeState
                {
                    WorldId = definition.WorldId,
                    InstanceId = id.ToString(),
                    RemainingReserves = state.RemainingReserves,
                    AvailableAtUtc = state.AvailableAtUnixSeconds <= 0
                        ? null
                        : DateTimeOffset.FromUnixTimeSeconds(state.AvailableAtUnixSeconds)
                            .UtcDateTime.ToString("O"),
                });
            }
            try
            {
                await repository.SaveResourceNodeStatesAsync(
                    definition.WorldId, payload, cancellationToken);
                foreach (var state in payload)
                {
                    if (!ulong.TryParse(state.InstanceId, out var id)
                        || !states.TryGetValue(id, out var current))
                    {
                        continue;
                    }
                    var savedAvailableAt = string.IsNullOrEmpty(state.AvailableAtUtc)
                        ? 0L
                        : DateTimeOffset.Parse(state.AvailableAtUtc).ToUnixTimeSeconds();
                    if (current.RemainingReserves == state.RemainingReserves
                        && current.AvailableAtUnixSeconds == savedAvailableAt)
                    {
                        dirty.Remove(id);
                    }
                }
            }
            finally
            {
                flushRunning = false;
                nextFlushAt = Time.unscaledTime + 5f;
            }
        }

        private void Update()
        {
            if (networkManager == null) return;
            if (networkManager.IsServer && dirty.Count > 0
                && !flushRunning && Time.unscaledTime >= nextFlushAt)
            {
                _ = FlushWithLoggingAsync();
            }
            if (Time.unscaledTime < nextAvailabilityRefresh) return;
            nextAvailabilityRefresh = Time.unscaledTime + 1f;
            foreach (var pair in states)
            {
                ApplyStateToView(pair.Key, pair.Value);
            }
        }

        private async Task FlushWithLoggingAsync()
        {
            try
            {
                await FlushAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save resource nodes: {exception.Message}");
            }
        }

        private void SetState(ResourceNodeRuntimeState state, bool persist, bool broadcast)
        {
            states[state.InstanceId] = state;
            if (persist) dirty.Add(state.InstanceId);
            ApplyStateToView(state.InstanceId, state);
            StateChanged?.Invoke(state.InstanceId, state);
            if (broadcast && networkManager != null && networkManager.IsServer)
            {
                SendStates(new[] { state }, null);
            }
        }

        private void SendStates(
            IEnumerable<ResourceNodeRuntimeState> values,
            IReadOnlyList<ulong> targets)
        {
            EnsureMessagingRegistered();
            if (!messagingRegistered) return;
            var list = values is ICollection<ResourceNodeRuntimeState> collection
                ? new List<ResourceNodeRuntimeState>(collection)
                : new List<ResourceNodeRuntimeState>(values);
            using var writer = new FastBufferWriter(
                Mathf.Max(64, 4 + list.Count * 20), Allocator.Temp, 65536);
            writer.WriteValueSafe((ushort)Mathf.Min(list.Count, ushort.MaxValue));
            foreach (var state in list)
            {
                writer.WriteValueSafe(state.InstanceId);
                writer.WriteValueSafe(state.RemainingReserves);
                writer.WriteValueSafe(state.AvailableAtUnixSeconds);
            }
            if (targets == null)
            {
                networkManager.CustomMessagingManager.SendNamedMessageToAll(
                    SnapshotMessage, writer, NetworkDelivery.ReliableSequenced);
            }
            else
            {
                foreach (var target in targets)
                {
                    networkManager.CustomMessagingManager.SendNamedMessage(
                        SnapshotMessage, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
        }

        private void OnSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (networkManager == null || !networkManager.IsClient) return;
            reader.ReadValueSafe(out ushort count);
            for (var index = 0; index < count; index++)
            {
                reader.ReadValueSafe(out ulong instanceId);
                reader.ReadValueSafe(out ushort remaining);
                reader.ReadValueSafe(out long availableAt);
                SetState(
                    new ResourceNodeRuntimeState(instanceId, remaining, availableAt),
                    persist: false,
                    broadcast: false);
            }
        }

        private void OnResourceNodeAdded(ResourceNodeView node)
        {
            if (node == null) return;
            ApplyStateToView(node.InstanceId, GetState(node));
        }

        private void EnsureMessagingRegistered()
        {
            if (messagingRegistered || networkManager?.CustomMessagingManager == null) return;
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                SnapshotMessage, OnSnapshotMessage);
            messagingRegistered = true;
        }

        private void ApplyStateToView(ulong instanceId, ResourceNodeRuntimeState state)
        {
            if (streamer == null || !streamer.TryGetResourceNode(instanceId, out var view)) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            view.ApplyState(state.RemainingReserves, state.IsAvailable(now));
        }

        private void OnDestroy()
        {
            if (streamer != null) streamer.ResourceNodeAdded -= OnResourceNodeAdded;
            if (messagingRegistered && networkManager?.CustomMessagingManager != null)
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
            }
        }
    }
}
