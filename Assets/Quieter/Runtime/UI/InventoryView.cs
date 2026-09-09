using System;
using System.Collections;
using System.Collections.Generic;
using Quieter.Inventory;
using Quieter.Survival;
using Quieter.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Quieter.UI
{
    public sealed class InventoryView : MonoBehaviour
    {
        private enum ResearchTableTab : byte
        {
            Research = 0,
            Studied = 1,
        }

        private static readonly Color PanelColor = new(0.055f, 0.065f, 0.08f, 0.96f);
        private static readonly Color SlotColor = new(0.12f, 0.14f, 0.18f, 0.98f);
        private static readonly Color RecipeButtonColor = new(0.105f, 0.13f, 0.16f, 1f);
        private static readonly Color SelectedRecipeColor = new(0.16f, 0.43f, 0.34f, 1f);
        private static InventoryView instance;
        private static bool requestedOpen;
        private static bool requestedWorkbench;
        private static bool requestedResearchTable;

        private readonly List<InventorySlotView> mainSlots = new();
        private readonly List<InventorySlotView> hotbarSlots = new();
        private readonly List<InventorySlotView> workbenchSlots = new();
        private readonly List<(CraftingCategory Category, Button Button)> categoryButtons = new();
        private readonly List<(CraftingRecipe Recipe, Button Button)> recipeButtons = new();
        private readonly List<GameObject> researchHistoryCards = new();
        private PlayerInventory inventory;
        private PlayerSurvival survival;
        private PlayerResourceInteraction resourceInteraction;
        private GameObject canvasRoot;
        private GameObject hotbarRoot;
        private GameObject inventoryPanel;
        private GameObject workbenchPanel;
        private GameObject recipePanel;
        private GameObject researchWorkspaceRoot;
        private GameObject researchTablePanel;
        private GameObject researchTabRoot;
        private GameObject studiedTabRoot;
        private GameObject researchKnowledgeRoot;
        private RectTransform researchHistoryListRoot;
        private Text researchHistoryEmptyText;
        private Button researchTabButton;
        private Button studiedTabButton;
        private GameObject dismissArea;
        private RectTransform cursorRoot;
        private Image cursorSwatch;
        private Text cursorName;
        private Text cursorQuantity;
        private RectTransform itemInfoRoot;
        private Image itemInfoIcon;
        private Text itemInfoName;
        private Text itemInfoDescription;
        private Text itemInfoStats;
        private RectTransform quantityEditorRoot;
        private Image quantityEditorBackground;
        private InputField quantityEditorInput;
        private Text pickupPrompt;
        private Button craftButton;
        private ItemCatalog recipeCatalog;
        private RectTransform recipeListRoot;
        private GameObject recipeDetailsRoot;
        private Text recipeDetailsText;
        private Text recipeSelectionHint;
        private InventorySlotView researchTableSlot;
        private Text researchTableStatus;
        private Text researchKnowledgeLabel;
        private Image researchKnowledgeFill;
        private Image researchHoldFill;
        private Button researchButton;
        private Text researchButtonText;
        private ResearchHoldButtonView researchHoldButton;
        private GameObject researchResultRoot;
        private Image researchResultBackground;
        private Text researchResultText;
        private Button dismantleTableButton;
        private GameObject interfaceFeedbackRoot;
        private Text interfaceFeedbackText;
        private ResearchTableTab selectedResearchTab;
        private int researchHistorySignature = int.MinValue;
        private CraftingCategory selectedCraftingCategory = CraftingCategory.Tools;
        private ushort selectedRecipeId;
        private InventorySlotView hoveredSlot;
        private InventorySlotView dragSourceSlot;
        private InventorySlotView dragTargetSlot;
        private ItemStackState draggedStack;
        private InventorySlotReference itemInfoSlot = InventorySlotReference.Invalid;
        private ushort itemInfoItemId;
        private InventorySlotReference quantitySlot = InventorySlotReference.Invalid;
        private ushort quantityItemId;
        private int selectedQuantity;
        private bool gameplayVisible;
        private bool movePending;
        private InventorySlotReference rightPressedSlot = InventorySlotReference.Invalid;
        private bool quantityWasActiveOnRightPress;
        private bool rightPressAdjustedQuantity;
        private bool updatingQuantityEditor;
        private Coroutine quantityEditorFocusRoutine;

        public static bool WorkbenchMode => requestedOpen && requestedWorkbench;
        public static bool ResearchTableMode => requestedOpen && requestedResearchTable;

        public static void SetMode(bool open, bool showWorkbench)
        {
            requestedOpen = open;
            requestedWorkbench = open && showWorkbench;
            requestedResearchTable = false;
            instance?.ApplyMode();
        }

        public static void SetResearchTableMode(bool open)
        {
            requestedOpen = open;
            requestedWorkbench = false;
            requestedResearchTable = open;
            instance?.ApplyMode();
        }

        private void Awake()
        {
            instance = this;
            BuildUi();
            ApplyMode();
        }

        private void Update()
        {
            BindLocalInventory();
            if (inventory == null)
            {
                SetGameplayVisible(false);
                return;
            }

            SetGameplayVisible(true);
            RefreshPickupPrompt();
            RefreshInterfaceFeedback();
            if (!requestedOpen) return;
            if (WorkbenchMode) RefreshCraftButton();

            if (cursorRoot != null && Mouse.current != null)
            {
                cursorRoot.position = Mouse.current.position.ReadValue() + new Vector2(22f, -22f);
            }

            HandleQuantityWheel();
            RefreshDragTarget();
            if (ResearchTableMode) RefreshResearchTable();
        }

        private void BindLocalInventory()
        {
            var playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            var next = playerObject != null ? playerObject.GetComponent<PlayerInventory>() : null;
            if (next == inventory) return;
            if (inventory != null) inventory.Changed -= Refresh;
            if (resourceInteraction != null) resourceInteraction.Changed -= Refresh;
            inventory = next;
            if (inventory != null) inventory.Changed += Refresh;
            survival = playerObject != null
                ? playerObject.GetComponent<PlayerSurvival>()
                : null;
            resourceInteraction = playerObject != null
                ? playerObject.GetComponent<PlayerResourceInteraction>()
                : null;
            if (resourceInteraction != null) resourceInteraction.Changed += Refresh;
            ResetQuantitySelection();
            Refresh();
        }

        private void BuildUi()
        {
            EnsureEventSystem();
            canvasRoot = new GameObject("InventoryCanvas");
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            var scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot.AddComponent<GraphicRaycaster>();

            BuildDismissArea();
            BuildHotbar();
            BuildInventoryPanel();
            BuildWorkbenchPanel();
            BuildRecipePanel();
            BuildResearchTablePanel();
            BuildItemInfoCard();
            BuildQuantityEditor();
            BuildCursor();
            pickupPrompt = CreateText(canvasRoot.transform, string.Empty, 18, FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetAnchoredRect(pickupPrompt.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -92f), new Vector2(600f, 44f));
            BuildInterfaceFeedback();
        }

        private void BuildDismissArea()
        {
            dismissArea = new GameObject("InventoryDismissArea", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            dismissArea.transform.SetParent(canvasRoot.transform, false);
            Stretch(dismissArea.GetComponent<RectTransform>());
            var image = dismissArea.GetComponent<Image>();
            image.color = Color.clear;
            var button = dismissArea.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HideItemInfo);
        }

        private void BuildHotbar()
        {
            var root = CreatePanel(canvasRoot.transform, new Color(0.04f, 0.05f, 0.065f, 0.9f));
            root.name = "Hotbar";
            hotbarRoot = root.gameObject;
            SetAnchoredRect(root.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(516f, 78f));
            for (var index = 0; index < InventoryLayout.HotbarSlotCount; index++)
            {
                var slot = CreateSlot(root.transform,
                    new InventorySlotReference(InventorySlotArea.Inventory,
                        InventoryLayout.FirstHotbarSlot + index));
                SetAnchoredRect(slot.RectTransform, new Vector2(0f, 0.5f),
                    new Vector2(8f + index * 84f, 0f), new Vector2(76f, 68f),
                    new Vector2(0f, 0.5f));
                slot.SetNumber(index + 1);
                hotbarSlots.Add(slot);
            }
        }

        private void BuildInventoryPanel()
        {
            inventoryPanel = CreatePanel(canvasRoot.transform, PanelColor).gameObject;
            inventoryPanel.name = "InventoryPanel";
            SetAnchoredRect(inventoryPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(620f, 500f));
            var title = CreateText(inventoryPanel.transform, "ИНВЕНТАРЬ", 25, FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetTopRect(title.rectTransform, 32f, -20f, 550f, 42f);
            var hint = CreateText(inventoryPanel.transform,
                "ЛКМ — информация или перетаскивание   Shift+ЛКМ — быстрый перенос\nПКМ — количество: колесо или цифры   За окно — выбросить   Tab — закрыть",
                12, FontStyle.Normal, TextAnchor.UpperLeft);
            hint.color = new Color(0.7f, 0.74f, 0.8f);
            SetTopRect(hint.rectTransform, 32f, -64f, 550f, 54f);

            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 6; column++)
                {
                    var index = row * 6 + column;
                    var slot = CreateSlot(inventoryPanel.transform,
                        new InventorySlotReference(InventorySlotArea.Inventory, index));
                    SetAnchoredRect(slot.RectTransform, new Vector2(0f, 1f),
                        new Vector2(32f + column * 92f, -132f - row * 88f),
                        new Vector2(82f, 78f), new Vector2(0f, 1f));
                    mainSlots.Add(slot);
                }
            }
        }

        private void BuildWorkbenchPanel()
        {
            workbenchPanel = CreatePanel(canvasRoot.transform, PanelColor).gameObject;
            workbenchPanel.name = "WorkbenchPanel";
            SetAnchoredRect(workbenchPanel.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(22f, 0f), new Vector2(330f, 500f), new Vector2(0f, 0.5f));
            var title = CreateText(workbenchPanel.transform, "КАРМАННЫЙ ВЕРСТАК", 22,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            SetTopRect(title.rectTransform, 24f, -24f, 284f, 40f);
            var hint = CreateText(workbenchPanel.transform,
                "Перетащите точное количество ингредиентов.\nShift+ЛКМ возвращает стак в инвентарь.",
                13, FontStyle.Normal, TextAnchor.UpperLeft);
            hint.color = new Color(0.7f, 0.74f, 0.8f);
            SetTopRect(hint.rectTransform, 24f, -70f, 282f, 62f);
            for (var index = 0; index < InventoryLayout.WorkbenchSlotCount; index++)
            {
                var slot = CreateSlot(workbenchPanel.transform,
                    new InventorySlotReference(InventorySlotArea.Workbench, index));
                var column = index % 3;
                var row = index / 3;
                SetAnchoredRect(slot.RectTransform, new Vector2(0f, 1f),
                    new Vector2(24f + column * 96f, -160f - row * 92f),
                    new Vector2(84f, 80f), new Vector2(0f, 1f));
                workbenchSlots.Add(slot);
            }
        }

        private void BuildRecipePanel()
        {
            recipeCatalog = Resources.Load<ItemCatalog>("Quieter/ItemCatalog");
            recipePanel = CreatePanel(canvasRoot.transform, PanelColor).gameObject;
            recipePanel.name = "RecipePanel";
            SetAnchoredRect(recipePanel.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(330f, 500f), new Vector2(1f, 0.5f));
            var title = CreateText(recipePanel.transform, "РЕЦЕПТЫ", 22, FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetAnchoredRect(title.rectTransform, new Vector2(0f, 1f),
                new Vector2(24f, -20f), new Vector2(282f, 38f), new Vector2(0f, 1f));

            BuildRecipeCategoryButtons();

            recipeListRoot = new GameObject("RecipeList", typeof(RectTransform))
                .GetComponent<RectTransform>();
            recipeListRoot.SetParent(recipePanel.transform, false);
            SetAnchoredRect(recipeListRoot, new Vector2(0f, 1f),
                new Vector2(24f, -114f), new Vector2(282f, 132f), new Vector2(0f, 1f));

            recipeSelectionHint = CreateText(recipePanel.transform,
                "Выберите рецепт, чтобы увидеть состав.", 15, FontStyle.Normal,
                TextAnchor.UpperLeft);
            recipeSelectionHint.name = "RecipeSelectionHint";
            recipeSelectionHint.color = new Color(0.68f, 0.73f, 0.79f);
            SetAnchoredRect(recipeSelectionHint.rectTransform, new Vector2(0f, 1f),
                new Vector2(24f, -260f), new Vector2(282f, 54f), new Vector2(0f, 1f));

            recipeDetailsRoot = CreatePanel(recipePanel.transform,
                new Color(0.075f, 0.09f, 0.11f, 0.96f)).gameObject;
            recipeDetailsRoot.name = "RecipeDetails";
            SetAnchoredRect(recipeDetailsRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(24f, -250f), new Vector2(282f, 148f), new Vector2(0f, 1f));
            recipeDetailsText = CreateText(recipeDetailsRoot.transform, string.Empty, 15,
                FontStyle.Normal, TextAnchor.UpperLeft);
            recipeDetailsText.name = "RecipeDetailsText";
            SetAnchoredRect(recipeDetailsText.rectTransform, new Vector2(0f, 1f),
                new Vector2(12f, -10f), new Vector2(258f, 128f), new Vector2(0f, 1f));

            craftButton = CreateButton(recipePanel.transform, "СКРАФТИТЬ",
                new Color(0.18f, 0.52f, 0.38f));
            craftButton.name = "CraftRecipeButton";
            SetAnchoredRect(craftButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0f, 28f), new Vector2(282f, 54f));
            craftButton.onClick.AddListener(CraftSelectedRecipe);
            craftButton.interactable = false;

            RebuildRecipeList();
            ClearRecipeSelection();
        }

        private void BuildResearchTablePanel()
        {
            researchWorkspaceRoot = CreatePanel(
                canvasRoot.transform, new Color(0.025f, 0.03f, 0.04f, 0.92f)).gameObject;
            researchWorkspaceRoot.name = "ResearchWorkspace";
            SetAnchoredRect(researchWorkspaceRoot.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1010f, 540f));
            researchWorkspaceRoot.transform.SetSiblingIndex(inventoryPanel.transform.GetSiblingIndex());

            researchTablePanel = CreatePanel(canvasRoot.transform, PanelColor).gameObject;
            researchTablePanel.name = "ResearchTablePanel";
            SetAnchoredRect(researchTablePanel.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(319f, 0f), new Vector2(340f, 500f));

            var title = CreateText(researchTablePanel.transform, "ИССЛЕДОВАТЕЛЬСКИЙ СТОЛ", 20,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            SetTopRect(title.rectTransform, 18f, -12f, 304f, 32f);

            researchTabButton = CreateButton(researchTablePanel.transform, "ИССЛЕДОВАНИЕ",
                SelectedRecipeColor);
            researchTabButton.name = "ResearchTabButton";
            SetAnchoredRect(researchTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(18f, -52f), new Vector2(148f, 34f), new Vector2(0f, 1f));
            researchTabButton.GetComponentInChildren<Text>().fontSize = 13;
            researchTabButton.onClick.AddListener(
                () => SelectResearchTableTab(ResearchTableTab.Research));

            studiedTabButton = CreateButton(researchTablePanel.transform, "ИЗУЧЕННОЕ",
                RecipeButtonColor);
            studiedTabButton.name = "StudiedTabButton";
            SetAnchoredRect(studiedTabButton.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(174f, -52f), new Vector2(148f, 34f), new Vector2(0f, 1f));
            studiedTabButton.GetComponentInChildren<Text>().fontSize = 13;
            studiedTabButton.onClick.AddListener(
                () => SelectResearchTableTab(ResearchTableTab.Studied));

            researchTabRoot = new GameObject("ResearchTabContent", typeof(RectTransform));
            researchTabRoot.transform.SetParent(researchTablePanel.transform, false);
            SetAnchoredRect(researchTabRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, -96f), new Vector2(340f, 338f), new Vector2(0f, 1f));

            var hint = CreateText(researchTabRoot.transform,
                "Перетащите образец и удерживайте кнопку 6 секунд. Шанс успеха — 70%.",
                13, FontStyle.Normal, TextAnchor.UpperLeft);
            hint.color = new Color(0.7f, 0.74f, 0.8f);
            SetTopRect(hint.rectTransform, 18f, -2f, 304f, 42f);

            researchTableSlot = CreateSlot(researchTabRoot.transform,
                new InventorySlotReference(InventorySlotArea.ResearchTable, 0));
            SetAnchoredRect(researchTableSlot.RectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -54f), new Vector2(78f, 74f), new Vector2(0f, 1f));

            researchTableStatus = CreateText(researchTabRoot.transform,
                "Положите неопознанный образец.", 14, FontStyle.Normal, TextAnchor.UpperLeft);
            researchTableStatus.color = new Color(0.76f, 0.82f, 0.88f);
            SetTopRect(researchTableStatus.rectTransform, 110f, -54f, 212f, 74f);

            researchKnowledgeRoot = new GameObject("SampleKnowledge", typeof(RectTransform));
            researchKnowledgeRoot.transform.SetParent(researchTabRoot.transform, false);
            SetAnchoredRect(researchKnowledgeRoot.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(18f, -138f),
                new Vector2(304f, 42f), new Vector2(0f, 1f));
            researchKnowledgeLabel = CreateText(researchKnowledgeRoot.transform,
                "Изученность залежи: —", 13, FontStyle.Bold, TextAnchor.MiddleLeft);
            researchKnowledgeLabel.color = new Color(0.74f, 0.82f, 0.88f);
            SetTopRect(researchKnowledgeLabel.rectTransform, 0f, 0f, 304f, 22f);

            var knowledgeBar = CreatePanel(researchKnowledgeRoot.transform,
                new Color(0.11f, 0.13f, 0.16f, 1f));
            knowledgeBar.name = "KnowledgeBar";
            SetAnchoredRect(knowledgeBar.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, -28f), new Vector2(304f, 10f), new Vector2(0f, 1f));
            researchKnowledgeFill = CreatePanel(knowledgeBar.transform,
                new Color(0.25f, 0.78f, 0.55f, 1f));
            InventoryView.Stretch(researchKnowledgeFill.rectTransform);
            researchKnowledgeFill.type = Image.Type.Filled;
            researchKnowledgeFill.fillMethod = Image.FillMethod.Horizontal;
            researchKnowledgeFill.fillAmount = 0f;
            researchKnowledgeRoot.SetActive(false);

            researchResultBackground = CreatePanel(researchTabRoot.transform,
                new Color(0.12f, 0.15f, 0.18f, 1f));
            researchResultRoot = researchResultBackground.gameObject;
            researchResultRoot.name = "ResearchResult";
            SetAnchoredRect(researchResultBackground.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -190f), new Vector2(304f, 62f), new Vector2(0f, 1f));
            researchResultText = CreateText(researchResultRoot.transform, string.Empty, 12,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            Stretch(researchResultText.rectTransform);
            researchResultText.rectTransform.offsetMin = new Vector2(12f, 6f);
            researchResultText.rectTransform.offsetMax = new Vector2(-12f, -6f);
            researchResultRoot.SetActive(false);

            researchButton = CreateButton(researchTabRoot.transform, "ИССЛЕДОВАТЬ",
                new Color(0.12f, 0.34f, 0.25f));
            researchButton.name = "HoldResearchButton";
            SetAnchoredRect(researchButton.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(18f, -264f), new Vector2(304f, 56f), new Vector2(0f, 1f));
            researchButtonText = researchButton.GetComponentInChildren<Text>();
            researchHoldFill = CreatePanel(researchButton.transform,
                new Color(0.28f, 0.82f, 0.57f, 1f));
            Stretch(researchHoldFill.rectTransform);
            researchHoldFill.type = Image.Type.Filled;
            researchHoldFill.fillMethod = Image.FillMethod.Horizontal;
            researchHoldFill.fillAmount = 0f;
            researchHoldFill.raycastTarget = false;
            researchHoldFill.transform.SetAsFirstSibling();
            researchButtonText.transform.SetAsLastSibling();
            researchButtonText.raycastTarget = false;
            researchHoldButton = researchButton.gameObject.AddComponent<ResearchHoldButtonView>();
            researchHoldButton.Initialize(
                () => resourceInteraction?.RequestBeginResearchHold(),
                () => resourceInteraction?.RequestCancelResearchHold());

            BuildStudiedTab();

            dismantleTableButton = CreateButton(researchTablePanel.transform, "РАЗОБРАТЬ СТОЛ",
                new Color(0.34f, 0.28f, 0.2f));
            SetAnchoredRect(dismantleTableButton.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(18f, -448f),
                new Vector2(304f, 38f), new Vector2(0f, 1f));
            dismantleTableButton.onClick.AddListener(
                () => resourceInteraction?.RequestDismantleResearchTable());
            ApplyResearchTableTab();
        }

        private void BuildStudiedTab()
        {
            studiedTabRoot = new GameObject("StudiedTabContent", typeof(RectTransform));
            studiedTabRoot.transform.SetParent(researchTablePanel.transform, false);
            SetAnchoredRect(studiedTabRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, -96f), new Vector2(340f, 338f), new Vector2(0f, 1f));

            var scrollObject = new GameObject("StudiedScrollView", typeof(RectTransform),
                typeof(ScrollRect));
            scrollObject.transform.SetParent(studiedTabRoot.transform, false);
            SetAnchoredRect(scrollObject.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(18f, -4f), new Vector2(304f, 326f), new Vector2(0f, 1f));

            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollObject.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var content = new GameObject("StudiedList", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            researchHistoryListRoot = content.GetComponent<RectTransform>();
            researchHistoryListRoot.anchorMin = new Vector2(0f, 1f);
            researchHistoryListRoot.anchorMax = new Vector2(1f, 1f);
            researchHistoryListRoot.pivot = new Vector2(0.5f, 1f);
            researchHistoryListRoot.anchoredPosition = Vector2.zero;
            researchHistoryListRoot.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 8, 0, 0);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = researchHistoryListRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            researchHistoryEmptyText = CreateText(studiedTabRoot.transform,
                "Пока нет успешно изученных залежей.", 14, FontStyle.Normal,
                TextAnchor.MiddleCenter);
            researchHistoryEmptyText.name = "StudiedEmptyMessage";
            researchHistoryEmptyText.color = new Color(0.68f, 0.73f, 0.79f);
            SetAnchoredRect(researchHistoryEmptyText.rectTransform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(280f, 72f));
        }

        private void BuildInterfaceFeedback()
        {
            var background = CreatePanel(canvasRoot.transform,
                new Color(0.045f, 0.06f, 0.07f, 0.98f));
            interfaceFeedbackRoot = background.gameObject;
            interfaceFeedbackRoot.name = "InterfaceFeedback";
            SetAnchoredRect(background.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(720f, 50f), new Vector2(0.5f, 0f));
            interfaceFeedbackText = CreateText(interfaceFeedbackRoot.transform, string.Empty, 15,
                FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(interfaceFeedbackText.rectTransform);
            interfaceFeedbackText.rectTransform.offsetMin = new Vector2(16f, 6f);
            interfaceFeedbackText.rectTransform.offsetMax = new Vector2(-16f, -6f);
            interfaceFeedbackRoot.SetActive(false);
            interfaceFeedbackRoot.transform.SetAsLastSibling();
        }

        private void BuildRecipeCategoryButtons()
        {
            categoryButtons.Clear();
            var categories = new List<CraftingCategory>();
            if (recipeCatalog != null)
            {
                foreach (var recipe in recipeCatalog.Recipes)
                {
                    if (recipe != null && !categories.Contains(recipe.Category))
                    {
                        categories.Add(recipe.Category);
                    }
                }
            }
            if (categories.Count == 0) categories.Add(CraftingCategory.Tools);
            selectedCraftingCategory = categories.Contains(CraftingCategory.Tools)
                ? CraftingCategory.Tools
                : categories[0];

            const float totalWidth = 282f;
            const float gap = 6f;
            var width = (totalWidth - gap * (categories.Count - 1)) / categories.Count;
            for (var index = 0; index < categories.Count; index++)
            {
                var category = categories[index];
                var button = CreateButton(recipePanel.transform,
                    CraftingCategoryNames.DisplayName(category).ToUpperInvariant(),
                    category == selectedCraftingCategory
                        ? SelectedRecipeColor
                        : RecipeButtonColor);
                button.name = $"RecipeCategory_{category}";
                SetAnchoredRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(24f + index * (width + gap), -68f),
                    new Vector2(width, 36f), new Vector2(0f, 1f));
                button.onClick.AddListener(() => SelectRecipeCategory(category));
                categoryButtons.Add((category, button));
            }
        }

        private void RebuildRecipeList()
        {
            if (recipeListRoot == null) return;
            for (var index = recipeListRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(recipeListRoot.GetChild(index).gameObject);
            }
            recipeButtons.Clear();

            var catalog = inventory?.Catalog ?? recipeCatalog;
            var recipes = catalog?.GetRecipes(selectedCraftingCategory);
            if (recipes == null || recipes.Count == 0)
            {
                var message = CreateText(recipeListRoot,
                    catalog == null
                        ? "Рецепты временно недоступны."
                        : "В этой категории пока нет рецептов.",
                    14, FontStyle.Normal, TextAnchor.UpperLeft);
                message.name = "EmptyRecipeCategoryMessage";
                message.color = new Color(0.68f, 0.73f, 0.79f);
                SetAnchoredRect(message.rectTransform, new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(282f, 70f), new Vector2(0f, 1f));
                return;
            }

            for (var index = 0; index < recipes.Count; index++)
            {
                var recipe = recipes[index];
                if (recipe == null) continue;
                var button = CreateButton(recipeListRoot, recipe.DisplayName, RecipeButtonColor);
                button.name = $"RecipeButton_{recipe.RecipeId}";
                SetAnchoredRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(0f, -index * 44f), new Vector2(282f, 38f),
                    new Vector2(0f, 1f));
                var recipeId = recipe.RecipeId;
                button.onClick.AddListener(() => SelectRecipe(recipeId));
                recipeButtons.Add((recipe, button));
            }
            RefreshRecipeButtonColors();
        }

        private void SelectRecipeCategory(CraftingCategory category)
        {
            if (selectedCraftingCategory == category) return;
            selectedCraftingCategory = category;
            ClearRecipeSelection();
            RebuildRecipeList();
            RefreshCategoryButtonColors();
        }

        private void SelectRecipe(ushort recipeId)
        {
            var catalog = inventory?.Catalog ?? recipeCatalog;
            if (catalog == null || !catalog.TryGetRecipe(recipeId, out var recipe)
                || recipe == null || recipe.Category != selectedCraftingCategory)
            {
                ClearRecipeSelection();
                return;
            }

            selectedRecipeId = recipeId;
            recipeDetailsText.text = BuildRecipeDescription(recipe);
            recipeSelectionHint.gameObject.SetActive(false);
            recipeDetailsRoot.SetActive(true);
            craftButton.gameObject.SetActive(true);
            RefreshRecipeButtonColors();
            RefreshCraftButton();
        }

        private void ClearRecipeSelection()
        {
            selectedRecipeId = 0;
            if (recipeDetailsText != null) recipeDetailsText.text = string.Empty;
            recipeDetailsRoot?.SetActive(false);
            craftButton?.gameObject.SetActive(false);
            recipeSelectionHint?.gameObject.SetActive(true);
            RefreshRecipeButtonColors();
            RefreshCraftButton();
        }

        private static string BuildRecipeDescription(CraftingRecipe recipe)
        {
            if (recipe == null || recipe.Output == null) return "Рецепт недоступен.";
            var value = $"{recipe.DisplayName.ToUpperInvariant()}\n"
                + $"Результат: {recipe.Output.DisplayName} × {recipe.OutputQuantity}\n\n";
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Item == null) continue;
                value += $"{ingredient.Quantity} × {ingredient.Item.DisplayName}\n";
            }
            if (recipe.WorkSeconds > 0f) value += $"\nВремя работы: {recipe.WorkSeconds:0} с";
            if (recipe.RequiresBurningHearth) value += "\nТребуется горящий очаг рядом";
            return value + "\nТочный набор без лишних предметов";
        }

        private void CraftSelectedRecipe()
        {
            if (selectedRecipeId != 0) inventory?.RequestCraft(selectedRecipeId);
        }

        private void RefreshCategoryButtonColors()
        {
            foreach (var entry in categoryButtons)
            {
                if (entry.Button != null && entry.Button.targetGraphic is Image image)
                {
                    image.color = entry.Category == selectedCraftingCategory
                        ? SelectedRecipeColor
                        : RecipeButtonColor;
                }
            }
        }

        private void RefreshRecipeButtonColors()
        {
            foreach (var entry in recipeButtons)
            {
                if (entry.Button != null && entry.Button.targetGraphic is Image image)
                {
                    image.color = entry.Recipe != null
                        && entry.Recipe.RecipeId == selectedRecipeId
                        ? SelectedRecipeColor
                        : RecipeButtonColor;
                }
            }
        }

        private void RefreshCraftButton()
        {
            if (craftButton == null) return;
            var label = craftButton.GetComponentInChildren<Text>();
            if (label != null) label.text = inventory != null && inventory.IsCrafting
                ? $"РАБОТА: {inventory.CraftSecondsRemaining:0} с"
                : "ИЗГОТОВИТЬ";
            craftButton.interactable = selectedRecipeId != 0 && WorkbenchMode
                && inventory != null && inventory.CanCraftLocally(selectedRecipeId);
        }

        private void BuildItemInfoCard()
        {
            var panel = CreatePanel(canvasRoot.transform, new Color(0.045f, 0.055f, 0.075f, 0.98f));
            panel.name = "ItemInfoCard";
            panel.raycastTarget = false;
            itemInfoRoot = panel.rectTransform;
            itemInfoRoot.sizeDelta = new Vector2(300f, 204f);

            itemInfoIcon = CreatePanel(itemInfoRoot, Color.gray);
            itemInfoIcon.raycastTarget = false;
            SetAnchoredRect(itemInfoIcon.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -16f), new Vector2(56f, 56f), new Vector2(0f, 1f));
            itemInfoName = CreateText(itemInfoRoot, string.Empty, 19, FontStyle.Bold,
                TextAnchor.UpperLeft);
            itemInfoName.raycastTarget = false;
            SetTopRect(itemInfoName.rectTransform, 84f, -16f, 198f, 54f);
            itemInfoDescription = CreateText(itemInfoRoot, string.Empty, 14, FontStyle.Normal,
                TextAnchor.UpperLeft);
            itemInfoDescription.raycastTarget = false;
            itemInfoDescription.color = new Color(0.8f, 0.83f, 0.88f);
            SetTopRect(itemInfoDescription.rectTransform, 16f, -82f, 268f, 58f);
            itemInfoStats = CreateText(itemInfoRoot, string.Empty, 13, FontStyle.Normal,
                TextAnchor.LowerLeft);
            itemInfoStats.raycastTarget = false;
            itemInfoStats.color = new Color(0.62f, 0.72f, 0.82f);
            SetAnchoredRect(itemInfoStats.rectTransform, new Vector2(0f, 0f),
                new Vector2(16f, 12f), new Vector2(268f, 52f), new Vector2(0f, 0f));
            itemInfoRoot.gameObject.SetActive(false);
        }

        private void BuildCursor()
        {
            var root = new GameObject("DraggedStack", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            root.transform.SetParent(canvasRoot.transform, false);
            cursorRoot = root.GetComponent<RectTransform>();
            cursorRoot.sizeDelta = new Vector2(122f, 54f);
            var background = root.GetComponent<Image>();
            background.color = new Color(0.03f, 0.035f, 0.05f, 0.94f);
            background.raycastTarget = false;
            cursorSwatch = CreatePanel(root.transform, Color.gray);
            cursorSwatch.raycastTarget = false;
            SetAnchoredRect(cursorSwatch.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(6f, 0f), new Vector2(38f, 38f), new Vector2(0f, 0.5f));
            cursorName = CreateText(root.transform, string.Empty, 12, FontStyle.Normal,
                TextAnchor.MiddleLeft);
            cursorName.raycastTarget = false;
            SetAnchoredRect(cursorName.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(48f, 7f), new Vector2(68f, 28f), new Vector2(0f, 0.5f));
            cursorQuantity = CreateText(root.transform, string.Empty, 16, FontStyle.Bold,
                TextAnchor.LowerRight);
            cursorQuantity.raycastTarget = false;
            SetAnchoredRect(cursorQuantity.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(48f, -13f), new Vector2(62f, 24f), new Vector2(0f, 0.5f));
            cursorRoot.gameObject.SetActive(false);
        }

        private void BuildQuantityEditor()
        {
            var root = new GameObject("QuantityEditor", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(InputField));
            root.transform.SetParent(canvasRoot.transform, false);
            quantityEditorRoot = root.GetComponent<RectTransform>();
            quantityEditorRoot.sizeDelta = new Vector2(86f, 36f);
            quantityEditorBackground = root.GetComponent<Image>();
            quantityEditorBackground.color = new Color(0.04f, 0.055f, 0.075f, 0.99f);
            var outline = root.GetComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.95f, 0.65f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            var valueText = CreateText(root.transform, string.Empty, 18, FontStyle.Bold,
                TextAnchor.MiddleCenter);
            valueText.supportRichText = false;
            SetAnchoredRect(valueText.rectTransform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(72f, 30f));

            quantityEditorInput = root.GetComponent<InputField>();
            quantityEditorInput.targetGraphic = quantityEditorBackground;
            quantityEditorInput.textComponent = valueText;
            quantityEditorInput.contentType = InputField.ContentType.IntegerNumber;
            quantityEditorInput.lineType = InputField.LineType.SingleLine;
            quantityEditorInput.characterLimit = 5;
            quantityEditorInput.caretColor = Color.white;
            quantityEditorInput.selectionColor = new Color(0.2f, 0.65f, 0.5f, 0.72f);
            quantityEditorInput.onValidateInput += ValidateQuantityCharacter;
            quantityEditorInput.onValueChanged.AddListener(OnQuantityEditorChanged);
            quantityEditorInput.onEndEdit.AddListener(_ => CommitQuantityEditor());
            quantityEditorRoot.gameObject.SetActive(false);
        }

        private void ApplyMode()
        {
            if (!WorkbenchMode) ClearRecipeSelection();
            if (!ResearchTableMode)
            {
                selectedResearchTab = ResearchTableTab.Research;
                researchHistorySignature = int.MinValue;
            }
            if (inventoryPanel != null)
            {
                SetAnchoredRect(inventoryPanel.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    ResearchTableMode ? new Vector2(-174f, 0f) : Vector2.zero,
                    new Vector2(620f, 500f));
            }
            if (inventoryPanel != null) inventoryPanel.SetActive(requestedOpen);
            if (workbenchPanel != null) workbenchPanel.SetActive(WorkbenchMode);
            if (recipePanel != null) recipePanel.SetActive(WorkbenchMode);
            if (researchWorkspaceRoot != null)
            {
                researchWorkspaceRoot.SetActive(ResearchTableMode);
            }
            if (researchTablePanel != null) researchTablePanel.SetActive(ResearchTableMode);
            ApplyResearchTableTab();
            if (dismissArea != null) dismissArea.SetActive(requestedOpen);
            CancelDrag();
            HideItemInfo();
            ResetQuantitySelection();
            Refresh();
        }

        private void Refresh()
        {
            RefreshCraftButton();
            if (inventory == null)
            {
                foreach (var slot in mainSlots) slot.SetStack(default, null, false, 0);
                foreach (var slot in hotbarSlots) slot.SetStack(default, null, false, 0);
                foreach (var slot in workbenchSlots) slot.SetStack(default, null, false, 0);
                researchTableSlot?.SetStack(default, null, false, 0);
                if (cursorRoot != null) cursorRoot.gameObject.SetActive(false);
                HideItemInfo();
                return;
            }

            ValidateQuantitySelection();
            foreach (var slot in mainSlots) RefreshSlot(slot);
            foreach (var slot in hotbarSlots) RefreshSlot(slot);
            foreach (var slot in workbenchSlots) RefreshSlot(slot);
            if (researchTableSlot != null) RefreshSlot(researchTableSlot);
            RefreshQuantityEditor();
            RefreshResearchTable();

            cursorRoot.gameObject.SetActive(requestedOpen && dragSourceSlot != null
                && !draggedStack.IsEmpty);
            RefreshItemInfo();

            RefreshCraftButton();
        }

        private void RefreshSlot(InventorySlotView slot)
        {
            var stack = inventory.GetReplicatedSlot(slot.Reference);
            var item = !stack.IsEmpty ? inventory.Catalog.GetItem(stack.ItemId) : null;
            var selected = slot.Reference.Area == InventorySlotArea.Inventory
                && InventoryLayout.IsHotbar(slot.Reference.Index)
                && slot.Reference.Index == InventoryLayout.FirstHotbarSlot
                    + inventory.SelectedHotbarIndex;
            var shownSelection = quantitySlot.Equals(slot.Reference) && !stack.IsEmpty
                ? selectedQuantity
                : 0;
            slot.SetStack(stack, item, selected, shownSelection,
                ResolveStackDisplayName(stack, item));
        }

        private string ResolveStackDisplayName(ItemStackState stack, ItemDefinition item)
        {
            if (item == null) return string.Empty;
            var name = item.Kind == ItemKind.HiddenSample && resourceInteraction != null
                ? resourceInteraction.GetSampleDisplayName(stack)
                : item.DisplayName;
            return item.Kind == ItemKind.Clothing && stack.Equipped
                ? name + " [надето]"
                : name;
        }

        private void SelectResearchTableTab(ResearchTableTab tab)
        {
            if (selectedResearchTab == tab) return;
            if (resourceInteraction?.IsLocalResearchHolding == true)
            {
                resourceInteraction.RequestCancelResearchHold();
            }
            selectedResearchTab = tab;
            CancelDrag();
            HideItemInfo();
            ApplyResearchTableTab();
            RefreshResearchTable();
        }

        private void ApplyResearchTableTab()
        {
            var researchSelected = selectedResearchTab == ResearchTableTab.Research;
            if (researchTabRoot != null)
            {
                researchTabRoot.SetActive(ResearchTableMode && researchSelected);
            }
            if (studiedTabRoot != null)
            {
                studiedTabRoot.SetActive(ResearchTableMode && !researchSelected);
            }
            if (researchTabButton != null)
            {
                researchTabButton.GetComponent<Image>().color = researchSelected
                    ? SelectedRecipeColor
                    : RecipeButtonColor;
            }
            if (studiedTabButton != null)
            {
                studiedTabButton.GetComponent<Image>().color = researchSelected
                    ? RecipeButtonColor
                    : SelectedRecipeColor;
            }
        }

        private void RefreshResearchHistory()
        {
            if (researchHistoryListRoot == null || researchHistoryEmptyText == null) return;
            var signature = 17;
            if (resourceInteraction != null)
            {
                unchecked
                {
                    for (var index = 0; index < resourceInteraction.KnowledgeCount; index++)
                    {
                        var knowledge = resourceInteraction.GetKnowledge(index);
                        if (knowledge.StudyBasisPoints == 0) continue;
                        signature = signature * 31 + knowledge.InstanceId.GetHashCode();
                        signature = signature * 31 + knowledge.StudyBasisPoints;
                    }
                }
            }
            if (signature == researchHistorySignature) return;
            researchHistorySignature = signature;
            foreach (var card in researchHistoryCards)
            {
                if (card != null) Destroy(card);
            }
            researchHistoryCards.Clear();

            if (resourceInteraction != null)
            {
                for (var index = 0; index < resourceInteraction.KnowledgeCount; index++)
                {
                    var knowledge = resourceInteraction.GetKnowledge(index);
                    if (knowledge.StudyBasisPoints == 0
                        || !resourceInteraction.TryGetDepositKnowledgePresentation(
                            knowledge.InstanceId, out var presentation))
                    {
                        continue;
                    }
                    researchHistoryCards.Add(CreateResearchHistoryCard(presentation));
                }
            }
            researchHistoryEmptyText.gameObject.SetActive(researchHistoryCards.Count == 0);
        }

        private GameObject CreateResearchHistoryCard(DepositKnowledgePresentation presentation)
        {
            var background = CreatePanel(researchHistoryListRoot,
                new Color(0.085f, 0.105f, 0.13f, 0.98f));
            var card = background.gameObject;
            card.name = $"StudiedDeposit_{presentation.InstanceId}";
            var layout = card.AddComponent<LayoutElement>();
            layout.minHeight = 74f;
            layout.preferredHeight = 74f;
            layout.flexibleHeight = 0f;

            var title = CreateText(card.transform, BuildResearchHistoryName(presentation), 13,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            title.name = "StudiedDepositName";
            SetTopRect(title.rectTransform, 10f, -5f, 208f, 24f);

            var percent = CreateText(card.transform,
                $"{presentation.StudyBasisPoints / 100f:0.#}%", 13,
                FontStyle.Bold, TextAnchor.MiddleRight);
            percent.name = "StudiedDepositPercent";
            percent.color = new Color(0.38f, 0.88f, 0.64f);
            SetTopRect(percent.rectTransform, 220f, -5f, 66f, 24f);

            var coordinates = CreateText(card.transform,
                FormatDepositCoordinates(presentation.Position), 11,
                FontStyle.Normal, TextAnchor.MiddleLeft);
            coordinates.name = "StudiedDepositCoordinates";
            coordinates.color = new Color(0.65f, 0.71f, 0.78f);
            SetTopRect(coordinates.rectTransform, 10f, -30f, 276f, 20f);

            var bar = CreatePanel(card.transform, new Color(0.04f, 0.05f, 0.065f, 1f));
            bar.name = "StudiedDepositProgressBar";
            SetAnchoredRect(bar.rectTransform, new Vector2(0f, 1f),
                new Vector2(10f, -57f), new Vector2(276f, 8f), new Vector2(0f, 1f));
            var fill = CreatePanel(bar.transform, new Color(0.25f, 0.78f, 0.55f, 1f));
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = presentation.StudyBasisPoints / 10000f;
            return card;
        }

        private string BuildResearchHistoryName(DepositKnowledgePresentation presentation)
        {
            if (presentation.StudyBasisPoints < 2500) return "Неизвестная залежь";
            if (presentation.StudyBasisPoints < 5000)
            {
                return ResourceBalance.CategoryName(presentation.Category);
            }
            return inventory?.Catalog != null
                && inventory.Catalog.TryGetItem(presentation.ResourceItemId, out var item)
                    ? item.DisplayName
                    : $"Ресурс {presentation.ResourceItemId}";
        }

        private static string FormatDepositCoordinates(Vector3 position) =>
            $"Источник: X {Mathf.RoundToInt(position.x)}, Z {Mathf.RoundToInt(position.z)}";

        private void RefreshResearchTable()
        {
            if (researchButton == null || dismantleTableButton == null
                || researchTableStatus == null)
            {
                return;
            }
            var stack = resourceInteraction?.CurrentResearchTableInput ?? default;
            var busy = resourceInteraction?.CurrentResearchTableBusy == true;
            var holding = resourceInteraction?.IsLocalResearchHolding == true;
            var confirmed = resourceInteraction?.IsLocalResearchConfirmed == true;
            var busyByOther = busy && !holding;
            var studyBasisPoints = !stack.IsEmpty && resourceInteraction != null
                ? resourceInteraction.GetStudyBasisPoints(stack.SourceNodeId)
                : 0;
            var showSampleKnowledge = ResourceBalance.CanShowSampleKnowledge(
                stack, studyBasisPoints);
            if (researchKnowledgeRoot != null)
            {
                researchKnowledgeRoot.SetActive(showSampleKnowledge);
            }
            if (researchKnowledgeFill != null)
            {
                researchKnowledgeFill.fillAmount = studyBasisPoints / 10000f;
            }
            if (researchKnowledgeLabel != null)
            {
                researchKnowledgeLabel.text =
                    $"Изученность залежи: {studyBasisPoints / 100f:0.#}%";
            }
            var progress = resourceInteraction?.CurrentResearchHoldProgress ?? 0f;
            if (researchHoldFill != null) researchHoldFill.fillAmount = holding ? progress : 0f;
            if (researchButtonText != null)
            {
                researchButtonText.text = holding
                    ? confirmed
                        ? $"ИССЛЕДОВАНИЕ… {progress * ResourceBalance.ResearchDurationSeconds:0.0} / {ResourceBalance.ResearchDurationSeconds:0} С"
                        : "ПОДГОТОВКА ИССЛЕДОВАНИЯ…"
                    : "УДЕРЖИВАЙТЕ: ИССЛЕДОВАТЬ";
            }
            researchButton.interactable = ResearchTableMode
                && selectedResearchTab == ResearchTableTab.Research && !stack.IsEmpty
                && (!busyByOther || holding);
            dismantleTableButton.interactable = ResearchTableMode && stack.IsEmpty
                && !busy && !holding;
            if (holding)
            {
                researchTableStatus.text = confirmed
                    ? "Исследование выполняется.\nНе отпускайте ЛКМ."
                    : "Сервер подтверждает начало опыта…";
            }
            else if (busyByOther)
            {
                researchTableStatus.text = "Стол занят другим игроком.";
            }
            else if (stack.IsEmpty)
            {
                researchTableStatus.text = "Положите неопознанный образец.";
            }
            else
            {
                var sampleName = ResolveStackDisplayName(
                    stack, inventory?.Catalog?.GetItem(stack.ItemId));
                if (showSampleKnowledge && resourceInteraction != null
                    && resourceInteraction.TryGetDepositKnowledgePresentation(
                        stack.SourceNodeId, out var presentation))
                {
                    researchTableStatus.text = sampleName + "\n"
                        + FormatDepositCoordinates(presentation.Position);
                }
                else
                {
                    researchTableStatus.text = sampleName;
                }
            }

            if (researchResultRoot != null && researchResultText != null)
            {
                var result = resourceInteraction?.CurrentResearchResult ?? string.Empty;
                researchResultRoot.SetActive(!string.IsNullOrEmpty(result));
                researchResultText.text = result;
                if (!string.IsNullOrEmpty(result) && researchResultBackground != null)
                {
                    researchResultBackground.color = resourceInteraction.CurrentResearchResultTone switch
                    {
                        ResearchResultTone.Success => new Color(0.08f, 0.32f, 0.2f, 1f),
                        ResearchResultTone.Failure => new Color(0.42f, 0.12f, 0.11f, 1f),
                        _ => new Color(0.22f, 0.25f, 0.28f, 1f),
                    };
                }
            }
            if (selectedResearchTab == ResearchTableTab.Studied)
            {
                RefreshResearchHistory();
            }
        }

        private void RefreshInterfaceFeedback()
        {
            if (interfaceFeedbackRoot == null || interfaceFeedbackText == null) return;
            var feedback = requestedOpen ? resourceInteraction?.LastFeedback : string.Empty;
            var visible = !string.IsNullOrEmpty(feedback);
            interfaceFeedbackRoot.SetActive(visible);
            if (!visible) return;
            interfaceFeedbackText.text = feedback;
            var background = interfaceFeedbackRoot.GetComponent<Image>();
            if (background != null)
            {
                background.color = resourceInteraction != null
                    && resourceInteraction.CurrentResearchResultTone == ResearchResultTone.Success
                        ? new Color(0.07f, 0.28f, 0.18f, 0.98f)
                        : resourceInteraction != null
                            && resourceInteraction.CurrentResearchResultTone == ResearchResultTone.Failure
                                ? new Color(0.36f, 0.09f, 0.08f, 0.98f)
                                : new Color(0.045f, 0.06f, 0.07f, 0.98f);
            }
            interfaceFeedbackRoot.transform.SetAsLastSibling();
        }

        private void HandleQuantityWheel()
        {
            if (!quantitySlot.IsValid || movePending) return;
            var wheel = Mouse.current?.scroll.ReadValue().y ?? 0f;
            if (Mathf.Abs(wheel) < 0.01f) return;
            CommitQuantityEditor();
            var stack = inventory.GetReplicatedSlot(quantitySlot);
            if (stack.IsEmpty || stack.ItemId != quantityItemId)
            {
                ResetQuantitySelection();
                Refresh();
                return;
            }

            ApplySelectedQuantity(Mathf.Clamp(
                selectedQuantity + (wheel > 0f ? 1 : -1),
                1,
                stack.Quantity));
            SetQuantityEditorText(selectedQuantity);
            if (Mouse.current?.rightButton.isPressed == true)
            {
                rightPressAdjustedQuantity = true;
            }

            Refresh();
            RefreshDragTarget();
        }

        private void OnSlotPointerDown(InventorySlotView slot, PointerEventData eventData)
        {
            if (inventory == null || !requestedOpen || movePending
                || eventData.button != PointerEventData.InputButton.Right)
            {
                return;
            }

            var stack = inventory.GetReplicatedSlot(slot.Reference);
            rightPressedSlot = slot.Reference;
            quantityWasActiveOnRightPress = !stack.IsEmpty
                && quantitySlot.Equals(slot.Reference)
                && quantityItemId == stack.ItemId;
            rightPressAdjustedQuantity = false;
            if (!stack.IsEmpty && !quantityWasActiveOnRightPress)
            {
                ActivateQuantitySelection(slot, stack, true);
            }
        }

        private void OnSlotClick(InventorySlotView slot, PointerEventData.InputButton button)
        {
            if (inventory == null || !requestedOpen || movePending) return;
            var stack = inventory.GetReplicatedSlot(slot.Reference);
            if (button == PointerEventData.InputButton.Left)
            {
                if (Keyboard.current?.leftShiftKey.isPressed == true
                    || Keyboard.current?.rightShiftKey.isPressed == true)
                {
                    if (!stack.IsEmpty)
                    {
                        ResetQuantitySelection();
                        HideItemInfo();
                        inventory.RequestShiftClick(slot.Reference);
                    }
                }
                else
                {
                    ShowItemInfo(slot);
                }
            }
            else if (button == PointerEventData.InputButton.Right)
            {
                if (stack.IsEmpty)
                {
                    ResetQuantitySelection();
                }
                else if (rightPressedSlot.Equals(slot.Reference)
                    && quantityWasActiveOnRightPress
                    && !rightPressAdjustedQuantity)
                {
                    ResetQuantitySelection();
                }
                else if (!quantitySlot.Equals(slot.Reference) || quantityItemId != stack.ItemId)
                {
                    ActivateQuantitySelection(slot, stack, true);
                }

                rightPressedSlot = InventorySlotReference.Invalid;
                quantityWasActiveOnRightPress = false;
                rightPressAdjustedQuantity = false;
                Refresh();
            }
        }

        private void ValidateQuantitySelection()
        {
            if (!quantitySlot.IsValid) return;
            var stack = inventory.GetReplicatedSlot(quantitySlot);
            if (stack.IsEmpty || stack.ItemId != quantityItemId)
            {
                ResetQuantitySelection();
                return;
            }

            var clamped = Mathf.Clamp(selectedQuantity, 1, stack.Quantity);
            if (clamped != selectedQuantity)
            {
                ApplySelectedQuantity(clamped);
                SetQuantityEditorText(selectedQuantity);
            }
        }

        private void ActivateQuantitySelection(
            InventorySlotView slot,
            ItemStackState stack,
            bool focusEditor)
        {
            quantitySlot = slot.Reference;
            quantityItemId = stack.ItemId;
            selectedQuantity = stack.Quantity;
            SetQuantityEditorText(selectedQuantity);
            HideItemInfo();
            Refresh();
            if (focusEditor) FocusQuantityEditorNextFrame();
        }

        private void RefreshQuantityEditor()
        {
            if (quantityEditorRoot == null) return;
            var slot = quantitySlot.IsValid ? FindSlot(quantitySlot) : null;
            var shouldShow = requestedOpen && !movePending && dragSourceSlot == null && slot != null;
            quantityEditorRoot.gameObject.SetActive(shouldShow);
            if (!shouldShow) return;

            if (!quantityEditorInput.isFocused
                && quantityEditorInput.text != selectedQuantity.ToString())
            {
                SetQuantityEditorText(selectedQuantity);
            }

            PositionQuantityEditor(slot);
        }

        private void PositionQuantityEditor(InventorySlotView slot)
        {
            Canvas.ForceUpdateCanvases();
            var canvasRect = (RectTransform)canvasRoot.transform;
            var corners = new Vector3[4];
            slot.RectTransform.GetWorldCorners(corners);
            var topScreen = RectTransformUtility.WorldToScreenPoint(
                null, (corners[1] + corners[2]) * 0.5f);
            var bottomScreen = RectTransformUtility.WorldToScreenPoint(
                null, (corners[0] + corners[3]) * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, topScreen, null, out var topLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, bottomScreen, null, out var bottomLocal);

            var halfSize = quantityEditorRoot.sizeDelta * 0.5f;
            var position = topLocal + Vector2.up * (halfSize.y + 8f);
            if (position.y + halfSize.y > canvasRect.rect.yMax - 8f)
            {
                position = bottomLocal + Vector2.down * (halfSize.y + 8f);
            }

            position.x = Mathf.Clamp(position.x,
                canvasRect.rect.xMin + halfSize.x + 8f,
                canvasRect.rect.xMax - halfSize.x - 8f);
            position.y = Mathf.Clamp(position.y,
                canvasRect.rect.yMin + halfSize.y + 8f,
                canvasRect.rect.yMax - halfSize.y - 8f);
            quantityEditorRoot.anchorMin = quantityEditorRoot.anchorMax = new Vector2(0.5f, 0.5f);
            quantityEditorRoot.pivot = new Vector2(0.5f, 0.5f);
            quantityEditorRoot.anchoredPosition = position;
            quantityEditorRoot.SetAsLastSibling();
        }

        private void OnQuantityEditorChanged(string value)
        {
            if (updatingQuantityEditor || inventory == null || !quantitySlot.IsValid) return;
            var stack = inventory.GetReplicatedSlot(quantitySlot);
            if (stack.IsEmpty || stack.ItemId != quantityItemId)
            {
                ResetQuantitySelection();
                Refresh();
                return;
            }

            var valid = int.TryParse(value, out var parsed)
                && parsed >= 1 && parsed <= stack.Quantity;
            SetQuantityEditorValidity(valid);
            if (!valid) return;

            ApplySelectedQuantity(parsed);
            if (Mouse.current?.rightButton.isPressed == true)
            {
                rightPressAdjustedQuantity = true;
            }

            Refresh();
            RefreshDragTarget();
        }

        private void CommitQuantityEditor()
        {
            if (updatingQuantityEditor || inventory == null || !quantitySlot.IsValid) return;
            var stack = inventory.GetReplicatedSlot(quantitySlot);
            if (stack.IsEmpty || stack.ItemId != quantityItemId)
            {
                ResetQuantitySelection();
                return;
            }

            ApplySelectedQuantity(NormalizeQuantityInput(
                quantityEditorInput.text,
                selectedQuantity,
                stack.Quantity));
            SetQuantityEditorText(selectedQuantity);
            SetQuantityEditorValidity(true);
            Refresh();
            RefreshDragTarget();
        }

        private void ApplySelectedQuantity(int quantity)
        {
            selectedQuantity = quantity;
            if (dragSourceSlot == null || !quantitySlot.Equals(dragSourceSlot.Reference)
                || draggedStack.ItemId != quantityItemId)
            {
                return;
            }

            draggedStack.Quantity = (ushort)selectedQuantity;
            cursorQuantity.text = selectedQuantity.ToString();
        }

        private void SetQuantityEditorText(int value)
        {
            if (quantityEditorInput == null) return;
            updatingQuantityEditor = true;
            quantityEditorInput.SetTextWithoutNotify(value.ToString());
            updatingQuantityEditor = false;
            SetQuantityEditorValidity(true);
        }

        private void SetQuantityEditorValidity(bool valid)
        {
            if (quantityEditorBackground == null) return;
            quantityEditorBackground.color = valid
                ? new Color(0.04f, 0.055f, 0.075f, 0.99f)
                : new Color(0.22f, 0.07f, 0.065f, 0.99f);
        }

        private void FocusQuantityEditorNextFrame()
        {
            if (quantityEditorFocusRoutine != null) StopCoroutine(quantityEditorFocusRoutine);
            quantityEditorFocusRoutine = StartCoroutine(FocusQuantityEditorRoutine());
        }

        private IEnumerator FocusQuantityEditorRoutine()
        {
            yield return null;
            if (quantityEditorInput == null || !quantityEditorInput.gameObject.activeInHierarchy
                || dragSourceSlot != null || !quantitySlot.IsValid)
            {
                quantityEditorFocusRoutine = null;
                yield break;
            }

            quantityEditorInput.Select();
            quantityEditorInput.ActivateInputField();
            yield return null;
            quantityEditorFocusRoutine = null;
            if (quantityEditorInput == null || !quantityEditorInput.isFocused
                || dragSourceSlot != null || !quantitySlot.IsValid)
            {
                yield break;
            }

            quantityEditorInput.selectionAnchorPosition = 0;
            quantityEditorInput.selectionFocusPosition = quantityEditorInput.text.Length;
        }

        private void HideQuantityEditorForDrag()
        {
            if (quantityEditorFocusRoutine != null)
            {
                StopCoroutine(quantityEditorFocusRoutine);
                quantityEditorFocusRoutine = null;
            }

            if (quantityEditorInput != null && quantityEditorInput.isFocused)
            {
                quantityEditorInput.DeactivateInputField();
            }

            quantityEditorRoot?.gameObject.SetActive(false);
        }

        private static char ValidateQuantityCharacter(string text, int characterIndex, char addedChar)
        {
            return char.IsDigit(addedChar) ? addedChar : '\0';
        }

        internal static int NormalizeQuantityInput(string value, int fallback, int maximum)
        {
            maximum = Mathf.Max(1, maximum);
            fallback = Mathf.Clamp(fallback, 1, maximum);
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return long.TryParse(value, out var parsed)
                ? (int)Math.Min(maximum, Math.Max(1L, parsed))
                : fallback;
        }

        private void ShowItemInfo(InventorySlotView slot)
        {
            var stack = inventory.GetReplicatedSlot(slot.Reference);
            if (stack.IsEmpty)
            {
                HideItemInfo();
                return;
            }

            itemInfoSlot = slot.Reference;
            itemInfoItemId = stack.ItemId;
            RefreshItemInfo();
        }

        private void RefreshItemInfo()
        {
            if (itemInfoRoot == null || inventory == null || !itemInfoSlot.IsValid)
            {
                itemInfoRoot?.gameObject.SetActive(false);
                return;
            }

            var stack = inventory.GetReplicatedSlot(itemInfoSlot);
            var slot = FindSlot(itemInfoSlot);
            if (stack.IsEmpty || stack.ItemId != itemInfoItemId || slot == null
                || !inventory.Catalog.TryGetItem(stack.ItemId, out var item))
            {
                HideItemInfo();
                return;
            }

            itemInfoIcon.sprite = item.Icon;
            itemInfoIcon.preserveAspect = item.Icon != null;
            itemInfoIcon.color = item.Icon != null ? Color.white : item.PlaceholderColor;
            itemInfoName.text = ResolveStackDisplayName(stack, item);
            itemInfoDescription.text = string.IsNullOrWhiteSpace(item.Description)
                ? "Описание пока не добавлено."
                : item.Description;
            if (item.IsDurable)
            {
                itemInfoStats.text = $"Прочность: {stack.Condition}/{item.MaximumDurability}";
            }
            else if (item.Kind == ItemKind.Clothing)
            {
                itemInfoStats.text = (stack.Equipped ? "Надето" : "Не надето")
                    + $"    Слой: {ClothingLayerName(item.ClothingLayer)}"
                    + $"    Намокание: {stack.Wetness / 100}%"
                    + $"    Чистота: {stack.Cleanliness / 100}%";
            }
            else if (item.Kind == ItemKind.Food)
            {
                itemInfoStats.text = $"Количество: {stack.Quantity}    Состояние: "
                    + FoodConditionName(stack.Freshness)
                    + "\n" + DescribeFoodSafety(stack);
            }
            else if (item.Kind == ItemKind.LiquidContainer)
            {
                itemInfoStats.text = stack.LiquidMilliliters == 0
                    ? $"Пусто    Вместимость: {item.LiquidCapacityMilliliters} мл"
                    : $"{LiquidName(stack)}: {stack.LiquidMilliliters} мл"
                        + $" / {item.LiquidCapacityMilliliters} мл";
            }
            else if (stack.Quality != ResourceQuality.None && item.Kind != ItemKind.HiddenSample)
            {
                itemInfoStats.text = $"Количество: {stack.Quantity}    Качество: "
                    + ResourceBalance.QualityName(stack.Quality);
            }
            else
            {
                itemInfoStats.text = $"Количество: {stack.Quantity}    Максимальный стак: {item.MaximumStack}";
            }
            itemInfoRoot.gameObject.SetActive(requestedOpen);
            PositionItemInfoCard(slot);
        }

        private static string FoodConditionName(ushort freshness)
        {
            if (freshness >= 8000) return "свежее";
            if (freshness >= 5500) return "лежалое";
            if (freshness >= 3000) return "сомнительное";
            if (freshness > 0) return "явно испорченное";
            return "гнилое";
        }

        private string DescribeFoodSafety(ItemStackState stack)
        {
            var botany = GetOwnerSkillLevel(SkillId.Botany);
            var sanitation = GetOwnerSkillLevel(SkillId.Sanitation);
            var toxins = stack.ToxinContamination / 10000f;
            var biological = stack.BiologicalContamination / 10000f;

            string identity;
            if (stack.ItemId == ResourceBalance.WildMushroomsItemId)
            {
                identity = botany switch
                {
                    < 2 => "Вид гриба определить не удаётся.",
                    < 5 when toxins >= 0.6f => "Признаки похожи на опасный гриб.",
                    < 5 => "Похож на съедобный, но уверенности нет.",
                    _ when toxins >= 0.35f => "Ботанические признаки указывают на сильную ядовитость.",
                    _ when toxins >= 0.08f => "Есть сомнительные признаки; употребление рискованно.",
                    _ => "Узнаваемый съедобный вид без явных ядовитых признаков.",
                };
            }
            else if (ResourceBalance.IsWildFood(stack.ItemId))
            {
                identity = botany < 2
                    ? "Дикое растение распознано лишь приблизительно."
                    : toxins >= 0.25f
                        ? "Есть признаки природной токсичности."
                        : "Явных ядовитых признаков не видно.";
            }
            else
            {
                identity = "Происхождение пищи известно.";
            }

            var hygiene = sanitation switch
            {
                < 2 when biological >= 0.55f => " На поверхности заметна грязь.",
                < 2 => " Чистоту на глаз оценить трудно.",
                < 5 when biological >= 0.35f => " Пища выглядит загрязнённой.",
                < 5 => " Явного загрязнения не видно.",
                _ when biological >= 0.2f => " Вероятна опасная биологическая грязь.",
                _ => " Признаков значимого загрязнения не видно.",
            };
            return identity + hygiene;
        }

        private int GetOwnerSkillLevel(SkillId skill)
        {
            if (survival == null) return 0;
            for (var index = 0; index < survival.ProgressionEntryCount; index++)
            {
                var entry = survival.GetProgressionEntry(index);
                if (!entry.Attribute && entry.Id == (byte)skill) return entry.Level;
            }
            return 0;
        }

        private static string ClothingLayerName(ClothingLayer layer) => layer switch
        {
            ClothingLayer.BaseBody => "нательный",
            ClothingLayer.MidBody => "средний",
            ClothingLayer.OuterBody => "наружный",
            ClothingLayer.Head => "голова",
            ClothingLayer.Hands => "руки",
            ClothingLayer.Feet => "ноги",
            _ => "не определён",
        };

        private static string LiquidName(ItemStackState stack) => stack.LiquidKind switch
        {
            LiquidKind.Water => "Вода",
            LiquidKind.SaltWater => "Солёная вода",
            LiquidKind.Broth => "Бульон",
            LiquidKind.HerbalInfusion => "Травяной настой",
            LiquidKind.Waste => "Отходы",
            _ => "Жидкость",
        };

        private void PositionItemInfoCard(InventorySlotView slot)
        {
            Canvas.ForceUpdateCanvases();
            var canvasRect = (RectTransform)canvasRoot.transform;
            var corners = new Vector3[4];
            slot.RectTransform.GetWorldCorners(corners);
            var rightScreen = RectTransformUtility.WorldToScreenPoint(null, (corners[2] + corners[3]) * 0.5f);
            var leftScreen = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[1]) * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, rightScreen, null, out var rightLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, leftScreen, null, out var leftLocal);

            var size = itemInfoRoot.sizeDelta;
            var pivot = new Vector2(0f, 0.5f);
            var position = rightLocal + Vector2.right * 12f;
            if (position.x + size.x > canvasRect.rect.xMax - 8f)
            {
                pivot.x = 1f;
                position = leftLocal + Vector2.left * 12f;
            }

            position.y = Mathf.Clamp(
                position.y,
                canvasRect.rect.yMin + size.y * 0.5f + 8f,
                canvasRect.rect.yMax - size.y * 0.5f - 8f);
            itemInfoRoot.anchorMin = itemInfoRoot.anchorMax = new Vector2(0.5f, 0.5f);
            itemInfoRoot.pivot = pivot;
            itemInfoRoot.anchoredPosition = position;
        }

        private void HideItemInfo()
        {
            itemInfoSlot = InventorySlotReference.Invalid;
            itemInfoItemId = 0;
            itemInfoRoot?.gameObject.SetActive(false);
        }

        private void OnSlotBeginDrag(InventorySlotView slot, PointerEventData eventData)
        {
            if (inventory == null || !requestedOpen || movePending
                || (eventData.button != PointerEventData.InputButton.Left
                    && eventData.button != PointerEventData.InputButton.Right)
                || Keyboard.current?.leftShiftKey.isPressed == true
                || Keyboard.current?.rightShiftKey.isPressed == true)
            {
                return;
            }

            var source = inventory.GetReplicatedSlot(slot.Reference);
            if (source.IsEmpty || !inventory.Catalog.TryGetItem(source.ItemId, out var item)) return;
            CommitQuantityEditor();
            if (eventData.button == PointerEventData.InputButton.Right
                && (!quantitySlot.Equals(slot.Reference) || quantityItemId != source.ItemId))
            {
                ActivateQuantitySelection(slot, source, false);
            }

            var amount = quantitySlot.Equals(slot.Reference) && quantityItemId == source.ItemId
                ? Mathf.Clamp(selectedQuantity, 1, source.Quantity)
                : source.Quantity;
            dragSourceSlot = slot;
            draggedStack = source.WithQuantity(amount);
            slot.SetDragSource(true);
            cursorSwatch.sprite = item.Icon;
            cursorSwatch.preserveAspect = item.Icon != null;
            cursorSwatch.color = item.Icon != null ? Color.white : item.PlaceholderColor;
            cursorName.text = ResolveStackDisplayName(source, item);
            cursorQuantity.text = amount.ToString();
            cursorRoot.gameObject.SetActive(true);
            cursorRoot.position = eventData.position + new Vector2(22f, -22f);
            HideQuantityEditorForDrag();
            HideItemInfo();
            RefreshDragTarget();
        }

        private void OnSlotDrag(InventorySlotView slot, PointerEventData eventData)
        {
            if (slot != dragSourceSlot || cursorRoot == null) return;
            cursorRoot.position = eventData.position + new Vector2(22f, -22f);
            RefreshDragTarget();
        }

        private void OnSlotEndDrag(InventorySlotView slot, PointerEventData eventData)
        {
            if (slot != dragSourceSlot) return;
            var source = slot.Reference;
            var destination = dragTargetSlot != null
                ? dragTargetSlot.Reference
                : InventorySlotReference.Invalid;
            var itemId = draggedStack.ItemId;
            var quantity = draggedStack.Quantity;
            var valid = dragTargetSlot != null && inventory.CanMoveStackLocally(
                source, destination, itemId, quantity);
            var dropIntoWorld = dragTargetSlot == null
                && IsOutsideInventoryWindows(eventData.position);
            CancelDrag();
            rightPressedSlot = InventorySlotReference.Invalid;
            quantityWasActiveOnRightPress = false;
            rightPressAdjustedQuantity = false;
            if (!valid && !dropIntoWorld)
            {
                Refresh();
                return;
            }

            movePending = true;
            Action<bool> completed = success =>
            {
                if (this == null) return;
                movePending = false;
                if (success)
                {
                    ResetQuantitySelection();
                    HideItemInfo();
                }

                Refresh();
            };
            if (dropIntoWorld)
            {
                inventory.RequestDropStack(source, itemId, quantity, completed);
            }
            else
            {
                inventory.RequestMoveStack(source, destination, itemId, quantity, completed);
            }
        }

        private bool IsOutsideInventoryWindows(Vector2 screenPosition)
        {
            return !ContainsScreenPoint(inventoryPanel, screenPosition)
                && !ContainsScreenPoint(hotbarRoot, screenPosition)
                && !ContainsScreenPoint(workbenchPanel, screenPosition)
                && !ContainsScreenPoint(recipePanel, screenPosition)
                && !ContainsScreenPoint(researchTablePanel, screenPosition)
                && (itemInfoRoot == null || !itemInfoRoot.gameObject.activeInHierarchy
                    || !RectTransformUtility.RectangleContainsScreenPoint(
                        itemInfoRoot, screenPosition, null));
        }

        private static bool ContainsScreenPoint(GameObject target, Vector2 screenPosition)
        {
            return target != null && target.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(
                    target.GetComponent<RectTransform>(), screenPosition, null);
        }

        private void RefreshDragTarget()
        {
            var next = dragSourceSlot != null ? hoveredSlot : null;
            if (dragTargetSlot != null && dragTargetSlot != next)
            {
                dragTargetSlot.SetDropTarget(false, false);
            }

            dragTargetSlot = next;
            if (dragTargetSlot == null) return;
            var valid = inventory != null && inventory.CanMoveStackLocally(
                dragSourceSlot.Reference,
                dragTargetSlot.Reference,
                draggedStack.ItemId,
                draggedStack.Quantity);
            dragTargetSlot.SetDropTarget(true, valid);
        }

        private void CancelDrag()
        {
            dragSourceSlot?.SetDragSource(false);
            dragTargetSlot?.SetDropTarget(false, false);
            dragSourceSlot = null;
            dragTargetSlot = null;
            draggedStack = default;
            cursorRoot?.gameObject.SetActive(false);
        }

        private InventorySlotView FindSlot(InventorySlotReference reference)
        {
            if (reference.Area == InventorySlotArea.ResearchTable)
            {
                return researchTableSlot != null && researchTableSlot.Reference.Equals(reference)
                    ? researchTableSlot
                    : null;
            }
            var list = reference.Area == InventorySlotArea.Workbench
                ? workbenchSlots
                : reference.Area == InventorySlotArea.Inventory
                    ? (InventoryLayout.IsHotbar(reference.Index) ? hotbarSlots : mainSlots)
                    : null;
            if (list == null) return null;
            foreach (var slot in list)
            {
                if (slot.Reference.Equals(reference)) return slot;
            }

            return null;
        }

        private void RefreshPickupPrompt()
        {
            if (pickupPrompt == null) return;
            var pickup = !requestedOpen ? inventory?.FocusedPickup : null;
            if (!requestedOpen && pickup == null && inventory?.HasFocusedCorpse == true)
            {
                pickupPrompt.gameObject.SetActive(true);
                var stage = inventory.FocusedCorpseStage switch
                {
                    CorpseDecayStage.EarlyDecay => "начавшее разлагаться тело",
                    CorpseDecayStage.ActiveDecay => "сильно разложившееся тело",
                    CorpseDecayStage.AdvancedDecay => "разложившиеся останки",
                    CorpseDecayStage.DryRemains => "сухие останки",
                    CorpseDecayStage.Buried => "погребённые останки",
                    CorpseDecayStage.Cremated => "сожжённые останки",
                    _ => "свежее тело",
                };
                pickupPrompt.text = inventory.FocusedCorpseItemsSealed
                    ? $"{stage}: вещи запечатаны в могиле"
                    : $"{stage}    [E] Обыскать    [B] Погребсти лопатой"
                        + "    [C] Сжечь у горящего очага";
                return;
            }
            if (pickup == null || pickup.Stack.IsEmpty || inventory.Catalog == null
                || !inventory.Catalog.TryGetItem(pickup.Stack.ItemId, out var item))
            {
                pickupPrompt.gameObject.SetActive(false);
                return;
            }

            pickupPrompt.gameObject.SetActive(true);
            pickupPrompt.text = $"[E] Подобрать: {ResolveStackDisplayName(pickup.Stack, item)}"
                + $" × {pickup.Stack.Quantity}";
        }

        private InventorySlotView CreateSlot(Transform parent, InventorySlotReference reference)
        {
            var gameObject = new GameObject("Slot", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Outline), typeof(InventorySlotView));
            gameObject.transform.SetParent(parent, false);
            var view = gameObject.GetComponent<InventorySlotView>();
            view.Initialize(
                reference,
                SlotColor,
                OnSlotClick,
                OnSlotPointerDown,
                OnSlotBeginDrag,
                OnSlotDrag,
                OnSlotEndDrag,
                entered =>
                {
                    hoveredSlot = entered;
                    RefreshDragTarget();
                },
                exited =>
                {
                    if (hoveredSlot == exited) hoveredSlot = null;
                    RefreshDragTarget();
                });
            return view;
        }

        private void ResetQuantitySelection()
        {
            if (quantityEditorFocusRoutine != null)
            {
                StopCoroutine(quantityEditorFocusRoutine);
                quantityEditorFocusRoutine = null;
            }
            if (quantityEditorInput != null)
            {
                updatingQuantityEditor = true;
                quantityEditorInput.DeactivateInputField();
                quantityEditorInput.SetTextWithoutNotify(string.Empty);
                updatingQuantityEditor = false;
            }
            quantityEditorRoot?.gameObject.SetActive(false);
            quantitySlot = InventorySlotReference.Invalid;
            quantityItemId = 0;
            selectedQuantity = 0;
            rightPressedSlot = InventorySlotReference.Invalid;
            quantityWasActiveOnRightPress = false;
            rightPressAdjustedQuantity = false;
        }

        private void SetGameplayVisible(bool visible)
        {
            if (gameplayVisible == visible) return;
            gameplayVisible = visible;
            foreach (var slot in hotbarSlots) slot.transform.parent.gameObject.SetActive(visible);
            if (!visible)
            {
                inventoryPanel?.SetActive(false);
                workbenchPanel?.SetActive(false);
                recipePanel?.SetActive(false);
                researchWorkspaceRoot?.SetActive(false);
                researchTablePanel?.SetActive(false);
                interfaceFeedbackRoot?.SetActive(false);
                dismissArea?.SetActive(false);
                cursorRoot?.gameObject.SetActive(false);
                HideItemInfo();
                CancelDrag();
                ResetQuantitySelection();
                pickupPrompt?.gameObject.SetActive(false);
            }
            else
            {
                ApplyMode();
            }
        }

        private void OnDestroy()
        {
            if (inventory != null) inventory.Changed -= Refresh;
            if (resourceInteraction != null) resourceInteraction.Changed -= Refresh;
            if (instance == this) instance = null;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            var inputModule = eventSystem.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private static Image CreatePanel(Transform parent, Color color)
        {
            var gameObject = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        internal static Text CreateText(Transform parent, string value, int size,
            FontStyle style, TextAnchor alignment)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, Color color)
        {
            var gameObject = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            gameObject.transform.SetParent(parent, false);
            gameObject.GetComponent<Image>().color = color;
            var button = gameObject.GetComponent<Button>();
            var text = CreateText(gameObject.transform, label, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return button;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetTopRect(RectTransform rect, float x, float y, float width, float height)
        {
            SetAnchoredRect(rect, new Vector2(0f, 1f), new Vector2(x, y),
                new Vector2(width, height), new Vector2(0f, 1f));
        }

        private static void SetAnchoredRect(RectTransform rect, Vector2 anchor,
            Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }

    public sealed class ResearchHoldButtonView : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Button button;
        private Action pressed;
        private Action released;
        private bool holding;

        public void Initialize(Action onPressed, Action onReleased)
        {
            button = GetComponent<Button>();
            pressed = onPressed;
            released = onReleased;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left
                || holding || button == null || !button.interactable)
            {
                return;
            }
            holding = true;
            pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (!holding) return;
            holding = false;
            released?.Invoke();
        }
    }

    public sealed class InventorySlotView : MonoBehaviour,
        IPointerDownHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private Action<InventorySlotView, PointerEventData.InputButton> clicked;
        private Action<InventorySlotView, PointerEventData> pointerPressed;
        private Action<InventorySlotView, PointerEventData> dragBegan;
        private Action<InventorySlotView, PointerEventData> dragged;
        private Action<InventorySlotView, PointerEventData> dragEnded;
        private Action<InventorySlotView> entered;
        private Action<InventorySlotView> exited;
        private Image background;
        private Image swatch;
        private Text nameText;
        private Text quantityText;
        private Text numberText;
        private Outline outline;
        private CanvasGroup canvasGroup;
        private Color baseColor;
        private bool hotbarSelected;
        private bool dropTarget;
        private bool validDropTarget;

        public InventorySlotReference Reference { get; private set; }
        public RectTransform RectTransform => (RectTransform)transform;

        public void Initialize(InventorySlotReference reference, Color color,
            Action<InventorySlotView, PointerEventData.InputButton> click,
            Action<InventorySlotView, PointerEventData> pointerDown,
            Action<InventorySlotView, PointerEventData> beginDrag,
            Action<InventorySlotView, PointerEventData> drag,
            Action<InventorySlotView, PointerEventData> endDrag,
            Action<InventorySlotView> enter, Action<InventorySlotView> exit)
        {
            Reference = reference;
            clicked = click;
            pointerPressed = pointerDown;
            dragBegan = beginDrag;
            dragged = drag;
            dragEnded = endDrag;
            entered = enter;
            exited = exit;
            baseColor = color;
            background = GetComponent<Image>();
            background.color = color;
            outline = GetComponent<Outline>();
            outline.effectDistance = new Vector2(2f, -2f);
            outline.effectColor = new Color(0.2f, 0.25f, 0.32f, 1f);
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

            swatch = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<Image>();
            swatch.transform.SetParent(transform, false);
            swatch.raycastTarget = false;
            var swatchRect = swatch.rectTransform;
            swatchRect.anchorMin = new Vector2(0.5f, 0.5f);
            swatchRect.anchorMax = new Vector2(0.5f, 0.5f);
            swatchRect.sizeDelta = new Vector2(38f, 38f);
            swatchRect.anchoredPosition = new Vector2(0f, 4f);

            nameText = InventoryView.CreateText(transform, string.Empty, 11,
                FontStyle.Normal, TextAnchor.LowerCenter);
            nameText.raycastTarget = false;
            InventoryView.Stretch(nameText.rectTransform);
            quantityText = InventoryView.CreateText(transform, string.Empty, 16,
                FontStyle.Bold, TextAnchor.UpperRight);
            quantityText.raycastTarget = false;
            InventoryView.Stretch(quantityText.rectTransform);
            quantityText.rectTransform.offsetMax = new Vector2(-5f, -3f);
            numberText = InventoryView.CreateText(transform, string.Empty, 13,
                FontStyle.Bold, TextAnchor.UpperLeft);
            numberText.raycastTarget = false;
            InventoryView.Stretch(numberText.rectTransform);
            numberText.rectTransform.offsetMin = new Vector2(5f, 0f);
            numberText.rectTransform.offsetMax = new Vector2(0f, -3f);
        }

        public void SetNumber(int number) => numberText.text = number > 0 ? number.ToString() : string.Empty;

        public void SetStack(ItemStackState stack, ItemDefinition item, bool selected,
            int chosenQuantity, string displayName = null)
        {
            var hasItem = !stack.IsEmpty && item != null;
            swatch.gameObject.SetActive(hasItem);
            nameText.text = hasItem
                ? (string.IsNullOrWhiteSpace(displayName) ? item.DisplayName : displayName)
                : string.Empty;
            quantityText.text = hasItem
                ? (chosenQuantity > 0 ? $"[{chosenQuantity}]"
                    : item.IsDurable ? stack.Condition.ToString()
                    : stack.Quantity.ToString())
                : string.Empty;
            if (hasItem)
            {
                swatch.sprite = item.Icon;
                swatch.preserveAspect = item.Icon != null;
                swatch.color = item.Icon != null ? Color.white : item.PlaceholderColor;
            }
            else
            {
                swatch.sprite = null;
            }
            hotbarSelected = selected;
            RefreshOutline();
        }

        public void SetDragSource(bool active)
        {
            if (canvasGroup != null) canvasGroup.alpha = active ? 0.42f : 1f;
        }

        public void SetDropTarget(bool active, bool valid)
        {
            dropTarget = active;
            validDropTarget = valid;
            RefreshOutline();
        }

        private void RefreshOutline()
        {
            if (outline == null) return;
            if (dropTarget)
            {
                outline.effectColor = validDropTarget
                    ? new Color(0.22f, 0.95f, 0.52f, 1f)
                    : new Color(1f, 0.28f, 0.25f, 1f);
                outline.effectDistance = new Vector2(5f, -5f);
                return;
            }

            outline.effectColor = hotbarSelected
                ? new Color(0.25f, 0.95f, 0.65f, 1f)
                : new Color(0.2f, 0.25f, 0.32f, 1f);
            outline.effectDistance = hotbarSelected
                ? new Vector2(4f, -4f)
                : new Vector2(2f, -2f);
        }

        public void OnPointerDown(PointerEventData eventData) => pointerPressed?.Invoke(this, eventData);
        public void OnPointerClick(PointerEventData eventData) => clicked?.Invoke(this, eventData.button);
        public void OnBeginDrag(PointerEventData eventData) => dragBegan?.Invoke(this, eventData);
        public void OnDrag(PointerEventData eventData) => dragged?.Invoke(this, eventData);
        public void OnEndDrag(PointerEventData eventData) => dragEnded?.Invoke(this, eventData);
        public void OnPointerEnter(PointerEventData eventData)
        {
            background.color = baseColor * 1.25f;
            entered?.Invoke(this);
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            background.color = baseColor;
            exited?.Invoke(this);
        }
    }
}
