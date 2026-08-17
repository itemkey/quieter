using System.IO;
using Quieter.Core;
using Quieter.Networking;
using Quieter.Player;
using Quieter.World;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Quieter.Editor
{
    [InitializeOnLoad]
    public static class QuieterProjectSetup
    {
        private const string ResourceFolder = "Assets/Resources/Quieter";
        private const string PlayerPrefabPath = ResourceFolder + "/NetworkPlayer.prefab";

        static QuieterProjectSetup()
        {
            EditorApplication.delayCall += ConfigureIfNeeded;
        }

        [MenuItem("Tools/Quieter/Configure Project")]
        public static void ConfigureProject()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Quieter");
            CreateEndpoint();
            CreateSteamSettings();
            CreateWorldCatalog();
            CreatePlayerPrefab();

            PlayerSettings.companyName = "Quieter";
            PlayerSettings.productName = "Quieter";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            Time.fixedDeltaTime = 1f / QuieterConstants.MovementSimulationRate;
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true),
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Quieter project resources are configured.");
        }

        public static void ConfigureFromCommandLine()
        {
            ConfigureProject();
        }

        private static void ConfigureIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ConfigureIfNeeded;
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                ConfigureProject();
            }
        }

        private static void CreateEndpoint()
        {
            const string path = ResourceFolder + "/ServerEndpoint.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ServerEndpoint>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ServerEndpoint>();
                asset.Configure("Домашний сервер Quieter", "127.0.0.1", 7777);
                AssetDatabase.CreateAsset(asset, path);
            }
        }

        private static void CreateSteamSettings()
        {
            const string path = ResourceFolder + "/SteamSettings.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SteamSettings>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SteamSettings>();
                asset.Configure(480, false);
                AssetDatabase.CreateAsset(asset, path);
            }
        }

        private static void CreateWorldCatalog()
        {
            const string path = ResourceFolder + "/WorldObjectCatalog.asset";
            var asset = AssetDatabase.LoadAssetAtPath<WorldObjectCatalog>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<WorldObjectCatalog>();
                asset.ConfigureDefaults();
                AssetDatabase.CreateAsset(asset, path);
            }
        }

        private static void CreatePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                return;
            }

            var root = new GameObject("NetworkPlayer");
            try
            {
                var controller = root.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.38f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                controller.stepOffset = 0.35f;
                controller.slopeLimit = 55f;
                controller.skinWidth = 0.08f;
                controller.minMoveDistance = 0f;
                root.AddComponent<NetworkObject>();
                var player = root.AddComponent<NetworkPlayer>();

                var presentation = GameObject.CreatePrimitive(PrimitiveType.Cube);
                presentation.name = "Presentation";
                Object.DestroyImmediate(presentation.GetComponent<BoxCollider>());
                presentation.transform.SetParent(root.transform, false);
                presentation.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                presentation.transform.localScale = new Vector3(0.8f, 1.8f, 0.8f);
                var renderer = presentation.GetComponent<MeshRenderer>();
                var material = new Material(
                    Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Hidden/InternalErrorShader"))
                {
                    color = new Color(0.2f, 0.55f, 0.85f),
                    name = "PlayerPlaceholderMaterial",
                };
                const string materialPath = ResourceFolder + "/PlayerPlaceholderMaterial.mat";
                AssetDatabase.CreateAsset(material, materialPath);
                renderer.sharedMaterial = material;

                var pivot = new GameObject("CameraPivot").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(0f, 1.62f, 0f);
                var cameraObject = new GameObject("OwnerCamera");
                cameraObject.transform.SetParent(pivot, false);
                cameraObject.tag = "Untagged";
                var camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.05f;
                camera.fieldOfView = 75f;
                cameraObject.AddComponent<AudioListener>();

                var serializedPlayer = new SerializedObject(player);
                serializedPlayer.FindProperty("presentationRoot").objectReferenceValue = presentation.transform;
                serializedPlayer.FindProperty("cameraPivot").objectReferenceValue = pivot;
                serializedPlayer.FindProperty("ownerCamera").objectReferenceValue = camera;
                serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var combined = Path.Combine(parent, child).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
