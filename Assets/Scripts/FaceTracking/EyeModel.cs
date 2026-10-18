using UnityEngine;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Geometry shared by the gaze estimator (inverse) and the mock tracker (forward).
    /// "Image space" is pixel-proportional and unmirrored: x right, y DOWN, z AWAY from the camera,
    /// with landmark z scaled like x (MediaPipe convention), i.e. P = (x * W, y * H, z * W).
    /// The eyeball centre sits <c>depth</c> eye-widths behind the eye-corner midpoint along the head's forward axis,
    /// and the iris centre lies <c>radius</c> eye-widths from the eyeball centre along the gaze direction.
    /// </summary>
    public static class EyeModel
    {
        public const float DefaultEyeballCenterDepth = 0.2f; // eye widths behind the corner midpoint
        public const float DefaultIrisSphereRadius = 0.42f;  // eye widths from eyeball centre to iris centre

        public static Vector3 ToImageSpace(Vector3 normalized, float width, float height) =>
            new Vector3(normalized.x * width, normalized.y * height, normalized.z * width);

        public static Vector3 ToNormalized(Vector3 image, float width, float height) =>
            new Vector3(image.x / width, image.y / height, image.z / width);

        /// <summary>
        /// Head axes in image space from the facial transformation matrix:
        /// right = toward the image right when frontal (subject's left), up = face up, forward = out of the face (toward the camera when frontal).
        /// </summary>
        public static void HeadAxes(Matrix4x4 facialTransform, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = FromMediaPipe(facialTransform.MultiplyVector(Vector3.right)).normalized;
            up = FromMediaPipe(facialTransform.MultiplyVector(Vector3.up)).normalized;
            forward = FromMediaPipe(facialTransform.MultiplyVector(Vector3.forward)).normalized;
        }

        /// <summary>Unit gaze direction in image space for eye-in-head angles (degrees, <see cref="GazeSample"/> signs).</summary>
        public static Vector3 DirectionFromHeadAngles(float yawDeg, float pitchDeg, Vector3 right, Vector3 up, Vector3 forward)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p);
            return right * (-Mathf.Sin(y) * cp) + up * Mathf.Sin(p) + forward * (Mathf.Cos(y) * cp);
        }

        /// <summary>Eye-in-head angles (degrees) of an image-space direction.</summary>
        public static void HeadAnglesFromDirection(Vector3 dir, Vector3 right, Vector3 up, Vector3 forward, out float yawDeg, out float pitchDeg)
        {
            float x = Vector3.Dot(dir, right), y = Vector3.Dot(dir, up), z = Vector3.Dot(dir, forward);
            yawDeg = Mathf.Atan2(-x, z) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Atan2(y, Mathf.Sqrt(x * x + z * z)) * Mathf.Rad2Deg;
        }

        /// <summary>Gaze angles (degrees) of an image-space direction relative to the camera axis (looking into the camera = 0, 0).</summary>
        public static void CameraAnglesFromDirection(Vector3 dir, out float yawDeg, out float pitchDeg)
        {
            yawDeg = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Atan2(-dir.y, Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z)) * Mathf.Rad2Deg;
        }

        /// <summary>Vertical field of view of the virtual camera MediaPipe's face geometry assumes.</summary>
        public const float DefaultCameraVerticalFovDeg = 63f;
        public const float DefaultDistanceCm = 50f;

        public static float FocalLengthPx(float imageHeight, float verticalFovDeg) =>
            0.5f * imageHeight / Mathf.Tan(0.5f * verticalFovDeg * Mathf.Deg2Rad);

        /// <summary>Head distance from the camera in cm (translation z of the facial transform), or <paramref name="fallbackCm"/> if implausible.</summary>
        public static float HeadDistanceCm(Matrix4x4 facialTransform, float fallbackCm)
        {
            float z = Mathf.Abs(facialTransform.m23);
            return float.IsNaN(z) || float.IsInfinity(z) || z < 10f || z > 300f ? fallbackCm : z;
        }

        /// <summary>Image-space point (x right, y down, z away, camera at origin, cm) for a pixel at the given depth.</summary>
        public static Vector3 BackProjectCm(float uPx, float vPx, float imageWidth, float imageHeight, float distanceCm, float focalPx) =>
            new Vector3((uPx - 0.5f * imageWidth) * distanceCm / focalPx, (vPx - 0.5f * imageHeight) * distanceCm / focalPx, distanceCm);

        /// <summary>Exact inverse of <see cref="CameraAnglesFromDirection"/>: unit image-space direction for camera-axis angles (degrees).</summary>
        public static Vector3 DirectionFromCameraAngles(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p);
            return new Vector3(-Mathf.Sin(y) * cp, -Mathf.Sin(p), -Mathf.Cos(y) * cp);
        }

        // MediaPipe camera space (y up, z toward viewer) → image space (y down, z away).
        static Vector3 FromMediaPipe(Vector3 v) => new Vector3(v.x, -v.y, -v.z);
    }
}
