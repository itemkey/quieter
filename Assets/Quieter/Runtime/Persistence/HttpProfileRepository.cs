using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using Quieter.Inventory;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Quieter.Persistence
{
    public sealed class HttpProfileRepository : IWorldRepository, IPlayerProfileRepository
    {
        [Serializable]
        private sealed class WorldResponse
        {
            public int worldId;
            public long seed;
            public ushort generatorVersion;
            public ushort chunkCountX;
            public ushort chunkCountZ;
            public ushort chunkSize;
            public ushort samplesPerSide;
            public float heightStep;
        }

        [Serializable]
        private sealed class LoginRequest
        {
            public string steamId;
            public string displayName;
            public float defaultX;
            public float defaultY;
            public float defaultZ;
        }

        [Serializable]
        private sealed class ProfileResponse
        {
            public string steamId;
            public string displayName;
            public float positionX;
            public float positionY;
            public float positionZ;
            public string createdAtUtc;
            public string lastSeenAtUtc;
            public InventorySlotResponse[] inventorySlots;
            public byte selectedHotbarIndex;
            public DepositKnowledgeResponse[] depositKnowledge;
            public MapNoteResponse[] mapNotes;
        }

        [Serializable]
        private sealed class InventorySlotResponse
        {
            public byte slotIndex;
            public ushort itemId;
            public ushort quantity;
            public ushort condition;
            public byte quality;
            public ushort hiddenItemId;
            public string sourceNodeId;
            public byte revealAtPercent;
        }

        [Serializable]
        private sealed class InventoryRequest
        {
            public byte selectedHotbarIndex;
            public InventorySlotResponse[] slots;
        }

        [Serializable]
        private sealed class PositionRequest
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class ResourceNodeResponse
        {
            public string instanceId;
            public ushort remainingReserves;
            public string availableAtUtc;
        }

        [Serializable]
        private sealed class ResourceNodeListResponse
        {
            public ResourceNodeResponse[] nodes;
        }

        [Serializable]
        private sealed class DepositKnowledgeResponse
        {
            public int worldId;
            public string instanceId;
            public ushort studyBasisPoints;
            public string discoveredAtUtc;
        }

        [Serializable]
        private sealed class DepositKnowledgeListResponse
        {
            public DepositKnowledgeResponse[] knowledge;
        }

        [Serializable]
        private sealed class MapNoteResponse
        {
            public int worldId;
            public string noteId;
            public float x;
            public float z;
            public string text;
            public string createdAtUtc;
            public string updatedAtUtc;
        }

        [Serializable]
        private sealed class MapNoteListResponse
        {
            public MapNoteResponse[] notes;
        }

        private readonly string baseUrl;
        private readonly string token;

        public HttpProfileRepository(string baseUrl, string token)
        {
            this.baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? throw new ArgumentException("Profile service URL is required.", nameof(baseUrl))
                : baseUrl.TrimEnd('/');
            this.token = token ?? string.Empty;
        }

        public async Task<WorldDefinition> GetOrCreateWorldAsync(
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(baseUrl + "/internal/world/current");
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<WorldResponse>(responseText);
            return new WorldDefinition
            {
                WorldId = response.worldId,
                Seed = response.seed,
                GeneratorVersion = response.generatorVersion,
                ChunkCountX = response.chunkCountX,
                ChunkCountZ = response.chunkCountZ,
                ChunkSize = response.chunkSize,
                SamplesPerSide = response.samplesPerSide,
                HeightStep = response.heightStep,
            };
        }

        public async Task<PlayerProfile> LoginAsync(
            ulong steamId,
            string displayName,
            Vector3 defaultSpawn,
            CancellationToken cancellationToken = default)
        {
            var payload = new LoginRequest
            {
                steamId = steamId.ToString(),
                displayName = displayName,
                defaultX = defaultSpawn.x,
                defaultY = defaultSpawn.y,
                defaultZ = defaultSpawn.z,
            };
            using var request = CreateJsonRequest(
                baseUrl + "/internal/players/login",
                UnityWebRequest.kHttpVerbPOST,
                JsonUtility.ToJson(payload));
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<ProfileResponse>(responseText);
            return new PlayerProfile
            {
                SteamId = ulong.Parse(response.steamId),
                DisplayName = response.displayName,
                Position = new Vector3(response.positionX, response.positionY, response.positionZ),
                CreatedAtUtc = ParseDate(response.createdAtUtc),
                LastSeenAtUtc = ParseDate(response.lastSeenAtUtc),
                InventorySlots = ToStoredSlots(response.inventorySlots),
                SelectedHotbarIndex = response.selectedHotbarIndex,
                DepositKnowledge = ToStoredKnowledge(response.depositKnowledge),
                MapNotes = ToStoredMapNotes(response.mapNotes),
            };
        }

        public async Task<IReadOnlyList<StoredResourceNodeState>> LoadResourceNodeStatesAsync(
            int worldId,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(
                $"{baseUrl}/internal/worlds/{worldId}/resource-nodes");
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<ResourceNodeListResponse>(responseText);
            var result = new List<StoredResourceNodeState>();
            if (response?.nodes == null) return result;
            foreach (var node in response.nodes)
            {
                if (node == null) continue;
                result.Add(new StoredResourceNodeState
                {
                    WorldId = worldId,
                    InstanceId = node.instanceId,
                    RemainingReserves = node.remainingReserves,
                    AvailableAtUtc = node.availableAtUtc,
                });
            }
            return result;
        }

        public async Task SaveResourceNodeStatesAsync(
            int worldId,
            IReadOnlyList<StoredResourceNodeState> states,
            CancellationToken cancellationToken = default)
        {
            var nodes = new List<ResourceNodeResponse>();
            if (states != null)
            {
                foreach (var state in states)
                {
                    if (state == null) continue;
                    nodes.Add(new ResourceNodeResponse
                    {
                        instanceId = state.InstanceId,
                        remainingReserves = state.RemainingReserves,
                        availableAtUtc = state.AvailableAtUtc,
                    });
                }
            }
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/worlds/{worldId}/resource-nodes",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(new ResourceNodeListResponse { nodes = nodes.ToArray() }));
            await SendAsync(request, cancellationToken);
        }

        public async Task SavePositionAsync(
            ulong steamId,
            Vector3 position,
            CancellationToken cancellationToken = default)
        {
            var payload = new PositionRequest { x = position.x, y = position.y, z = position.z };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/position",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        public async Task SaveInventoryAsync(
            ulong steamId,
            IReadOnlyList<StoredInventorySlot> slots,
            byte selectedHotbarIndex,
            CancellationToken cancellationToken = default)
        {
            var payloadSlots = new List<InventorySlotResponse>();
            if (slots != null)
            {
                foreach (var slot in slots)
                {
                    if (slot == null) continue;
                    payloadSlots.Add(new InventorySlotResponse
                    {
                        slotIndex = slot.SlotIndex,
                        itemId = slot.ItemId,
                        quantity = slot.Quantity,
                        condition = slot.Condition,
                        quality = slot.Quality,
                        hiddenItemId = slot.HiddenItemId,
                        sourceNodeId = slot.SourceNodeId,
                        revealAtPercent = slot.RevealAtPercent,
                    });
                }
            }

            var payload = new InventoryRequest
            {
                selectedHotbarIndex = selectedHotbarIndex,
                slots = payloadSlots.ToArray(),
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/inventory",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        public async Task SaveDepositKnowledgeAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredDepositKnowledge> knowledge,
            CancellationToken cancellationToken = default)
        {
            var entries = new List<DepositKnowledgeResponse>();
            if (knowledge != null)
            {
                foreach (var entry in knowledge)
                {
                    if (entry == null) continue;
                    entries.Add(new DepositKnowledgeResponse
                    {
                        worldId = worldId,
                        instanceId = entry.InstanceId,
                        studyBasisPoints = entry.StudyBasisPoints,
                        discoveredAtUtc = entry.DiscoveredAtUtc,
                    });
                }
            }
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/worlds/{worldId}/deposit-knowledge",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(new DepositKnowledgeListResponse { knowledge = entries.ToArray() }));
            await SendAsync(request, cancellationToken);
        }

        public async Task SaveMapNotesAsync(
            ulong steamId,
            int worldId,
            IReadOnlyList<StoredMapNote> notes,
            CancellationToken cancellationToken = default)
        {
            var entries = new List<MapNoteResponse>();
            if (notes != null)
            {
                foreach (var note in notes)
                {
                    if (note == null) continue;
                    entries.Add(new MapNoteResponse
                    {
                        worldId = worldId,
                        noteId = note.NoteId,
                        x = note.X,
                        z = note.Z,
                        text = note.Text,
                        createdAtUtc = note.CreatedAtUtc,
                        updatedAtUtc = note.UpdatedAtUtc,
                    });
                }
            }
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/worlds/{worldId}/map-notes",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(new MapNoteListResponse { notes = entries.ToArray() }));
            await SendAsync(request, cancellationToken);
        }

        private static List<StoredInventorySlot> ToStoredSlots(InventorySlotResponse[] slots)
        {
            var result = new List<StoredInventorySlot>();
            if (slots == null) return result;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                result.Add(new StoredInventorySlot
                {
                    SlotIndex = slot.slotIndex,
                    ItemId = slot.itemId,
                    Quantity = slot.quantity,
                    Condition = slot.condition,
                    Quality = slot.quality,
                    HiddenItemId = slot.hiddenItemId,
                    SourceNodeId = slot.sourceNodeId,
                    RevealAtPercent = slot.revealAtPercent,
                });
            }

            return result;
        }

        private static List<StoredDepositKnowledge> ToStoredKnowledge(
            DepositKnowledgeResponse[] knowledge)
        {
            var result = new List<StoredDepositKnowledge>();
            if (knowledge == null) return result;
            foreach (var entry in knowledge)
            {
                if (entry == null) continue;
                result.Add(new StoredDepositKnowledge
                {
                    WorldId = entry.worldId,
                    InstanceId = entry.instanceId,
                    StudyBasisPoints = entry.studyBasisPoints,
                    DiscoveredAtUtc = entry.discoveredAtUtc,
                });
            }
            return result;
        }

        private static List<StoredMapNote> ToStoredMapNotes(MapNoteResponse[] notes)
        {
            var result = new List<StoredMapNote>();
            if (notes == null) return result;
            foreach (var note in notes)
            {
                if (note == null) continue;
                result.Add(new StoredMapNote
                {
                    WorldId = note.worldId,
                    NoteId = note.noteId,
                    X = note.x,
                    Z = note.z,
                    Text = note.text,
                    CreatedAtUtc = note.createdAtUtc,
                    UpdatedAtUtc = note.updatedAtUtc,
                });
            }
            return result;
        }

        private async Task<string> SendAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.SetRequestHeader("X-Quieter-Internal-Token", token);
            }

            request.timeout = 10;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Profile service returned {request.responseCode}: {request.error} {request.downloadHandler?.text}");
            }

            return request.downloadHandler?.text ?? string.Empty;
        }

        private static UnityWebRequest CreateJsonRequest(string url, string method, string json)
        {
            return new UnityWebRequest(url, method)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            }.WithJsonHeader();
        }

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.UtcNow;
        }
    }

    internal static class UnityWebRequestExtensions
    {
        public static UnityWebRequest WithJsonHeader(this UnityWebRequest request)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }
    }
}
