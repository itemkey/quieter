using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using Quieter.Inventory;
using Quieter.Survival;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Quieter.Persistence
{
    public sealed class HttpProfileRepository : IWorldRepository, IPersistentCharacterRepository,
        IInheritanceRepository
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
            public InventorySlotResponse[] pendingItems;
            public byte selectedHotbarIndex;
            public DepositKnowledgeResponse[] depositKnowledge;
            public MapNoteResponse[] mapNotes;
            public string survivalJson;
            public string characterId;
            public long survivalRevision;
            public string registeredHeirCharacterId;
            public long estateRevision;
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
            public string sampleId;
            public string itemInstanceId;
            public ushort freshness = 10000;
            public ushort biologicalContamination;
            public ushort toxinContamination;
            public ushort wetness;
            public ushort cleanliness = 10000;
            public ushort liquidMilliliters;
            public byte liquidKind;
            public bool equipped;
        }

        [Serializable]
        private sealed class InventoryRequest
        {
            public byte selectedHotbarIndex;
            public InventorySlotResponse[] slots;
            public InventorySlotResponse[] pendingItems;
        }

        [Serializable]
        private sealed class PositionRequest
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class SurvivalRequest
        {
            public string survivalJson;
            public long revision;
        }

        [Serializable]
        private sealed class SnapshotRequest
        {
            public string characterId;
            public PositionRequest position;
            public InventoryRequest inventory;
            public SurvivalRequest survival;
        }

        [Serializable]
        private sealed class WorldCharacterListResponse
        {
            public ProfileResponse[] characters;
        }

        [Serializable]
        private sealed class NewStrangerRequest
        {
            public string operationId;
            public string previousCharacterId;
            public long expectedRevision;
            public PositionRequest position;
        }

        [Serializable]
        private sealed class CharacterPairSnapshotRequest
        {
            public string operationId;
            public SnapshotRequest source;
            public string destinationSteamId;
            public SnapshotRequest destination;
        }

        [Serializable]
        private sealed class CreateWorldNpcRequest
        {
            public string name;
            public SnapshotRequest snapshot;
        }

        [Serializable]
        private sealed class RegisterHeirRequest
        {
            public string heirCharacterId;
            public long expectedEstateRevision;
        }

        [Serializable]
        private sealed class AssumeHeirRequest
        {
            public string operationId;
            public string deceasedCharacterId;
            public long expectedRevision;
        }

        [Serializable]
        private sealed class CreateHeirOfferRequest
        {
            public string operationId;
            public string recipientSteamId;
            public string deceasedCharacterId;
            public long expectedDonorEstateRevision;
        }

        [Serializable]
        private sealed class AcceptHeirOfferRequest
        {
            public string operationId;
            public string deceasedCharacterId;
            public long expectedRevision;
        }

        [Serializable]
        private sealed class HeirOfferResponse
        {
            public string offerId;
            public string donorSteamId;
            public string donorDisplayName;
            public string recipientSteamId;
            public string deceasedCharacterId;
            public string heirCharacterId;
            public string heirName;
            public string offeredAtUtc;
            public string acceptanceStartedAtUtc;
            public string expiresAtUtc;
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
        private sealed class PlacedObjectResponse
        {
            public string objectId;
            public string ownerAccountId;
            public string assignedCharacterId;
            public ushort itemId;
            public float x;
            public float y;
            public float z;
            public float yaw;
            public bool locked;
            public InventorySlotResponse input;
            public string createdAtUtc;
            public string updatedAtUtc;
        }

        [Serializable]
        private sealed class PlacedObjectListResponse
        {
            public PlacedObjectResponse[] objects;
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
            public string mapItemInstanceId;
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
            return ToProfile(response);
        }

        public async Task<IReadOnlyList<PlayerProfile>> LoadWorldCharactersAsync(
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(baseUrl + "/internal/world/characters");
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<WorldCharacterListResponse>(responseText);
            var result = new List<PlayerProfile>();
            if (response?.characters == null) return result;
            foreach (var character in response.characters)
            {
                if (character != null) result.Add(ToProfile(character));
            }
            return result;
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

        public async Task<IReadOnlyList<StoredPlacedObject>> LoadPlacedObjectsAsync(
            int worldId,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(
                $"{baseUrl}/internal/worlds/{worldId}/placed-objects");
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<PlacedObjectListResponse>(responseText);
            var result = new List<StoredPlacedObject>();
            if (response?.objects == null) return result;
            foreach (var entry in response.objects)
            {
                if (entry == null) continue;
                result.Add(new StoredPlacedObject
                {
                    WorldId = worldId,
                    ObjectId = entry.objectId,
                    OwnerAccountId = entry.ownerAccountId,
                    AssignedCharacterId = entry.assignedCharacterId,
                    ItemId = entry.itemId,
                    X = entry.x,
                    Y = entry.y,
                    Z = entry.z,
                    Yaw = entry.yaw,
                    Locked = entry.locked,
                    Input = ToStoredSlot(entry.input),
                    CreatedAtUtc = entry.createdAtUtc,
                    UpdatedAtUtc = entry.updatedAtUtc,
                });
            }
            return result;
        }

        public async Task SavePlacedObjectsAsync(
            int worldId,
            IReadOnlyList<StoredPlacedObject> objects,
            CancellationToken cancellationToken = default)
        {
            var entries = new List<PlacedObjectResponse>();
            if (objects != null)
            {
                foreach (var entry in objects)
                {
                    if (entry == null) continue;
                    entries.Add(new PlacedObjectResponse
                    {
                        objectId = entry.ObjectId,
                        ownerAccountId = entry.OwnerAccountId,
                        assignedCharacterId = entry.AssignedCharacterId,
                        itemId = entry.ItemId,
                        x = entry.X,
                        y = entry.Y,
                        z = entry.Z,
                        yaw = entry.Yaw,
                        locked = entry.Locked,
                        input = ToSlotResponse(entry.Input),
                        createdAtUtc = entry.CreatedAtUtc,
                        updatedAtUtc = entry.UpdatedAtUtc,
                    });
                }
            }
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/worlds/{worldId}/placed-objects",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(new PlacedObjectListResponse { objects = entries.ToArray() }));
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
            IReadOnlyList<StoredInventorySlot> pendingItems,
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
                        sampleId = slot.SampleId,
                        itemInstanceId = slot.ItemInstanceId,
                        freshness = slot.Freshness,
                        biologicalContamination = slot.BiologicalContamination,
                        toxinContamination = slot.ToxinContamination,
                        wetness = slot.Wetness,
                        cleanliness = slot.Cleanliness,
                        liquidMilliliters = slot.LiquidMilliliters,
                        liquidKind = slot.LiquidKind,
                        equipped = slot.Equipped,
                    });
                }
            }

            var payload = new InventoryRequest
            {
                selectedHotbarIndex = selectedHotbarIndex,
                slots = payloadSlots.ToArray(),
                pendingItems = ToSlotResponses(pendingItems),
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
                        mapItemInstanceId = note.MapItemInstanceId,
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

        public async Task SaveSnapshotAsync(
            ulong steamId, Vector3 position,
            IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems,
            byte selectedHotbarIndex, CharacterSurvivalState survival,
            CancellationToken cancellationToken = default)
        {
            if (survival == null) throw new ArgumentNullException(nameof(survival));
            var payload = new SnapshotRequest
            {
                characterId = survival.CharacterId,
                position = new PositionRequest { x = position.x, y = position.y, z = position.z },
                inventory = new InventoryRequest
                {
                    selectedHotbarIndex = selectedHotbarIndex,
                    slots = ToSlotResponses(slots),
                    pendingItems = ToSlotResponses(pendingItems),
                },
                survival = new SurvivalRequest
                {
                    survivalJson = JsonUtility.ToJson(survival), revision = survival.Revision,
                },
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/snapshot",
                UnityWebRequest.kHttpVerbPUT, JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        public async Task SaveDetachedCharacterAsync(
            Vector3 position, IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems, byte selectedHotbarIndex,
            CharacterSurvivalState survival, CancellationToken cancellationToken = default)
        {
            if (survival == null || string.IsNullOrWhiteSpace(survival.CharacterId))
                throw new ArgumentException("Detached character identity is required.", nameof(survival));
            var payload = CreateSnapshotPayload(
                position, slots, pendingItems, selectedHotbarIndex, survival);
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/world/characters/{survival.CharacterId}/snapshot",
                UnityWebRequest.kHttpVerbPUT, JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        public async Task<PlayerProfile> CreateNewStrangerAsync(
            ulong steamId, string operationId, string previousCharacterId,
            long expectedRevision, Vector3 spawn,
            CancellationToken cancellationToken = default)
        {
            var payload = new NewStrangerRequest
            {
                operationId = operationId,
                previousCharacterId = previousCharacterId,
                expectedRevision = expectedRevision,
                position = new PositionRequest { x = spawn.x, y = spawn.y, z = spawn.z },
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/new-stranger",
                UnityWebRequest.kHttpVerbPOST, JsonUtility.ToJson(payload));
            return ToProfile(JsonUtility.FromJson<ProfileResponse>(
                await SendAsync(request, cancellationToken)));
        }

        public async Task SaveCharacterPairAsync(
            string operationId, CharacterPersistenceSnapshot source,
            ulong destinationSteamId, CharacterPersistenceSnapshot destination,
            CancellationToken cancellationToken = default)
        {
            if (source?.Survival == null || destination?.Survival == null)
                throw new ArgumentException("Both character snapshots are required.");
            var payload = new CharacterPairSnapshotRequest
            {
                operationId = operationId,
                source = CreateSnapshotPayload(source.Position, source.Slots, source.PendingItems,
                    source.SelectedHotbarIndex, source.Survival),
                destinationSteamId = destinationSteamId.ToString(),
                destination = CreateSnapshotPayload(destination.Position, destination.Slots,
                    destination.PendingItems, destination.SelectedHotbarIndex, destination.Survival),
            };
            var json = JsonUtility.ToJson(payload);
            Exception lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var request = CreateJsonRequest(
                        baseUrl + "/internal/world/character-transfer",
                        UnityWebRequest.kHttpVerbPOST, json);
                    await SendAsync(request, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { lastError = exception; }
            }
            throw new InvalidOperationException(
                "Character transfer could not be confirmed after idempotent retries.", lastError);
        }

        public async Task<PlayerProfile> CreateWorldNpcAsync(
            string name, CharacterPersistenceSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (snapshot?.Survival == null)
                throw new ArgumentException("NPC snapshot is missing.", nameof(snapshot));
            var payload = new CreateWorldNpcRequest
            {
                name = name,
                snapshot = CreateSnapshotPayload(
                    snapshot.Position, snapshot.Slots, snapshot.PendingItems,
                    snapshot.SelectedHotbarIndex, snapshot.Survival),
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/world/npcs", UnityWebRequest.kHttpVerbPOST,
                JsonUtility.ToJson(payload));
            return ToProfile(JsonUtility.FromJson<ProfileResponse>(
                await SendAsync(request, cancellationToken)));
        }

        public async Task<PlayerProfile> RegisterHeirAsync(
            ulong steamId, string heirCharacterId, long expectedEstateRevision,
            CancellationToken cancellationToken = default)
        {
            var payload = new RegisterHeirRequest
            {
                heirCharacterId = heirCharacterId,
                expectedEstateRevision = expectedEstateRevision,
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/heir",
                UnityWebRequest.kHttpVerbPUT, JsonUtility.ToJson(payload));
            return ToProfile(JsonUtility.FromJson<ProfileResponse>(
                await SendAsync(request, cancellationToken)));
        }

        public async Task<PlayerProfile> AssumeRegisteredHeirAsync(
            ulong steamId, string operationId, string deceasedCharacterId,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            var payload = new AssumeHeirRequest
            {
                operationId = operationId,
                deceasedCharacterId = deceasedCharacterId,
                expectedRevision = expectedRevision,
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/assume-heir",
                UnityWebRequest.kHttpVerbPOST, JsonUtility.ToJson(payload));
            return ToProfile(JsonUtility.FromJson<ProfileResponse>(
                await SendAsync(request, cancellationToken)));
        }

        public async Task<PendingHeirOffer> OfferRegisteredHeirAsync(
            ulong donorSteamId,
            ulong recipientSteamId,
            string operationId,
            string deceasedCharacterId,
            long expectedDonorEstateRevision,
            CancellationToken cancellationToken = default)
        {
            var payload = new CreateHeirOfferRequest
            {
                operationId = operationId,
                recipientSteamId = recipientSteamId.ToString(),
                deceasedCharacterId = deceasedCharacterId,
                expectedDonorEstateRevision = expectedDonorEstateRevision,
            };
            var json = JsonUtility.ToJson(payload);
            Exception lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = CreateJsonRequest(
                    $"{baseUrl}/internal/players/{donorSteamId}/heir-offers",
                    UnityWebRequest.kHttpVerbPOST, json);
                try
                {
                    return ToPendingHeirOffer(JsonUtility.FromJson<HeirOfferResponse>(
                        await SendAsync(request, cancellationToken)));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { lastError = exception; }
            }
            throw new InvalidOperationException(
                "Heir donation could not be confirmed after idempotent retries.", lastError);
        }

        public async Task<PendingHeirOffer> GetPendingHeirOfferAsync(
            ulong recipientSteamId,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(
                $"{baseUrl}/internal/players/{recipientSteamId}/heir-offer");
            var response = await SendAsync(request, cancellationToken);
            return string.IsNullOrWhiteSpace(response)
                ? null
                : ToPendingHeirOffer(JsonUtility.FromJson<HeirOfferResponse>(response));
        }

        public async Task<PlayerProfile> AcceptHeirOfferAsync(
            ulong recipientSteamId,
            string offerId,
            string operationId,
            string deceasedCharacterId,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            var payload = new AcceptHeirOfferRequest
            {
                operationId = operationId,
                deceasedCharacterId = deceasedCharacterId,
                expectedRevision = expectedRevision,
            };
            var json = JsonUtility.ToJson(payload);
            Exception lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = CreateJsonRequest(
                    $"{baseUrl}/internal/players/{recipientSteamId}/heir-offers/{offerId}/accept",
                    UnityWebRequest.kHttpVerbPOST, json);
                try
                {
                    return ToProfile(JsonUtility.FromJson<ProfileResponse>(
                        await SendAsync(request, cancellationToken)));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { lastError = exception; }
            }
            throw new InvalidOperationException(
                "Heir acceptance could not be confirmed after idempotent retries.", lastError);
        }

        public async Task SaveSurvivalAsync(
            ulong steamId,
            CharacterSurvivalState survival,
            CancellationToken cancellationToken = default)
        {
            if (survival == null) return;
            var payload = new SurvivalRequest
            {
                survivalJson = JsonUtility.ToJson(survival),
                revision = survival.Revision,
            };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/survival",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        private static CharacterSurvivalState ParseSurvival(
            string json,
            string characterId,
            long revision)
        {
            CharacterSurvivalState result = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    result = JsonUtility.FromJson<CharacterSurvivalState>(json);
                }
                catch (Exception)
                {
                    result = null;
                }
            }
            result ??= new CharacterSurvivalState();
            result.EnsureInitialized();
            if (!string.IsNullOrWhiteSpace(characterId))
            {
                result.CharacterId = characterId;
            }
            result.Revision = Math.Max(result.Revision, revision);
            return result;
        }

        private static PlayerProfile ToProfile(ProfileResponse response)
        {
            if (response == null) throw new InvalidOperationException("Profile response is empty.");
            return new PlayerProfile
            {
                SteamId = ulong.TryParse(response.steamId, out var steamId) ? steamId : 0,
                DisplayName = response.displayName,
                Position = new Vector3(response.positionX, response.positionY, response.positionZ),
                CreatedAtUtc = ParseDate(response.createdAtUtc),
                LastSeenAtUtc = ParseDate(response.lastSeenAtUtc),
                InventorySlots = ToStoredSlots(response.inventorySlots),
                PendingItems = ToStoredSlots(response.pendingItems),
                SelectedHotbarIndex = response.selectedHotbarIndex,
                DepositKnowledge = ToStoredKnowledge(response.depositKnowledge),
                MapNotes = ToStoredMapNotes(response.mapNotes),
                Survival = ParseSurvival(response.survivalJson, response.characterId, response.survivalRevision),
                RegisteredHeirCharacterId = response.registeredHeirCharacterId,
                EstateRevision = response.estateRevision,
            };
        }

        private static PendingHeirOffer ToPendingHeirOffer(HeirOfferResponse response)
        {
            if (response == null) return null;
            return new PendingHeirOffer
            {
                OfferId = response.offerId,
                DonorSteamId = ulong.TryParse(response.donorSteamId, out var donor) ? donor : 0,
                DonorDisplayName = response.donorDisplayName,
                RecipientSteamId = ulong.TryParse(response.recipientSteamId, out var recipient)
                    ? recipient : 0,
                DeceasedCharacterId = response.deceasedCharacterId,
                HeirCharacterId = response.heirCharacterId,
                HeirName = response.heirName,
                OfferedAtUtc = ParseDate(response.offeredAtUtc),
                AcceptanceStartedAtUtc = ParseDate(response.acceptanceStartedAtUtc),
                ExpiresAtUtc = ParseDate(response.expiresAtUtc),
            };
        }

        private static SnapshotRequest CreateSnapshotPayload(
            Vector3 position, IReadOnlyList<StoredInventorySlot> slots,
            IReadOnlyList<StoredInventorySlot> pendingItems, byte selectedHotbarIndex,
            CharacterSurvivalState survival) => new()
        {
            characterId = survival.CharacterId,
            position = new PositionRequest { x = position.x, y = position.y, z = position.z },
            inventory = new InventoryRequest
            {
                selectedHotbarIndex = selectedHotbarIndex,
                slots = ToSlotResponses(slots), pendingItems = ToSlotResponses(pendingItems),
            },
            survival = new SurvivalRequest
            {
                survivalJson = JsonUtility.ToJson(survival), revision = survival.Revision,
            },
        };

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
                    SampleId = slot.sampleId,
                    ItemInstanceId = slot.itemInstanceId,
                    Freshness = slot.freshness,
                    BiologicalContamination = slot.biologicalContamination,
                    ToxinContamination = slot.toxinContamination,
                    Wetness = slot.wetness,
                    Cleanliness = slot.cleanliness,
                    LiquidMilliliters = slot.liquidMilliliters,
                    LiquidKind = slot.liquidKind,
                    Equipped = slot.equipped,
                });
            }

            return result;
        }

        private static StoredInventorySlot ToStoredSlot(InventorySlotResponse slot) => slot == null
            ? null
            : new StoredInventorySlot
            {
                SlotIndex = slot.slotIndex,
                ItemId = slot.itemId,
                Quantity = slot.quantity,
                Condition = slot.condition,
                Quality = slot.quality,
                HiddenItemId = slot.hiddenItemId,
                SourceNodeId = slot.sourceNodeId,
                RevealAtPercent = slot.revealAtPercent,
                SampleId = slot.sampleId,
                ItemInstanceId = slot.itemInstanceId,
                Freshness = slot.freshness,
                BiologicalContamination = slot.biologicalContamination,
                ToxinContamination = slot.toxinContamination,
                Wetness = slot.wetness,
                Cleanliness = slot.cleanliness,
                LiquidMilliliters = slot.liquidMilliliters,
                LiquidKind = slot.liquidKind,
                Equipped = slot.equipped,
            };

        private static InventorySlotResponse[] ToSlotResponses(
            IReadOnlyList<StoredInventorySlot> slots)
        {
            if (slots == null) return Array.Empty<InventorySlotResponse>();
            var result = new List<InventorySlotResponse>(slots.Count);
            foreach (var slot in slots)
            {
                var converted = ToSlotResponse(slot);
                if (converted != null) result.Add(converted);
            }
            return result.ToArray();
        }

        private static InventorySlotResponse ToSlotResponse(StoredInventorySlot slot) => slot == null
            ? null
            : new InventorySlotResponse
            {
                slotIndex = slot.SlotIndex,
                itemId = slot.ItemId,
                quantity = slot.Quantity,
                condition = slot.Condition,
                quality = slot.Quality,
                hiddenItemId = slot.HiddenItemId,
                sourceNodeId = slot.SourceNodeId,
                revealAtPercent = slot.RevealAtPercent,
                sampleId = slot.SampleId,
                itemInstanceId = slot.ItemInstanceId,
                freshness = slot.Freshness,
                biologicalContamination = slot.BiologicalContamination,
                toxinContamination = slot.ToxinContamination,
                wetness = slot.Wetness,
                cleanliness = slot.Cleanliness,
                liquidMilliliters = slot.LiquidMilliliters,
                liquidKind = slot.LiquidKind,
                equipped = slot.Equipped,
            };

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
                    MapItemInstanceId = note.mapItemInstanceId,
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
