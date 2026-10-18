using UnityEngine;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// One face-tracking result in MediaPipe Face Landmarker format.
    /// Landmarks are in normalized, UNMIRRORED camera image coordinates:
    /// x in [0,1] left→right, y in [0,1] top→bottom, z is relative depth (smaller = closer to camera).
    /// </summary>
    public sealed class FaceFrame
    {
        public const int LandmarkCount = 478;
        public const int BlendshapeCount = 52;

        public bool HasFace;
        public double TimestampMs;
        public int FrameId;
        public int ImageWidth;
        public int ImageHeight;

        public readonly Vector3[] Landmarks = new Vector3[LandmarkCount];
        public readonly float[] Blendshapes = new float[BlendshapeCount];

        /// <summary>MediaPipe facial transformation matrix (face → camera space, right-handed, y up, z toward viewer).</summary>
        public Matrix4x4 FacialTransform = Matrix4x4.identity;

        /// <summary>Fills this frame from a float buffer laid out as described in <see cref="FaceBufferLayout"/>.</summary>
        public void ReadFrom(float[] buffer)
        {
            HasFace = buffer[FaceBufferLayout.HasFace] > 0.5f;
            TimestampMs = buffer[FaceBufferLayout.TimestampMs];
            FrameId = (int)buffer[FaceBufferLayout.FrameId];
            ImageWidth = (int)buffer[FaceBufferLayout.ImageWidth];
            ImageHeight = (int)buffer[FaceBufferLayout.ImageHeight];

            // MediaPipe doesn't document the flattening order, so detect it: the translation (face depth is never ~0)
            // sits in elements 12-14 when column-major and in 3, 7, 11 when row-major.
            int mo = FaceBufferLayout.Matrix;
            float colT = Mathf.Abs(buffer[mo + 12]) + Mathf.Abs(buffer[mo + 13]) + Mathf.Abs(buffer[mo + 14]);
            float rowT = Mathf.Abs(buffer[mo + 3]) + Mathf.Abs(buffer[mo + 7]) + Mathf.Abs(buffer[mo + 11]);
            bool rowMajor = rowT > colT;
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                if (rowMajor) m[i / 4, i % 4] = buffer[mo + i];
                else m[i % 4, i / 4] = buffer[mo + i];
            }
            FacialTransform = m;

            System.Array.Copy(buffer, FaceBufferLayout.Blendshapes, Blendshapes, 0, BlendshapeCount);

            int o = FaceBufferLayout.Landmarks;
            for (int i = 0; i < LandmarkCount; i++, o += 3)
                Landmarks[i] = new Vector3(buffer[o], buffer[o + 1], buffer[o + 2]);
        }

        public void CopyTo(FaceFrame other)
        {
            other.HasFace = HasFace;
            other.TimestampMs = TimestampMs;
            other.FrameId = FrameId;
            other.ImageWidth = ImageWidth;
            other.ImageHeight = ImageHeight;
            other.FacialTransform = FacialTransform;
            System.Array.Copy(Landmarks, other.Landmarks, LandmarkCount);
            System.Array.Copy(Blendshapes, other.Blendshapes, BlendshapeCount);
        }
    }

    /// <summary>
    /// Float32 buffer layout shared by the JS bridge (facetracker.js) and <see cref="FaceFrame.ReadFrom"/>.
    /// </summary>
    public static class FaceBufferLayout
    {
        public const int HasFace = 0;        // 1 if a face was detected, else 0
        public const int TimestampMs = 1;    // performance.now() of the video frame
        public const int FrameId = 2;        // increments per processed video frame
        public const int ImageWidth = 3;     // video width in pixels
        public const int ImageHeight = 4;    // video height in pixels
        public const int Matrix = 5;         // 16 floats, MediaPipe matrix.data verbatim (layout auto-detected on read)
        public const int Blendshapes = 21;   // 52 floats, MediaPipe category index order
        public const int Landmarks = 73;     // 478 * (x, y, z)
        public const int Length = Landmarks + FaceFrame.LandmarkCount * 3; // 1507
    }

    /// <summary>
    /// Landmark indices of interest. "EyeA" is the eye on the LEFT of the unmirrored image (the subject's right eye);
    /// "EyeB" is the eye on the RIGHT of the unmirrored image (the subject's left eye).
    /// Iris-to-eye pairing should not be assumed: match iris centers to eyes by proximity.
    /// </summary>
    public static class FaceLandmarkIndex
    {
        public const int EyeAOuter = 33;
        public const int EyeAInner = 133;
        public const int EyeAUpper = 159;
        public const int EyeALower = 145;

        public const int EyeBInner = 362;
        public const int EyeBOuter = 263;
        public const int EyeBUpper = 386;
        public const int EyeBLower = 374;

        public const int IrisCenter1 = 468; // followed by 4 iris contour points 469-472
        public const int IrisCenter2 = 473; // followed by 4 iris contour points 474-477

        public const int NoseTip = 1;
        public const int Chin = 152;
        public const int Forehead = 10;
    }

    /// <summary>MediaPipe blendshape indices (subject's Left/Right).</summary>
    public static class BlendshapeIndex
    {
        public const int EyeBlinkLeft = 9;
        public const int EyeBlinkRight = 10;
        public const int EyeLookDownLeft = 11;
        public const int EyeLookDownRight = 12;
        public const int EyeLookInLeft = 13;
        public const int EyeLookInRight = 14;
        public const int EyeLookOutLeft = 15;
        public const int EyeLookOutRight = 16;
        public const int EyeLookUpLeft = 17;
        public const int EyeLookUpRight = 18;
    }
}
