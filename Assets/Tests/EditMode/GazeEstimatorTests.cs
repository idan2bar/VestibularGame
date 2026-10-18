using NUnit.Framework;
using UnityEngine;

namespace VestibularGame.FaceTracking.Tests
{
    public class GazeEstimatorTests
    {
        const double FrameMs = 33.0;

        GazeEstimator _est;
        double _timeMs;

        const float W = 640f, H = 480f;
        const float DistanceCm = 50f;

        static GazeEstimatorSettings FastSettings() => new GazeEstimatorSettings
        {
            // High cutoff and no beta: the filters pass values through almost unchanged, so tests settle fast.
            FilterMinCutoff = 1000f,
            FilterBeta = 0f,
            CalibrationDurationSec = 1f,
            HeadCalibrationDurationSec = 4f,
            // Gain 1 so eye-in-head expectations are the raw model angles.
            EyeYawGain = 1f,
            EyePitchGain = 1f,
        };

        [SetUp]
        public void SetUp()
        {
            _est = new GazeEstimator(FastSettings());
            _timeMs = 1000.0;
        }

        static float FocalPx => EyeModel.FocalLengthPx(H, EyeModel.DefaultCameraVerticalFovDeg);

        /// <summary>
        /// Head matrix (rotation + translation z = -50 cm, as MediaPipe reports) and the eye corner-midpoints in image-space pixels.
        /// The face centre sits at the image centre plus 0.05 W, so for a frontal head (and under pure yaw) the cyclopean eye is exactly at
        /// the image centre; <paramref name="faceShiftPx"/> moves the whole face (x right, y down, pixels).
        /// </summary>
        static void FaceGeometry(float headYaw, float headPitch, float headRoll, Vector2 faceShiftPx, out Matrix4x4 matrix,
            out Vector3 right, out Vector3 up, out Vector3 fwd, out Vector3 midA, out Vector3 midB)
        {
            matrix = HeadPoseMath.ToMatrix(headYaw, headPitch, headRoll);
            matrix.m23 = -DistanceCm;
            EyeModel.HeadAxes(matrix, out right, out up, out fwd);

            var centre = new Vector3(W * 0.5f + faceShiftPx.x, H * 0.5f + 0.05f * W + faceShiftPx.y, 0f);
            midA = centre + right * (-0.08f * W) + up * (0.05f * W);
            midB = centre + right * (0.08f * W) + up * (0.05f * W);
        }

        /// <summary>Cyclopean eye (mean of both corner midpoints) in image-space cm.</summary>
        static Vector3 CyclopeanEyeCm(Vector3 midA, Vector3 midB)
        {
            Vector3 m = (midA + midB) * 0.5f;
            return EyeModel.BackProjectCm(m.x, m.y, W, H, DistanceCm, FocalPx);
        }

        /// <summary>
        /// Synthetic frame built with the exact forward model the estimator inverts (see <see cref="EyeModel"/>): head pose from the
        /// angles, eye corners at +-w/2 along the head's right axis, eyeball centres behind the corner midpoints, irises on the eyeball spheres.
        /// Eye angles are eye-in-head (GazeSample signs). <paramref name="lookAtCamera"/> overrides them with parallel rays from both eyes
        /// toward the lens (the camera origin); <paramref name="faceShiftPx"/> moves the whole face in the image (x right, y down).
        /// </summary>
        static FaceFrame MakeModelFrame(double ts, float headYaw, float headPitch, float headRoll, float eyeYaw, float eyePitch,
            bool lookAtCamera = false, float blink = 0f, bool hasFace = true, bool swapIrisOrder = false,
            Vector2 faceShiftPx = default)
        {
            FaceGeometry(headYaw, headPitch, headRoll, faceShiftPx, out Matrix4x4 matrix,
                out Vector3 right, out Vector3 up, out Vector3 fwd, out Vector3 midA, out Vector3 midB);
            var f = new FaceFrame
            {
                HasFace = hasFace,
                TimestampMs = ts,
                ImageWidth = (int)W,
                ImageHeight = (int)H,
                FacialTransform = matrix,
            };

            float w = 0.06f * W;

            Vector3 dir = lookAtCamera
                ? (-CyclopeanEyeCm(midA, midB)).normalized
                : EyeModel.DirectionFromHeadAngles(eyeYaw, eyePitch, right, up, fwd);
            Vector3 irisOffset = dir * (EyeModel.DefaultIrisSphereRadius * w);
            Vector3 centreToMid = fwd * (EyeModel.DefaultEyeballCenterDepth * w);
            Vector3 irisA = midA - centreToMid + irisOffset;
            Vector3 irisB = midB - centreToMid + irisOffset;

            f.Landmarks[FaceLandmarkIndex.EyeAOuter] = EyeModel.ToNormalized(midA - right * (w * 0.5f), W, H);
            f.Landmarks[FaceLandmarkIndex.EyeAInner] = EyeModel.ToNormalized(midA + right * (w * 0.5f), W, H);
            f.Landmarks[FaceLandmarkIndex.EyeBInner] = EyeModel.ToNormalized(midB - right * (w * 0.5f), W, H);
            f.Landmarks[FaceLandmarkIndex.EyeBOuter] = EyeModel.ToNormalized(midB + right * (w * 0.5f), W, H);

            Vector3 nIrisA = EyeModel.ToNormalized(irisA, W, H), nIrisB = EyeModel.ToNormalized(irisB, W, H);
            f.Landmarks[FaceLandmarkIndex.IrisCenter1] = swapIrisOrder ? nIrisB : nIrisA;
            f.Landmarks[FaceLandmarkIndex.IrisCenter2] = swapIrisOrder ? nIrisA : nIrisB;

            f.Blendshapes[BlendshapeIndex.EyeBlinkLeft] = blink;
            f.Blendshapes[BlendshapeIndex.EyeBlinkRight] = blink;
            return f;
        }

