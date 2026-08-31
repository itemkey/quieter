using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Quieter.Inventory;
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
    }
}
