using System;
using UnityEngine;
using UnityEngine.Events;

namespace VestibularGame.Game
{
    /// <summary>
    /// A timed play session. Starts when the flow enters <see cref="GamePhase.Playing"/>, tracks the continuous
    /// centre-gaze streak through a <see cref="GazeStreak"/> and ends after <c>sessionSeconds</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour
    {
        [Tooltip("Game flow controller. Found in the scene if left empty; without one the session starts in Start().")]
        [SerializeField] GameFlowController flow;
        [Tooltip("Centre gaze detector. Found in the scene if left empty.")]
        [SerializeField] CenterGazeDetector detector;
        [Tooltip("Length of a session in seconds.")]
        [SerializeField] float sessionSeconds = 120f;
        [Tooltip("Seconds of continuous looking per streak level.")]
        [SerializeField] float stepSeconds = 5f;
        [Tooltip("Highest streak level.")]
        [SerializeField] int maxLevel = 6;
        [SerializeField] UnityEvent onSessionStarted;
        [SerializeField] UnityEvent<int> onLevelReached;
        [SerializeField] UnityEvent onStreakReset;
        [SerializeField] UnityEvent onLookAway;
        [SerializeField] UnityEvent onLookBack;
        [SerializeField] UnityEvent onSessionEnded;

        readonly GazeStreak streak = new GazeStreak();
        bool running;
        bool finished;
        float elapsed;
        float lookingSeconds;
        float bestStreakSeconds;
        int bestLevel;
        int levelsReached;

        /// <summary>Raised when a session starts.</summary>
        public event Action SessionStarted;
        /// <summary>Raised with the new level (1..MaxLevel) when the streak levels up.</summary>
        public event Action<int> LevelReached;
        /// <summary>Raised when a streak with time above zero ends by looking away.</summary>
        public event Action StreakReset;
        /// <summary>Raised when the detector stops looking while the session runs.</summary>
        public event Action LookAway;
        /// <summary>Raised when the detector starts looking while the session runs.</summary>
        public event Action LookBack;
        /// <summary>Raised with the final stats when the session ends by time.</summary>
        public event Action<SessionStats> SessionEnded;

        public bool IsRunning => running;
        public bool IsFinished => finished;
        public float SessionSeconds => sessionSeconds;
        public float RemainingSeconds => Mathf.Max(0f, sessionSeconds - elapsed);
        public int Level => streak.Level;
        public int MaxLevel => streak.MaxLevel;
        public float StepProgress => streak.StepProgress;
        public SessionStats Stats => new SessionStats(lookingSeconds, bestStreakSeconds, bestLevel, levelsReached);

        void Awake()
        {
            if (flow == null) flow = FindAnyObjectByType<GameFlowController>();
            if (detector == null) detector = FindAnyObjectByType<CenterGazeDetector>();
            SyncStreak();
        }

        void OnValidate()
        {
            sessionSeconds = Mathf.Max(0.1f, sessionSeconds);
            stepSeconds = Mathf.Max(0.01f, stepSeconds);
            maxLevel = Mathf.Max(1, maxLevel);
            SyncStreak();
        }

        void SyncStreak()
        {
            streak.StepSeconds = stepSeconds;
            streak.MaxLevel = maxLevel;
        }

        void OnEnable()
        {
            if (flow != null)
            {
                flow.PhaseChanged += OnPhaseChanged;
                if (flow.Phase == GamePhase.Playing) Restart();
            }
            if (detector != null)
            {
                detector.LookStarted += OnLookStarted;
                detector.LookStopped += OnLookStopped;
            }
        }

        void Start()
        {
            if (flow == null) Restart();
        }

        void OnDisable()
        {
            if (flow != null) flow.PhaseChanged -= OnPhaseChanged;
            if (detector != null)
            {
                detector.LookStarted -= OnLookStarted;
                detector.LookStopped -= OnLookStopped;
            }
            running = false;
        }

        void OnDestroy()
        {
            SessionStarted = null;
            LevelReached = null;
            StreakReset = null;
            LookAway = null;
            LookBack = null;
            SessionEnded = null;
            onSessionStarted?.RemoveAllListeners();
            onLevelReached?.RemoveAllListeners();
            onStreakReset?.RemoveAllListeners();
            onLookAway?.RemoveAllListeners();
            onLookBack?.RemoveAllListeners();
            onSessionEnded?.RemoveAllListeners();
        }

        /// <summary>Starts a new session now without recalibrating.</summary>
        public void Restart()
        {
            SyncStreak();
            streak.Reset();
            elapsed = 0f;
            lookingSeconds = 0f;
            bestStreakSeconds = 0f;
            bestLevel = 0;
            levelsReached = 0;
            finished = false;
            if (detector != null) detector.SetDetectionEnabled(true);
            running = true;
            SessionStarted?.Invoke();
            onSessionStarted?.Invoke();
        }

        void OnPhaseChanged(GamePhase phase)
        {
            if (phase == GamePhase.Playing)
            {
                Restart();
                return;
            }
            running = false;
            finished = false;
            streak.Reset();
        }

        void OnLookStarted()
        {
            if (!running) return;
            LookBack?.Invoke();
            onLookBack?.Invoke();
        }

        void OnLookStopped()
        {
            if (!running) return;
            LookAway?.Invoke();
            onLookAway?.Invoke();
        }

        void Update()
        {
            if (!running) return;

            float dt = Time.unscaledDeltaTime;
            elapsed += dt;

            bool looking = detector != null && detector.IsLooking;
            if (looking) lookingSeconds += dt;

            int previousLevel = streak.Level;
            StreakTransition t = streak.Step(looking, dt);
            if (streak.StreakSeconds > bestStreakSeconds) bestStreakSeconds = streak.StreakSeconds;
            if (streak.Level > bestLevel) bestLevel = streak.Level;

            if (t == StreakTransition.LevelUp)
            {
                levelsReached += streak.Level - previousLevel;
                LevelReached?.Invoke(streak.Level);
                onLevelReached?.Invoke(streak.Level);
            }
            else if (t == StreakTransition.Reset)
            {
                StreakReset?.Invoke();
                onStreakReset?.Invoke();
            }

            if (elapsed >= sessionSeconds) EndSession();
        }

        void EndSession()
        {
            running = false;
            finished = true;
            if (detector != null) detector.SetDetectionEnabled(false);
            SessionEnded?.Invoke(Stats);
            onSessionEnded?.Invoke();
        }
    }
}
