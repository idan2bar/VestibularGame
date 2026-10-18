namespace VestibularGame.Game
{
    /// <summary>The stages of the game flow, in the order they run.</summary>
    public enum GamePhase
    {
        /// <summary>The user looks at the centre point while the centre calibration runs.</summary>
        CentreCalibration,
        /// <summary>The centre calibration just finished and its result is shown briefly.</summary>
        CentreResult,
        /// <summary>The user keeps looking at the centre while turning and nodding for the head-movement calibration.</summary>
        HeadCalibration,
        /// <summary>The head calibration just finished; its success or failure is shown briefly.</summary>
        HeadResult,
        /// <summary>Calibration is done and gameplay runs; the centre gaze detector is active.</summary>
        Playing,
    }
}