        /// <summary>Gaze angles relative to the camera axis that the forward model produces for the given pose (what the estimator should recover).</summary>
        static void ExpectedCameraGaze(float headYaw, float headPitch, float headRoll, float eyeYaw, float eyePitch, out float gazeYaw, out float gazePitch)
        {
            EyeModel.HeadAxes(HeadPoseMath.ToMatrix(headYaw, headPitch, headRoll), out Vector3 right, out Vector3 up, out Vector3 fwd);
            Vector3 dir = EyeModel.DirectionFromHeadAngles(eyeYaw, eyePitch, right, up, fwd);
            EyeModel.CameraAnglesFromDirection(dir, out gazeYaw, out gazePitch);
        }

        GazeSample Run(int frames, float headYaw = 0f, float headPitch = 0f, float headRoll = 0f,
            float eyeYaw = 0f, float eyePitch = 0f, bool lookAtCamera = false, float blink = 0f, bool hasFace = true, bool swapIrisOrder = false,
            Vector2 faceShiftPx = default)
        {
            GazeSample s = default;
            for (int i = 0; i < frames; i++)
            {
                _timeMs += FrameMs; // strictly increasing timestamp for every Process call
                s = _est.Process(MakeModelFrame(_timeMs, headYaw, headPitch, headRoll, eyeYaw, eyePitch, lookAtCamera, blink, hasFace, swapIrisOrder,
                    faceShiftPx));
            }
            return s;
        }

        void Calibrate()
        {
            _est.BeginCalibration();
            Run(60);
            Assert.IsTrue(_est.IsCalibrated, "calibration should complete after 60 frames (~2 s)");
        }

        // ---- "Imperfect tracker" synthetic relation used by the head-calibration tests ----
        // The user fixates the centre while moving the head, but the tracker's eye-in-head angles carry a systematic head-dependent error:
        // a gain error on yaw and a U-shaped (quadratic in head yaw) error on pitch. The raw gaze therefore drifts with the head pose.

        const float SynthYawGain = -1.3f;     // perfect fixation would be -1.0 (eye counter-rotates the head)
        const float SynthPitchGain = -0.6f;   // perfect fixation would be -1.0
        const float SynthPitchQuad = -0.02f;  // per deg^2 of head yaw

        // Sweep amplitudes of a real user: about +-20 deg yaw and +-12 deg pitch (above the 16/10 deg range minimums).
        const float SweepYawDeg = 20f, SweepPitchDeg = 12f;
        const int SweepHalfFrames = 60;       // yaw sweep first, then pitch sweep (~2 s each); the 4 s calibration ends after ~123 frames
        const int SweepMaxFrames = 400;

        static void SyntheticEyeAngles(float headYaw, float headPitch, out float eyeYaw, out float eyePitch)
        {
            eyeYaw = SynthYawGain * headYaw;
            eyePitch = SynthPitchGain * headPitch + SynthPitchQuad * headYaw * headYaw;
        }

        /// <summary>
        /// Frame of the imperfect tracker at a head pose (frontal-face geometry). <paramref name="extraEyeYaw"/> and <paramref name="extraEyePitch"/>
        /// add a gross eye-angle error.
        /// </summary>
        FaceFrame SyntheticFrame(float headYaw, float headPitch, float extraEyeYaw = 0f, float blink = 0f, float extraEyePitch = 0f)
        {
            SyntheticEyeAngles(headYaw, headPitch, out float eyeYaw, out float eyePitch);
            _timeMs += FrameMs; // strictly increasing timestamp for every Process call
            return MakeModelFrame(_timeMs, headYaw, headPitch, 0f, eyeYaw + extraEyeYaw, eyePitch + extraEyePitch, blink: blink);
        }

