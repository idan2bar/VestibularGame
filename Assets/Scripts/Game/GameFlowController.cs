using System;
using UnityEngine;
using UnityEngine.Events;
using VestibularGame.FaceTracking;

namespace VestibularGame.Game
{
    /// <summary>
    /// Runs CentreCalibration, CentreResult, HeadCalibration, HeadResult and Playing in order,
    /// toggles the matching step objects and enables the <see cref="CenterGazeDetector"/> only while playing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour
    {
        [Tooltip("Face tracking controller. Found in the scene if left empty.")]
        [SerializeField] FaceTrackingController controller;
        [Tooltip("Centre gaze detector. Found in the scene if left empty.")]
        [SerializeField] CenterGazeDetector detector;
        [Tooltip("Active during CentreCalibration and CentreResult.")]
        [SerializeField] GameObject centreCalibrationStep;
        [Tooltip("Active during HeadCalibration and HeadResult.")]
        [SerializeField] GameObject headCalibrationStep;
        [Tooltip("Active during Playing.")]
        [SerializeField] GameObject playStep;
        [Tooltip("Seconds the centre calibration result stays visible.")]
        [SerializeField] float centreResultSeconds = 1.5f;
        [Tooltip("Seconds a successful head calibration result stays visible before play starts.")]
        [SerializeField] float headSuccessResultSeconds = 2.5f;
        [Tooltip("Seconds a failed head calibration result stays visible before it is retried.")]
        [SerializeField] float headFailureResultSeconds = 2.5f;
        [SerializeField] UnityEvent<GamePhase> onPhaseChanged;

        GamePhase phase = GamePhase.CentreCalibration;
        CalibrationResult lastResult;
        float phaseStartTime;

        /// <summary>Raised after the phase changed and the step objects were updated.</summary>
        public event Action<GamePhase> PhaseChanged;
        /// <summary>Raised when a centre or head calibration finishes, before the result phase is entered.</summary>
        public event Action<CalibrationResult> ResultReady;

        public GamePhase Phase => phase;
        public CalibrationResult LastResult => lastResult;
        public FaceTrackingController Controller => controller;

        /// <summary>0..1 progress of the running calibration; 1 in result phases and while playing.</summary>
        public float StepProgress
        {
            get
            {
                if (controller == null) return 1f;
                switch (phase)
                {
                    case GamePhase.CentreCalibration: return controller.Estimator.CalibrationProgress;
                    case GamePhase.HeadCalibration: return controller.Estimator.HeadCalibrationProgress;
                    default: return 1f;
                }
            }
        }

        void Awake()
        {
            if (controller == null) controller = FindAnyObjectByType<FaceTrackingController>();
            if (detector == null) detector = FindAnyObjectByType<CenterGazeDetector>();
        }

        void Start() => Recalibrate();

        void OnDisable()
        {
            if (controller != null) controller.CancelCalibration();
            if (detector != null) detector.SetDetectionEnabled(false);
        }

        void OnDestroy()
        {
            PhaseChanged = null;
            ResultReady = null;
        }

        /// <summary>Restarts the whole flow from the centre calibration, dropping the current calibration.</summary>
        public void Recalibrate()
        {
            if (controller != null)
            {
                controller.CancelCalibration();
                controller.Estimator.ResetCalibration();
            }
            if (detector != null) detector.SetDetectionEnabled(false);
            if (controller != null) controller.BeginCalibration();
            EnterPhase(GamePhase.CentreCalibration);
        }

        void Update()
        {
            if (controller == null) return;

            GazeEstimator estimator = controller.Estimator;
            float elapsed = Time.unscaledTime - phaseStartTime;

            switch (phase)
            {
                case GamePhase.CentreCalibration:
                    if (!estimator.IsCalibrating && estimator.IsCalibrated)
                    {
                        lastResult = new CalibrationResult(false, true, HeadCalibrationState.None, 0f);
                        ResultReady?.Invoke(lastResult);
                        EnterPhase(GamePhase.CentreResult);
                    }
                    break;

                case GamePhase.CentreResult:
                    if (elapsed >= centreResultSeconds)
                    {
                        controller.BeginHeadCalibration();
                        EnterPhase(GamePhase.HeadCalibration);
                    }
                    break;

                case GamePhase.HeadCalibration:
                    HeadCalibrationState state = estimator.HeadCalibrationState;
                    if (state != HeadCalibrationState.Collecting)
                    {
                        bool success = state == HeadCalibrationState.Succeeded;
                        lastResult = new CalibrationResult(true, success, state, success ? estimator.HeadCalibrationRmsDeg : 0f);
                        ResultReady?.Invoke(lastResult);
                        EnterPhase(GamePhase.HeadResult);
                    }
                    break;

                case GamePhase.HeadResult:
                    if (lastResult.Success)
                    {
                        if (elapsed >= headSuccessResultSeconds)
                        {
                            EnterPhase(GamePhase.Playing);
                            if (detector != null) detector.SetDetectionEnabled(true);
                        }
                    }
                    else if (elapsed >= headFailureResultSeconds)
                    {
                        controller.BeginHeadCalibration();
                        EnterPhase(GamePhase.HeadCalibration);
                    }
                    break;
            }
        }

        void EnterPhase(GamePhase next)
        {
            if (phase == GamePhase.Playing && next != GamePhase.Playing && detector != null)
                detector.SetDetectionEnabled(false);

            phase = next;
            phaseStartTime = Time.unscaledTime;

            if (centreCalibrationStep != null)
                centreCalibrationStep.SetActive(next == GamePhase.CentreCalibration || next == GamePhase.CentreResult);
            if (headCalibrationStep != null)
                headCalibrationStep.SetActive(next == GamePhase.HeadCalibration || next == GamePhase.HeadResult);
            if (playStep != null) playStep.SetActive(next == GamePhase.Playing);

            PhaseChanged?.Invoke(next);
            onPhaseChanged?.Invoke(next);
        }
    }
}
