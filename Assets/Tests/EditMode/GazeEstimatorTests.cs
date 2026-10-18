using NUnit.Framework;
using UnityEngine;

namespace VestibularGame.FaceTracking.Tests
{
    public class GazeEstimatorTests
    {
        const double FrameMs = 33.0;

        GazeEstimator _est;
        double _timeMs;

        [SetUp]
        public void SetUp()
        {
            // High cutoff and no beta: the filters pass values through almost unchanged, so tests settle fast.
            _est = new GazeEstimator(new GazeEstimatorSettings
            {
                FilterMinCutoff = 1000f,
                FilterBeta = 0f,
                CalibrationDurationSec = 1f,
            });
            _timeMs = 1000.0;
        }

        /// <summary>Synthetic frame. Iris offsets are in normalized image units (negative dx = image left, negative dy = image up).</summary>
        static FaceFrame MakeFrame(double timestampMs, float yaw = 0f, float pitch = 0f, float roll = 0f,
            float irisDx = 0f, float irisDy = 0f, float blink = 0f, bool hasFace = true, bool swapIrisOrder = false)
        {
            var f = new FaceFrame
            {
                HasFace = hasFace,
                TimestampMs = timestampMs,
                ImageWidth = 640,
                ImageHeight = 480,
                FacialTransform = HeadPoseMath.ToMatrix(yaw, pitch, roll),
            };

            // Eye A (image left): outer 0.39, inner 0.45. Eye B (image right): inner 0.55, outer 0.61.
            f.Landmarks[FaceLandmarkIndex.EyeAOuter] = new Vector3(0.39f, 0.45f, 0f);
            f.Landmarks[FaceLandmarkIndex.EyeAInner] = new Vector3(0.45f, 0.45f, 0f);
            f.Landmarks[FaceLandmarkIndex.EyeBInner] = new Vector3(0.55f, 0.45f, 0f);
            f.Landmarks[FaceLandmarkIndex.EyeBOuter] = new Vector3(0.61f, 0.45f, 0f);

            var irisA = new Vector3(0.42f + irisDx, 0.45f + irisDy, 0f);
            var irisB = new Vector3(0.58f + irisDx, 0.45f + irisDy, 0f);
            f.Landmarks[FaceLandmarkIndex.IrisCenter1] = swapIrisOrder ? irisB : irisA;
            f.Landmarks[FaceLandmarkIndex.IrisCenter2] = swapIrisOrder ? irisA : irisB;

            f.Blendshapes[BlendshapeIndex.EyeBlinkLeft] = blink;
            f.Blendshapes[BlendshapeIndex.EyeBlinkRight] = blink;
            return f;
        }

        GazeSample Run(int frames, float yaw = 0f, float pitch = 0f, float roll = 0f,
            float irisDx = 0f, float irisDy = 0f, float blink = 0f, bool hasFace = true, bool swapIrisOrder = false)
        {
            GazeSample s = default;
            for (int i = 0; i < frames; i++)
            {
                _timeMs += FrameMs;
                s = _est.Process(MakeFrame(_timeMs, yaw, pitch, roll, irisDx, irisDy, blink, hasFace, swapIrisOrder));
            }
            return s;
        }

        void Calibrate()
        {
            _est.BeginCalibration();
            Run(60);
            Assert.IsTrue(_est.IsCalibrated, "calibration should complete after 60 frames (~2 s)");
        }

        [Test]
        public void IrisShiftedImageLeft_GivesPositiveEyeYaw()
        {
            var s = Run(10, irisDx: -0.01f);
            Assert.Greater(s.EyeYaw, 5f);
            Assert.AreEqual(0f, s.EyePitch, 0.5f);
        }

        [Test]
        public void IrisShiftedImageRight_GivesNegativeEyeYaw()
        {
            var s = Run(10, irisDx: 0.01f);
            Assert.Less(s.EyeYaw, -5f);
        }

        [Test]
        public void IrisShiftedUp_GivesPositiveEyePitch()
        {
            var s = Run(10, irisDy: -0.01f);
            Assert.Greater(s.EyePitch, 5f);
            Assert.AreEqual(0f, s.EyeYaw, 0.5f);
        }

