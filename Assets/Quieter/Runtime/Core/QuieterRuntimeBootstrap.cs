using System;
using System.Threading.Tasks;
using Quieter.Networking;
using Quieter.Persistence;
using Quieter.UI;
using Quieter.World;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Quieter.Core
{
    [DefaultExecutionOrder(-10000)]
    public sealed class QuieterRuntimeBootstrap : MonoBehaviour
    {
        public static QuieterRuntimeBootstrap Instance { get; private set; }

        public SessionCoordinator Session { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            if (Instance != null)
            {
                return;
            }

            var runtime = new GameObject("QuieterRuntime");
            DontDestroyOnLoad(runtime);
            runtime.AddComponent<QuieterRuntimeBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 1f / QuieterConstants.MovementSimulationRate;
        }

        private async void Start()
        {
            if (Instance != this)
            {
                return;
            }

            try
            {
                await InitializeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
#if !UNITY_SERVER
                MainMenuView.ShowFatalError(exception.Message);
#else
                Application.Quit(1);
#endif
            }
        }

        private async Task InitializeAsync()
        {
            var arguments = RuntimeArguments.Parse(Environment.GetCommandLineArgs());
            var endpoint = Resources.Load<ServerEndpoint>("Quieter/ServerEndpoint");
            var steamSettings = Resources.Load<SteamSettings>("Quieter/SteamSettings");
            var catalog = Resources.Load<WorldObjectCatalog>("Quieter/WorldObjectCatalog");
            var playerPrefab = Resources.Load<GameObject>("Quieter/NetworkPlayer");
            if (endpoint == null || steamSettings == null || catalog == null || playerPrefab == null)
            {
                throw new InvalidOperationException(
                    "Quieter resources are missing. Run Tools > Quieter > Configure Project.");
            }

            if (steamSettings.ProductionBuild
                && steamSettings.AppId == QuieterConstants.DevelopmentSteamAppId)
            {
                throw new InvalidOperationException("Production build cannot use Steam App ID 480.");
            }

            DisableTemplateCamera();
            var networkObject = new GameObject("NetworkRuntime");
            DontDestroyOnLoad(networkObject);
            var transport = networkObject.AddComponent<UnityTransport>();
            var networkManager = networkObject.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = QuieterConstants.ServerTickRate,
            };
            if (!networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab }))
            {
                throw new InvalidOperationException("Netcode rejected the network player prefab.");
            }

            var worldObject = new GameObject("ProceduralWorld");
            worldObject.transform.SetParent(transform, false);
            var worldStreamer = worldObject.AddComponent<WorldStreamer>();

            IWorldRepository worldRepository;
            IPlayerProfileRepository playerRepository;
            if (!string.IsNullOrWhiteSpace(arguments.ProfileServiceUrl))
            {
                var remote = new HttpProfileRepository(
                    arguments.ProfileServiceUrl,
                    arguments.ProfileServiceToken);
                worldRepository = remote;
                playerRepository = remote;
            }
            else
            {
                var local = new LocalJsonRepository();
                worldRepository = local;
                playerRepository = local;
            }

            IClientAuthenticationProvider clientAuthentication = null;
            IServerAuthenticationProvider serverAuthentication = null;
            var developmentAuth = arguments.UseDevelopmentAuthentication || arguments.IsHost;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (arguments.IsServer)
            {
                serverAuthentication = developmentAuth
                    ? new DevelopmentAuthenticationProvider()
                    : new SteamServerAuthenticationProvider(arguments.Port);
            }
            else if (arguments.IsHost)
            {
                var development = new DevelopmentAuthenticationProvider();
                clientAuthentication = development;
                serverAuthentication = development;
            }
            else
            {
                clientAuthentication = developmentAuth
                    ? new DevelopmentAuthenticationProvider()
                    : new SteamClientAuthenticationProvider(steamSettings.AppId);
            }
#else
            if (developmentAuth)
            {
                throw new InvalidOperationException(
                    "Тестовая авторизация исключена из обычной сборки.");
            }

            if (arguments.IsServer)
            {
                serverAuthentication = new SteamServerAuthenticationProvider(arguments.Port);
            }
            else
            {
                clientAuthentication = new SteamClientAuthenticationProvider(steamSettings.AppId);
            }
#endif

            Session = gameObject.AddComponent<SessionCoordinator>();
            Session.Configure(
                networkManager,
                transport,
                playerPrefab,
                worldStreamer,
                catalog,
                clientAuthentication,
                serverAuthentication,
                worldRepository,
                playerRepository);

            if (arguments.IsServer)
            {
                if (serverAuthentication == null || !serverAuthentication.IsReady)
                {
                    throw new InvalidOperationException(serverAuthentication?.Status ?? "Server authentication is missing.");
                }

                await Session.StartServerAsync(arguments.Port);
                return;
            }

            if (arguments.IsHost)
            {
                await Session.StartHostAsync(endpoint);
                return;
            }

            var menu = gameObject.AddComponent<MainMenuView>();
            menu.Initialize(Session, endpoint);
        }

        private static void DisableTemplateCamera()
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (var camera in cameras)
            {
                camera.enabled = false;
                var listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }
    }
}