        GazeSample RunSynthetic(int frames, float headYaw, float headPitch, float extraEyeYaw = 0f, float blink = 0f)
        {
            GazeSample s = default;
            for (int i = 0; i < frames; i++)
                s = _est.Process(SyntheticFrame(headYaw, headPitch, extraEyeYaw, blink));
            return s;
        }

        /// <summary>
        /// Begins a head calibration and feeds the sweep until the state leaves Collecting: a yaw sweep (amplitude <paramref name="yawAmpDeg"/>, pitch 0)
        /// for the first half, then a pitch sweep with a small yaw wobble. Every <paramref name="outlierEvery"/>-th frame (if &gt; 0) gets a gross
        /// eye-angle error of 0.75x, 1x or 1.25x <paramref name="outlierDeg"/> (cycling), alternating yaw+, pitch-, yaw-, pitch+.
        /// </summary>
        void RunHeadSweep(float yawAmpDeg, float pitchAmpDeg, int outlierEvery = 0, float outlierDeg = 20f)
        {
            _est.BeginHeadCalibration();
            Assert.AreEqual(HeadCalibrationState.Collecting, _est.HeadCalibrationState);

            float wobbleDeg = 0.15f * yawAmpDeg;
            int frames = 0;
            while (_est.HeadCalibrationState == HeadCalibrationState.Collecting && frames < SweepMaxFrames)
            {
                float hy, hp;
                if (frames < SweepHalfFrames)
                {
                    float phase = 2f * Mathf.PI * frames / SweepHalfFrames;
                    hy = yawAmpDeg * Mathf.Sin(phase);
                    hp = 0f;
                }
                else
                {
                    float phase = 2f * Mathf.PI * (frames - SweepHalfFrames) / SweepHalfFrames;
                    hy = wobbleDeg * Mathf.Sin(2f * phase);
                    hp = pitchAmpDeg * Mathf.Sin(phase);
                }
                float extraYaw = 0f, extraPitch = 0f;
                if (outlierEvery > 0 && frames % outlierEvery == outlierEvery - 1)
                {
                    int k = frames / outlierEvery;
                    float mag = outlierDeg * (0.75f + 0.25f * (k % 3));
                    switch (k % 4)
                    {
                        case 0: extraYaw = mag; break;
                        case 1: extraPitch = -mag; break;
                        case 2: extraYaw = -mag; break;
                        default: extraPitch = mag; break;
                    }
                }
                _est.Process(SyntheticFrame(hy, hp, extraYaw, 0f, extraPitch));
                frames++;
            }
            Assert.Less(frames, SweepMaxFrames, "head calibration should finish within the frame budget");
        }

        void RunStandardHeadSweep(int outlierEvery = 0) => RunHeadSweep(SweepYawDeg, SweepPitchDeg, outlierEvery);

        /// <summary>At head poses inside the collected range, the corrected gaze must be near zero and on center.</summary>
        void AssertCorrectedAtPoses(float toleranceDeg)
        {
            foreach (float yaw in new[] { -15f, 0f, 15f })
            foreach (float pitch in new[] { -8f, 0f, 8f })
            {
                var s = RunSynthetic(10, yaw, pitch);
                string pose = $"head yaw {yaw}, pitch {pitch}";
                Assert.Less(Mathf.Abs(s.GazeYaw), toleranceDeg, "GazeYaw at " + pose);
                Assert.Less(Mathf.Abs(s.GazePitch), toleranceDeg, "GazePitch at " + pose);
                Assert.IsTrue(s.IsOnCenter, "IsOnCenter at " + pose);
            }
        }

        [Test]
        public void IrisShiftedImageLeft_GivesPositiveEyeYaw()
        {
            var s = Run(10, eyeYaw: 10f);
            Assert.AreEqual(10f, s.EyeYaw, 0.3f);
            Assert.AreEqual(0f, s.EyePitch, 0.3f);
        }

        [Test]
        public void IrisShiftedImageRight_GivesNegativeEyeYaw()
        {
            var s = Run(10, eyeYaw: -10f);
            Assert.AreEqual(-10f, s.EyeYaw, 0.3f);
        }

        [Test]
        public void IrisShiftedUp_GivesPositiveEyePitch()
        {
            var s = Run(10, eyePitch: 10f);
            Assert.AreEqual(10f, s.EyePitch, 0.3f);
            Assert.AreEqual(0f, s.EyeYaw, 0.3f);
        }

