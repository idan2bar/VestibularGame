using UnityEngine;

namespace VestibularGame.FaceTracking
{
    [System.Serializable]
    public class GazeEstimatorSettings
    {
        public float OnCenterEnterDeg = 5f;   // become "on center" when deviation < this
        public float OnCenterExitDeg = 8f;    // become "off center" when deviation > this
        public float BlinkThreshold = 0.5f;   // eyeBlink blendshape score
        public float FilterMinCutoff = 1.0f;
        public float FilterBeta = 0.05f;
        public float CalibrationDurationSec = 2f;
        public float EyeGain = 1f;            // multiplier on eye-in-head angles
    }

    /// <summary>
    /// Turns <see cref="FaceFrame"/>s into a <see cref="GazeSample"/>: head pose (from the facial transform) plus
    /// eye-in-head angles (iris position within the eye corners), made relative to a calibrated screen center.
    /// Sign conventions follow <see cref="GazeSample"/>. Process is allocation-free.
    /// </summary>
    public sealed class GazeEstimator
    {
        // Eyeball radius as a fraction of eye width (corner-to-corner distance).
        const float EyeballRadiusRatio = 0.42f;
        // Largest frame gap that still counts toward calibration time.
        const double MaxCalibrationStepSec = 0.25;

        readonly OneEuroFilter _headYawF, _headPitchF, _headRollF, _eyeYawF, _eyePitchF;

        GazeSample _latest;
        bool _calibrating, _isCalibrated, _onCenter;
        float _baseYaw, _baseSum0, _basePitch, _baseSum1;
        int _calSamples;
        double _calElapsed;
        bool _prevValid;
        double _lastTime;
        float _eyeYaw, _eyePitch;

        public GazeEstimator(GazeEstimatorSettings settings = null)
        {
            Settings = settings ?? new GazeEstimatorSettings();
            _headYawF = NewFilter();
            _headPitchF = NewFilter();
            _headRollF = NewFilter();
            _eyeYawF = NewFilter();
            _eyePitchF = NewFilter();
        }

        public GazeEstimatorSettings Settings { get; }
        public GazeSample Latest => _latest;
        public bool IsCalibrating => _calibrating;
        public bool IsCalibrated => _isCalibrated;

        public float CalibrationProgress
        {
            get
            {
                if (!_calibrating) return _isCalibrated ? 1f : 0f;
                float d = Settings.CalibrationDurationSec;
                return d <= 0f ? 0f : Mathf.Clamp01((float)(_calElapsed / d));
            }
        }

        /// <summary>Starts averaging the gaze over <see cref="GazeEstimatorSettings.CalibrationDurationSec"/>. An earlier calibration stays active until the new one completes.</summary>
        public void BeginCalibration()
        {
            _calibrating = true;
            _calElapsed = 0;
            _calSamples = 0;
            _baseSum0 = 0f;
            _baseSum1 = 0f;
            _prevValid = false;
        }

        public void CancelCalibration()
        {
            _calibrating = false;
        }

        public void ResetCalibration()
        {
            _calibrating = false;
            _isCalibrated = false;
            _baseYaw = 0f;
            _basePitch = 0f;
            _onCenter = false;
            _latest.IsCalibrated = false;
            _latest.IsOnCenter = false;
        }

        /// <summary>Call once per new frame; time is taken from <see cref="FaceFrame.TimestampMs"/>.</summary>
        public GazeSample Process(FaceFrame frame)
        {
            var s = _latest;
            s.TimestampMs = frame.TimestampMs;

            if (!frame.HasFace)
            {
                s.HasFace = false;
                s.IsBlinking = false;
                s.IsOnCenter = false;
                s.IsCalibrated = _isCalibrated;
                _onCenter = false;
                _prevValid = false;
                _latest = s;
                return s;
            }

            var cfg = Settings;
            double t = frame.TimestampMs * 0.001;
            SyncFilter(_headYawF, cfg);
            SyncFilter(_headPitchF, cfg);
            SyncFilter(_headRollF, cfg);
            SyncFilter(_eyeYawF, cfg);
            SyncFilter(_eyePitchF, cfg);

            HeadPoseMath.FromMatrix(frame.FacialTransform, out float hy, out float hp, out float hr);
            hy = _headYawF.Filter(hy, t);
            hp = _headPitchF.Filter(hp, t);
            hr = _headRollF.Filter(hr, t);

            float blink = Mathf.Max(frame.Blendshapes[BlendshapeIndex.EyeBlinkLeft], frame.Blendshapes[BlendshapeIndex.EyeBlinkRight]);
            bool blinking = blink > cfg.BlinkThreshold;

            if (!blinking && TryEstimateEyes(frame, out float ey, out float ep))
            {
                _eyeYaw = _eyeYawF.Filter(ey * cfg.EyeGain, t);
                _eyePitch = _eyePitchF.Filter(ep * cfg.EyeGain, t);
            }

            float rawYaw = hy + _eyeYaw;
            float rawPitch = hp + _eyePitch;

            if (_calibrating && !blinking)
            {
                if (_prevValid)
                    _calElapsed += System.Math.Min(System.Math.Max(t - _lastTime, 0.0), MaxCalibrationStepSec);
                _baseSum0 += rawYaw;
                _baseSum1 += rawPitch;
                _calSamples++;

                if (_calElapsed >= cfg.CalibrationDurationSec)
                {
                    _baseYaw = _baseSum0 / _calSamples;
                    _basePitch = _baseSum1 / _calSamples;
                    _isCalibrated = true;
                    _calibrating = false;
                }
            }
            _prevValid = !blinking;
            _lastTime = t;

            float gy = _isCalibrated ? rawYaw - _baseYaw : rawYaw;
            float gp = _isCalibrated ? rawPitch - _basePitch : rawPitch;
            float dev = Mathf.Sqrt(gy * gy + gp * gp);

            if (!_isCalibrated)
                _onCenter = false;
            else if (!blinking)
            {
                if (_onCenter) { if (dev > cfg.OnCenterExitDeg) _onCenter = false; }
                else if (dev < cfg.OnCenterEnterDeg) _onCenter = true;
            }

            s.HasFace = true;
            s.IsBlinking = blinking;
            s.IsCalibrated = _isCalibrated;
            s.HeadYaw = hy;
            s.HeadPitch = hp;
            s.HeadRoll = hr;
            s.EyeYaw = _eyeYaw;
            s.EyePitch = _eyePitch;
            s.GazeYaw = gy;
            s.GazePitch = gp;
            s.DeviationDeg = dev;
            s.IsOnCenter = _onCenter;
            _latest = s;
            return s;
        }

