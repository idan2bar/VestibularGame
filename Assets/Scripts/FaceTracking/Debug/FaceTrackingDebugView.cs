using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VestibularGame.FaceTracking.DebugUI
{
    /// <summary>
    /// Self-building uGUI for inspecting face tracking: landmark view, gaze cursor vs. center target,
    /// status label, numeric readout and Start / Calibrate / Preview buttons.
    /// </summary>
    public sealed class FaceTrackingDebugView : MonoBehaviour
    {
        static readonly Color ColorOn = new Color(0.2f, 0.9f, 0.3f, 1f);
        static readonly Color ColorOff = new Color(1f, 0.25f, 0.2f, 1f);
        static readonly Color ColorIdle = new Color(0.6f, 0.6f, 0.6f, 1f);
        static readonly Color ColorWarn = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color ColorButton = new Color(0.2f, 0.22f, 0.3f, 0.95f);

        const float CenterDotSize = 40f;
        const float CursorSize = 36f;
        const float TextInterval = 0.1f;
        const string MockHint = "Mock: mouse = eyes | RMB or Shift + mouse = head | Space = blink | H = toggle face";

        [SerializeField] FaceTrackingController controller;
        [Tooltip("Canvas units (1080x1920 reference) per degree of gaze.")]
        [SerializeField] float pixelsPerDegree = 25f;
        [SerializeField] bool previewVisible = true;

        Font font;
        Sprite circleSprite;
        RectTransform safeRoot;
        RectTransform field;
        Rect lastSafeArea;
        Vector2Int lastScreenSize;

        LandmarkGraphic landmarkGraphic;
        readonly Vector3[] landmarkSnapshot = new Vector3[FaceFrame.LandmarkCount];
        bool haveSnapshot;

        Image centerDot;
        RectTransform centerDotRect;
        Image cursor;
        RectTransform cursorRect;
        Text statusLabel;
        Text readout;
        Text hintLabel;
        Button startButton, calibrateButton, previewButton;
        Text startText, calibrateText, previewText;

        readonly StringBuilder sb = new StringBuilder(512);
        float nextTextTime;
        int lastCalibPercent = -1;

        /// <summary>Assigns the controller to visualize (before Awake, or at runtime).</summary>
        public void SetController(FaceTrackingController value)
        {
            if (controller == value) return;
            if (isActiveAndEnabled && controller != null) controller.SampleUpdated -= OnSample;
            controller = value;
            if (isActiveAndEnabled && controller != null) controller.SampleUpdated += OnSample;
        }

        void Awake()
        {
            if (controller == null) controller = FindAnyObjectByType<FaceTrackingController>();
            if (controller == null) controller = gameObject.AddComponent<FaceTrackingController>();

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            circleSprite = CreateCircleSprite(64);
            EnsureEventSystem();
            BuildUi();
        }

        void Start()
        {
            controller.SetPreviewVisible(previewVisible);
        }

        void OnEnable()
        {
            if (controller != null) controller.SampleUpdated += OnSample;
        }

        void OnDisable()
        {
            if (controller != null) controller.SampleUpdated -= OnSample;
        }

        void OnDestroy()
        {
            if (circleSprite != null)
            {
                Destroy(circleSprite.texture);
                Destroy(circleSprite);
            }
        }

        void OnSample(GazeSample s)
        {
            if (!s.HasFace) return;
            System.Array.Copy(controller.LatestFrame.Landmarks, landmarkSnapshot, FaceFrame.LandmarkCount);
            haveSnapshot = true;
            landmarkGraphic.SetPoints(landmarkSnapshot, FaceFrame.LandmarkCount, true);
        }

        void Update()
        {
            ApplySafeArea();

            var tracker = controller.Tracker;
            var estimator = controller.Estimator;
            var s = controller.LatestSample;
            bool running = tracker.Status == FaceTrackerStatus.Running;
            bool hasFace = running && s.HasFace;
            bool calibrating = estimator.IsCalibrating;

            landmarkGraphic.color = hasFace ? Color.white : new Color(0.5f, 0.5f, 0.5f, haveSnapshot ? 0.35f : 0f);

            UpdateCursor(s, hasFace);
            UpdateCenterDot(calibrating);
            UpdateStatus(s, hasFace, calibrating);

            if (Time.unscaledTime >= nextTextTime)
            {
                nextTextTime = Time.unscaledTime + TextInterval;
                UpdateReadout(tracker, s);
                UpdateButtons(tracker, estimator, running, calibrating);
            }
        }

        void UpdateCursor(GazeSample s, bool hasFace)
        {
            if (hasFace)
            {
                Vector2 half = field.rect.size * 0.5f - Vector2.one * (CursorSize * 0.5f);
                half = Vector2.Max(half, Vector2.zero);
                float x = Mathf.Clamp(s.GazeYaw * pixelsPerDegree, -half.x, half.x);
                float y = Mathf.Clamp(s.GazePitch * pixelsPerDegree, -half.y, half.y);
                cursorRect.anchoredPosition = new Vector2(x, y);
            }

            cursor.color = !hasFace || !s.IsCalibrated ? ColorIdle : s.IsOnCenter ? ColorOn : ColorOff;
        }

        void UpdateCenterDot(bool calibrating)
        {
            float scale = calibrating ? 2f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
            centerDotRect.localScale = new Vector3(scale, scale, 1f);
            centerDot.color = calibrating ? ColorWarn : new Color(1f, 1f, 1f, 0.85f);
        }

        void UpdateStatus(GazeSample s, bool hasFace, bool calibrating)
        {
            string text;
            Color color;
            if (!hasFace) { text = "NO FACE"; color = ColorIdle; }
            else if (calibrating) { text = "CALIBRATING"; color = ColorWarn; }
            else if (!s.IsCalibrated) { text = "NOT CALIBRATED"; color = ColorIdle; }
            else if (s.IsBlinking) { text = "BLINK"; color = ColorWarn; }
            else if (s.IsOnCenter) { text = "ON CENTER"; color = ColorOn; }
            else { text = "OFF CENTER"; color = ColorOff; }

            statusLabel.text = text;
            statusLabel.color = color;
        }

        void UpdateReadout(IFaceTracker tracker, GazeSample s)
        {
            sb.Clear();
            sb.Append("Backend: ").Append(controller.IsMock ? "Mock" : "WebGL").Append('\n');
            sb.Append("Status: ").Append(tracker.Status.ToString());
            if (!string.IsNullOrEmpty(tracker.ErrorMessage)) sb.Append(" - ").Append(tracker.ErrorMessage);
            sb.Append('\n');
            sb.Append("FPS: "); AppendF1(controller.TrackingFps); sb.Append('\n');
            sb.Append("Head Y/P/R: "); AppendF1(s.HeadYaw); sb.Append(" / "); AppendF1(s.HeadPitch); sb.Append(" / "); AppendF1(s.HeadRoll); sb.Append('\n');
            sb.Append("Eye Y/P: "); AppendF1(s.EyeYaw); sb.Append(" / "); AppendF1(s.EyePitch); sb.Append('\n');
            sb.Append("Gaze Y/P: "); AppendF1(s.GazeYaw); sb.Append(" / "); AppendF1(s.GazePitch); sb.Append('\n');
            sb.Append("Deviation: "); AppendF1(s.DeviationDeg); sb.Append(" deg");
            readout.text = sb.ToString();
        }

        void AppendF1(float v) => sb.Append(v.ToString("F1", CultureInfo.InvariantCulture));

        void UpdateButtons(IFaceTracker tracker, GazeEstimator estimator, bool running, bool calibrating)
        {
            var st = tracker.Status;
            startText.text = st == FaceTrackerStatus.Running || st == FaceTrackerStatus.Starting ? "Stop" : "Start camera";
            startButton.interactable = st != FaceTrackerStatus.Unsupported;

            calibrateButton.interactable = running;
            if (calibrating)
            {
                int pct = Mathf.RoundToInt(Mathf.Clamp01(estimator.CalibrationProgress) * 100f);
                if (pct != lastCalibPercent)
                {
                    lastCalibPercent = pct;
                    calibrateText.text = "Look at the dot… " + pct + "%";
                }
            }
            else
            {
                lastCalibPercent = -1;
                calibrateText.text = "Calibrate";
            }

            previewText.text = previewVisible ? "Preview: ON" : "Preview: OFF";
        }

        void OnStartClicked()
        {
            var st = controller.Tracker.Status;
            if (st == FaceTrackerStatus.Running || st == FaceTrackerStatus.Starting) controller.StopTracking();
            else controller.StartTracking();
        }

        void OnCalibrateClicked()
        {
            if (controller.Estimator.IsCalibrating) controller.Estimator.CancelCalibration();
            else controller.BeginCalibration();
        }

        void OnPreviewClicked()
        {
            previewVisible = !previewVisible;
            controller.SetPreviewVisible(previewVisible);
        }

        // ---------------------------------------------------------------- layout

        void ApplySafeArea()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            var area = Screen.safeArea;
            if (area == lastSafeArea && size == lastScreenSize) return;
            lastSafeArea = area;
            lastScreenSize = size;
            if (size.x <= 0 || size.y <= 0) return;

            safeRoot.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            safeRoot.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("FaceTrackingDebugCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            safeRoot = NewRect("SafeArea", canvasGo.transform);
            Stretch(safeRoot);
            ApplySafeArea();

            // Gaze field: center target and cursor (drawn first, behind the HUD).
            field = NewRect("GazeField", safeRoot);
            Stretch(field);
            centerDot = NewImage("CenterTarget", field, circleSprite, Color.white);
            centerDotRect = centerDot.rectTransform;
            centerDotRect.sizeDelta = new Vector2(CenterDotSize, CenterDotSize);
            cursor = NewImage("GazeCursor", field, circleSprite, ColorIdle);
            cursorRect = cursor.rectTransform;
            cursorRect.sizeDelta = new Vector2(CursorSize, CursorSize);

            // Landmark panel, top-right.
            var panel = NewImage("LandmarkPanel", safeRoot, null, new Color(0f, 0f, 0f, 0.5f));
            var panelRect = panel.rectTransform;
            SetAnchored(panelRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(420f, 315f));
            var lmRect = NewRect("Landmarks", panelRect);
            Stretch(lmRect);
            landmarkGraphic = lmRect.gameObject.AddComponent<LandmarkGraphic>();
            landmarkGraphic.raycastTarget = false;
            landmarkGraphic.SetAspect(4f / 3f);
            landmarkGraphic.SetHighlightIndices(new[]
            {
                FaceLandmarkIndex.EyeAOuter, FaceLandmarkIndex.EyeAInner, FaceLandmarkIndex.EyeAUpper, FaceLandmarkIndex.EyeALower,
                FaceLandmarkIndex.EyeBInner, FaceLandmarkIndex.EyeBOuter, FaceLandmarkIndex.EyeBUpper, FaceLandmarkIndex.EyeBLower
            });
            landmarkGraphic.AddHighlightRange(468, 477);
            landmarkGraphic.color = new Color(0.5f, 0.5f, 0.5f, 0f);

            // Readout, top-left.
            readout = NewText("Readout", safeRoot, 28, TextAnchor.UpperLeft, Color.white);
            SetAnchored(readout.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(560f, 500f));
            readout.verticalOverflow = VerticalWrapMode.Overflow;

            // Bottom stack: buttons, mock hint, status label.
            var row = NewRect("Buttons", safeRoot);
            SetAnchored(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 24f), new Vector2(-48f, 120f));
            row.pivot = new Vector2(0.5f, 0f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            startButton = NewButton("StartButton", row, "Start camera", OnStartClicked, out startText);
            calibrateButton = NewButton("CalibrateButton", row, "Calibrate", OnCalibrateClicked, out calibrateText);
            previewButton = NewButton("PreviewButton", row, "Preview", OnPreviewClicked, out previewText);

            hintLabel = NewText("MockHint", safeRoot, 24, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.7f));
            SetAnchored(hintLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 156f), new Vector2(-48f, 64f));
            hintLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            hintLabel.text = MockHint;
            hintLabel.gameObject.SetActive(controller.IsMock);

            statusLabel = NewText("Status", safeRoot, 72, TextAnchor.LowerCenter, ColorIdle);
            SetAnchored(statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 230f), new Vector2(-48f, 110f));
            statusLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            statusLabel.fontStyle = FontStyle.Bold;
            statusLabel.resizeTextForBestFit = true;
            statusLabel.resizeTextMinSize = 24;
            statusLabel.resizeTextMaxSize = 72;
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ---------------------------------------------------------------- widget helpers

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void SetAnchored(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text NewText(string name, Transform parent, int size, TextAnchor anchor, Color color)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        Button NewButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Text text)
        {
            var img = NewImage(name, parent, null, ColorButton);
            img.raycastTarget = true;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);

            text = NewText("Label", img.transform, 34, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(8f, 8f);
            text.rectTransform.offsetMax = new Vector2(-8f, -8f);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 16;
            text.resizeTextMaxSize = 34;
            text.text = label;
            return button;
        }

        static Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
