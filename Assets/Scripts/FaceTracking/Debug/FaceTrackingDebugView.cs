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
        const float ResultDisplaySeconds = 5f;
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
        Text promptLabel;
        Button startButton, calibrateButton, headCalButton, previewButton;
        Text startText, calibrateText, headCalText, previewText;

        ScatterPlotGraphic scatterPlot;
        Text fitLabel;

        readonly StringBuilder sb = new StringBuilder(512);
        readonly StringBuilder fitSb = new StringBuilder(128);
        readonly StringBuilder promptSb = new StringBuilder(160);
        float nextTextTime;
        HeadCalibrationState lastHeadState;
        HeadCalibrationState resultState;
        float resultUntil = -1f;

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
            if (s.HasFace && !s.IsBlinking)
            {
                scatterPlot.AddPoint(0, s.HeadYaw, s.GazeYaw);
                scatterPlot.AddPoint(1, s.HeadPitch, s.GazePitch);
            }

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
            bool calibrating = estimator.IsCalibrating || estimator.IsHeadCalibrating;

            landmarkGraphic.color = hasFace ? Color.white : new Color(0.5f, 0.5f, 0.5f, haveSnapshot ? 0.35f : 0f);

            TrackHeadCalibrationResult(estimator);
            UpdateCursor(s, hasFace);
            UpdateCenterDot(calibrating);
            UpdateStatus(s, hasFace, calibrating);

            if (Time.unscaledTime >= nextTextTime)
            {
                nextTextTime = Time.unscaledTime + TextInterval;
                UpdateReadout(tracker, estimator, s);
                UpdateFitLabel();
                UpdatePrompt(estimator);
                UpdateButtons(tracker, estimator, running, calibrating);
            }
        }

        /// <summary>Detects the end of a head calibration to show its result for a few seconds and refresh the plot.</summary>
        void TrackHeadCalibrationResult(GazeEstimator estimator)
        {
            var state = estimator.HeadCalibrationState;
            if (lastHeadState == HeadCalibrationState.Collecting && state != HeadCalibrationState.Collecting
                && state != HeadCalibrationState.None)
            {
                resultState = state;
                resultUntil = Time.unscaledTime + ResultDisplaySeconds;
                if (state == HeadCalibrationState.Succeeded) scatterPlot.Clear(); // show the post-calibration result
            }
            if (state == HeadCalibrationState.Collecting) resultUntil = -1f;
            lastHeadState = state;
        }

        void UpdatePrompt(GazeEstimator estimator)
        {
            promptSb.Clear();
            Color color = ColorWarn;
            if (estimator.IsCalibrating)
            {
                int pct = Mathf.RoundToInt(Mathf.Clamp01(estimator.CalibrationProgress) * 100f);
                promptSb.Append("Look at the dot and hold still… ").Append(pct).Append('%');
            }
            else if (estimator.IsHeadCalibrating)
            {
                float progress = Mathf.Clamp01(estimator.HeadCalibrationProgress);
                var cfg = estimator.Settings;
                float yawW = RangeWidth(estimator.HeadCalibrationYawRange);
                float pitchW = RangeWidth(estimator.HeadCalibrationPitchRange);
                promptSb.Append(progress < 0.5f
                    ? "Keep looking at the dot — slowly turn your head LEFT and RIGHT"
                    : "Keep looking at the dot — slowly nod UP and DOWN");
                promptSb.Append("\nYaw ").Append(Mathf.FloorToInt(yawW)).Append('/').Append(Mathf.RoundToInt(cfg.HeadCalibrationMinYawRangeDeg))
                    .Append("° ").Append(yawW >= cfg.HeadCalibrationMinYawRangeDeg ? "ok" : "more")
                    .Append(" · Pitch ").Append(Mathf.FloorToInt(pitchW)).Append('/').Append(Mathf.RoundToInt(cfg.HeadCalibrationMinPitchRangeDeg))
                    .Append("° ").Append(pitchW >= cfg.HeadCalibrationMinPitchRangeDeg ? "ok" : "more")
                    .Append(" · ").Append(Mathf.RoundToInt(progress * 100f)).Append('%');
            }
            else if (Time.unscaledTime < resultUntil)
            {
                switch (resultState)
                {
                    case HeadCalibrationState.Succeeded:
                        promptSb.Append("Head calibration OK — residual ")
                            .Append(estimator.HeadCalibrationRmsDeg.ToString("F1", CultureInfo.InvariantCulture)).Append("° RMS");
                        color = ColorOn;
                        break;
                    case HeadCalibrationState.InsufficientMotion:
                        promptSb.Append("Not enough head movement — press Calibrate to retry");
                        color = ColorWarn;
                        break;
                    default:
                        promptSb.Append("Head calibration failed — press Calibrate to retry");
                        color = ColorOff;
                        break;
                }
            }

            promptLabel.text = promptSb.ToString();
            promptLabel.color = color;
        }

        static float RangeWidth(Vector2 range)
        {
            float w = range.y - range.x;
            return float.IsNaN(w) || w < 0f ? 0f : w;
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

        void UpdateReadout(IFaceTracker tracker, GazeEstimator estimator, GazeSample s)
        {
            sb.Clear();
            sb.Append("Backend: ").Append(controller.IsMock ? "Mock" : "WebGL").Append('\n');
            sb.Append("Status: ").Append(tracker.Status.ToString());
            if (!string.IsNullOrEmpty(tracker.ErrorMessage)) sb.Append(" - ").Append(tracker.ErrorMessage);
            sb.Append('\n');
            sb.Append("FPS: "); AppendF1(controller.TrackingFps); sb.Append('\n');
            var frame = controller.LatestFrame;
            sb.Append("Camera: ");
            if (frame.ImageWidth > 0 && frame.ImageHeight > 0) sb.Append(frame.ImageWidth).Append('x').Append(frame.ImageHeight);
            else sb.Append('—');
            sb.Append('\n');
            sb.Append("Head Y/P/R: "); AppendF1(s.HeadYaw); sb.Append(" / "); AppendF1(s.HeadPitch); sb.Append(" / "); AppendF1(s.HeadRoll); sb.Append('\n');
            sb.Append("Eye Y/P: "); AppendF1(s.EyeYaw); sb.Append(" / "); AppendF1(s.EyePitch); sb.Append('\n');
            sb.Append("Gaze Y/P: "); AppendF1(s.GazeYaw); sb.Append(" / "); AppendF1(s.GazePitch); sb.Append('\n');
            sb.Append("Deviation: "); AppendF1(s.DeviationDeg); sb.Append(" deg\n");
            sb.Append("Centre cal: ").Append(estimator.IsCalibrating ? "calibrating" : estimator.IsCalibrated ? "done" : "none").Append('\n');
            sb.Append("Head cal: ");
            switch (estimator.HeadCalibrationState)
            {
                case HeadCalibrationState.Succeeded:
                    sb.Append("OK "); AppendF1(estimator.HeadCalibrationRmsDeg); sb.Append("° rms");
                    break;
                case HeadCalibrationState.Collecting:
                    sb.Append("collecting ").Append(Mathf.RoundToInt(Mathf.Clamp01(estimator.HeadCalibrationProgress) * 100f)).Append('%');
                    break;
                case HeadCalibrationState.InsufficientMotion:
                    sb.Append("need more motion");
                    break;
                case HeadCalibrationState.Failed:
                    sb.Append("failed");
                    break;
                default:
                    sb.Append("none");
                    break;
            }
            readout.text = sb.ToString();
        }

        void AppendF1(float v) => sb.Append(v.ToString("F1", CultureInfo.InvariantCulture));

        void UpdateFitLabel()
        {
            fitSb.Clear();
            AppendFit("Yaw", 0);
            fitSb.Append('\n');
            AppendFit("Pitch", 1);
            fitLabel.text = fitSb.ToString();
        }

        /// <summary>
        /// RMS of the gaze error over the series (how far it is from 0) and the least-squares drift slope of the error vs. head angle.
        /// </summary>
        void AppendFit(string name, int series)
        {
            fitSb.Append(name);
            if (!scatterPlot.TryGetStats(series, out float rms, out int count) || count < ScatterPlotGraphic.MinFitCount)
            {
                fitSb.Append(" — (turn head)");
                return;
            }

            fitSb.Append(" err RMS ").Append(rms.ToString("F1", CultureInfo.InvariantCulture)).Append('°');
            if (scatterPlot.TryGetFit(series, out float drift, out _, out _))
            {
                fitSb.Append(" · drift ").Append(drift.ToString("+0.00;-0.00;+0.00", CultureInfo.InvariantCulture))
                    .Append("/° (n ").Append(count).Append(')');
            }
            else
            {
                fitSb.Append(" (turn head)");
            }
        }

        void OnClearPlotClicked()
        {
            scatterPlot.Clear();
        }

        void UpdateButtons(IFaceTracker tracker, GazeEstimator estimator, bool running, bool calibrating)
        {
            var st = tracker.Status;
            startText.text = st == FaceTrackerStatus.Running || st == FaceTrackerStatus.Starting ? "Stop" : "Start camera";
            startButton.interactable = st != FaceTrackerStatus.Unsupported;

            calibrateButton.interactable = running || calibrating;
            calibrateText.text = calibrating ? "Cancel" : "Calibrate";

            headCalButton.interactable = running && estimator.IsCalibrated && !estimator.IsCalibrating;

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
            var estimator = controller.Estimator;
            if (estimator.IsCalibrating || estimator.IsHeadCalibrating) controller.CancelCalibration();
            else controller.CalibrateFull();
        }

        void OnHeadCalClicked()
        {
            controller.BeginHeadCalibration();
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

            // Calibration prompt, centred horizontally just below the dot (which pulses up to ~2.4x while calibrating).
            promptLabel = NewText("CalibrationPrompt", field, 40, TextAnchor.UpperCenter, ColorWarn);
            SetAnchored(promptLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -80f), new Vector2(-96f, 170f));
            promptLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            promptLabel.fontStyle = FontStyle.Bold;
            promptLabel.resizeTextForBestFit = true;
            promptLabel.resizeTextMinSize = 24;
            promptLabel.resizeTextMaxSize = 40;
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

            // Head-vs-eye scatter plot, right below the landmark panel (top-right column: 24 + 315 + 16 = 355 from the top).
            var plotPanel = NewImage("ScatterPanel", safeRoot, null, new Color(0f, 0f, 0f, 0.6f));
            var plotPanelRect = plotPanel.rectTransform;
            SetAnchored(plotPanelRect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -355f), new Vector2(420f, 420f));

            var plotTitle = NewText("ScatterTitle", plotPanelRect, 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            SetAnchored(plotTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), new Vector2(0f, 32f));
            plotTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            plotTitle.text = "Gaze error vs head (fixate centre)";

            // 380x380 plot area inside the 420x420 panel (x spans ±30°, y spans ±15°); the bright horizontal axis is the ideal line.
            var plotRect = NewRect("ScatterPlot", plotPanelRect);
            Stretch(plotRect);
            plotRect.offsetMin = new Vector2(20f, 4f);
            plotRect.offsetMax = new Vector2(-20f, -36f);
            scatterPlot = plotRect.gameObject.AddComponent<ScatterPlotGraphic>();
            scatterPlot.raycastTarget = false;

            fitLabel = NewText("ScatterFit", safeRoot, 22, TextAnchor.UpperLeft, Color.white);
            SetAnchored(fitLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -781f), new Vector2(420f, 112f));
            fitLabel.verticalOverflow = VerticalWrapMode.Overflow;

            // Full-width button across the 420-wide column, right edge at -24.
            var clearButton = NewButton("ClearPlotButton", safeRoot, "Clear plot", OnClearPlotClicked, out _);
            SetAnchored((RectTransform)clearButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -901f), new Vector2(420f, 64f));

            // Readout, top-left.
            readout = NewText("Readout", safeRoot, 28, TextAnchor.UpperLeft, Color.white);
            SetAnchored(readout.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(560f, 500f));
            readout.verticalOverflow = VerticalWrapMode.Overflow;

            // Bottom stack: buttons, mock hint, status label.
            var row = NewRect("Buttons", safeRoot);
            SetAnchored(row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 24f), new Vector2(-48f, 120f));
            row.pivot = new Vector2(0.5f, 0f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f; // four buttons across the ~1032-wide row (~245 each)
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            startButton = NewButton("StartButton", row, "Start camera", OnStartClicked, out startText);
            calibrateButton = NewButton("CalibrateButton", row, "Calibrate", OnCalibrateClicked, out calibrateText);
            headCalButton = NewButton("HeadCalButton", row, "Head cal", OnHeadCalClicked, out headCalText);
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
