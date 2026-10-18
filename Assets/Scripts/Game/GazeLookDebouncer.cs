namespace VestibularGame.Game
{
    public enum LookTransition { None, Started, Stopped }

    /// <summary>
    /// Turns a noisy per-frame "looking at the centre" signal into a stable looking / not-looking state.
    /// Raw looking must hold continuously for <see cref="StartDelay"/> to start; raw not-looking must hold
    /// continuously for <see cref="StopDelay"/> to stop. A delay of 0 changes the state on that step.
    /// </summary>
    public sealed class GazeLookDebouncer
    {
        float heldTime;

        public GazeLookDebouncer(float startDelay, float stopDelay)
        {
            StartDelay = startDelay;
            StopDelay = stopDelay;
        }

        public float StartDelay { get; set; }
        public float StopDelay { get; set; }
        public bool IsLooking { get; private set; }

        /// <summary>Seconds since the last <see cref="LookTransition.Started"/> while looking, else 0.</summary>
        public float LookingDuration { get; private set; }

        public LookTransition Step(bool rawLooking, float dt)
        {
            if (dt < 0f) dt = 0f;

            if (!IsLooking)
            {
                if (!rawLooking)
                {
                    heldTime = 0f;
                    return LookTransition.None;
                }

                heldTime += dt;
                if (heldTime < StartDelay) return LookTransition.None;

                IsLooking = true;
                heldTime = 0f;
                LookingDuration = 0f;
                return LookTransition.Started;
            }

            LookingDuration += dt;
            if (rawLooking)
            {
                heldTime = 0f;
                return LookTransition.None;
            }

            heldTime += dt;
            if (heldTime < StopDelay) return LookTransition.None;

            return ForceStop();
        }

        /// <summary>Stops immediately. Returns <see cref="LookTransition.Stopped"/> if it was looking.</summary>
        public LookTransition ForceStop()
        {
            bool was = IsLooking;
            Reset();
            return was ? LookTransition.Stopped : LookTransition.None;
        }

        public void Reset()
        {
            IsLooking = false;
            heldTime = 0f;
            LookingDuration = 0f;
        }
    }
}