        /// <summary>Eye-in-head yaw/pitch in degrees (+yaw = user's right, +pitch = up), averaged over both eyes.</summary>
        static bool TryEstimateEyes(FaceFrame f, out float yawDeg, out float pitchDeg)
        {
            yawDeg = 0f;
            pitchDeg = 0f;

            // Pixel-proportional coordinates so angles are not distorted by the image aspect ratio.
            float sx = f.ImageWidth > 0 && f.ImageHeight > 0 ? f.ImageWidth : 4f;
            float sy = f.ImageWidth > 0 && f.ImageHeight > 0 ? f.ImageHeight : 3f;
            var lm = f.Landmarks;

            Vector2 a1 = Px(lm[FaceLandmarkIndex.EyeAOuter], sx, sy), a2 = Px(lm[FaceLandmarkIndex.EyeAInner], sx, sy);
            Vector2 b1 = Px(lm[FaceLandmarkIndex.EyeBInner], sx, sy), b2 = Px(lm[FaceLandmarkIndex.EyeBOuter], sx, sy);
            Vector2 i1 = Px(lm[FaceLandmarkIndex.IrisCenter1], sx, sy), i2 = Px(lm[FaceLandmarkIndex.IrisCenter2], sx, sy);

            // Pair iris centers to eyes by proximity to the corner midpoints.
            Vector2 midA = (a1 + a2) * 0.5f, midB = (b1 + b2) * 0.5f;
            float straight = (i1 - midA).sqrMagnitude + (i2 - midB).sqrMagnitude;
            float swapped = (i1 - midB).sqrMagnitude + (i2 - midA).sqrMagnitude;
            Vector2 irisA = straight <= swapped ? i1 : i2;
            Vector2 irisB = straight <= swapped ? i2 : i1;

            float yawSum = 0f, pitchSum = 0f, y, p;
            int n = 0;
            if (EyeAngles(a1, a2, irisA, out y, out p)) { yawSum += y; pitchSum += p; n++; }
            if (EyeAngles(b1, b2, irisB, out y, out p)) { yawSum += y; pitchSum += p; n++; }
            if (n == 0) return false;

            yawDeg = yawSum / n;
            pitchDeg = pitchSum / n;
            return true;
        }

        static bool EyeAngles(Vector2 c1, Vector2 c2, Vector2 iris, out float yawDeg, out float pitchDeg)
        {
            yawDeg = 0f;
            pitchDeg = 0f;

            Vector2 axis = c2 - c1;
            if (axis.x < 0f) axis = -axis; // always point toward image +x, whichever corner is inner/outer
            float width = axis.magnitude;
            if (width < 1e-3f) return false;
            axis /= width;
            var up = new Vector2(axis.y, -axis.x); // perpendicular pointing toward image up (y grows downward)

            Vector2 offset = iris - (c1 + c2) * 0.5f;
            float h = Vector2.Dot(offset, axis) / width;  // +: iris toward image right
            float v = Vector2.Dot(offset, up) / width;    // +: iris toward image up

            // Iris moving toward image left means the user looks to THEIR right (unmirrored image).
            yawDeg = -Mathf.Asin(Mathf.Clamp(h / EyeballRadiusRatio, -1f, 1f)) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Asin(Mathf.Clamp(v / EyeballRadiusRatio, -1f, 1f)) * Mathf.Rad2Deg;
            return true;
        }

        static Vector2 Px(Vector3 l, float sx, float sy) => new Vector2(l.x * sx, l.y * sy);

        OneEuroFilter NewFilter() => new OneEuroFilter(Settings.FilterMinCutoff, Settings.FilterBeta);

        static void SyncFilter(OneEuroFilter f, GazeEstimatorSettings cfg)
        {
            f.MinCutoff = cfg.FilterMinCutoff;
            f.Beta = cfg.FilterBeta;
        }
    }
}
