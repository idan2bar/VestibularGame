using System.Runtime.InteropServices;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// <see cref="IFaceTracker"/> backed by MediaPipe Face Landmarker running in the browser
    /// (WebGL template + FaceTrackerBridge.jslib). On every other platform it reports Unsupported.
    /// </summary>
    public sealed class WebGLFaceTracker : IFaceTracker
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FaceTracker_Start(int width, int height);
        [DllImport("__Internal")] private static extern void FaceTracker_Stop();
        [DllImport("__Internal")] private static extern int FaceTracker_GetStatus();
        [DllImport("__Internal")] private static extern void FaceTracker_SetPreviewVisible(int visible);
        [DllImport("__Internal")] private static extern string FaceTracker_GetError();
        [DllImport("__Internal")] private static extern int FaceTracker_CopyLatest(float[] dst, int length);

        private readonly float[] _buffer = new float[FaceBufferLayout.Length];
        private int _lastFrameId = -1;
        private readonly int _requestedWidth;
        private readonly int _requestedHeight;
#endif

        /// <param name="requestedWidth">Camera width requested from the browser (ideal, not guaranteed).</param>
        /// <param name="requestedHeight">Camera height requested from the browser (ideal, not guaranteed).</param>
        public WebGLFaceTracker(int requestedWidth = 1280, int requestedHeight = 720)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _requestedWidth = requestedWidth;
            _requestedHeight = requestedHeight;
#endif
        }

        public FaceTrackerStatus Status
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                int s = FaceTracker_GetStatus();
                return s >= 0 && s <= (int)FaceTrackerStatus.Unsupported
                    ? (FaceTrackerStatus)s
                    : FaceTrackerStatus.Unsupported;
#else
                return FaceTrackerStatus.Unsupported;
#endif
            }
        }

        public string ErrorMessage
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var status = Status;
                if (status == FaceTrackerStatus.Error || status == FaceTrackerStatus.Unsupported)
                    return FaceTracker_GetError() ?? string.Empty;
#endif
                return string.Empty;
            }
        }

        public void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _lastFrameId = -1;
            FaceTracker_Start(_requestedWidth, _requestedHeight);
#endif
        }

        public void Stop()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FaceTracker_Stop();
#endif
        }

        public bool TryGetLatest(FaceFrame frame)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int frameId = FaceTracker_CopyLatest(_buffer, _buffer.Length);
            if (frameId == -1 || frameId == _lastFrameId)
                return false;

            _lastFrameId = frameId;
            frame.ReadFrom(_buffer);
            return true;
#else
            return false;
#endif
        }

        public void SetPreviewVisible(bool visible)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FaceTracker_SetPreviewVisible(visible ? 1 : 0);
#endif
        }

        public void Dispose() => Stop();
    }
}