        [Test]
        public void IrisPairingDoesNotDependOnLandmarkOrder()
        {
            var a = Run(10, eyeYaw: 10f, eyePitch: 5f);
            _est = new GazeEstimator(_est.Settings);
            var b = Run(10, eyeYaw: 10f, eyePitch: 5f, swapIrisOrder: true);
            Assert.AreEqual(a.EyeYaw, b.EyeYaw, 1e-3f);
            Assert.AreEqual(a.EyePitch, b.EyePitch, 1e-3f);
            Assert.Greater(b.EyeYaw, 0f);
        }

        [Test]
        public void EyeYawGain_ScalesEyeInHeadYaw()
        {
            var settings = FastSettings();
            settings.EyeYawGain = 2f;
            _est = new GazeEstimator(settings);
            var s = Run(10, eyeYaw: 5f);
            Assert.AreEqual(10f, s.EyeYaw, 0.3f);
            Assert.AreEqual(10f, s.GazeYaw, 0.3f);
            Assert.AreEqual(0f, s.EyePitch, 0.3f);
        }

        [Test]
        public void EyePitchGain_ScalesEyeInHeadPitch()
        {
            var settings = FastSettings();
            settings.EyePitchGain = 1.8f;
            _est = new GazeEstimator(settings);
            var s = Run(10, eyePitch: 5f);
            Assert.AreEqual(9f, s.EyePitch, 0.3f);
            Assert.AreEqual(9f, s.GazePitch, 0.3f);
            Assert.AreEqual(0f, s.EyeYaw, 0.3f);
        }

        [TestCase(-25f)]
        [TestCase(-10f)]
        [TestCase(10f)]
        [TestCase(25f)]
        public void HeadTurn_EyesFixatingCamera_GazeStaysZero(float headYaw)
        {
            var s = Run(10, headYaw: headYaw, lookAtCamera: true);
            Assert.AreEqual(headYaw, s.HeadYaw, 0.1f);
            Assert.AreEqual(0f, s.GazeYaw, 0.3f);
            Assert.AreEqual(0f, s.GazePitch, 0.3f);
            Assert.AreEqual(-headYaw, s.EyeYaw, 0.3f);
        }

        [TestCase(-15f)]
        [TestCase(15f)]
        public void HeadPitch_EyesFixatingCamera_GazeStaysZero(float headPitch)
        {
            var s = Run(10, headPitch: headPitch, lookAtCamera: true);
            Assert.AreEqual(headPitch, s.HeadPitch, 0.1f);
            Assert.AreEqual(0f, s.GazeYaw, 0.3f);
            Assert.AreEqual(0f, s.GazePitch, 0.3f);
            // Pitching the head lowers the eye midpoint slightly (~0.15 cm), tilting the eye-to-lens ray by ~0.15 deg.
            Assert.AreEqual(-headPitch, s.EyePitch, 0.5f);
        }

        [Test]
        public void CombinedHeadPose_GazeMatchesForwardModel()
        {
            // Roll shifts the eye midpoint sideways, so looking at the lens would not read exactly zero; compare against the forward model instead.
            ExpectedCameraGaze(15f, -10f, 8f, 4f, -2f, out float expYaw, out float expPitch);
            var s = Run(10, headYaw: 15f, headPitch: -10f, headRoll: 8f, eyeYaw: 4f, eyePitch: -2f);
            Assert.AreEqual(expYaw, s.GazeYaw, 0.3f);
            Assert.AreEqual(expPitch, s.GazePitch, 0.3f);
            Assert.AreEqual(4f, s.EyeYaw, 0.3f);
            Assert.AreEqual(-2f, s.EyePitch, 0.3f);
        }

        [TestCase(80f, 0f)]
        [TestCase(-80f, 0f)]
        [TestCase(0f, 80f)]
        [TestCase(0f, -80f)]
        [TestCase(80f, 80f)]
        [TestCase(-80f, -80f)]
        public void FaceShiftInImage_LeavesGazeAnglesUnchanged(float shiftX, float shiftY)
        {
            // Gaze is the camera-space direction of the eye ray; where the face sits in the image does not change it.
            ExpectedCameraGaze(10f, 5f, 0f, 6f, -4f, out float expYaw, out float expPitch);
            var s = Run(10, headYaw: 10f, headPitch: 5f, eyeYaw: 6f, eyePitch: -4f, faceShiftPx: new Vector2(shiftX, shiftY));
            Assert.AreEqual(expYaw, s.GazeYaw, 0.3f);
            Assert.AreEqual(expPitch, s.GazePitch, 0.3f);
            Assert.AreEqual(6f, s.EyeYaw, 0.3f);
            Assert.AreEqual(-4f, s.EyePitch, 0.3f);
        }

