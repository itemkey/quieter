using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Quieter.Inventory;
using Quieter.World;
using Quieter.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Quieter.Tests
{
    public sealed class InventoryInterfacePlayModeTests
    {
        [UnityTest]
        public IEnumerator InventoryAndWorkbenchModes_SwitchWithoutKeepingBothLayouts()
        {
            var root = new GameObject("InventoryViewTest");
            root.AddComponent<InventoryView>();
            yield return null;

            var inventory = Find(root, "InventoryPanel");
            var workbench = Find(root, "WorkbenchPanel");
            var recipes = Find(root, "RecipePanel");
            var itemInfo = Find(root, "ItemInfoCard");
            var draggedStack = Find(root, "DraggedStack");
            var dismissArea = Find(root, "InventoryDismissArea");
            Assert.That(inventory, Is.Not.Null);
            Assert.That(workbench, Is.Not.Null);
            Assert.That(recipes, Is.Not.Null);
            Assert.That(itemInfo, Is.Not.Null);
            Assert.That(itemInfo.activeSelf, Is.False);
            Assert.That(draggedStack, Is.Not.Null);
            Assert.That(draggedStack.activeSelf, Is.False);
            Assert.That(dismissArea, Is.Not.Null);

            InventoryView.SetMode(true, false);
            Assert.That(inventory.activeSelf, Is.True);
            Assert.That(workbench.activeSelf, Is.False);
            Assert.That(recipes.activeSelf, Is.False);

            InventoryView.SetMode(true, true);
            Assert.That(inventory.activeSelf, Is.True);
            Assert.That(workbench.activeSelf, Is.True);
            Assert.That(recipes.activeSelf, Is.True);

            InventoryView.SetMode(false, false);
            Assert.That(inventory.activeSelf, Is.False);
            Assert.That(workbench.activeSelf, Is.False);
            Assert.That(recipes.activeSelf, Is.False);
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecipePanel_SelectsRecipesAndResetsAfterClosingWorkbench()
        {
            InventoryView.SetMode(false, false);
            var root = new GameObject("RecipePanelTest");
            root.AddComponent<InventoryView>();
            yield return null;

            InventoryView.SetMode(true, true);
            var category = Find(root, "RecipeCategory_Tools");
            var axeButtonObject = Find(root, "RecipeButton_1");
            var pickaxeButtonObject = Find(root, "RecipeButton_2");
            var shovelButtonObject = Find(root, "RecipeButton_3");
            var details = Find(root, "RecipeDetails");
            var detailsText = Find(root, "RecipeDetailsText").GetComponent<Text>();
            var selectionHint = Find(root, "RecipeSelectionHint");
            var craftButtonObject = Find(root, "CraftRecipeButton");
            Assert.That(category, Is.Not.Null);
            Assert.That(category.GetComponentInChildren<Text>().text, Is.EqualTo("ИНСТРУМЕНТЫ"));
            Assert.That(axeButtonObject, Is.Not.Null);
            Assert.That(pickaxeButtonObject, Is.Not.Null);
            Assert.That(shovelButtonObject, Is.Not.Null);
            Assert.That(details.activeSelf, Is.False);
            Assert.That(craftButtonObject.activeSelf, Is.False);
            Assert.That(selectionHint.activeSelf, Is.True);

            var axeButton = axeButtonObject.GetComponent<Button>();
            var pickaxeButton = pickaxeButtonObject.GetComponent<Button>();
            var defaultAxeColor = axeButton.targetGraphic.color;
            var defaultPickaxeColor = pickaxeButton.targetGraphic.color;
            axeButton.onClick.Invoke();
            Assert.That(details.activeSelf, Is.True);
            Assert.That(craftButtonObject.activeSelf, Is.True);
            Assert.That(craftButtonObject.GetComponent<Button>().interactable, Is.False);
            Assert.That(selectionHint.activeSelf, Is.False);
            StringAssert.Contains("ПРИМИТИВНЫЙ ТОПОР", detailsText.text);
            StringAssert.Contains("3 × Дерево", detailsText.text);
            StringAssert.Contains("2 × Камень", detailsText.text);
            StringAssert.Contains("1 × Верёвка", detailsText.text);
            Assert.That(axeButton.targetGraphic.color, Is.Not.EqualTo(defaultAxeColor));
            Assert.That(pickaxeButton.targetGraphic.color, Is.EqualTo(defaultPickaxeColor));

            pickaxeButton.onClick.Invoke();
            StringAssert.Contains("ПРИМИТИВНАЯ КИРКА", detailsText.text);
            StringAssert.Contains("2 × Дерево", detailsText.text);
            StringAssert.Contains("3 × Камень", detailsText.text);
            Assert.That(axeButton.targetGraphic.color, Is.EqualTo(defaultAxeColor));
            Assert.That(pickaxeButton.targetGraphic.color, Is.Not.EqualTo(defaultPickaxeColor));

            shovelButtonObject.GetComponent<Button>().onClick.Invoke();
            StringAssert.Contains("ПРИМИТИВНАЯ ЛОПАТА", detailsText.text);
            StringAssert.Contains("2 × Дерево", detailsText.text);
            StringAssert.Contains("2 × Камень", detailsText.text);
            StringAssert.Contains("1 × Верёвка", detailsText.text);

            InventoryView.SetMode(false, false);
            InventoryView.SetMode(true, true);
            Assert.That(details.activeSelf, Is.False);
            Assert.That(craftButtonObject.activeSelf, Is.False);
            Assert.That(selectionHint.activeSelf, Is.True);

            InventoryView.SetMode(false, false);
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SlotView_SeparatesClickAndDragCallbacks()
        {
            var root = new GameObject("SlotCallbacks", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(InventorySlotView));
            var slot = root.GetComponent<InventorySlotView>();
            var clicks = 0;
            var presses = 0;
            var begins = 0;
            var drags = 0;
            var ends = 0;
            slot.Initialize(
                new InventorySlotReference(InventorySlotArea.Inventory, 0),
                Color.gray,
                (_, _) => clicks++,
                (_, _) => presses++,
                (_, _) => begins++,
                (_, _) => drags++,
                (_, _) => ends++,
                _ => { },
                _ => { });
            var eventSystem = EventSystem.current ?? new GameObject(
                "TestEventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
            };

            slot.OnPointerDown(pointer);
            slot.OnPointerClick(pointer);
            slot.OnBeginDrag(pointer);
            slot.OnDrag(pointer);
            slot.OnEndDrag(pointer);

            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(presses, Is.EqualTo(1));
            Assert.That(begins, Is.EqualTo(1));
            Assert.That(drags, Is.EqualTo(1));
            Assert.That(ends, Is.EqualTo(1));
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuantityEditor_IsNumericReusableAndHiddenUntilSelection()
        {
            var root = new GameObject("QuantityEditorTest");
            root.AddComponent<InventoryView>();
            yield return null;

            var editor = Find(root, "QuantityEditor");
            Assert.That(editor, Is.Not.Null);
            Assert.That(editor.activeSelf, Is.False);
            var input = editor.GetComponent<InputField>();
            Assert.That(input, Is.Not.Null);
            Assert.That(input.contentType, Is.EqualTo(InputField.ContentType.IntegerNumber));
            Assert.That(input.lineType, Is.EqualTo(InputField.LineType.SingleLine));
            Assert.That(input.characterLimit, Is.EqualTo(5));
            Assert.That(input.onValidateInput(string.Empty, 0, '7'), Is.EqualTo('7'));
            Assert.That(input.onValidateInput(string.Empty, 0, 'x'), Is.EqualTo('\0'));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuantityEditor_FocusesAndSelectsTheWholeCurrentValue()
        {
            var root = new GameObject("QuantityEditorFocusTest");
            var view = root.AddComponent<InventoryView>();
            yield return null;

            var editor = Find(root, "QuantityEditor");
            var input = editor.GetComponent<InputField>();
            input.SetTextWithoutNotify("20");
            editor.SetActive(true);
            typeof(InventoryView).GetField(
                    "quantitySlot",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(view, new InventorySlotReference(InventorySlotArea.Inventory, 0));
            var focus = typeof(InventoryView).GetMethod(
                "FocusQuantityEditorNextFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(focus, Is.Not.Null);
            focus.Invoke(view, null);
            yield return null;
            yield return null;

            Assert.That(input.isFocused, Is.True);
            Assert.That(Mathf.Min(input.selectionAnchorPosition, input.selectionFocusPosition),
                Is.EqualTo(0));
            Assert.That(Mathf.Max(input.selectionAnchorPosition, input.selectionFocusPosition),
                Is.EqualTo(input.text.Length));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResearchTableInterface_IsCenteredAndSeparatesBothProgressBars()
        {
            InventoryView.SetMode(false, false);
            var root = new GameObject("ResearchTableLayoutTest");
            root.AddComponent<InventoryView>();
            yield return null;

            InventoryView.SetResearchTableMode(true);
            var workspace = Find(root, "ResearchWorkspace");
            var inventory = Find(root, "InventoryPanel");
            var table = Find(root, "ResearchTablePanel");
            var researchTab = Find(root, "ResearchTabContent");
            var studiedTab = Find(root, "StudiedTabContent");
            var researchTabButton = Find(root, "ResearchTabButton");
            var studiedTabButton = Find(root, "StudiedTabButton");
            var sampleKnowledge = Find(root, "SampleKnowledge");
            var knowledge = Find(root, "KnowledgeBar");
            var holdButton = Find(root, "HoldResearchButton");
            var result = Find(root, "ResearchResult");
            var feedback = Find(root, "InterfaceFeedback");
            Assert.That(workspace, Is.Not.Null);
            Assert.That(workspace.activeSelf, Is.True);
            Assert.That(inventory.activeSelf, Is.True);
            Assert.That(table.activeSelf, Is.True);
            Assert.That(researchTabButton, Is.Not.Null);
            Assert.That(studiedTabButton, Is.Not.Null);
            Assert.That(researchTab.activeSelf, Is.True);
            Assert.That(studiedTab.activeSelf, Is.False);
            Assert.That(sampleKnowledge.activeSelf, Is.False,
                "An empty or unidentified sample must not expose deposit knowledge.");
            Assert.That(result.activeSelf, Is.False);
            Assert.That(feedback, Is.Not.Null);
            Assert.That(holdButton.GetComponent<ResearchHoldButtonView>(), Is.Not.Null);

            var inventoryRect = inventory.GetComponent<RectTransform>();
            var tableRect = table.GetComponent<RectTransform>();
            Assert.That(inventoryRect.anchoredPosition.x, Is.EqualTo(-174f).Within(0.01f));
            Assert.That(tableRect.anchoredPosition.x, Is.EqualTo(319f).Within(0.01f));
            Assert.That(inventoryRect.anchoredPosition.x + inventoryRect.rect.width * 0.5f,
                Is.LessThan(tableRect.anchoredPosition.x - tableRect.rect.width * 0.5f));

            var knowledgeRect = knowledge.GetComponent<RectTransform>();
            var holdRect = holdButton.GetComponent<RectTransform>();
            Assert.That(knowledgeRect.anchoredPosition.y,
                Is.GreaterThan(holdRect.anchoredPosition.y));
            Assert.That(holdButton.GetComponentsInChildren<Image>(true).Length,
                Is.GreaterThanOrEqualTo(2), "The hold button needs a separate fill image.");

            studiedTabButton.GetComponent<Button>().onClick.Invoke();
            Assert.That(researchTab.activeSelf, Is.False);
            Assert.That(studiedTab.activeSelf, Is.True);
            Assert.That(Find(root, "StudiedEmptyMessage").activeSelf, Is.True);

            var feedbackRect = feedback.GetComponent<RectTransform>();
            Assert.That(feedbackRect.anchorMin.y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(feedbackRect.pivot.y, Is.EqualTo(0f).Within(0.01f));

            InventoryView.SetMode(false, false);
            InventoryView.SetResearchTableMode(true);
            Assert.That(researchTab.activeSelf, Is.True,
                "Reopening the table must reset it to the research tab.");
            Assert.That(studiedTab.activeSelf, Is.False);

            InventoryView.SetMode(false, false);
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResearchHoldButton_RequiresContinuousPointerHold()
        {
            var root = new GameObject("ResearchHoldButtonTest", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button),
                typeof(ResearchHoldButtonView));
            var hold = root.GetComponent<ResearchHoldButtonView>();
            var starts = 0;
            var cancellations = 0;
            hold.Initialize(() => starts++, () => cancellations++);
            var eventSystem = EventSystem.current ?? new GameObject(
                "ResearchHoldEventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
            };

            hold.OnPointerDown(pointer);
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(cancellations, Is.Zero);
            hold.OnPointerExit(pointer);
            Assert.That(cancellations, Is.EqualTo(1));
            hold.OnPointerUp(pointer);
            Assert.That(cancellations, Is.EqualTo(1));

            hold.OnPointerDown(pointer);
            hold.OnPointerUp(pointer);
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(cancellations, Is.EqualTo(2));
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResearchHistory_KeepsSameResourcesSeparatedBySourceNode()
        {
            InventoryView.SetMode(false, false);
            var root = new GameObject("ResearchHistorySourceTest");
            var view = root.AddComponent<InventoryView>();
            yield return null;

            var buildName = typeof(InventoryView).GetMethod(
                "BuildResearchHistoryName", BindingFlags.Instance | BindingFlags.NonPublic);
            var createCard = typeof(InventoryView).GetMethod(
                "CreateResearchHistoryCard", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buildName, Is.Not.Null);
            Assert.That(createCard, Is.Not.Null);

            var unknown = new DepositKnowledgePresentation(
                1, 2400, new Vector3(1f, 0f, 2f), ResourceCategory.NonOre, 17);
            var category = new DepositKnowledgePresentation(
                2, 2500, new Vector3(3f, 0f, 4f), ResourceCategory.NonOre, 17);
            Assert.That(buildName.Invoke(view, new object[] { unknown }),
                Is.EqualTo("Неизвестная залежь"));
            Assert.That(buildName.Invoke(view, new object[] { category }),
                Is.EqualTo("нерудное сырьё"));

            var first = new DepositKnowledgePresentation(
                1001, 5500, new Vector3(-135f, 0f, 266f),
                ResourceCategory.NonOre, 17);
            var second = new DepositKnowledgePresentation(
                2002, 7200, new Vector3(412f, 0f, -80f),
                ResourceCategory.NonOre, 17);
            var firstCard = (GameObject)createCard.Invoke(view, new object[] { first });
            var secondCard = (GameObject)createCard.Invoke(view, new object[] { second });

            Assert.That(firstCard.name, Is.EqualTo("StudiedDeposit_1001"));
            Assert.That(secondCard.name, Is.EqualTo("StudiedDeposit_2002"));
            Assert.That(Find(firstCard, "StudiedDepositCoordinates")
                    .GetComponent<Text>().text,
                Does.Contain("X -135").And.Contain("Z 266"));
            Assert.That(Find(secondCard, "StudiedDepositCoordinates")
                    .GetComponent<Text>().text,
                Does.Contain("X 412").And.Contain("Z -80"));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResearchResult_IsDetailedInsideTableAndShortAtBottom()
        {
            InventoryView.SetMode(false, false);
            var interactionObject = new GameObject("Research result interaction");
            var interaction = interactionObject.AddComponent<PlayerResourceInteraction>();
            var inventory = interactionObject.GetComponent<PlayerInventory>();
            var root = new GameObject("ResearchResultPresentationTest");
            var view = root.AddComponent<InventoryView>();
            yield return null;

            SetField(view, "inventory", inventory);
            SetField(view, "resourceInteraction", interaction);
            SetField(interaction, "localResearchResult",
                "Исследование успешно. Образец израсходован. Изученность: +20%, всего 55%.");
            SetField(interaction, "localResearchResultTone", ResearchResultTone.Success);
            SetField(interaction, "lastFeedback", "УСПЕХ");
            SetField(interaction, "feedbackExpiresAt", Time.unscaledTime + 5f);
            InventoryView.SetResearchTableMode(true);

            Invoke(view, "RefreshResearchTable");
            Invoke(view, "RefreshInterfaceFeedback");
            var result = Find(root, "ResearchResult");
            var detailedText = GetField<Text>(view, "researchResultText");
            var bottomFeedback = Find(root, "InterfaceFeedback");
            var bottomText = GetField<Text>(view, "interfaceFeedbackText");

            Assert.That(result.activeSelf, Is.True);
            Assert.That(detailedText.text, Does.Contain("Образец израсходован")
                .And.Contain("+20%"));
            Assert.That(bottomFeedback.activeSelf, Is.True);
            Assert.That(bottomText.text, Is.EqualTo("УСПЕХ"));
            Assert.That(bottomFeedback.GetComponent<RectTransform>().anchorMin.y,
                Is.EqualTo(0f).Within(0.01f));

            InventoryView.SetMode(false, false);
            Object.Destroy(root);
            Object.Destroy(interactionObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MapNoteInterface_HasPlacementEditorAndEscapeCancellationStates()
        {
            var createdRoot = ResourceMapView.Instance == null;
            var root = createdRoot
                ? new GameObject("ResourceMapNoteInterfaceTest")
                : ResourceMapView.Instance.gameObject;
            var view = ResourceMapView.Instance ?? root.AddComponent<ResourceMapView>();
            yield return null;

            var addButton = Find(root, "AddMapNoteButton");
            var editor = Find(root, "MapNoteEditor");
            var inputObject = Find(root, "MapNoteInput");
            var actions = Find(root, "MapNoteActions");
            var confirmation = Find(root, "MapNoteDeleteConfirmation");
            var hint = Find(root, "MapNotePlacementHint");
            var map = Find(root, "WorldMap");
            Assert.That(addButton, Is.Not.Null);
            Assert.That(inputObject, Is.Not.Null);
            Assert.That(map, Is.Not.Null);
            var input = inputObject.GetComponent<InputField>();
            Assert.That(editor.activeSelf, Is.False);
            Assert.That(actions.activeSelf, Is.False);
            Assert.That(confirmation.activeSelf, Is.False);
            Assert.That(hint.activeSelf, Is.False);
            Assert.That(input.lineType, Is.EqualTo(InputField.LineType.SingleLine));
            Assert.That(input.characterLimit, Is.EqualTo(80));

            var beginEditor = typeof(ResourceMapView).GetMethod(
                "BeginNoteEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            var cancelTransient = typeof(ResourceMapView).GetMethod(
                "CancelMapTransientState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(beginEditor, Is.Not.Null);
            Assert.That(cancelTransient, Is.Not.Null);
            beginEditor.Invoke(view, new object[] { 0UL, new Vector2(12f, -4f), string.Empty });
            Assert.That(editor.activeSelf, Is.True);
            var mapWasOpen = map.activeSelf;
            map.SetActive(true);
            Assert.That(ResourceMapView.TryClose(), Is.True);
            Assert.That(editor.activeSelf, Is.False);
            Assert.That(map.activeSelf, Is.True,
                "The first Escape must cancel editing without closing the map.");

            typeof(ResourceMapView).GetField(
                    "placingNote", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(view, true);
            hint.SetActive(true);
            Assert.That((bool)cancelTransient.Invoke(view, null), Is.True);
            Assert.That(hint.activeSelf, Is.False);
            Assert.That((bool)cancelTransient.Invoke(view, null), Is.False,
                "A second Escape may close the map after the transient state was cancelled.");
            map.SetActive(mapWasOpen);

            if (createdRoot) Object.Destroy(root);
            yield return null;
        }

        [TestCase("7", 10, 20, 7)]
        [TestCase("0", 10, 20, 1)]
        [TestCase("999", 10, 20, 20)]
        [TestCase("", 10, 20, 10)]
        [TestCase(null, 30, 20, 20)]
        public void QuantityInput_NormalizesInvalidAndOutOfRangeValues(
            string value,
            int fallback,
            int maximum,
            int expected)
        {
            var method = typeof(InventoryView).GetMethod(
                "NormalizeQuantityInput",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var result = (int)method.Invoke(null, new object[] { value, fallback, maximum });
            Assert.That(result, Is.EqualTo(expected));
        }

        private static GameObject Find(GameObject root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name) return transform.gameObject;
            }

            return null;
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {name}.");
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {name}.");
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            var method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {name}.");
            method.Invoke(target, null);
        }
    }
}
