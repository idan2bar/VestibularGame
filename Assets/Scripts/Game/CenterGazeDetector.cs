using System;
using UnityEngine;
using UnityEngine.Events;
using VestibularGame.FaceTracking;

namespace VestibularGame.Game
{
    /// <summary>
    /// The gameplay API for "is the user looking at the centre point?". Debounces the per-frame
    /// <see cref="GazeSample"/> from the <see cref="FaceTrackingController"/> and raises
    /// <see cref="LookStarted"/> / <see cref="LookStopped"/>. A blink while already looking does not count as looking away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CenterGazeDetector : MonoBehaviour
    {
        [SerializeField] FaceTrackingController controller;
        [Tooltip("Seconds the raw 'looking at centre' signal must hold before LookStarted fires.")]
        [SerializeField] float lookStartDelay = 0.15f;
        [Tooltip("Seconds the raw 'not looking' signal must hold before LookStopped fires.")]
        [SerializeField] float lookStopDelay = 0.30f;
        [Tooltip("Whether detection is on at start. The flow controller enables it when the game begins playing.")]
        [SerializeField] bool startEnabled;
        [SerializeField] UnityEvent onLookStarted;
        [SerializeField] UnityEvent onLookStopped;

        readonly GazeLookDebouncer debouncer = new GazeLookDebouncer(0.15f, 0.30f);
        bool detectionEnabled;
        bool initialized;

        public bool IsLooking => debouncer.IsLooking;
        public bool DetectionEnabled => initialized ? detectionEnabled : startEnabled;

        /// <summary>Seconds since LookStarted while looking, else 0.</summary>
        public float LookingDuration => debouncer.LookingDuration;

        public event Action LookStarted;
        public event Action LookStopped;

        void Awake()
        {
            SyncDelays();
            if (!initialized)
            {
                initialized = true;
                detectionEnabled = startEnabled;
            }
            if (controller == null) controller = FindAnyObjectByType<FaceTrackingController>();
        }

        void OnValidate()
        {
            lookStartDelay = Mathf.Max(0f, lookStartDelay);
            lookStopDelay = Mathf.Max(0f, lookStopDelay);
            SyncDelays();
        }

        void SyncDelays()
        {
            debouncer.StartDelay = lookStartDelay;
            debouncer.StopDelay = lookStopDelay;
        }

        void Update()
        {
            if (!DetectionEnabled) return;
            if (controller == null)
            {
                controller = FindAnyObjectByType<FaceTrackingController>();
                if (controller == null) return;
            }

            GazeSample s = controller.LatestSample;
            bool raw = controller.TrackingFps > 0f && s.HasFace && s.IsCalibrated
                && (s.IsOnCenter || (s.IsBlinking && debouncer.IsLooking));

            Raise(debouncer.Step(raw, Time.unscaledDeltaTime));
        }

        /// <summary>Enables or disables detection. Disabling while looking fires LookStopped immediately.</summary>
        public void SetDetectionEnabled(bool enabled)
        {
            initialized = true;
            detectionEnabled = enabled;
            if (!enabled) Raise(debouncer.ForceStop());
        }

        void Raise(LookTransition t)
        {
            if (t == LookTransition.Started)
            {
                LookStarted?.Invoke();
                onLookStarted?.Invoke();
            }
            else if (t == LookTransition.Stopped)
            {
                LookStopped?.Invoke();
                onLookStopped?.Invoke();
            }
        }

        void OnDestroy()
        {
            LookStarted = null;
            LookStopped = null;
            onLookStarted?.RemoveAllListeners();
            onLookStopped?.RemoveAllListeners();
        }
    }
}
