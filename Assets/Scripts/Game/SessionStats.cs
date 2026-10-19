namespace VestibularGame.Game
{
    /// <summary>Summary of one play session.</summary>
    public readonly struct SessionStats
    {
        /// <summary>Total time spent looking during the session.</summary>
        public readonly float LookingSeconds;

        /// <summary>Longest continuous streak.</summary>
        public readonly float BestStreakSeconds;

        /// <summary>Highest level reached in any streak.</summary>
        public readonly int BestLevel;

        /// <summary>Total level-ups over the session (each level gained counts).</summary>
        public readonly int LevelsReached;

        public SessionStats(float lookingSeconds, float bestStreakSeconds, int bestLevel, int levelsReached)
        {
            LookingSeconds = lookingSeconds;
            BestStreakSeconds = bestStreakSeconds;
            BestLevel = bestLevel;
            LevelsReached = levelsReached;
        }
    }
}
