using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VestibularGame.FaceTracking;

namespace VestibularGame.Game
{
    /// <summary>
    /// Drives the already-built calibration UI (instruction text, progress bar, result text and the
    /// always-visible recalibrate button) from <see cref="GameFlowController"/> state. It never creates UI;
    /// every element is a serialized reference. Strings are only rebuilt when their content changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CalibrationHud : MonoBehaviour
    {
        enum InstructionKind
        {
            None,
            Centre,
            NoFace,
            HeadTurn,
            HeadNod,
            HeadOk,
        }

        [Header("References")]
        [Tooltip("The game flow this HUD mirrors.")]
        [SerializeField] GameFlowController flow;
        [Tooltip("Instruction text shown during the calibration phases.")]
        [SerializeField] Text instructionText;
        [Tooltip("Root object of the progress bar; active only while a calibration is running.")]
        [SerializeField] GameObject progressRoot;
        [Tooltip("Filled image (horizontal) whose fillAmount follows the flow's step progress.")]
        [SerializeField] Image progressFill;
        [Tooltip("Text shown briefly after each calibration step.")]
        [SerializeField] Text resultText;
        [Tooltip("Always visible; restarts the whole calibration flow.")]
        [SerializeField] Button recalibrateButton;

        [Header("Instructions")]
        [TextArea]
        [Tooltip("Shown during centre calibration.")]
        [SerializeField] string centreInstruction = "Look at the dot and hold still";
        [TextArea]
        [Tooltip("Shown during head calibration (a live hint line is appended).")]
        [SerializeField] string headInstruction = "Keep looking at the dot while slowly turning your head left and right, then nodding up and down";
        [TextArea]
        [Tooltip("Hint appended while the head yaw range is still too small.")]
        [SerializeField] string turnMoreHint = "Turn your head more";
        [TextArea]
        [Tooltip("Hint appended while the head pitch range is still too small.")]
        [SerializeField] string nodMoreHint = "Nod more";
        [TextArea]
        [Tooltip("Shown instead of the instruction while no face is detected during calibration.")]
        [SerializeField] string noFaceText = "Can't see your face — face the camera";

        [Header("Results")]
        [Tooltip("Shown after a successful centre calibration.")]
        [SerializeField] string centreResultMessage = "Centre calibrated";
        [Tooltip("Prefix of the head success message; followed by the residual, e.g. \"Head calibration OK — residual 1.2° RMS\".")]
        [SerializeField] string headSuccessPrefix = "Head calibration OK — residual ";
        [Tooltip("Shown when the user did not move their head enough.")]
        [SerializeField] string insufficientMotionMessage = "Not enough head movement — let's try again";
        [Tooltip("Shown when the head calibration fit failed.")]
        [SerializeField] string failedMessage = "Head calibration failed — let's try again";
        [Tooltip("Result text colour for success.")]
        [SerializeField] Color successColor = new Color(0.4f, 1f, 0.5f, 1f);
        [Tooltip("Result text colour for failure / retry.")]
        [SerializeField] Color failureColor = new Color(1f, 0.45f, 0.4f, 1f);

        readonly StringBuilder sb = new StringBuilder(160);
        InstructionKind shownKind = InstructionKind.None;
        bool kindValid;
        bool subscribed;

        void OnEnable()
        {
            if (flow == null) return;
            flow.PhaseChanged += OnPhaseChanged;
            flow.ResultReady += OnResultReady;
            subscribed = true;
            if (recalibrateButton != null) recalibrateButton.onClick.AddListener(OnRecalibrateClicked);

            ApplyPhase(flow.Phase);
        }

        void OnDisable()
        {
            if (subscribed && flow != null)
            {
                flow.PhaseChanged -= OnPhaseChanged;
                flow.ResultReady -= OnResultReady;
            }
            subscribed = false;
            if (recalibrateButton != null) recalibrateButton.onClick.RemoveListener(OnRecalibrateClicked);
        }

        void Update()
        {
            if (flow == null) return;
            GamePhase phase = flow.Phase;
            if (!IsCalibrating(phase)) return;

            if (progressFill != null) progressFill.fillAmount = Mathf.Clamp01(flow.StepProgress);
            SetInstruction(ChooseKind(phase));
        }

        void OnRecalibrateClicked()
        {
            if (flow != null) flow.Recalibrate();
        }

        void OnPhaseChanged(GamePhase phase) => ApplyPhase(phase);

        void OnResultReady(CalibrationResult result)
        {
            if (flow != null && IsResultPhase(flow.Phase)) ApplyResult(result);
        }

        void ApplyPhase(GamePhase phase)
        {
            bool calibrating = IsCalibrating(phase);
            bool result = IsResultPhase(phase);

            if (progressRoot != null) SetActive(progressRoot, calibrating);
            if (progressFill != null) progressFill.fillAmount = calibrating ? Mathf.Clamp01(flow.StepProgress) : 0f;

            if (resultText != null) SetActive(resultText.gameObject, result);
            if (result) ApplyResult(flow.LastResult);

            if (calibrating)
            {
                kindValid = false; // force a refresh on phase entry
                SetInstruction(ChooseKind(phase));
            }
            else
            {
                kindValid = false;
                SetInstruction(InstructionKind.None);
            }
        }

        InstructionKind ChooseKind(GamePhase phase)
        {
            FaceTrackingController controller = flow.Controller;
            if (controller != null && !controller.LatestSample.HasFace) return InstructionKind.NoFace;

            if (phase == GamePhase.CentreCalibration) return InstructionKind.Centre;
            if (phase != GamePhase.HeadCalibration) return InstructionKind.None;

            GazeEstimator estimator = controller != null ? controller.Estimator : null;
            if (estimator == null) return InstructionKind.HeadOk;

            GazeEstimatorSettings cfg = estimator.Settings;
            if (RangeWidth(estimator.HeadCalibrationYawRange) < cfg.HeadCalibrationMinYawRangeDeg) return InstructionKind.HeadTurn;
            if (RangeWidth(estimator.HeadCalibrationPitchRange) < cfg.HeadCalibrationMinPitchRangeDeg) return InstructionKind.HeadNod;
            return InstructionKind.HeadOk;
        }

        void SetInstruction(InstructionKind kind)
        {
            if (instructionText == null) return;
            if (kindValid && kind == shownKind) return;
            kindValid = true;
            shownKind = kind;

            if (kind == InstructionKind.None)
            {
                instructionText.text = string.Empty;
                SetActive(instructionText.gameObject, false);
                return;
            }

            sb.Clear();
            switch (kind)
            {
                case InstructionKind.Centre:
                    sb.Append(centreInstruction);
                    break;
                case InstructionKind.NoFace:
                    sb.Append(noFaceText);
                    break;
                case InstructionKind.HeadTurn:
                    sb.Append(headInstruction).Append('\n').Append(turnMoreHint);
                    break;
                case InstructionKind.HeadNod:
                    sb.Append(headInstruction).Append('\n').Append(nodMoreHint);
                    break;
                default:
                    sb.Append(headInstruction);
                    break;
            }
            instructionText.text = sb.ToString();
            SetActive(instructionText.gameObject, true);
        }

        void ApplyResult(CalibrationResult result)
        {
            if (resultText == null) return;

            bool success;
            sb.Clear();
            if (!result.IsHead)
            {
                success = result.Success;
                sb.Append(success ? centreResultMessage : failedMessage);
            }
            else if (result.Success && result.HeadState == HeadCalibrationState.Succeeded)
            {
                success = true;
                sb.Append(headSuccessPrefix)
                    .Append(result.RmsDeg.ToString("F1", CultureInfo.InvariantCulture))
                    .Append("° RMS");
            }
            else
            {
                success = false;
                sb.Append(result.HeadState == HeadCalibrationState.InsufficientMotion ? insufficientMotionMessage : failedMessage);
            }

            resultText.text = sb.ToString();
            resultText.color = success ? successColor : failureColor;
        }

        static bool IsCalibrating(GamePhase phase) =>
            phase == GamePhase.CentreCalibration || phase == GamePhase.HeadCalibration;

        static bool IsResultPhase(GamePhase phase) =>
            phase == GamePhase.CentreResult || phase == GamePhase.HeadResult;

        static float RangeWidth(Vector2 range)
        {
            float w = range.y - range.x;
            return float.IsNaN(w) || w < 0f ? 0f : w;
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }
    }
}
