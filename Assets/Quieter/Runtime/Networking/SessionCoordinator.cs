using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Core;
using Quieter.Persistence;
using Quieter.Player;
using Quieter.World;
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
        }

        private readonly Dictionary<ulong, double> pendingClients = new();
        private readonly Dictionary<ulong, AuthenticatedClient> authenticatedClients = new();
        private readonly Dictionary<ulong, ulong> authenticatingSteamIds = new();
        private readonly HashSet<ulong> authenticatingClients = new();
        private readonly CancellationTokenSource lifetime = new();

        private NetworkManager networkManager;
        private UnityTransport transport;
        private GameObject playerPrefab;
        private WorldStreamer worldStreamer;
        private WorldObjectCatalog worldObjectCatalog;
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

        public event Action<string> StatusChanged;
        public event Action<bool> GameplayStateChanged;

        public bool IsClientConnected => networkManager != null && networkManager.IsConnectedClient;
        public bool IsServerRunning => networkManager != null && networkManager.IsServer;
        public bool IsAuthenticationReady => clientAuthentication?.IsReady ?? false;
        public string AuthenticationStatus => clientAuthentication?.Status ?? serverAuthentication?.Status ?? string.Empty;

        public void Configure(
            NetworkManager manager,
            UnityTransport unityTransport,
            GameObject networkPlayerPrefab,
            WorldStreamer streamer,
            WorldObjectCatalog catalog,
            IClientAuthenticationProvider clientAuth,
            IServerAuthenticationProvider serverAuth,
            IWorldRepository worlds,
            IPlayerProfileRepository players)
        {
            networkManager = manager;
            transport = unityTransport;
            playerPrefab = networkPlayerPrefab;
            worldStreamer = streamer;
            worldObjectCatalog = catalog;
            clientAuthentication = clientAuth;
            serverAuthentication = serverAuth;
            worldRepository = worlds;
            playerRepository = players;

            networkManager.NetworkConfig.TickRate = QuieterConstants.ServerTickRate;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.ConnectionApprovalCallback = ApprovalCheck;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnServerStarted += OnServerStarted;
            Application.wantsToQuit += OnWantsToQuit;
        }

        public async Task StartServerAsync(ushort port)
        {
            ChangeStatus("Загрузка постоянного мира...");
            worldDefinition = await worldRepository.GetOrCreateWorldAsync(lifetime.Token);
            if (worldDefinition.GeneratorVersion != QuieterConstants.GeneratorVersion)
            {
                throw new InvalidOperationException(
                    $"Server world generator {worldDefinition.GeneratorVersion} does not match build {QuieterConstants.GeneratorVersion}.");
            }

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
        public async Task StartDevelopmentHostAsync()
        {
            if (networkManager.IsClient || networkManager.IsServer)
            {
                return;
            }

            var previousClient = clientAuthentication;
            previousClient?.Dispose();
            if (serverAuthentication != null && !ReferenceEquals(previousClient, serverAuthentication))
            {
                serverAuthentication.Dispose();
            }

            var development = new DevelopmentAuthenticationProvider();
            clientAuthentication = development;
            serverAuthentication = development;
            ChangeStatus("Запуск локального тестового мира...");
            await StartHostAsync("127.0.0.1", QuieterConstants.DefaultGamePort);
        }
#endif

        private async Task StartHostAsync(string address, ushort port)
        {
            preparedClientPayload = await clientAuthentication.CreatePayloadAsync(lifetime.Token);
            hasPreparedClientPayload = true;
            authenticationSent = false;
            worldDefinition = await worldRepository.GetOrCreateWorldAsync(lifetime.Token);
            worldStreamer.Initialize(worldDefinition, worldObjectCatalog, true, true);
            transport.SetConnectionData(address, port, "0.0.0.0");
            SetConnectionHello();
            if (!networkManager.StartHost())
            {
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
                _ = SaveAllPositionsAsync(lifetime.Token);
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
                if (authenticated.Player != null)
                {
                    _ = playerRepository.SavePositionAsync(
                        authenticated.SteamId,
                        authenticated.Player.transform.position,
                        lifetime.Token);
                }
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
                if (!pendingClients.ContainsKey(clientId))
                {
                    serverAuthentication.EndSession(authentication.SteamId);
                    return;
                }

                var position = ValidateSpawn(profile.Position, spawn);
                worldStreamer.EnsureLoadedAround(position);
                var instance = Instantiate(playerPrefab, position, Quaternion.identity);
                var networkObject = instance.GetComponent<NetworkObject>();
                networkObject.SpawnAsPlayerObject(clientId, true);
                var player = instance.GetComponent<NetworkPlayer>();
                player.AssignServerIdentity(authentication.SteamId, profile.DisplayName, position);
                player.ServerDespawning += OnServerPlayerDespawning;
                pendingClients.Remove(clientId);
                authenticatedClients[clientId] = new AuthenticatedClient
                {
                    ClientId = clientId,
                    SteamId = authentication.SteamId,
                    DisplayName = profile.DisplayName,
                    Player = player,
                };
                SendWorldBootstrap(clientId);
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

        private async Task SaveAllPositionsAsync(CancellationToken cancellationToken)
        {
            var clients = new List<AuthenticatedClient>(authenticatedClients.Values);
            foreach (var client in clients)
            {
                if (client.Player != null)
                {
                    try
                    {
                        await playerRepository.SavePositionAsync(
                            client.SteamId,
                            client.Player.transform.position,
                            cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Could not save {client.SteamId}: {exception.Message}");
                    }
                }
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
                client.Player = null;
                _ = playerRepository.SavePositionAsync(
                    client.SteamId,
                    player.transform.position,
                    lifetime.Token);
                break;
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await SaveAllPositionsAsync(timeout.Token);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Final position save did not finish: {exception.Message}");
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
