using UnityEngine;
using UnityEngine.InputSystem;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Mouse- and keyboard-driven stand-in for the webcam face tracker, for testing gaze features in the Editor.
    /// Controls:
    /// <list type="bullet">
    /// <item>Mouse position, relative to screen center, drives the eyes (looking right or up moves the irises the same way).</item>
    /// <item>Hold right mouse button or Left Shift: the mouse drives the head instead (yaw up to ±30°, pitch up to ±20°) and the eyes stay centered.</item>
    /// <item>Hold Space: both eyes blink.</item>
    /// <item>H: toggles face detected / face lost.</item>
    /// </list>
    /// </summary>
    public sealed class MockFaceTracker : IFaceTracker
    {
        const float HeadYawMaxDeg = 30f;
        const float HeadPitchMaxDeg = 20f;
        const float FaceShiftX = 0.08f;
        const float FaceShiftY = 0.08f;
        const float EyeWidth = 0.06f;
        const float EyeSpacingX = 0.08f;
        const float EyeOffsetY = -0.05f;
        const float LidOffsetY = 0.012f;
        const float IrisRangeX = 0.25f;
        const float IrisRangeY = 0.15f;
        const float IrisRadius = 0.2f * EyeWidth;
        const float OutlineRadiusX = 0.14f;
        const float OutlineRadiusY = 0.19f;
        const int ImageWidthPx = 640;
        const int ImageHeightPx = 480;

        int _frameId;
        int _lastToggleFrame = -1;
        bool _hasFace = true;

        public FaceTrackerStatus Status { get; private set; } = FaceTrackerStatus.Idle;
        public string ErrorMessage => string.Empty;

        public void Start() => Status = FaceTrackerStatus.Running;
        public void Stop() => Status = FaceTrackerStatus.Idle;
        public void Dispose() => Stop();
        public void SetPreviewVisible(bool visible) { }

        public bool TryGetLatest(FaceFrame frame)
        {
            if (Status != FaceTrackerStatus.Running) return false;

            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;

            // Normalized mouse position: x right, y up, each in [-1, 1].
            float gx = 0f, gy = 0f;
            if (mouse != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 p = mouse.position.ReadValue();
                gx = Mathf.Clamp((p.x - Screen.width * 0.5f) / (Screen.width * 0.5f), -1f, 1f);
                gy = Mathf.Clamp((p.y - Screen.height * 0.5f) / (Screen.height * 0.5f), -1f, 1f);
            }

            bool headMode = false;
            bool blinking = false;
            if (mouse != null && mouse.rightButton.isPressed) headMode = true;
            if (keyboard != null)
            {
                if (keyboard.leftShiftKey.isPressed) headMode = true;
                blinking = keyboard.spaceKey.isPressed;

                // Guard against toggling twice if TryGetLatest is called more than once in a frame.
                if (keyboard.hKey.wasPressedThisFrame && Time.frameCount != _lastToggleFrame)
                {
                    _hasFace = !_hasFace;
                    _lastToggleFrame = Time.frameCount;
                }
            }

            float yawDeg = 0f, pitchDeg = 0f;
            float eyeGx = gx, eyeGy = gy;
            if (headMode)
            {
                yawDeg = gx * HeadYawMaxDeg;
                pitchDeg = gy * HeadPitchMaxDeg;
                eyeGx = 0f;
                eyeGy = 0f;
            }

            // Turning the head shifts the whole face: turning right (+yaw) moves it toward image left.
            float cx = 0.5f - Mathf.Sin(yawDeg * Mathf.Deg2Rad) * FaceShiftX;
            float cy = 0.5f - Mathf.Sin(pitchDeg * Mathf.Deg2Rad) * FaceShiftY;

            frame.FrameId = ++_frameId;
            frame.TimestampMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            frame.ImageWidth = ImageWidthPx;
            frame.ImageHeight = ImageHeightPx;
            frame.HasFace = _hasFace;
            frame.FacialTransform = HeadPoseMath.ToMatrix(yawDeg, pitchDeg, 0f);

            float[] bs = frame.Blendshapes;
            for (int i = 0; i < FaceFrame.BlendshapeCount; i++) bs[i] = 0f;
            if (blinking)
            {
                bs[BlendshapeIndex.EyeBlinkLeft] = 1f;
                bs[BlendshapeIndex.EyeBlinkRight] = 1f;
            }

            Vector3[] lm = frame.Landmarks;

            // Default: an outline ellipse around the face center, spread by index.
            for (int i = 0; i < FaceFrame.LandmarkCount; i++)
            {
                float t = i * (Mathf.PI * 2f / FaceFrame.LandmarkCount);
                lm[i] = new Vector3(cx + OutlineRadiusX * Mathf.Cos(t), cy + OutlineRadiusY * Mathf.Sin(t), 0f);
            }

            lm[FaceLandmarkIndex.NoseTip] = new Vector3(cx, cy + 0.03f, 0f);
            lm[FaceLandmarkIndex.Chin] = new Vector3(cx, cy + 0.15f, 0f);
            lm[FaceLandmarkIndex.Forehead] = new Vector3(cx, cy - 0.15f, 0f);

            // Eye A is on the image left (subject's right eye), eye B on the image right (subject's left eye).
            float eyeY = cy + EyeOffsetY;
            float eyeAx = cx - EyeSpacingX;
            float eyeBx = cx + EyeSpacingX;
            float lid = blinking ? 0f : LidOffsetY;

            WriteEye(lm, FaceLandmarkIndex.EyeAOuter, FaceLandmarkIndex.EyeAInner,
                FaceLandmarkIndex.EyeAUpper, FaceLandmarkIndex.EyeALower, eyeAx, eyeY, EyeWidth, lid);
            WriteEye(lm, FaceLandmarkIndex.EyeBInner, FaceLandmarkIndex.EyeBOuter,
                FaceLandmarkIndex.EyeBUpper, FaceLandmarkIndex.EyeBLower, eyeBx, eyeY, EyeWidth, lid);

            // Iris offset: looking right (+gx) moves the iris toward image left; looking up (+gy) moves it up.
            float irisDx = -eyeGx * IrisRangeX * EyeWidth;
            float irisDy = -eyeGy * IrisRangeY * EyeWidth;
            WriteIris(lm, FaceLandmarkIndex.IrisCenter1, eyeAx + irisDx, eyeY + irisDy);
            WriteIris(lm, FaceLandmarkIndex.IrisCenter2, eyeBx + irisDx, eyeY + irisDy);

            return true;
        }

        static void WriteEye(Vector3[] lm, int leftIdx, int rightIdx, int upperIdx, int lowerIdx,
            float ecx, float ecy, float width, float lid)
        {
            float halfW = width * 0.5f;
            lm[leftIdx] = new Vector3(ecx - halfW, ecy, 0f);
            lm[rightIdx] = new Vector3(ecx + halfW, ecy, 0f);
            lm[upperIdx] = new Vector3(ecx, ecy - lid, 0f);
            lm[lowerIdx] = new Vector3(ecx, ecy + lid, 0f);
        }

        static void WriteIris(Vector3[] lm, int centerIdx, float ix, float iy)
        {
            lm[centerIdx] = new Vector3(ix, iy, 0f);
            lm[centerIdx + 1] = new Vector3(ix + IrisRadius, iy, 0f);
            lm[centerIdx + 2] = new Vector3(ix, iy + IrisRadius, 0f);
            lm[centerIdx + 3] = new Vector3(ix - IrisRadius, iy, 0f);
            lm[centerIdx + 4] = new Vector3(ix, iy - IrisRadius, 0f);
        }
    }
}