        [Test]
        public void Uncalibrated_LookingAlongCameraAxis_GivesZero()
        {
            var s = Run(10);
            Assert.IsFalse(s.IsCalibrated);
            Assert.AreEqual(0f, s.GazeYaw, 0.05f);
            Assert.AreEqual(0f, s.GazePitch, 0.05f);
        }

        [Test]
        public void EyeOnly_GazeMatchesEyeAngles()
        {
            var s = Run(10, eyeYaw: 12f, eyePitch: -8f);
            Assert.AreEqual(12f, s.GazeYaw, 0.3f);
            Assert.AreEqual(-8f, s.GazePitch, 0.3f);
            Assert.AreEqual(12f, s.EyeYaw, 0.3f);
            Assert.AreEqual(-8f, s.EyePitch, 0.3f);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(20f, 0f, 0f)]
        [TestCase(-30f, 0f, 0f)]
        [TestCase(0f, 15f, 0f)]
        [TestCase(0f, -25f, 0f)]
        [TestCase(0f, 0f, 10f)]
        [TestCase(0f, 0f, -20f)]
        [TestCase(20f, 10f, 5f)]
        [TestCase(-35f, -15f, 12f)]
        public void HeadPoseMatrixRoundTrips(float yaw, float pitch, float roll)
        {
            HeadPoseMath.FromMatrix(HeadPoseMath.ToMatrix(yaw, pitch, roll), out float y, out float p, out float r);
            Assert.AreEqual(yaw, y, 0.05f);
            Assert.AreEqual(pitch, p, 0.05f);
            Assert.AreEqual(roll, r, 0.05f);
        }

        [Test]
        public void HeadPoseIsReportedThroughSample()
        {
            var s = Run(5, headYaw: 20f, headPitch: 10f, headRoll: 5f);
            Assert.AreEqual(20f, s.HeadYaw, 0.1f);
            Assert.AreEqual(10f, s.HeadPitch, 0.1f);
            Assert.AreEqual(5f, s.HeadRoll, 0.1f);
            Assert.IsTrue(s.HasFace);
        }

        [Test]
        public void Uncalibrated_ReportsRawGazeAndIsNotOnCenter()
        {
            var s = Run(10, headYaw: 10f, headPitch: 5f, eyeYaw: 4f, eyePitch: -3f);
            ExpectedCameraGaze(10f, 5f, 0f, 4f, -3f, out float expYaw, out float expPitch);
            Assert.IsFalse(s.IsCalibrated);
            Assert.IsFalse(s.IsOnCenter);
            Assert.AreEqual(expYaw, s.GazeYaw, 0.3f);
            Assert.AreEqual(expPitch, s.GazePitch, 0.3f);
            Assert.AreEqual(4f, s.EyeYaw, 0.3f);
            Assert.AreEqual(-3f, s.EyePitch, 0.3f);
        }

        [Test]
        public void Calibration_ReportsProgressAndCompletes()
        {
            Assert.IsFalse(_est.IsCalibrating);
            _est.BeginCalibration();
            Assert.IsTrue(_est.IsCalibrating);

            Run(15);
            Assert.Greater(_est.CalibrationProgress, 0f);
            Assert.Less(_est.CalibrationProgress, 1f);
            Assert.IsFalse(_est.IsCalibrated);

            Run(30);
            Assert.IsTrue(_est.IsCalibrated);
            Assert.IsFalse(_est.IsCalibrating);
            Assert.AreEqual(1f, _est.CalibrationProgress, 1e-4f);
        }

        [Test]
        public void Calibration_PausesWithoutFace()
        {
            _est.BeginCalibration();
            Run(10);
            float progress = _est.CalibrationProgress;
            Run(30, hasFace: false);
            Assert.AreEqual(progress, _est.CalibrationProgress, 1e-4f);
            Assert.IsTrue(_est.IsCalibrating);
        }

        [Test]
        public void Calibration_IgnoresBlinkFrames()
        {
            _est.BeginCalibration();
            Run(10);
            float progress = _est.CalibrationProgress;
            Run(30, blink: 0.9f);
            Assert.AreEqual(progress, _est.CalibrationProgress, 1e-4f);
        }

