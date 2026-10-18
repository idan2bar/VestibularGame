using System;
using UnityEngine;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Owns the face-tracking backend and the <see cref="GazeEstimator"/>, polls the tracker every frame
    /// and publishes the latest <see cref="GazeSample"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FaceTrackingController : MonoBehaviour
    {
        [SerializeField] GazeEstimatorSettings settings = new GazeEstimatorSettings();
        [Tooltip("Use the mouse-driven mock tracker even in WebGL player builds.")]
        [SerializeField] bool forceMock;
        [SerializeField] bool autoStart = true;
        [SerializeField] int requestedCameraWidth = 1280;
        [SerializeField] int requestedCameraHeight = 720;

        IFaceTracker tracker;
        GazeEstimator estimator;
        FaceFrame latestFrame;
        GazeSample latestSample;
        bool isMock;
        bool previewSet;
        bool previewVisible;
        bool fullCalibrationPending;
        float fps;
        float lastFrameTime = -1f;

        /// <summary>Raised once per new tracker frame, after the estimator has processed it.</summary>
        public event Action<GazeSample> SampleUpdated;

        public IFaceTracker Tracker { get { EnsureInitialized(); return tracker; } }
        public GazeEstimator Estimator { get { EnsureInitialized(); return estimator; } }

        /// <summary>Owned frame instance, overwritten whenever the tracker delivers a new result.</summary>
        public FaceFrame LatestFrame { get { EnsureInitialized(); return latestFrame; } }

        public GazeSample LatestSample => latestSample;
        public bool IsMock { get { EnsureInitialized(); return isMock; } }

        /// <summary>Smoothed rate of new tracker frames in Hz (0 if none arrived in the last second).</summary>
        public float TrackingFps => lastFrameTime >= 0f && Time.unscaledTime - lastFrameTime <= 1f ? fps : 0f;

        /// <summary>Camera size requested from the browser on WebGL (ideal, not guaranteed).</summary>
        public Vector2Int RequestedCameraSize => new Vector2Int(requestedCameraWidth, requestedCameraHeight);

        void Awake() => EnsureInitialized();

        void Start()
        {
            if (autoStart) StartTracking();
        }

        void EnsureInitialized()
        {
            if (tracker != null) return;

            if (settings == null) settings = new GazeEstimatorSettings();
            estimator = new GazeEstimator(settings);
            latestFrame = new FaceFrame();

#if UNITY_WEBGL && !UNITY_EDITOR
            isMock = forceMock;
            tracker = forceMock ? (IFaceTracker)new MockFaceTracker() : new WebGLFaceTracker(requestedCameraWidth, requestedCameraHeight);
#else
            isMock = true;
            tracker = new MockFaceTracker();
#endif
        }

        void Update()
        {
            EnsureInitialized();

            if (fullCalibrationPending && !estimator.IsCalibrating)
            {
                fullCalibrationPending = false;
                if (estimator.IsCalibrated) estimator.BeginHeadCalibration();
            }

            if (!tracker.TryGetLatest(latestFrame)) return;

            float now = Time.unscaledTime;
            if (lastFrameTime >= 0f)
            {
                float dt = now - lastFrameTime;
                if (dt > 0f)
                {
                    float inst = 1f / dt;
                    fps = dt > 1f || fps <= 0f ? inst : Mathf.Lerp(fps, inst, 0.1f);
                }
            }
            lastFrameTime = now;

            latestSample = estimator.Process(latestFrame);
            SampleUpdated?.Invoke(latestSample);
        }

        /// <summary>Starts the camera/tracker. On iOS Safari call this from a user gesture (button click).</summary>
        public void StartTracking()
        {
            EnsureInitialized();
            tracker.Start();
            if (previewSet) tracker.SetPreviewVisible(previewVisible);
        }

        public void StopTracking()
        {
            EnsureInitialized();
            tracker.Stop();
            estimator.CancelCalibration();
            estimator.CancelHeadCalibration();
            fullCalibrationPending = false;
            latestFrame.HasFace = false;
            latestSample = default;
            fps = 0f;
            lastFrameTime = -1f;
        }

        public void BeginCalibration()
        {
            EnsureInitialized();
            estimator.BeginCalibration();
        }

        /// <summary>Cancels any running centre or head calibration, including a pending full calibration.</summary>
        public void CancelCalibration()
        {
            EnsureInitialized();
            fullCalibrationPending = false;
            estimator.CancelCalibration();
            estimator.CancelHeadCalibration();
        }

        /// <summary>Starts the head-movement calibration (the centre calibration should be done first).</summary>
        public void BeginHeadCalibration()
        {
            EnsureInitialized();
            estimator.BeginHeadCalibration();
        }

        /// <summary>Starts the centre calibration, then the head-movement calibration once the centre one has succeeded.</summary>
        public void CalibrateFull()
        {
            EnsureInitialized();
            estimator.BeginCalibration();
            fullCalibrationPending = true;
        }

        public void SetPreviewVisible(bool visible)
        {
            EnsureInitialized();
            previewSet = true;
            previewVisible = visible;
            tracker.SetPreviewVisible(visible);
        }

        void OnDestroy()
        {
            tracker?.Dispose();
            tracker = null;
            SampleUpdated = null;
        }
    }
}
