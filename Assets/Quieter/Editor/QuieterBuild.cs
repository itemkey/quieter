using System;
using System.IO;
using Quieter.Core;
using Quieter.Networking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Quieter.Editor
{
    public static class QuieterBuild
    {
        private static readonly string[] Scenes = { "Assets/Scenes/SampleScene.unity" };

        [MenuItem("Tools/Quieter/Build/Windows Development Client")]
        public static void BuildWindowsDevelopmentClient()
        {
            QuieterProjectSetup.ConfigureProject();
            ApplySteamOverride();
            ApplyBuildMetadata();
            var outputRoot = GetOutputRoot();
            var executable = Path.Combine(outputRoot, "WindowsClient", "Quieter.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            ApplyEndpointOverride();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = executable,
                target = BuildTarget.StandaloneWindows64,
                options = IsProductionBuild() ? BuildOptions.None : BuildOptions.Development,
            });
            EnsureSucceeded(report);
            var steam = Resources.Load<SteamSettings>("Quieter/SteamSettings");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(executable), "steam_appid.txt"),
                steam.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        [MenuItem("Tools/Quieter/Build/Linux Dedicated Server")]
        public static void BuildLinuxDedicatedServer()
        {
            QuieterProjectSetup.ConfigureProject();
            ApplySteamOverride();
            ApplyBuildMetadata();
            var outputRoot = GetOutputRoot();
            var executable = Path.Combine(outputRoot, "LinuxServer", "QuieterServer");
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = executable,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = IsProductionBuild() ? BuildOptions.None : BuildOptions.Development,
            });
            EnsureSucceeded(report);
        }

        public static void BuildWindowsFromCommandLine() => BuildWindowsDevelopmentClient();
        public static void BuildLinuxServerFromCommandLine() => BuildLinuxDedicatedServer();

        private static string GetOutputRoot()
        {
            var configured = Environment.GetEnvironmentVariable("QUIETER_BUILD_OUTPUT");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"))
                : Path.GetFullPath(configured);
        }

        private static void ApplyEndpointOverride()
        {
            var host = Environment.GetEnvironmentVariable("QUIETER_SERVER_HOST");
            var portText = Environment.GetEnvironmentVariable("QUIETER_SERVER_PORT");
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            var endpoint = Resources.Load<ServerEndpoint>("Quieter/ServerEndpoint");
            if (endpoint == null)
            {
                throw new BuildFailedException("ServerEndpoint asset is missing.");
            }

            var port = ushort.TryParse(portText, out var parsed)
                ? parsed
                : QuieterConstants.DefaultGamePort;
            endpoint.Configure(endpoint.DisplayName, host, port);
            var caPath = Environment.GetEnvironmentVariable("QUIETER_DTLS_CA_FILE");
            if (!string.IsNullOrWhiteSpace(caPath))
            {
                if (!File.Exists(caPath))
                {
                    throw new BuildFailedException($"DTLS CA file does not exist: {caPath}");
                }

                endpoint.ConfigureSecurity(
                    true,
                    Environment.GetEnvironmentVariable("QUIETER_DTLS_SERVER_NAME"),
                    File.ReadAllText(caPath));
            }
            EditorUtility.SetDirty(endpoint);
            AssetDatabase.SaveAssets();
        }

        private static void ApplySteamOverride()
        {
            var settings = Resources.Load<SteamSettings>("Quieter/SteamSettings");
            if (settings == null)
            {
                throw new BuildFailedException("SteamSettings asset is missing.");
            }

            var appIdText = Environment.GetEnvironmentVariable("QUIETER_STEAM_APP_ID");
            var appId = uint.TryParse(appIdText, out var parsed)
                ? parsed
                : QuieterConstants.DevelopmentSteamAppId;
            var production = string.Equals(
                Environment.GetEnvironmentVariable("QUIETER_PRODUCTION_BUILD"),
                "1",
                StringComparison.Ordinal);
            settings.Configure(appId, production);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void ApplyBuildMetadata()
        {
            var version = Environment.GetEnvironmentVariable("QUIETER_BUILD_VERSION");
            if (!string.IsNullOrWhiteSpace(version))
            {
                PlayerSettings.bundleVersion = version;
            }
        }

        private static void EnsureSucceeded(BuildReport report)
        {
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Build failed with {report.summary.totalErrors} errors. "
                    + "For Linux builds install the Unity Linux Build Support module.");
            }
        }

        private static bool IsProductionBuild()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("QUIETER_PRODUCTION_BUILD"),
                "1",
                StringComparison.Ordinal);
        }
    }

    public sealed class QuieterBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var steam = Resources.Load<SteamSettings>("Quieter/SteamSettings");
            if (steam == null)
            {
                throw new BuildFailedException("SteamSettings asset is missing.");
            }

            if (steam.ProductionBuild && steam.AppId == QuieterConstants.DevelopmentSteamAppId)
            {
                throw new BuildFailedException("Production builds cannot use Spacewar App ID 480.");
            }

            if (steam.ProductionBuild && report.summary.platform == BuildTarget.StandaloneWindows64)
            {
                var endpoint = Resources.Load<ServerEndpoint>("Quieter/ServerEndpoint");
                if (endpoint == null || !endpoint.UseDtls
                    || string.IsNullOrWhiteSpace(endpoint.PinnedCaCertificate))
                {
                    throw new BuildFailedException(
                        "Production client requires DTLS and a pinned CA certificate.");
                }
            }
        }
    }
}