        [Test]
        public void CancelAndResetCalibration()
        {
            _est.BeginCalibration();
            Run(5);
            _est.CancelCalibration();
            Assert.IsFalse(_est.IsCalibrating);
            Assert.IsFalse(_est.IsCalibrated);

            Calibrate();
            _est.ResetCalibration();
            Assert.IsFalse(_est.IsCalibrated);
            Assert.IsFalse(Run(5).IsOnCenter);
        }

        [Test]
        public void AfterCalibration_CenteredSampleIsOnCenter()
        {
            Calibrate();
            var s = Run(10);
            Assert.IsTrue(s.IsCalibrated);
            Assert.IsTrue(s.IsOnCenter);
            Assert.AreEqual(0f, s.DeviationDeg, 0.5f);
        }

        [Test]
        public void Calibration_SubtractsBaseline()
        {
            // The user's "center" is a turned head with eyes slightly off; baseline absorbs it.
            _est.BeginCalibration();
            Run(60, headYaw: 15f, eyeYaw: 4f, eyePitch: -2f);
            Assert.IsTrue(_est.IsCalibrated);
            var s = Run(10, headYaw: 15f, eyeYaw: 4f, eyePitch: -2f);
            Assert.AreEqual(0f, s.GazeYaw, 0.1f);
            Assert.AreEqual(0f, s.GazePitch, 0.1f);
            Assert.IsTrue(s.IsOnCenter);

            // Moving the eyes 6 deg further (same head) moves the camera-space gaze by the model-predicted amount.
            ExpectedCameraGaze(15f, 0f, 0f, 4f, -2f, out float baseYaw, out _);
            ExpectedCameraGaze(15f, 0f, 0f, 10f, -2f, out float movedYaw, out _);
            var moved = Run(10, headYaw: 15f, eyeYaw: 10f, eyePitch: -2f);
            Assert.AreEqual(movedYaw - baseYaw, moved.GazeYaw, 0.3f);
        }

        [Test]
        public void LargeIrisOffset_IsNotOnCenter()
        {
            Calibrate();
            Assert.IsTrue(Run(5).IsOnCenter);
            var s = Run(10, eyeYaw: 12f);
            Assert.AreEqual(12f, s.DeviationDeg, 0.3f);
            Assert.Greater(s.DeviationDeg, 8f);
            Assert.IsFalse(s.IsOnCenter);
        }

        [Test]
        public void LargeHeadTurn_IsNotOnCenter()
        {
            Calibrate();
            // Head turned 25 deg with the eyes straight ahead in the head: the gaze follows the head.
            var s = Run(10, headYaw: 25f, eyeYaw: 0f);
            Assert.AreEqual(25f, s.GazeYaw, 0.3f);
            Assert.IsFalse(s.IsOnCenter);
        }

        [Test]
        public void Hysteresis_KeepsStateBetweenEnterAndExitThresholds()
        {
            Calibrate();
            Assert.IsTrue(Run(5).IsOnCenter);

            // 6.5 deg is between enter (5) and exit (8): stays on center.
            var s = Run(5, eyeYaw: 6.5f);
            Assert.AreEqual(6.5f, s.DeviationDeg, 0.2f);
            Assert.IsTrue(s.IsOnCenter);

            // Beyond exit: off center.
            Assert.IsFalse(Run(5, eyeYaw: 10f).IsOnCenter);

            // Back in the band: stays off until below enter.
            Assert.IsFalse(Run(5, eyeYaw: 6.5f).IsOnCenter);
            Assert.IsTrue(Run(5, eyeYaw: 2f).IsOnCenter);
        }

        [Test]
        public void Blink_KeepsStateAndHoldsEyeAngles()
        {
            Calibrate();
            var before = Run(5, eyeYaw: 12f); // looking away, off center
            Assert.IsFalse(before.IsOnCenter);
            Assert.Greater(before.EyeYaw, 5f);

            // During the blink the iris landmarks jump to garbage (centered here); they must be ignored.
            var during = Run(10, blink: 0.9f);
            Assert.IsTrue(during.IsBlinking);
            Assert.IsFalse(during.IsOnCenter);
            Assert.AreEqual(before.EyeYaw, during.EyeYaw, 1e-3f);
            Assert.AreEqual(before.EyePitch, during.EyePitch, 1e-3f);

            // And the other way round: on center stays on center through a blink with a wild iris.
            _est.CancelCalibration();
            Run(5);
            Assert.IsTrue(Run(5).IsOnCenter);
            var blinkOn = Run(10, eyeYaw: 12f, blink: 0.9f);
            Assert.IsTrue(blinkOn.IsBlinking);
            Assert.IsTrue(blinkOn.IsOnCenter);
        }

