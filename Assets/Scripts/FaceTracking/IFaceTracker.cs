using System;

namespace VestibularGame.FaceTracking
{
    public enum FaceTrackerStatus
    {
        Idle,
        Starting,     // loading model / waiting for camera permission
        Running,
        Error,
        Unsupported   // backend not available on this platform
    }

    /// <summary>
    /// A face-tracking backend. Implementations are plain C# classes polled from the main thread once per frame.
    /// </summary>
    public interface IFaceTracker : IDisposable
    {
        FaceTrackerStatus Status { get; }
        string ErrorMessage { get; }

        void Start();
        void Stop();

        /// <summary>
        /// Copies the most recent result into <paramref name="frame"/>.
        /// Returns true only if a result newer than the previous call's was written.
        /// </summary>
        bool TryGetLatest(FaceFrame frame);

        /// <summary>Shows or hides the backend's own camera preview, if it has one.</summary>
        void SetPreviewVisible(bool visible);
    }
}
