using System;

namespace SecondCursor.Core
{
    /// <summary>
    /// Minimal 2D vector for the engine-free simulation layer (Core has no UnityEngine reference so it
    /// can be unit-tested outside Unity). Runtime code converts with the extension methods in
    /// SecondCursor.VecExt. Coordinates are virtual screen pixels, origin bottom-left, +Y up.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x;
        public float y;

        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);

        public float Length => (float)Math.Sqrt(x * x + y * y);
        public float SqrLength => x * x + y * y;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(x / l, y / l) : Zero;
            }
        }

        /// <summary>Counter-clockwise perpendicular.</summary>
        public Vec2 Perpendicular => new Vec2(-y, x);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.x / s, a.y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDelta)
        {
            Vec2 d = target - current;
            float len = d.Length;
            if (len <= maxDelta || len < 1e-6f) return target;
            return current + d / len * maxDelta;
        }

        public static Vec2 ClampMagnitude(Vec2 v, float max)
        {
            float l = v.Length;
            return l > max && l > 1e-6f ? v / l * max : v;
        }

        /// <summary>Quadratic bezier.</summary>
        public static Vec2 Bezier(Vec2 a, Vec2 control, Vec2 b, float t)
        {
            float u = 1f - t;
            return a * (u * u) + control * (2f * u * t) + b * (t * t);
        }

        public bool IsFinite => !float.IsNaN(x) && !float.IsNaN(y) && !float.IsInfinity(x) && !float.IsInfinity(y);

        public bool Equals(Vec2 other) => x.Equals(other.x) && y.Equals(other.y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (x.GetHashCode() * 397) ^ y.GetHashCode();
        public override string ToString() => "(" + x.ToString("0.#") + ", " + y.ToString("0.#") + ")";
    }

    /// <summary>Small math helpers shared by the simulation code.</summary>
    public static class MathUtil
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Minimum-jerk position profile (the classic human reaching curve): 10t^3 - 15t^4 + 6t^5.</summary>
        public static float MinJerk(float t)
        {
            t = Clamp01(t);
            float t3 = t * t * t;
            return t3 * (10f - 15f * t + 6f * t * t);
        }

        /// <summary>Frame-rate independent exponential approach factor.</summary>
        public static float Damp(float sharpness, float dt) => 1f - (float)Math.Exp(-sharpness * dt);

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Cheap smooth 1D value noise in [-1,1], deterministic for a given seed.</summary>
        public static float Noise1(float x, int seed)
        {
            int i0 = (int)Math.Floor(x);
            float f = x - i0;
            float a = Hash(i0, seed);
            float b = Hash(i0 + 1, seed);
            float s = f * f * (3f - 2f * f);
            return a + (b - a) * s;
        }

        static float Hash(int i, int seed)
        {
            unchecked
            {
                uint h = (uint)(i * 374761393 + seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x7FFFFF - 1f;
            }
        }
    }
}