        [Test]
        public void NoFace_ClearsOnCenterAndKeepsLastAngles()
        {
            Calibrate();
            var withFace = Run(5, headYaw: 3f);
            Assert.IsTrue(withFace.IsOnCenter);

            var s = Run(3, hasFace: false);
            Assert.IsFalse(s.HasFace);
            Assert.IsFalse(s.IsOnCenter);
            Assert.IsTrue(s.IsCalibrated);
            Assert.AreEqual(withFace.HeadYaw, s.HeadYaw, 1e-3f);
            Assert.AreEqual(_timeMs, s.TimestampMs, 1e-6);
            Assert.IsTrue(_est.IsCalibrated);
            Assert.AreEqual(s.HasFace, _est.Latest.HasFace);
        }

        [Test]
        public void HeadCalibration_RemovesSystematicHeadDependentError()
        {
            Calibrate();

            // Without the head model the imperfect tracker's gaze drifts with the head: proves the scenario is meaningful.
            var before = RunSynthetic(10, 15f, 0f);
            Assert.Greater(Mathf.Abs(before.GazeYaw), 2f, "centre calibration alone should leave a clear head-dependent yaw error");

            RunStandardHeadSweep();
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);
            Assert.IsTrue(_est.IsHeadCalibrated);
            Assert.IsFalse(_est.IsHeadCalibrating);
            Assert.Less(_est.HeadCalibrationRmsDeg, 0.3f);
            Assert.AreEqual(1f, _est.HeadCalibrationProgress, 1e-4f);

            AssertCorrectedAtPoses(0.5f);
        }

        [Test]
        public void HeadCalibration_InsufficientMotion_KeepsPreviousState()
        {
            // Only +-4 deg yaw and +-2 deg pitch: far below the required ranges.
            RunHeadSweep(4f, 2f);
            Assert.AreEqual(HeadCalibrationState.InsufficientMotion, _est.HeadCalibrationState);
            Assert.IsFalse(_est.IsHeadCalibrated);
            Assert.IsFalse(_est.IsHeadCalibrating);

            // After a successful calibration, a second insufficient one keeps the first model.
            RunStandardHeadSweep();
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);
            float rms = _est.HeadCalibrationRmsDeg;

            RunHeadSweep(4f, 2f);
            Assert.AreEqual(HeadCalibrationState.InsufficientMotion, _est.HeadCalibrationState);
            Assert.IsTrue(_est.IsHeadCalibrated);
            Assert.AreEqual(rms, _est.HeadCalibrationRmsDeg, 1e-6f);

