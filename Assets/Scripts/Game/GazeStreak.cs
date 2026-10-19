using UnityEngine;

namespace VestibularGame.Game
{
    public enum StreakTransition { None, LevelUp, Reset }

    /// <summary>
    /// Continuous-looking streak split into fixed steps (default 5 s). Any look-away ends the streak.
    /// </summary>
    public sealed class GazeStreak
    {
        float stepSeconds;
        int maxLevel;

        public GazeStreak(float stepSeconds = 5f, int maxLevel = 6)
        {
            StepSeconds = stepSeconds;
            MaxLevel = maxLevel;
        }

        /// <summary>Seconds of continuous looking per level. Clamped to at least 0.01.</summary>
        public float StepSeconds
        {
            get => stepSeconds;
            set => stepSeconds = Mathf.Max(0.01f, value);
        }

        /// <summary>Highest reachable level. Clamped to at least 1.</summary>
        public int MaxLevel
        {
            get => maxLevel;
            set => maxLevel = Mathf.Max(1, value);
        }

        /// <summary>Full steps completed in the current streak.</summary>
        public int Level { get; private set; }

        /// <summary>Continuous looking time of the current streak. Keeps growing past MaxLevel.</summary>
        public float StreakSeconds { get; private set; }

        /// <summary>0..1 progress toward the next level; 1 when Level == MaxLevel.</summary>
        public float StepProgress
        {
            get
            {
                if (Level >= maxLevel) return 1f;
                return Mathf.Clamp01(StreakSeconds / stepSeconds - Level);
            }
        }

        /// <summary>
        /// Looking: adds dt to the streak and returns LevelUp if the level increased (once per call).
        /// Not looking: returns Reset if a streak was running, otherwise None.
        /// </summary>
        public StreakTransition Step(bool isLooking, float dt)
        {
            if (dt < 0f) dt = 0f;

            if (!isLooking)
            {
                if (StreakSeconds <= 0f) return StreakTransition.None;
                Reset();
                return StreakTransition.Reset;
            }

            StreakSeconds += dt;
            int level = Mathf.Min(maxLevel, Mathf.FloorToInt(StreakSeconds / stepSeconds));
            if (level > Level)
            {
                Level = level;
                return StreakTransition.LevelUp;
            }
            return StreakTransition.None;
        }

        /// <summary>Resets the streak silently.</summary>
        public void Reset()
        {
            Level = 0;
            StreakSeconds = 0f;
        }
    }
}
