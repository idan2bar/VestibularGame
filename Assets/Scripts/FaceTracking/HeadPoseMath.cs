using UnityEngine;

namespace VestibularGame.FaceTracking
{
    /// <summary>
    /// Converts between MediaPipe's facial transformation matrix and yaw/pitch/roll in degrees.
    /// MediaPipe camera space (unmirrored image): +x = image right, +y = up, +z = toward the viewer.
    /// In face space the face looks along +z and its up is +y.
    /// Signs follow <see cref="GazeSample"/>: +yaw = user turns to their right (face points toward image LEFT),
    /// +pitch = user looks up, +roll = head tilted toward the user's right shoulder.
    /// If a real device shows inverted signs, fix it here only.
    /// </summary>
    public static class HeadPoseMath
    {
        public static void FromMatrix(Matrix4x4 m, out float yaw, out float pitch, out float roll)
        {
            Vector3 f = m.MultiplyVector(Vector3.forward).normalized; // face forward (+z) in camera space
            Vector3 u = m.MultiplyVector(Vector3.up).normalized;      // face up (+y) in camera space

            float yawRad = Mathf.Atan2(-f.x, f.z);
            float pitchRad = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f));

            // Undo yaw and pitch, then read roll from the remaining up vector.
            Vector3 u2 = RotX(pitchRad).MultiplyVector(RotY(yawRad).MultiplyVector(u));
            float rollRad = Mathf.Atan2(-u2.x, u2.y);

            yaw = yawRad * Mathf.Rad2Deg;
            pitch = pitchRad * Mathf.Rad2Deg;
            roll = rollRad * Mathf.Rad2Deg;
        }

        /// <summary>Builds a rotation-only facial transformation matrix (inverse of <see cref="FromMatrix"/>).</summary>
        public static Matrix4x4 ToMatrix(float yawDeg, float pitchDeg, float rollDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad, r = rollDeg * Mathf.Deg2Rad;
            return RotY(-y) * RotX(-p) * RotZ(r);
        }

        // Right-handed rotation matrices (column vectors).
        static Matrix4x4 RotX(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m11 = c; m.m12 = -s;
            m.m21 = s; m.m22 = c;
            return m;
        }

        static Matrix4x4 RotY(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m00 = c; m.m02 = s;
            m.m20 = -s; m.m22 = c;
            return m;
        }

        static Matrix4x4 RotZ(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m00 = c; m.m01 = -s;
            m.m10 = s; m.m11 = c;
            return m;
        }
    }
}
