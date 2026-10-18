using VestibularGame.FaceTracking;

namespace VestibularGame.Game
{
    /// <summary>Outcome of a finished centre or head-movement calibration.</summary>
    public readonly struct CalibrationResult
    {
        /// <summary>False for a centre calibration result, true for a head calibration result.</summary>
        public readonly bool IsHead;
        public readonly bool Success;
        /// <summary>Final head calibration state; <see cref="HeadCalibrationState.None"/> for centre results.</summary>
        public readonly HeadCalibrationState HeadState;
        /// <summary>Residual RMS of the head fit in degrees (0 for centre results).</summary>
        public readonly float RmsDeg;

        public CalibrationResult(bool isHead, bool success, HeadCalibrationState headState, float rmsDeg)
        {
            IsHead = isHead;
            Success = success;
            HeadState = headState;
            RmsDeg = rmsDeg;
        }
    }
}
