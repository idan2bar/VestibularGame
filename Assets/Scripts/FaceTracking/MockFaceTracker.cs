using UnityEngine;
using UnityEngine.InputSystem;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Mouse- and keyboard-driven stand-in for the webcam face tracker, for testing gaze features in the Editor.
    /// Landmarks come from the same forward 3D model the gaze estimator inverts (see <see cref="EyeModel"/>):
    /// the face is rigidly rotated by the head pose, each eyeball centre sits behind its eye-corner midpoint,
    /// and each iris lies on its eyeball along the gaze direction.
    /// Controls:
    /// <list type="bullet">
    /// <item>Mouse position, relative to screen center, drives the eyes inside the head (yaw up to ±30°, pitch up to ±20°). Looking right or up moves the irises the same way.</item>
    /// <item>Hold right mouse button or Left Shift: the mouse drives the head instead (yaw up to ±30°, pitch up to ±20°) and the eyes keep fixating the camera.</item>
    /// <item>Hold Space: both eyes blink.</item>
    /// <item>H: toggles face detected / face lost.</item>
    /// </list>
    /// </summary>
    public sealed class MockFaceTracker : IFaceTracker
    {
        const float HeadYawMaxDeg = 30f;
        const float HeadPitchMaxDeg = 20f;
        const float EyeYawMaxDeg = 30f;
        const float EyePitchMaxDeg = 20f;
        const int ImageWidthPx = 640;
        const int ImageHeightPx = 480;

        // Face geometry in head space, in pixels. A point is center + right*a + up*b + forward*c,
        // where forward points out of the face (toward the camera when frontal).
        const float EyeWidth = 0.06f * ImageWidthPx;      // w
        const float EyeSpacing = 0.08f * ImageWidthPx;    // eye centres at ±EyeSpacing along right
        const float EyeHeight = 0.05f * ImageWidthPx;     // eye centres EyeHeight above the face centre along up
        const float LidOffset = 0.15f * EyeWidth;         // upper/lower lids at ±LidOffset along up (collapsed while blinking)
        const float IrisContourRadius = 0.12f * EyeWidth;
        const float OutlineRadiusX = 0.14f * ImageWidthPx;
        const float OutlineRadiusY = 0.19f * ImageHeightPx;
        const float NoseDrop = 0.03f * ImageHeightPx;
        const float NoseForward = 0.6f * EyeWidth;
        const float ChinDrop = 0.15f * ImageHeightPx;
        const float ChinForward = 0.2f * EyeWidth;
        const float ForeheadRise = 0.15f * ImageHeightPx;

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

            // Head mode: the mouse turns the head and the eyes keep fixating the camera.
            // Eye mode: the head stays still and the mouse rotates the eyes inside it.
            float headYawDeg = 0f, headPitchDeg = 0f;
            float eyeYawDeg = 0f, eyePitchDeg = 0f;
            if (headMode)
            {
                headYawDeg = gx * HeadYawMaxDeg;
                headPitchDeg = gy * HeadPitchMaxDeg;
            }
            else
            {
                eyeYawDeg = gx * EyeYawMaxDeg;
                eyePitchDeg = gy * EyePitchMaxDeg;
            }

            Matrix4x4 facial = HeadPoseMath.ToMatrix(headYawDeg, headPitchDeg, 0f);
            facial.m23 = -50f; // face 50 cm in front of the camera (MediaPipe camera space: face at negative z)
            EyeModel.HeadAxes(facial, out Vector3 axisRight, out Vector3 axisUp, out Vector3 axisFwd);

            frame.FrameId = ++_frameId;
            frame.TimestampMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            frame.ImageWidth = ImageWidthPx;
            frame.ImageHeight = ImageHeightPx;
            frame.HasFace = _hasFace;
            frame.FacialTransform = facial;

            float[] bs = frame.Blendshapes;
            for (int i = 0; i < FaceFrame.BlendshapeCount; i++) bs[i] = 0f;
            if (blinking)
            {
                bs[BlendshapeIndex.EyeBlinkLeft] = 1f;
                bs[BlendshapeIndex.EyeBlinkRight] = 1f;
            }

            // The face centre sits at the image centre; every landmark is a head-space offset projected through the head axes.
            Vector3 origin = new Vector3(ImageWidthPx * 0.5f, ImageHeightPx * 0.5f, 0f);
            Vector3[] lm = frame.Landmarks;

            // Default: an outline ellipse on the face plane, spread by index.
            for (int i = 0; i < FaceFrame.LandmarkCount; i++)
            {
                float t = i * (Mathf.PI * 2f / FaceFrame.LandmarkCount);
                SetLandmark(lm, i, HeadPoint(origin, axisRight, axisUp, axisFwd,
                    OutlineRadiusX * Mathf.Cos(t), OutlineRadiusY * Mathf.Sin(t), 0f));
            }

            SetLandmark(lm, FaceLandmarkIndex.NoseTip,
                HeadPoint(origin, axisRight, axisUp, axisFwd, 0f, -NoseDrop, NoseForward));
            SetLandmark(lm, FaceLandmarkIndex.Chin,
                HeadPoint(origin, axisRight, axisUp, axisFwd, 0f, -ChinDrop, ChinForward));
            SetLandmark(lm, FaceLandmarkIndex.Forehead,
                HeadPoint(origin, axisRight, axisUp, axisFwd, 0f, ForeheadRise, 0f));

            // Eye A is on the image left (subject's right eye), eye B on the image right (subject's left eye).
            Vector3 eyeACentre = HeadPoint(origin, axisRight, axisUp, axisFwd, -EyeSpacing, EyeHeight, 0f);
            Vector3 eyeBCentre = HeadPoint(origin, axisRight, axisUp, axisFwd, EyeSpacing, EyeHeight, 0f);
            float lid = blinking ? 0f : LidOffset;

            WriteEye(lm, FaceLandmarkIndex.EyeAOuter, FaceLandmarkIndex.EyeAInner,
                FaceLandmarkIndex.EyeAUpper, FaceLandmarkIndex.EyeALower, eyeACentre, axisRight, axisUp, lid);
            WriteEye(lm, FaceLandmarkIndex.EyeBInner, FaceLandmarkIndex.EyeBOuter,
                FaceLandmarkIndex.EyeBUpper, FaceLandmarkIndex.EyeBLower, eyeBCentre, axisRight, axisUp, lid);

            // Gaze in image space. Eye mode: the eyes rotate inside the head. Head mode: both eyes look at the
            // camera lens (perfect VOR), from the cyclopean eye pixel back toward the origin; one shared direction for both eyes.
            Vector3 gaze;
            if (headMode)
            {
                Vector3 cyclopeanPx = 0.5f * (eyeACentre + eyeBCentre);
                Vector3 eyeCm = EyeModel.BackProjectCm(cyclopeanPx.x, cyclopeanPx.y, ImageWidthPx, ImageHeightPx, 50f,
                    EyeModel.FocalLengthPx(ImageHeightPx, EyeModel.DefaultCameraVerticalFovDeg));
                gaze = (-eyeCm).normalized;
            }
            else
            {
                gaze = EyeModel.DirectionFromHeadAngles(eyeYawDeg, eyePitchDeg, axisRight, axisUp, axisFwd);
            }

            WriteIris(lm, FaceLandmarkIndex.IrisCenter1, eyeACentre, gaze, axisRight, axisUp, axisFwd);
            WriteIris(lm, FaceLandmarkIndex.IrisCenter2, eyeBCentre, gaze, axisRight, axisUp, axisFwd);

            return true;
        }

        // Head-space point in image pixels: origin + right*a + up*b + forward*c.
        static Vector3 HeadPoint(Vector3 origin, Vector3 right, Vector3 up, Vector3 fwd, float a, float b, float c) =>
            origin + right * a + up * b + fwd * c;

        // Stores an image-pixel point as a normalized landmark (x/W, y/H, z/W).
        static void SetLandmark(Vector3[] lm, int index, Vector3 imagePx) =>
            lm[index] = EyeModel.ToNormalized(imagePx, ImageWidthPx, ImageHeightPx);

        static void WriteEye(Vector3[] lm, int leftIdx, int rightIdx, int upperIdx, int lowerIdx,
            Vector3 centre, Vector3 right, Vector3 up, float lid)
        {
            Vector3 halfWidth = right * (EyeWidth * 0.5f);
            SetLandmark(lm, leftIdx, centre - halfWidth);
            SetLandmark(lm, rightIdx, centre + halfWidth);
            SetLandmark(lm, upperIdx, centre + up * lid);
            SetLandmark(lm, lowerIdx, centre - up * lid);
        }

        static void WriteIris(Vector3[] lm, int centerIdx, Vector3 eyeCentre, Vector3 gaze,
            Vector3 right, Vector3 up, Vector3 fwd)
        {
            // The eyeball centre sits behind the eye-corner midpoint; the iris sits on the eyeball along the gaze.
            Vector3 eyeball = eyeCentre - fwd * (EyeModel.DefaultEyeballCenterDepth * EyeWidth);
            Vector3 iris = eyeball + gaze * (EyeModel.DefaultIrisSphereRadius * EyeWidth);

            SetLandmark(lm, centerIdx, iris);
            SetLandmark(lm, centerIdx + 1, iris + right * IrisContourRadius);
            SetLandmark(lm, centerIdx + 2, iris + up * IrisContourRadius);
            SetLandmark(lm, centerIdx + 3, iris - right * IrisContourRadius);
            SetLandmark(lm, centerIdx + 4, iris - up * IrisContourRadius);
        }
    }
}
