using System;
using System.Collections.Generic;
using System.Text;
using Quieter.Player;
using Quieter.Inventory;
using Quieter.Survival;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Quieter.UI
{
    public sealed class SurvivalView : MonoBehaviour
    {
        private static SurvivalView current;
        private static readonly Dictionary<BodyRegion, string> RegionNames = new()
        {
            [BodyRegion.Head] = "голова",
            [BodyRegion.Neck] = "шея",
            [BodyRegion.Chest] = "грудь",
            [BodyRegion.Abdomen] = "живот",
            [BodyRegion.Pelvis] = "таз",
            [BodyRegion.LeftUpperArm] = "левое плечо",
            [BodyRegion.RightUpperArm] = "правое плечо",
            [BodyRegion.LeftForearm] = "левое предплечье",
            [BodyRegion.RightForearm] = "правое предплечье",
            [BodyRegion.LeftHand] = "левая кисть",
            [BodyRegion.RightHand] = "правая кисть",
            [BodyRegion.LeftThigh] = "левое бедро",
            [BodyRegion.RightThigh] = "правое бедро",
            [BodyRegion.LeftShin] = "левая голень",
            [BodyRegion.RightShin] = "правая голень",
            [BodyRegion.LeftFoot] = "левая стопа",
            [BodyRegion.RightFoot] = "правая стопа",
        };

        private Canvas canvas;
        private Text sensationText;
        private Text bodyText;
        private GameObject bodyPanel;
        private GameObject progressionPanel;
        private readonly Text[] progressionColumns = new Text[3];
        private RectTransform woundButtonRoot;
        private Text woundDetailsText;
        private Text treatmentStatusText;
        private readonly Dictionary<MedicalActionType, Button> treatmentButtons = new();
        private readonly Dictionary<MedicalActionType, Button> supportiveTreatmentButtons = new();
        private Button dentalExtractionButton;
        private uint selectedWoundId;
        private int woundUiSignature = int.MinValue;
        private Image screenEffect;
        private Image focusEffect;
        private Image microsleepEffect;
        private Sprite generatedFocusSprite;
        private AudioSource heartbeatAudio;
        private AudioSource breathingAudio;
        private AudioSource ringingAudio;
        private PlayerSurvival survival;
        private NetworkPlayer ownerPlayer;
        private PlayerInventory ownerInventory;
        private GameObject creationPanel;
        private GameObject deathPanel;
        private Text deathCauseText;
        private Button acceptHeirOfferButton;
        private GameObject workerBookPanel;
        private Text workerBookText;
        private readonly List<Button> workerBookButtons = new();
        private Text creationSummary;
        private Button confirmCreationButton;
        private readonly List<TraitId> selectedTraits = new();
        private readonly Dictionary<TraitId, Button> traitButtons = new();

        public static bool IsCreationOpen { get; private set; }
        public static bool IsBodyOpen { get; private set; }
        public static bool IsProgressionOpen { get; private set; }
        public static bool IsDeathOpen { get; private set; }
        public static bool IsWorkerBookOpen { get; private set; }

        private void Awake()
        {
            current = this;
            BuildInterface();
        }

        private void OnDestroy()
        {
            IsCreationOpen = false;
            IsBodyOpen = false;
            IsProgressionOpen = false;
            IsDeathOpen = false;
            IsWorkerBookOpen = false;
            if (current == this) current = null;
            if (survival != null) survival.Changed -= Refresh;
            DestroyGeneratedAudio(heartbeatAudio);
            DestroyGeneratedAudio(breathingAudio);
            DestroyGeneratedAudio(ringingAudio);
            if (generatedFocusSprite != null)
            {
                var texture = generatedFocusSprite.texture;
                Destroy(generatedFocusSprite);
                if (texture != null) Destroy(texture);
            }
        }

        private void Update()
        {
            ResolveOwner();
            if (Keyboard.current?.hKey.wasPressedThisFrame == true && survival != null)
            {
                SetBodyOpen(!bodyPanel.activeSelf);
            }
            if (Keyboard.current?.jKey.wasPressedThisFrame == true && survival != null)
            {
                SetProgressionOpen(!progressionPanel.activeSelf);
            }
            if (!IsCreationOpen && !IsDeathOpen
                && Keyboard.current?.kKey.wasPressedThisFrame == true
                && survival != null)
            {
                SetWorkerBookOpen(!workerBookPanel.activeSelf);
            }
            if (!IsCreationOpen
                && Keyboard.current?.zKey.wasPressedThisFrame == true
                && survival != null)
            {
                survival.RequestSleepToggle();
            }
            var worldInterfaceOpen = ResourceMapView.IsOpen || ResourceMapView.IsDepositOpen
                || ownerInventory?.IsInterfaceOpen == true;
            if (!IsCreationOpen && !worldInterfaceOpen
                && Keyboard.current?.vKey.wasPressedThisFrame == true
                && survival != null)
            {
                survival.RequestRelieve(false);
            }
            if (!IsCreationOpen && !worldInterfaceOpen
                && Keyboard.current?.bKey.wasPressedThisFrame == true
                && survival != null && ownerInventory?.HasFocusedCorpse != true)
            {
                survival.RequestRelieve(true);
            }
            if (!IsCreationOpen && !worldInterfaceOpen
                && Keyboard.current?.gKey.wasPressedThisFrame == true
                && survival != null)
            {
                survival.RequestWashHands();
            }
            Refresh();
        }

        private void ResolveOwner()
        {
            if (survival != null && survival.IsSpawned) return;
            foreach (var candidate in FindObjectsByType<PlayerSurvival>(FindObjectsInactive.Exclude))
            {
                if (!candidate.IsOwner) continue;
                survival = candidate;
                ownerPlayer = candidate.GetComponent<NetworkPlayer>();
                ownerInventory = candidate.GetComponent<PlayerInventory>();
                survival.Changed += Refresh;
                break;
            }
        }

        private void BuildInterface()
        {
            var root = new GameObject(
                "SurvivalInterface",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 170;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            screenEffect = new GameObject("ConditionVignette", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            screenEffect.transform.SetParent(root.transform, false);
            InventoryView.Stretch(screenEffect.rectTransform);
            screenEffect.raycastTarget = false;
            screenEffect.color = Color.clear;

            focusEffect = new GameObject("FocusLoss", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            focusEffect.transform.SetParent(root.transform, false);
            InventoryView.Stretch(focusEffect.rectTransform);
            focusEffect.raycastTarget = false;
            generatedFocusSprite = CreatePeripheralHazeSprite();
            focusEffect.sprite = generatedFocusSprite;
            focusEffect.color = Color.clear;

            microsleepEffect = new GameObject("MicrosleepCurtain", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            microsleepEffect.transform.SetParent(root.transform, false);
            InventoryView.Stretch(microsleepEffect.rectTransform);
            microsleepEffect.raycastTarget = false;
            microsleepEffect.color = Color.clear;

            heartbeatAudio = CreateConditionAudio(root.transform, "Heartbeat", CreateHeartbeatClip());
            breathingAudio = CreateConditionAudio(root.transform, "Breathing", CreateBreathingClip());
            ringingAudio = CreateConditionAudio(root.transform, "Ringing", CreateRingingClip());

            sensationText = InventoryView.CreateText(
                root.transform,
                string.Empty,
                17,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            var sensationRect = sensationText.rectTransform;
            sensationRect.anchorMin = new Vector2(0f, 1f);
            sensationRect.anchorMax = new Vector2(0f, 1f);
            sensationRect.pivot = new Vector2(0f, 1f);
            sensationRect.anchoredPosition = new Vector2(24f, -24f);
            sensationRect.sizeDelta = new Vector2(520f, 280f);
            sensationText.color = new Color(0.92f, 0.91f, 0.84f);
            sensationText.raycastTarget = false;

            bodyPanel = new GameObject("BodyPanel", typeof(RectTransform), typeof(Image));
            bodyPanel.transform.SetParent(root.transform, false);
            var panelImage = bodyPanel.GetComponent<Image>();
            panelImage.color = new Color(0.045f, 0.052f, 0.06f, 0.96f);
            var panelRect = (RectTransform)bodyPanel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1120f, 950f);
            bodyText = InventoryView.CreateText(
                bodyPanel.transform,
                string.Empty,
                19,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            bodyText.rectTransform.anchorMin = new Vector2(0f, 0f);
            bodyText.rectTransform.anchorMax = new Vector2(0f, 1f);
            bodyText.rectTransform.pivot = new Vector2(0f, 0.5f);
            bodyText.rectTransform.anchoredPosition = new Vector2(42f, 0f);
            bodyText.rectTransform.sizeDelta = new Vector2(475f, -72f);
            bodyText.color = new Color(0.9f, 0.91f, 0.9f);
            BuildMedicalControls();
            bodyPanel.SetActive(false);
            BuildProgressionPanel(root.transform);
            BuildWorkerBook(root.transform);
            BuildCharacterCreation(root.transform);
            BuildDeathPanel(root.transform);
        }

        private void BuildDeathPanel(Transform parent)
        {
            deathPanel = new GameObject("DeathPanel", typeof(RectTransform), typeof(Image));
            deathPanel.transform.SetParent(parent, false);
            deathPanel.GetComponent<Image>().color = new Color(0.025f, 0.018f, 0.018f, 0.97f);
            var rect = (RectTransform)deathPanel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 520f);
            deathCauseText = InventoryView.CreateText(
                deathPanel.transform, string.Empty, 24, FontStyle.Normal, TextAnchor.UpperCenter);
            SetTopRect(deathCauseText.rectTransform, 48f, -42f, 680f, 270f);
            acceptHeirOfferButton = CreateButton(
                deathPanel.transform,
                "ПРИНЯТЬ ПРЕДЛОЖЕННОГО НАСЛЕДНИКА",
                new Color(0.18f, 0.42f, 0.3f));
            var heirButtonRect = (RectTransform)acceptHeirOfferButton.transform;
            heirButtonRect.anchorMin = heirButtonRect.anchorMax = new Vector2(0.5f, 0f);
            heirButtonRect.pivot = new Vector2(0.5f, 0f);
            heirButtonRect.anchoredPosition = new Vector2(0f, 128f);
            heirButtonRect.sizeDelta = new Vector2(560f, 64f);
            acceptHeirOfferButton.onClick.AddListener(
                () => survival?.RequestAcceptHeirOffer());
            acceptHeirOfferButton.gameObject.SetActive(false);
            var button = CreateButton(
                deathPanel.transform, "НАЧАТЬ ЖИЗНЬ НОВОГО ЧУЖАКА", new Color(0.42f, 0.17f, 0.13f));
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 48f);
            buttonRect.sizeDelta = new Vector2(560f, 64f);
            button.onClick.AddListener(() => survival?.RequestNewStranger());
            deathPanel.SetActive(false);
        }

        private void BuildWorkerBook(Transform parent)
        {
            workerBookPanel = new GameObject(
                "WorkerBook", typeof(RectTransform), typeof(Image));
            workerBookPanel.transform.SetParent(parent, false);
            workerBookPanel.GetComponent<Image>().color = new Color(0.06f, 0.052f, 0.04f, 0.98f);
            var rect = (RectTransform)workerBookPanel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1040f, 760f);
            var title = InventoryView.CreateText(
                workerBookPanel.transform,
                "РАБОЧАЯ КНИГА",
                30,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetTopRect(title.rectTransform, 30f, -20f, 980f, 46f);
            workerBookText = InventoryView.CreateText(
                workerBookPanel.transform,
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(workerBookText.rectTransform, 48f, -82f, 944f, 230f);
            workerBookText.color = new Color(0.91f, 0.88f, 0.76f);

            var actions = new (string Label, WorkerContractAction Action)[]
            {
                ("Следующая работа", WorkerContractAction.SelectNextJob),
                ("Приоритет +", WorkerContractAction.RaiseSelectedPriority),
                ("Приоритет −", WorkerContractAction.LowerSelectedPriority),
                ("Центр зоны здесь", WorkerContractAction.SetWorkZoneHere),
                ("Склад здесь", WorkerContractAction.SetStorageHere),
                ("Зона уже", WorkerContractAction.NarrowWorkZone),
                ("Зона шире", WorkerContractAction.WidenWorkZone),
                ("Начинать раньше", WorkerContractAction.StartEarlier),
                ("Начинать позже", WorkerContractAction.StartLater),
                ("Заканчивать раньше", WorkerContractAction.EndEarlier),
                ("Заканчивать позже", WorkerContractAction.EndLater),
                ("Рацион +200", WorkerContractAction.IncreaseRation),
                ("Рацион −200", WorkerContractAction.DecreaseRation),
                ("Сменить оплату", WorkerContractAction.CyclePayment),
            };
            for (var index = 0; index < actions.Length; index++)
            {
                var captured = actions[index].Action;
                var button = CreateButton(
                    workerBookPanel.transform,
                    actions[index].Label,
                    new Color(0.24f, 0.31f, 0.25f));
                var column = index % 2;
                var row = index / 2;
                SetTopRect(
                    (RectTransform)button.transform,
                    48f + column * 482f,
                    -326f - row * 52f,
                    462f,
                    42f);
                button.onClick.AddListener(
                    () => survival?.RequestConfigureFocusedWorker(captured));
                workerBookButtons.Add(button);
            }
            var hint = InventoryView.CreateText(
                workerBookPanel.transform,
                "K / Escape — закрыть. Изменения принимает сервер только для вашего работника рядом.",
                15,
                FontStyle.Italic,
                TextAnchor.MiddleCenter);
            SetTopRect(hint.rectTransform, 35f, -710f, 970f, 28f);
            workerBookPanel.SetActive(false);
        }

        private void BuildProgressionPanel(Transform parent)
        {
            progressionPanel = new GameObject(
                "ProgressionPanel", typeof(RectTransform), typeof(Image));
            progressionPanel.transform.SetParent(parent, false);
            var image = progressionPanel.GetComponent<Image>();
            image.color = new Color(0.035f, 0.041f, 0.05f, 0.975f);
            var rect = (RectTransform)progressionPanel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1320f, 820f);

            var title = InventoryView.CreateText(
                progressionPanel.transform,
                "ОПЫТ ЭТОЙ ЖИЗНИ",
                30,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetTopRect(title.rectTransform, 30f, -22f, 1260f, 48f);
            for (var index = 0; index < progressionColumns.Length; index++)
            {
                progressionColumns[index] = InventoryView.CreateText(
                    progressionPanel.transform,
                    string.Empty,
                    16,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft);
                SetTopRect(
                    progressionColumns[index].rectTransform,
                    42f + index * 430f,
                    -88f,
                    400f,
                    680f);
                progressionColumns[index].color = new Color(0.88f, 0.89f, 0.84f);
            }
            var hint = InventoryView.CreateText(
                progressionPanel.transform,
                "J — закрыть. Навыки закрепляются сном и полностью принадлежат этому телу.",
                15,
                FontStyle.Italic,
                TextAnchor.MiddleCenter);
            SetBottomRect(hint.rectTransform, 30f, 18f, 1260f, 34f);
            hint.color = new Color(0.67f, 0.7f, 0.72f);
            progressionPanel.SetActive(false);
        }

        private void BuildMedicalControls()
        {
            var divider = new GameObject("MedicalDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(bodyPanel.transform, false);
            var dividerRect = (RectTransform)divider.transform;
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(0f, 1f);
            dividerRect.pivot = new Vector2(0f, 0.5f);
            dividerRect.anchoredPosition = new Vector2(535f, 0f);
            dividerRect.sizeDelta = new Vector2(2f, -54f);
            divider.GetComponent<Image>().color = new Color(0.25f, 0.28f, 0.3f, 0.7f);

            var heading = InventoryView.CreateText(
                bodyPanel.transform,
                "ОСМОТР И ЛЕЧЕНИЕ",
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetTopRect(heading.rectTransform, 565f, -28f, 515f, 34f);

            woundButtonRoot = new GameObject("ObservedWounds", typeof(RectTransform))
                .GetComponent<RectTransform>();
            woundButtonRoot.SetParent(bodyPanel.transform, false);
            SetTopRect(woundButtonRoot, 565f, -72f, 515f, 285f);

            woundDetailsText = InventoryView.CreateText(
                bodyPanel.transform,
                "Выберите травму.",
                15,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(woundDetailsText.rectTransform, 565f, -370f, 515f, 108f);
            woundDetailsText.color = new Color(0.88f, 0.86f, 0.76f);

            var actions = new[]
            {
                MedicalActionType.Inspect,
                MedicalActionType.ApplyPressure,
                MedicalActionType.Wash,
                MedicalActionType.Disinfect,
                MedicalActionType.Suture,
                MedicalActionType.Bandage,
                MedicalActionType.Splint,
                MedicalActionType.RemoveBandage,
            };
            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                var button = CreateButton(
                    bodyPanel.transform,
                    MedicalActionName(action),
                    new Color(0.14f, 0.25f, 0.27f));
                SetTopRect(
                    (RectTransform)button.transform,
                    565f + index % 2 * 260f,
                    -493f - index / 2 * 48f,
                    245f,
                    39f);
                var captured = action;
                button.onClick.AddListener(() => RequestTreatment(captured));
                treatmentButtons[action] = button;
            }

            dentalExtractionButton = CreateButton(
                bodyPanel.transform,
                "Удалить больной зуб",
                new Color(0.34f, 0.18f, 0.16f));
            SetTopRect(
                (RectTransform)dentalExtractionButton.transform,
                565f,
                -682f,
                505f,
                39f);
            dentalExtractionButton.onClick.AddListener(
                () => survival?.RequestDentalExtraction());

            var supportiveActions = new[]
            {
                MedicalActionType.Warm,
                MedicalActionType.Cool,
                MedicalActionType.OralRehydration,
                MedicalActionType.HerbalPainRelief,
                MedicalActionType.AntiparasiticCourse,
            };
            for (var index = 0; index < supportiveActions.Length; index++)
            {
                var action = supportiveActions[index];
                var button = CreateButton(
                    bodyPanel.transform,
                    MedicalActionName(action),
                    new Color(0.18f, 0.25f, 0.19f));
                SetTopRect(
                    (RectTransform)button.transform,
                    565f + index % 2 * 260f,
                    -729f - index / 2 * 48f,
                    245f,
                    39f);
                var captured = action;
                button.onClick.AddListener(
                    () => survival?.RequestSupportiveTreatment(captured));
                supportiveTreatmentButtons[action] = button;
            }

            treatmentStatusText = InventoryView.CreateText(
                bodyPanel.transform,
                string.Empty,
                15,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(treatmentStatusText.rectTransform, 565f, -874f, 515f, 54f);
            treatmentStatusText.color = new Color(1f, 0.77f, 0.37f);
        }

        private void RefreshMedicalControls()
        {
            if (survival == null || woundButtonRoot == null) return;
            var signature = 17;
            var selectedStillExists = false;
            for (var index = 0; index < survival.ObservedWoundCount; index++)
            {
                var wound = survival.GetObservedWound(index);
                signature = unchecked(signature * 31 + (int)wound.WoundId);
                signature = unchecked(signature * 31 + wound.SeverityStage);
                signature = unchecked(signature * 31 + wound.BleedingStage);
                signature = unchecked(signature * 31 + (byte)wound.TreatmentFlags);
                selectedStillExists |= wound.WoundId == selectedWoundId;
            }
            if (!selectedStillExists)
            {
                selectedWoundId = survival.ObservedWoundCount > 0
                    ? survival.GetObservedWound(0).WoundId
                    : 0;
            }
            if (signature != woundUiSignature)
            {
                woundUiSignature = signature;
                RebuildWoundButtons();
            }

            if (TryGetObservedWound(selectedWoundId, out var selected))
            {
                woundDetailsText.text = BuildObservedWoundDetails(selected);
            }
            else
            {
                woundDetailsText.text = "Видимых травм, требующих обработки, нет.";
            }

            var activity = survival.TreatmentActivity;
            foreach (var button in treatmentButtons.Values)
            {
                button.interactable = selectedWoundId != 0 && !activity.Active;
            }
            if (dentalExtractionButton != null)
            {
                dentalExtractionButton.interactable = !activity.Active
                    && (survival.OwnerCondition.Symptoms & SymptomFlags.Toothache) != 0;
            }
            foreach (var pair in supportiveTreatmentButtons)
            {
                var symptoms = survival.OwnerCondition.Symptoms;
                pair.Value.interactable = !activity.Active && (pair.Key switch
                {
                    MedicalActionType.Warm => (symptoms
                        & (SymptomFlags.Cold | SymptomFlags.Shivering)) != 0,
                    MedicalActionType.Cool => (symptoms
                        & (SymptomFlags.Overheated | SymptomFlags.Fever)) != 0,
                    MedicalActionType.OralRehydration => (symptoms
                        & (SymptomFlags.Thirst | SymptomFlags.DryMouth
                            | SymptomFlags.Diarrhea)) != 0,
                    MedicalActionType.HerbalPainRelief => (symptoms
                        & (SymptomFlags.Pain | SymptomFlags.SeverePain
                            | SymptomFlags.Fever | SymptomFlags.Cough)) != 0,
                    MedicalActionType.AntiparasiticCourse => (symptoms
                        & SymptomFlags.ParasiteSigns) != 0,
                    _ => false,
                });
            }
            if (activity.Active)
            {
                var now = survival.NetworkManager != null
                    ? survival.NetworkManager.ServerTime.Time
                    : Time.unscaledTimeAsDouble;
                treatmentStatusText.text = $"{MedicalActionName(activity.Action)}… "
                    + $"ещё {Mathf.Max(0f, (float)(activity.CompletesAtServerTime - now)):0.0} с";
            }
            else
            {
                treatmentStatusText.text = activity.Message.ToString();
            }
        }

        private void RebuildWoundButtons()
        {
            for (var index = woundButtonRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(woundButtonRoot.GetChild(index).gameObject);
            }
            for (var index = 0; index < survival.ObservedWoundCount; index++)
            {
                var wound = survival.GetObservedWound(index);
                var label = $"{RegionName(wound.Region)} — "
                    + (wound.TypeKnown ? InjuryName(wound.Type) : "неясная травма");
                var button = CreateButton(
                    woundButtonRoot,
                    label,
                    wound.WoundId == selectedWoundId
                        ? new Color(0.52f, 0.34f, 0.16f)
                        : new Color(0.2f, 0.17f, 0.14f));
                var rect = (RectTransform)button.transform;
                var column = index % 2;
                var row = index / 2;
                SetTopRect(rect, column * 258f, -row * 31f, 250f, 27f);
                var captured = wound.WoundId;
                button.onClick.AddListener(() =>
                {
                    selectedWoundId = captured;
                    woundUiSignature = int.MinValue;
                    RefreshMedicalControls();
                });
            }
        }

        private void RequestTreatment(MedicalActionType action)
        {
            if (survival == null || selectedWoundId == 0) return;
            survival.RequestTreatment(selectedWoundId, action);
        }

        private bool TryGetObservedWound(uint woundId, out ObservedWoundState wound)
        {
            for (var index = 0; index < survival.ObservedWoundCount; index++)
            {
                wound = survival.GetObservedWound(index);
                if (wound.WoundId == woundId) return true;
            }
            wound = default;
            return false;
        }

        private static string BuildObservedWoundDetails(ObservedWoundState wound)
        {
            var builder = new StringBuilder();
            builder.Append(RegionName(wound.Region)).Append(": ")
                .Append(wound.TypeKnown ? InjuryName(wound.Type) : "характер травмы неясен")
                .Append(". Тяжесть: ").Append(StageName(wound.SeverityStage)).Append('.');
            if (wound.BleedingStage > 0)
            {
                builder.Append(" Кровотечение: ").Append(StageName(wound.BleedingStage)).Append('.');
            }
            if (wound.ContaminationStage != byte.MaxValue)
            {
                builder.Append(" Загрязнение: ").Append(StageName(wound.ContaminationStage)).Append('.');
            }
            if (wound.InfectionStage != byte.MaxValue && wound.InfectionStage > 0)
            {
                builder.Append(" Признаки инфекции: ").Append(StageName(wound.InfectionStage)).Append('.');
            }
            if (wound.InternalBleedingSuspected)
            {
                builder.Append(" Возможное внутреннее кровотечение.");
            }
            if (wound.TreatmentFlags != ObservedTreatmentFlags.None)
            {
                builder.Append(" Выполнено: ")
                    .Append(TreatmentFlagsText(wound.TreatmentFlags)).Append('.');
            }
            return builder.ToString();
        }

        private void SetBodyOpen(bool open)
        {
            if (bodyPanel == null) return;
            if (open && IsProgressionOpen) SetProgressionOpen(false);
            if (open && IsWorkerBookOpen) SetWorkerBookOpen(false);
            bodyPanel.SetActive(open);
            IsBodyOpen = open;
            ownerPlayer?.SetInventoryInterfaceOpen(open);
            if (open)
            {
                woundUiSignature = int.MinValue;
                RefreshMedicalControls();
            }
        }

        private void SetProgressionOpen(bool open)
        {
            if (progressionPanel == null) return;
            if (open && IsBodyOpen) SetBodyOpen(false);
            if (open && IsWorkerBookOpen) SetWorkerBookOpen(false);
            progressionPanel.SetActive(open);
            IsProgressionOpen = open;
            ownerPlayer?.SetInventoryInterfaceOpen(open);
            if (open) RefreshProgression();
        }

        private void SetWorkerBookOpen(bool open)
        {
            if (workerBookPanel == null) return;
            if (open && IsBodyOpen) SetBodyOpen(false);
            if (open && IsProgressionOpen) SetProgressionOpen(false);
            workerBookPanel.SetActive(open);
            IsWorkerBookOpen = open;
            ownerPlayer?.SetInventoryInterfaceOpen(open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open) RefreshWorkerBook();
        }

        public static bool TryCloseWorkerBook()
        {
            if (!IsWorkerBookOpen || current == null) return false;
            current.SetWorkerBookOpen(false);
            return true;
        }

        private void RefreshProgression()
        {
            if (survival == null || progressionColumns[0] == null) return;
            var attributes = new StringBuilder("ХАРАКТЕРИСТИКИ\n\n");
            var firstSkills = new StringBuilder("НАВЫКИ I\n\n");
            var secondSkills = new StringBuilder("НАВЫКИ II\n\n");
            var skillOrdinal = 0;
            for (var index = 0; index < survival.ProgressionEntryCount; index++)
            {
                var entry = survival.GetProgressionEntry(index);
                if (entry.Attribute)
                {
                    attributes.Append(AttributeName((CharacterAttributeId)entry.Id))
                        .Append(": ").Append(AttributeGradeName(entry.Level)).AppendLine();
                    continue;
                }
                var target = skillOrdinal++ < 20 ? firstSkills : secondSkills;
                target.Append(SkillName((SkillId)entry.Id))
                    .Append(": ").Append(entry.Level).Append("/10")
                    .Append(entry.Level < 10 ? ProgressGlyph(entry.ProgressStage) : "  ◆")
                    .AppendLine();
            }
            progressionColumns[0].text = attributes.ToString();
            progressionColumns[1].text = firstSkills.ToString();
            progressionColumns[2].text = secondSkills.ToString();
        }

        private void BuildCharacterCreation(Transform parent)
        {
            creationPanel = new GameObject(
                "CharacterCreation",
                typeof(RectTransform),
                typeof(Image));
            creationPanel.transform.SetParent(parent, false);
            var image = creationPanel.GetComponent<Image>();
            image.color = new Color(0.035f, 0.041f, 0.05f, 0.985f);
            var rect = (RectTransform)creationPanel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1180f, 920f);

            var title = InventoryView.CreateText(
                creationPanel.transform,
                "НОВЫЙ ЧУЖАК",
                34,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetTopRect(title.rectTransform, 40f, -25f, 1100f, 54f);
            var description = InventoryView.CreateText(
                creationPanel.transform,
                "4 очка. Не более двух положительных и двух отрицательных черт. Выбор останется с этим телом до смерти.",
                18,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            description.color = new Color(0.72f, 0.75f, 0.78f);
            SetTopRect(description.rectTransform, 70f, -82f, 1040f, 48f);

            var positiveIndex = 0;
            var negativeIndex = 0;
            foreach (var definition in TraitCatalog.All)
            {
                var columnX = definition.Positive ? 55f : 605f;
                var row = definition.Positive ? positiveIndex++ : negativeIndex++;
                var sign = definition.Positive ? $"−{definition.PointValue}" : $"+{definition.PointValue}";
                var button = CreateButton(
                    creationPanel.transform,
                    $"{definition.Name}  [{sign}]",
                    definition.Positive
                        ? new Color(0.14f, 0.27f, 0.22f)
                        : new Color(0.29f, 0.16f, 0.16f));
                SetTopRect(
                    (RectTransform)button.transform,
                    columnX,
                    -148f - row * 46f,
                    520f,
                    38f);
                var captured = definition.Id;
                button.onClick.AddListener(() => ToggleTrait(captured));
                traitButtons[definition.Id] = button;
            }

            creationSummary = InventoryView.CreateText(
                creationPanel.transform,
                string.Empty,
                18,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetBottomRect(creationSummary.rectTransform, 55f, 34f, 760f, 54f);
            confirmCreationButton = CreateButton(
                creationPanel.transform,
                "НАЧАТЬ ЖИЗНЬ",
                new Color(0.18f, 0.48f, 0.36f));
            SetBottomRect(
                (RectTransform)confirmCreationButton.transform,
                850f,
                34f,
                275f,
                54f);
            confirmCreationButton.onClick.AddListener(ConfirmTraits);
            creationPanel.SetActive(false);
            RefreshTraitSelection();
        }

        private void Refresh()
        {
            if (survival == null || !survival.IsOwner)
            {
                sensationText.text = string.Empty;
                screenEffect.color = Color.clear;
                focusEffect.color = Color.clear;
                microsleepEffect.color = Color.clear;
                SilenceConditionAudio();
                return;
            }

            var state = survival.OwnerCondition;
            var lifeLost = state.LifeState == CharacterLifeState.Dead
                || state.ControlKind == CharacterControlKind.ForcedNpc;
            if (lifeLost && IsWorkerBookOpen) SetWorkerBookOpen(false);
            if (deathPanel.activeSelf != lifeLost)
            {
                deathPanel.SetActive(lifeLost);
                IsDeathOpen = lifeLost;
                Cursor.lockState = lifeLost ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = lifeLost;
            }
            if (state.ControlKind == CharacterControlKind.ForcedNpc)
            {
                deathCauseText.text = "ЭТА ЖИЗНЬ ПОТЕРЯНА\n\n"
                    + "Захват завершён: тело стало принуждённым работником пленителя.\n\n"
                    + "Новый чужак не получит опыт, карту, вещи или права этой жизни.";
            }
            else if (state.LifeState == CharacterLifeState.Dead)
            {
                deathCauseText.text = $"ЭТА ЖИЗНЬ ЗАКОНЧЕНА\n\nПричина: {DeathCauseText(state.DeathCause)}\n\n"
                    + "Тело, вещи и карта останутся там, где вы умерли. Новый чужак не получит опыт и права этой жизни.";
            }
            var heirOffer = survival.OwnerHeirOffer;
            var heirOfferActive = heirOffer.Available
                && heirOffer.ExpiresAtUtcTicks > DateTime.UtcNow.Ticks;
            acceptHeirOfferButton.gameObject.SetActive(lifeLost && heirOfferActive);
            if (lifeLost && heirOfferActive)
            {
                var remaining = TimeSpan.FromTicks(Math.Max(
                    0L, heirOffer.ExpiresAtUtcTicks - DateTime.UtcNow.Ticks));
                deathCauseText.text += $"\n\n{heirOffer.DonorName} безвозвратно предлагает наследника "
                    + $"{heirOffer.HeirName}. На решение осталось "
                    + $"{Math.Max(0, (int)remaining.TotalMinutes)} мин.";
            }
            SetCreationOpen(state.NeedsCharacterCreation);
            sensationText.gameObject.SetActive(ClientPreferences.SymptomTextEnabled);
            var sensations = BuildSensations(state);
            if (survival.HasMedicalConsentRequest)
                sensations += $"\n{survival.MedicalConsentHealerName} просит разрешение на помощь: Y — разрешить, N — отказать.";
            if (!string.IsNullOrWhiteSpace(survival.ExternalMedicalMessage))
                sensations += "\n" + survival.ExternalMedicalMessage;
            if (!string.IsNullOrWhiteSpace(survival.FocusedMedicalTargetName))
            {
                sensations += $"\nЦель: {survival.FocusedMedicalTargetName}. T — осмотр; F1–F7 — лечение; F8 — путы; F9 — перенос; F10 — начать захват в камере.";
                if (survival.FocusedTargetLifeState == CharacterLifeState.Dead
                    || survival.FocusedTargetControlKind == CharacterControlKind.ForcedNpc)
                    sensations += " Shift+F12 — безвозвратно предложить ей своего зарегистрированного наследника.";
                if (survival.FocusedTargetControlKind is CharacterControlKind.FreeNpc
                    or CharacterControlKind.ContractedNpc or CharacterControlKind.ForcedNpc)
                {
                    sensations += $"\nНаблюдение: {NpcActivityName(survival.FocusedTargetActivity)}";
                    if (survival.FocusedTargetControlKind == CharacterControlKind.FreeNpc)
                        sensations += ". F11 — предложить договор за паёк.";
                    else
                    {
                        sensations += $", работа — {WorkerJobName(survival.FocusedTargetJob)}. K — рабочая книга; F12 — выбрать следующую работу; Shift+F11 — назначить наследником.";
                        if (survival.FocusedTargetHasPersonalRequest)
                            sensations += $" F11 — исполнить запрос: {PersonalRequestName(survival.FocusedTargetPersonalRequest)}.";
                    }
                    if (survival.FocusedTargetHasWorkEvidence)
                    {
                        var range = survival.FocusedTargetSkillRange;
                        sensations += $" По выполненным задачам: {NpcSkillEstimate(range.Minimum, range.Maximum)}.";
                    }
                }
            }
            sensationText.text = sensations.Trim();
            bodyText.text = BuildBodyReport(state);
            RefreshMedicalControls();
            RefreshProgression();
            RefreshWorkerBook();
            RefreshConditionEffects(state);
        }

        private void RefreshWorkerBook()
        {
            if (workerBookPanel == null || !workerBookPanel.activeSelf || survival == null) return;
            var contract = survival.FocusedTargetWorkerContract;
            var targetName = survival.FocusedMedicalTargetName;
            var available = !string.IsNullOrWhiteSpace(targetName) && contract.Active;
            foreach (var button in workerBookButtons) button.interactable = available;
            if (!available)
            {
                workerBookText.text = "Посмотрите на своего действующего работника с расстояния до нескольких шагов.\n\n"
                    + "Рабочая книга не раскрывает скрытые навыки и здоровье: они по-прежнему узнаются "
                    + "по наблюдению, обучению и осмотру.";
                return;
            }
            var loyalty = contract.Voluntary ? "добровольный договор" : "принуждение";
            workerBookText.text = $"{targetName} — {loyalty}\n"
                + $"Сейчас выполняется: {WorkerJobName(contract.ActiveJob)}. "
                + $"Выбрано для настройки: {WorkerJobName(contract.SelectedJob)}, "
                + $"приоритет {contract.SelectedPriority}/3.\n"
                + $"Рабочее время: {contract.WorkdayStartHour:00}:00–{contract.WorkdayEndHour:00}:00. "
                + $"Рацион: {contract.DailyRationCalories} ккал/сутки. "
                + $"Оплата: {PaymentItemName(contract.PaymentItemId)} ×{contract.PaymentQuantity}.\n"
                + $"Зона: радиус {contract.WorkZoneRadius:0} м от "
                + $"X {contract.WorkZoneCenter.x:0}, Z {contract.WorkZoneCenter.z:0}. "
                + $"Склад: X {contract.StoragePosition.x:0}, Z {contract.StoragePosition.z:0}.\n"
                + $"Нарушений условий подряд: {contract.ConsecutiveBreaches}. "
                + "Нулевой приоритет отключает работу; невыплаченная оплата считается нарушением.";
        }

        private void RefreshConditionEffects(OwnerConditionState state)
        {
            var symptoms = state.Symptoms;
            if (!ClientPreferences.ScreenEffectsEnabled)
            {
                screenEffect.color = Color.clear;
            }
            else if (state.LifeState >= CharacterLifeState.Unconscious)
            {
                screenEffect.color = new Color(0.01f, 0.01f, 0.015f, 0.82f);
            }
            else if ((symptoms & SymptomFlags.TunnelVision) != 0)
            {
                screenEffect.color = new Color(0.12f, 0.01f, 0.015f, 0.24f);
            }
            else if ((symptoms & (SymptomFlags.Dizzy | SymptomFlags.Confusion)) != 0)
            {
                screenEffect.color = new Color(0.06f, 0.06f, 0.08f, 0.12f);
            }
            else
            {
                screenEffect.color = Color.clear;
            }

            if (!ClientPreferences.FocusEffectsEnabled
                || state.LifeState >= CharacterLifeState.Unconscious)
            {
                focusEffect.color = Color.clear;
            }
            else
            {
                var focusLoss = (symptoms & SymptomFlags.TunnelVision) != 0
                    ? 0.72f
                    : (symptoms & SymptomFlags.Dizzy) != 0
                        ? 0.34f
                        : (symptoms & SymptomFlags.SeverePain) != 0
                            ? 0.18f
                            : 0f;
                focusEffect.color = new Color(0.025f, 0.028f, 0.035f, focusLoss);
            }

            var curtainAlpha = 0f;
            if (ClientPreferences.FlashingEffectsEnabled
                && state.LifeState < CharacterLifeState.Unconscious)
            {
                if ((symptoms & SymptomFlags.Microsleep) != 0)
                {
                    var cycle = Mathf.Repeat(Time.unscaledTime, 6.5f);
                    if (cycle < 0.22f) curtainAlpha = Mathf.SmoothStep(0f, 0.92f, cycle / 0.22f);
                    else if (cycle < 0.72f) curtainAlpha = Mathf.SmoothStep(0.92f, 0f, (cycle - 0.22f) / 0.5f);
                }
                else if ((symptoms & SymptomFlags.LosingConsciousness) != 0)
                {
                    curtainAlpha = (Mathf.Sin(Time.unscaledTime * 1.25f) + 1f) * 0.16f;
                }
            }
            microsleepEffect.color = new Color(0.005f, 0.006f, 0.009f, curtainAlpha);
            RefreshConditionAudio(state);
        }

        private void RefreshConditionAudio(OwnerConditionState state)
        {
            var enabled = ClientPreferences.ConditionAudioEnabled
                && state.LifeState != CharacterLifeState.Dead;
            var symptoms = state.Symptoms;
            var heartbeat = 0f;
            var breathing = 0f;
            var ringing = 0f;
            if (enabled)
            {
                if ((symptoms & SymptomFlags.Panic) != 0) heartbeat += 0.22f;
                if ((symptoms & SymptomFlags.SeverePain) != 0) heartbeat += 0.2f;
                if ((symptoms & SymptomFlags.TunnelVision) != 0) heartbeat += 0.32f;
                if ((symptoms & SymptomFlags.Breathless) != 0) breathing += 0.32f;
                if ((symptoms & SymptomFlags.Cough) != 0) breathing += 0.12f;
                if ((symptoms & SymptomFlags.AgonalBreathing) != 0) breathing += 0.45f;
                if ((symptoms & SymptomFlags.Dizzy) != 0) ringing += 0.08f;
                if ((symptoms & SymptomFlags.TunnelVision) != 0) ringing += 0.18f;
            }

            FadeAudio(heartbeatAudio, Mathf.Clamp(heartbeat, 0f, 0.5f));
            FadeAudio(breathingAudio, Mathf.Clamp(breathing, 0f, 0.5f));
            FadeAudio(ringingAudio, Mathf.Clamp(ringing, 0f, 0.28f));
            if (heartbeatAudio != null)
            {
                heartbeatAudio.pitch = (symptoms & SymptomFlags.Panic) != 0 ? 1.28f : 1f;
            }
        }

        private void SilenceConditionAudio()
        {
            FadeAudio(heartbeatAudio, 0f);
            FadeAudio(breathingAudio, 0f);
            FadeAudio(ringingAudio, 0f);
        }

        private static void FadeAudio(AudioSource source, float target)
        {
            if (source == null) return;
            source.volume = Mathf.MoveTowards(
                source.volume,
                target,
                Mathf.Max(0.02f, Time.unscaledDeltaTime * 0.65f));
        }

        private static AudioSource CreateConditionAudio(
            Transform parent,
            string name,
            AudioClip clip)
        {
            var root = new GameObject(name, typeof(AudioSource));
            root.transform.SetParent(parent, false);
            var source = root.GetComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.Play();
            return source;
        }

        private static void DestroyGeneratedAudio(AudioSource source)
        {
            if (source != null && source.clip != null) Destroy(source.clip);
        }

        private static Sprite CreatePeripheralHazeSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "QuieterPeripheralHaze",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var nx = (x + 0.5f) / size * 2f - 1f;
                    var ny = (y + 0.5f) / size * 2f - 1f;
                    var radius = Mathf.Sqrt(nx * nx + ny * ny * 0.62f);
                    var alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 1.05f, radius));
                    pixels[x + y * size] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private static AudioClip CreateHeartbeatClip()
        {
            const int sampleRate = 11025;
            const int seconds = 2;
            var data = new float[sampleRate * seconds];
            AddPulse(data, sampleRate, 0.08f, 0.13f, 58f, 0.72f);
            AddPulse(data, sampleRate, 0.34f, 0.1f, 52f, 0.46f);
            var clip = AudioClip.Create("QuieterHeartbeat", data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void AddPulse(
            float[] data,
            int sampleRate,
            float startSeconds,
            float durationSeconds,
            float frequency,
            float volume)
        {
            var start = Mathf.RoundToInt(startSeconds * sampleRate);
            var count = Mathf.RoundToInt(durationSeconds * sampleRate);
            for (var index = 0; index < count && start + index < data.Length; index++)
            {
                var t = index / (float)sampleRate;
                var envelope = Mathf.Exp(-t * 28f);
                data[start + index] += Mathf.Sin(t * Mathf.PI * 2f * frequency) * envelope * volume;
            }
        }

        private static AudioClip CreateBreathingClip()
        {
            const int sampleRate = 11025;
            const int seconds = 4;
            var data = new float[sampleRate * seconds];
            for (var index = 0; index < data.Length; index++)
            {
                var t = index / (float)sampleRate;
                var envelope = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 0.5f)), 1.7f);
                var breath = Mathf.Sin(t * Mathf.PI * 2f * 120f)
                    + Mathf.Sin(t * Mathf.PI * 2f * 173f) * 0.45f;
                data[index] = breath * envelope * 0.16f;
            }
            var clip = AudioClip.Create("QuieterBreathing", data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateRingingClip()
        {
            const int sampleRate = 11025;
            const int seconds = 2;
            var data = new float[sampleRate * seconds];
            for (var index = 0; index < data.Length; index++)
            {
                var t = index / (float)sampleRate;
                data[index] = (Mathf.Sin(t * Mathf.PI * 2f * 742f)
                    + Mathf.Sin(t * Mathf.PI * 2f * 751f) * 0.45f) * 0.12f;
            }
            var clip = AudioClip.Create("QuieterRinging", data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static string PersonalRequestName(NpcPersonalRequestKind request) => request switch
        {
            NpcPersonalRequestKind.Food => "еда",
            NpcPersonalRequestKind.CleanWater => "чистая вода",
            NpcPersonalRequestKind.Medicine => "медицинский запас",
            NpcPersonalRequestKind.Tool => "инструмент",
            _ => "помощь",
        };

        private void ToggleTrait(TraitId trait)
        {
            if (selectedTraits.Contains(trait))
            {
                selectedTraits.Remove(trait);
            }
            else if (selectedTraits.Count < 4)
            {
                selectedTraits.Add(trait);
            }
            RefreshTraitSelection();
        }

        private void RefreshTraitSelection()
        {
            var valid = TraitCatalog.TryValidate(
                selectedTraits,
                out var remaining,
                out var error);
            if (creationSummary != null)
            {
                creationSummary.text = valid
                    ? $"Осталось очков: {remaining}. Неиспользованные очки сгорят."
                    : error;
                creationSummary.color = valid
                    ? new Color(0.85f, 0.84f, 0.72f)
                    : new Color(1f, 0.55f, 0.48f);
            }
            if (confirmCreationButton != null) confirmCreationButton.interactable = valid;
            foreach (var pair in traitButtons)
            {
                var selected = selectedTraits.Contains(pair.Key);
                pair.Value.GetComponent<Image>().color = selected
                    ? new Color(0.62f, 0.48f, 0.17f)
                    : TraitCatalog.Get(pair.Key).Positive
                        ? new Color(0.14f, 0.27f, 0.22f)
                        : new Color(0.29f, 0.16f, 0.16f);
            }
        }

        private void ConfirmTraits()
        {
            if (survival == null) return;
            if (!TraitCatalog.TryValidate(selectedTraits, out _, out _)) return;
            survival.RequestTraitSelection(selectedTraits);
        }

        private void SetCreationOpen(bool open)
        {
            if (creationPanel == null || creationPanel.activeSelf == open) return;
            if (open && IsBodyOpen) SetBodyOpen(false);
            if (open && IsProgressionOpen) SetProgressionOpen(false);
            if (open && IsWorkerBookOpen) SetWorkerBookOpen(false);
            creationPanel.SetActive(open);
            IsCreationOpen = open;
            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static string BuildSensations(OwnerConditionState state)
        {
            var lines = new List<string>();
            if (state.Sleeping) lines.Add("Вы спите. Z — проснуться.");
            if (state.Bound) lines.Add("Руки стянуты путами: обычные действия недоступны.");
            if (state.Captive)
                lines.Add("Вы удерживаетесь в запертой камере. Освобождение или открытая дверь прервут захват.");
            if (state.BeingCarried) lines.Add("Вас переносит другой человек.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Thirst, "Хочется пить.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.DryMouth, "Во рту совсем сухо.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Hunger, "Желудок сводит от голода.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Weakness, "Тело заметно ослабло.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Fatigue, "Глаза слипаются.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Microsleep, "Сознание проваливается на мгновения.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Shivering, "Дрожь невозможно остановить.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Overheated, "Жар становится невыносимым.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Bleeding, "Чувствуется продолжающееся кровотечение.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.SeverePain, "Боль мешает думать и двигаться.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Fever, "Лихорадит.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Breathless, "Не хватает воздуха.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.SmokeIrritation,
                "Дым режет глаза и провоцирует кашель.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Cough,
                "Кашель становится всё глубже.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.AbdominalCramps,
                "Живот болезненно сводит.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Diarrhea,
                "Кишечник не удерживает воду.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.ParasiteSigns,
                "Пища насыщает всё хуже, тело постепенно истощается.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.NutritionalDeficiency,
                "Кожа бледная, восстановление замедлилось — рацион слишком однообразен.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.BladderPressure, "Нужно помочиться.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.BowelPressure, "Нужно опорожнить кишечник.");
            AddSymptom(lines, state.Symptoms, SymptomFlags.Toothache, "Ноет зуб.");
            if (state.LifeState == CharacterLifeState.Dead)
            {
                lines.Clear();
                lines.Add($"Смерть: {DeathCauseText(state.DeathCause)}");
            }
            return string.Join("\n", lines);
        }

        private static string BuildBodyReport(OwnerConditionState state)
        {
            var builder = new StringBuilder();
            builder.AppendLine("СОСТОЯНИЕ ТЕЛА");
            builder.AppendLine("Только ощущения и наблюдаемые признаки — скрытых показателей нет.");
            builder.AppendLine();
            if (state.WoundedRegions == 0)
            {
                builder.AppendLine("Явных травм не обнаружено.");
            }
            else
            {
                builder.AppendLine("Травмированные области:");
                foreach (var pair in RegionNames)
                {
                    var mask = 1u << (int)pair.Key;
                    if ((state.WoundedRegions & mask) == 0) continue;
                    builder.Append("• ").Append(pair.Value);
                    if ((state.FracturedRegions & mask) != 0)
                    {
                        builder.Append(" — возможен перелом");
                    }
                    builder.AppendLine();
                }
            }
            AppendConditionAssessment(builder, state);
            builder.AppendLine();
            builder.AppendLine("H — закрыть панель");
            builder.AppendLine("Z — лечь спать или проснуться");
            builder.AppendLine("V — опорожнить мочевой пузырь; B — кишечник");
            builder.AppendLine("G — вымыть руки водой с мылом");
            builder.AppendLine("Уход за другим: Shift+F1…F6 — тепло, холод, раствор, травы, зуб, курс.");
            builder.AppendLine("Лечение справа выполняется не мгновенно и расходует материалы.");
            return builder.ToString();
        }

        private static void AppendConditionAssessment(
            StringBuilder builder,
            OwnerConditionState state)
        {
            var gastrointestinalSymptoms = (state.Symptoms
                & (SymptomFlags.AbdominalCramps | SymptomFlags.Diarrhea
                    | SymptomFlags.Nausea)) != 0;
            var respiratorySymptoms = (state.Symptoms
                & (SymptomFlags.Cough | SymptomFlags.Breathless)) != 0;
            var parasiteSymptoms = (state.Symptoms & SymptomFlags.ParasiteSigns) != 0;
            if (!gastrointestinalSymptoms && !respiratorySymptoms && !parasiteSymptoms)
                return;

            builder.AppendLine();
            builder.AppendLine("Предполагаемые внутренние состояния:");
            if (gastrointestinalSymptoms)
            {
                builder.Append("• кишечное расстройство");
                builder.AppendLine(state.GastrointestinalStage == byte.MaxValue
                    ? " — причина и тяжесть пока неясны"
                    : $" — вероятная инфекция, {StageName(state.GastrointestinalStage)}");
            }
            if (respiratorySymptoms)
            {
                builder.Append("• нарушение дыхания");
                builder.AppendLine(state.RespiratoryStage == byte.MaxValue
                    ? " — дым, болезнь или травму ещё нужно различить"
                    : $" — вероятная дыхательная инфекция, {StageName(state.RespiratoryStage)}");
            }
            if (parasiteSymptoms)
            {
                builder.Append("• длительное нарушение усвоения пищи");
                builder.AppendLine(state.ParasiteStage == byte.MaxValue
                    ? " — без навыка причина неясна"
                    : $" — вероятна паразитарная нагрузка, {StageName(state.ParasiteStage)}");
            }
        }

        private static void AddSymptom(
            ICollection<string> lines,
            SymptomFlags value,
            SymptomFlags flag,
            string text)
        {
            if ((value & flag) != 0) lines.Add(text);
        }

        private static string DeathCauseText(DeathCause cause) => cause switch
        {
            DeathCause.BloodLoss => "критическая кровопотеря",
            DeathCause.RespiratoryFailure => "остановка дыхательной функции",
            DeathCause.BrainFailure => "необратимое повреждение мозга",
            DeathCause.CardiacFailure => "отказ сердца",
            DeathCause.Hypothermia => "переохлаждение",
            DeathCause.Hyperthermia => "перегрев",
            DeathCause.Dehydration => "обезвоживание и отказ почек",
            DeathCause.Starvation => "крайнее истощение",
            DeathCause.Sepsis => "заражение крови",
            DeathCause.Poisoning => "отравление и отказ органов",
            DeathCause.MultipleOrganFailure => "полиорганная недостаточность",
            _ => "причина требует осмотра",
        };

        private static string RegionName(BodyRegion region)
            => RegionNames.TryGetValue(region, out var value) ? value : "неизвестная область";

        private static string InjuryName(InjuryType type) => type switch
        {
            InjuryType.Abrasion => "ссадина",
            InjuryType.Laceration => "резаная рана",
            InjuryType.Puncture => "колотая рана",
            InjuryType.Contusion => "ушиб",
            InjuryType.Sprain => "растяжение",
            InjuryType.ClosedFracture => "закрытый перелом",
            InjuryType.OpenFracture => "открытый перелом",
            InjuryType.Burn => "ожог",
            _ => "травма",
        };

        private static string StageName(byte stage) => stage switch
        {
            0 => "незначительная",
            1 => "лёгкая",
            2 => "умеренная",
            3 => "тяжёлая",
            _ => "критическая",
        };

        private static string MedicalActionName(MedicalActionType action) => action switch
        {
            MedicalActionType.Inspect => "Осмотреть",
            MedicalActionType.ApplyPressure => "Прижать рану",
            MedicalActionType.Wash => "Промыть",
            MedicalActionType.Disinfect => "Обработать",
            MedicalActionType.Suture => "Наложить швы",
            MedicalActionType.Bandage => "Перевязать",
            MedicalActionType.Splint => "Наложить шину",
            MedicalActionType.RemoveBandage => "Снять повязку",
            MedicalActionType.DentalExtraction => "Удалить зуб",
            MedicalActionType.Warm => "Постепенно согреть",
            MedicalActionType.Cool => "Охладить водой",
            MedicalActionType.OralRehydration => "Солевой раствор",
            MedicalActionType.HerbalPainRelief => "Травяной состав",
            MedicalActionType.AntiparasiticCourse => "Курс от паразитов",
            _ => "Лечить",
        };

        private static string TreatmentFlagsText(ObservedTreatmentFlags flags)
        {
            var values = new List<string>();
            if ((flags & ObservedTreatmentFlags.Pressure) != 0) values.Add("кровь прижата");
            if ((flags & ObservedTreatmentFlags.Washed) != 0) values.Add("промыто");
            if ((flags & ObservedTreatmentFlags.Disinfected) != 0) values.Add("обработано");
            if ((flags & ObservedTreatmentFlags.Sutured) != 0) values.Add("наложены швы");
            if ((flags & ObservedTreatmentFlags.Bandaged) != 0) values.Add("повязка");
            if ((flags & ObservedTreatmentFlags.Splinted) != 0) values.Add("шина");
            return string.Join(", ", values);
        }

        private static string NpcActivityName(NpcActivityKind value) => value switch
        {
            NpcActivityKind.Wander => "бродит поблизости",
            NpcActivityKind.SeekWater => "ищет воду",
            NpcActivityKind.SeekFood => "ищет пищу",
            NpcActivityKind.Rest => "отдыхает",
            NpcActivityKind.Work => "работает",
            NpcActivityKind.Flee => "пытается уйти от опасности",
            NpcActivityKind.Sabotage => "ведёт себя подозрительно",
            NpcActivityKind.Combat => "готовится к нападению",
            _ => "бездействует",
        };

        private static string WorkerJobName(WorkerJobKind value) => value switch
        {
            WorkerJobKind.Mining => "добыча",
            WorkerJobKind.Logging => "рубка",
            WorkerJobKind.Foraging => "сбор",
            WorkerJobKind.Hauling => "переноска",
            WorkerJobKind.Construction => "строительство",
            WorkerJobKind.CookingAndWater => "готовка и вода",
            WorkerJobKind.Sanitation => "санитария",
            WorkerJobKind.PatientCare => "уход за больными",
            _ => "неизвестно",
        };

        private static string PaymentItemName(ushort itemId) => itemId switch
        {
            25 => "ягоды",
            26 => "коренья",
            28 => "лекарственные травы",
            _ => itemId == 0 ? "не назначена" : $"предмет {itemId}",
        };

        private static string NpcSkillEstimate(byte minimum, byte maximum)
        {
            if (maximum - minimum >= 5) return "опыта пока недостаточно для уверенной оценки";
            var midpoint = (minimum + maximum) * 0.5f;
            return midpoint switch
            {
                < 2f => "явно неопытен в этой работе",
                < 4f => "имеет начальный опыт",
                < 6f => "работает уверенно",
                < 8f => "очень умелый работник",
                _ => "похоже, настоящий мастер",
            };
        }

        private static string AttributeGradeName(byte grade) => grade switch
        {
            1 => "крайне слабая",
            2 => "очень слабая",
            3 => "слабая",
            4 => "ниже средней",
            5 => "обычная",
            6 => "развитая",
            7 => "сильная",
            8 => "очень сильная",
            9 => "выдающаяся",
            _ => "предельная",
        };

        private static string ProgressGlyph(byte stage) => stage switch
        {
            0 => "  ·",
            1 => "  ◔",
            2 => "  ◑",
            3 => "  ◕",
            _ => "  ●",
        };

        private static string AttributeName(CharacterAttributeId id) => id switch
        {
            CharacterAttributeId.Strength => "Сила",
            CharacterAttributeId.MuscularEndurance => "Мышечная выносливость",
            CharacterAttributeId.AerobicCapacity => "Аэробная форма",
            CharacterAttributeId.Mobility => "Подвижность",
            CharacterAttributeId.Balance => "Равновесие",
            CharacterAttributeId.Coordination => "Координация",
            CharacterAttributeId.FineMotorControl => "Точная моторика",
            CharacterAttributeId.Perception => "Восприятие",
            CharacterAttributeId.Memory => "Память",
            CharacterAttributeId.Reasoning => "Мышление",
            CharacterAttributeId.Willpower => "Воля",
            CharacterAttributeId.Constitution => "Конституция",
            _ => id.ToString(),
        };

        private static string SkillName(SkillId id) => id switch
        {
            SkillId.Running => "Бег",
            SkillId.LoadCarrying => "Перенос грузов",
            SkillId.Landing => "Приземление",
            SkillId.RestraintEscape => "Освобождение от пут",
            SkillId.UnarmedCombat => "Рукопашный бой",
            SkillId.BluntWeapons => "Дробящее оружие",
            SkillId.EdgedWeapons => "Рубящее оружие",
            SkillId.Polearms => "Древковое оружие",
            SkillId.Defence => "Защита",
            SkillId.Foraging => "Собирательство",
            SkillId.Woodcutting => "Рубка",
            SkillId.Mining => "Горное дело",
            SkillId.Excavation => "Земляные работы",
            SkillId.Carpentry => "Плотничество",
            SkillId.Masonry => "Каменная кладка",
            SkillId.Pottery => "Гончарство",
            SkillId.Toolmaking => "Изготовление инструментов",
            SkillId.CordageAndTextiles => "Верёвки и текстиль",
            SkillId.Construction => "Строительство",
            SkillId.Firekeeping => "Обращение с огнём",
            SkillId.Cooking => "Готовка",
            SkillId.WaterSafety => "Безопасность воды",
            SkillId.Sanitation => "Санитария",
            SkillId.Navigation => "Навигация",
            SkillId.Cartography => "Картография",
            SkillId.Geology => "Геология",
            SkillId.Botany => "Ботаника",
            SkillId.WeatherReading => "Чтение погоды",
            SkillId.Diagnosis => "Диагностика",
            SkillId.FirstAid => "Первая помощь",
            SkillId.WoundCare => "Обработка ран",
            SkillId.Suturing => "Швы",
            SkillId.Bonesetting => "Костоправство",
            SkillId.SurgeryAndDentistry => "Хирургия и стоматология",
            SkillId.HerbalMedicine => "Травничество",
            SkillId.Nursing => "Уход за больными",
            SkillId.Persuasion => "Убеждение",
            SkillId.Intimidation => "Запугивание",
            SkillId.Leadership => "Руководство",
            SkillId.Teaching => "Обучение",
            _ => id.ToString(),
        };

        private static Button CreateButton(Transform parent, string label, Color color)
        {
            var gameObject = new GameObject(
                "TraitButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            gameObject.transform.SetParent(parent, false);
            gameObject.GetComponent<Image>().color = color;
            var text = InventoryView.CreateText(
                gameObject.transform,
                label,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            InventoryView.Stretch(text.rectTransform);
            return gameObject.GetComponent<Button>();
        }

        private static void SetTopRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetBottomRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
