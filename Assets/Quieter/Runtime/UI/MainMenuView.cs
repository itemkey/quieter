using System;
using Quieter.Networking;
using Quieter.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Quieter.UI
{
    public sealed class MainMenuView : MonoBehaviour
    {
        private static readonly Color BackgroundColor = new(0.055f, 0.065f, 0.08f, 0.96f);
        private static MainMenuView current;
        private static GameObject fatalErrorRoot;

        private SessionCoordinator session;
        private ServerEndpoint endpoint;
        private GameObject canvasRoot;
        private Text statusText;
        private Text steamText;
        private Button connectButton;
        private Toggle headBobToggle;
        private Slider sensitivitySlider;
        private Text sensitivityValueText;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private Button localTestButton;
#endif
        private Text hudText;
        private Camera menuCamera;
        private bool busy;
        private string currentStatus = string.Empty;

        public void Initialize(SessionCoordinator coordinator, ServerEndpoint serverEndpoint)
        {
            current = this;
            session = coordinator;
            endpoint = serverEndpoint;
            BuildUi();
            session.StatusChanged += OnStatusChanged;
            session.GameplayStateChanged += OnGameplayStateChanged;
            steamText.text = session.AuthenticationStatus;
            statusText.text = session.IsAuthenticationReady
                ? "Готово к подключению"
                : "Steam недоступен. Для разработки запустите локальный тест.";
            currentStatus = statusText.text;
            RefreshButtons();
        }

        public static void ShowFatalError(string message)
        {
            if (current != null)
            {
                current.OnStatusChanged(message);
                return;
            }

            Debug.LogError(message);
            if (fatalErrorRoot != null)
            {
                return;
            }

            fatalErrorRoot = new GameObject("QuieterFatalErrorCanvas");
            DontDestroyOnLoad(fatalErrorRoot);
            var canvas = fatalErrorRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            fatalErrorRoot.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var background = CreatePanel(fatalErrorRoot.transform, BackgroundColor);
            Stretch(background.rectTransform, 0f, 0f, 1f, 1f);
            var errorText = CreateText(
                background.transform,
                $"QUIETER\n\nОшибка запуска\n{message}\n\nОстановите Play Mode и повторите запуск.",
                22,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            errorText.color = new Color(1f, 0.72f, 0.67f);
            Stretch(errorText.rectTransform, 0f, 0f, 1f, 1f);
            errorText.rectTransform.offsetMin = new Vector2(80f, 60f);
            errorText.rectTransform.offsetMax = new Vector2(-80f, -60f);
        }

        private void BuildUi()
        {
            EnsureEventSystem();
            canvasRoot = new GameObject("MainMenuCanvas");
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot.AddComponent<GraphicRaycaster>();

            var cameraObject = new GameObject("MenuCamera");
            cameraObject.transform.SetParent(canvasRoot.transform, false);
            menuCamera = cameraObject.AddComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = new Color(0.035f, 0.04f, 0.065f);
            menuCamera.cullingMask = 0;
            menuCamera.depth = -100f;

            var background = CreatePanel(canvasRoot.transform, BackgroundColor);
            Stretch(background.rectTransform, 0f, 0f, 1f, 1f);

            var card = CreatePanel(background.transform, new Color(0.11f, 0.13f, 0.17f, 1f));
            var cardRect = card.rectTransform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(680f, 540f);

            var title = CreateText(card.transform, "QUIETER", 42, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, 40f, -28f, 600f, 62f, new Vector2(0f, 1f));

            var subtitle = CreateText(
                card.transform,
                "Постоянный процедурный мир",
                20,
                FontStyle.Normal,
                TextAnchor.MiddleCenter);
            subtitle.color = new Color(0.65f, 0.7f, 0.78f);
            SetRect(subtitle.rectTransform, 40f, -94f, 600f, 36f, new Vector2(0f, 1f));

            var serverName = CreateText(
                card.transform,
                endpoint.DisplayName,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetRect(serverName.rectTransform, 52f, -158f, 576f, 38f, new Vector2(0f, 1f));

            steamText = CreateText(
                card.transform,
                "Проверка Steam...",
                17,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            steamText.color = new Color(0.55f, 0.75f, 0.9f);
            SetRect(steamText.rectTransform, 52f, -204f, 576f, 30f, new Vector2(0f, 1f));

            statusText = CreateText(
                card.transform,
                "Готово к подключению",
                16,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            statusText.color = new Color(0.73f, 0.76f, 0.8f);
            SetRect(statusText.rectTransform, 52f, -244f, 576f, 60f, new Vector2(0f, 1f));

            headBobToggle = CreateToggle(card.transform, "Естественное покачивание камеры");
            SetRect(
                headBobToggle.GetComponent<RectTransform>(),
                52f,
                195f,
                270f,
                42f,
                Vector2.zero);
            headBobToggle.SetIsOnWithoutNotify(ClientPreferences.HeadBobEnabled);
            headBobToggle.onValueChanged.AddListener(value => ClientPreferences.HeadBobEnabled = value);

            var sensitivityLabel = CreateText(
                card.transform,
                "Чувствительность мыши",
                15,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            sensitivityLabel.color = new Color(0.73f, 0.76f, 0.8f);
            SetRect(sensitivityLabel.rectTransform, 330f, 224f, 298f, 24f, Vector2.zero);

            sensitivitySlider = CreateSlider(card.transform);
            SetRect(
                sensitivitySlider.GetComponent<RectTransform>(),
                330f,
                195f,
                230f,
                28f,
                Vector2.zero);
            sensitivitySlider.minValue = ClientPreferences.MinimumMouseSensitivity;
            sensitivitySlider.maxValue = ClientPreferences.MaximumMouseSensitivity;
            sensitivitySlider.SetValueWithoutNotify(ClientPreferences.MouseSensitivity);

            sensitivityValueText = CreateText(
                card.transform,
                ClientPreferences.MouseSensitivity.ToString("0.00"),
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            SetRect(sensitivityValueText.rectTransform, 570f, 195f, 58f, 28f, Vector2.zero);
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            localTestButton = CreateButton(
                card.transform,
                "ИГРАТЬ ЛОКАЛЬНО (ТЕСТ)",
                new Color(0.16f, 0.48f, 0.36f));
            SetRect(localTestButton.GetComponent<RectTransform>(), 52f, 112f, 576f, 58f, Vector2.zero);
            localTestButton.onClick.AddListener(StartLocalTest);
#endif

            connectButton = CreateButton(card.transform, "ПОДКЛЮЧИТЬСЯ К СЕРВЕРУ", new Color(0.18f, 0.48f, 0.72f));
            SetRect(connectButton.GetComponent<RectTransform>(), 52f, 42f, 390f, 58f, Vector2.zero);
            connectButton.onClick.AddListener(Connect);

            var quitButton = CreateButton(card.transform, "ВЫЙТИ", new Color(0.25f, 0.27f, 0.31f));
            SetRect(quitButton.GetComponent<RectTransform>(), 458f, 42f, 170f, 58f, Vector2.zero);
            quitButton.onClick.AddListener(Quit);

            hudText = CreateText(canvasRoot.transform, string.Empty, 16, FontStyle.Normal, TextAnchor.UpperLeft);
            hudText.color = Color.white;
            SetRect(hudText.rectTransform, 18f, -16f, 700f, 86f, new Vector2(0f, 1f));
            hudText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (hudText == null || !hudText.gameObject.activeSelf)
            {
                return;
            }

            var playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (playerObject == null)
            {
                hudText.text = currentStatus;
                return;
            }

            var position = playerObject.transform.position;
            hudText.text = $"{currentStatus}\n"
                + $"X {position.x:0.0}   Y {position.y:0.0}   Z {position.z:0.0}\n"
                + "WASD — движение   Shift — бег   Ctrl — присяд   Space — прыжок   Esc — освободить мышь";
        }

        private async void Connect()
        {
            busy = true;
            RefreshButtons();
            try
            {
                await session.ConnectAsync(endpoint);
            }
            catch (Exception exception)
            {
                OnStatusChanged(exception.Message);
                busy = false;
                RefreshButtons();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private async void StartLocalTest()
        {
            busy = true;
            RefreshButtons();
            steamText.text = "Тестовая авторизация — Steam не требуется";
            try
            {
                await session.StartDevelopmentHostAsync();
            }
            catch (Exception exception)
            {
                OnStatusChanged(exception.Message);
                busy = false;
                RefreshButtons();
            }
        }
#endif

        private void OnStatusChanged(string status)
        {
            currentStatus = status;
            if (statusText != null)
            {
                statusText.text = status;
            }

            if (hudText != null)
            {
                hudText.text = status;
            }

            RefreshButtons();
        }

        private void OnGameplayStateChanged(bool inGame)
        {
            if (canvasRoot == null)
            {
                return;
            }

            var background = canvasRoot.transform.Find("Panel");
            if (background != null)
            {
                background.gameObject.SetActive(!inGame);
            }

            if (menuCamera != null)
            {
                menuCamera.enabled = !inGame;
            }

            hudText.gameObject.SetActive(inGame);
            if (!inGame)
            {
                busy = false;
                RefreshButtons();
            }
        }

        private void RefreshButtons()
        {
            if (connectButton != null)
            {
                connectButton.interactable = !busy
                    && !session.IsClientConnected
                    && session.IsAuthenticationReady;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (localTestButton != null)
            {
                localTestButton.interactable = !busy && !session.IsClientConnected;
            }
#endif
        }

        private void OnSensitivityChanged(float value)
        {
            ClientPreferences.MouseSensitivity = value;
            if (sensitivityValueText != null)
            {
                sensitivityValueText.text = value.ToString("0.00");
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.StatusChanged -= OnStatusChanged;
                session.GameplayStateChanged -= OnGameplayStateChanged;
            }

            if (current == this)
            {
                current = null;
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

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

        private static Text CreateText(
            Transform parent,
            string value,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, Color color)
        {
            var gameObject = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            var button = gameObject.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = color * 1.2f;
            colors.pressedColor = color * 0.8f;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
            button.colors = colors;
            var text = CreateText(gameObject.transform, label, 17, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f, 0f, 1f, 1f);
            return button;
        }

        private static Toggle CreateToggle(Transform parent, string label)
        {
            var root = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            var toggle = root.GetComponent<Toggle>();

            var backgroundObject = new GameObject(
                "Background",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            backgroundObject.transform.SetParent(root.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(30f, 30f);
            var background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.2f, 0.23f, 0.28f, 1f);

            var checkObject = new GameObject(
                "Checkmark",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            checkObject.transform.SetParent(backgroundObject.transform, false);
            var checkRect = checkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(18f, 18f);
            var check = checkObject.GetComponent<Image>();
            check.color = new Color(0.28f, 0.75f, 0.58f, 1f);

            var text = CreateText(root.transform, label, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(42f, 0f);
            text.rectTransform.offsetMax = Vector2.zero;

            toggle.targetGraphic = background;
            toggle.graphic = check;
            return toggle;
        }

        private static Slider CreateSlider(Transform parent)
        {
            var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            var slider = root.GetComponent<Slider>();

            var backgroundObject = new GameObject(
                "Background",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            backgroundObject.transform.SetParent(root.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.38f);
            backgroundRect.anchorMax = new Vector2(1f, 0.62f);
            backgroundRect.offsetMin = new Vector2(7f, 0f);
            backgroundRect.offsetMax = new Vector2(-7f, 0f);
            backgroundObject.GetComponent<Image>().color = new Color(0.2f, 0.23f, 0.28f, 1f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(root.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.38f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.62f);
            fillAreaRect.offsetMin = new Vector2(7f, 0f);
            fillAreaRect.offsetMax = new Vector2(-7f, 0f);

            var fillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            fillObject.transform.SetParent(fillArea.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillObject.GetComponent<Image>().color = new Color(0.28f, 0.65f, 0.9f, 1f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(7f, 0f);
            handleAreaRect.offsetMax = new Vector2(-7f, 0f);

            var handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            handleObject.transform.SetParent(handleArea.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 24f);
            var handle = handleObject.GetComponent<Image>();
            handle.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private static void Stretch(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height,
            Vector2 anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, anchor.y > 0.5f ? 1f : 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
