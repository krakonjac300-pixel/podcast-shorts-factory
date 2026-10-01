// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, the DSP toolkit, part 2: filters, envelopes and small math
//  Part of the one partial class (see ProceduralSoundBank.cs for the registry and the rules).
//  Phase Q4 (CH9): split out of the 2,412-line file by family; not one sample changed
//  (SoundBankFingerprintTests checks every sound, sample for sample).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SecondCursor.Core.Audio
{
    public static partial class ProceduralSoundBank
    {
        // ---------------- Filters ----------------

        private sealed class OnePole
        {
            private float _a, _z;

            public OnePole(float hz) { Set(hz); }

            public void Set(float hz) { _a = 1f - MathF.Exp(-TwoPi * hz / Sr); }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Lp(float x) { _z += _a * (x + AntiDenormal - _z); return _z; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Hp(float x) { _z += _a * (x + AntiDenormal - _z); return x - _z; }
        }

        /// <summary>Topology-preserving state-variable filter (Zavalishin / Simper). Cheap to modulate.</summary>
        private sealed class Svf
        {
            private float _a1, _a2, _a3, _k, _ic1, _ic2;

            public Svf(float hz, float q = 0.7071f) { Set(hz, q); }

            public void Set(float hz, float q)
            {
                if (hz > 0.45f * Sr) hz = 0.45f * Sr;
                if (hz < 5f) hz = 5f;
                float g = MathF.Tan(MathF.PI * hz / Sr);
                _k = 1f / q;
                _a1 = 1f / (1f + g * (g + _k));
                _a2 = g * _a1;
                _a3 = g * _a2;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Tick(float x, out float band, out float low)
            {
                float v3 = x + AntiDenormal - _ic2;
                band = _a1 * _ic1 + _a2 * v3;
                low = _ic2 + _a2 * _ic1 + _a3 * v3;
                _ic1 = 2f * band - _ic1;
                _ic2 = 2f * low - _ic2;
            }

            public float Lp(float x) { Tick(x, out _, out float low); return low; }

            public float Hp(float x) { Tick(x, out float band, out float low); return x - _k * band - low; }

            /// <summary>Band-pass with unity gain at the centre (note: only 6 dB/oct skirts).</summary>
            public float Bp(float x) { Tick(x, out float band, out _); return _k * band; }
        }

        /// <summary>4th-order Butterworth low-pass (24 dB/oct) - the tool for keeping noise out of the top octave.</summary>
        private sealed class Lp4
        {
            private readonly Svf _a, _b;

            public Lp4(float hz)
            {
                _a = new Svf(hz, 0.5412f);
                _b = new Svf(hz, 1.3066f);
            }

            public float Process(float x) => _b.Lp(_a.Lp(x));
        }

        /// <summary>Two-pole resonator: a unit impulse rings as gain * sin(), decaying with time constant tau.</summary>
        private sealed class Resonator
        {
            private readonly float _b0, _a1, _a2;
            private float _y1, _y2;

            public Resonator(float hz, float tauSec, float gain)
            {
                double w = 2.0 * Math.PI * hz / SampleRate;
                double r = Math.Exp(-1.0 / (tauSec * SampleRate));
                _a1 = (float)(2.0 * r * Math.Cos(w));
                _a2 = (float)(-r * r);
                _b0 = (float)(gain * Math.Sin(w));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Process(float x)
            {
                float y = _b0 * (x + AntiDenormal) + _a1 * _y1 + _a2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }

        /// <summary>
        /// Several two-pole resonators fed the same (noise) input and stepped together, so their independent
        /// recursions overlap in the CPU pipeline. Each gain is the output RMS for uniform white noise in [-1, 1]
        /// (a resonator integrates noise over ~tau, so its raw gain would grow with sqrt(tau * SampleRate)).
        /// </summary>
        private sealed class ResonatorBank
        {
            private readonly float[] _b0, _a1, _a2, _y1, _y2;
            public readonly float[] Out;

            public ResonatorBank(float[] hz, float tauSec, float[] rmsGains)
            {
                int k = hz.Length;
                _b0 = new float[k]; _a1 = new float[k]; _a2 = new float[k];
                _y1 = new float[k]; _y2 = new float[k]; Out = new float[k];
                double r = Math.Exp(-1.0 / (tauSec * SampleRate));
                double norm = Math.Sqrt(6.0 * (1.0 - r * r)); // unit-amplitude ringing -> unit RMS for noise of variance 1/3
                for (int j = 0; j < k; j++)
                {
                    double w = 2.0 * Math.PI * hz[j] / SampleRate;
                    _a1[j] = (float)(2.0 * r * Math.Cos(w));
                    _a2[j] = (float)(-r * r);
                    _b0[j] = (float)(rmsGains[j] * norm * Math.Sin(w));
                }
            }

            /// <summary>Steps every resonator with input x; results land in <see cref="Out"/>.</summary>
            public void Process(float x)
            {
                x += AntiDenormal;
                for (int j = 0; j < Out.Length; j++)
                {
                    float y = _b0[j] * x + _a1[j] * _y1[j] + _a2[j] * _y2[j];
                    _y2[j] = _y1[j];
                    _y1[j] = y;
                    Out[j] = y;
                }
            }
        }

        /// <summary>Paul Kellet's economy pink-noise filter.</summary>
        private sealed class PinkFilter
        {
            private float _b0, _b1, _b2;

            public float Process(float w)
            {
                _b0 = 0.99765f * _b0 + w * 0.0990460f;
                _b1 = 0.96300f * _b1 + w * 0.2965164f;
                _b2 = 0.57000f * _b2 + w * 1.0526913f;
                return (_b0 + _b1 + _b2 + w * 0.1848f) * 0.25f;
            }
        }

        /// <summary>
        /// Freeverb-style mono reverb: 8 parallel damped combs into 4 series all-passes, at most unity power
        /// gain (damping lowers it), so "wet = 1" means a tail about as energetic as the dry sound.
        /// Unrolled so the eight independent comb recursions overlap in the CPU pipeline.
        /// </summary>
        private sealed class Reverb
        {
            private readonly float[] _c0, _c1, _c2, _c3, _c4, _c5, _c6, _c7, _a0, _a1, _a2, _a3;
            private int _i0, _i1, _i2, _i3, _i4, _i5, _i6, _i7, _j0, _j1, _j2, _j3;
            private float _l0, _l1, _l2, _l3, _l4, _l5, _l6, _l7;
            private readonly float _feedback, _damp, _inGain;

            /// <param name="size">Delay-length scale (1 = medium room).</param>
            /// <param name="feedback">Comb feedback (0.7 short .. 0.92 long).</param>
            /// <param name="damp">High-frequency damping 0..1.</param>
            public Reverb(float size, float feedback, float damp)
            {
                _c0 = Line(1116, size); _c1 = Line(1188, size); _c2 = Line(1277, size); _c3 = Line(1356, size);
                _c4 = Line(1422, size); _c5 = Line(1491, size); _c6 = Line(1557, size); _c7 = Line(1617, size);
                _a0 = Line(556, size); _a1 = Line(441, size); _a2 = Line(341, size); _a3 = Line(225, size);
                _feedback = feedback;
                _damp = damp;
                _inGain = MathF.Sqrt((1f - feedback * feedback) / 8f);
            }

            private static float[] Line(int length, float size) => new float[Math.Max(8, (int)(length * size))];

            public float Process(float x)
            {
                float input = x * _inGain + AntiDenormal;
                float sum = Comb(_c0, ref _i0, ref _l0, input) + Comb(_c1, ref _i1, ref _l1, input)
                          + Comb(_c2, ref _i2, ref _l2, input) + Comb(_c3, ref _i3, ref _l3, input)
                          + Comb(_c4, ref _i4, ref _l4, input) + Comb(_c5, ref _i5, ref _l5, input)
                          + Comb(_c6, ref _i6, ref _l6, input) + Comb(_c7, ref _i7, ref _l7, input);
                sum = Allpass(_a0, ref _j0, sum);
                sum = Allpass(_a1, ref _j1, sum);
                sum = Allpass(_a2, ref _j2, sum);
                return Allpass(_a3, ref _j3, sum);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float Comb(float[] line, ref int pos, ref float lp, float input)
            {
                int p = pos;
                float y = line[p];
                lp = y + (lp - y) * _damp;
                line[p] = input + lp * _feedback;
                pos = ++p == line.Length ? 0 : p;
                return y;
            }

            /// <summary>True Schroeder all-pass, g = 0.5 (Freeverb's shortcut form is not all-pass: +3.7 dB per stage).</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float Allpass(float[] line, ref int pos, float x)
            {
                int p = pos;
                float delayed = line[p];
                float w = x + 0.5f * delayed;
                line[p] = w;
                pos = ++p == line.Length ? 0 : p;
                return delayed - 0.5f * w;
            }
        }

        // ---------------- Envelopes & small math ----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sq(float x) => x * x;

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        private static float DbToGain(float db) => MathF.Pow(10f, db / 20f);

        private static float SmoothStep(float e0, float e1, float x)
        {
            float u = Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }

        /// <summary>exp(-t/tau) for t &gt;= 0, 0 before (and flushed to 0 far into the tail).</summary>
        private static float Decay(float t, float tau) => t < 0f || t > 40f * tau ? 0f : MathF.Exp(-t / tau);

        /// <summary>Raised-cosine rise over [0, 1].</summary>
        private static float Rise(float u) => u <= 0f ? 0f : (u >= 1f ? 1f : 0.5f - 0.5f * SinUnit(0.25f + 0.5f * u));

        /// <summary>Raised-cosine attack followed by exponential decay.</summary>
        private static float AttackDecay(float t, float attack, float tau)
        {
            if (t < 0f) return 0f;
            if (t < attack) return Rise(t / attack);
            return Decay(t - attack, tau);
        }

        /// <summary>Raised-cosine bump centred at c with half-width w (0 outside).</summary>
        private static float Bump(float t, float c, float w)
        {
            float d = (t - c) / w;
            return d <= -1f || d >= 1f ? 0f : 0.5f + 0.5f * SinWrap(0.25f + 0.5f * d);
        }

        /// <summary>Circular distance between two positions on a loop, both in [0, 1).</summary>
        private static float LoopDistance(float a, float b)
        {
            float d = MathF.Abs(a - b);
            return d > 0.5f ? 1f - d : d;
        }

        private static float SoftClip(float x)
        {
            if (x <= -3f) return -1f;
            if (x >= 3f) return 1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2); // Pade tanh
        }

        private static float Crush(float x, float levels) => MathF.Round(x * levels) / levels;
    }
}
