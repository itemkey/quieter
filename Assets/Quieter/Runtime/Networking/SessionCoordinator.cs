using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Core;
using Quieter.Persistence;
using Quieter.Player;
using Quieter.World;
using Quieter.Survival;
using Quieter.Inventory;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Quieter.Networking
{
    public sealed class SessionCoordinator : MonoBehaviour
    {
        private const string AuthenticationMessage = "quieter.auth.v1";
        private const string WorldBootstrapMessage = "quieter.world.v1";
        private const string RejectionMessage = "quieter.reject.v1";
        private const int MaxTicketBytes = 2048;

        private sealed class AuthenticatedClient
        {
            public ulong ClientId;
            public ulong SteamId;
            public string DisplayName;
            public NetworkPlayer Player;
            public PlayerInventory Inventory;
            public PlayerResourceInteraction ResourceInteraction;
            public PlayerSurvival Survival;
            public Task FinalSaveTask;
        }

        private sealed class PlayerSaveSnapshot
        {
            public ulong SteamId;
            public long Revision;
            public Vector3 Position;
            public List<StoredInventorySlot> Slots;
            public List<StoredInventorySlot> PendingItems;
            public byte SelectedHotbarIndex;
            public Task KnowledgeSaveTask;
            public CharacterSurvivalState Survival;
        }

        private readonly Dictionary<ulong, double> pendingClients = new();
        private readonly Dictionary<ulong, AuthenticatedClient> authenticatedClients = new();
        private readonly Dictionary<ulong, AuthenticatedClient> offlineBodies = new();
        private readonly Dictionary<ulong, ulong> authenticatingSteamIds = new();
        private readonly HashSet<ulong> authenticatingClients = new();
        private readonly Dictionary<ulong, SemaphoreSlim> playerSaveGates = new();
        private readonly Dictionary<ulong, long> playerSaveRevisions = new();
        private readonly Dictionary<ulong, long> persistedPlayerSaveRevisions = new();
        private readonly HashSet<Task> pendingFinalSaveTasks = new();
        private readonly CancellationTokenSource lifetime = new();

        private NetworkManager networkManager;
        private UnityTransport transport;
        private GameObject playerPrefab;
        private WorldStreamer worldStreamer;
        private ResourceWorldService resourceWorld;
        private PlacedObjectWorldService placedObjects;
        private WorldObjectCatalog worldObjectCatalog;
        private ItemCatalog itemCatalog;
        private GameObject worldItemPrefab;
        private IClientAuthenticationProvider clientAuthentication;
        private IServerAuthenticationProvider serverAuthentication;
        private IWorldRepository worldRepository;
        private IPlayerProfileRepository playerRepository;
        private ClientAuthenticationPayload preparedClientPayload;
        private WorldDefinition worldDefinition;
        private bool hasPreparedClientPayload;
        private bool authenticationSent;
        private bool shuttingDown;
        private bool shutdownSaveCompleted;
        private bool messagesRegistered;
        private float nextPositionSaveAt;
        private string lastRejection = string.Empty;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool developmentLootSpawned;
        private Task developmentHostStartTask;
#endif

        public event Action<string> StatusChanged;
        public event Action<bool> GameplayStateChanged;

        public bool IsClientConnected => networkManager != null && networkManager.IsConnectedClient;
        public bool IsServerRunning => networkManager != null && networkManager.IsServer;
        public bool IsAuthenticationReady => clientAuthentication?.IsReady ?? false;
        public string AuthenticationStatus => clientAuthentication?.Status ?? serverAuthentication?.Status ?? string.Empty;
        public ResourceWorldService ResourceWorld => resourceWorld;
        public PlacedObjectWorldService PlacedObjects => placedObjects;

        public void Configure(
            NetworkManager manager,
            UnityTransport unityTransport,
            GameObject networkPlayerPrefab,
            WorldStreamer streamer,
            ResourceWorldService resources,
            PlacedObjectWorldService placedObjectService,
            WorldObjectCatalog catalog,
            IClientAuthenticationProvider clientAuth,
            IServerAuthenticationProvider serverAuth,
            IWorldRepository worlds,
            IPlayerProfileRepository players,
            ItemCatalog items,
            GameObject networkWorldItemPrefab)
        {
            networkManager = manager;
            transport = unityTransport;
            playerPrefab = networkPlayerPrefab;
            worldStreamer = streamer;
            resourceWorld = resources;
            placedObjects = placedObjectService;
            worldObjectCatalog = catalog;
            clientAuthentication = clientAuth;
            serverAuthentication = serverAuth;
            worldRepository = worlds;
            playerRepository = players;
            itemCatalog = items;
            worldItemPrefab = networkWorldItemPrefab;

            networkManager.NetworkConfig.TickRate = QuieterConstants.ServerTickRate;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.ConnectionApprovalCallback = ApprovalCheck;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnServerStarted += OnServerStarted;
            Application.wantsToQuit += OnWantsToQuit;
            resourceWorld.Configure(networkManager, worldStreamer);
            placedObjects.Configure(networkManager);
        }

        public async Task StartServerAsync(ushort port)
        {
            ChangeStatus("Загрузка постоянного мира...");
            worldDefinition = await worldRepository.GetOrCreateWorldAsync(lifetime.Token);
            FindAnyObjectByType<WorldWeatherService>()?.Initialize(worldDefinition.Seed);
            if (worldDefinition.GeneratorVersion != QuieterConstants.GeneratorVersion)
            {
                throw new InvalidOperationException(
                    $"Server world generator {worldDefinition.GeneratorVersion} does not match build {QuieterConstants.GeneratorVersion}.");
            }

            await resourceWorld.InitializeServerAsync(worldDefinition, worldRepository, lifetime.Token);
            await placedObjects.InitializeServerAsync(worldDefinition, worldRepository, lifetime.Token);
            worldStreamer.Initialize(worldDefinition, worldObjectCatalog, true, false);
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            if (!TransportSecurityConfigurator.ConfigureServerFromEnvironment(transport))
            {
                throw new InvalidOperationException(
                    "Выделенный сервер требует сертификат и закрытый ключ DTLS.");
            }

            if (!networkManager.StartServer())
            {
                throw new InvalidOperationException("Не удалось запустить выделенный сервер.");
            }

            RegisterMessages();
        }

        public Task StartHostAsync(ServerEndpoint endpoint)
        {
            return StartHostAsync(endpoint.Address, endpoint.Port);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public Task StartDevelopmentHostAsync()
        {
            if (networkManager.IsClient || networkManager.IsServer)
            {
                return Task.CompletedTask;
            }

            if (developmentHostStartTask != null && !developmentHostStartTask.IsCompleted)
            {
                return developmentHostStartTask;
            }

            developmentHostStartTask = StartDevelopmentHostCoreAsync();
            return developmentHostStartTask;
        }

        private async Task StartDevelopmentHostCoreAsync()
        {
            var previousClient = clientAuthentication;
            previousClient?.Dispose();
            if (serverAuthentication != null && !ReferenceEquals(previousClient, serverAuthentication))
            {
                serverAuthentication.Dispose();
            }

            var development = new DevelopmentAuthenticationProvider(
                DevelopmentAuthenticationProvider.StableLocalHostId);
            clientAuthentication = development;
            serverAuthentication = development;
            var port = FindAvailableDevelopmentPort(QuieterConstants.DefaultGamePort);
            ChangeStatus(port == QuieterConstants.DefaultGamePort
                ? "Запуск локального тестового мира..."
                : $"Порт {QuieterConstants.DefaultGamePort} занят. Локальный тест запустится на порту {port}...");
            await StartHostAsync("127.0.0.1", port);
        }

        public static ushort FindAvailableDevelopmentPort(ushort preferredPort)
        {
            if (CanBindUdpPort(preferredPort))
            {
                return preferredPort;
            }

            using var socket = CreateExclusiveUdpSocket();
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
        }

        private static bool CanBindUdpPort(ushort port)
        {
            try
            {
                using var socket = CreateExclusiveUdpSocket();
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static Socket CreateExclusiveUdpSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true,
            };
            return socket;
        }
#endif

        private async Task StartHostAsync(string address, ushort port)
        {
            preparedClientPayload = await clientAuthentication.CreatePayloadAsync(lifetime.Token);
            hasPreparedClientPayload = true;
            authenticationSent = false;
            worldDefinition = await worldRepository.GetOrCreateWorldAsync(lifetime.Token);
            FindAnyObjectByType<WorldWeatherService>()?.Initialize(worldDefinition.Seed);
            await resourceWorld.InitializeServerAsync(worldDefinition, worldRepository, lifetime.Token);
            await placedObjects.InitializeServerAsync(worldDefinition, worldRepository, lifetime.Token);
            worldStreamer.Initialize(worldDefinition, worldObjectCatalog, true, true);
            transport.SetConnectionData(address, port, "0.0.0.0");
            SetConnectionHello();
            if (!networkManager.StartHost())
            {
                networkManager.Shutdown();
                throw new InvalidOperationException("Не удалось запустить локальный хост.");
            }

            RegisterMessages();
        }

        public async Task ConnectAsync(ServerEndpoint endpoint)
        {
            if (networkManager.IsClient || networkManager.IsServer)
            {
                return;
            }

            lastRejection = string.Empty;
            ChangeStatus("Получение билета Steam...");
            preparedClientPayload = await clientAuthentication.CreatePayloadAsync(lifetime.Token);
            hasPreparedClientPayload = true;
            authenticationSent = false;
            transport.SetConnectionData(endpoint.Address, endpoint.Port);
            if (!TransportSecurityConfigurator.ConfigureClient(transport, endpoint))
            {
                throw new InvalidOperationException("Не удалось настроить защищённое соединение DTLS.");
            }

            SetConnectionHello();
            ChangeStatus($"Подключение к {endpoint.DisplayName}...");
            if (!networkManager.StartClient())
            {
                throw new InvalidOperationException("Клиент не смог начать подключение.");
            }

            RegisterMessages();
        }

        public void Disconnect()
        {
            if (networkManager != null && (networkManager.IsClient || networkManager.IsServer))
            {
                networkManager.Shutdown();
            }

            GameplayStateChanged?.Invoke(false);
            ChangeStatus("Отключено");
        }

        private void Update()
        {
            clientAuthentication?.Tick();
            serverAuthentication?.Tick();

            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            var now = networkManager.ServerTime.Time;
            var expired = ListPool<ulong>.Get();
            foreach (var pair in pendingClients)
            {
                if (now - pair.Value > QuieterConstants.AuthenticationTimeoutSeconds)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (var clientId in expired)
            {
                RejectAndDisconnect(clientId, "Steam не подтвердил вход вовремя.");
            }

            ListPool<ulong>.Release(expired);

            if (Time.unscaledTime >= nextPositionSaveAt)
            {
                nextPositionSaveAt = Time.unscaledTime + QuieterConstants.PositionSaveIntervalSeconds;
                _ = SaveAllPlayerStateAsync(lifetime.Token);
            }
        }

        private void ApprovalCheck(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            response.CreatePlayerObject = false;

            if (authenticatedClients.Count + pendingClients.Count >= QuieterConstants.DefaultMaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Сервер заполнен.";
                return;
            }

            var compatibility = ConnectionCompatibility.Validate(
                request.Payload,
                QuieterConstants.ProtocolVersion,
                worldDefinition.GeneratorVersion);
            if (!compatibility.Accepted)
            {
                response.Approved = false;
                response.Reason = compatibility.Error;
                return;
            }

            response.Approved = true;
        }

        private void OnServerStarted()
        {
            nextPositionSaveAt = Time.unscaledTime + QuieterConstants.PositionSaveIntervalSeconds;
            ChangeStatus($"Сервер запущен на UDP {transport.ConnectionData.Port}");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void SpawnDevelopmentLoot(Vector3 playerPosition, float playerYaw)
        {
            if (developmentLootSpawned || worldItemPrefab == null || itemCatalog == null
                || !networkManager.IsServer)
            {
                return;
            }

            developmentLootSpawned = true;
            var occupied = new List<Vector3>(3);
            SpawnDevelopmentStack(1, 8, playerPosition, playerYaw, 0, occupied);
            SpawnDevelopmentStack(2, 10, playerPosition, playerYaw, 1, occupied);
            SpawnDevelopmentStack(3, 4, playerPosition, playerYaw, 2, occupied);
            Debug.Log(
                $"[Quieter] Тестовые ресурсы созданы рядом с игроком у "
                + $"X {playerPosition.x:0.0}, Z {playerPosition.z:0.0}.");
        }

        private void SpawnDevelopmentStack(
            ushort itemId,
            int quantity,
            Vector3 playerPosition,
            float playerYaw,
            int stackIndex,
            List<Vector3> occupied)
        {
            if (!itemCatalog.TryGetItem(itemId, out _)) return;
            var position = ResolveDevelopmentLootPosition(
                playerPosition,
                playerYaw,
                stackIndex,
                occupied);
            occupied.Add(position);
            var instance = Instantiate(worldItemPrefab, position, Quaternion.identity);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(true);
            instance.GetComponent<NetworkWorldItem>().InitializeServer(itemId, quantity);
        }

        private Vector3 ResolveDevelopmentLootPosition(
            Vector3 playerPosition,
            float playerYaw,
            int stackIndex,
            IReadOnlyList<Vector3> occupied)
        {
            var lastCandidate = playerPosition;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var planar = GetDevelopmentLootPlanarPosition(
                    playerPosition,
                    playerYaw,
                    stackIndex,
                    attempt);
                var terrainHeight = worldStreamer.SampleHeight(planar.x, planar.y);
                var rayOriginY = Mathf.Max(playerPosition.y + 8f, terrainHeight + 10f);
                var surfaceHeight = terrainHeight;
                if (Physics.Raycast(
                    new Vector3(planar.x, rayOriginY, planar.y),
                    Vector3.down,
                    out var hit,
                    30f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    surfaceHeight = hit.point.y;
                }

                lastCandidate = new Vector3(planar.x, surfaceHeight + 0.42f, planar.y);
                if (IsDevelopmentLootPositionClear(lastCandidate, occupied))
                {
                    return lastCandidate;
                }
            }

            return lastCandidate;
        }

        private static bool IsDevelopmentLootPositionClear(
            Vector3 position,
            IReadOnlyList<Vector3> occupied)
        {
            foreach (var existing in occupied)
            {
                if ((existing - position).sqrMagnitude < 0.75f * 0.75f) return false;
            }

            var colliders = Physics.OverlapBox(
                position + Vector3.up * 0.28f,
                new Vector3(0.36f, 0.24f, 0.36f),
                Quaternion.identity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (var collider in colliders)
            {
                if (collider is TerrainCollider) continue;
                if (collider is MeshCollider
                    && collider.GetComponent<WorldChunkView>() != null) continue;
                return false;
            }

            return true;
        }

        public static Vector2 GetDevelopmentLootPlanarPosition(
            Vector3 playerPosition,
            float playerYaw,
            int stackIndex,
            int attempt = 0)
        {
            var lateral = stackIndex switch
            {
                0 => -1.15f,
                2 => 1.15f,
                _ => 0f,
            };
            var forward = (stackIndex == 1 ? 2.65f : 2.35f) + Mathf.Max(0, attempt) * 1.05f;
            var worldOffset = Quaternion.Euler(0f, playerYaw, 0f)
                * new Vector3(lateral, 0f, forward);
            return new Vector2(playerPosition.x + worldOffset.x, playerPosition.z + worldOffset.z);
        }
#endif

        private void OnClientConnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                pendingClients[clientId] = networkManager.ServerTime.Time;
            }

            if (networkManager.IsClient && clientId == networkManager.LocalClientId)
            {
                _ = SendLocalAuthenticationAsync(clientId);
            }
        }

        private async Task SendLocalAuthenticationAsync(ulong clientId)
        {
            // StartHost invokes OnClientConnected synchronously. Waiting one turn lets
            // StartHostAsync register the named message handlers before the host sends
            // its authentication payload to itself.
            await Task.Yield();
            if (authenticationSent || !networkManager.IsClient
                || clientId != networkManager.LocalClientId)
            {
                return;
            }

            if (!hasPreparedClientPayload)
            {
                RejectLocal("Клиент не подготовил билет авторизации.");
                return;
            }

            authenticationSent = true;
            ChangeStatus("Проверка авторизации...");
            SendAuthentication(preparedClientPayload);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            pendingClients.Remove(clientId);
            authenticatingClients.Remove(clientId);
            if (authenticatingSteamIds.Remove(clientId, out var authenticatingSteamId))
            {
                serverAuthentication?.EndSession(authenticatingSteamId);
            }
            if (authenticatedClients.Remove(clientId, out var authenticated))
            {
                serverAuthentication?.EndSession(authenticated.SteamId);
                var playerSave = authenticated.FinalSaveTask;
                if (authenticated.Player != null)
                {
                    authenticated.Survival?.ServerSetOffline(true);
                    offlineBodies[authenticated.SteamId] = authenticated;
                    var snapshot = CapturePlayerSaveSnapshot(
                        authenticated, lifetime.Token, true);
                    playerSave = SavePlayerSnapshotAsync(snapshot, lifetime.Token);
                    authenticated.FinalSaveTask = playerSave;
                }
                playerSave ??= Task.CompletedTask;
                TrackFinalSave(CompleteDisconnectedSaveAsync(playerSave, lifetime.Token));
            }

            if (networkManager.IsClient && clientId == networkManager.LocalClientId)
            {
                authenticationSent = false;
                GameplayStateChanged?.Invoke(false);
                var reason = !string.IsNullOrWhiteSpace(lastRejection)
                    ? lastRejection
                    : networkManager.DisconnectReason;
                ChangeStatus(string.IsNullOrWhiteSpace(reason) ? "Соединение закрыто" : reason);
            }
        }

        private void RegisterMessages()
        {
            if (messagesRegistered)
            {
                return;
            }

            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                AuthenticationMessage,
                OnAuthenticationMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                WorldBootstrapMessage,
                OnWorldBootstrapMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                RejectionMessage,
                OnRejectionMessage);
            messagesRegistered = true;
        }

        private void SendAuthentication(ClientAuthenticationPayload payload)
        {
            var safeName = new FixedString64Bytes(
                string.IsNullOrWhiteSpace(payload.DisplayName) ? "Steam Player" : payload.DisplayName);
            var ticketLength = (ushort)Mathf.Min(payload.Ticket.Length, MaxTicketBytes);
            using var writer = new FastBufferWriter(4096, Allocator.Temp);
            writer.WriteValueSafe(payload.SteamId);
            writer.WriteValueSafe(safeName);
            writer.WriteValueSafe(ticketLength);
            for (var index = 0; index < ticketLength; index++)
            {
                writer.WriteValueSafe(payload.Ticket[index]);
            }

            networkManager.CustomMessagingManager.SendNamedMessage(
                AuthenticationMessage,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void OnAuthenticationMessage(ulong clientId, FastBufferReader reader)
        {
            if (!networkManager.IsServer || !pendingClients.ContainsKey(clientId)
                || !authenticatingClients.Add(clientId))
            {
                return;
            }

            reader.ReadValueSafe(out ulong claimedSteamId);
            reader.ReadValueSafe(out FixedString64Bytes displayName);
            reader.ReadValueSafe(out ushort ticketLength);
            if (ticketLength == 0 || ticketLength > MaxTicketBytes
                || !reader.TryBeginRead(ticketLength))
            {
                RejectAndDisconnect(clientId, "Некорректный Steam-билет.");
                return;
            }

            var ticket = new byte[ticketLength];
            for (var index = 0; index < ticketLength; index++)
            {
                reader.ReadValueSafe(out ticket[index]);
            }

            authenticatingSteamIds[clientId] = claimedSteamId;

            serverAuthentication.Validate(
                claimedSteamId,
                ticket,
                result => _ = FinishAuthenticationAsync(clientId, displayName.ToString(), result));
        }

        private async Task FinishAuthenticationAsync(
            ulong clientId,
            string displayName,
            ServerAuthenticationResult authentication)
        {
            authenticatingClients.Remove(clientId);
            authenticatingSteamIds.Remove(clientId);
            if (!authentication.Success)
            {
                RejectAndDisconnect(clientId, authentication.Error);
                return;
            }

            if (!pendingClients.ContainsKey(clientId))
            {
                serverAuthentication.EndSession(authentication.SteamId);
                return;
            }

            try
            {
                var spawn = new Vector3(0f, worldStreamer.SampleHeight(0f, 0f) + 2f, 0f);
                var profile = await playerRepository.LoginAsync(
                    authentication.SteamId,
                    displayName,
                    spawn,
                    lifetime.Token);
                offlineBodies.TryGetValue(authentication.SteamId, out var offlineBody);
                if (offlineBody != null
                    && offlineBody.Player != null)
                {
                    profile.Position = offlineBody.Player.transform.position;
                    profile.InventorySlots = offlineBody.Inventory?.CreateStoredSlotsSnapshot()
                        ?? profile.InventorySlots;
                    profile.PendingItems = offlineBody.Inventory?.CreateStoredPendingItemsSnapshot()
                        ?? profile.PendingItems;
                    profile.SelectedHotbarIndex = offlineBody.Inventory?.GetServerSelectedHotbarIndex()
                        ?? profile.SelectedHotbarIndex;
                    profile.Survival = offlineBody.Survival?.CreateServerSnapshot()
                        ?? profile.Survival;
                }
                if (!pendingClients.ContainsKey(clientId))
                {
                    serverAuthentication.EndSession(authentication.SteamId);
                    return;
                }
                if (offlineBody != null)
                {
                    offlineBodies.Remove(authentication.SteamId);
                }

                var position = ValidateSpawn(profile.Position, spawn);
                worldStreamer.EnsureLoadedAround(position);
                var instance = Instantiate(playerPrefab, position, Quaternion.identity);
                var networkObject = instance.GetComponent<NetworkObject>();
                networkObject.DontDestroyWithOwner = true;
                networkObject.SpawnAsPlayerObject(clientId, true);
                var player = instance.GetComponent<NetworkPlayer>();
                player.AssignServerIdentity(authentication.SteamId, profile.DisplayName, position);
                var playerSurvival = instance.GetComponent<PlayerSurvival>();
                if (playerSurvival == null)
                {
                    throw new InvalidOperationException("Network player prefab has no PlayerSurvival.");
                }
                playerSurvival.InitializeServer(
                    profile.Survival,
                    authentication.SteamId,
                    profile.DisplayName);
                playerSurvival.ServerSetOffline(false);
                var playerInventory = instance.GetComponent<PlayerInventory>();
                if (playerInventory == null)
                {
                    throw new InvalidOperationException("Network player prefab has no PlayerInventory.");
                }
                playerInventory.InitializeServer(
                    profile.InventorySlots,
                    profile.PendingItems,
                    profile.SelectedHotbarIndex,
                    authentication.SteamId);
                var resourceInteraction = instance.GetComponent<PlayerResourceInteraction>();
                if (resourceInteraction == null)
                {
                    throw new InvalidOperationException(
                        "Network player prefab has no PlayerResourceInteraction.");
                }
                resourceInteraction.InitializeServer(
                    profile.DepositKnowledge,
                    profile.MapNotes,
                    authentication.SteamId,
                    worldDefinition.WorldId,
                    playerRepository);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                SpawnDevelopmentLoot(position, player.transform.eulerAngles.y);
#endif
                player.ServerDespawning += OnServerPlayerDespawning;
                playerSurvival.ServerDied += OnServerPlayerDied;
                pendingClients.Remove(clientId);
                authenticatedClients[clientId] = new AuthenticatedClient
                {
                    ClientId = clientId,
                    SteamId = authentication.SteamId,
                    DisplayName = profile.DisplayName,
                    Player = player,
                    Inventory = playerInventory,
                    ResourceInteraction = resourceInteraction,
                    Survival = playerSurvival,
                };
                if (offlineBody?.Player != null && offlineBody.Player.NetworkObject.IsSpawned)
                {
                    offlineBody.Player.ServerDespawning -= OnServerPlayerDespawning;
                    if (offlineBody.Survival != null)
                    {
                        offlineBody.Survival.ServerDied -= OnServerPlayerDied;
                    }
                    offlineBody.Player.NetworkObject.Despawn(true);
                }
                SendWorldBootstrap(clientId);
                resourceWorld.SendSnapshot(clientId);
                placedObjects.SendSnapshot(clientId);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                serverAuthentication.EndSession(authentication.SteamId);
                RejectAndDisconnect(clientId, "Не удалось загрузить профиль игрока.");
            }
        }

        private void SendWorldBootstrap(ulong clientId)
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(worldDefinition.WorldId);
            writer.WriteValueSafe(worldDefinition.Seed);
            writer.WriteValueSafe(worldDefinition.GeneratorVersion);
            writer.WriteValueSafe(worldDefinition.ChunkCountX);
            writer.WriteValueSafe(worldDefinition.ChunkCountZ);
            writer.WriteValueSafe(worldDefinition.ChunkSize);
            writer.WriteValueSafe(worldDefinition.SamplesPerSide);
            writer.WriteValueSafe(worldDefinition.HeightStep);
            networkManager.CustomMessagingManager.SendNamedMessage(
                WorldBootstrapMessage,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }

        private void OnWorldBootstrapMessage(ulong clientId, FastBufferReader reader)
        {
            if (!networkManager.IsClient)
            {
                return;
            }

            var definition = new WorldDefinition();
            reader.ReadValueSafe(out definition.WorldId);
            reader.ReadValueSafe(out definition.Seed);
            reader.ReadValueSafe(out definition.GeneratorVersion);
            reader.ReadValueSafe(out definition.ChunkCountX);
            reader.ReadValueSafe(out definition.ChunkCountZ);
            reader.ReadValueSafe(out definition.ChunkSize);
            reader.ReadValueSafe(out definition.SamplesPerSide);
            reader.ReadValueSafe(out definition.HeightStep);
            if (definition.GeneratorVersion != QuieterConstants.GeneratorVersion)
            {
                RejectLocal("Клиент не поддерживает генератор мира сервера.");
                return;
            }

            worldDefinition = definition;
            FindAnyObjectByType<WorldWeatherService>()?.Initialize(worldDefinition.Seed);
            resourceWorld.InitializeClient(definition);
            placedObjects.InitializeClient(definition);
            worldStreamer.Initialize(
                definition,
                worldObjectCatalog,
                networkManager.IsServer,
                true);
            GameplayStateChanged?.Invoke(true);
            ChangeStatus("В мире");
        }

        private void RejectAndDisconnect(ulong clientId, string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? "Авторизация отклонена." : reason;
            if (networkManager.IsServer && networkManager.ConnectedClients.ContainsKey(clientId))
            {
                using var writer = new FastBufferWriter(512, Allocator.Temp);
                var message = new FixedString512Bytes(reason);
                writer.WriteValueSafe(message);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    RejectionMessage,
                    clientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
                StartCoroutine(DisconnectAfterMessage(clientId));
            }

            pendingClients.Remove(clientId);
            authenticatingClients.Remove(clientId);
            if (authenticatingSteamIds.Remove(clientId, out var steamId))
            {
                serverAuthentication?.EndSession(steamId);
            }
        }

        private IEnumerator DisconnectAfterMessage(ulong clientId)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (networkManager != null && networkManager.IsServer)
            {
                networkManager.DisconnectClient(clientId, "Authentication rejected");
            }
        }

        private void OnRejectionMessage(ulong clientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out FixedString512Bytes reason);
            lastRejection = reason.ToString();
            ChangeStatus(lastRejection);
        }

        private void RejectLocal(string reason)
        {
            lastRejection = reason;
            ChangeStatus(reason);
            Disconnect();
        }

        private Vector3 ValidateSpawn(Vector3 requested, Vector3 fallback)
        {
            if (!IsFinite(requested.x) || !IsFinite(requested.y)
                || !IsFinite(requested.z))
            {
                return fallback;
            }

            requested = worldStreamer.ClampToWorld(requested);
            var ground = worldStreamer.SampleHeight(requested.x, requested.z);
            if (requested.y < ground + 0.5f || requested.y > ground + 100f)
            {
                requested.y = ground + 2f;
            }

            return requested;
        }

        private async Task SaveAllPlayerStateAsync(CancellationToken cancellationToken)
        {
            var clients = new List<AuthenticatedClient>(authenticatedClients.Values);
            clients.AddRange(offlineBodies.Values);
            foreach (var client in clients)
            {
                if (client.Player != null)
                {
                    try
                    {
                        await SavePlayerStateAsync(client, cancellationToken, false);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Could not save {client.SteamId}: {exception.Message}");
                    }
                }
            }
        }

        private async Task SavePlayerStateAsync(
            AuthenticatedClient client,
            CancellationToken cancellationToken,
            bool normalizeTemporaryStorage)
        {
            if (client?.Player == null) return;
            var gate = GetPlayerSaveGate(client.SteamId);
            var acquired = false;
            PlayerSaveSnapshot snapshot = null;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                snapshot = CapturePlayerSaveSnapshot(
                    client, cancellationToken, normalizeTemporaryStorage);
                if (snapshot != null)
                {
                    await PersistPlayerSnapshotAsync(snapshot, cancellationToken);
                }
            }
            finally
            {
                if (acquired) gate.Release();
                if (snapshot?.KnowledgeSaveTask != null)
                {
                    await snapshot.KnowledgeSaveTask;
                }
            }
        }

        private async Task SavePlayerSnapshotAsync(
            PlayerSaveSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (snapshot == null) return;
            var gate = GetPlayerSaveGate(snapshot.SteamId);
            var acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                acquired = true;
                await PersistPlayerSnapshotAsync(snapshot, cancellationToken);
            }
            finally
            {
                if (acquired) gate.Release();
                if (snapshot.KnowledgeSaveTask != null)
                {
                    await snapshot.KnowledgeSaveTask;
                }
            }
        }

        private async Task PersistPlayerSnapshotAsync(
            PlayerSaveSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (persistedPlayerSaveRevisions.TryGetValue(
                    snapshot.SteamId, out var persistedRevision)
                && persistedRevision >= snapshot.Revision)
            {
                return;
            }
            if (playerRepository is IAtomicPlayerProfileRepository atomicRepository)
            {
                await atomicRepository.SaveSnapshotAsync(
                    snapshot.SteamId, snapshot.Position, snapshot.Slots, snapshot.PendingItems,
                    snapshot.SelectedHotbarIndex, snapshot.Survival, cancellationToken);
            }
            else
            {
                await playerRepository.SavePositionAsync(
                    snapshot.SteamId, snapshot.Position, cancellationToken);
                await playerRepository.SaveInventoryAsync(
                    snapshot.SteamId, snapshot.Slots, snapshot.PendingItems,
                    snapshot.SelectedHotbarIndex, cancellationToken);
                await playerRepository.SaveSurvivalAsync(
                    snapshot.SteamId, snapshot.Survival, cancellationToken);
            }
            persistedPlayerSaveRevisions[snapshot.SteamId] = snapshot.Revision;
        }

        private PlayerSaveSnapshot CapturePlayerSaveSnapshot(
            AuthenticatedClient client,
            CancellationToken cancellationToken,
            bool normalizeTemporaryStorage)
        {
            if (client?.Player == null) return null;
            var slots = client.Inventory == null
                ? new List<StoredInventorySlot>()
                : normalizeTemporaryStorage
                    ? client.Inventory.PrepareAndCreateStoredSlots()
                    : client.Inventory.CreateStoredSlotsSnapshot();
            var selected = client.Inventory?.GetServerSelectedHotbarIndex() ?? (byte)0;
            var pendingItems = client.Inventory?.CreateStoredPendingItemsSnapshot()
                ?? new List<StoredInventorySlot>();
            playerSaveRevisions.TryGetValue(client.SteamId, out var revision);
            revision = System.Math.Max(
                revision,
                client.Survival?.ServerState?.Revision ?? 0);
            revision++;
            playerSaveRevisions[client.SteamId] = revision;
            var survival = client.Survival?.CreateServerSnapshot();
            if (survival != null)
            {
                survival.Revision = revision;
            }
            return new PlayerSaveSnapshot
            {
                SteamId = client.SteamId,
                Revision = revision,
                Position = client.Player.transform.position,
                Slots = slots,
                PendingItems = pendingItems,
                SelectedHotbarIndex = selected,
                Survival = survival,
                KnowledgeSaveTask = client.ResourceInteraction != null
                    ? client.ResourceInteraction.FlushKnowledgeAsync(cancellationToken)
                    : Task.CompletedTask,
            };
        }

        private SemaphoreSlim GetPlayerSaveGate(ulong steamId)
        {
            if (playerSaveGates.TryGetValue(steamId, out var gate)) return gate;
            gate = new SemaphoreSlim(1, 1);
            playerSaveGates[steamId] = gate;
            return gate;
        }

        private async Task CompleteDisconnectedSaveAsync(
            Task playerSave,
            CancellationToken cancellationToken)
        {
            try
            {
                await playerSave;
            }
            finally
            {
                try
                {
                    if (resourceWorld != null)
                    {
                        await resourceWorld.FlushAsync(cancellationToken);
                    }
                }
                finally
                {
                    if (placedObjects != null)
                    {
                        await placedObjects.FlushAsync(cancellationToken);
                    }
                }
            }
        }

        private void TrackFinalSave(Task task)
        {
            if (task == null || !pendingFinalSaveTasks.Add(task)) return;
            _ = ObserveFinalSaveAsync(task);
        }

        private async Task ObserveFinalSaveAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Final disconnect save did not finish: {exception.Message}");
            }
            finally
            {
                pendingFinalSaveTasks.Remove(task);
            }
        }

        private void SetConnectionHello()
        {
            networkManager.NetworkConfig.ConnectionData = ConnectionCompatibility.CreatePayload(
                QuieterConstants.ProtocolVersion,
                QuieterConstants.GeneratorVersion);
        }

        private void ChangeStatus(string status)
        {
            Debug.Log($"[Quieter] {status}");
            StatusChanged?.Invoke(status);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnServerPlayerDespawning(NetworkPlayer player)
        {
            foreach (var client in authenticatedClients.Values)
            {
                if (client.Player != player)
                {
                    continue;
                }

                player.ServerDespawning -= OnServerPlayerDespawning;
                if (client.Survival != null)
                {
                    client.Survival.ServerDied -= OnServerPlayerDied;
                }
                var snapshot = CapturePlayerSaveSnapshot(client, lifetime.Token, true);
                client.FinalSaveTask = SavePlayerSnapshotAsync(snapshot, lifetime.Token);
                TrackFinalSave(client.FinalSaveTask);
                client.Player = null;
                break;
            }
        }

        private void OnServerPlayerDied(CharacterSurvivalState deceased)
        {
            foreach (var client in authenticatedClients.Values)
            {
                if (client.Survival == null || !client.Survival.ServerIsDead
                    || client.Survival.ServerState?.CharacterId != deceased?.CharacterId)
                    continue;
                TrackFinalSave(SavePlayerStateAsync(client, lifetime.Token, true));
                return;
            }
            foreach (var client in offlineBodies.Values)
            {
                if (client.Survival == null || !client.Survival.ServerIsDead
                    || client.Survival.ServerState?.CharacterId != deceased?.CharacterId)
                    continue;
                TrackFinalSave(SavePlayerStateAsync(client, lifetime.Token, true));
                return;
            }
        }

        private bool OnWantsToQuit()
        {
            if (networkManager == null || !networkManager.IsServer || shutdownSaveCompleted)
            {
                return true;
            }

            if (!shuttingDown)
            {
                shuttingDown = true;
                _ = SaveBeforeQuitAsync();
            }

            return false;
        }

        private async Task SaveBeforeQuitAsync()
        {
            var clients = new List<AuthenticatedClient>(authenticatedClients.Values);
            clients.AddRange(offlineBodies.Values);
            foreach (var client in clients)
            {
                try
                {
                    if (client.Player != null)
                    {
                        await SavePlayerStateAsync(client, CancellationToken.None, true);
                    }
                    else if (client.FinalSaveTask != null)
                    {
                        await client.FinalSaveTask;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Final player save for {client.SteamId} did not finish: {exception.Message}");
                }
            }

            var pendingFinalSaves = new List<Task>(pendingFinalSaveTasks);
            if (pendingFinalSaves.Count > 0)
            {
                try
                {
                    await Task.WhenAll(pendingFinalSaves);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"A disconnected player's final save did not finish: {exception.Message}");
                }
            }
            try
            {
                if (resourceWorld != null)
                {
                    await resourceWorld.FlushAsync(CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Final resource-node save did not finish: {exception.Message}");
            }
            try
            {
                if (placedObjects != null)
                {
                    await placedObjects.FlushAsync(CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Final placed-object save did not finish: {exception.Message}");
            }

            shutdownSaveCompleted = true;
            Application.Quit();
        }

        private void OnDestroy()
        {
            lifetime.Cancel();
            Application.wantsToQuit -= OnWantsToQuit;
            lifetime.Dispose();
            clientAuthentication?.Dispose();
            serverAuthentication?.Dispose();
            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= OnClientConnected;
                networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                networkManager.OnServerStarted -= OnServerStarted;
            }
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
