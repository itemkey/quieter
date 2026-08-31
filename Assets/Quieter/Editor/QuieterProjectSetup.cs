using System.IO;
using Quieter.Core;
using Quieter.Networking;
using Quieter.Player;
using Quieter.World;
using Quieter.Inventory;
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
        private const string WorldItemPrefabPath = ResourceFolder + "/NetworkWorldItem.prefab";
        private const string ItemCatalogPath = ResourceFolder + "/ItemCatalog.asset";

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
            CreateItemCatalog();
            CreateWorldItemPrefab();
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

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var itemCatalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            var worldCatalog = AssetDatabase.LoadAssetAtPath<WorldObjectCatalog>(
                ResourceFolder + "/WorldObjectCatalog.asset");
            if (player == null || player.GetComponent<PlayerInventory>() == null
                || player.GetComponent<PlayerResourceInteraction>() == null
                || itemCatalog == null || !itemCatalog.TryGetItem(22, out _)
                || !itemCatalog.TryGetRecipe(3, out _)
                || worldCatalog == null || worldCatalog.Resources.Count < 15
                || AssetDatabase.LoadAssetAtPath<GameObject>(WorldItemPrefabPath) == null)
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
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.ConfigureDefaults();
            EditorUtility.SetDirty(asset);
        }

        private static void CreatePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
                try
                {
                    if (contents.GetComponent<PlayerInventory>() == null)
                    {
                        contents.AddComponent<PlayerInventory>();
                    }
                    if (contents.GetComponent<PlayerResourceInteraction>() == null)
                    {
                        contents.AddComponent<PlayerResourceInteraction>();
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
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
                root.AddComponent<PlayerInventory>();
                root.AddComponent<PlayerResourceInteraction>();

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

        private static void CreateItemCatalog()
        {
            var stone = CreateItemDefinition(
                "Stone", 1, "Камень", "Прочный природный материал для строительства и создания инструментов.",
                20, PickupPlacementPriority.InventoryFirst,
                new Color(0.48f, 0.52f, 0.58f));
            var wood = CreateItemDefinition(
                "Wood", 2, "Дерево", "Основной материал для изготовления инструментов и других предметов.",
                20, PickupPlacementPriority.InventoryFirst,
                new Color(0.48f, 0.29f, 0.13f));
            var rope = CreateItemDefinition(
                "Rope", 3, "Верёвка", "Гибкий материал для связывания деталей и создания снаряжения.",
                10, PickupPlacementPriority.InventoryFirst,
                new Color(0.77f, 0.66f, 0.35f));
            var axe = CreateItemDefinition(
                "Axe", 4, "Примитивный топор", "Простой каменный топор. Его применение будет добавлено в будущей механике рубки.",
                1, PickupPlacementPriority.HotbarFirst,
                new Color(0.31f, 0.42f, 0.48f),
                ItemKind.Tool, ToolKind.Axe);
            var pickaxe = CreateItemDefinition(
                "PrimitivePickaxe", 5, "Примитивная кирка",
                "Каменная кирка для добычи породы и разведки месторождений.",
                1, PickupPlacementPriority.HotbarFirst,
                new Color(0.36f, 0.43f, 0.48f),
                ItemKind.Tool, ToolKind.Pickaxe, 1, 120);
            var hiddenSample = CreateItemDefinition(
                "UnknownSample", 6, "Неопознанный образец",
                "Образец породы неизвестного состава. Исследуйте исходное месторождение.",
                20, PickupPlacementPriority.InventoryFirst,
                new Color(0.42f, 0.42f, 0.4f), ItemKind.HiddenSample);
            var iron = CreateItemDefinition(
                "IronOre", 7, "Железная руда", "Сырьё для железа, инструментов и оружия.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.45f, 0.22f, 0.12f));
            var copper = CreateItemDefinition(
                "CopperOre", 8, "Медная руда", "Ранняя металлическая руда и основа бронзы.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.12f, 0.62f, 0.54f));
            var tin = CreateItemDefinition(
                "TinOre", 9, "Оловянная руда", "Редкая добавка к меди для получения бронзы.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.66f, 0.69f, 0.7f));
            var lead = CreateItemDefinition(
                "LeadOre", 10, "Свинцовая руда", "Тяжёлая руда для ремесла, грузов и кровли.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.28f, 0.32f, 0.42f));
            var silver = CreateItemDefinition(
                "SilverOre", 11, "Серебряная руда", "Дорогая руда для монет, украшений и торговли.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.78f, 0.82f, 0.88f));
            var gold = CreateItemDefinition(
                "GoldOre", 12, "Золотая руда", "Крайне редкий символ богатства и статуса.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.92f, 0.67f, 0.08f));
            var coal = CreateItemDefinition(
                "Coal", 13, "Уголь", "Топливо для будущей кузни и плавки.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.08f, 0.08f, 0.09f));
            var salt = CreateItemDefinition(
                "RockSalt", 14, "Каменная соль", "Ценный ресурс для еды, животных и торговли.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.9f, 0.88f, 0.78f));
            var sulfur = CreateItemDefinition(
                "Sulfur", 15, "Сера", "Минерал для алхимии, лекарств и ремесла.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.88f, 0.76f, 0.08f));
            var cinnabar = CreateItemDefinition(
                "Cinnabar", 16, "Киноварь", "Редкая красная руда ртути, пигмент и алхимический товар.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.66f, 0.06f, 0.06f));
            var limestone = CreateItemDefinition(
                "Limestone", 17, "Известняк", "Светлый камень для будущего производства извести и строительства.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.72f, 0.68f, 0.55f));
            var clay = CreateItemDefinition(
                "Clay", 18, "Глина", "Пластичное сырьё для будущей керамики и кирпича.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.52f, 0.24f, 0.13f));
            var gypsum = CreateItemDefinition(
                "Gypsum", 19, "Гипс", "Мягкий светлый минерал для будущей штукатурки.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.9f, 0.88f, 0.8f));
            var flint = CreateItemDefinition(
                "Flint", 20, "Кремень", "Твёрдый камень для огнива и ранних инструментов.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.16f, 0.19f, 0.2f));
            var marble = CreateItemDefinition(
                "Marble", 21, "Мрамор", "Редкий дорогой строительный камень с выразительными прожилками.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.82f, 0.82f, 0.79f));
            var shovel = CreateItemDefinition(
                "PrimitiveShovel", 22, "Примитивная лопата",
                "Каменная лопата для разработки глиняных залежей.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.38f, 0.34f, 0.28f),
                ItemKind.Tool, ToolKind.Shovel, 1, 100);

            const string recipePath = ResourceFolder + "/AxeRecipe.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(recipePath);
            if (recipe == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(recipePath) != null)
                {
                    AssetDatabase.DeleteAsset(recipePath);
                }
                recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(recipe, recipePath);
            }
            recipe.Configure(1, "Примитивный топор", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 3 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, axe, 1, CraftingCategory.Tools);
            EditorUtility.SetDirty(recipe);

            const string pickaxeRecipePath = ResourceFolder + "/PrimitivePickaxeRecipe.asset";
            var pickaxeRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(pickaxeRecipePath);
            if (pickaxeRecipe == null)
            {
                pickaxeRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(pickaxeRecipe, pickaxeRecipePath);
            }
            pickaxeRecipe.Configure(2, "Примитивная кирка", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 3 },
            }, pickaxe, 1, CraftingCategory.Tools);
            EditorUtility.SetDirty(pickaxeRecipe);

            const string shovelRecipePath = ResourceFolder + "/PrimitiveShovelRecipe.asset";
            var shovelRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(shovelRecipePath);
            if (shovelRecipe == null)
            {
                shovelRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(shovelRecipe, shovelRecipePath);
            }
            shovelRecipe.Configure(3, "Примитивная лопата", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, shovel, 1, CraftingCategory.Tools);
            EditorUtility.SetDirty(shovelRecipe);

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            if (catalog == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(ItemCatalogPath) != null)
                {
                    AssetDatabase.DeleteAsset(ItemCatalogPath);
                }
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, ItemCatalogPath);
            }
            catalog.Configure(
                new[]
                {
                    stone, wood, rope, axe, pickaxe, hiddenSample, iron, copper, tin,
                    lead, silver, gold, coal, salt, sulfur, cinnabar,
                    limestone, clay, gypsum, flint, marble, shovel,
                },
                new[] { recipe, pickaxeRecipe, shovelRecipe });
            EditorUtility.SetDirty(catalog);
        }

        private static ItemDefinition CreateItemDefinition(
            string fileName,
            ushort id,
            string displayName,
            string description,
            ushort maximumStack,
            PickupPlacementPriority priority,
            Color color,
            ItemKind kind = ItemKind.Resource,
            ToolKind tool = ToolKind.None,
            byte toolTier = 0,
            ushort maximumDurability = 0)
        {
            var path = $"{ResourceFolder}/{fileName}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.Configure(id, displayName, maximumStack, priority, color,
                itemDescription: description,
                kind: kind,
                configuredTool: tool,
                configuredToolTier: toolTier,
                configuredMaximumDurability: maximumDurability);
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void CreateWorldItemPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WorldItemPrefabPath) != null) return;
            var root = new GameObject("NetworkWorldItem");
            try
            {
                root.AddComponent<NetworkObject>();
                var collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.4f;
                collider.isTrigger = true;
                root.AddComponent<NetworkWorldItem>();
                PrefabUtility.SaveAsPrefabAsset(root, WorldItemPrefabPath);
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
