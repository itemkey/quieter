using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using Quieter.Inventory;
using UnityEngine;

namespace Quieter.Persistence
{
    public sealed class LocalJsonRepository : IWorldRepository, IPlayerProfileRepository
    {
        [Serializable]
        private sealed class State
        {
            public bool HasWorld;
            public WorldDefinition World;
            public List<StoredPlayer> Players = new();
            public List<StoredResourceNodeState> ResourceNodes = new();
            public List<StoredPlacedObject> PlacedObjects = new();
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
            public List<StoredDepositKnowledge> DepositKnowledge = new();
            public List<StoredMapNote> MapNotes = new();
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

                    if (state.World.GeneratorVersion < Core.QuieterConstants.GeneratorVersion)
                    {
                        state.World.GeneratorVersion = Core.QuieterConstants.GeneratorVersion;
                        changed = true;
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
                player.LastSeenAtUtc = now.ToString("O");
                Save();
                return Task.FromResult(ToProfile(player));
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
                    player.InventorySlots = CloneSlots(slots);
                    player.PendingItems = CloneSlots(pendingItems);
                    player.SelectedHotbarIndex = (byte)Math.Min(
                        (int)selectedHotbarIndex,
                        InventoryLayout.HotbarSlotCount - 1);
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }

            return Task.CompletedTask;
        }

        public Task SaveDepositKnowledgeAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredDepositKnowledge> knowledge,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var player = state.Players.Find(candidate => candidate.SteamId == steamId.ToString());
                if (player != null)
                {
                    player.DepositKnowledge.RemoveAll(entry => entry == null
                        || entry.WorldId == worldId);
                    var currentWorld = CloneKnowledge(knowledge);
                    foreach (var entry in currentWorld) entry.WorldId = worldId;
                    player.DepositKnowledge.AddRange(currentWorld);
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
                    player.MapNotes.RemoveAll(entry => entry == null || entry.WorldId == worldId);
                    var currentWorld = CloneMapNotes(notes);
                    foreach (var entry in currentWorld) entry.WorldId = worldId;
                    player.MapNotes.AddRange(currentWorld);
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }
            return Task.CompletedTask;
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
            var defaultWorldId = state.World.WorldId == 0 ? 1 : state.World.WorldId;
            foreach (var node in state.ResourceNodes)
            {
                if (node != null && node.WorldId == 0) node.WorldId = defaultWorldId;
            }
            foreach (var placed in state.PlacedObjects)
            {
                if (placed != null && placed.WorldId == 0) placed.WorldId = defaultWorldId;
            }
            foreach (var player in state.Players)
            {
                player.InventorySlots ??= new List<StoredInventorySlot>();
                player.DepositKnowledge ??= new List<StoredDepositKnowledge>();
                player.MapNotes ??= new List<StoredMapNote>();
                foreach (var entry in player.DepositKnowledge)
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
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static PlayerProfile ToProfile(StoredPlayer player)
        {
            return new PlayerProfile
            {
                SteamId = ulong.Parse(player.SteamId),
                DisplayName = player.DisplayName,
                Position = player.Position,
                CreatedAtUtc = ParseDate(player.CreatedAtUtc),
                LastSeenAtUtc = ParseDate(player.LastSeenAtUtc),
                InventorySlots = CloneSlots(player.InventorySlots),
                PendingItems = CloneSlots(player.PendingItems),
                SelectedHotbarIndex = player.SelectedHotbarIndex,
                DepositKnowledge = CloneKnowledge(player.DepositKnowledge),
                MapNotes = CloneMapNotes(player.MapNotes),
            };
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
                    ItemId = entry.ItemId,
                    X = entry.X,
                    Y = entry.Y,
                    Z = entry.Z,
                    Yaw = entry.Yaw,
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
