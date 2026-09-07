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
        private uint selectedWoundId;
        private int woundUiSignature = int.MinValue;
        private Image screenEffect;
        private PlayerSurvival survival;
        private NetworkPlayer ownerPlayer;
        private PlayerInventory ownerInventory;
        private GameObject creationPanel;
        private Text creationSummary;
        private Button confirmCreationButton;
        private readonly List<TraitId> selectedTraits = new();
        private readonly Dictionary<TraitId, Button> traitButtons = new();

        public static bool IsCreationOpen { get; private set; }
        public static bool IsBodyOpen { get; private set; }
        public static bool IsProgressionOpen { get; private set; }

        private void Awake()
        {
            BuildInterface();
        }

        private void OnDestroy()
        {
            IsCreationOpen = false;
            IsBodyOpen = false;
            IsProgressionOpen = false;
            if (survival != null) survival.Changed -= Refresh;
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
                && survival != null)
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
            panelRect.sizeDelta = new Vector2(1120f, 780f);
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
            BuildCharacterCreation(root.transform);
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

            treatmentStatusText = InventoryView.CreateText(
                bodyPanel.transform,
                string.Empty,
                15,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(treatmentStatusText.rectTransform, 565f, -692f, 515f, 62f);
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
            progressionPanel.SetActive(open);
            IsProgressionOpen = open;
            ownerPlayer?.SetInventoryInterfaceOpen(open);
            if (open) RefreshProgression();
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
                return;
            }

            var state = survival.OwnerCondition;
            SetCreationOpen(state.NeedsCharacterCreation);
            sensationText.gameObject.SetActive(ClientPreferences.SymptomTextEnabled);
            sensationText.text = BuildSensations(state);
            bodyText.text = BuildBodyReport(state);
            RefreshMedicalControls();
            RefreshProgression();
            if (!ClientPreferences.ScreenEffectsEnabled)
            {
                screenEffect.color = Color.clear;
            }
            else if (state.LifeState >= CharacterLifeState.Unconscious)
            {
                screenEffect.color = new Color(0.01f, 0.01f, 0.015f, 0.72f);
            }
            else if ((state.Symptoms & SymptomFlags.TunnelVision) != 0)
            {
                screenEffect.color = new Color(0.12f, 0.01f, 0.015f, 0.28f);
            }
            else if ((state.Symptoms & SymptomFlags.Dizzy) != 0)
            {
                screenEffect.color = new Color(0.06f, 0.06f, 0.08f, 0.12f);
            }
            else
            {
                screenEffect.color = Color.clear;
            }
        }

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
            builder.AppendLine();
            builder.AppendLine("H — закрыть панель");
            builder.AppendLine("Z — лечь спать или проснуться");
            builder.AppendLine("V — опорожнить мочевой пузырь; B — кишечник");
            builder.AppendLine("G — вымыть руки водой с мылом");
            builder.AppendLine("Лечение справа выполняется не мгновенно и расходует материалы.");
            return builder.ToString();
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
