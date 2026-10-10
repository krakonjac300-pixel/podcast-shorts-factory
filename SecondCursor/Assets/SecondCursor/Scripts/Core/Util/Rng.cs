using System;
using System.Collections.Generic;

namespace SecondCursor.Core
{
    /// <summary>
    /// Deterministic xorshift64* random generator. Used instead of UnityEngine.Random so simulation code is
    /// reproducible (the prototype is intentionally NOT heavily randomized) and testable outside Unity.
    /// </summary>
    public sealed class Rng
    {
        ulong _state;

        public Rng(int seed)
        {
            _state = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
            if (_state == 0) _state = 0x2545F4914F6CDD1DUL;
            NextULong();
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 2685821657736338717UL;
            }
        }

        /// <summary>Uniform float in [0,1).</summary>
        public float NextFloat() => (NextULong() >> 40) / 16777216f;

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability) => NextFloat() < probability;

        public float Sign() => Chance(0.5f) ? 1f : -1f;

        /// <summary>Standard normal sample (Box-Muller).</summary>
        public float Gaussian()
        {
            float u1 = 1f - NextFloat();
            float u2 = NextFloat();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        public Vec2 InsideUnitCircle()
        {
            float a = Range(0f, (float)(Math.PI * 2.0));
            float r = (float)Math.Sqrt(NextFloat());
            return new Vec2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r);
        }

        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[Range(0, list.Count)];
        }
    }
}
