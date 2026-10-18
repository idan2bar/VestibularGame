namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Output of <see cref="GazeEstimator"/>. Angles are in degrees.
    /// Sign convention, from the user's point of view facing the screen:
    /// +yaw = toward the screen's right edge (the user's right), +pitch = up, +roll = head tilted toward the user's right shoulder.
    /// </summary>
    public struct GazeSample
    {
        public bool HasFace;
        public bool IsBlinking;
        public bool IsCalibrated;

        // Raw (uncalibrated) head pose.
        public float HeadYaw;
        public float HeadPitch;
        public float HeadRoll;

        // Eye-in-head rotation, averaged over both eyes (approximate, uncalibrated, with the estimator's yaw/pitch gains applied).
        public float EyeYaw;
        public float EyePitch;

        // Gaze angles relative to the calibrated centre.
        public float GazeYaw;
        public float GazePitch;

        /// <summary>Angular distance of the gaze from the calibrated centre.</summary>
        public float DeviationDeg;

        /// <summary>Hysteresis-filtered "looking at the center" state.</summary>
        public bool IsOnCenter;

        public double TimestampMs;
    }
}
