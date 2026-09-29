// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank
//  Every sound in the game is synthesised here, from code, at start-up.
//  Pure C# (no UnityEngine), deterministic (own PCG32 PRNG), mono 44.1 kHz.
//
//  Layout:
//    1. Public API + registry
//    2. DSP toolkit  (PRNG, oscillators, filters, reverb, envelopes, helpers)
//    3. Sound designs (one method per id, grouped by family)
//
//  Design rule: the player's own actions sound clean, dry and digital (ui_*);
//  everything the entity does is physical, in the room, or electrically "wrong".
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace SecondCursor.Core.Audio
{
    /// <summary>
    /// Procedural sound bank for SECOND CURSOR. Every effect and ambience is synthesised on demand
    /// (there are no audio assets). Output is mono float PCM at <see cref="SampleRate"/>,
    /// peak &lt;= 0.9, DC-free; one-shots are de-clicked and loops are seamless.
    /// Generation is deterministic for a given (id, seed) pair and thread-safe.
    /// </summary>
    public static class ProceduralSoundBank
    {
        /// <summary>Sample rate of every generated buffer, in Hz.</summary>
        public const int SampleRate = 44100;

        /// <summary>All sound ids in a stable order.</summary>
        public static IReadOnlyList<string> Ids => IdList;

        /// <summary>True if <paramref name="id"/> names a sound in the bank.</summary>
        public static bool Has(string id) => id != null && Index.ContainsKey(id);

        /// <summary>True if the sound is built to loop seamlessly.</summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static bool IsLoop(string id) => Find(id).Loop;

        /// <summary>Suggested AudioSource volume (0..1). Relative loudness is already baked into the PCM.</summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static float DefaultVolume(string id) => Find(id).Volume;

        /// <summary>
        /// Synthesises a sound as mono PCM in [-1, 1] at <see cref="SampleRate"/>.
        /// Different seeds give natural variations (e.g. every keystroke differs slightly).
        /// </summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static float[] Generate(string id, int seed = 0)
        {
            Entry entry = Find(id);
            var rng = new Rng(Mix64(HashId(id) ^ Mix64((ulong)(uint)seed + 0x632BE59BD9B4E019UL)));
            return entry.Build(rng);
        }

        // ====================================================================
        //  1. Registry
        // ====================================================================

        private delegate float[] Builder(Rng rng);

        private sealed class Entry
        {
            public readonly string Id;
            public readonly bool Loop;
            public readonly float Volume;
            public readonly Builder Build;

            public Entry(string id, bool loop, float volume, Builder build)
            {
                Id = id; Loop = loop; Volume = volume; Build = build;
            }
        }

        private static readonly Entry[] Entries =
        {
            // --- OS / UI (the player's own world: clean, dry, digital)
            new Entry("ui_click",         false, 0.70f, UiClick),
            new Entry("ui_select",        false, 0.60f, UiSelect),
            new Entry("ui_window",        false, 0.60f, UiWindow),
            new Entry("notify_mail",      false, 0.70f, NotifyMail),
            new Entry("sys_error",        false, 0.70f, SysError),
            new Entry("sys_warning",      false, 0.70f, SysWarning),
            new Entry("sys_startup",      false, 0.80f, SysStartup),
            // --- physical input devices (the second cursor is heard through these: in the room)
            new Entry("mouse_click",      false, 0.80f, MouseClick),
            new Entry("mouse_release",    false, 0.80f, MouseRelease),
            new Entry("key_tap",          false, 0.70f, KeyTap),
            new Entry("key_space",        false, 0.70f, KeySpace),
            new Entry("key_enter",        false, 0.70f, KeyEnter),
            // --- hardware
            new Entry("bios_beep",        false, 0.55f, BiosBeep),
            new Entry("hdd_seek",         false, 0.65f, HddSeek),
            new Entry("hdd_spinup",       false, 0.65f, HddSpinup),
            new Entry("crt_on",           false, 0.75f, CrtOn),
            new Entry("crt_off",          false, 0.75f, CrtOff),
            new Entry("shred_loop",       true,  0.60f, ShredLoop),
            // --- ambience
            new Entry("amb_room",         true,  0.80f, AmbRoom),
            new Entry("amb_fluorescent",  true,  0.60f, AmbFluorescent),
            new Entry("amb_crt_hum",      true,  0.60f, AmbCrtHum),
            new Entry("drone_tension",    true,  0.70f, DroneTension),
            // --- the entity
            new Entry("entity_static",    true,  0.80f, EntityStatic),
            new Entry("entity_appear",    false, 0.80f, EntityAppear),
            new Entry("glitch_burst",     false, 0.70f, GlitchBurst),
            new Entry("tug_strain",       true,  0.70f, TugStrain),
            new Entry("grab_snap",        false, 0.80f, GrabSnap),
            // --- CCTV
            new Entry("camera_switch",    false, 0.70f, CameraSwitch),
            new Entry("camera_static",    true,  0.60f, CameraStatic),
            // --- the building
            new Entry("low_thump",        false, 0.90f, LowThump),
            new Entry("door_distant",     false, 0.90f, DoorDistant),
            new Entry("footstep_distant", false, 0.90f, FootstepDistant),
            new Entry("power_down",       false, 0.90f, PowerDown),
            new Entry("end_tone",         false, 0.80f, EndTone),
        };

        private static readonly Dictionary<string, Entry> Index = BuildIndex();
        private static readonly ReadOnlyCollection<string> IdList = BuildIdList();

        private static Dictionary<string, Entry> BuildIndex()
        {
            var d = new Dictionary<string, Entry>(Entries.Length, StringComparer.Ordinal);
            foreach (Entry e in Entries) d.Add(e.Id, e);
            return d;
        }

        private static ReadOnlyCollection<string> BuildIdList()
        {
            var ids = new string[Entries.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Entries[i].Id;
            return Array.AsReadOnly(ids);
        }

        private static Entry Find(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!Index.TryGetValue(id, out Entry e))
                throw new ArgumentException("Unknown sound id '" + id + "'.", nameof(id));
            return e;
        }

        private static ulong HashId(string id)
        {
            ulong h = 14695981039346656037UL; // FNV-1a (string.GetHashCode is randomised per process)
            foreach (char c in id) { h ^= c; h = unchecked(h * 1099511628211UL); }
            return h;
        }

        private static ulong Mix64(ulong z)
        {
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

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

        // ---------------- Buffers ----------------

        // Sample counts are computed in double: float intermediates may be evaluated at higher precision on some
        // runtimes (e.g. Mono without float32 mode), which would change buffer lengths by a sample.
        private static int Sec(float s) => (int)(s * (double)SampleRate + 0.5);

        private static int Ms(float ms) => (int)(ms * (SampleRate / 1000.0) + 0.5);

        private static float[] Buf(float seconds) => new float[Sec(seconds)];

        private static float[] WhiteNoise(int n, Rng r)
        {
            var b = new float[n];
            for (int i = 0; i < n; i++) b[i] = r.Signed();
            return b;
        }

        private static void Mix(float[] dst, float[] src, float gain)
        {
            int end = Math.Min(src.Length, dst.Length);
            for (int i = 0; i < end; i++) dst[i] += gain * src[i];
        }

        /// <summary>Adds src into a loop buffer starting at 'offset', wrapping around the loop end.</summary>
        private static void AddWrapped(float[] loop, float[] src, int offset, float gain = 1f)
        {
            int n = loop.Length;
            int p = ((offset % n) + n) % n;
            for (int i = 0; i < src.Length; i++)
            {
                loop[p] += gain * src[i];
                if (++p == n) p = 0;
            }
        }

        private static void Scale(float[] b, float g)
        {
            for (int i = 0; i < b.Length; i++) b[i] *= g;
        }

        // Seamless processing of loops. A stateful filter run over a loop must start in the state it will be in
        // when the loop wraps around, or the seam clicks. Every loop filter below therefore runs over "virtual"
        // indices j = -warmup .. n-1: the negative ones replay the end of the loop (the audio that precedes sample
        // 0 when looping) to warm the state up, and only j >= 0 writes output. In place is safe: sample i is read
        // before it is written, and the warm-up only reads. Written as plain loops (no delegates) so filters
        // inline on every runtime.

        /// <summary>Loop index for virtual index j in [-n, n).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Wrap(int j, int n) => j < 0 ? j + n : j;

        private static void LowPass(float[] b, float hz, float q = 0.7071f)
        {
            var f = new Svf(hz, q);
            for (int i = 0; i < b.Length; i++) b[i] = f.Lp(b[i]);
        }

        private static void LowPass4(float[] b, float hz)
        {
            var f = new Lp4(hz);
            for (int i = 0; i < b.Length; i++) b[i] = f.Process(b[i]);
        }

        private static void LowPass4Loop(float[] b, float hz)
        {
            var f = new Lp4(hz);
            int n = b.Length;
            for (int j = -Math.Min(n, Ms(30f)); j < n; j++)
            {
                int i = Wrap(j, n);
                float y = f.Process(b[i]);
                if (j >= 0) b[i] = y;
            }
        }

        /// <summary>One-shot reverb send: b = dry*b + wet*reverb(b).</summary>
        private static void AddReverb(float[] b, float size, float feedback, float damp, float wet,
                                      float dry = 1f, float preDelayMs = 0f, float wetLowPassHz = 0f)
        {
            var rev = new Reverb(size, feedback, damp);
            var lp = wetLowPassHz > 0f ? new OnePole(wetLowPassHz) : null;
            int pd = Ms(preDelayMs);
            var src = (float[])b.Clone();
            for (int i = 0; i < b.Length; i++)
            {
                float w = rev.Process(i >= pd ? src[i - pd] : 0f);
                if (lp != null) w = lp.Lp(w);
                b[i] = dry * src[i] + wet * w;
            }
        }

        /// <summary>Loop reverb send, processed circularly so the tail wraps into the loop start.</summary>
        private static void AddReverbLoop(float[] b, float size, float feedback, float damp, float wet,
                                          float dry, float warmupSec)
        {
            var rev = new Reverb(size, feedback, damp);
            int n = b.Length;
            for (int j = -Math.Min(n, Sec(warmupSec)); j < n; j++)
            {
                int i = Wrap(j, n);
                float x = b[i], w = rev.Process(x);
                if (j >= 0) b[i] = dry * x + wet * w;
            }
        }

        /// <summary>Small office at night: a few dull early reflections (desk, monitor, walls) + a faint diffuse tail.</summary>
        private static void AddRoom(float[] b, float early, float tail)
        {
            float[] tapMs = { 2.3f, 5.1f, 11.3f, 23.0f };
            float[] tapGain = { 0.25f, 0.15f, 0.08f, 0.05f };
            int n = b.Length;
            var er = new float[n];
            for (int k = 0; k < tapMs.Length; k++)
            {
                int d = Ms(tapMs[k]);
                float g = early * tapGain[k];
                for (int i = d; i < n; i++) er[i] += g * b[i - d];
            }
            var rev = new Reverb(0.4f, 0.72f, 0.6f);
            var lp = new Svf(3500f, 0.6f);
            for (int i = 0; i < n; i++) b[i] += lp.Lp(er[i] + tail * rev.Process(b[i]));
        }

        /// <summary>Flutter echo between parallel corridor walls.</summary>
        private static void AddFlutter(float[] b, float delayMs, int count, float gain, float decay, float lpHz)
        {
            int n = b.Length, d = Ms(delayMs);
            var src = (float[])b.Clone();
            var echoes = new float[n];
            float g = gain;
            for (int k = 1; k <= count; k++, g *= decay)
            {
                int off = d * k;
                for (int i = off; i < n; i++) echoes[i] += g * src[i - off];
            }
            var lp = new Svf(lpHz, 0.6f);
            for (int i = 0; i < n; i++) b[i] += lp.Lp(echoes[i]);
        }

        // ---------------- Modal impacts (switches, keys, relays, heels, doors) ----------------

        private readonly struct Mode
        {
            public readonly float Freq, TauMs, Gain;

            public Mode(float freq, float tauMs, float gain) { Freq = freq; TauMs = tauMs; Gain = gain; }
        }

        /// <summary>
        /// Excites a bank of resonant modes with a unit-area half-sine pulse (+ a short noise burst)
        /// and adds the ringing at sample 'at'. Wider pulses give duller, heavier impacts.
        /// </summary>
        private static void AddImpact(float[] dst, int at, float amp, Mode[] modes, Rng r,
                                      int pulseWidth = 4, float noise = 0.3f, float noiseTauMs = 0.6f,
                                      float pitch = 1f, float decay = 1f, float direct = 0f)
        {
            if (at < 0 || at >= dst.Length || amp == 0f) return;
            float longest = 0f;
            foreach (Mode m in modes) longest = Math.Max(longest, m.TauMs);
            int len = Math.Min(dst.Length - at, Ms(Math.Max(longest * decay, noiseTauMs) * 9f) + pulseWidth + 4);

            var ex = new float[len];
            float area = 0f;
            for (int j = 0; j < pulseWidth; j++) area += Sin01(0.5 * (j + 0.5) / pulseWidth);
            for (int j = 0; j < pulseWidth && j < len; j++) ex[j] = Sin01(0.5 * (j + 0.5) / pulseWidth) / area;
            if (noise > 0f)
            {
                float k = MathF.Exp(-1f / (noiseTauMs * 0.001f * Sr)), e = noise;
                int nl = Math.Min(len, Ms(noiseTauMs * 9f) + 1);
                for (int j = 0; j < nl; j++, e *= k) ex[j] += e * r.Signed();
            }

            foreach (Mode m in modes)
            {
                float f = m.Freq * pitch;
                if (f >= 0.45f * Sr) continue;
                float tauMs = m.TauMs * decay;
                var res = new Resonator(f, tauMs * 0.001f, m.Gain * amp);
                int end = Math.Min(len, Ms(tauMs * 9f) + pulseWidth + 4);
                for (int j = 0; j < end; j++) dst[at + j] += res.Process(ex[j]);
            }

            if (direct > 0f)
            {
                var lp = new Svf(7000f, 0.7071f);
                for (int j = 0; j < len; j++) dst[at + j] += direct * amp * lp.Lp(ex[j]);
            }
        }

        // ---------------- Tonal building blocks ----------------

        private readonly struct Partial
        {
            public readonly float Ratio, Amp, Tau, Detune;

            public Partial(float ratio, float amp, float tau, float detuneHz = 0f)
            {
                Ratio = ratio; Amp = amp; Tau = tau; Detune = detuneHz;
            }
        }

        /// <summary>
        /// Adds amp * exp(-t/tau) * sin(2*pi*hz*t) with a raised-cosine attack, using the exact two-pole
        /// recursion (a damped rotation) instead of a sin and an exp per sample.
        /// </summary>
        private static void AddDecaySine(float[] b, int start, float hz, float amp, float tau, float attack)
        {
            if (start < 0 || start >= b.Length || hz >= 0.45f * Sr) return;
            int end = Math.Min(b.Length, start + (int)(tau * 11.5f * Sr));
            int atk = Math.Max(1, (int)(attack * Sr));
            double w = 2.0 * Math.PI * hz / SampleRate, r = Math.Exp(-1.0 / (tau * SampleRate));
            double c = 2.0 * r * Math.Cos(w), r2 = r * r;
            double prev = 0.0, cur = amp * r * Math.Sin(w); // s[0] = 0, s[1]
            for (int i = start + 1, j = 1; i < end; i++, j++)
            {
                b[i] += j < atk ? (float)cur * Rise((float)j / atk) : (float)cur;
                double next = c * cur - r2 * prev;
                prev = cur;
                cur = next;
            }
        }

        /// <summary>
        /// A struck bell / glass / chime voice: decaying partials, each an exact damped-rotation recursion.
        /// All partials run in one pass so their independent recursions overlap in the CPU pipeline.
        /// </summary>
        private static void AddBell(float[] b, float startSec, float hz, float amp, Partial[] partials,
                                    float decayScale = 1f, float attack = 0.0015f)
        {
            int start = Sec(startSec), count = 0;
            if (start >= b.Length) return;
            // Longest decay first, so partials can retire from the end of the list as they die out.
            // A stable insertion sort: Array.Sort is unstable and differs between runtimes.
            var order = (Partial[])partials.Clone();
            for (int i = 1; i < order.Length; i++)
            {
                Partial p = order[i];
                int j = i - 1;
                for (; j >= 0 && order[j].Tau < p.Tau; j--) order[j + 1] = order[j];
                order[j + 1] = p;
            }
            var c = new double[order.Length];
            var r2 = new double[order.Length];
            var cur = new double[order.Length];
            var prev = new double[order.Length];
            var ends = new int[order.Length];
            foreach (Partial p in order)
            {
                double f = hz * p.Ratio + p.Detune, tau = p.Tau * decayScale;
                if (f > 11000.0) continue;
                double w = 2.0 * Math.PI * f / SampleRate, r = Math.Exp(-1.0 / (tau * SampleRate));
                c[count] = 2.0 * r * Math.Cos(w);
                r2[count] = r * r;
                cur[count] = amp * p.Amp * r * Math.Sin(w); // s[1]; s[0] = 0
                ends[count] = Math.Min(b.Length, start + (int)(tau * 11.5 * SampleRate));
                count++;
            }
            if (count == 0) return;
            int end = ends[0], atk = Math.Max(1, (int)(attack * Sr));
            for (int i = start + 1, j = 1; i < end; i++, j++)
            {
                while (count > 1 && i >= ends[count - 1]) count--;
                double sum = 0.0;
                for (int k = 0; k < count; k++)
                {
                    double y = cur[k];
                    sum += y;
                    cur[k] = c[k] * y - r2[k] * prev[k];
                    prev[k] = y;
                }
                b[i] += j < atk ? (float)sum * Rise((float)j / atk) : (float)sum;
            }
        }

        /// <summary>
        /// Held wavetable tone with two detuned voices: attack, optional decay while held, raised-cosine
        /// release, and an optional struck accent (extra level that decays in ~25 ms) at the onset.
        /// </summary>
        private static void AddTone(float[] b, Wavetable wt, float start, float dur, float hz, float amp,
                                    float attack, float release, float decayTau = 0f, float detuneCents = 0f,
                                    float accent = 0f)
        {
            int s0 = Sec(start), n = Sec(dur + release);
            float up = MathF.Pow(2f, detuneCents / 1200f), down = 1f / up;
            Phasor a = default, c = default;
            c.Phase = 0.37;
            for (int j = 0; j < n && s0 + j < b.Length; j++)
            {
                float t = j * Dt;
                float env = Rise(t / attack) * (1f + accent * Decay(t, 0.025f));
                if (decayTau > 0f) env *= Decay(t, decayTau);
                if (t > dur) env *= 1f - Rise((t - dur) / release);
                float v = 0.5f * (wt.At(a.Advance(hz * up)) + wt.At(c.Advance(hz * down)));
                b[s0 + j] += amp * env * v;
            }
        }

        // ---------------- Finishing ----------------

        private static void FadeIn(float[] b, int len)
        {
            len = Math.Min(len, b.Length);
            for (int i = 0; i < len; i++) b[i] *= Rise((float)i / len);
        }

        private static void FadeOut(float[] b, int len)
        {
            len = Math.Min(len, b.Length);
            int n = b.Length;
            for (int i = 0; i < len; i++) b[n - 1 - i] *= Rise((float)i / len);
        }

        /// <summary>Sum (or sum of squares) accumulated in float per 256-sample block and in double across blocks.</summary>
        private static double Sum(float[] b, bool squares)
        {
            double total = 0;
            for (int i0 = 0; i0 < b.Length; i0 += 256)
            {
                int end = Math.Min(b.Length, i0 + 256);
                float block = 0f;
                if (squares) for (int i = i0; i < end; i++) block += b[i] * b[i];
                else for (int i = i0; i < end; i++) block += b[i];
                total += block;
            }
            return total;
        }

        /// <summary>Removes the mean with a Hann-shaped correction, so the (already faded) ends stay at zero.</summary>
        private static void RemoveDcWindowed(float[] b)
        {
            int n = b.Length;
            if (n < 3) return;
            float c = (float)(Sum(b, false) / ((n - 1) * 0.5)); // the Hann window sums to (n - 1) / 2
            double phase = 0.0, step = 0.5 / (n - 1);
            for (int i = 0; i < n; i++, phase += step) b[i] -= c * Sq(SinPhase(phase));
        }

        private static void RemoveMean(float[] b)
        {
            float m = (float)(Sum(b, false) / b.Length);
            for (int i = 0; i < b.Length; i++) b[i] -= m;
        }

        private static float Rms(float[] b) => (float)Math.Sqrt(Sum(b, true) / b.Length);

        private static float Peak(float[] b)
        {
            float lo = 0f, hi = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float x = b[i];
                if (x > hi) hi = x;
                else if (x < lo) lo = x;
            }
            return Math.Max(hi, -lo);
        }

        /// <summary>
        /// Scales to the target RMS without letting the peak pass the ceiling. With softLimit, a transient that
        /// would overshoot is rounded off by a tanh knee above 0.5 instead of turning the whole sound down.
        /// </summary>
        private static void Normalize(float[] b, float rmsDb, bool softLimit)
        {
            float rms = Rms(b);
            if (rms <= 0f) return;
            float target = DbToGain(rmsDb);
            float g = target / rms;
            if (softLimit && Peak(b) * g > PeakCeiling)
            {
                const float knee = 0.5f, span = PeakCeiling - knee;
                for (int i = 0; i < b.Length; i++)
                {
                    float x = b[i] * g, a = MathF.Abs(x);
                    b[i] = a <= knee ? x : MathF.Sign(x) * (knee + span * SoftClip((a - knee) / span));
                }
                g = target / Rms(b);
            }
            float peak = Peak(b);
            if (peak * g > PeakCeiling) g = PeakCeiling / peak;
            Scale(b, g);
        }

        /// <summary>
        /// Rotates a (seamless, hence rotation-invariant) loop so it starts on the quietest near-zero sample of its
        /// first 10 ms: an AudioSource starting the loop cold then begins at ~0 instead of mid-waveform (no click),
        /// and rhythmic loops keep their phase to within a few ms.
        /// </summary>
        private static float[] StartAtZeroCrossing(float[] b)
        {
            int n = b.Length, best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Math.Min(n, Ms(10f)); i++)
            {
                float score = MathF.Abs(b[i]) + 0.5f * MathF.Abs(b[i] - b[(i + n - 1) % n]);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best == 0) return b;
            var rotated = new float[n];
            Array.Copy(b, best, rotated, 0, n - best);
            Array.Copy(b, 0, rotated, n - best, best);
            return rotated;
        }

        /// <summary>
        /// DC-block, de-click the ends, set the level (optionally rounding off overshooting transients), then the
        /// optional 4-pole "air" low-pass - after the limiter, so it also cleans up the limiter's own
        /// harmonics - zero the mean (the tanh knee is not perfectly symmetric) and re-trim the level.
        /// </summary>
        private static float[] FinishOneShot(float[] b, float rmsDb, float fadeInMs, float fadeOutMs,
                                             float airHz = 0f, bool softLimit = false, float hpHz = 18f)
        {
            var hp = new OnePole(hpHz);
            for (int i = 0; i < b.Length; i++) b[i] = hp.Hp(b[i]);
            FadeIn(b, Math.Max(2, Ms(fadeInMs)));
            FadeOut(b, Math.Max(2, Ms(fadeOutMs)));
            Normalize(b, rmsDb, softLimit);
            if (airHz > 0f)
            {
                var air = new Lp4(airHz);
                for (int i = 0; i < b.Length; i++) b[i] = air.Process(b[i]);
            }
            RemoveDcWindowed(b);
            Normalize(b, rmsDb, false);
            return b;
        }

        /// <summary>
        /// Loop version of <see cref="FinishOneShot"/>: optional circular high-pass / "air" low-pass (circular, so
        /// the seam stays intact), zero mean, level, start on a zero crossing. Loops are built from DC-free parts,
        /// so most skip the filters.
        /// </summary>
        private static float[] FinishLoop(float[] b, float rmsDb, float airHz = 0f, float hpHz = 0f)
        {
            if (airHz > 0f || hpHz > 0f)
            {
                OnePole hp = hpHz > 0f ? new OnePole(hpHz) : null;
                Lp4 air = airHz > 0f ? new Lp4(airHz) : null;
                int n = b.Length;
                for (int j = -Math.Min(n, Sec(0.5f)); j < n; j++)
                {
                    int i = Wrap(j, n);
                    float x = b[i];
                    if (hp != null) x = hp.Hp(x);
                    if (air != null) x = air.Process(x);
                    if (j >= 0) b[i] = x;
                }
            }
            RemoveMean(b);
            Normalize(b, rmsDb, false);
            return StartAtZeroCrossing(b);
        }

        // ====================================================================
        //  3. Sound designs
        // ====================================================================

        // ---------------- OS / UI: the player's own actions. Clean, dry, digital. ----------------

        /// <summary>Soft rounded "tk": a gliding sine tick over a warm body, no room at all.</summary>
        private static float[] UiClick(Rng r)
        {
            float[] b = Buf(0.034f);
            var edge = new Svf(3600f, 1.1f);
            Phasor tick = default, body = default, low = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float f = 1900f + 500f * Decay(t, 0.003f);
                b[i] = 0.90f * AttackDecay(t, 0.0004f, 0.0032f) * tick.Sin(f)
                     + 0.45f * AttackDecay(t, 0.0006f, 0.0060f) * body.Sin(1040f)
                     + 0.20f * AttackDecay(t, 0.0010f, 0.0085f) * low.Sin(430f)
                     + 0.28f * AttackDecay(t, 0.0002f, 0.0011f) * edge.Bp(r.Signed());
            }
            return FinishOneShot(b, -21f, 0.2f, 6f, 9000f);
        }

        /// <summary>Tiny, higher, lighter tick for list items.</summary>
        private static float[] UiSelect(Rng r)
        {
            float[] b = Buf(0.016f);
            var edge = new Svf(5000f, 1.4f);
            Phasor a = default, c = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                b[i] = 0.85f * AttackDecay(t, 0.0003f, 0.0021f) * a.Sin(3050f)
                     + 0.35f * AttackDecay(t, 0.0004f, 0.0034f) * c.Sin(1525f)
                     + 0.18f * AttackDecay(t, 0.0001f, 0.0006f) * edge.Bp(r.Signed());
            }
            return FinishOneShot(b, -23f, 0.2f, 4f, 9000f);
        }

        /// <summary>Rising band of soft air that lands on a small tick.</summary>
        private static float[] UiWindow(Rng r)
        {
            float[] b = Buf(0.095f);
            var air = new Svf(800f, 1.3f);
            var body = new Svf(380f, 0.8f);
            var soft = new Lp4(5000f);
            Phasor t1 = default, t2 = default;
            const float swellEnd = 0.052f, tickAt = 0.056f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                if ((i & 15) == 0) air.Set(800f * MathF.Pow(3.2f, Clamp01(t / swellEnd)), 1.3f);
                float env = t < swellEnd ? Sq(SinUnit(0.25f * t / swellEnd)) : Decay(t - swellEnd, 0.010f);
                float w = r.Signed();
                float s = soft.Process(env * (0.75f * air.Bp(w) + 0.3f * body.Bp(w)));
                float tt = t - tickAt;
                if (tt >= 0f)
                    s += 0.5f * AttackDecay(tt, 0.0003f, 0.0028f) * t1.Sin(2250f)
                       + 0.28f * AttackDecay(tt, 0.0005f, 0.005f) * t2.Sin(1125f);
                b[i] = s;
            }
            return FinishOneShot(b, -23f, 0.5f, 10f, 9000f);
        }

        private static readonly Partial[] SoftBell =
        {
            new Partial(1.00f, 1.00f, 0.24f),
            new Partial(1.00f, 0.20f, 0.30f, 1.1f),   // slow shimmer
            new Partial(2.00f, 0.20f, 0.12f),
            new Partial(3.00f, 0.07f, 0.07f),
            new Partial(4.20f, 0.05f, 0.035f),        // glassy glint
        };

        /// <summary>Rising fifth, then a gentle fall to the major third: A5 - E6 - C#6.</summary>
        private static float[] NotifyMail(Rng r)
        {
            float[] b = Buf(0.68f);
            AddBell(b, 0.000f, 880.00f, 0.80f, SoftBell, 0.9f);
            AddBell(b, 0.085f, 1318.51f, 0.70f, SoftBell, 0.9f);
            AddBell(b, 0.170f, 1108.73f, 0.62f, SoftBell, 1.5f);
            AddReverb(b, 0.55f, 0.78f, 0.45f, 0.35f);
            return FinishOneShot(b, -20f, 0.5f, 120f);
        }

        /// <summary>Two struck tritone dyads a semitone apart ("da-dum"), soft-square timbre, beating voices.</summary>
        private static float[] SysError(Rng r)
        {
            float[] b = Buf(0.42f);
            var wt = new Wavetable(new[] { 1f, 0.10f, 0.30f, 0f, 0.12f, 0f, 0.05f, 0f, 0.02f });
            AddTone(b, wt, 0.000f, 0.135f, 523.25f, 0.50f, 0.003f, 0.018f, 0.25f, 7f, 0.8f);
            AddTone(b, wt, 0.000f, 0.135f, 739.99f, 0.40f, 0.003f, 0.018f, 0.25f, 7f, 0.8f);
            AddTone(b, wt, 0.140f, 0.200f, 493.88f, 0.55f, 0.003f, 0.070f, 0.16f, 9f, 0.9f);
            AddTone(b, wt, 0.140f, 0.200f, 698.46f, 0.45f, 0.003f, 0.070f, 0.16f, 9f, 0.9f);
            LowPass(b, 5000f);
            AddReverb(b, 0.45f, 0.75f, 0.5f, 0.25f);
            return FinishOneShot(b, -19f, 0.5f, 25f);
        }

        private static readonly Partial[] AlertBell =
        {
            new Partial(1.00f, 1.00f, 0.30f),
            new Partial(1.00f, 0.18f, 0.34f, 1.6f),
            new Partial(2.00f, 0.25f, 0.16f),
            new Partial(2.76f, 0.18f, 0.09f),
            new Partial(5.40f, 0.06f, 0.03f),
        };

        /// <summary>A minor-third dyad struck like a small alarm bell, with a warm body an octave down.</summary>
        private static float[] SysWarning(Rng r)
        {
            float[] b = Buf(0.58f);
            AddBell(b, 0.000f, 1174.66f, 0.55f, AlertBell, 1.0f, 0.001f);
            AddBell(b, 0.004f, 1396.91f, 0.45f, AlertBell, 0.9f, 0.001f);
            AddDecaySine(b, 0, 587.33f, 0.22f, 0.2f, 0.002f);
            AddReverb(b, 0.5f, 0.78f, 0.45f, 0.3f);
            return FinishOneShot(b, -20f, 0.3f, 60f);
        }

        private static readonly Partial[] GlassBell =
        {
            new Partial(1.00f, 1.00f, 1.30f),
            new Partial(1.00f, 0.30f, 1.10f, 1.2f),
            new Partial(2.00f, 0.28f, 0.70f),
            new Partial(3.00f, 0.10f, 0.35f),
            new Partial(4.24f, 0.08f, 0.18f),
            new Partial(5.87f, 0.04f, 0.10f),
        };

        /// <summary>
        /// NEXUS OS start-up: a warm pad swells on G(maj7), resolves to D(add9) (plagal, hopeful), while a
        /// glass arpeggio climbs D5-A5-B5-F#6 and sighs E6 -> D6 over the resolution (the melancholy).
        /// </summary>
        private static float[] SysStartup(Rng r)
        {
            const float len = 4.4f;
            float[] pad = Buf(len);
            var wt = new Wavetable(SawAmps(28, 1.15f));
            AddPadChord(pad, wt, new[] { 98.00f, 146.83f, 246.94f, 369.99f }, 0.00f, 1.00f, 1.75f, 0.95f, r);
            AddPadChord(pad, wt, new[] { 73.42f, 110.00f, 185.00f, 329.63f, 440.00f }, 1.55f, 0.80f, 3.00f, 1.30f, r);
            var lp = new Svf(300f, 0.8f);
            for (int i = 0; i < pad.Length; i++)
            {
                if ((i & 31) == 0)
                {
                    float t = i * Dt;
                    lp.Set(300f + 1500f * SmoothStep(0f, 1.5f, t) - 600f * SmoothStep(2.0f, 4.2f, t), 0.8f);
                }
                pad[i] = lp.Lp(pad[i]);
            }

            float[] b = Buf(len);
            Mix(b, pad, 0.55f);
            AddBell(b, 0.35f, 587.33f, 0.45f, GlassBell);
            AddBell(b, 0.52f, 880.00f, 0.42f, GlassBell);
            AddBell(b, 0.69f, 987.77f, 0.40f, GlassBell);
            AddBell(b, 0.86f, 1479.98f, 0.34f, GlassBell, 1.1f);
            AddBell(b, 1.58f, 1318.51f, 0.40f, GlassBell, 1.2f);
            AddBell(b, 2.08f, 1174.66f, 0.46f, GlassBell, 1.6f);
            AddBell(b, 2.10f, 1760.00f, 0.10f, GlassBell, 1.2f);
            AddReverb(b, 1.25f, 0.86f, 0.35f, 0.8f);
            return FinishOneShot(b, -20f, 2f, 450f);
        }

        /// <summary>Pad chord: every note is two saw voices +/-6 cents apart, sharing one sin^2 swell / cos^2 release.</summary>
        private static void AddPadChord(float[] b, Wavetable wt, float[] notes, float start, float attack,
                                        float holdEnd, float release, Rng r)
        {
            int s0 = Sec(start), s1 = Math.Min(b.Length, Sec(holdEnd + release));
            var env = new float[s1 - s0];
            for (int j = 0; j < env.Length; j++)
            {
                float t = j * Dt, tr = t + start - holdEnd;
                env[j] = Sq(SinUnit(0.25f * Math.Min(1f, t / attack)));
                if (tr > 0f) env[j] *= Sq(SinUnit(0.25f + 0.25f * Math.Min(1f, tr / release)));
            }
            float per = 0.5f / MathF.Sqrt(notes.Length);
            foreach (float f in notes)
            {
                float gain = per * (f < 150f ? 1.2f : 1f), hzUp = f * 1.0035f, hzDown = f * 0.9965f;
                Phasor up = default, down = default;
                up.Phase = r.Float();
                down.Phase = r.Float();
                for (int j = 0; j < env.Length; j++)
                    b[s0 + j] += gain * env[j] * (wt.At(up.Advance(hzUp)) + wt.At(down.Advance(hzDown)));
            }
        }

        // ---------------- Physical input devices ----------------
        // The second cursor is heard through these: real plastic, in the real room.

        private static readonly Mode[] MouseSwitchModes =
        {
            new Mode(240f, 12f, 0.10f),   // desk / shell thump
            new Mode(610f, 9f, 0.20f),    // button plastic
            new Mode(1470f, 7f, 0.42f),   // shell cavity
            new Mode(2380f, 5f, 0.70f),
            new Mode(3870f, 3.4f, 1.00f), // micro-switch leaf snap
            new Mode(5520f, 2.2f, 0.65f),
            new Mode(7350f, 1.4f, 0.32f),
        };

        private static readonly Mode[] MouseReleaseModes =
        {
            new Mode(700f, 6f, 0.10f),
            new Mode(1760f, 5f, 0.35f),
            new Mode(2900f, 3.5f, 0.70f),
            new Mode(4450f, 2.4f, 1.00f),
            new Mode(6300f, 1.6f, 0.60f),
            new Mode(8100f, 1.0f, 0.25f),
        };

        /// <summary>Omron-style micro-switch: leaf snap, contact settle and plunger bottoming, then desk and walls.</summary>
        private static float[] MouseClick(Rng r)
        {
            float[] b = Buf(0.11f);
            float p = r.Jitter(0.02f);
            AddImpact(b, 0, 1.00f, MouseSwitchModes, r, 4, 0.30f, 0.5f, p, 1f, 0.25f);
            AddImpact(b, Ms(0.85f), 0.40f, MouseSwitchModes, r, 5, 0.20f, 0.4f, p * 1.04f, 0.8f);
            AddImpact(b, Ms(2.6f), 0.26f, MouseSwitchModes, r, 8, 0.15f, 0.6f, p * 0.93f, 1.1f);
            AddRoom(b, 1f, 0.09f);
            return FinishOneShot(b, -21f, 0.1f, 30f, 9500f, true);
        }

        /// <summary>The spring returning: lighter, higher, crisper, same room.</summary>
        private static float[] MouseRelease(Rng r)
        {
            float[] b = Buf(0.09f);
            float p = r.Jitter(0.02f);
            AddImpact(b, 0, 0.70f, MouseReleaseModes, r, 4, 0.30f, 0.4f, p, 0.8f, 0.2f);
            AddImpact(b, Ms(0.6f), 0.30f, MouseReleaseModes, r, 4, 0.20f, 0.3f, p * 1.05f, 0.6f);
            AddRoom(b, 1f, 0.07f);
            return FinishOneShot(b, -24f, 0.1f, 25f, 10000f, true);
        }

        /// <summary>90s office keyboard: keycap bottoming out on the plate, a small rock, then a faint upstroke.</summary>
        private static float[] KeyTap(Rng r)
        {
            float[] b = Buf(0.07f);
            float pitch = r.Range(0.93f, 1.07f), bright = r.Range(0.8f, 1.2f);
            float body = r.Range(0.75f, 1.3f), decay = r.Range(0.85f, 1.2f);
            Mode[] modes =
            {
                new Mode(290f * r.Jitter(0.06f), 14f, 0.40f * body),  // case / desk
                new Mode(1230f * r.Jitter(0.04f), 6f, 0.55f),          // keycap
                new Mode(2080f * r.Jitter(0.04f), 4.5f, 0.80f * bright),
                new Mode(3240f * r.Jitter(0.04f), 3f, 0.60f * bright),
                new Mode(4650f * r.Jitter(0.05f), 2f, 0.35f * bright),
                new Mode(6100f * r.Jitter(0.05f), 1.3f, 0.18f * bright),
            };
            AddImpact(b, 0, 1f, modes, r, r.Int(6, 11), r.Range(0.25f, 0.45f), 1.5f, pitch, decay, 0.15f);
            AddImpact(b, Ms(r.Range(1.4f, 3.2f)), r.Range(0.22f, 0.38f), modes, r, 8, 0.2f, 1f, pitch * 1.03f, decay * 0.8f);
            AddImpact(b, Ms(r.Range(36f, 46f)), r.Range(0.07f, 0.12f), modes, r, 6, 0.25f, 0.8f, pitch * 1.15f, 0.6f);
            return FinishOneShot(b, -22f + r.Range(-1.2f, 1.2f), 0.1f, 8f, 9000f, true);
        }

        private static readonly Mode[] StabilizerModes =
        {
            new Mode(3150f, 25f, 0.06f),
            new Mode(4720f, 18f, 0.04f),
            new Mode(2230f, 30f, 0.03f),
        };

        /// <summary>Long bar: both ends land a few ms apart, lower modes, a faint stabiliser-wire ring.</summary>
        private static float[] KeySpace(Rng r)
        {
            float[] b = Buf(0.115f);
            float pitch = r.Range(0.95f, 1.05f);
            Mode[] modes =
            {
                new Mode(170f * r.Jitter(0.05f), 18f, 0.50f),
                new Mode(520f * r.Jitter(0.04f), 10f, 0.40f),
                new Mode(980f * r.Jitter(0.04f), 7f, 0.60f),
                new Mode(1650f * r.Jitter(0.04f), 5f, 0.70f),
                new Mode(2600f * r.Jitter(0.04f), 3.5f, 0.50f),
                new Mode(3900f * r.Jitter(0.05f), 2f, 0.25f),
            };
            int second = Ms(r.Range(2.5f, 4f));
            AddImpact(b, 0, 1f, modes, r, 10, 0.35f, 1.8f, pitch, 1f, 0.12f);
            AddImpact(b, second, 0.7f, modes, r, 9, 0.3f, 1.5f, pitch * 1.02f);
            AddImpact(b, second, 1f, StabilizerModes, r, 3, 0.2f, 0.4f, pitch);
            int up = Ms(r.Range(64f, 74f));
            AddImpact(b, up, 0.12f, modes, r, 6, 0.3f, 0.8f, pitch * 1.12f, 0.6f);
            AddImpact(b, up, 0.4f, StabilizerModes, r, 3, 0.1f, 0.3f, pitch);
            return FinishOneShot(b, -21f, 0.1f, 10f, 9000f, true);
        }

        /// <summary>Big key, heavier strike: wider pulse, stronger low body, a hint of stabiliser.</summary>
        private static float[] KeyEnter(Rng r)
        {
            float[] b = Buf(0.11f);
            float pitch = r.Range(0.95f, 1.05f);
            Mode[] modes =
            {
                new Mode(210f * r.Jitter(0.05f), 16f, 0.60f),
                new Mode(760f * r.Jitter(0.04f), 9f, 0.50f),
                new Mode(1380f * r.Jitter(0.04f), 6f, 0.70f),
                new Mode(2250f * r.Jitter(0.04f), 4f, 0.60f),
                new Mode(3500f * r.Jitter(0.05f), 2.5f, 0.30f),
            };
            AddImpact(b, 0, 1.25f, modes, r, 11, 0.4f, 1.8f, pitch, 1f, 0.12f);
            AddImpact(b, Ms(r.Range(1.8f, 2.8f)), 0.45f, modes, r, 9, 0.25f, 1.2f, pitch * 1.02f, 0.8f);
            AddImpact(b, Ms(1.5f), 0.5f, StabilizerModes, r, 3, 0.15f, 0.4f, pitch * 1.1f);
            AddImpact(b, Ms(r.Range(56f, 64f)), 0.12f, modes, r, 6, 0.3f, 0.8f, pitch * 1.12f, 0.6f);
            return FinishOneShot(b, -20f, 0.1f, 10f, 9000f, true);
        }

        // ---------------- Hardware ----------------

        /// <summary>PIT-driven 1 kHz pulse through a tiny PC speaker (resonant ~3 kHz, dull above 5 kHz).</summary>
        private static float[] BiosBeep(Rng r)
        {
            float[] b = Buf(0.17f);
            const float f0 = 1000f, duty = 0.47f;
            var amps = new float[11];
            var phases = new float[11];
            for (int h = 0; h < amps.Length; h++)
            {
                int n = h + 1;
                float f = n * f0;
                float speaker = 1f / (1f + MathF.Pow(f / 5500f, 4f)) * (1f + 0.6f * MathF.Exp(-Sq((f - 3000f) / 900f)));
                amps[h] = 2f / (n * MathF.PI) * MathF.Sin(n * MathF.PI * duty) * speaker;
                phases[h] = 0.25f;
            }
            var wt = new Wavetable(amps, phases);
            Phasor p = default;
            int on = Sec(0.150f), atk = Ms(2.5f), rel = Ms(6f);
            for (int i = 0; i < on; i++)
                b[i] = Rise((float)i / atk) * Rise((float)(on - i) / rel) * wt.At(p.Advance(f0));
            return FinishOneShot(b, -21f, 0.1f, 1f);
        }

        private static readonly Mode[] HddClickModes =
        {
            new Mode(170f, 6f, 0.25f),
            new Mode(940f, 3f, 0.45f),
            new Mode(1880f, 2.4f, 0.75f),
            new Mode(3050f, 1.8f, 0.60f),
            new Mode(4500f, 1.1f, 0.30f),
        };

        /// <summary>Voice-coil actuator chatter in three clusters, each with a buzzing whirr, over a faint spindle.</summary>
        private static float[] HddSeek(Rng r)
        {
            const float len = 0.42f;
            float[] b = Buf(len);
            float[][] clusters =
            {
                new[] { 0.000f, 0.017f, 0.031f, 0.056f },
                new[] { 0.140f, 0.151f, 0.160f, 0.176f, 0.195f, 0.213f },
                new[] { 0.292f, 0.312f, 0.339f },
            };
            var whirrEnv = new float[b.Length];
            foreach (float[] c in clusters)
            {
                foreach (float t0 in c)
                {
                    float t = Math.Max(0f, t0 + r.Range(-0.0025f, 0.0025f));
                    AddImpact(b, Sec(t), r.Range(0.5f, 1f), HddClickModes, r, 4, 0.35f, 0.3f, r.Jitter(0.05f));
                }
                float a = Math.Max(0f, c[0] - 0.004f), z = c[c.Length - 1] + 0.016f;
                for (int i = Sec(a); i < Math.Min(b.Length, Sec(z)); i++)
                    whirrEnv[i] = Bump(i * Dt, 0.5f * (a + z), 0.5f * (z - a));
            }
            var buzz = new Svf(780f, 2.5f);
            Phasor gate = default, s1 = default, s2 = default, whine = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float g = Sq(0.5f + 0.5f * gate.Sin(140f));
                float spindle = SmoothStep(0f, 0.04f, t) * (1f - SmoothStep(len - 0.06f, len, t));
                b[i] += 0.12f * whirrEnv[i] * g * buzz.Bp(r.Signed())
                      + spindle * (0.018f * s1.Sin(90f) + 0.010f * s2.Sin(180f) + 0.003f * whine.Sin(2710f));
            }
            return FinishOneShot(b, -26f, 0.1f, 20f, 8000f, true);
        }

        /// <summary>Motor kick, spindle whine rising exponentially to 5400 rpm, head-load clicks as it settles.</summary>
        private static float[] HddSpinup(Rng r)
        {
            const float len = 2.1f;
            float[] b = Buf(len);
            var air = new Svf(900f, 0.7f);
            Phasor p1 = default, p2 = default, p3 = default, w1 = default, w2 = default, w3 = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float spin = (1f - MathF.Exp(-t / 0.5f)) * SmoothStep(0f, 0.12f, t);
                float f = 12f + 78f * spin;
                float speed = f / 90f, s2 = speed * speed;
                float hum = 0.35f * p1.Sin(f) + 0.20f * p2.Sin(2f * f) + 0.10f * p3.Sin(3f * f);
                float whine = 0.10f * s2 * w1.Sin(8f * f) + 0.05f * s2 * w2.Sin(24f * f) + 0.02f * s2 * speed * w3.Sin(41f * f);
                float fade = SmoothStep(0f, 0.02f, t) * (1f - SmoothStep(1.8f, len, t));
                b[i] = fade * (speed * hum + whine + 0.06f * speed * air.Lp(r.Signed()));
            }
            Mode[] kick = { new Mode(120f, 25f, 0.8f), new Mode(380f, 12f, 0.4f), new Mode(1300f, 4f, 0.2f) };
            AddImpact(b, Ms(15f), 0.8f, kick, r, 24, 0.2f, 2f);
            AddImpact(b, Sec(1.42f), 0.45f, HddClickModes, r, 4, 0.35f, 0.3f);
            AddImpact(b, Sec(1.55f), 0.35f, HddClickModes, r, 4, 0.35f, 0.3f, 1.05f);
            AddImpact(b, Sec(1.61f), 0.40f, HddClickModes, r, 4, 0.35f, 0.3f, 0.97f);
            return FinishOneShot(b, -27f, 1f, 250f, 8000f);
        }

        private static readonly Mode[] RelayModes =
        {
            new Mode(330f, 8f, 0.30f),
            new Mode(1280f, 5f, 0.40f),
            new Mode(2600f, 3f, 0.70f),
            new Mode(4100f, 2f, 0.60f),
            new Mode(6300f, 1.2f, 0.30f),
        };

        /// <summary>60 Hz mains hum with a random-phase harmonic series (transformer / degauss coil).</summary>
        private static Wavetable MainsHum(Rng r, float brightness)
        {
            float[] amps = { 1f, 0.85f, 0.6f, 0.45f, 0.3f, 0.25f, 0.12f, 0.15f, 0.08f, 0.10f, 0.05f, 0.06f };
            for (int h = 0; h < amps.Length; h++) amps[h] *= MathF.Pow(brightness, h);
            return new Wavetable(amps, RandomPhases(amps.Length, r));
        }

        /// <summary>Relay click, low thump, the degauss "thoom" swelling and dying, static crackles, the flyback settling.</summary>
        private static float[] CrtOn(Rng r)
        {
            const float len = 1.25f;
            float[] b = Buf(len);
            AddImpact(b, 0, 0.9f, RelayModes, r, 4, 0.3f, 0.4f);
            AddImpact(b, Ms(1.8f), 0.45f, RelayModes, r, 5, 0.2f, 0.3f, 1.05f);

            Wavetable hum = MainsHum(r, 1f);
            var hiss = new Lp4(6000f);
            var hissHp = new OnePole(1500f);
            Phasor thump = default, dg = default, wob = default, whine = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float tt = t - 0.012f;
                if (tt >= 0f) b[i] += 0.8f * AttackDecay(tt, 0.004f, 0.09f) * thump.Sin(42f + 48f * Decay(tt, 0.05f));
                float td = t - 0.02f;
                if (td >= 0f)
                {
                    float rattle = 1f + 0.12f * wob.Sin(7.5f);
                    b[i] += 0.55f * AttackDecay(td, 0.08f, 0.26f) * SoftClip(1.6f * rattle * hum.At(dg.Advance(60f)));
                }
                b[i] += 0.035f * AttackDecay(t - 0.08f, 0.1f, 0.25f) * hissHp.Hp(hiss.Process(r.Signed()));
                float tw = t - 0.12f;
                if (tw >= 0f)
                    b[i] += 0.012f * SmoothStep(0f, 0.25f, tw) * whine.Sin(7860f - 900f * Decay(tw, 0.12f));
            }
            Mode[] crackle = { new Mode(2900f, 0.8f, 0.5f), new Mode(5200f, 0.5f, 0.4f), new Mode(1700f, 1.2f, 0.3f) };
            for (int k = 0; k < 6; k++)
                AddImpact(b, Sec(r.Range(0.08f, 0.7f)), r.Range(0.1f, 0.3f), crackle, r, 3, 0.8f, 0.3f, r.Jitter(0.2f));
            return FinishOneShot(b, -22f, 0.1f, 180f, 10000f, true);
        }

        private static readonly Mode[] PowerButtonModes =
        {
            new Mode(180f, 12f, 0.40f),
            new Mode(1100f, 6f, 0.50f),
            new Mode(2200f, 4f, 0.70f),
            new Mode(3400f, 2.5f, 0.50f),
            new Mode(5100f, 1.5f, 0.25f),
        };

        /// <summary>Button clack, hum collapsing, the flyback whistle falling away, a last thoop, discharge ticks.</summary>
        private static float[] CrtOff(Rng r)
        {
            const float len = 1.5f;
            float[] b = Buf(len);
            AddImpact(b, 0, 1f, PowerButtonModes, r, 7, 0.35f, 0.8f);

            Wavetable hum = MainsHum(r, 0.9f);
            var band = new Svf(3000f, 1.2f);
            Phasor hp = default, w1 = default, w2 = default, th = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                b[i] += 0.25f * (t < 0.004f ? 1f : Decay(t - 0.004f, 0.045f)) * hum.At(hp.Advance(60f));
                float tw = t - 0.01f;
                if (tw >= 0f)
                {
                    float f = 120f + 4300f * MathF.Exp(-tw / 0.28f);
                    b[i] += 0.12f * AttackDecay(tw, 0.015f, 0.38f) * (w1.Sin(f) + 0.3f * w2.Sin(1.5f * f));
                    if ((i & 31) == 0) band.Set(200f + 2800f * MathF.Exp(-t / 0.3f), 1.2f);
                    b[i] += 0.05f * AttackDecay(tw, 0.01f, 0.35f) * band.Bp(r.Signed());
                }
                float tt = t - 0.015f;
                if (tt >= 0f) b[i] += 0.5f * AttackDecay(tt, 0.004f, 0.12f) * th.Sin(35f + 40f * Decay(tt, 0.06f));
            }
            Mode[] tick = { new Mode(2600f, 0.7f, 0.5f), new Mode(4700f, 0.5f, 0.3f) };
            for (int k = 0; k < 4; k++)
                AddImpact(b, Sec(r.Range(0.25f, 0.9f)), r.Range(0.05f, 0.15f), tick, r, 3, 0.6f, 0.3f, r.Jitter(0.2f));
            return FinishOneShot(b, -24f, 3f, 150f, 9000f, true);
        }

        /// <summary>
        /// 1 s loop, eight grind strokes (swung, accented, two with stutter re-triggers): bit-crushed band-passed
        /// crunch, a comb-filtered grinding bed and a 55 Hz motor riding the stroke envelope.
        /// The loop point sits in the gap just before a stroke.
        /// </summary>
        private static float[] ShredLoop(Rng r)
        {
            int n = Sec(1f);
            const int strokes = 8;
            float[] accents = { 0.95f, 0.55f, 0.8f, 0.5f, 1.0f, 0.6f, 0.85f, 0.65f };
            float[] b = new float[n];
            float[] env = new float[n];
            for (int k = 0; k < strokes; k++)
            {
                int at = Ms(8f) + k * n / strokes + (k % 2 == 1 ? Ms(9f) : 0);
                bool stutter = k == 3 || k == 7;
                int len = Ms(115f);
                var ev = new float[len];
                var bp = new Svf(r.Range(1500f, 2600f), 0.9f);
                var bp2 = new Svf(r.Range(400f, 700f), 1.2f);
                int hold = r.Int(2, 5);
                float levels = r.Range(10f, 28f), held = 0f;
                for (int j = 0; j < len; j++)
                {
                    float t = j * Dt;
                    float e = AttackDecay(t, 0.0015f, 0.032f);
                    if (stutter) e += 0.7f * AttackDecay(t - 0.014f, 0.001f, 0.01f) + 0.5f * AttackDecay(t - 0.028f, 0.001f, 0.012f);
                    float x = r.Signed();
                    float s = e * (0.8f * bp.Bp(x) + 0.5f * bp2.Bp(x));
                    if (j % hold == 0) held = Crush(1.5f * s, levels);
                    ev[j] = held;
                    env[(at + j) % n] += e * accents[k];
                }
                AddWrapped(b, ev, at, accents[k]);
                var tick = new float[Ms(12f)];
                AddDecaySine(tick, 0, 3200f, 0.15f * accents[k], 0.002f, 0.0003f);
                AddWrapped(b, tick, at);
            }

            // grinding bed: comb-filtered noise (pitched ~110 Hz), and a 55 Hz motor through a low-pass
            float[] grind = WhiteNoise(n, r), motor = new float[n];
            var wt = new Wavetable(SawAmps(18, 1f));
            var motorOsc = new LoopOsc(55f, n, r.Float());
            for (int i = 0; i < n; i++) motor[i] = wt.At(motorOsc.Next());
            var comb = new float[Sec(1f / 110f)];
            var gbp = new Svf(1000f, 0.8f);
            var mlp = new Svf(700f, 0.9f);
            for (int j = -Ms(200f), cp = 0; j < n; j++)
            {
                int i = Wrap(j, n);
                float y = comb[cp];
                comb[cp] = grind[i] + 0.72f * y;
                if (++cp == comb.Length) cp = 0;
                float g = gbp.Bp(y), m = mlp.Lp(motor[i]);
                if (j < 0) continue;
                float e = Math.Min(1f, env[i]);
                b[i] += 0.25f * e * g + 0.22f * (0.35f + 0.65f * e) * m;
            }
            return FinishLoop(b, -22f, 7500f);
        }

        // ---------------- Ambience ----------------

        /// <summary>
        /// Night office, 8 s: PC fan (pinkish air, 280 Hz blade-pass tone with a slow wobble, 40 Hz motor),
        /// a trace of 120 Hz mains, and distant HVAC rumble breathing once per loop. All tones are harmonics
        /// of 20 Hz, so they come from one tiled 2205-sample cycle.
        /// </summary>
        private static float[] AmbRoom(Rng r)
        {
            int n = Sec(8f);
            const int period = SampleRate / 20;
            float[] blade = HarmonicCycle(period, new[] { 14, 28 }, new[] { 0.06f, 0.025f }, r);
            float[] steady = HarmonicCycle(period, new[] { 2, 6 }, new[] { 0.03f, 0.012f }, r);
            float[] fan = WhiteNoise(n, r);
            float[] hvac = WhiteNoise(n, r);
            var pink = new PinkFilter();
            var fanLp = new Svf(1600f, 0.6f);
            var fanHp = new OnePole(70f);
            var hlp = new OnePole(170f);
            float brown = 0f, wobble = 1f, swell = 1f;
            float[] b = new float[n];
            for (int j = -Sec(0.5f); j < n; j++)
            {
                int i = Wrap(j, n);
                if ((i & 63) == 0)
                {
                    double u = i / (double)n;
                    wobble = 1f + 0.18f * Sin01(3 * u) + 0.08f * Sin01(9 * u + 0.3);
                    swell = 1f + 0.12f * Sin01(u);
                }
                brown = 0.997f * brown + 0.03f * hvac[i];
                float air = fanHp.Hp(fanLp.Lp(pink.Process(fan[i]))), rumble = hlp.Lp(brown);
                if (j < 0) continue;
                int c = i % period;
                b[i] = 0.5f * air + 1.1f * swell * rumble + wobble * blade[c] + steady[c];
            }
            return FinishLoop(b, -36f, 0f, 18f);
        }

        /// <summary>
        /// Ballast buzz: one 60 Hz period of a 120 Hz-dominant harmonic series with a ~2.3 kHz "buzz" formant,
        /// tiled; slow flutter, two flicker dips and discharge hiss pulsing at 120 Hz.
        /// </summary>
        private static float[] AmbFluorescent(Rng r)
        {
            int n = Sec(4f);
            const int period = MainsPeriod;
            var harmonics = new int[100];
            var amps = new float[100];
            for (int h = 1; h <= 100; h++)
            {
                float f = 60f * h;
                float a = h % 2 == 0 ? MathF.Pow(h / 2f, -0.85f) : 0.18f * MathF.Pow(h, -0.7f);
                a *= 1f + 1.8f * MathF.Exp(-Sq((f - 2300f) / 1400f));
                if (f > 4000f) a *= MathF.Exp(-(f - 4000f) / 1200f);
                harmonics[h - 1] = h;
                amps[h - 1] = a;
            }
            float[] cycle = HarmonicCycle(period, harmonics, amps, r);

            float[] hiss = WhiteNoise(n, r);
            var hbp = new Svf(3200f, 0.8f);
            var hlp = new Lp4(6500f);
            for (int j = -Ms(20f); j < n; j++)
            {
                int i = Wrap(j, n);
                float y = hlp.Process(hbp.Bp(hiss[i]));
                if (j >= 0) hiss[i] = y;
            }

            var gate = new float[period]; // the arc re-ignites twice per mains cycle: hiss pulses at 120 Hz
            for (int i = 0; i < period; i++) gate[i] = 0.35f * Sq(Sq(Sin01((double)i / period)));
            float[] b = new float[n];
            float flutter = 1f;
            for (int i = 0, c = 0; i < n; i++)
            {
                if ((i & 15) == 0)
                {
                    float t = i * Dt;
                    double u = t / 4.0;
                    flutter = 1f + 0.025f * Sin01(29 * u) + 0.018f * Sin01(46 * u) + 0.04f * Sin01(2 * u)
                            - 0.30f * Bump(t, 2.70f, 0.035f) - 0.12f * Bump(t, 0.90f, 0.015f);
                }
                b[i] = flutter * (cycle[c] + gate[c] * hiss[i]);
                if (++c == period) c = 0;
            }
            return FinishLoop(b, -40f);
        }

        /// <summary>Transformer hum (tiled 60 Hz period), a very faint wavering 7.9 kHz whine, and a little sizzle.</summary>
        private static float[] AmbCrtHum(Rng r)
        {
            int n = Sec(3f);
            const int period = MainsPeriod;
            float[] cycle = HarmonicCycle(period, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 },
                new[] { 1f, 0.55f, 0.35f, 0.22f, 0.14f, 0.10f, 0.06f, 0.05f, 0.03f, 0.03f, 0.02f, 0.02f }, r);
            float[] sizzle = WhiteNoise(n, r);
            var sbp = new Svf(4500f, 0.7f);
            var slp = new Lp4(7000f);
            for (int j = -Ms(20f); j < n; j++)
            {
                int i = Wrap(j, n);
                float y = slp.Process(sbp.Bp(sizzle[i]));
                if (j >= 0) sizzle[i] = y;
            }

            var whine = new LoopOsc(7867f, n, r.Float());
            double[] fm1 = LoopFmCurve(n, 2.5f, 1), fm2 = LoopFmCurve(n, 1f, 5, 0.3);
            float[] b = new float[n];
            for (int i = 0, c = 0; i < n; i++)
            {
                b[i] = cycle[c] + 0.018f * Sin01(whine.Next() + fm1[i] + fm2[i]) + 0.02f * sizzle[i];
                if (++c == period) c = 0;
            }
            return FinishLoop(b, -38f);
        }

        /// <summary>
        /// 8 s tension bed: a sub pair beating 3x per loop; an E2/F2/Bb2 cluster, each note breathing on its own
        /// period, through a slowly opening low-pass; distant "bowed metal" (noise-excited, very narrow resonators
        /// at struck-plate ratios) swelling in and out through a long reverb; a breath of air.
        /// </summary>
        private static float[] DroneTension(Rng r)
        {
            int n = Sec(8f);
            float invN = 1f / n;

            var wt = new Wavetable(SawAmps(14, 1.5f));
            var c1 = new LoopOsc(82.5f, n, r.Float());
            var c2 = new LoopOsc(87.375f, n, r.Float());
            var c3 = new LoopOsc(116.5f, n, r.Float());
            var s1 = new LoopOsc(41.25f, n, r.Float());
            var s2 = new LoopOsc(41.625f, n, r.Float());
            float[] b = new float[n];
            float a1 = 0f, a2 = 0f, a3 = 0f;
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                {
                    double u = i * (double)invN;
                    a1 = 0.55f + 0.45f * Sin01(u);
                    a2 = 0.50f + 0.50f * Sin01(2 * u + 0.3);
                    a3 = 0.35f + 0.35f * Sin01(u + 0.55);
                }
                b[i] = a1 * wt.At(c1.Next()) + a2 * wt.At(c2.Next()) + a3 * wt.At(c3.Next());
            }
            // cluster through a slowly opening low-pass; in the same pass, pink "air" noise band-passed at 380 Hz
            float[] air = WhiteNoise(n, r);
            var clp = new Svf(400f, 1.1f);
            var pink = new PinkFilter();
            var abp = new Svf(380f, 0.7f);
            var alp = new OnePole(1500f);
            for (int j = -Sec(0.3f); j < n; j++)
            {
                int i = Wrap(j, n);
                if ((i & 31) == 0) clp.Set(280f + 380f * (0.5f + 0.5f * Sin01(i * (double)invN + 0.6)), 1.1f);
                float c = clp.Lp(b[i]), a = alp.Lp(abp.Bp(pink.Process(air[i])));
                if (j < 0) continue;
                b[i] = c;
                air[i] = a;
            }

            // distant bowed metal: very narrow resonators at struck-plate ratios, excited by noise, each
            // swelling in and out at its own place in the loop
            float[] ratios = { 1f, 1.593f, 2.136f, 2.296f, 2.653f, 3.6f };
            int partials = ratios.Length;
            var hz = new float[partials];
            var gains = new float[partials];
            var centre = new float[partials];
            var width = new float[partials];
            for (int k = 0; k < partials; k++)
            {
                hz[k] = 311.1f * ratios[k] * r.Jitter(0.003f);
                gains[k] = 0.12f / (1f + 0.35f * k);
                centre[k] = r.Float();
                width[k] = r.Range(0.18f, 0.35f);
            }
            var bank = new ResonatorBank(hz, 0.35f, gains);
            float[] excite = WhiteNoise(n, r);
            float[] metal = new float[n];
            var swell = new float[partials];
            for (int i = n - Sec(1.8f); i < n; i++) bank.Process(excite[i]); // warm-up: 5 time constants
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                    for (int k = 0; k < partials; k++) swell[k] = Bump(LoopDistance(i * invN, centre[k]), 0f, width[k]);
                bank.Process(excite[i]);
                float m = 0f;
                for (int k = 0; k < partials; k++) m += swell[k] * bank.Out[k];
                metal[i] = m;
            }
            AddReverbLoop(metal, 1.2f, 0.86f, 0.5f, 1.2f, 0.3f, 1.8f);

            float breath = 0f;
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0) breath = 0.6f + 0.4f * Sin01(i * (double)invN + 0.4);
                float sub = 0.275f * (SinPhase(s1.Next()) + SinPhase(s2.Next()));
                b[i] = SoftClip(1.2f * (0.35f * b[i] + sub + 0.6f * metal[i] + 0.12f * breath * air[i]));
            }
            return FinishLoop(b, -27f);
        }

        // ---------------- The entity ----------------

        /// <summary>Slow "breath" of the entity's activity over a loop (0..1, periodic in u).</summary>
        private static float Breath(float u) => 0.5f - 0.5f * SinWrap(u + 0.25f);

        /// <summary>
        /// 2 s loop of electrical life: many small spark clusters, each with its own resonant colour, denser in one
        /// half of the loop (it breathes); a few 120 Hz arcing "fzzt"s; a faint sizzle; a ghost of 120 Hz hum.
        /// </summary>
        private static float[] EntityStatic(Rng r)
        {
            int n = Sec(2f);
            float[] b = new float[n];

            for (int c = 0; c < 30; c++)
            {
                float pos;
                do pos = r.Float(); while (r.Float() > 0.35f + 0.65f * Breath(pos));
                int tail = Ms(8f);
                int evLen = Ms(r.Range(12f, 45f)) + tail;
                var ev = new float[evLen];
                int grains = r.Int(3, 14);
                float meanGap = r.Range(1.5f, 4f) * 0.001f * Sr;
                float amp = r.Range(0.5f, 1f) * (0.6f + 0.4f * Breath(pos));
                int t = 0;
                for (int g = 0; g < grains; g++)
                {
                    int w = r.Int(2, 10);
                    if (t + w >= evLen - tail) break;
                    float a = amp * (0.3f + 0.7f * MathF.Pow(r.Float(), 1.5f)) * (r.Chance(0.5f) ? 1f : -1f);
                    for (int j = 0; j < w; j++) ev[t + j] += a * r.Signed() * (1f - (float)j / w);
                    t += 1 + (int)(-MathF.Log(1f - 0.999f * r.Float()) * meanGap);
                }
                var bp = new Svf(r.Range(900f, 4200f), r.Range(2f, 6f));
                var hp = new OnePole(1500f);
                for (int j = 0; j < evLen; j++)
                {
                    float x = ev[j];
                    ev[j] = 0.8f * bp.Bp(x) + 0.15f * hp.Hp(x);
                }
                AddWrapped(b, ev, (int)(pos * n));
            }

            for (int a = 0; a < 3; a++)
            {
                int len = Ms(r.Range(30f, 80f));
                var ev = new float[len];
                var bp = new Svf(r.Range(1800f, 3000f), 2.2f);
                Phasor gate = default, flicker = default;
                float fl = r.Range(25f, 45f);
                for (int j = 0; j < len; j++)
                {
                    float env = Sq(SinUnit(0.5f * j / len)) * (0.7f + 0.3f * flicker.Sin(fl));
                    float g = MathF.Max(0f, gate.Sin(120f));
                    ev[j] = 0.45f * env * g * g * g * bp.Bp(r.Signed());
                }
                AddWrapped(b, ev, r.Int(0, n));
            }

            float[] bed = WhiteNoise(n, r);
            float[] hum = HarmonicCycle(MainsPeriod, new[] { 2, 4, 6, 10 }, new[] { 1f, 0.5f, 0.35f, 0.2f }, r);
            var bbp = new Svf(3500f, 0.9f);
            for (int j = -Ms(20f); j < n; j++)
            {
                int i = Wrap(j, n);
                float sizzle = bbp.Bp(bed[i]);
                if (j >= 0) b[i] += (0.3f + 0.7f * Breath(i / (float)n)) * (0.016f * sizzle + 0.012f * hum[i % MainsPeriod]);
            }
            LowPass4Loop(b, 7000f);
            return FinishLoop(b, -32f);
        }

        /// <summary>A few crushed, sample-held milliseconds of noise and square: the entity's "tick".</summary>
        private static void AddGlitchTick(float[] b, int at, float amp, Rng r)
        {
            int len = Ms(r.Range(3f, 6f)), hold = r.Int(3, 7);
            float levels = r.Range(3f, 7f), f = r.Range(900f, 2400f), held = 0f;
            Phasor p = default;
            for (int j = 0; j < len && at + j < b.Length; j++)
            {
                float sq = p.Sin(f) >= 0f ? 0.5f : -0.5f;
                if (j % hold == 0) held = Crush(0.6f * r.Signed() + sq, levels);
                float env = Math.Min(1f, j / 6f) * (1f - (float)j / len);
                b[at + j] += amp * held * env;
            }
        }

        /// <summary>Reverse-reverb swell of a dissonant struck tone, sucked into a stuttering glitch tick.</summary>
        private static float[] EntityAppear(Rng r)
        {
            float[] fwd = Buf(0.78f);
            var bp = new Svf(1400f, 0.9f);
            Phasor a = default, c = default, d = default;
            for (int i = 0; i < fwd.Length; i++)
            {
                float t = i * Dt;
                float tone = 0.35f * a.Sin(233.08f) + 0.25f * c.Sin(329.63f) + 0.18f * d.Sin(466.16f);
                fwd[i] = AttackDecay(t, 0.002f, 0.16f) * tone + 0.6f * AttackDecay(t, 0.0005f, 0.03f) * bp.Bp(r.Signed());
            }
            AddReverb(fwd, 0.8f, 0.86f, 0.45f, 0.9f, 0.8f);
            LowPass4(fwd, 6000f);

            float[] b = Buf(1.0f);
            int m = fwd.Length;
            for (int i = 0; i < m; i++)
            {
                float u = (float)i / m;
                b[i] = fwd[m - 1 - i] * u * u * u;
            }
            var ticks = new float[b.Length];
            AddGlitchTick(ticks, m, 1f, r);
            AddGlitchTick(ticks, m + Ms(21f), 0.45f, r);
            AddGlitchTick(ticks, m + Ms(37f), 0.22f, r);
            LowPass4(ticks, 7000f);
            Mix(b, ticks, 0.8f);
            Phasor low = default;
            for (int i = m; i < b.Length; i++)
                b[i] += 0.4f * AttackDecay((i - m) * Dt, 0.0005f, 0.008f) * low.Sin(180f);
            return FinishOneShot(b, -24f, 5f, 60f, 0f, true);
        }

        /// <summary>
        /// Digital failure: crushed noise, crushed square tones and pitch zips, cut up by dropouts and by stutters
        /// that repeat slices of the previous segment ("b-b-b-bzzt").
        /// </summary>
        private static float[] GlitchBurst(Rng r)
        {
            float[] b = Buf(0.32f);
            int pos = 0, end = Sec(0.28f), prevStart = -1, prevLen = 0;
            while (pos < end)
            {
                float pick = pos == 0 ? r.Range(0.5f, 1f) : r.Float(); // always open with sound
                if (prevLen > 0 && pick < 0.32f)
                {
                    int slice = Math.Min(prevLen, Ms(r.Range(4f, 11f)));
                    int reps = r.Int(2, 6);
                    for (int k = 0; k < reps && pos + slice < b.Length; k++)
                    {
                        float g = 1f - 0.12f * k;
                        for (int j = 0; j < slice; j++) b[pos + j] = g * b[prevStart + j];
                        pos += slice + (r.Chance(0.3f) ? Ms(r.Range(1f, 4f)) : 0);
                    }
                    prevLen = 0;
                    continue;
                }
                if (pick < 0.45f)
                {
                    pos += Ms(r.Range(3f, 10f));
                    continue;
                }
                int len = Math.Min(b.Length - pos, Ms(r.Range(8f, 26f)));
                int hold = r.Int(2, 9);
                float levels = r.Range(3f, 12f), level = r.Range(0.55f, 1f), held = 0f;
                float f0 = r.Range(180f, 2400f), f1 = pick > 0.85f ? r.Range(120f, 400f) : f0;
                Phasor p = default;
                for (int j = 0; j < len; j++)
                {
                    float f = f0 * MathF.Pow(f1 / f0, (float)j / len);
                    float s = p.Sin(f);
                    float v = pick < 0.62f ? r.Signed()
                            : pick < 0.85f ? (s >= 0f ? 0.6f : -0.6f) + 0.25f * r.Signed()
                            : 1.2f * s;
                    if (j % hold == 0) held = Crush(v, levels);
                    b[pos + j] = level * held;
                }
                prevStart = pos;
                prevLen = len;
                pos += len;
            }
            LowPass4(b, 8000f);
            return FinishOneShot(b, -20f, 0.5f, 25f, 0f, true);
        }

        /// <summary>
        /// 1.5 s loop: a beating 98 / 103.3 / 146.7 Hz buzz through a resonant band-pass that sweeps twice per
        /// loop, a vibrato whine, a 4 Hz throb and crackling grit. Every rate stays musical from 0.8x to 1.8x,
        /// and the spectrum is kept under ~6 kHz so even 1.8x never gets shrill.
        /// </summary>
        private static float[] TugStrain(Rng r)
        {
            int n = Sec(1.5f);
            var wt = new Wavetable(SawAmps(20, 1.25f));
            var o1 = new LoopOsc(98f, n, r.Float());
            var o2 = new LoopOsc(103.333f, n, r.Float());
            var o3 = new LoopOsc(146.667f, n, r.Float());
            double[] fm3 = LoopFmCurve(n, 1.5f, 3), fmWhine = LoopFmCurve(n, 6f, 9);
            float[] buzz = new float[n];
            for (int i = 0; i < n; i++)
                buzz[i] = 0.5f * wt.At(o1.Next()) + 0.45f * wt.At(o2.Next())
                        + 0.25f * wt.At(Frac(o3.Next() + fm3[i]));
            // strain: a resonant band-pass sweeping twice per loop; grit: band-passed noise (gated below)
            float[] grit = WhiteNoise(n, r);
            var bp = new Svf(900f, 3.5f);
            var gbp = new Svf(2200f, 1.5f);
            for (int j = -Ms(200f); j < n; j++)
            {
                int i = Wrap(j, n);
                if ((i & 15) == 0) bp.Set(900f * MathF.Pow(1.6f, Sin01(2.0 * i / n)), 3.5f);
                float x = buzz[i], strained = 0.6f * bp.Bp(x) + 0.25f * x, g = gbp.Bp(grit[i]);
                if (j < 0) continue;
                buzz[i] = strained;
                grit[i] = g;
            }
            var gate = new float[n];
            for (int k = 0; k < 14; k++)
            {
                var ev = new float[Ms(r.Range(8f, 30f))];
                for (int j = 0; j < ev.Length; j++) ev[j] = Sq(SinUnit(0.5f * j / ev.Length));
                AddWrapped(gate, ev, r.Int(0, n), r.Range(0.4f, 1f));
            }

            var whine = new LoopOsc(1306.667f, n, r.Float());
            var throb = new LoopOsc(4f, n);
            float[] b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float th = 0.78f + 0.22f * SinPhase(throb.Next());
                float s = th * buzz[i]
                        + 0.06f * Sin01(whine.Next() + fmWhine[i])
                        + 0.05f * Math.Min(1f, gate[i]) * grit[i];
                b[i] = SoftClip(1.3f * s);
            }
            return FinishLoop(b, -25f, 6000f);
        }

        /// <summary>Fractional part in [0, 1) for small values of either sign.</summary>
        private static double Frac(double x)
        {
            double f = x - (int)x;
            return f < 0.0 ? f + 1.0 : f;
        }

        private static readonly Mode[] SnapModes =
        {
            new Mode(1300f, 5f, 0.4f),
            new Mode(2200f, 4f, 0.8f),
            new Mode(3600f, 2.5f, 0.6f),
        };

        /// <summary>A crack, a fast downward "thwip" pop, a low thump and a short buzzing zap.</summary>
        private static float[] GrabSnap(Rng r)
        {
            float[] b = Buf(0.22f);
            AddImpact(b, 0, 0.8f, SnapModes, r, 3, 0.6f, 1.0f, 1f, 1f, 0.4f);
            var bp = new Svf(1800f, 2f);
            Phasor pop = default, low = default, buzz = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float f = 140f + 1150f * MathF.Exp(-t / 0.012f);
                float gate = 0.6f + 0.4f * (buzz.Sin(110f) >= 0f ? 1f : -1f);
                b[i] += 0.7f * AttackDecay(t, 0.0005f, 0.028f) * pop.Sin(f)
                      + 0.35f * AttackDecay(t, 0.002f, 0.04f) * low.Sin(88f)
                      + 0.28f * AttackDecay(t - 0.004f, 0.002f, 0.045f) * gate * bp.Bp(r.Signed());
            }
            return FinishOneShot(b, -19f, 0.1f, 30f, 9000f, true);
        }

        // ---------------- CCTV ----------------

        /// <summary>Selector relay (armature + two contact bounces), a jittery static burst, a lock-in tick.</summary>
        private static float[] CameraSwitch(Rng r)
        {
            float[] b = Buf(0.36f);
            AddImpact(b, 0, 0.9f, RelayModes, r, 3, 0.25f, 0.3f, 1.1f);
            AddImpact(b, Ms(3.2f), 0.5f, RelayModes, r, 4, 0.2f, 0.3f, 1.15f, 0.8f);
            AddImpact(b, Ms(6.9f), 0.25f, RelayModes, r, 4, 0.15f, 0.3f, 1.12f, 0.7f);

            var hp = new Svf(300f, 0.7f);
            var lp = new Lp4(6000f);
            Phasor v = default;
            float target = 1f, level = 1f;
            int s0 = Ms(9f), next = 0;
            for (int i = s0; i < b.Length; i++)
            {
                float t = (i - s0) * Dt;
                if (i >= next) { target = r.Range(0.55f, 1f); next = i + Ms(r.Range(4f, 12f)); }
                level += (target - level) * 0.05f;
                float env = AttackDecay(t, 0.003f, 10f) * (1f - SmoothStep(0.2f, 0.285f, t));
                float field = 0.7f + 0.3f * MathF.Max(0f, v.Sin(60f));
                b[i] += 0.5f * env * level * field * lp.Process(hp.Hp(r.Signed()));
            }
            AddImpact(b, Sec(0.296f), 0.2f, RelayModes, r, 3, 0.1f, 0.2f, 1.4f, 0.4f);
            return FinishOneShot(b, -22f, 0.1f, 30f, 9000f, true);
        }

        /// <summary>Soft CCTV snow: filtered noise with a 60 Hz field buzz, gentle drift and a trace of hum.</summary>
        private static float[] CameraStatic(Rng r)
        {
            int n = Sec(2f);
            float[] b = WhiteNoise(n, r);
            var lp = new Lp4(5000f);
            var soft = new OnePole(7000f);
            var hp = new Svf(180f, 0.7f);
            for (int j = -Ms(50f); j < n; j++)
            {
                int i = Wrap(j, n);
                float y = hp.Hp(soft.Lp(lp.Process(b[i])));
                if (j >= 0) b[i] = y;
            }
            var field = new float[MainsPeriod]; // vertical-sync buzz on the noise, plus a trace of mains hum
            for (int i = 0; i < MainsPeriod; i++) field[i] = 0.88f + 0.12f * Sin01((double)i / MainsPeriod);
            float[] hum = HarmonicCycle(MainsPeriod, new[] { 1, 2 }, new[] { 0.018f, 0.012f }, r);
            float drift = 1f;
            for (int i = 0, c = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                {
                    double u = i / (double)n;
                    drift = 1f + 0.06f * Sin01(u) + 0.04f * Sin01(3 * u + 0.3) + 0.03f * Sin01(7 * u + 0.7);
                }
                b[i] = b[i] * field[c] * drift + hum[c];
                if (++c == MainsPeriod) c = 0;
            }
            return FinishLoop(b, -32f);
        }

        // ---------------- The building ----------------

        /// <summary>Sub sine falling 80 -> 36 Hz, a felt beater knock, gentle saturation, dark room tail.</summary>
        private static float[] LowThump(Rng r)
        {
            float[] b = Buf(0.85f);
            Phasor p = default;
            var beater = new Svf(260f, 0.8f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float f = 36f + 44f * Decay(t, 0.045f);
                float body = AttackDecay(t, 0.004f, 0.24f) * p.Sin(f);
                float knock = 2.2f * AttackDecay(t, 0.0008f, 0.014f) * beater.Lp(r.Signed());
                b[i] = SoftClip(1.3f * body) + 0.35f * knock;
            }
            AddReverb(b, 1.3f, 0.85f, 0.75f, 0.25f, 1f, 0f, 400f);
            return FinishOneShot(b, -18f, 0.3f, 200f, 0f, false, 22f);
        }

        private static readonly Mode[] DoorModes =
        {
            new Mode(62f, 110f, 1.0f),
            new Mode(118f, 60f, 0.6f),
            new Mode(185f, 45f, 0.5f),
            new Mode(310f, 28f, 0.35f),
            new Mode(520f, 16f, 0.25f),
            new Mode(880f, 9f, 0.15f),
            new Mode(1650f, 6f, 0.10f),
        };

        private static readonly Mode[] LatchModes =
        {
            new Mode(980f, 12f, 0.3f),
            new Mode(1850f, 9f, 0.5f),
            new Mode(2900f, 6f, 0.4f),
            new Mode(4200f, 3f, 0.2f),
        };

        /// <summary>A heavy fire door slams and its latch rattles, far away through walls, in a big stairwell.</summary>
        private static float[] DoorDistant(Rng r)
        {
            float[] b = Buf(1.6f);
            AddImpact(b, Ms(30f), 1f, DoorModes, r, 60, 0.5f, 8f);
            AddImpact(b, Ms(34f), 0.35f, LatchModes, r, 6, 0.3f, 1f);
            AddImpact(b, Ms(118f), 0.3f, DoorModes, r, 40, 0.3f, 5f, 1.05f, 0.6f);
            AddImpact(b, Ms(121f), 0.18f, LatchModes, r, 5, 0.2f, 1f, 1.03f);
            LowPass(b, 900f);
            LowPass(b, 1400f);
            AddReverb(b, 1.6f, 0.9f, 0.6f, 1.3f, 0.6f, 18f, 1800f);
            return FinishOneShot(b, -30f, 2f, 400f);
        }

        private static readonly Mode[] HeelModes =
        {
            new Mode(160f, 22f, 0.6f),
            new Mode(420f, 12f, 0.4f),
            new Mode(910f, 8f, 0.5f),
            new Mode(1600f, 5f, 0.45f),
            new Mode(2550f, 3f, 0.3f),
        };

        /// <summary>A hard heel strike and sole roll on tile, far down a corridor: flutter echoes + tail.</summary>
        private static float[] FootstepDistant(Rng r)
        {
            float[] b = Buf(0.55f);
            AddImpact(b, Ms(15f), 1f, HeelModes, r, 10, 0.5f, 2f, r.Jitter(0.04f));
            var bp = new Svf(900f, 0.8f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt - 0.075f;
                if (t >= 0f && t < 0.08f) b[i] += 0.3f * AttackDecay(t, 0.006f, 0.014f) * bp.Bp(r.Signed());
            }
            LowPass(b, 1500f);
            AddFlutter(b, 9.5f, 6, 0.4f, 0.7f, 1800f);
            AddReverb(b, 1.15f, 0.85f, 0.55f, 1.4f, 0.85f, 12f, 2200f);
            return FinishOneShot(b, -32f, 2f, 150f);
        }

        private static readonly Mode[] BreakerModes =
        {
            new Mode(95f, 40f, 0.8f),
            new Mode(260f, 20f, 0.5f),
            new Mode(1200f, 8f, 0.5f),
            new Mode(2100f, 6f, 0.45f),
            new Mode(3300f, 4f, 0.3f),
        };

        /// <summary>
        /// Building power failure: mains hum, ballast buzz, a whine and a fan are running; the breaker thunks,
        /// the hums sag and collapse, the tubes flicker twice, motors spin down, relays clack further away.
        /// </summary>
        private static float[] PowerDown(Rng r)
        {
            const float len = 2.6f, cut = 0.22f;
            float[] b = Buf(len);
            Wavetable hum = MainsHum(r, 0.95f);
            var buzzAmps = new float[30];
            for (int h = 0; h < buzzAmps.Length; h++)
                buzzAmps[h] = MathF.Pow(h + 1, -0.8f) * (1f + 1.5f * MathF.Exp(-Sq((120f * (h + 1) - 2300f) / 1200f)));
            var buzz = new Wavetable(buzzAmps, RandomPhases(buzzAmps.Length, r));
            var fanBp = new Svf(700f, 0.7f);
            Phasor hp = default, bz = default, wh = default, blade = default, whump = default;
            float mains = 1f, sag = 1f, flick = 1f, spin = 1f, whumpEnv = 0f, whumpHz = 70f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt, tc = t - cut;
                bool on = tc < 0f;
                if ((i & 15) == 0 && !on)
                {
                    mains = Decay(tc, 0.12f);
                    sag = 1f - 0.45f * (1f - Decay(tc, 0.2f));
                    flick = 0.6f * Bump(tc, 0.07f, 0.025f) + 0.35f * Bump(tc, 0.17f, 0.02f);
                    spin = Decay(tc, 0.55f);
                    fanBp.Set(250f + 450f * spin, 0.7f);
                    whumpHz = 34f + 36f * Decay(tc, 0.06f);
                }
                float s = 0.30f * mains * hum.At(hp.Advance(60f * sag))
                        + 0.12f * flick * buzz.At(bz.Advance(120f * sag))
                        + 0.020f * spin * spin * wh.Sin(6500f * (0.1f + 0.9f * spin))
                        + 0.10f * spin * fanBp.Bp(r.Signed())
                        + 0.05f * spin * blade.Sin(280f * spin);
                if (!on)
                {
                    if ((i & 3) == 0) whumpEnv = 0.7f * AttackDecay(tc, 0.004f, 0.25f);
                    s += whumpEnv * whump.Sin(whumpHz);
                }
                b[i] = s;
            }
            AddImpact(b, Sec(cut), 0.8f, BreakerModes, r, 12, 0.5f, 2f);

            float[] relays = Buf(len);
            AddImpact(relays, Sec(cut + 0.19f), 0.5f, RelayModes, r, 5, 0.3f, 0.4f, 0.8f);
            AddImpact(relays, Sec(cut + 0.37f), 0.35f, RelayModes, r, 5, 0.3f, 0.4f, 0.7f);
            AddImpact(relays, Sec(cut + 0.71f), 0.22f, RelayModes, r, 6, 0.3f, 0.4f, 0.65f);
            LowPass(relays, 3500f);
            Mix(b, relays, 1f);
            AddReverb(b, 1.3f, 0.86f, 0.5f, 0.6f);
            return FinishOneShot(b, -22f, 30f, 300f, 9000f, true);
        }

        /// <summary>
        /// A3 pair beating slowly, a sub-octave, a tritone shadow, a trembling high pair and a glassy A6 that
        /// arrives late; everything sags ~20 cents as it fades.
        /// </summary>
        private static float[] EndTone(Rng r)
        {
            const float len = 6f;
            float[] b = Buf(len);
            var root = new Wavetable(new[] { 0.22f, 0.42f, 0f, 0f, 0f, 0.05f }); // A2, A3, E5 (h1, h2, h6 of 110 Hz)
            Phasor a = default, a2 = default, tri = default, glass = default, vib = default, t1 = default, t2 = default;
            a2.Phase = r.Float();
            t2.Phase = r.Float();
            float env = 0f, glassEnv = 0f, trembleEnv = 0f, sag = 1f;
            for (int i = 0; i < b.Length; i++)
            {
                if ((i & 63) == 0)
                {
                    float t = i * Dt;
                    sag = 1f - 0.012f * SmoothStep(3f, 6f, t);
                    env = SmoothStep(0f, 1.3f, t) * (1f - SmoothStep(3.4f, 5.95f, t));
                    glassEnv = SmoothStep(1.2f, 2.8f, t) * (1f - SmoothStep(3.6f, 5.8f, t));
                    trembleEnv = SmoothStep(2.0f, 3.5f, t) * (1f - SmoothStep(3.8f, 5.6f, t));
                }
                float v = 1f + 0.0018f * vib.Sin(4.7f);
                b[i] = env * (root.At(a.Advance(110f * sag)) + 0.42f * a2.Sin(220.45f * sag)
                            + 0.12f * tri.Sin(311.13f * sag) + 0.05f * glassEnv * glass.Sin(1760f * sag * v)
                            + 0.025f * trembleEnv * (t1.Sin(1244.5f * sag) + t2.Sin(1247.5f * sag)));
            }
            AddReverb(b, 1.5f, 0.88f, 0.45f, 0.8f);
            return FinishOneShot(b, -24f, 10f, 400f);
        }
    }
}
