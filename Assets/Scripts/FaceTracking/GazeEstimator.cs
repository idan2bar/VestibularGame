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
        public float EyeYawGain = 1f;         // multiplier on eye-in-head yaw
        public float EyePitchGain = 1f;       // multiplier on eye-in-head pitch
        public float EyeballCenterDepth = EyeModel.DefaultEyeballCenterDepth; // eye widths behind the corner midpoint
        public float IrisSphereRadius = EyeModel.DefaultIrisSphereRadius;     // eye widths, eyeball centre to iris centre
        public float HeadCalibrationDurationSec = 12f;
        public float HeadCalibrationMinYawRangeDeg = 16f;   // head yaw max - min required
        public float HeadCalibrationMinPitchRangeDeg = 10f; // head pitch max - min required
        public float HeadCalibrationRidge = 0.001f;         // ridge strength per kept sample on the non-intercept terms (kept small: larger values bias slopes)
    }

    public enum HeadCalibrationState { None, Collecting, Succeeded, InsufficientMotion, Failed }

    /// <summary>
    /// Turns <see cref="FaceFrame"/>s into a <see cref="GazeSample"/>: a 3D eyeball model gives the averaged gaze ray (per-axis gains on the
    /// eye-in-head angles), filtered as camera angles. Gaze is relative to the centre calibration, or, once a head-movement calibration has
    /// succeeded, to a quadratic model of the gaze error over head yaw/pitch fitted while the user fixates the centre and moves their head.
    /// Sign conventions follow <see cref="GazeSample"/>. Process is allocation-free.
    /// </summary>
    public sealed class GazeEstimator
    {
        // Largest frame gap that still counts toward calibration time.
        const double MaxCalibrationStepSec = 0.25;
        const int HeadCapacity = 1200;
        const int HeadMinSamples = 60;
        const int HeadTerms = 6;
        const int HeadCols = HeadTerms + 2; // normal-equation columns + yaw and pitch right-hand sides
        const float HeadScaleDeg = 20f;
        const int HeadOutlierRefits = 3;
        const float HeadOutlierMinDeg = 2f;         // residual limit never goes below this
        const float HeadOutlierMedianFactor = 3f;   // limit = factor * median residual norm (median of a 2D Gaussian norm ~ 1.18 sigma, so ~3.5 sigma)

        readonly OneEuroFilter _headYawF, _headPitchF, _headRollF, _eyeYawF, _eyePitchF, _gazeYawF, _gazePitchF;

        GazeSample _latest;
        bool _calibrating, _isCalibrated, _onCenter;
        float _baseYaw, _basePitch;
        float _calSumX, _calSumY;
        int _calSamples;
        double _calElapsed;
        bool _prevValid;
        double _lastTime;
        float _eyeYaw, _eyePitch, _gazeYaw, _gazePitch;

        // Head-movement calibration.
        HeadCalibrationState _hcState, _hcPrevState;
        bool _headModel, _hcPrevValid, _hcSeen;
        double _hcElapsed;
        int _hcCount, _hcWrite;
        float _hcMinYaw, _hcMaxYaw, _hcMinPitch, _hcMaxPitch, _hcRms;
        float _modMinYaw, _modMaxYaw, _modMinPitch, _modMaxPitch, _modMeanYaw, _modMeanPitch;
        readonly float[] _hcHeadYaw = new float[HeadCapacity], _hcHeadPitch = new float[HeadCapacity];
        readonly float[] _hcGazeYaw = new float[HeadCapacity], _hcGazePitch = new float[HeadCapacity];
        readonly float[] _hcX = new float[HeadCapacity], _hcY = new float[HeadCapacity];
        readonly float[] _hcSort = new float[HeadCapacity]; // residual-norm scratch for the median
        readonly bool[] _hcKeep = new bool[HeadCapacity];
        readonly float[] _coefYaw = new float[HeadTerms], _coefPitch = new float[HeadTerms];
        readonly float[] _fitYaw = new float[HeadTerms], _fitPitch = new float[HeadTerms];
        readonly double[] _norm = new double[HeadTerms * HeadCols];
        readonly double[] _feat = new double[HeadTerms];

        public GazeEstimator(GazeEstimatorSettings settings = null)
        {
            Settings = settings ?? new GazeEstimatorSettings();
            _headYawF = NewFilter();
            _headPitchF = NewFilter();
            _headRollF = NewFilter();
            _eyeYawF = NewFilter();
            _eyePitchF = NewFilter();
            _gazeYawF = NewFilter();
            _gazePitchF = NewFilter();
        }

        public GazeEstimatorSettings Settings { get; }
        public GazeSample Latest => _latest;
        public bool IsCalibrating => _calibrating;
        public bool IsCalibrated => _isCalibrated;

        public HeadCalibrationState HeadCalibrationState => _hcState;
        public bool IsHeadCalibrating => _hcState == HeadCalibrationState.Collecting;
        public bool IsHeadCalibrated => _headModel;
        public float HeadCalibrationRmsDeg => _hcRms;
        public Vector2 HeadCalibrationYawRange => _hcSeen ? new Vector2(_hcMinYaw, _hcMaxYaw) : Vector2.zero;
        public Vector2 HeadCalibrationPitchRange => _hcSeen ? new Vector2(_hcMinPitch, _hcMaxPitch) : Vector2.zero;

        public float CalibrationProgress
        {
            get
            {
                if (!_calibrating) return _isCalibrated ? 1f : 0f;
                float d = Settings.CalibrationDurationSec;
                return d <= 0f ? 0f : Mathf.Clamp01((float)(_calElapsed / d));
            }
        }

        public float HeadCalibrationProgress
        {
            get
            {
                if (_hcState != HeadCalibrationState.Collecting) return _headModel ? 1f : 0f;
                float d = Settings.HeadCalibrationDurationSec;
                return d <= 0f ? 0f : Mathf.Clamp01((float)(_hcElapsed / d));
            }
        }

        /// <summary>Starts averaging the gaze over <see cref="GazeEstimatorSettings.CalibrationDurationSec"/>. An earlier calibration stays active until the new one completes.</summary>
        public void BeginCalibration()
        {
            _calibrating = true;
            _calElapsed = 0;
            _calSamples = 0;
            _calSumX = 0f;
            _calSumY = 0f;
            _prevValid = false;
        }

        public void CancelCalibration()
        {
            _calibrating = false;
        }

        /// <summary>
        /// Starts collecting head pose and raw gaze for <see cref="GazeEstimatorSettings.HeadCalibrationDurationSec"/> while the user fixates the centre
        /// and moves their head. An earlier head model stays active until a new fit succeeds.
        /// </summary>
        public void BeginHeadCalibration()
        {
            if (_hcState != HeadCalibrationState.Collecting) _hcPrevState = _hcState;
            _hcState = HeadCalibrationState.Collecting;
            _hcElapsed = 0;
            _hcCount = 0;
            _hcWrite = 0;
            _hcSeen = false;
            _hcPrevValid = false;
            _hcMinYaw = _hcMinPitch = float.MaxValue;
            _hcMaxYaw = _hcMaxPitch = float.MinValue;
        }

        public void CancelHeadCalibration()
        {
            if (_hcState == HeadCalibrationState.Collecting) _hcState = _hcPrevState;
        }

        public void ResetCalibration()
        {
            _calibrating = false;
            _isCalibrated = false;
            _baseYaw = _basePitch = 0f;
            _onCenter = false;
            _headModel = false;
            _hcState = _hcPrevState = HeadCalibrationState.None;
            _hcCount = _hcWrite = 0;
            _hcSeen = false;
            _hcRms = 0f;
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
                s.IsCalibrated = _isCalibrated || _headModel;
                _onCenter = false;
                _prevValid = false;
                _hcPrevValid = false;
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
            SyncFilter(_gazeYawF, cfg);
            SyncFilter(_gazePitchF, cfg);

            HeadPoseMath.FromMatrix(frame.FacialTransform, out float hy, out float hp, out float hr);
            hy = _headYawF.Filter(hy, t);
            hp = _headPitchF.Filter(hp, t);
            hr = _headRollF.Filter(hr, t);

            float blink = Mathf.Max(frame.Blendshapes[BlendshapeIndex.EyeBlinkLeft], frame.Blendshapes[BlendshapeIndex.EyeBlinkRight]);
            bool blinking = blink > cfg.BlinkThreshold;

            bool eyesValid = false;
            if (!blinking && TryEstimateEyes(frame, cfg, out float ey, out float ep, out float rgy, out float rgp))
            {
                _eyeYaw = _eyeYawF.Filter(ey, t);
                _eyePitch = _eyePitchF.Filter(ep, t);
                _gazeYaw = _gazeYawF.Filter(rgy, t);
                _gazePitch = _gazePitchF.Filter(rgp, t);
                eyesValid = true;
            }
            bool ok = !blinking && eyesValid;
            double step = System.Math.Min(System.Math.Max(t - _lastTime, 0.0), MaxCalibrationStepSec);

            if (_calibrating && ok)
            {
                if (_prevValid) _calElapsed += step;
                _calSumX += _gazeYaw;
                _calSumY += _gazePitch;
                _calSamples++;

                if (_calElapsed >= cfg.CalibrationDurationSec)
                {
                    _baseYaw = _calSumX / _calSamples;
                    _basePitch = _calSumY / _calSamples;
                    _isCalibrated = true;
                    _calibrating = false;
                }
            }

            if (_hcState == HeadCalibrationState.Collecting && ok)
            {
                if (_hcPrevValid) _hcElapsed += step;
                _hcHeadYaw[_hcWrite] = hy;
                _hcHeadPitch[_hcWrite] = hp;
                _hcGazeYaw[_hcWrite] = _gazeYaw;
                _hcGazePitch[_hcWrite] = _gazePitch;
                if (++_hcWrite >= HeadCapacity) _hcWrite = 0;
                if (_hcCount < HeadCapacity) _hcCount++;
                _hcSeen = true;
                _hcMinYaw = Mathf.Min(_hcMinYaw, hy);
                _hcMaxYaw = Mathf.Max(_hcMaxYaw, hy);
                _hcMinPitch = Mathf.Min(_hcMinPitch, hp);
                _hcMaxPitch = Mathf.Max(_hcMaxPitch, hp);

                if (_hcElapsed >= cfg.HeadCalibrationDurationSec)
                    FinishHeadCalibration(cfg);
            }
            _prevValid = _hcPrevValid = ok;
            _lastTime = t;

            float gy, gp;
            if (_headModel)
            {
                Predict(Mathf.Clamp(hy, _modMinYaw, _modMaxYaw), Mathf.Clamp(hp, _modMinPitch, _modMaxPitch), out float py, out float pp);
                gy = _gazeYaw - py;
                gp = _gazePitch - pp;
            }
            else
            {
                gy = _gazeYaw - _baseYaw;
                gp = _gazePitch - _basePitch;
            }
            float dev = Mathf.Sqrt(gy * gy + gp * gp);

            bool calibrated = _isCalibrated || _headModel;
            if (!calibrated)
                _onCenter = false;
            else if (!blinking)
            {
                if (_onCenter) { if (dev > cfg.OnCenterExitDeg) _onCenter = false; }
                else if (dev < cfg.OnCenterEnterDeg) _onCenter = true;
            }

            s.HasFace = true;
            s.IsBlinking = blinking;
            s.IsCalibrated = calibrated;
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

        /// <summary>Predicted raw gaze (deg) of the active head model at a head pose, which must already be clamped to the model range.</summary>
        void Predict(float headYaw, float headPitch, out float yaw, out float pitch)
        {
            float x = (headYaw - _modMeanYaw) / HeadScaleDeg, y = (headPitch - _modMeanPitch) / HeadScaleDeg;
            yaw = EvalFit(_coefYaw, x, y);
            pitch = EvalFit(_coefPitch, x, y);
        }

        static float EvalFit(float[] c, float x, float y) =>
            c[0] + c[1] * x + c[2] * y + c[3] * x * x + c[4] * y * y + c[5] * x * y;

        void FinishHeadCalibration(GazeEstimatorSettings cfg)
        {
            int n = _hcCount;
            if (_hcMaxYaw - _hcMinYaw < cfg.HeadCalibrationMinYawRangeDeg || _hcMaxPitch - _hcMinPitch < cfg.HeadCalibrationMinPitchRangeDeg)
            {
                _hcState = HeadCalibrationState.InsufficientMotion;
                return;
            }
            if (n < HeadMinSamples) { _hcState = HeadCalibrationState.Failed; return; }

            float mY = 0f, mP = 0f;
            for (int i = 0; i < n; i++) { mY += _hcHeadYaw[i]; mP += _hcHeadPitch[i]; }
            mY /= n;
            mP /= n;
            for (int i = 0; i < n; i++)
            {
                _hcX[i] = (_hcHeadYaw[i] - mY) / HeadScaleDeg;
                _hcY[i] = (_hcHeadPitch[i] - mP) / HeadScaleDeg;
                _hcKeep[i] = true;
            }

            if (!FitRobust(n, cfg.HeadCalibrationRidge, out int kept)) { _hcState = HeadCalibrationState.Failed; return; }
            float rms = FitRms(n, kept);

            System.Array.Copy(_fitYaw, _coefYaw, HeadTerms);
            System.Array.Copy(_fitPitch, _coefPitch, HeadTerms);
            _modMeanYaw = mY;
            _modMeanPitch = mP;
            _modMinYaw = _hcMinYaw;
            _modMaxYaw = _hcMaxYaw;
            _modMinPitch = _hcMinPitch;
            _modMaxPitch = _hcMaxPitch;
            _hcRms = rms;
            _headModel = true;
            _hcState = HeadCalibrationState.Succeeded;
        }

        /// <summary>
        /// Fits all samples, then up to <see cref="HeadOutlierRefits"/> times re-selects the samples whose 2D residual is within a median-based
        /// limit (judged over all n, so earlier drops can return) and refits. Returns false if too few samples remain or a fit is singular.
        /// </summary>
        bool FitRobust(int n, float ridge, out int kept)
        {
            kept = n;
            for (int i = 0; i < n; i++) _hcKeep[i] = true;
            if (!SolveFit(n, kept, ridge)) return false;

            for (int iter = 0; iter < HeadOutlierRefits; iter++)
            {
                int m = 0;
                for (int i = 0; i < n; i++)
                    if (_hcKeep[i]) _hcSort[m++] = FitResidualNorm(i);
                System.Array.Sort(_hcSort, 0, m);
                float limit = Mathf.Max(HeadOutlierMinDeg, HeadOutlierMedianFactor * _hcSort[m / 2]);

                int newKept = 0;
                bool changed = false;
                for (int i = 0; i < n; i++)
                {
                    bool keep = FitResidualNorm(i) <= limit;
                    if (keep != _hcKeep[i]) changed = true;
                    _hcKeep[i] = keep;
                    if (keep) newKept++;
                }
                kept = newKept;
                if (!changed) break;
                if (kept < HeadMinSamples || !SolveFit(n, kept, ridge)) return false;
            }
            return true;
        }

        void FitResidual(int i, out float ry, out float rp)
        {
            ry = _hcGazeYaw[i] - EvalFit(_fitYaw, _hcX[i], _hcY[i]);
            rp = _hcGazePitch[i] - EvalFit(_fitPitch, _hcX[i], _hcY[i]);
        }

        float FitResidualNorm(int i)
        {
            FitResidual(i, out float ry, out float rp);
            return Mathf.Sqrt(ry * ry + rp * rp);
        }

        float FitRms(int n, int kept)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                if (!_hcKeep[i]) continue;
                FitResidual(i, out float ry, out float rp);
                sum += ry * ry + rp * rp;
            }
            return (float)System.Math.Sqrt(sum / System.Math.Max(kept, 1));
        }

        /// <summary>Ridge least squares of gaze yaw and pitch on [1, x, y, x², y², xy] over the kept samples, into <see cref="_fitYaw"/>/<see cref="_fitPitch"/>.</summary>
        bool SolveFit(int n, int kept, float ridge)
        {
            var a = _norm;
            var f = _feat;
            System.Array.Clear(a, 0, a.Length);
            for (int i = 0; i < n; i++)
            {
                if (!_hcKeep[i]) continue;
                double x = _hcX[i], y = _hcY[i];
                f[0] = 1; f[1] = x; f[2] = y; f[3] = x * x; f[4] = y * y; f[5] = x * y;
                for (int r = 0; r < HeadTerms; r++)
                {
                    int row = r * HeadCols;
                    for (int c = 0; c < HeadTerms; c++) a[row + c] += f[r] * f[c];
                    a[row + HeadTerms] += f[r] * _hcGazeYaw[i];
                    a[row + HeadTerms + 1] += f[r] * _hcGazePitch[i];
                }
            }
            for (int r = 1; r < HeadTerms; r++) a[r * HeadCols + r] += ridge * kept;

            for (int col = 0; col < HeadTerms; col++)
            {
                int pivot = col;
                double best = System.Math.Abs(a[col * HeadCols + col]);
                for (int r = col + 1; r < HeadTerms; r++)
                {
                    double v = System.Math.Abs(a[r * HeadCols + col]);
                    if (v > best) { best = v; pivot = r; }
                }
                if (best < 1e-12) return false;
                if (pivot != col)
                    for (int c = 0; c < HeadCols; c++)
                    {
                        double tmp = a[col * HeadCols + c];
                        a[col * HeadCols + c] = a[pivot * HeadCols + c];
                        a[pivot * HeadCols + c] = tmp;
                    }
                double inv = 1.0 / a[col * HeadCols + col];
                for (int r = col + 1; r < HeadTerms; r++)
                {
                    double k = a[r * HeadCols + col] * inv;
                    if (k == 0) continue;
                    for (int c = col; c < HeadCols; c++) a[r * HeadCols + c] -= k * a[col * HeadCols + c];
                }
            }
            for (int r = HeadTerms - 1; r >= 0; r--)
            {
                double sy = a[r * HeadCols + HeadTerms], sp = a[r * HeadCols + HeadTerms + 1];
                for (int c = r + 1; c < HeadTerms; c++)
                {
                    sy -= a[r * HeadCols + c] * _fitYaw[c];
                    sp -= a[r * HeadCols + c] * _fitPitch[c];
                }
                double d = a[r * HeadCols + r];
                _fitYaw[r] = (float)(sy / d);
                _fitPitch[r] = (float)(sp / d);
            }
            for (int r = 0; r < HeadTerms; r++)
                if (float.IsNaN(_fitYaw[r]) || float.IsNaN(_fitPitch[r]) || float.IsInfinity(_fitYaw[r]) || float.IsInfinity(_fitPitch[r])) return false;
            return true;
        }

        /// <summary>
        /// Gain-applied eye-in-head angles and gaze angles relative to the camera axis, in degrees (+yaw = user's right, +pitch = up),
        /// from the averaged 3D eyeball-to-iris rays of both eyes.
        /// </summary>
        static bool TryEstimateEyes(FaceFrame f, GazeEstimatorSettings cfg, out float eyeYaw, out float eyePitch, out float gazeYaw, out float gazePitch)
        {
            eyeYaw = eyePitch = gazeYaw = gazePitch = 0f;

            float W = f.ImageWidth > 0 && f.ImageHeight > 0 ? f.ImageWidth : 640f;
            float H = f.ImageWidth > 0 && f.ImageHeight > 0 ? f.ImageHeight : 480f;
            EyeModel.HeadAxes(f.FacialTransform, out Vector3 right, out Vector3 up, out Vector3 fwd);

            var lm = f.Landmarks;
            Vector3 a1 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.EyeAOuter], W, H), a2 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.EyeAInner], W, H);
            Vector3 b1 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.EyeBInner], W, H), b2 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.EyeBOuter], W, H);
            Vector3 i1 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.IrisCenter1], W, H), i2 = EyeModel.ToImageSpace(lm[FaceLandmarkIndex.IrisCenter2], W, H);

            // Pair iris centers to eyes by proximity to the corner midpoints.
            Vector3 midA = (a1 + a2) * 0.5f, midB = (b1 + b2) * 0.5f;
            float straight = Sq2D(i1 - midA) + Sq2D(i2 - midB);
            float swapped = Sq2D(i1 - midB) + Sq2D(i2 - midA);
            Vector3 irisA = straight <= swapped ? i1 : i2;
            Vector3 irisB = straight <= swapped ? i2 : i1;

            // Eye width in pixels: 2D corner distance corrected for foreshortening by the head turn (landmark z is not used).
            float cosTurn = Mathf.Max(Mathf.Sqrt(right.x * right.x + right.y * right.y), 0.3f);
            float dA = Mathf.Sqrt(Sq2D(a2 - a1)), dB = Mathf.Sqrt(Sq2D(b2 - b1));
            bool okA = dA >= 1e-3f, okB = dB >= 1e-3f;
            if (!okA && !okB) return false;
            float w = ((okA ? dA : 0f) + (okB ? dB : 0f)) / ((okA ? 1 : 0) + (okB ? 1 : 0)) / cosTurn;

            Vector3 g = Vector3.zero;
            if (okA) g += EyeRay(midA, irisA, fwd, w, cfg);
            if (okB) g += EyeRay(midB, irisB, fwd, w, cfg);
            g = g.normalized;

            EyeModel.HeadAnglesFromDirection(g, right, up, fwd, out eyeYaw, out eyePitch);
            eyeYaw *= cfg.EyeYawGain;
            eyePitch *= cfg.EyePitchGain;
            if (cfg.EyeYawGain != 1f || cfg.EyePitchGain != 1f)
                g = EyeModel.DirectionFromHeadAngles(eyeYaw, eyePitch, right, up, fwd);

            EyeModel.CameraAnglesFromDirection(g, out gazeYaw, out gazePitch);
            return true;
        }

        /// <summary>Unit vector from the eyeball centre (behind the corner midpoint) to the iris lifted onto the eyeball sphere using its 2D position only.</summary>
        static Vector3 EyeRay(Vector3 mid, Vector3 iris, Vector3 fwd, float w, GazeEstimatorSettings cfg)
        {
            Vector3 c = mid - fwd * (cfg.EyeballCenterDepth * w);
            float r = cfg.IrisSphereRadius * w;
            float dx = iris.x - c.x, dy = iris.y - c.y;
            float rho2 = dx * dx + dy * dy;
            if (rho2 > r * r)
            {
                float k = r / Mathf.Sqrt(rho2);
                dx *= k;
                dy *= k;
                rho2 = r * r;
            }
            float dz = -Mathf.Sqrt(Mathf.Max(r * r - rho2, 0f));
            return new Vector3(dx, dy, dz).normalized;
        }

        static float Sq2D(Vector3 v) => v.x * v.x + v.y * v.y;

        OneEuroFilter NewFilter() => new OneEuroFilter(Settings.FilterMinCutoff, Settings.FilterBeta);

        static void SyncFilter(OneEuroFilter f, GazeEstimatorSettings cfg)
        {
            f.MinCutoff = cfg.FilterMinCutoff;
            f.Beta = cfg.FilterBeta;
        }
    }
}
