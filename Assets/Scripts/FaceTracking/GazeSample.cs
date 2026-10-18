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

        // Eye-in-head rotation, averaged over both eyes (approximate, uncalibrated).
        public float EyeYaw;
        public float EyePitch;

        // Combined gaze relative to the calibrated screen center (zero when uncalibrated baseline is unknown).
        public float GazeYaw;
        public float GazePitch;

        /// <summary>Angular distance of the gaze from the calibrated center.</summary>
        public float DeviationDeg;

        /// <summary>Hysteresis-filtered "looking at the center" state.</summary>
        public bool IsOnCenter;

        public double TimestampMs;
    }
}
