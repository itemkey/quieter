using System.IO;
using Quieter.Core;
using Quieter.Networking;
using Quieter.Player;
using Quieter.World;
using Quieter.Inventory;
using Quieter.Survival;
using Quieter.Combat;
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
                || player.GetComponent<PlayerSurvival>() == null
                || player.GetComponent<PlayerCombat>() == null
                || player.GetComponent<NpcBrain>() == null
                || itemCatalog == null || !itemCatalog.TryGetItem(47, out _)
                || !itemCatalog.TryGetItem(48, out _)
                || !itemCatalog.TryGetItem(49, out _)
                || !itemCatalog.TryGetRecipe(14, out _)
                || !itemCatalog.TryGetRecipe(15, out _)
                || !itemCatalog.TryGetRecipe(16, out _)
                || !itemCatalog.TryGetItem(62, out _)
                || !itemCatalog.TryGetRecipe(29, out _)
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
                    if (contents.GetComponent<PlayerSurvival>() == null)
                    {
                        contents.AddComponent<PlayerSurvival>();
                    }
                    if (contents.GetComponent<PlayerCombat>() == null)
                    {
                        contents.AddComponent<PlayerCombat>();
                    }
                    if (contents.GetComponent<NpcBrain>() == null)
                    {
                        contents.AddComponent<NpcBrain>();
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
                root.GetComponent<NetworkObject>().DontDestroyWithOwner = true;
                var player = root.AddComponent<NetworkPlayer>();
                root.AddComponent<PlayerInventory>();
                root.AddComponent<PlayerResourceInteraction>();
                root.AddComponent<PlayerSurvival>();
                root.AddComponent<PlayerCombat>();
                root.AddComponent<NpcBrain>();

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
                "Axe", 4, "Примитивный топор", "Простой каменный топор для рубки деревьев.",
                1, PickupPlacementPriority.HotbarFirst,
                new Color(0.31f, 0.42f, 0.48f),
                ItemKind.Tool, ToolKind.Axe, 1, 100);
            var pickaxe = CreateItemDefinition(
                "PrimitivePickaxe", 5, "Примитивная кирка",
                "Каменная кирка для добычи породы и разведки месторождений.",
                1, PickupPlacementPriority.HotbarFirst,
                new Color(0.36f, 0.43f, 0.48f),
                ItemKind.Tool, ToolKind.Pickaxe, 1, 120);
            var hiddenSample = CreateItemDefinition(
                "UnknownSample", 6, "Неопознанный образец",
                "Одиночный образец породы. Исследуйте его за исследовательским столом.",
                1, PickupPlacementPriority.InventoryFirst,
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
            var plantFiber = CreateItemDefinition(
                "PlantFiber", 23, "Растительное волокно",
                "Прочные растительные волокна для изготовления верёвки.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.47f, 0.66f, 0.2f));
            var researchTable = CreateItemDefinition(
                "PrimitiveResearchTable", 24, "Примитивный исследовательский стол",
                "Простой деревянный стол для исследования образцов месторождений.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.42f, 0.25f, 0.11f),
                ItemKind.Placeable);
            var berries = CreateItemDefinition(
                "WildBerries", 25, "Дикие ягоды", "Немного воды и сахара; быстро портятся и могут быть заражены.",
                12, PickupPlacementPriority.InventoryFirst, new Color(0.55f, 0.08f, 0.18f), ItemKind.Food,
                calories: 55f, proteinGrams: 0.7f, micronutrients: 0.15f, waterLiters: 0.02f,
                shelfLifeGameHours: 30f, fatGrams: 0.3f, minerals: 0.06f);
            var roots = CreateItemDefinition(
                "EdibleRoots", 26, "Съедобные коренья", "Грубая пища, требующая очистки и приготовления.",
                10, PickupPlacementPriority.InventoryFirst, new Color(0.48f, 0.31f, 0.14f), ItemKind.Food,
                calories: 110f, proteinGrams: 2.2f, micronutrients: 0.18f, waterLiters: 0.03f,
                shelfLifeGameHours: 96f, fatGrams: 0.2f, minerals: 0.14f);
            var mushrooms = CreateItemDefinition(
                "WildMushrooms", 27, "Дикие грибы", "Без знания ботаники легко спутать съедобные и опасные виды.",
                10, PickupPlacementPriority.InventoryFirst, new Color(0.58f, 0.46f, 0.31f), ItemKind.Food,
                calories: 25f, proteinGrams: 3f, micronutrients: 0.16f, waterLiters: 0.02f,
                shelfLifeGameHours: 20f, fatGrams: 0.4f, minerals: 0.18f);
            var herbs = CreateItemDefinition(
                "MedicinalHerbs", 28, "Лекарственные травы", "Сырьё для правдоподобных средневековых составов.",
                12, PickupPlacementPriority.InventoryFirst, new Color(0.26f, 0.53f, 0.2f), ItemKind.Medical);
            var waterskin = CreateItemDefinition(
                "Waterskin", 29, "Бурдюк", "Сосуд для воды; жидкость сохраняет источник и загрязнение.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.42f, 0.24f, 0.12f), ItemKind.LiquidContainer,
                liquidCapacityMilliliters: 1200);
            var cookingPot = CreateItemDefinition(
                "CookingPot", 30, "Глиняный котелок", "Обожжённая посуда для кипячения воды и приготовления пищи на очаге.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.52f, 0.34f, 0.23f), ItemKind.LiquidContainer,
                liquidCapacityMilliliters: 2000, massKg: 1.5f, volumeLiters: 2.5f);
            var soap = CreateItemDefinition(
                "Soap", 31, "Мыло", "Снижает загрязнение рук, тела, ткани и инструментов.",
                8, PickupPlacementPriority.InventoryFirst, new Color(0.79f, 0.74f, 0.56f), ItemKind.Medical);
            var cloth = CreateItemDefinition(
                "CleanCloth", 32, "Ткань", "Её чистота определяет безопасность повязки.",
                12, PickupPlacementPriority.InventoryFirst, new Color(0.81f, 0.77f, 0.66f), ItemKind.Medical);
            var needle = CreateItemDefinition(
                "NeedleAndThread", 33, "Игла и нить", "Для швов; перед применением инструменты следует очистить и прогреть.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.65f, 0.65f, 0.62f), ItemKind.Medical);
            var splint = CreateItemDefinition(
                "WoodenSplint", 34, "Деревянная шина", "Фиксирует повреждённую конечность на время долгого сращения.",
                4, PickupPlacementPriority.InventoryFirst, new Color(0.55f, 0.36f, 0.17f), ItemKind.Medical);
            var sheet = CreateItemDefinition(
                "FiberSheet", 35, "Лист из волокон", "Высушенный лист для письма и картографии.",
                8, PickupPlacementPriority.InventoryFirst, new Color(0.76f, 0.69f, 0.5f), ItemKind.Document);
            var physicalMap = CreateItemDefinition(
                "PhysicalMap", 36, "Карта", "Физический документ: нанесённые сведения остаются на предмете после смерти владельца.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.69f, 0.57f, 0.34f), ItemKind.Document);
            var charcoal = CreateItemDefinition(
                "Charcoal", 37, "Древесный уголь", "Топливо и материал для письма.",
                20, PickupPlacementPriority.InventoryFirst, new Color(0.1f, 0.09f, 0.08f));
            var bucket = CreateItemDefinition(
                "WoodenBucket", 38, "Деревянное ведро", "Открытый сосуд для переноса воды и отходов.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.42f, 0.28f, 0.13f), ItemKind.LiquidContainer,
                liquidCapacityMilliliters: 8000, massKg: 1.8f, volumeLiters: 9f);
            var tunic = CreateItemDefinition(
                "WoolTunic", 39, "Шерстяная туника", "Слой одежды, который намокает, пачкается и удерживает тепло.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.36f, 0.34f, 0.28f), ItemKind.Clothing,
                insulation: 0.56f, waterResistance: 0.18f, massKg: 1.4f, volumeLiters: 3f,
                clothingLayer: ClothingLayer.MidBody);
            var chamberPot = CreateItemDefinition(
                "ChamberPot", 40, "Ночной горшок", "Сосуд для отходов; требует опорожнения и очистки.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.52f, 0.42f, 0.31f), ItemKind.LiquidContainer,
                liquidCapacityMilliliters: 2500, massKg: 1.6f, volumeLiters: 3f);
            var compass = CreateItemDefinition(
                "RareCompass", 41, "Редкий компас", "Показывает положение и направление на физической карте.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.72f, 0.58f, 0.18f), ItemKind.Tool);
            var club = CreateItemDefinition(
                "WoodenClub", 42, "Дубина", "Тяжёлое короткое оружие, вызывающее ушибы и переломы.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.34f, 0.2f, 0.1f), ItemKind.Weapon);
            var spear = CreateItemDefinition(
                "WoodenSpear", 43, "Копьё", "Длинное колющее оружие; опасно на дистанции, неудобно вплотную.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.48f, 0.36f, 0.17f), ItemKind.Weapon);
            var leanTo = CreateItemDefinition(
                "LeanTo", 44, "Навес", "Крыша и ветрозащитная стенка: ослабляют дождь и ветер, но не удерживают дым.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.31f, 0.27f, 0.12f), ItemKind.Placeable,
                massKg: 28f, volumeLiters: 80f);
            var hearth = CreateItemDefinition(
                "StoneHearth", 45, "Каменный очаг", "Сжигает ветки или древесный уголь, даёт настоящее тепло и требует топлива.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.33f, 0.31f, 0.28f), ItemKind.Placeable,
                massKg: 35f, volumeLiters: 35f);
            var wastePit = CreateItemDefinition(
                "UnlinedWastePit", 46, "Необлицованная выгребная яма", "Примитивная яма для отходов. Дешёвая, но заражает почву и воду ниже по склону.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.19f, 0.12f, 0.055f), ItemKind.Placeable);
            var holdingCell = CreateItemDefinition(
                "HoldingCell", 47, "Запираемая камера",
                "Тяжёлая деревянная клетка с дверью и замком. Непрерывное удержание связанного пленника в запертой камере запускает захват.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.24f, 0.14f, 0.055f),
                ItemKind.Placeable, massKg: 75f, volumeLiters: 180f);
            var bed = CreateItemDefinition(
                "Bed", 48, "Кровать",
                "Принадлежащая владельцу кровать. Работнику нужно постоянное спальное место, прежде чем договор станет устойчивым.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.39f, 0.29f, 0.15f),
                ItemKind.Placeable, massKg: 24f, volumeLiters: 70f);
            var inheritanceDeed = CreateItemDefinition(
                "InheritanceDeed", 49, "Наследственная грамота",
                "Уникальный физический документ для регистрации добровольно лояльного наследника. Грамота остаётся предметом и может быть потеряна.",
                1, PickupPlacementPriority.InventoryFirst, new Color(0.72f, 0.62f, 0.39f),
                ItemKind.Document, massKg: 0.05f, volumeLiters: 0.02f);
            var floor = CreateItemDefinition("WoodFloor", 50, "Деревянный пол",
                "Модуль основания для сухого замкнутого помещения.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.34f, 0.2f, 0.08f),
                ItemKind.Placeable, massKg: 32f, volumeLiters: 95f);
            var wall = CreateItemDefinition("WoodWall", 51, "Деревянная стена",
                "Стыкуемый модуль стены, защищающий от ветра и дождя.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.31f, 0.18f, 0.07f),
                ItemKind.Placeable, massKg: 38f, volumeLiters: 105f);
            var roof = CreateItemDefinition("WoodRoof", 52, "Деревянная крыша",
                "Стыкуемый кровельный модуль; в закрытом доме требует вентиляции очага.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.27f, 0.16f, 0.06f),
                ItemKind.Placeable, massKg: 35f, volumeLiters: 100f);
            var doorway = CreateItemDefinition("Doorway", 53, "Дверной проём",
                "Стеновой модуль с проходом для двери.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.32f, 0.19f, 0.07f),
                ItemKind.Placeable, massKg: 32f, volumeLiters: 90f);
            var door = CreateItemDefinition("WoodDoor", 54, "Деревянная дверь",
                "Принадлежащая владельцу дверь с простым замком.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.26f, 0.13f, 0.045f),
                ItemKind.Placeable, massKg: 18f, volumeLiters: 45f);
            var chest = CreateItemDefinition("WoodChest", 55, "Деревянный сундук",
                "Запираемое физическое хранилище владельца.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.34f, 0.19f, 0.065f),
                ItemKind.Placeable, massKg: 22f, volumeLiters: 60f);
            var cartographyTable = CreateItemDefinition("CartographyTable", 56,
                "Картографический стол", "Копирует и объединяет физические карты с расходом листа и угля.",
                1, PickupPlacementPriority.HotbarFirst, new Color(0.39f, 0.27f, 0.12f),
                ItemKind.Placeable, massKg: 26f, volumeLiters: 70f);
            var latrine = CreateItemDefinition("Latrine", 57, "Уборная",
                "Санитарный узел, который направляет отходы в ближайшую яму.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.29f, 0.2f, 0.1f),
                ItemKind.Placeable, massKg: 20f, volumeLiters: 55f);
            var washBasin = CreateItemDefinition("WashBasin", 58, "Умывальник",
                "Глиняная чаша для мытья рук, тела и инструментов.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.5f, 0.39f, 0.27f),
                ItemKind.Placeable, massKg: 12f, volumeLiters: 24f);
            var barrel = CreateItemDefinition("WaterBarrel", 59, "Бочка",
                "Стационарная ёмкость для воды; чистота сосуда влияет на содержимое.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.35f, 0.2f, 0.07f),
                ItemKind.Placeable, massKg: 30f, volumeLiters: 120f);
            var well = CreateItemDefinition("Well", 60, "Колодец",
                "Источник грунтовой воды, уязвимый для загрязнения сверху по рельефу.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.34f, 0.33f, 0.3f),
                ItemKind.Placeable, massKg: 95f, volumeLiters: 180f);
            var drain = CreateItemDefinition("Drain", 61, "Дренажный канал",
                "Стыкуемый участок канавы или трубы для переноса жидких отходов.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.37f, 0.29f, 0.2f),
                ItemKind.Placeable, massKg: 15f, volumeLiters: 30f);
            var linedPit = CreateItemDefinition("LinedWastePit", 62, "Облицованная выгребная яма",
                "Каменная облицовка резко уменьшает утечку, но переполнение всё равно опасно.", 1,
                PickupPlacementPriority.HotbarFirst, new Color(0.31f, 0.29f, 0.24f),
                ItemKind.Placeable, massKg: 80f, volumeLiters: 145f);
            var undertunic = CreateItemDefinition("LinenUndertunic", 63, "Льняная нижняя рубаха",
                "Лёгкий нательный слой. Впитывает пот и должен регулярно стираться.", 1,
                PickupPlacementPriority.InventoryFirst, new Color(0.72f, 0.68f, 0.54f),
                ItemKind.Clothing, insulation: 0.18f, waterResistance: 0.04f,
                massKg: 0.55f, volumeLiters: 1.2f, clothingLayer: ClothingLayer.BaseBody);
            var cloak = CreateItemDefinition("WoolCloak", 64, "Шерстяной плащ",
                "Наружный слой против ветра и дождя; промокший становится тяжёлым и холодным.", 1,
                PickupPlacementPriority.InventoryFirst, new Color(0.25f, 0.28f, 0.22f),
                ItemKind.Clothing, insulation: 0.4f, waterResistance: 0.34f,
                massKg: 2.2f, volumeLiters: 5f, clothingLayer: ClothingLayer.OuterBody);
            var hood = CreateItemDefinition("WoolHood", 65, "Шерстяной капюшон",
                "Отдельный слой для головы и шеи.", 1,
                PickupPlacementPriority.InventoryFirst, new Color(0.31f, 0.3f, 0.25f),
                ItemKind.Clothing, insulation: 0.12f, waterResistance: 0.16f,
                massKg: 0.35f, volumeLiters: 0.8f, clothingLayer: ClothingLayer.Head);
            var gloves = CreateItemDefinition("WorkGloves", 66, "Рабочие рукавицы",
                "Защищают руки от холода, но грязные рукавицы загрязняют всё, к чему прикасаются.", 1,
                PickupPlacementPriority.InventoryFirst, new Color(0.38f, 0.31f, 0.2f),
                ItemKind.Clothing, insulation: 0.08f, waterResistance: 0.2f,
                massKg: 0.3f, volumeLiters: 0.6f, clothingLayer: ClothingLayer.Hands);
            var boots = CreateItemDefinition("WrappedBoots", 67, "Обмотанные башмаки",
                "Слой для стоп: сохраняет тепло, но долго сохнет и удерживает грязь.", 1,
                PickupPlacementPriority.InventoryFirst, new Color(0.28f, 0.2f, 0.12f),
                ItemKind.Clothing, insulation: 0.15f, waterResistance: 0.28f,
                massKg: 1.1f, volumeLiters: 2.2f, clothingLayer: ClothingLayer.Feet);
            var nuts = CreateItemDefinition(
                "WildNuts", 68, "Дикие орехи",
                "Плотная калорийная пища с белком и жирами; долго хранится в сухом месте.",
                16, PickupPlacementPriority.InventoryFirst, new Color(0.43f, 0.25f, 0.1f),
                ItemKind.Food, calories: 175f, proteinGrams: 5.2f, micronutrients: 0.12f,
                waterLiters: 0.005f, shelfLifeGameHours: 240f, massKg: 0.06f,
                volumeLiters: 0.08f, fatGrams: 15.4f, minerals: 0.22f);
            var cookedRoots = CreateItemDefinition(
                "CookedRoots", 69, "Варёные коренья",
                "Размягчённые жаром коренья. Грязь погибла, но испорченность и небиологические токсины остались.",
                10, PickupPlacementPriority.InventoryFirst, new Color(0.58f, 0.38f, 0.17f),
                ItemKind.Food, calories: 125f, proteinGrams: 2.2f, micronutrients: 0.14f,
                waterLiters: 0.04f, shelfLifeGameHours: 36f, fatGrams: 0.2f,
                minerals: 0.13f);
            var cookedMushrooms = CreateItemDefinition(
                "CookedMushrooms", 70, "Приготовленные грибы",
                "Термически обработанные грибы. Жар убивает микробы, но ядовитый вид не становится съедобным.",
                10, PickupPlacementPriority.InventoryFirst, new Color(0.49f, 0.32f, 0.19f),
                ItemKind.Food, calories: 32f, proteinGrams: 3f, micronutrients: 0.13f,
                waterLiters: 0.015f, shelfLifeGameHours: 14f, fatGrams: 0.4f,
                minerals: 0.17f);
            var roastedNuts = CreateItemDefinition(
                "RoastedNuts", 71, "Поджаренные орехи",
                "Сухие поджаренные орехи. Плотная пища, которую удобно хранить и переносить.",
                16, PickupPlacementPriority.InventoryFirst, new Color(0.36f, 0.18f, 0.055f),
                ItemKind.Food, calories: 180f, proteinGrams: 5.2f, micronutrients: 0.1f,
                waterLiters: 0.002f, shelfLifeGameHours: 200f, massKg: 0.055f,
                volumeLiters: 0.075f, fatGrams: 15.4f, minerals: 0.21f);
            var wetFiberSheet = CreateItemDefinition(
                "WetFiberSheet", 72, "Мокрый лист из волокон",
                "Сформованный из волокон лист. Перед письмом его нужно полностью высушить.",
                8, PickupPlacementPriority.InventoryFirst, new Color(0.55f, 0.52f, 0.39f),
                ItemKind.Resource, massKg: 0.16f, volumeLiters: 0.35f);

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

            const string ropeRecipePath = ResourceFolder + "/RopeRecipe.asset";
            var ropeRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(ropeRecipePath);
            if (ropeRecipe == null)
            {
                ropeRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(ropeRecipe, ropeRecipePath);
            }
            ropeRecipe.Configure(4, "Верёвка", new[]
            {
                new CraftingRecipe.Ingredient { Item = plantFiber, Quantity = 3 },
            }, rope, 1, CraftingCategory.Materials);
            EditorUtility.SetDirty(ropeRecipe);

            const string researchTableRecipePath = ResourceFolder + "/PrimitiveResearchTableRecipe.asset";
            var researchTableRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(researchTableRecipePath);
            if (researchTableRecipe == null)
            {
                researchTableRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(researchTableRecipe, researchTableRecipePath);
            }
            researchTableRecipe.Configure(5, "Примитивный исследовательский стол", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 20 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 4 },
            }, researchTable, 1, CraftingCategory.Structures);
            EditorUtility.SetDirty(researchTableRecipe);

            const string leanToRecipePath = ResourceFolder + "/LeanToRecipe.asset";
            var leanToRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(leanToRecipePath);
            if (leanToRecipe == null)
            {
                leanToRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(leanToRecipe, leanToRecipePath);
            }
            leanToRecipe.Configure(6, "Навес", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 16 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 6 },
            }, leanTo, 1, CraftingCategory.Structures);
            EditorUtility.SetDirty(leanToRecipe);

            const string hearthRecipePath = ResourceFolder + "/StoneHearthRecipe.asset";
            var hearthRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(hearthRecipePath);
            if (hearthRecipe == null)
            {
                hearthRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(hearthRecipe, hearthRecipePath);
            }
            hearthRecipe.Configure(7, "Каменный очаг", new[]
            {
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 12 },
                new CraftingRecipe.Ingredient { Item = clay, Quantity = 4 },
                new CraftingRecipe.Ingredient { Item = flint, Quantity = 1 },
            }, hearth, 1, CraftingCategory.Structures);
            EditorUtility.SetDirty(hearthRecipe);

            const string bucketRecipePath = ResourceFolder + "/WoodenBucketRecipe.asset";
            var bucketRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(bucketRecipePath);
            if (bucketRecipe == null)
            {
                bucketRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(bucketRecipe, bucketRecipePath);
            }
            bucketRecipe.Configure(8, "Деревянное ведро", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 6 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 2 },
            }, bucket, 1, CraftingCategory.Tools);
            EditorUtility.SetDirty(bucketRecipe);

            const string wastePitRecipePath = ResourceFolder + "/UnlinedWastePitRecipe.asset";
            var wastePitRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(wastePitRecipePath);
            if (wastePitRecipe == null)
            {
                wastePitRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(wastePitRecipe, wastePitRecipePath);
            }
            wastePitRecipe.Configure(9, "Необлицованная выгребная яма", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 4 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 2 },
            }, wastePit, 1, CraftingCategory.Structures);
            EditorUtility.SetDirty(wastePitRecipe);

            var potRecipe = CreateSurvivalRecipe("CookingPotRecipe", 10, cookingPot,
                new[] { new CraftingRecipe.Ingredient { Item = clay, Quantity = 5 } },
                90f, true, SkillId.Pottery);
            var chamberPotRecipe = CreateSurvivalRecipe("ChamberPotRecipe", 11, chamberPot,
                new[] { new CraftingRecipe.Ingredient { Item = clay, Quantity = 4 } },
                60f, true, SkillId.Pottery);
            var clothRecipe = CreateSurvivalRecipe("ClothRecipe", 12, cloth,
                new[] { new CraftingRecipe.Ingredient { Item = plantFiber, Quantity = 8 } },
                30f, false, SkillId.CordageAndTextiles);
            var splintRecipe = CreateSurvivalRecipe("SplintRecipe", 13, splint, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 20f, false, SkillId.Carpentry);
            const string holdingCellRecipePath = ResourceFolder + "/HoldingCellRecipe.asset";
            var holdingCellRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(
                holdingCellRecipePath);
            if (holdingCellRecipe == null)
            {
                holdingCellRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(holdingCellRecipe, holdingCellRecipePath);
            }
            holdingCellRecipe.Configure(14, "Запираемая камера", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 20 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 8 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 12 },
            }, holdingCell, 1, CraftingCategory.Structures, 180f, false,
                SkillId.Construction);
            EditorUtility.SetDirty(holdingCellRecipe);

            const string bedRecipePath = ResourceFolder + "/BedRecipe.asset";
            var bedRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(bedRecipePath);
            if (bedRecipe == null)
            {
                bedRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(bedRecipe, bedRecipePath);
            }
            bedRecipe.Configure(15, "Кровать", new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 8 },
                new CraftingRecipe.Ingredient { Item = cloth, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 2 },
            }, bed, 1, CraftingCategory.Structures, 60f, false, SkillId.Carpentry);
            EditorUtility.SetDirty(bedRecipe);

            const string deedRecipePath = ResourceFolder + "/InheritanceDeedRecipe.asset";
            var deedRecipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(deedRecipePath);
            if (deedRecipe == null)
            {
                deedRecipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(deedRecipe, deedRecipePath);
            }
            deedRecipe.Configure(16, "Наследственная грамота", new[]
            {
                new CraftingRecipe.Ingredient { Item = sheet, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = charcoal, Quantity = 1 },
            }, inheritanceDeed, 1, CraftingCategory.Materials, 45f, false,
                SkillId.Cartography);
            EditorUtility.SetDirty(deedRecipe);

            var floorRecipe = CreateSurvivalRecipe("WoodFloorRecipe", 17, floor, new[]
            { new CraftingRecipe.Ingredient { Item = wood, Quantity = 6 } },
                40f, false, SkillId.Construction, CraftingCategory.Structures);
            var wallRecipe = CreateSurvivalRecipe("WoodWallRecipe", 18, wall, new[]
            { new CraftingRecipe.Ingredient { Item = wood, Quantity = 8 } },
                55f, false, SkillId.Construction, CraftingCategory.Structures);
            var roofRecipe = CreateSurvivalRecipe("WoodRoofRecipe", 19, roof, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 8 },
                new CraftingRecipe.Ingredient { Item = plantFiber, Quantity = 4 },
            }, 65f, false, SkillId.Construction, CraftingCategory.Structures);
            var doorwayRecipe = CreateSurvivalRecipe("DoorwayRecipe", 20, doorway, new[]
            { new CraftingRecipe.Ingredient { Item = wood, Quantity = 10 } },
                70f, false, SkillId.Construction, CraftingCategory.Structures);
            var doorRecipe = CreateSurvivalRecipe("WoodDoorRecipe", 21, door, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 6 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 50f, false, SkillId.Carpentry, CraftingCategory.Structures);
            var chestRecipe = CreateSurvivalRecipe("WoodChestRecipe", 22, chest, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 8 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 65f, false, SkillId.Carpentry, CraftingCategory.Structures);
            var cartographyTableRecipe = CreateSurvivalRecipe("CartographyTableRecipe", 23,
                cartographyTable, new[]
                {
                    new CraftingRecipe.Ingredient { Item = wood, Quantity = 10 },
                    new CraftingRecipe.Ingredient { Item = sheet, Quantity = 1 },
                }, 80f, false, SkillId.Carpentry, CraftingCategory.Structures);
            var latrineRecipe = CreateSurvivalRecipe("LatrineRecipe", 24, latrine, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 4 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 45f, false, SkillId.Sanitation, CraftingCategory.Structures);
            var washBasinRecipe = CreateSurvivalRecipe("WashBasinRecipe", 25, washBasin,
                new[] { new CraftingRecipe.Ingredient { Item = clay, Quantity = 5 } },
                75f, true, SkillId.Pottery, CraftingCategory.Structures);
            var barrelRecipe = CreateSurvivalRecipe("WaterBarrelRecipe", 26, barrel, new[]
            {
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 8 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 2 },
            }, 85f, false, SkillId.Carpentry, CraftingCategory.Structures);
            var wellRecipe = CreateSurvivalRecipe("WellRecipe", 27, well, new[]
            {
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 20 },
                new CraftingRecipe.Ingredient { Item = wood, Quantity = 6 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 2 },
            }, 180f, false, SkillId.Masonry, CraftingCategory.Structures);
            var drainRecipe = CreateSurvivalRecipe("DrainRecipe", 28, drain, new[]
            {
                new CraftingRecipe.Ingredient { Item = clay, Quantity = 6 },
                new CraftingRecipe.Ingredient { Item = stone, Quantity = 4 },
            }, 75f, true, SkillId.Masonry, CraftingCategory.Structures);
            var linedPitRecipe = CreateSurvivalRecipe("LinedWastePitRecipe", 29, linedPit,
                new[]
                {
                    new CraftingRecipe.Ingredient { Item = stone, Quantity = 16 },
                    new CraftingRecipe.Ingredient { Item = clay, Quantity = 8 },
                }, 150f, false, SkillId.Masonry, CraftingCategory.Structures);
            var undertunicRecipe = CreateSurvivalRecipe("LinenUndertunicRecipe", 30,
                undertunic, new[]
                { new CraftingRecipe.Ingredient { Item = cloth, Quantity = 2 } },
                45f, false, SkillId.CordageAndTextiles);
            var cloakRecipe = CreateSurvivalRecipe("WoolCloakRecipe", 31, cloak, new[]
            {
                new CraftingRecipe.Ingredient { Item = cloth, Quantity = 4 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 75f, false, SkillId.CordageAndTextiles);
            var hoodRecipe = CreateSurvivalRecipe("WoolHoodRecipe", 32, hood,
                new[] { new CraftingRecipe.Ingredient { Item = cloth, Quantity = 1 } },
                30f, false, SkillId.CordageAndTextiles);
            var glovesRecipe = CreateSurvivalRecipe("WorkGlovesRecipe", 33, gloves,
                new[] { new CraftingRecipe.Ingredient { Item = cloth, Quantity = 1 } },
                35f, false, SkillId.CordageAndTextiles);
            var bootsRecipe = CreateSurvivalRecipe("WrappedBootsRecipe", 34, boots, new[]
            {
                new CraftingRecipe.Ingredient { Item = cloth, Quantity = 2 },
                new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
            }, 55f, false, SkillId.CordageAndTextiles);
            var wetSheetRecipe = CreateSurvivalRecipe("WetFiberSheetRecipe", 35,
                wetFiberSheet, new[]
                {
                    new CraftingRecipe.Ingredient { Item = plantFiber, Quantity = 6 },
                }, 30f, false, SkillId.CordageAndTextiles,
                CraftingCategory.Materials, 500, 10000);
            var drySheetRecipe = CreateSurvivalRecipe("DryFiberSheetRecipe", 36,
                sheet, new[]
                {
                    new CraftingRecipe.Ingredient { Item = wetFiberSheet, Quantity = 1 },
                }, 120f, false, SkillId.CordageAndTextiles, CraftingCategory.Materials);
            var physicalMapRecipe = CreateSurvivalRecipe("PhysicalMapRecipe", 37,
                physicalMap, new[]
                {
                    new CraftingRecipe.Ingredient { Item = sheet, Quantity = 2 },
                    new CraftingRecipe.Ingredient { Item = charcoal, Quantity = 1 },
                    new CraftingRecipe.Ingredient { Item = rope, Quantity = 1 },
                }, 60f, false, SkillId.Cartography, CraftingCategory.Materials);

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
                    limestone, clay, gypsum, flint, marble, shovel, plantFiber, researchTable,
                    berries, roots, mushrooms, herbs, waterskin, cookingPot, soap, cloth,
                    needle, splint, sheet, physicalMap, charcoal, bucket, tunic, chamberPot,
                    compass, club, spear, leanTo, hearth, wastePit, holdingCell,
                    bed, inheritanceDeed,
                    floor, wall, roof, doorway, door, chest, cartographyTable, latrine,
                    washBasin, barrel, well, drain, linedPit,
                    undertunic, cloak, hood, gloves, boots, nuts,
                    cookedRoots, cookedMushrooms, roastedNuts, wetFiberSheet,
                },
                new[] { recipe, pickaxeRecipe, shovelRecipe, ropeRecipe, researchTableRecipe,
                    leanToRecipe, hearthRecipe, bucketRecipe, wastePitRecipe,
                    potRecipe, chamberPotRecipe, clothRecipe, splintRecipe,
                    holdingCellRecipe, bedRecipe, deedRecipe, floorRecipe, wallRecipe,
                    roofRecipe, doorwayRecipe, doorRecipe, chestRecipe,
                    cartographyTableRecipe, latrineRecipe, washBasinRecipe, barrelRecipe,
                    wellRecipe, drainRecipe, linedPitRecipe, undertunicRecipe, cloakRecipe,
                    hoodRecipe, glovesRecipe, bootsRecipe, wetSheetRecipe,
                    drySheetRecipe, physicalMapRecipe });

            EditorUtility.SetDirty(catalog);
        }

        private static CraftingRecipe CreateSurvivalRecipe(
            string name, ushort id, ItemDefinition output,
            CraftingRecipe.Ingredient[] ingredients, float seconds, bool needsHearth, SkillId skill,
            CraftingCategory category = CraftingCategory.Tools,
            ushort waterMilliliters = 0,
            ushort outputWetness = 0)
        {
            var path = $"{ResourceFolder}/{name}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                AssetDatabase.CreateAsset(recipe, path);
            }
            recipe.Configure(id, output.DisplayName, ingredients, output, 1,
                category, seconds, needsHearth, skill,
                waterMilliliters, outputWetness);
            EditorUtility.SetDirty(recipe);
            return recipe;
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
            ushort maximumDurability = 0,
            float calories = 0f,
            float proteinGrams = 0f,
            float micronutrients = 0f,
            float waterLiters = 0f,
            ushort liquidCapacityMilliliters = 0,
            float insulation = 0f,
            float waterResistance = 0f,
            float shelfLifeGameHours = 0f,
            float massKg = 0.2f,
            float volumeLiters = 0.25f,
            ClothingLayer clothingLayer = ClothingLayer.None,
            float fatGrams = 0f,
            float minerals = 0f)
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
                configuredMaximumDurability: maximumDurability,
                configuredUnitMassKg: massKg,
                configuredUnitVolumeLiters: volumeLiters,
                configuredCaloriesPerUnit: calories,
                configuredProteinGramsPerUnit: proteinGrams,
                configuredMicronutrientsPerUnit: micronutrients,
                configuredWaterLitersPerUnit: waterLiters,
                configuredLiquidCapacityMilliliters: liquidCapacityMilliliters,
                configuredInsulation: insulation,
                configuredWaterResistance: waterResistance,
                configuredShelfLifeGameHours: shelfLifeGameHours,
                configuredClothingLayer: clothingLayer,
                configuredFatGramsPerUnit: fatGrams,
                configuredMineralsPerUnit: minerals);
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
