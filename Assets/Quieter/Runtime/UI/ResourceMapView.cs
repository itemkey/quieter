using System;
using System.Collections.Generic;
using Quieter.Core;
using Quieter.Inventory;
using Quieter.Player;
using Quieter.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Quieter.UI
{
    public sealed class ResourceMapView : MonoBehaviour
    {
        private const ushort PhysicalMapItemId = 36;
        private const ushort CompassItemId = 41;
        private readonly Dictionary<ulong, WorldObjectSpawn> depositSpawns = new();
        private Canvas canvas;
        private GameObject hudRoot;
        private GameObject mapRoot;
        private GameObject depositInfoRoot;
        private RectTransform mapRect;
        private RectTransform markerRoot;
        private RectTransform playerMarker;
        private Text promptText;
        private Text depositText;
        private Text depositInfoText;
        private Text durabilityText;
        private Text feedbackText;
        private Text mapDetailsText;
        private Text noteFeedbackText;
        private Text notePlacementText;
        private GameObject noteEditorRoot;
        private GameObject noteActionsRoot;
        private GameObject noteDeleteConfirmRoot;
        private InputField noteInput;
        private ulong selectedMapInstanceId;
        private ulong selectedNoteId;
        private Vector2 selectedNotePosition;
        private bool placingNote;
        private RawImage terrainImage;
        private PlayerResourceInteraction interaction;
        private PlayerInventory inventory;
        private NetworkPlayer player;
        private ResourceWorldService resourceWorld;
        private WorldObjectCatalog worldCatalog;
        private ItemCatalog itemCatalog;
        private DeterministicChunkGenerator mapGenerator;
        private bool mapBuilt;
        private bool markersDirty = true;
        private float depositOpenedAt;

        public static ResourceMapView Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.mapRoot != null
            && Instance.mapRoot.activeSelf;
        public static bool IsDepositOpen => Instance != null && Instance.depositInfoRoot != null
            && Instance.depositInfoRoot.activeSelf;

        public static void OpenDeposit(ResourceNodeView node, ushort studyBasisPoints)
        {
            if (Instance == null || node == null) return;
            Instance.ShowDeposit(node, studyBasisPoints);
        }

        public static bool TryClose()
        {
            if (IsDepositOpen)
            {
                Instance.SetDepositOpen(false);
                return true;
            }
            if (!IsOpen) return false;
            if (Instance.CancelMapTransientState()) return true;
            Instance.SetMapOpen(false);
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            worldCatalog = Resources.Load<WorldObjectCatalog>("Quieter/WorldObjectCatalog");
            itemCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            BuildInterface();
        }

        private void Update()
        {
            ResolveOwner();
            if (interaction == null) return;
            if (Keyboard.current?.mKey.wasPressedThisFrame == true)
            {
                if (!IsOpen && (inventory == null
                    || !inventory.TryGetReplicatedItemInstance(
                        PhysicalMapItemId, out selectedMapInstanceId)))
                {
                    feedbackText.text = "У вас нет физической карты. Нужны два листа, материал для письма и шнур.";
                    return;
                }
                SetMapOpen(!IsOpen);
            }
            if (IsDepositOpen && Time.unscaledTime - depositOpenedAt > 0.15f
                && Keyboard.current?.eKey.wasPressedThisFrame == true)
            {
                SetDepositOpen(false);
            }
            RefreshHud();
            if (IsOpen)
            {
                if (inventory == null || !inventory.ContainsReplicatedItemInstance(
                        PhysicalMapItemId, selectedMapInstanceId))
                {
                    feedbackText.text = "Карты больше нет в инвентаре.";
                    SetMapOpen(false);
                    return;
                }
                if (!mapBuilt) BuildMapContent();
                if (markersDirty) RefreshMarkers();
                UpdatePlayerMarker();
                if (noteFeedbackText != null)
                {
                    var noteFeedback = interaction.LastFeedback;
                    if (!string.IsNullOrEmpty(noteFeedback)) noteFeedbackText.text = noteFeedback;
                }
            }
        }

        private void ResolveOwner()
        {
            if (interaction != null && interaction.IsSpawned) return;
            var candidates = FindObjectsByType<PlayerResourceInteraction>(
                FindObjectsInactive.Exclude);
            foreach (var candidate in candidates)
            {
                if (!candidate.IsOwner) continue;
                interaction = candidate;
                inventory = candidate.GetComponent<PlayerInventory>();
                player = candidate.GetComponent<NetworkPlayer>();
                resourceWorld = QuieterRuntimeBootstrap.Instance?.Session?.ResourceWorld;
                interaction.Changed += OnInteractionChanged;
                markersDirty = true;
                break;
            }
        }

        private void BuildInterface()
        {
            var canvasObject = new GameObject("ResourceInterface", typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 180;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            hudRoot = new GameObject("ResourceHud", typeof(RectTransform));
            hudRoot.transform.SetParent(canvasObject.transform, false);
            InventoryView.Stretch((RectTransform)hudRoot.transform);

            promptText = InventoryView.CreateText(hudRoot.transform, string.Empty, 18,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchored(promptText.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0f, 112f), new Vector2(820f, 54f));
            promptText.color = Color.white;

            depositText = InventoryView.CreateText(hudRoot.transform, string.Empty, 15,
                FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchored(depositText.rectTransform, new Vector2(1f, 1f),
                new Vector2(-230f, -155f), new Vector2(420f, 260f));
            depositText.color = new Color(0.92f, 0.93f, 0.95f);

            durabilityText = InventoryView.CreateText(hudRoot.transform, string.Empty, 15,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchored(durabilityText.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0f, 54f), new Vector2(420f, 34f));

            feedbackText = InventoryView.CreateText(hudRoot.transform, string.Empty, 17,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchored(feedbackText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -135f), new Vector2(720f, 40f));
            feedbackText.color = new Color(1f, 0.88f, 0.42f);

            BuildMapInterface(canvasObject.transform);
            BuildDepositInfoInterface(canvasObject.transform);
        }

        private void BuildDepositInfoInterface(Transform parent)
        {
            depositInfoRoot = new GameObject("DepositInformation", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            depositInfoRoot.transform.SetParent(parent, false);
            InventoryView.Stretch((RectTransform)depositInfoRoot.transform);
            depositInfoRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.36f);

            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            card.transform.SetParent(depositInfoRoot.transform, false);
            card.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.065f, 0.96f);
            SetAnchored((RectTransform)card.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 560f));

            var title = InventoryView.CreateText(card.transform, "СВЕДЕНИЯ О МЕСТОРОЖДЕНИИ", 22,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -38f), new Vector2(500f, 48f));

            depositInfoText = InventoryView.CreateText(card.transform, string.Empty, 18,
                FontStyle.Normal, TextAnchor.UpperLeft);
            depositInfoText.supportRichText = false;
            SetAnchored(depositInfoText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -5f), new Vector2(480f, 390f));

            var close = CreateMapButton(card.transform, "ЗАКРЫТЬ  [E / ESC]",
                new Color(0.18f, 0.38f, 0.32f));
            SetAnchored(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0f, 42f), new Vector2(360f, 48f));
            close.onClick.AddListener(() => SetDepositOpen(false));
            depositInfoRoot.SetActive(false);
        }

        private void BuildMapInterface(Transform parent)
        {
            mapRoot = new GameObject("WorldMap", typeof(RectTransform), typeof(Image));
            mapRoot.transform.SetParent(parent, false);
            InventoryView.Stretch((RectTransform)mapRoot.transform);
            mapRoot.GetComponent<Image>().color = new Color(0.035f, 0.04f, 0.035f, 0.97f);

            var title = InventoryView.CreateText(mapRoot.transform,
                "КАРТА РАЗВЕДКИ    [M / ESC — закрыть]", 24, FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(-150f, -35f), new Vector2(900f, 46f));

            var mapObject = new GameObject("Terrain", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage));
            mapObject.transform.SetParent(mapRoot.transform, false);
            mapRect = (RectTransform)mapObject.transform;
            SetAnchored(mapRect, new Vector2(0.5f, 0.5f),
                new Vector2(-170f, -15f), new Vector2(780f, 780f));
            terrainImage = mapObject.GetComponent<RawImage>();
            terrainImage.color = Color.white;
            var pointer = mapObject.AddComponent<MapPointerHandler>();
            pointer.Clicked = OnMapClicked;

            markerRoot = new GameObject("Markers", typeof(RectTransform)).GetComponent<RectTransform>();
            markerRoot.SetParent(mapRect, false);
            InventoryView.Stretch(markerRoot);

            playerMarker = CreateMarker(markerRoot, new Color(0.2f, 0.72f, 1f), 16f);
            playerMarker.name = "PlayerMarker";
            playerMarker.SetAsLastSibling();

            var addNoteButton = CreateMapButton(mapRoot.transform, "ДОБАВИТЬ ЗАМЕТКУ",
                new Color(0.5f, 0.38f, 0.12f));
            addNoteButton.name = "AddMapNoteButton";
            SetAnchored(addNoteButton.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                new Vector2(-190f, -75f), new Vector2(330f, 44f));
            addNoteButton.onClick.AddListener(BeginNotePlacement);

            notePlacementText = InventoryView.CreateText(mapRoot.transform, string.Empty, 15,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            notePlacementText.name = "MapNotePlacementHint";
            notePlacementText.color = new Color(1f, 0.82f, 0.32f);
            SetAnchored(notePlacementText.rectTransform, new Vector2(1f, 1f),
                new Vector2(-190f, -126f), new Vector2(330f, 52f));
            notePlacementText.gameObject.SetActive(false);

            mapDetailsText = InventoryView.CreateText(mapRoot.transform,
                "Выберите отметку месторождения.", 17, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchored(mapDetailsText.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-190f, 8f), new Vector2(330f, 460f));
            mapDetailsText.supportRichText = false;

            BuildMapNoteActions();
            BuildMapNoteEditor();

            noteFeedbackText = InventoryView.CreateText(mapRoot.transform, string.Empty, 14,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            noteFeedbackText.name = "MapNoteFeedback";
            noteFeedbackText.color = new Color(1f, 0.82f, 0.32f);
            SetAnchored(noteFeedbackText.rectTransform, new Vector2(1f, 0f),
                new Vector2(-190f, 38f), new Vector2(330f, 44f));

            mapRoot.SetActive(false);
        }

        private void BuildMapNoteActions()
        {
            noteActionsRoot = new GameObject("MapNoteActions", typeof(RectTransform));
            noteActionsRoot.transform.SetParent(mapRoot.transform, false);
            SetAnchored((RectTransform)noteActionsRoot.transform, new Vector2(1f, 0f),
                new Vector2(-190f, 118f), new Vector2(330f, 46f));
            var edit = CreateMapButton(noteActionsRoot.transform, "ИЗМЕНИТЬ",
                new Color(0.18f, 0.38f, 0.48f));
            SetAnchored(edit.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(78f, 0f), new Vector2(150f, 42f));
            edit.onClick.AddListener(EditSelectedNote);
            var delete = CreateMapButton(noteActionsRoot.transform, "УДАЛИТЬ",
                new Color(0.48f, 0.18f, 0.16f));
            SetAnchored(delete.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-78f, 0f), new Vector2(150f, 42f));
            delete.onClick.AddListener(ShowDeleteConfirmation);
            noteActionsRoot.SetActive(false);

            noteDeleteConfirmRoot = new GameObject("MapNoteDeleteConfirmation", typeof(RectTransform));
            noteDeleteConfirmRoot.transform.SetParent(mapRoot.transform, false);
            SetAnchored((RectTransform)noteDeleteConfirmRoot.transform, new Vector2(1f, 0f),
                new Vector2(-190f, 126f), new Vector2(330f, 92f));
            var question = InventoryView.CreateText(noteDeleteConfirmRoot.transform,
                "Удалить эту заметку?", 15, FontStyle.Bold, TextAnchor.UpperCenter);
            SetAnchored(question.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(320f, 28f));
            var confirm = CreateMapButton(noteDeleteConfirmRoot.transform, "ДА",
                new Color(0.58f, 0.18f, 0.16f));
            SetAnchored(confirm.GetComponent<RectTransform>(), new Vector2(0f, 0f),
                new Vector2(76f, 20f), new Vector2(140f, 36f));
            confirm.onClick.AddListener(DeleteSelectedNote);
            var cancel = CreateMapButton(noteDeleteConfirmRoot.transform, "НЕТ",
                new Color(0.22f, 0.28f, 0.32f));
            SetAnchored(cancel.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-76f, 20f), new Vector2(140f, 36f));
            cancel.onClick.AddListener(() =>
            {
                noteDeleteConfirmRoot.SetActive(false);
                noteActionsRoot.SetActive(selectedNoteId != 0);
            });
            noteDeleteConfirmRoot.SetActive(false);
        }

        private void BuildMapNoteEditor()
        {
            noteEditorRoot = new GameObject("MapNoteEditor", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            noteEditorRoot.transform.SetParent(mapRoot.transform, false);
            noteEditorRoot.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.065f, 0.98f);
            SetAnchored((RectTransform)noteEditorRoot.transform, new Vector2(1f, 0.5f),
                new Vector2(-190f, 15f), new Vector2(350f, 240f));
            var title = InventoryView.CreateText(noteEditorRoot.transform, "ТЕКСТ ЗАМЕТКИ", 17,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -30f), new Vector2(310f, 36f));

            var inputObject = new GameObject("MapNoteInput", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(noteEditorRoot.transform, false);
            inputObject.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.14f, 1f);
            SetAnchored((RectTransform)inputObject.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -88f), new Vector2(310f, 52f));
            var inputText = InventoryView.CreateText(inputObject.transform, string.Empty, 16,
                FontStyle.Normal, TextAnchor.MiddleLeft);
            inputText.supportRichText = false;
            InventoryView.Stretch(inputText.rectTransform);
            inputText.rectTransform.offsetMin = new Vector2(12f, 4f);
            inputText.rectTransform.offsetMax = new Vector2(-12f, -4f);
            noteInput = inputObject.GetComponent<InputField>();
            noteInput.textComponent = inputText;
            noteInput.targetGraphic = inputObject.GetComponent<Image>();
            noteInput.lineType = InputField.LineType.SingleLine;
            noteInput.characterLimit = MapNoteRules.MaximumTextLength;

            var save = CreateMapButton(noteEditorRoot.transform, "СОХРАНИТЬ",
                new Color(0.18f, 0.5f, 0.35f));
            SetAnchored(save.GetComponent<RectTransform>(), new Vector2(0f, 0f),
                new Vector2(92f, 38f), new Vector2(170f, 44f));
            save.onClick.AddListener(SaveNoteEditor);
            var cancel = CreateMapButton(noteEditorRoot.transform, "ОТМЕНА",
                new Color(0.22f, 0.28f, 0.32f));
            SetAnchored(cancel.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-78f, 38f), new Vector2(130f, 44f));
            cancel.onClick.AddListener(CancelNoteEditor);
            noteEditorRoot.SetActive(false);
        }

        private void RefreshHud()
        {
            if (inventory == null || interaction == null || IsOpen || IsDepositOpen)
            {
                if (IsOpen || IsDepositOpen)
                {
                    promptText.text = string.Empty;
                    depositText.text = string.Empty;
                    durabilityText.text = string.Empty;
                    feedbackText.text = string.Empty;
                }
                return;
            }
            var node = interaction.FocusedNode;
            promptText.text = interaction.IsPlacementMode
                ? interaction.PlacementHasSurface
                    ? "[ЛКМ] Установить    [Колесо] Повернуть    [ПКМ/Escape] Отмена"
                    : "Нет подходящей поверхности    [ПКМ/Escape] Отмена"
                : interaction.FocusedTable != null
                    ? "[E] Открыть исследовательский стол"
                    : interaction.FocusedStructure != null
                        ? BuildStructurePrompt(interaction.FocusedStructure)
                        : BuildPrompt(node);
            depositText.text = string.Empty;
            var active = inventory.GetReplicatedSlot(new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + inventory.SelectedHotbarIndex));
            if (!active.IsEmpty && itemCatalog != null
                && itemCatalog.TryGetItem(active.ItemId, out var item) && item.IsDurable)
            {
                durabilityText.text = $"{item.DisplayName}: {active.Condition}/{item.MaximumDurability}";
                durabilityText.color = active.Condition <= item.MaximumDurability / 5
                    ? new Color(1f, 0.35f, 0.25f)
                    : new Color(0.82f, 0.9f, 1f);
            }
            else
            {
                durabilityText.text = string.Empty;
            }
            feedbackText.text = interaction.LastFeedback;
        }

        private string BuildPrompt(ResourceNodeView node)
        {
            if (node == null || !node.IsAvailable) return string.Empty;
            if (node.Descriptor.IsWaterSource)
                return "[E] Набрать воду в сосуд    [R] Вымыть предмет в руке";
            if (node.Descriptor.IsLoosePickup)
            {
                var name = ItemName(node.Descriptor.ResourceItemId);
                return $"[E] Подобрать: {name}";
            }
            if (node.Descriptor.Kind == WorldObjectKind.StoneOutcrop)
            {
                return "[ЛКМ] Добывать камень";
            }
            if (node.Descriptor.IsResearchable)
            {
                return "[ЛКМ] Добывать    [E] Открыть сведения";
            }
            if (node.Descriptor.IsTree)
            {
                return "[ЛКМ] Рубить дерево";
            }
            return string.Empty;
        }

        private string BuildStructurePrompt(SurvivalStructureView structure)
        {
            if (SurvivalStructureRules.IsWastePit(structure.ItemId))
                return "[E] Опорожнить сосуд с отходами";
            if (structure.ItemId == SurvivalStructureRules.LeanToItemId)
                return "Навес — частичная защита от дождя и ветра";
            if (structure.ItemId == SurvivalStructureRules.HoldingCellItemId)
                return "[E] Запереть или отпереть дверь камеры";
            if (structure.ItemId == SurvivalStructureRules.DoorItemId)
                return "[E] Запереть или отпереть дверь";
            if (structure.ItemId == SurvivalStructureRules.ChestItemId)
                return "[E] Запереть или отпереть сундук    [Shift+E] Положить/взять предмет";
            if (structure.ItemId == SurvivalStructureRules.CartographyTableItemId)
                return "[E] Скопировать или объединить физические карты";
            if (structure.ItemId == SurvivalStructureRules.LatrineItemId)
                return "[E] Воспользоваться уборной (нужен сток к яме)";
            if (structure.ItemId == SurvivalStructureRules.WashBasinItemId)
                return "[E] Вымыться    [Shift+E] Налить/набрать воду";
            if (structure.ItemId == SurvivalStructureRules.BarrelItemId)
                return "[E] Налить или набрать воду";
            if (structure.ItemId == SurvivalStructureRules.WellItemId)
                return "[E] Набрать воду из колодца";
            if (structure.ItemId == SurvivalStructureRules.DrainItemId)
                return "Дренаж — соедините непрерывным уклоном с выгребной ямой";
            if (structure.ItemId == SurvivalStructureRules.BedItemId)
                return "Кровать назначается работнику при заключении договора";
            if (structure.ItemId != SurvivalStructureRules.HearthItemId) return string.Empty;
            var active = inventory.GetReplicatedSlot(new InventorySlotReference(
                InventorySlotArea.Inventory,
                InventoryLayout.FirstHotbarSlot + inventory.SelectedHotbarIndex));
            if (active.ItemId == 30 && active.LiquidMilliliters > 0)
                return "[E] Кипятить    [Shift+E] Настой с травами    [Ctrl+E] Отвар из кореньев";
            if (active.ItemId == 33)
                return "[E] Прогреть очищенную иглу";
            return ResourceBalance.TryGetCookedFoodItemId(active.ItemId, out _)
                ? "[E] Приготовить пищу на очаге"
                : "[E] Добавить древесину или уголь в очаг";
        }

        private void SetMapOpen(bool open)
        {
            if (mapRoot == null || interaction == null) return;
            if (open)
            {
                SetDepositOpen(false);
                inventory?.SetInterfaceOpen(false);
            }
            mapRoot.SetActive(open);
            hudRoot.SetActive(!open);
            player?.SetInventoryInterfaceOpen(open);
            if (open)
            {
                markersDirty = true;
                if (!mapBuilt) BuildMapContent();
            }
            else
            {
                ResetMapNoteUi();
            }
        }

        private void ShowDeposit(ResourceNodeView node, ushort studyBasisPoints)
        {
            if (node == null || depositInfoText == null) return;
            var spawn = new WorldObjectSpawn(
                node.InstanceId,
                default,
                node.transform.position,
                node.transform.rotation,
                node.transform.localScale,
                node.Descriptor);
            depositInfoText.text = BuildDepositDetails(spawn, studyBasisPoints);
            SetDepositOpen(true);
        }

        private void SetDepositOpen(bool open)
        {
            if (depositInfoRoot == null) return;
            if (open)
            {
                if (IsOpen) SetMapOpen(false);
                inventory?.SetInterfaceOpen(false);
                depositOpenedAt = Time.unscaledTime;
            }
            depositInfoRoot.SetActive(open);
            if (hudRoot != null) hudRoot.SetActive(!open && !IsOpen);
            player?.SetInventoryInterfaceOpen(open);
        }

        private void BuildMapContent()
        {
            if (resourceWorld == null || worldCatalog == null) return;
            var definition = resourceWorld.Definition;
            if (definition.ChunkCountX == 0) return;
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                name = "QuieterWorldMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    var fiber = Mathf.PerlinNoise(x * 0.61f, y * 0.73f) * 0.035f;
                    texture.SetPixel(x, y, new Color(
                        0.69f + fiber,
                        0.61f + fiber,
                        0.43f + fiber));
                }
            }
            texture.Apply(false, true);
            terrainImage.texture = texture;

            depositSpawns.Clear();
            mapBuilt = true;
            markersDirty = true;
        }

        private void RefreshMarkers()
        {
            if (!mapBuilt || interaction == null) return;
            for (var index = markerRoot.childCount - 1; index >= 0; index--)
            {
                var child = markerRoot.GetChild(index);
                if (child == playerMarker) continue;
                Destroy(child.gameObject);
            }
            for (var noteIndex = 0; noteIndex < interaction.MapNoteCount; noteIndex++)
            {
                var note = interaction.GetMapNote(noteIndex);
                if (note.NoteId == 0 || note.MapItemInstanceId != selectedMapInstanceId) continue;
                var marker = CreateMarker(markerRoot, new Color(1f, 0.73f, 0.16f), 15f);
                marker.name = $"MapNote_{note.NoteId}";
                marker.anchoredPosition = WorldToMap(note.Position);
                var button = marker.gameObject.AddComponent<Button>();
                button.targetGraphic = marker.GetComponent<Image>();
                var capturedNote = note;
                button.onClick.AddListener(() => SelectMapNote(capturedNote));
                var label = InventoryView.CreateText(marker, ShortNoteText(note.Text.ToString()),
                    10, FontStyle.Bold, TextAnchor.UpperCenter);
                label.supportRichText = false;
                label.color = new Color(1f, 0.9f, 0.55f);
                SetAnchored(label.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0f, -12f), new Vector2(180f, 30f));
            }
            playerMarker.SetAsLastSibling();
            markersDirty = false;
        }

        private void UpdatePlayerMarker()
        {
            if (playerMarker == null || interaction == null) return;
            var hasCompass = inventory != null
                && inventory.ContainsReplicatedItem(CompassItemId);
            playerMarker.gameObject.SetActive(hasCompass);
            if (!hasCompass) return;
            playerMarker.anchoredPosition = WorldToMap(interaction.transform.position);
            playerMarker.localRotation = Quaternion.Euler(0f, 0f, -interaction.transform.eulerAngles.y);
        }

        private void BeginNotePlacement()
        {
            if (interaction == null) return;
            if (interaction.CountMapNotes(selectedMapInstanceId)
                >= MapNoteRules.MaximumNotesPerMap)
            {
                noteFeedbackText.text = "Достигнут лимит: 64 заметки.";
                return;
            }
            placingNote = true;
            selectedNoteId = 0;
            noteEditorRoot.SetActive(false);
            noteActionsRoot.SetActive(false);
            noteDeleteConfirmRoot.SetActive(false);
            notePlacementText.text = "Щёлкните по карте или нажмите Esc для отмены.";
            notePlacementText.gameObject.SetActive(true);
            mapDetailsText.text = "Выберите место для новой личной заметки.";
        }

        private void OnMapClicked(PointerEventData eventData)
        {
            if (!IsOpen || interaction == null) return;
            if (eventData.button != PointerEventData.InputButton.Right
                && !(placingNote && eventData.button == PointerEventData.InputButton.Left))
            {
                return;
            }
            if (!TryMapScreenToWorld(eventData.position, out var position)) return;
            BeginNoteEditor(0, position, string.Empty);
        }

        private bool TryMapScreenToWorld(Vector2 screenPosition, out Vector2 worldPosition)
        {
            worldPosition = default;
            if (resourceWorld == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    mapRect, screenPosition, canvas.worldCamera, out var local))
            {
                return false;
            }
            var normalizedX = Mathf.Clamp01(local.x / mapRect.rect.width + 0.5f);
            var normalizedZ = Mathf.Clamp01(local.y / mapRect.rect.height + 0.5f);
            var definition = resourceWorld.Definition;
            worldPosition = new Vector2(
                Mathf.Lerp(definition.WorldMinimum.x, definition.WorldMaximum.x, normalizedX),
                Mathf.Lerp(definition.WorldMinimum.z, definition.WorldMaximum.z, normalizedZ));
            return true;
        }

        private void BeginNoteEditor(ulong noteId, Vector2 position, string text)
        {
            placingNote = false;
            notePlacementText.gameObject.SetActive(false);
            noteDeleteConfirmRoot.SetActive(false);
            noteActionsRoot.SetActive(false);
            selectedNoteId = noteId;
            selectedNotePosition = position;
            noteInput.SetTextWithoutNotify(text ?? string.Empty);
            noteEditorRoot.SetActive(true);
            noteInput.ActivateInputField();
            noteInput.Select();
        }

        private void SaveNoteEditor()
        {
            var text = MapNoteRules.NormalizeText(noteInput.text);
            if (text.Length == 0)
            {
                noteFeedbackText.text = "Введите текст заметки.";
                noteInput.ActivateInputField();
                return;
            }
            if (selectedNoteId == 0)
            {
                interaction?.RequestCreateMapNote(
                    selectedMapInstanceId, selectedNotePosition, text);
                mapDetailsText.text = "Заметка сохраняется…";
            }
            else
            {
                interaction?.RequestUpdateMapNote(
                    selectedMapInstanceId, selectedNoteId, selectedNotePosition, text);
                mapDetailsText.text = $"{text}\n\nКоординаты: "
                    + $"{selectedNotePosition.x:0}, {selectedNotePosition.y:0}";
                noteActionsRoot.SetActive(true);
            }
            noteEditorRoot.SetActive(false);
        }

        private void CancelNoteEditor()
        {
            noteEditorRoot.SetActive(false);
            if (selectedNoteId != 0 && TryGetMapNote(selectedNoteId, out var note))
            {
                SelectMapNote(note);
            }
            else
            {
                selectedNoteId = 0;
                mapDetailsText.text = "Выберите отметку месторождения или личную заметку.";
            }
        }

        private void EditSelectedNote()
        {
            if (selectedNoteId != 0 && TryGetMapNote(selectedNoteId, out var note))
            {
                BeginNoteEditor(note.NoteId, note.Position, note.Text.ToString());
            }
        }

        private void ShowDeleteConfirmation()
        {
            if (selectedNoteId == 0) return;
            noteActionsRoot.SetActive(false);
            noteDeleteConfirmRoot.SetActive(true);
        }

        private void DeleteSelectedNote()
        {
            if (selectedNoteId != 0)
            {
                interaction?.RequestDeleteMapNote(selectedMapInstanceId, selectedNoteId);
            }
            selectedNoteId = 0;
            noteDeleteConfirmRoot.SetActive(false);
            noteActionsRoot.SetActive(false);
            mapDetailsText.text = "Заметка удаляется…";
        }

        private void SelectDepositMarker(WorldObjectSpawn spawn, ushort study)
        {
            selectedNoteId = 0;
            placingNote = false;
            notePlacementText.gameObject.SetActive(false);
            noteEditorRoot.SetActive(false);
            noteActionsRoot.SetActive(false);
            noteDeleteConfirmRoot.SetActive(false);
            mapDetailsText.text = BuildDepositDetails(spawn, study);
        }

        private void SelectMapNote(MapNoteNetworkState note)
        {
            selectedNoteId = note.NoteId;
            selectedNotePosition = note.Position;
            placingNote = false;
            notePlacementText.gameObject.SetActive(false);
            noteEditorRoot.SetActive(false);
            noteDeleteConfirmRoot.SetActive(false);
            noteActionsRoot.SetActive(true);
            mapDetailsText.text = $"ЛИЧНАЯ ЗАМЕТКА\n\n{note.Text}\n\n"
                + $"Координаты: {note.Position.x:0}, {note.Position.y:0}";
        }

        private bool TryGetMapNote(ulong noteId, out MapNoteNetworkState note)
        {
            if (interaction != null)
            {
                for (var index = 0; index < interaction.MapNoteCount; index++)
                {
                    note = interaction.GetMapNote(index);
                    if (note.MapItemInstanceId == selectedMapInstanceId
                        && note.NoteId == noteId)
                    {
                        return true;
                    }
                }
            }
            note = default;
            return false;
        }

        private bool CancelMapTransientState()
        {
            if (noteEditorRoot != null && noteEditorRoot.activeSelf)
            {
                CancelNoteEditor();
                return true;
            }
            if (placingNote)
            {
                placingNote = false;
                notePlacementText.gameObject.SetActive(false);
                mapDetailsText.text = "Выберите отметку месторождения или личную заметку.";
                return true;
            }
            if (noteDeleteConfirmRoot != null && noteDeleteConfirmRoot.activeSelf)
            {
                noteDeleteConfirmRoot.SetActive(false);
                noteActionsRoot.SetActive(selectedNoteId != 0);
                return true;
            }
            return false;
        }

        private void ResetMapNoteUi()
        {
            placingNote = false;
            selectedNoteId = 0;
            notePlacementText?.gameObject.SetActive(false);
            noteEditorRoot?.SetActive(false);
            noteActionsRoot?.SetActive(false);
            noteDeleteConfirmRoot?.SetActive(false);
            if (mapDetailsText != null)
            {
                mapDetailsText.text = "Выберите отметку месторождения или личную заметку.";
            }
        }

        private static string ShortNoteText(string value)
        {
            value = MapNoteRules.NormalizeText(value);
            return value.Length <= 30 ? value : value.Substring(0, 29) + "…";
        }

        private Vector2 WorldToMap(Vector3 position)
        {
            var definition = resourceWorld.Definition;
            var minimum = definition.WorldMinimum;
            var normalizedX = Mathf.InverseLerp(minimum.x, definition.WorldMaximum.x, position.x);
            var normalizedZ = Mathf.InverseLerp(minimum.z, definition.WorldMaximum.z, position.z);
            return new Vector2(
                (normalizedX - 0.5f) * mapRect.rect.width,
                (normalizedZ - 0.5f) * mapRect.rect.height);
        }

        private Vector2 WorldToMap(Vector2 position) => WorldToMap(
            new Vector3(position.x, 0f, position.y));

        private string BuildMarkerName(WorldObjectSpawn spawn, ushort study)
        {
            if (study < 2500) return "Неизвестное месторождение";
            if (study < 5000) return ResourceBalance.CategoryName(spawn.Resource.Category);
            if (study < 7500) return $"Месторождение: {ItemName(spawn.Resource.ResourceItemId)}";
            return $"{ResourceBalance.RichnessName(spawn.Resource.Richness)}: "
                + ItemName(spawn.Resource.ResourceItemId);
        }

        private string BuildDepositDetails(WorldObjectSpawn spawn, ushort studyBasisPoints)
        {
            var percent = Mathf.Clamp(studyBasisPoints / 100, 0, 100);
            var descriptor = spawn.Resource;
            var state = resourceWorld.GetState(spawn.InstanceId, descriptor);
            var depleted = state.RemainingReserves == 0;
            var resource = percent >= 50 ? ItemName(descriptor.ResourceItemId) : "неизвестно";
            var category = percent >= 25
                ? ResourceBalance.CategoryName(descriptor.Category)
                : "неизвестно";
            var richness = percent >= 75
                ? ResourceBalance.RichnessName(descriptor.Richness)
                : "неизвестно";
            var reserves = percent >= 75
                ? ResourceBalance.ReserveName(descriptor.ReserveSize)
                : "неизвестно";
            var quality = percent >= 100
                ? ResourceBalance.QualityName(descriptor.Quality)
                : "неизвестно";
            var hardness = percent >= 100 ? descriptor.Hardness.ToString() : "неизвестно";
            var impurity = percent >= 100
                ? descriptor.ImpurityItemId == 0
                    ? "нет"
                    : $"{ItemName(descriptor.ImpurityItemId)} — {descriptor.ImpurityChancePercent}%"
                : "неизвестно";
            var remaining = percent >= 100
                ? descriptor.InitialReserves == 0
                    ? "0%"
                    : $"{Mathf.RoundToInt(state.RemainingReserves * 100f / descriptor.InitialReserves)}%"
                : "неизвестно";
            var recommendedTool = percent >= 100
                ? ResourceBalance.ToolName(descriptor.RequiredTool)
                : "неизвестно";
            return $"{(depleted ? "ИСТОЩЕНО\n" : string.Empty)}"
                + $"Изученность: {percent}%\n"
                + $"Категория: {category}\n"
                + $"Ресурс: {resource}\n"
                + $"Богатство: {richness}\n"
                + $"Запасы: {reserves}\n"
                + $"Качество: {quality}\n"
                + $"Твёрдость: {hardness}\n"
                + $"Примеси: {impurity}\n"
                + $"Остаток: {remaining}\n"
                + $"Инструмент: {recommendedTool}";
        }

        private string ItemName(ushort itemId)
        {
            return itemCatalog != null && itemCatalog.TryGetItem(itemId, out var item)
                ? item.DisplayName
                : $"Ресурс {itemId}";
        }

        private void OnInteractionChanged()
        {
            markersDirty = true;
        }

        private static RectTransform CreateMarker(Transform parent, Color color, float size)
        {
            var marker = new GameObject("MapMarker", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            marker.SetParent(parent, false);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(size, size);
            marker.GetComponent<Image>().color = color;
            return marker;
        }

        private static Button CreateMapButton(Transform parent, string label, Color color)
        {
            var target = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            target.transform.SetParent(parent, false);
            var image = target.GetComponent<Image>();
            image.color = color;
            var button = target.GetComponent<Button>();
            button.targetGraphic = image;
            var text = InventoryView.CreateText(target.transform, label, 15,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            InventoryView.Stretch(text.rectTransform);
            return button;
        }

        private static void SetAnchored(
            RectTransform rect,
            Vector2 anchor,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OnDestroy()
        {
            if (interaction != null) interaction.Changed -= OnInteractionChanged;
            if (Instance == this) Instance = null;
        }
    }

    public sealed class MapPointerHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action<PointerEventData> Clicked { get; set; }

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(eventData);
    }
}
