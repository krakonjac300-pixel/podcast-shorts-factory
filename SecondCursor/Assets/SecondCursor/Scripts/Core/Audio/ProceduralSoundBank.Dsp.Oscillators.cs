// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, the DSP toolkit, part 1: PRNG, oscillators and tables
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
        // ====================================================================
        //  2. DSP toolkit
        // ====================================================================

        private const float Sr = SampleRate;
        private const float Dt = 1f / SampleRate;
        private const double DtD = 1.0 / SampleRate;
        private const float TwoPi = 2f * MathF.PI;
        private const float PeakCeiling = 0.89f;

        /// <summary>
        /// Tiny offset added to every recursive filter's input. IIR states ringing down into silence would
        /// otherwise sink into the denormal range, where x86 float math is ~20-100x slower. It is added to the
        /// input (not the state), so it costs nothing on the loop-carried dependency chain. -360 dBFS of DC.
        /// </summary>
        private const float AntiDenormal = 1e-18f;
        private const int MainsPeriod = SampleRate / 60;   // 735 samples = one 60 Hz cycle (US mains)

        // ---------------- PRNG (PCG32) ----------------

        private sealed class Rng
        {
            private ulong _state;

            public Rng(ulong seed)
            {
                _state = seed;
                Next();
                Next();
            }

            public uint Next()
            {
                ulong old = _state;
                _state = unchecked(old * 6364136223846793005UL + 1442695040888963407UL);
                uint xs = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xs >> rot) | (xs << ((-rot) & 31));
            }

            /// <summary>Uniform [0, 1).</summary>
            public float Float() => (Next() >> 8) * (1f / 16777216f);

            /// <summary>Uniform [-1, 1).</summary>
            public float Signed() => (Next() >> 8) * (2f / 16777216f) - 1f;

            public float Range(float lo, float hi) => lo + (hi - lo) * Float();

            public int Int(int lo, int hiExclusive) => lo + (int)(((ulong)Next() * (ulong)(hiExclusive - lo)) >> 32);

            public bool Chance(float p) => Float() < p;

            /// <summary>Multiplicative jitter: 1 +/- amount.</summary>
            public float Jitter(float amount) => 1f + amount * Signed();
        }

        // ---------------- Oscillators ----------------

        private const int SinSize = 4096;
        private static readonly float[] SinTable = BuildSinTable();

        private static float[] BuildSinTable()
        {
            var t = new float[SinSize + 2];
            for (int i = 0; i < t.Length; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / SinSize);
            return t;
        }

        /// <summary>Keeps the table index positive for phases down to -4096 cycles (no Math.Floor needed).</summary>
        private const double SinOffset = 4096.0 * SinSize;

        /// <summary>sin(2*pi*phase), phase in cycles (-4096 .. +500000). Interpolated table, ~-140 dB error.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sin01(double phase)
        {
            double x = phase * SinSize + SinOffset;
            int k = (int)x;
            float f = (float)(x - k);
            int i = k & (SinSize - 1);
            float a = SinTable[i];
            return a + (SinTable[i + 1] - a) * f;
        }

        // Hot loops stay in one numeric domain: per-sample int/float/double conversions are what is slow
        // (on .NET float->double stalls on a false register dependency, on Mono int->float/double does).
        // Oscillators therefore run on double phases, envelopes on float ones.

        /// <summary>sin(2*pi*phase) for a double phase in [0, 1] - the oscillators' fast path.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SinPhase(double phase)
        {
            double x = phase * SinSize;
            int i = (int)x;
            float f = (float)(x - i);
            float a = SinTable[i];
            return a + (SinTable[i + 1] - a) * f;
        }

        /// <summary>sin(2*pi*phase) for a float phase in [0, 1] - the envelopes' fast path.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SinUnit(float phase)
        {
            float x = phase * SinSize;
            int i = (int)x;
            float f = x - i;
            float a = SinTable[i];
            return a + (SinTable[i + 1] - a) * f;
        }

        /// <summary>sin(2*pi*phase) for a small float phase of either sign.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SinWrap(float phase)
        {
            phase -= (int)phase;
            return SinUnit(phase < 0f ? phase + 1f : phase);
        }

        /// <summary>Free-running phase accumulator for one-shots (frequency may change every sample).</summary>
        private struct Phasor
        {
            public double Phase;

            /// <summary>Returns the current phase in [0, 1) and advances (0 &lt;= hz &lt; SampleRate).</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Advance(float hz)
            {
                double p = Phase;
                Phase += hz * DtD;
                if (Phase >= 1.0) Phase -= 1.0;
                return p;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Sin(float hz) => SinPhase(Advance(hz));
        }

        /// <summary>
        /// Periodic phase for loops: the frequency is rounded to a whole number of cycles per loop, so the phase
        /// is back at its start at the loop point (to ~1e-11 cycles with double accumulation).
        /// </summary>
        private struct LoopOsc
        {
            private readonly double _inc;
            private double _phase;

            public LoopOsc(float hz, int loopSamples, float startPhase = 0f)
            {
                int cycles = Math.Max(0, (int)Math.Round(hz * (double)loopSamples / SampleRate));
                _inc = (double)cycles / loopSamples;
                _phase = startPhase - Math.Floor(startPhase);
            }

            /// <summary>Phase in [0, 1), then advance one sample.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Next()
            {
                double p = _phase;
                _phase += _inc;
                if (_phase >= 1.0) _phase -= 1.0;
                return p;
            }
        }

        /// <summary>
        /// Phase offset (cycles) of a sinusoidal vibrato of +/-depthHz running 'cycles' times per loop.
        /// It integrates to zero over the loop, so frequency-modulated loop tones stay seamless.
        /// </summary>
        private static double LoopFm(int i, int n, float depthHz, int cycles, double phase0 = 0.0)
        {
            double rateHz = (double)cycles * SampleRate / n;
            double u = (double)i * cycles / n + phase0;
            return depthHz / (TwoPi * rateHz) * (Sin01(phase0 + 0.25) - Sin01(u + 0.25));
        }

        /// <summary>
        /// <see cref="LoopFm"/> evaluated every 32 samples and linearly interpolated (the vibrato is slow, so this
        /// is exact to ~1e-7 cycles); the last block ends exactly on the value at sample 0, keeping the seam.
        /// </summary>
        private static double[] LoopFmCurve(int n, float depthHz, int cycles, double phase0 = 0.0)
        {
            const int block = 32;
            var c = new double[n];
            for (int i0 = 0; i0 < n; i0 += block)
            {
                int len = Math.Min(block, n - i0);
                double a = LoopFm(i0, n, depthHz, cycles, phase0), b = LoopFm(i0 + len, n, depthHz, cycles, phase0);
                for (int j = 0; j < len; j++) c[i0 + j] = a + (b - a) * j / len;
            }
            return c;
        }

        /// <summary>Single-cycle wavetable (built additively, so band-limited by construction).</summary>
        private sealed class Wavetable
        {
            private const int Size = 2048;
            private readonly float[] _t = new float[Size + 2];

            public Wavetable(float[] harmonicAmps, float[] harmonicPhases = null)
            {
                for (int h = 0; h < harmonicAmps.Length; h++)
                {
                    float a = harmonicAmps[h];
                    if (a == 0f) continue;
                    double ph = harmonicPhases != null ? harmonicPhases[h] : 0.0;
                    for (int i = 0; i < _t.Length; i++) _t[i] += a * Sin01((double)(h + 1) * i / Size + ph);
                }
            }

            /// <summary>Sample at phase in [0, 1).</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float At(double phase)
            {
                double x = phase * Size;
                int i = (int)x;
                float f = (float)(x - i);
                float a = _t[i];
                return a + (_t[i + 1] - a) * f;
            }
        }

        /// <summary>Harmonic amplitudes 1/n^tilt (a soft saw-like spectrum).</summary>
        private static float[] SawAmps(int count, float tilt)
        {
            var a = new float[count];
            for (int h = 0; h < count; h++) a[h] = MathF.Pow(h + 1, -tilt);
            return a;
        }

        private static float[] RandomPhases(int count, Rng r)
        {
            var p = new float[count];
            for (int i = 0; i < count; i++) p[i] = r.Float();
            return p;
        }

        /// <summary>One period of a periodic signal with 'period' samples, built from harmonics of SampleRate/period.</summary>
        private static float[] HarmonicCycle(int period, int[] harmonics, float[] amps, Rng r)
        {
            var cycle = new float[period];
            for (int k = 0; k < harmonics.Length; k++)
            {
                double ph = r.Float();
                for (int i = 0; i < period; i++) cycle[i] += amps[k] * Sin01((double)harmonics[k] * i / period + ph);
            }
            return cycle;
        }
    }
}
