// Shared pure C# (no UnityEngine). Compiled into both the Unity project and the .NET Companion Host.
using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>Math helpers. Quaternion/vector algebra is identical to Unity's (component-wise copy is valid).</summary>
    public static class M
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InvLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));
        public static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        public static float Smoother(float t) { t = Clamp01(t); return t * t * t * (t * (6f * t - 15f) + 10f); }
        public static float EaseOut(float t) { t = Clamp01(t); return 1f - (1f - t) * (1f - t); }
        public static float EaseIn(float t) { t = Clamp01(t); return t * t; }
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Frac(float x) => x - (float)Math.Floor(x);

        /// <summary>Unity-compatible Euler (degrees): rotation order Z, then X, then Y (q = qy * qx * qz).</summary>
        public static Quaternion Euler(float xDeg, float yDeg, float zDeg)
        {
            var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, xDeg * Deg2Rad);
            var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yDeg * Deg2Rad);
            var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, zDeg * Deg2Rad);
            // System.Numerics: Concatenate(a,b) / a*b applies ... we want v' = qy*(qx*(qz*v)).
            return Mul(qy, Mul(qx, qz));
        }
        public static Quaternion Euler(Vector3 e) => Euler(e.X, e.Y, e.Z);

        /// <summary>Hamilton product a*b meaning "apply b first, then a" (same as Unity's operator*).</summary>
        public static Quaternion Mul(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        /// <summary>Rotate v by q (same as Unity's q * v).</summary>
        public static Vector3 Rot(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.X, q.Y, q.Z);
            float s = q.W;
            return 2f * Vector3.Dot(u, v) * u + (s * s - Vector3.Dot(u, u)) * v + 2f * s * Vector3.Cross(u, v);
        }

        public static Quaternion Inverse(Quaternion q) => Quaternion.Inverse(q);

        /// <summary>Rotation whose columns are the given orthonormal axes (x,y,z images of the unit axes).</summary>
        public static Quaternion FromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            float m00 = x.X, m01 = y.X, m02 = z.X;
            float m10 = x.Y, m11 = y.Y, m12 = z.Y;
            float m20 = x.Z, m21 = y.Z, m22 = z.Z;
            float tr = m00 + m11 + m22;
            Quaternion q;
            if (tr > 0)
            {
                float s = (float)Math.Sqrt(tr + 1.0) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1.0 + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1.0 + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = (float)Math.Sqrt(1.0 + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Quaternion.Normalize(q);
        }

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        public static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }

        public static float BezierLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int steps = 24)
        {
            float len = 0; var prev = p0;
            for (int i = 1; i <= steps; i++) { var p = Bezier(p0, p1, p2, p3, i / (float)steps); len += Vector3.Distance(prev, p); prev = p; }
            return len;
        }

        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float l = v.Length();
            return l < 1e-7f ? fallback : v / l;
        }
    }

    /// <summary>JSON-friendly vector (public fields, no engine dependency).</summary>
    [Serializable]
    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static implicit operator Vector3(V3 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator V3(Vector3 v) => new V3(v.X, v.Y, v.Z);
        public override string ToString() => $"({x:0.####},{y:0.####},{z:0.####})";
    }

    [Serializable]
    public struct Q4
    {
        public float x, y, z, w;
        public Q4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Q4 Identity => new Q4(0, 0, 0, 1);
        public static implicit operator Quaternion(Q4 q) => (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) ? Quaternion.Identity : new Quaternion(q.x, q.y, q.z, q.w);
        public static implicit operator Q4(Quaternion q) => new Q4(q.X, q.Y, q.Z, q.W);
    }
}