        [Test]
        public void IrisPairingDoesNotDependOnLandmarkOrder()
        {
            var a = Run(10, irisDx: -0.01f, irisDy: -0.005f);
            _est = new GazeEstimator(_est.Settings);
            var b = Run(10, irisDx: -0.01f, irisDy: -0.005f, swapIrisOrder: true);
            Assert.AreEqual(a.EyeYaw, b.EyeYaw, 1e-3f);
            Assert.AreEqual(a.EyePitch, b.EyePitch, 1e-3f);
            Assert.Greater(b.EyeYaw, 0f);
        }

        [Test]
        public void EyeGainScalesEyeAngles()
        {
            var baseline = Run(10, irisDx: -0.005f);
            _est = new GazeEstimator(new GazeEstimatorSettings { FilterMinCutoff = 1000f, FilterBeta = 0f, EyeGain = 2f });
            var doubled = Run(10, irisDx: -0.005f);
            Assert.AreEqual(baseline.EyeYaw * 2f, doubled.EyeYaw, 1e-2f);
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
            var s = Run(5, yaw: 20f, pitch: 10f, roll: 5f);
            Assert.AreEqual(20f, s.HeadYaw, 0.1f);
            Assert.AreEqual(10f, s.HeadPitch, 0.1f);
            Assert.AreEqual(5f, s.HeadRoll, 0.1f);
            Assert.IsTrue(s.HasFace);
        }

        [Test]
        public void Uncalibrated_ReportsRawGazeAndIsNotOnCenter()
        {
            var s = Run(10, yaw: 10f);
            Assert.IsFalse(s.IsCalibrated);
            Assert.IsFalse(s.IsOnCenter);
            Assert.AreEqual(s.HeadYaw + s.EyeYaw, s.GazeYaw, 1e-3f);
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
            Run(60, yaw: 15f, irisDx: -0.003f);
            Assert.IsTrue(_est.IsCalibrated);
            var s = Run(10, yaw: 15f, irisDx: -0.003f);
            Assert.AreEqual(0f, s.GazeYaw, 0.1f);
            Assert.IsTrue(s.IsOnCenter);
        }

        [Test]
        public void LargeIrisOffset_IsNotOnCenter()
        {
            Calibrate();
            Assert.IsTrue(Run(5).IsOnCenter);
            var s = Run(10, irisDx: -0.01f);
            Assert.Greater(s.DeviationDeg, 8f);
            Assert.IsFalse(s.IsOnCenter);
        }

        [Test]
        public void LargeHeadTurn_IsNotOnCenter()
        {
            Calibrate();
            var s = Run(10, yaw: 25f);
            Assert.AreEqual(25f, s.GazeYaw, 0.2f);
            Assert.IsFalse(s.IsOnCenter);
        }

        [Test]
        public void Hysteresis_KeepsStateBetweenEnterAndExitThresholds()
        {
            Calibrate();
            Assert.IsTrue(Run(5).IsOnCenter);

            // 6.5 deg is between enter (5) and exit (8): stays on center.
            var s = Run(5, yaw: 6.5f);
            Assert.AreEqual(6.5f, s.DeviationDeg, 0.2f);
            Assert.IsTrue(s.IsOnCenter);

            // Beyond exit: off center.
            Assert.IsFalse(Run(5, yaw: 10f).IsOnCenter);

            // Back in the band: stays off until below enter.
            Assert.IsFalse(Run(5, yaw: 6.5f).IsOnCenter);
            Assert.IsTrue(Run(5, yaw: 2f).IsOnCenter);
        }

        [Test]
        public void Blink_KeepsStateAndHoldsEyeAngles()
        {
            Calibrate();
            var before = Run(5, irisDx: -0.01f); // looking away, off center
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
            var blinkOn = Run(10, irisDx: -0.01f, blink: 0.9f);
            Assert.IsTrue(blinkOn.IsBlinking);
            Assert.IsTrue(blinkOn.IsOnCenter);
        }

        [Test]
        public void NoFace_ClearsOnCenterAndKeepsLastAngles()
        {
            Calibrate();
            var withFace = Run(5, yaw: 3f);
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
