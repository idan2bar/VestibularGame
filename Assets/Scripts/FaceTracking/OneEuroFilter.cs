using UnityEngine;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// One Euro filter (Casiez et al., CHI 2012) for a single float: low-pass whose cutoff rises with signal speed.
    /// Lower <see cref="MinCutoff"/> = less jitter, higher <see cref="Beta"/> = less lag during fast motion.
    /// </summary>
    public sealed class OneEuroFilter
    {
        public float MinCutoff { get; set; }
        public float Beta { get; set; }
        public float DCutoff { get; set; }

        bool _initialized;
        double _lastTime;
        float _lastRaw;
        float _lastFiltered;
        float _lastDeriv;

        public OneEuroFilter(float minCutoff, float beta, float dCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DCutoff = dCutoff;
        }

        /// <summary>Filters <paramref name="value"/> sampled at <paramref name="timestampSec"/>. Non-increasing timestamps return the previous output.</summary>
        public float Filter(float value, double timestampSec)
        {
            if (!_initialized)
            {
                _initialized = true;
                _lastTime = timestampSec;
                _lastRaw = value;
                _lastFiltered = value;
                _lastDeriv = 0f;
                return value;
            }

            float dt = (float)(timestampSec - _lastTime);
            if (dt <= 0f)
                return _lastFiltered;

            float deriv = (value - _lastRaw) / dt;
            _lastDeriv += Alpha(DCutoff, dt) * (deriv - _lastDeriv);

            float cutoff = MinCutoff + Beta * Mathf.Abs(_lastDeriv);
            _lastFiltered += Alpha(cutoff, dt) * (value - _lastFiltered);

            _lastTime = timestampSec;
            _lastRaw = value;
            return _lastFiltered;
        }

        public void Reset()
        {
            _initialized = false;
            _lastDeriv = 0f;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-4f));
            return 1f / (1f + tau / dt);
        }
    }
}