            var s = RunSynthetic(10, 15f, 0f);
            Assert.IsTrue(s.IsCalibrated);
            Assert.Less(Mathf.Abs(s.GazeYaw), 0.5f);
            Assert.Less(Mathf.Abs(s.GazePitch), 0.5f);
        }

        [Test]
        public void HeadCalibration_RejectsOutliers()
        {
            Calibrate();
            // 10% of the collection frames (every 10th) have the eyes 15-25 deg off, in mixed directions on both axes.
            RunStandardHeadSweep(outlierEvery: 10);
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);
            Assert.IsTrue(_est.IsHeadCalibrated);
            // The reported RMS covers the kept inliers only: an outlier of 15+ deg would dominate it otherwise.
            Assert.Less(_est.HeadCalibrationRmsDeg, 0.3f);

            AssertCorrectedAtPoses(0.5f);
        }

        [Test]
        public void HeadCalibration_ClampsOutsideCollectedRange()
        {
            RunStandardHeadSweep();
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);
            Assert.AreEqual(SweepYawDeg, _est.HeadCalibrationYawRange.y, 0.5f);
            Assert.AreEqual(SweepPitchDeg, _est.HeadCalibrationPitchRange.y, 0.5f);

            // Head at twice the collected +-20 deg yaw, eyes held at what the imperfect tracker reports at the edge of the range.
            // Unclamped, the quadratic would predict a ~-32 deg U-shaped pitch error here and the corrected pitch would be ~+20 deg.
            SyntheticEyeAngles(SweepYawDeg, 0f, out float edgeEyeYaw, out float edgeEyePitch);
            GazeSample s = default;
            for (int i = 0; i < 10; i++)
            {
                _timeMs += FrameMs;
                s = _est.Process(MakeModelFrame(_timeMs, 40f, 0f, 0f, edgeEyeYaw, edgeEyePitch));
            }

            Assert.AreEqual(40f, s.HeadYaw, 0.1f);
            Assert.IsFalse(float.IsNaN(s.GazeYaw) || float.IsInfinity(s.GazeYaw));
            Assert.IsFalse(float.IsNaN(s.GazePitch) || float.IsInfinity(s.GazePitch));
            Assert.Less(Mathf.Abs(s.GazePitch), 10f);
        }

        [Test]
        public void HeadCalibration_CancelAndReset()
        {
            _est.BeginHeadCalibration();
            Assert.IsTrue(_est.IsHeadCalibrating);
            RunSynthetic(10, 10f, 0f);
            Assert.Greater(_est.HeadCalibrationProgress, 0f);

            _est.CancelHeadCalibration();
            Assert.AreEqual(HeadCalibrationState.None, _est.HeadCalibrationState);
            Assert.IsFalse(_est.IsHeadCalibrating);
            Assert.IsFalse(_est.IsHeadCalibrated);
            Assert.AreEqual(0f, _est.HeadCalibrationProgress, 1e-4f);

            Calibrate();
            RunStandardHeadSweep();
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);

            // Cancelling a later attempt restores the successful state and keeps its model.
            _est.BeginHeadCalibration();
            RunSynthetic(10, 10f, 0f);
            _est.CancelHeadCalibration();
            Assert.AreEqual(HeadCalibrationState.Succeeded, _est.HeadCalibrationState);
            Assert.IsTrue(_est.IsHeadCalibrated);

            _est.ResetCalibration();
            Assert.AreEqual(HeadCalibrationState.None, _est.HeadCalibrationState);
            Assert.IsFalse(_est.IsHeadCalibrated);
            Assert.IsFalse(_est.IsCalibrated);
            Assert.IsFalse(RunSynthetic(5, 0f, 0f).IsCalibrated);
        }

        [Test]
        public void HeadCalibration_BlinksDoNotAdvanceOrCollect()
        {
            _est.BeginHeadCalibration();
            RunSynthetic(10, 5f, 0f);
            float progress = _est.HeadCalibrationProgress;
            Assert.Greater(progress, 0f);
            Assert.Less(progress, 1f);

            // 200 blinking frames (~6.6 s) are more than the 4 s duration, yet nothing advances and nothing finishes.
            var s = RunSynthetic(200, 25f, 10f, blink: 0.9f);
            Assert.IsTrue(s.IsBlinking);
            Assert.AreEqual(progress, _est.HeadCalibrationProgress, 1e-4f);
            Assert.AreEqual(HeadCalibrationState.Collecting, _est.HeadCalibrationState);
            Assert.IsFalse(_est.IsHeadCalibrated);
        }

        [Test]
        public void OneEuroFilter_ConvergesToConstant()
        {
            var filter = new OneEuroFilter(1f, 0.05f);
            double t = 0;
            Assert.AreEqual(0f, filter.Filter(0f, t), 1e-6f);

            float v = 0f;
            for (int i = 0; i < 300; i++)
            {
                t += 0.033;
                v = filter.Filter(10f, t);
            }
            Assert.AreEqual(10f, v, 0.01f);
        }

        [Test]
        public void OneEuroFilter_FirstSamplePassesThroughAndConstantStaysConstant()
        {
            var filter = new OneEuroFilter(1f, 0.05f);
            Assert.AreEqual(7.5f, filter.Filter(7.5f, 5.0), 1e-6f);
            for (int i = 1; i <= 20; i++)
                Assert.AreEqual(7.5f, filter.Filter(7.5f, 5.0 + i * 0.033), 1e-4f);
        }

        [Test]
        public void OneEuroFilter_NonIncreasingTimestampReturnsPrevious()
        {
            var filter = new OneEuroFilter(1f, 0.05f);
            filter.Filter(1f, 1.0);
            float prev = filter.Filter(2f, 1.033);
            Assert.AreEqual(prev, filter.Filter(100f, 1.033), 1e-6f);
            Assert.AreEqual(prev, filter.Filter(100f, 0.5), 1e-6f);
        }

        [Test]
        public void OneEuroFilter_ResetForgetsHistory()
        {
            var filter = new OneEuroFilter(1f, 0.05f);
            filter.Filter(0f, 0.0);
            filter.Filter(0f, 0.033);
            filter.Reset();
            Assert.AreEqual(50f, filter.Filter(50f, 0.0), 1e-6f);
        }

        [Test]
        public void OneEuroFilter_HigherBetaTracksFastMotionCloser()
        {
            var slow = new OneEuroFilter(1f, 0f);
            var fast = new OneEuroFilter(1f, 1f);
            float a = 0f, b = 0f;
            for (int i = 0; i < 30; i++)
            {
                double t = i * 0.033;
                float x = i * 3f;
                a = slow.Filter(x, t);
                b = fast.Filter(x, t);
            }
            float target = 29 * 3f;
            Assert.Less(Mathf.Abs(target - b), Mathf.Abs(target - a));
        }
    }
}
