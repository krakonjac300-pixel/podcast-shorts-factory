// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank
//  Every sound in the game is synthesised here, from code, at start-up.
//  Pure C# (no UnityEngine), deterministic (own PCG32 PRNG), mono 44.1 kHz.
//
//  Layout:
//    1. Public API + registry
//    2. DSP toolkit  (PRNG, oscillators, filters, reverb, envelopes, helpers)
//    3. Sound designs (one method per id, grouped by family)
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
            // --- physical input devices (used for the SECOND cursor: physical, in the room)
            new Entry("mouse_click",      false, 0.85f, MouseClick),
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

        /// <summary>sin(2*pi*phase) with the phase in cycles (any value). Table lookup, ~-140 dB error.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sin01(double phase)
        {
            double x = (phase - Math.Floor(phase)) * SinSize;
            int i = (int)x;
            float f = (float)(x - i);
            float a = SinTable[i];
            return a + (SinTable[i + 1] - a) * f;
        }

        /// <summary>Free-running phase accumulator for one-shots (frequency may change per sample).</summary>
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
            public float Sin(float hz)
            {
                double x = Advance(hz) * SinSize;
                int i = (int)x;
                float f = (float)(x - i);
                float a = SinTable[i];
                return a + (SinTable[i + 1] - a) * f;
            }
        }

        /// <summary>
        /// Exactly periodic phase for loops: the frequency is rounded to a whole number of cycles per loop,
        /// and the phase is tracked with an integer accumulator so the loop point is sample-exact.
        /// </summary>
        private struct LoopOsc
        {
            private readonly int _cycles, _n;
            private readonly double _invN;
            private int _acc;

            public LoopOsc(float hz, int loopSamples, float startPhase = 0f)
            {
                _n = loopSamples;
                _cycles = Math.Max(0, (int)Math.Round(hz * (double)loopSamples / SampleRate));
                _invN = 1.0 / loopSamples;
                _acc = (int)(startPhase * loopSamples) % loopSamples;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Next()
            {
                double p = _acc * _invN;
                _acc += _cycles;
                if (_acc >= _n) _acc -= _n;
                return p;
            }
        }

        /// <summary>
        /// Phase offset (cycles) of a sinusoidal vibrato of +/-depthHz running 'cycles' times per loop.
        /// It integrates to zero over the loop, so FM'd loop tones stay seamless.
        /// </summary>
        private static double LoopFm(int i, int n, float depthHz, int cycles, double phase0 = 0.0)
        {
            double rateHz = (double)cycles * SampleRate / n;
            double u = (double)i * cycles / n + phase0;
            return depthHz / (TwoPi * rateHz) * (Sin01(phase0 + 0.25) - Sin01(u + 0.25));
        }

        /// <summary>Single-cycle wavetable (built additively, so it is band-limited by construction).</summary>
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

        // ---------------- Filters ----------------

        private sealed class OnePole
        {
            private float _a, _z;

            public OnePole(float hz) { Set(hz); }

            public void Set(float hz) { _a = 1f - MathF.Exp(-TwoPi * hz / Sr); }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Lp(float x) { _z += _a * (x - _z); return _z; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Hp(float x) { _z += _a * (x - _z); return x - _z; }
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
                float v3 = x - _ic2;
                band = _a1 * _ic1 + _a2 * v3;
                low = _ic2 + _a2 * _ic1 + _a3 * v3;
                _ic1 = 2f * band - _ic1;
                _ic2 = 2f * low - _ic2;
            }

            public float Lp(float x) { Tick(x, out _, out float low); return low; }

            public float Hp(float x) { Tick(x, out float band, out float low); return x - _k * band - low; }

            /// <summary>Band-pass with unity gain at the centre frequency.</summary>
            public float Bp(float x) { Tick(x, out float band, out _); return _k * band; }
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
                float y = _b0 * x + _a1 * _y1 + _a2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
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

        /// <summary>Freeverb-style mono reverb (8 damped combs + 4 all-passes), roughly unity power gain.</summary>
        private sealed class Reverb
        {
            private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
            private static readonly int[] AllpassLengths = { 556, 441, 341, 225 };

            private readonly float[][] _comb = new float[8][];
            private readonly int[] _combPos = new int[8];
            private readonly float[] _combLp = new float[8];
            private readonly float[][] _ap = new float[4][];
            private readonly int[] _apPos = new int[4];
            private readonly float _feedback, _damp, _inGain;

            /// <param name="size">Delay-length scale (1 = medium room).</param>
            /// <param name="feedback">Comb feedback (0.7 short .. 0.92 long).</param>
            /// <param name="damp">High-frequency damping 0..1.</param>
            public Reverb(float size, float feedback, float damp)
            {
                for (int i = 0; i < 8; i++) _comb[i] = new float[Math.Max(16, (int)(CombLengths[i] * size))];
                for (int i = 0; i < 4; i++) _ap[i] = new float[Math.Max(8, (int)(AllpassLengths[i] * size))];
                _feedback = feedback;
                _damp = damp;
                _inGain = MathF.Sqrt((1f - feedback * feedback) / 8f);
            }

            public float Process(float x)
            {
                float input = x * _inGain + 1e-18f, sum = 0f;
                for (int i = 0; i < 8; i++)
                {
                    float[] buf = _comb[i];
                    int p = _combPos[i];
                    float y = buf[p];
                    _combLp[i] = y + (_combLp[i] - y) * _damp;
                    buf[p] = input + _combLp[i] * _feedback;
                    if (++p == buf.Length) p = 0;
                    _combPos[i] = p;
                    sum += y;
                }
                for (int i = 0; i < 4; i++)
                {
                    float[] buf = _ap[i];
                    int p = _apPos[i];
                    float y = buf[p];
                    buf[p] = sum + y * 0.5f;
                    sum = y - sum;
                    if (++p == buf.Length) p = 0;
                    _apPos[i] = p;
                }
                return sum;
            }
        }

        // ---------------- Envelopes & small math ----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sq(float x) => x * x;

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float DbToGain(float db) => MathF.Pow(10f, db / 20f);

        private static float SmoothStep(float e0, float e1, float x)
        {
            float u = Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }

        /// <summary>exp(-t/tau) for t &gt;= 0, 0 before (and flushed to 0 far into the tail).</summary>
        private static float Decay(float t, float tau) => t < 0f || t > 40f * tau ? 0f : MathF.Exp(-t / tau);

        /// <summary>Raised-cosine attack followed by exponential decay.</summary>
        private static float AttackDecay(float t, float attack, float tau)
        {
            if (t < 0f) return 0f;
            if (t < attack) return 0.5f - 0.5f * Sin01(0.25 + 0.5 * t / attack);
            return Decay(t - attack, tau);
        }

        /// <summary>Raised-cosine bump centred at c with half-width w (0 outside).</summary>
        private static float Bump(float t, float c, float w)
        {
            float d = (t - c) / w;
            return d <= -1f || d >= 1f ? 0f : 0.5f + 0.5f * Sin01(0.25 + 0.5 * d);
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

        private static int Sec(float s) => (int)(s * SampleRate + 0.5f);

        private static int Ms(float ms) => (int)(ms * 0.001f * SampleRate + 0.5f);

        private static float[] Buf(float seconds) => new float[Sec(seconds)];

        private static float[] WhiteNoise(int n, Rng r)
        {
            var b = new float[n];
            for (int i = 0; i < n; i++) b[i] = r.Signed();
            return b;
        }

        private static void Mix(float[] dst, float[] src, float gain, int offset = 0)
        {
            int end = Math.Min(src.Length, dst.Length - offset);
            for (int i = Math.Max(0, -offset); i < end; i++) dst[i + offset] += gain * src[i];
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

        /// <summary>
        /// Runs a stateful processor over a loop so the result is seamless: it is first warmed up on the
        /// last 'warmup' samples (the audio that precedes sample 0 when looping), then run over the loop.
        /// </summary>
        private static void Circular(float[] b, Func<float, float> proc, int warmup)
        {
            int n = b.Length;
            warmup = Math.Min(warmup, n);
            for (int i = n - warmup; i < n; i++) proc(b[i]);
            for (int i = 0; i < n; i++) b[i] = proc(b[i]);
        }

        /// <summary>Like <see cref="Circular"/>, for processors whose parameters depend on the sample index.</summary>
        private static void CircularIndexed(float[] b, Func<int, float, float> proc, int warmup)
        {
            int n = b.Length;
            warmup = Math.Min(warmup, n);
            for (int i = n - warmup; i < n; i++) proc(i, b[i]);
            for (int i = 0; i < n; i++) b[i] = proc(i, b[i]);
        }

        private static void LowPass(float[] b, float hz, float q = 0.7071f)
        {
            var f = new Svf(hz, q);
            for (int i = 0; i < b.Length; i++) b[i] = f.Lp(b[i]);
        }

        private static void HighPass(float[] b, float hz, float q = 0.7071f)
        {
            var f = new Svf(hz, q);
            for (int i = 0; i < b.Length; i++) b[i] = f.Hp(b[i]);
        }

        private static void LowPassLoop(float[] b, float hz, float q = 0.7071f)
        {
            var f = new Svf(hz, q);
            Circular(b, f.Lp, Ms(30f));
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
        private static void AddReverbLoop(float[] b, float size, float feedback, float damp, float wet, float dry = 1f)
        {
            var rev = new Reverb(size, feedback, damp);
            var w = (float[])b.Clone();
            Circular(w, rev.Process, b.Length);
            for (int i = 0; i < b.Length; i++) b[i] = dry * b[i] + wet * w[i];
        }

        /// <summary>Small office: a few low-passed early reflections (desk, monitor, walls) + a short diffuse tail.</summary>
        private static void AddRoom(float[] b, float early, float tail)
        {
            float[] tapMs = { 1.9f, 4.6f, 7.9f, 12.4f, 17.7f, 24.1f, 31.5f };
            float[] tapGain = { 0.32f, 0.22f, 0.16f, 0.11f, 0.08f, 0.055f, 0.04f };
            int n = b.Length;
            var er = new float[n];
            for (int k = 0; k < tapMs.Length; k++)
            {
                int d = Ms(tapMs[k]);
                float g = early * tapGain[k];
                for (int i = d; i < n; i++) er[i] += g * b[i - d];
            }
            var rev = new Reverb(0.33f, 0.7f, 0.45f);
            var lp = new Svf(4200f, 0.6f);
            for (int i = 0; i < n; i++)
            {
                float wet = er[i] + tail * rev.Process(b[i]);
                b[i] += lp.Lp(wet);
            }
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

        /// <summary>Adds an exponentially decaying sine with a short raised-cosine attack.</summary>
        private static void AddDecaySine(float[] b, int start, float hz, float amp, float tau, float attack)
        {
            if (start < 0 || start >= b.Length || hz >= 0.45f * Sr) return;
            int end = Math.Min(b.Length, start + (int)(tau * 12f * Sr));
            int atk = Math.Max(1, (int)(attack * Sr));
            float k = MathF.Exp(-1f / (tau * Sr)), env = amp;
            Phasor ph = default;
            for (int i = start, j = 0; i < end; i++, j++, env *= k)
            {
                float a = j < atk ? env * (0.5f - 0.5f * Sin01(0.25 + 0.5 * j / atk)) : env;
                b[i] += a * ph.Sin(hz);
            }
        }

        /// <summary>A struck bell / glass / chime voice made of decaying partials.</summary>
        private static void AddBell(float[] b, float startSec, float hz, float amp, Partial[] partials,
                                    float decayScale = 1f, float attack = 0.0015f)
        {
            int start = Sec(startSec);
            foreach (Partial p in partials)
            {
                float f = hz * p.Ratio + p.Detune;
                if (f > 11000f) continue;
                AddDecaySine(b, start, f, amp * p.Amp, p.Tau * decayScale, attack);
            }
        }

        /// <summary>Held wavetable tone: attack, optional decay while held, raised-cosine release; two detuned voices.</summary>
        private static void AddTone(float[] b, Wavetable wt, float start, float dur, float hz, float amp,
                                    float attack, float release, float decayTau = 0f, float detuneCents = 0f)
        {
            int s0 = Sec(start), n = Sec(dur + release);
            float up = MathF.Pow(2f, detuneCents / 1200f), down = 1f / up;
            Phasor a = default, c = default;
            c.Phase = 0.37;
            for (int j = 0; j < n && s0 + j < b.Length; j++)
            {
                float t = j * Dt;
                float env = t < attack ? 0.5f - 0.5f * Sin01(0.25 + 0.5 * t / attack) : 1f;
                if (decayTau > 0f) env *= Decay(t, decayTau);
                if (t > dur) env *= 0.5f + 0.5f * Sin01(0.25 + 0.5 * Math.Min(1f, (t - dur) / release));
                float v = 0.5f * (wt.At(a.Advance(hz * up)) + wt.At(c.Advance(hz * down)));
                b[s0 + j] += amp * env * v;
            }
        }

        // ---------------- Finishing ----------------

        private static void FadeIn(float[] b, int len)
        {
            len = Math.Min(len, b.Length);
            for (int i = 0; i < len; i++) b[i] *= 0.5f - 0.5f * Sin01(0.25 + 0.5 * i / len);
        }

        private static void FadeOut(float[] b, int len)
        {
            len = Math.Min(len, b.Length);
            int n = b.Length;
            for (int i = 0; i < len; i++) b[n - 1 - i] *= 0.5f - 0.5f * Sin01(0.25 + 0.5 * i / len);
        }

        /// <summary>Removes the mean with a Hann-shaped correction, so the (already faded) ends stay at zero.</summary>
        private static void RemoveDcWindowed(float[] b)
        {
            int n = b.Length;
            if (n < 3) return;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += b[i];
            float c = (float)(sum / ((n - 1) * 0.5));
            for (int i = 0; i < n; i++) b[i] -= c * Sq(Sin01(0.5 * i / (n - 1)));
        }

        private static void RemoveMean(float[] b)
        {
            double sum = 0;
            for (int i = 0; i < b.Length; i++) sum += b[i];
            float m = (float)(sum / b.Length);
            for (int i = 0; i < b.Length; i++) b[i] -= m;
        }

        /// <summary>Scales to the target RMS level, never letting the peak exceed the ceiling.</summary>
        private static void Normalize(float[] b, float rmsDb)
        {
            double e = 0;
            float peak = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                e += (double)b[i] * b[i];
                float a = MathF.Abs(b[i]);
                if (a > peak) peak = a;
            }
            if (peak <= 0f) return;
            float rms = (float)Math.Sqrt(e / b.Length);
            float g = DbToGain(rmsDb) / rms;
            if (peak * g > PeakCeiling) g = PeakCeiling / peak;
            Scale(b, g);
        }

        /// <summary>DC-block, tame the top octave, de-click the ends, zero the mean, set the level.</summary>
        private static float[] FinishOneShot(float[] b, float rmsDb, float fadeInMs, float fadeOutMs, float hpHz = 18f)
        {
            var hp = new OnePole(hpHz);
            var air1 = new Svf(11500f, 0.5412f);
            var air2 = new Svf(11500f, 1.3066f);
            for (int i = 0; i < b.Length; i++) b[i] = air2.Lp(air1.Lp(hp.Hp(b[i])));
            FadeIn(b, Math.Max(2, Ms(fadeInMs)));
            FadeOut(b, Math.Max(2, Ms(fadeOutMs)));
            RemoveDcWindowed(b);
            Normalize(b, rmsDb);
            return b;
        }

        /// <summary>Loop version of <see cref="FinishOneShot"/>: every stage is circular, so the seam stays intact.</summary>
        private static float[] FinishLoop(float[] b, float rmsDb, float hpHz = 18f)
        {
            var hp = new OnePole(hpHz);
            var air1 = new Svf(11500f, 0.5412f);
            var air2 = new Svf(11500f, 1.3066f);
            Circular(b, x => air2.Lp(air1.Lp(hp.Hp(x))), Math.Min(b.Length, Sec(0.5f)));
            RemoveMean(b);
            Normalize(b, rmsDb);
            return b;
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
            return FinishOneShot(b, -22f, 0.2f, 6f);
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
            return FinishOneShot(b, -25f, 0.2f, 4f);
        }

        /// <summary>Rising band-passed air that lands on a soft tick.</summary>
        private static float[] UiWindow(Rng r)
        {
            float[] b = Buf(0.095f);
            var air = new Svf(800f, 1.3f);
            var body = new Svf(380f, 0.8f);
            Phasor t1 = default, t2 = default;
            const float swellEnd = 0.052f, tickAt = 0.056f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                if ((i & 15) == 0) air.Set(800f * MathF.Pow(3.2f, Clamp01(t / swellEnd)), 1.3f);
                float env = t < swellEnd ? Sq(Sin01(0.25 * t / swellEnd)) : Decay(t - swellEnd, 0.010f);
                float w = r.Signed();
                float s = env * (0.75f * air.Bp(w) + 0.3f * body.Bp(w));
                float tt = t - tickAt;
                if (tt >= 0f)
                    s += 0.5f * AttackDecay(tt, 0.0003f, 0.0028f) * t1.Sin(2250f)
                       + 0.28f * AttackDecay(tt, 0.0005f, 0.005f) * t2.Sin(1125f);
                b[i] = s;
            }
            return FinishOneShot(b, -24f, 0.5f, 10f);
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

        /// <summary>Two stacked tritones a semitone apart ("da-dum"), soft-square timbre with beating voices.</summary>
        private static float[] SysError(Rng r)
        {
            float[] b = Buf(0.42f);
            var wt = new Wavetable(new[] { 1f, 0.10f, 0.30f, 0f, 0.12f, 0f, 0.05f, 0f, 0.02f });
            AddTone(b, wt, 0.000f, 0.135f, 523.25f, 0.50f, 0.004f, 0.018f, 0f, 7f);
            AddTone(b, wt, 0.000f, 0.135f, 739.99f, 0.40f, 0.004f, 0.018f, 0f, 7f);
            AddTone(b, wt, 0.140f, 0.200f, 493.88f, 0.55f, 0.004f, 0.070f, 0.16f, 9f);
            AddTone(b, wt, 0.140f, 0.200f, 698.46f, 0.45f, 0.004f, 0.070f, 0.16f, 9f);
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

            float[] bells = Buf(len);
            AddBell(bells, 0.35f, 587.33f, 0.45f, GlassBell);
            AddBell(bells, 0.52f, 880.00f, 0.42f, GlassBell);
            AddBell(bells, 0.69f, 987.77f, 0.40f, GlassBell);
            AddBell(bells, 0.86f, 1479.98f, 0.34f, GlassBell, 1.1f);
            AddBell(bells, 1.58f, 1318.51f, 0.40f, GlassBell, 1.2f);
            AddBell(bells, 2.08f, 1174.66f, 0.46f, GlassBell, 1.6f);
            AddBell(bells, 2.10f, 1760.00f, 0.10f, GlassBell, 1.2f);

            float[] b = Buf(len);
            Mix(b, pad, 0.55f);
            Mix(b, bells, 1.0f);
            AddReverb(b, 1.25f, 0.86f, 0.35f, 0.55f);
            return FinishOneShot(b, -20f, 2f, 450f);
        }

        private static void AddPadChord(float[] b, Wavetable wt, float[] notes, float start, float attack,
                                        float holdEnd, float release, Rng r)
        {
            int s0 = Sec(start), s1 = Math.Min(b.Length, Sec(holdEnd + release));
            float per = 0.5f / MathF.Sqrt(notes.Length);
            foreach (float f in notes)
            {
                float gain = per * (f < 150f ? 1.2f : 1f);
                for (int v = 0; v < 2; v++)
                {
                    float hz = f * (v == 0 ? 1.0035f : 0.9965f); // +/- 6 cents: slow chorus
                    Phasor ph = default;
                    ph.Phase = r.Float();
                    for (int i = s0; i < s1; i++)
                    {
                        float t = (i - s0) * Dt;
                        float env = t < attack ? Sq(Sin01(0.25 * t / attack)) : 1f;
                        float tr = i * Dt - holdEnd;
                        if (tr > 0f) env *= Sq(Sin01(0.25 + 0.25 * Math.Min(1f, tr / release)));
                        b[i] += gain * env * wt.At(ph.Advance(hz));
                    }
                }
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

        /// <summary>Omron-style micro-switch: leaf snap, contact settle and plunger bottoming, then the desk and walls.</summary>
        private static float[] MouseClick(Rng r)
        {
            float[] b = Buf(0.12f);
            float p = r.Jitter(0.02f);
            AddImpact(b, 0, 1.00f, MouseSwitchModes, r, 4, 0.30f, 0.5f, p, 1f, 0.25f);
            AddImpact(b, Ms(0.85f), 0.40f, MouseSwitchModes, r, 5, 0.20f, 0.4f, p * 1.04f, 0.8f);
            AddImpact(b, Ms(2.6f), 0.26f, MouseSwitchModes, r, 8, 0.15f, 0.6f, p * 0.93f, 1.1f);
            AddRoom(b, 0.55f, 0.10f);
            LowPass(b, 9500f);
            return FinishOneShot(b, -19f, 0.1f, 25f);
        }

        /// <summary>The spring returning: lighter, higher, crisper.</summary>
        private static float[] MouseRelease(Rng r)
        {
            float[] b = Buf(0.10f);
            float p = r.Jitter(0.02f);
            AddImpact(b, 0, 0.70f, MouseReleaseModes, r, 4, 0.30f, 0.4f, p, 0.8f, 0.2f);
            AddImpact(b, Ms(0.6f), 0.30f, MouseReleaseModes, r, 4, 0.20f, 0.3f, p * 1.05f, 0.6f);
            AddRoom(b, 0.5f, 0.08f);
            LowPass(b, 10000f);
            return FinishOneShot(b, -25f, 0.1f, 25f);
        }

        /// <summary>90s office keyboard: keycap bottoming out on the plate, a small rock, then the upstroke.</summary>
        private static float[] KeyTap(Rng r)
        {
            float[] b = Buf(0.075f);
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
            AddImpact(b, Ms(r.Range(36f, 50f)), r.Range(0.14f, 0.24f), modes, r, 5, 0.3f, 0.8f, pitch * 1.15f, 0.6f);
            return FinishOneShot(b, -22f + r.Range(-1.2f, 1.2f), 0.1f, 8f);
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
            AddImpact(b, up, 0.22f, modes, r, 6, 0.3f, 0.8f, pitch * 1.12f, 0.6f);
            AddImpact(b, up, 0.5f, StabilizerModes, r, 3, 0.1f, 0.3f, pitch);
            return FinishOneShot(b, -21f, 0.1f, 10f);
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
            AddImpact(b, Ms(r.Range(56f, 64f)), 0.22f, modes, r, 6, 0.3f, 0.8f, pitch * 1.12f, 0.6f);
            return FinishOneShot(b, -20f, 0.1f, 10f);
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
            {
                float env = i < atk ? 0.5f - 0.5f * Sin01(0.25 + 0.5 * i / atk)
                          : i > on - rel ? 0.5f - 0.5f * Sin01(0.25 + 0.5 * (on - i) / rel) : 1f;
                b[i] = env * wt.At(p.Advance(f0));
            }
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
                    AddImpact(b, Sec(t), r.Range(0.5f, 1f), HddClickModes, r, 3, 0.4f, 0.3f, r.Jitter(0.05f));
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
                      + spindle * (0.018f * s1.Sin(90f) + 0.010f * s2.Sin(180f) + 0.004f * whine.Sin(2710f));
            }
            return FinishOneShot(b, -26f, 0.1f, 20f);
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
                float speed = f / 90f;
                float hum = 0.35f * p1.Sin(f) + 0.20f * p2.Sin(2f * f) + 0.10f * p3.Sin(3f * f);
                float whine = 0.10f * speed * speed * w1.Sin(8f * f)
                            + 0.05f * speed * speed * w2.Sin(24f * f)
                            + 0.02f * speed * speed * speed * w3.Sin(41f * f);
                float fade = SmoothStep(0f, 0.02f, t) * (1f - SmoothStep(1.8f, len, t));
                b[i] = fade * (speed * hum + whine + 0.06f * speed * air.Lp(r.Signed()));
            }
            Mode[] kick = { new Mode(120f, 25f, 0.8f), new Mode(380f, 12f, 0.4f), new Mode(1300f, 4f, 0.2f) };
            AddImpact(b, Ms(15f), 0.8f, kick, r, 24, 0.2f, 2f);
            AddImpact(b, Sec(1.42f), 0.45f, HddClickModes, r, 3, 0.4f, 0.3f);
            AddImpact(b, Sec(1.55f), 0.35f, HddClickModes, r, 3, 0.4f, 0.3f, 1.05f);
            AddImpact(b, Sec(1.61f), 0.40f, HddClickModes, r, 3, 0.4f, 0.3f, 0.97f);
            return FinishOneShot(b, -27f, 1f, 250f);
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

        /// <summary>Relay click, a low thump, the degauss "thoom" decaying, static crackles, the flyback settling in.</summary>
        private static float[] CrtOn(Rng r)
        {
            const float len = 1.25f;
            float[] b = Buf(len);
            AddImpact(b, 0, 0.9f, RelayModes, r, 4, 0.3f, 0.4f);
            AddImpact(b, Ms(1.8f), 0.45f, RelayModes, r, 5, 0.2f, 0.3f, 1.05f);

            Wavetable hum = MainsHum(r, 1f);
            var hiss = new Svf(6000f, 0.7f);
            var hissHp = new OnePole(1500f);
            Phasor thump = default, dg = default, whine = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float tt = t - 0.012f;
                if (tt >= 0f) b[i] += 0.8f * AttackDecay(tt, 0.004f, 0.09f) * thump.Sin(42f + 48f * Decay(tt, 0.05f));
                float td = t - 0.02f;
                if (td >= 0f) b[i] += 0.55f * AttackDecay(td, 0.03f, 0.28f) * SoftClip(1.6f * hum.At(dg.Advance(60f)));
                b[i] += 0.035f * AttackDecay(t - 0.08f, 0.1f, 0.25f) * hissHp.Hp(hiss.Lp(r.Signed()));
                float tw = t - 0.12f;
                if (tw >= 0f)
                    b[i] += 0.012f * SmoothStep(0f, 0.25f, tw) * whine.Sin(7860f - 900f * Decay(tw, 0.12f));
            }
            Mode[] crackle = { new Mode(2900f, 0.8f, 0.5f), new Mode(5200f, 0.5f, 0.4f), new Mode(1700f, 1.2f, 0.3f) };
            for (int k = 0; k < 6; k++)
                AddImpact(b, Sec(r.Range(0.08f, 0.7f)), r.Range(0.1f, 0.3f), crackle, r, 2, 0.8f, 0.3f, r.Jitter(0.2f));
            return FinishOneShot(b, -22f, 0.1f, 180f);
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
                AddImpact(b, Sec(r.Range(0.25f, 0.9f)), r.Range(0.05f, 0.15f), tick, r, 2, 0.6f, 0.3f, r.Jitter(0.2f));
            return FinishOneShot(b, -24f, 3f, 150f);
        }

        /// <summary>
        /// 1 s loop, eight grind strokes (swung, accented, two with stutter re-triggers): bit-crushed band-passed
        /// crunch, a comb-filtered grinding bed and a 55 Hz motor riding the stroke envelope.
        /// </summary>
        private static float[] ShredLoop(Rng r)
        {
            int n = Sec(1f);
            const int strokes = 8;
            float[] accents = { 1.0f, 0.55f, 0.8f, 0.5f, 1.0f, 0.6f, 0.85f, 0.45f };
            float[] b = new float[n];
            float[] env = new float[n];
            for (int k = 0; k < strokes; k++)
            {
                int at = k * n / strokes + (k % 2 == 1 ? Ms(9f) : 0);
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

            // grinding bed: comb-filtered noise (pitched ~110 Hz), gated by the strokes
            float[] grind = WhiteNoise(n, r);
            var comb = new float[Sec(1f / 110f)];
            int cp = 0;
            var gbp = new Svf(1000f, 0.8f);
            Circular(grind, x =>
            {
                float y = comb[cp];
                comb[cp] = x + 0.72f * y;
                if (++cp == comb.Length) cp = 0;
                return gbp.Bp(y);
            }, Ms(200f));

            var wt = new Wavetable(SawAmps(18, 1f));
            var motorOsc = new LoopOsc(55f, n);
            float[] motor = new float[n];
            for (int i = 0; i < n; i++) motor[i] = wt.At(motorOsc.Next());
            var mlp = new Svf(700f, 0.9f);
            Circular(motor, mlp.Lp, Ms(100f));

            for (int i = 0; i < n; i++)
            {
                float e = Math.Min(1f, env[i]);
                b[i] += 0.25f * e * grind[i] + 0.22f * (0.35f + 0.65f * e) * motor[i];
            }
            LowPassLoop(b, 9000f);
            return FinishLoop(b, -22f);
        }

        // ---------------- Ambience ----------------

        /// <summary>Night office: PC fan (pinkish air, 280 Hz blade tone, 40 Hz motor) over distant HVAC rumble.</summary>
        private static float[] AmbRoom(Rng r)
        {
            int n = Sec(8f);
            float[] fan = WhiteNoise(n, r);
            var pink = new PinkFilter();
            var lp = new Svf(1600f, 0.6f);
            var hp = new Svf(70f, 0.6f);
            Circular(fan, x => hp.Hp(lp.Lp(pink.Process(x))), Sec(0.5f));

            float[] hvac = WhiteNoise(n, r);
            float brown = 0f;
            var hlp = new Svf(170f, 0.6f);
            Circular(hvac, x => { brown = 0.997f * brown + 0.03f * x; return hlp.Lp(brown); }, Sec(0.5f));

            var blade = new LoopOsc(280f, n);
            var blade2 = new LoopOsc(560f, n);
            var motor = new LoopOsc(40f, n);
            var mains = new LoopOsc(120f, n);
            var wob1 = new LoopOsc(0.375f, n);
            var wob2 = new LoopOsc(1.125f, n);
            var swell = new LoopOsc(0.125f, n);
            float[] b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float w = 1f + 0.18f * Sin01(wob1.Next()) + 0.08f * Sin01(wob2.Next() + 0.3);
                float tones = w * (0.06f * Sin01(blade.Next()) + 0.025f * Sin01(blade2.Next()))
                            + 0.03f * Sin01(motor.Next()) + 0.012f * Sin01(mains.Next());
                float s = 1f + 0.12f * Sin01(swell.Next());
                b[i] = 0.5f * fan[i] + tones + 1.1f * s * hvac[i];
            }
            return FinishLoop(b, -36f);
        }

        /// <summary>
        /// Ballast buzz: one 60 Hz period (735 samples) of a 120 Hz-dominant harmonic series with a ~2.3 kHz
        /// "buzz" formant, tiled; slow flutter, two flicker dips and discharge hiss pulsing at 120 Hz.
        /// </summary>
        private static float[] AmbFluorescent(Rng r)
        {
            int n = Sec(4f);
            const int period = SampleRate / 60;
            var cycle = new float[period];
            for (int h = 1; h <= 100; h++)
            {
                float f = 60f * h;
                float a = h % 2 == 0 ? MathF.Pow(h / 2f, -0.85f) : 0.18f * MathF.Pow(h, -0.7f);
                a *= 1f + 1.8f * MathF.Exp(-Sq((f - 2300f) / 1400f));
                if (f > 4000f) a *= MathF.Exp(-(f - 4000f) / 1200f);
                double ph = r.Float();
                for (int i = 0; i < period; i++) cycle[i] += a * Sin01((double)h * i / period + ph);
            }

            float[] hiss = WhiteNoise(n, r);
            var hbp = new Svf(3200f, 0.8f);
            Circular(hiss, hbp.Bp, Ms(20f));

            var f1 = new LoopOsc(7.25f, n);
            var f2 = new LoopOsc(11.5f, n);
            var f3 = new LoopOsc(0.5f, n);
            float[] b = new float[n];
            for (int i = 0, c = 0; i < n; i++)
            {
                float t = i * Dt;
                float flutter = 1f + 0.025f * Sin01(f1.Next()) + 0.018f * Sin01(f2.Next()) + 0.04f * Sin01(f3.Next())
                              - 0.30f * Bump(t, 2.70f, 0.035f) - 0.12f * Bump(t, 0.90f, 0.015f);
                float g = Sq(Sq(Sin01((double)c / period)));
                b[i] = flutter * (cycle[c] + 0.35f * g * hiss[i]);
                if (++c == period) c = 0;
            }
            return FinishLoop(b, -40f);
        }

        /// <summary>Transformer hum (tiled 60 Hz period), a very faint wavering 7.9 kHz whine, and a little sizzle.</summary>
        private static float[] AmbCrtHum(Rng r)
        {
            int n = Sec(3f);
            const int period = SampleRate / 60;
            float[] amps = { 1f, 0.55f, 0.35f, 0.22f, 0.14f, 0.10f, 0.06f, 0.05f, 0.03f, 0.03f, 0.02f, 0.02f };
            var cycle = new float[period];
            for (int h = 0; h < amps.Length; h++)
            {
                double ph = r.Float();
                for (int i = 0; i < period; i++) cycle[i] += amps[h] * Sin01((double)(h + 1) * i / period + ph);
            }
            float[] sizzle = WhiteNoise(n, r);
            var sbp = new Svf(4500f, 0.7f);
            Circular(sizzle, sbp.Bp, Ms(20f));

            var whine = new LoopOsc(7867f, n);
            float[] b = new float[n];
            for (int i = 0, c = 0; i < n; i++)
            {
                double wp = whine.Next() + LoopFm(i, n, 2.5f, 1) + LoopFm(i, n, 1f, 5, 0.3);
                b[i] = cycle[c] + 0.018f * Sin01(wp) + 0.02f * sizzle[i];
                if (++c == period) c = 0;
            }
            return FinishLoop(b, -38f);
        }

        /// <summary>
        /// 8 s tension bed: a sub pair beating 3x per loop, an E2/F2/Bb2 cluster breathing through a slowly
        /// opening filter, and distant inharmonic metal partials swelling in and out through a long reverb.
        /// </summary>
        private static float[] DroneTension(Rng r)
        {
            int n = Sec(8f);
            float invN = 1f / n;

            // sub pair (41.25 / 41.625 Hz)
            var s1 = new LoopOsc(41.25f, n);
            var s2 = new LoopOsc(41.625f, n);

            // low cluster with independent periodic swells, then a slowly moving low-pass
            var wt = new Wavetable(SawAmps(14, 1.5f));
            var c1 = new LoopOsc(82.5f, n);
            var c2 = new LoopOsc(87.375f, n);
            var c3 = new LoopOsc(116.5f, n);
            float[] cluster = new float[n];
            for (int i = 0; i < n; i++)
            {
                double u = i * (double)invN;
                float a1 = 0.55f + 0.45f * Sin01(u);
                float a2 = 0.50f + 0.50f * Sin01(2 * u + 0.3);
                float a3 = 0.35f + 0.35f * Sin01(u + 0.55);
                cluster[i] = a1 * wt.At(c1.Next()) + a2 * wt.At(c2.Next()) + a3 * wt.At(c3.Next());
            }
            var clp = new Svf(400f, 1.1f);
            CircularIndexed(cluster, (i, x) =>
            {
                if ((i & 31) == 0) clp.Set(280f + 380f * (0.5f + 0.5f * Sin01(i * (double)invN + 0.6)), 1.1f);
                return clp.Lp(x);
            }, Sec(0.5f));

            // distant metal: struck-plate ratios on 311 Hz, each partial doubled 0.25 Hz apart (slow beats)
            float[] ratios = { 1f, 1.593f, 2.136f, 2.296f, 2.653f, 2.918f, 3.6f };
            float[] metal = new float[n];
            for (int k = 0; k < ratios.Length; k++)
            {
                float f = 311.1f * ratios[k];
                var oa = new LoopOsc(f, n, r.Float());
                var ob = new LoopOsc(f + 0.25f, n, r.Float());
                float centre = r.Float(), width = r.Range(0.25f, 0.45f);
                float amp = 0.06f / (1f + 0.4f * k);
                float vib = r.Range(0.4f, 1.2f);
                int vibCycles = r.Int(1, 4);
                float swell = 0f;
                for (int i = 0; i < n; i++)
                {
                    if ((i & 31) == 0)
                    {
                        float d = i * invN - centre;
                        d -= MathF.Round(d); // circular distance on the loop
                        swell = Bump(d, 0f, width);
                    }
                    double fm = LoopFm(i, n, vib, vibCycles);
                    metal[i] += amp * swell * (Sin01(oa.Next() + fm) + Sin01(ob.Next() + fm));
                }
            }
            AddReverbLoop(metal, 1.4f, 0.88f, 0.5f, 1.2f, 0.25f);

            // air
            float[] air = WhiteNoise(n, r);
            var pink = new PinkFilter();
            var abp = new Svf(380f, 0.7f);
            Circular(air, x => abp.Bp(pink.Process(x)), Sec(0.5f));

            float[] b = new float[n];
            var airSwell = new LoopOsc(0.125f, n, 0.4f);
            for (int i = 0; i < n; i++)
            {
                float sub = 0.5f * (Sin01(s1.Next()) + Sin01(s2.Next()));
                float s = 0.55f * sub + 0.35f * cluster[i] + 0.6f * metal[i]
                        + 0.12f * (0.6f + 0.4f * Sin01(airSwell.Next())) * air[i];
                b[i] = SoftClip(1.2f * s);
            }
            return FinishLoop(b, -27f, 22f);
        }

        // ---------------- The entity ----------------

        /// <summary>Slow "breath" of the entity's activity over a loop (0..1, periodic in u).</summary>
        private static float Breath(float u) => 0.5f - 0.5f * Sin01(u + 0.25);

        /// <summary>
        /// 2 s loop of electrical life: clustered spark crackles with individual resonant colours (denser in one
        /// half of the loop, so it breathes), a few 120 Hz arcing "fzzt"s, and a faint sizzle bed.
        /// </summary>
        private static float[] EntityStatic(Rng r)
        {
            int n = Sec(2f);
            float[] b = new float[n];

            for (int c = 0; c < 16; c++)
            {
                float pos;
                do pos = r.Float(); while (r.Float() > 0.3f + 0.7f * Breath(pos));
                int tail = Ms(10f);
                int evLen = Ms(r.Range(25f, 90f)) + tail;
                var ev = new float[evLen];
                int grains = r.Int(4, 22);
                float meanGap = r.Range(1.5f, 5f) * 0.001f * Sr;
                float amp = r.Range(0.25f, 1f);
                int t = 0;
                for (int g = 0; g < grains; g++)
                {
                    int w = r.Int(2, 14);
                    if (t + w >= evLen - tail) break;
                    float a = amp * (0.15f + 0.85f * MathF.Pow(r.Float(), 3f)) * (r.Chance(0.5f) ? 1f : -1f);
                    for (int j = 0; j < w; j++) ev[t + j] += a * r.Signed() * (1f - (float)j / w);
                    t += 1 + (int)(-MathF.Log(1f - 0.999f * r.Float()) * meanGap);
                }
                var bp = new Svf(r.Range(1300f, 5200f), r.Range(1.5f, 5f));
                var hp = new OnePole(2000f);
                for (int j = 0; j < evLen; j++)
                {
                    float x = ev[j];
                    ev[j] = 0.75f * bp.Bp(x) + 0.35f * hp.Hp(x);
                }
                AddWrapped(b, ev, (int)(pos * n));
            }

            for (int a = 0; a < 3; a++)
            {
                int len = Ms(r.Range(30f, 90f));
                var ev = new float[len];
                var bp = new Svf(r.Range(2000f, 3500f), 2.2f);
                Phasor gate = default, flicker = default;
                float fl = r.Range(25f, 45f);
                for (int j = 0; j < len; j++)
                {
                    float env = Sq(Sin01(0.5 * j / len)) * (0.7f + 0.3f * flicker.Sin(fl));
                    float g = MathF.Max(0f, gate.Sin(120f));
                    ev[j] = 0.8f * env * g * g * g * bp.Bp(r.Signed());
                }
                AddWrapped(b, ev, r.Int(0, n));
            }

            float[] bed = WhiteNoise(n, r);
            var bhp = new Svf(3000f, 0.7f);
            var blp = new Svf(8000f, 0.7f);
            Circular(bed, x => blp.Lp(bhp.Hp(x)), Ms(50f));
            for (int i = 0; i < n; i++) b[i] += 0.03f * (0.3f + 0.7f * Breath(i / (float)n)) * bed[i];

            LowPassLoop(b, 9000f);
            return FinishLoop(b, -30f);
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
                fwd[i] = AttackDecay(t, 0.002f, 0.18f) * tone + 0.6f * AttackDecay(t, 0.0005f, 0.03f) * bp.Bp(r.Signed());
            }
            AddReverb(fwd, 1.2f, 0.87f, 0.45f, 1.4f, 0.5f);

            float[] b = Buf(1.0f);
            int m = fwd.Length;
            for (int i = 0; i < m; i++)
            {
                float u = (float)i / m;
                b[i] = fwd[m - 1 - i] * u * u;
            }
            AddGlitchTick(b, m, 1f, r);
            AddGlitchTick(b, m + Ms(21f), 0.45f, r);
            AddGlitchTick(b, m + Ms(37f), 0.22f, r);
            Phasor low = default;
            for (int i = m; i < b.Length; i++)
                b[i] += 0.4f * AttackDecay((i - m) * Dt, 0.0005f, 0.008f) * low.Sin(180f);
            LowPass(b, 9500f);
            return FinishOneShot(b, -24f, 5f, 60f);
        }

        /// <summary>Segments of crushed noise, crushed square tones, zips, gaps and stutter repeats.</summary>
        private static float[] GlitchBurst(Rng r)
        {
            float[] b = Buf(0.32f);
            int pos = 0, end = Sec(0.29f), lastStart = 0, lastLen = 0;
            while (pos < end)
            {
                int len = Math.Min(end - pos, Ms(r.Range(8f, 34f)));
                int kind = r.Int(0, 10);
                float level = r.Range(0.5f, 1f);
                if (kind >= 5 && kind < 7 && lastLen > 0)
                {
                    int chunk = Math.Min(lastLen, Ms(r.Range(5f, 14f)));
                    int reps = r.Int(2, 5);
                    len = 0;
                    for (int k = 0; k < reps && pos + len + chunk <= b.Length; k++, len += chunk)
                        for (int j = 0; j < chunk; j++) b[pos + len + j] = b[lastStart + j] * (1f - 0.12f * k);
                }
                else if (kind == 7)
                {
                    len = Ms(r.Range(4f, 12f));
                }
                else
                {
                    int hold = r.Int(2, 9);
                    float levels = r.Range(3f, 12f), held = 0f;
                    float f0 = r.Range(180f, 2400f), f1 = kind >= 8 ? r.Range(150f, 400f) : f0;
                    Phasor p = default;
                    for (int j = 0; j < len; j++)
                    {
                        float u = (float)j / len;
                        float f = f0 * MathF.Pow(f1 / f0, u);
                        float tone = p.Sin(f) >= 0f ? 0.6f : -0.6f;
                        float v = kind < 3 ? r.Signed() : (kind >= 8 ? 1.4f * Sin01(p.Phase) : tone + 0.25f * r.Signed());
                        if (j % hold == 0) held = Crush(v, levels);
                        b[pos + j] = level * held;
                    }
                    lastStart = pos;
                    lastLen = len;
                }
                pos += Math.Max(len, 1);
            }
            LowPass(b, 9000f);
            return FinishOneShot(b, -20f, 0.5f, 25f);
        }

        /// <summary>
        /// 1.5 s loop: a beating 98 / 103.3 / 146.7 Hz buzz through a resonant band-pass that sweeps twice per
        /// loop, a vibrato whine, a 4 Hz throb and crackling grit. Every rate stays musical from 0.8x to 1.8x,
        /// and the spectrum is kept under 6 kHz so even 1.8x never gets shrill.
        /// </summary>
        private static float[] TugStrain(Rng r)
        {
            int n = Sec(1.5f);
            var wt = new Wavetable(SawAmps(20, 1.25f));
            var o1 = new LoopOsc(98f, n);
            var o2 = new LoopOsc(103.333f, n);
            var o3 = new LoopOsc(146.667f, n);
            float[] buzz = new float[n];
            for (int i = 0; i < n; i++)
                buzz[i] = 0.5f * wt.At(o1.Next()) + 0.45f * wt.At(o2.Next())
                        + 0.25f * wt.At(PositiveFrac(o3.Next() + LoopFm(i, n, 1.5f, 3)));
            var bp = new Svf(900f, 3.5f);
            CircularIndexed(buzz, (i, x) =>
            {
                if ((i & 15) == 0) bp.Set(900f * MathF.Pow(1.6f, Sin01(2.0 * i / n)), 3.5f);
                return 0.6f * bp.Bp(x) + 0.25f * x;
            }, Ms(200f));

            float[] grit = WhiteNoise(n, r);
            var gbp = new Svf(2200f, 1.5f);
            Circular(grit, gbp.Bp, Ms(20f));
            var gate = new float[n];
            for (int k = 0; k < 14; k++)
            {
                var ev = new float[Ms(r.Range(8f, 30f))];
                for (int j = 0; j < ev.Length; j++) ev[j] = Sq(Sin01(0.5 * j / ev.Length));
                AddWrapped(gate, ev, r.Int(0, n), r.Range(0.4f, 1f));
            }

            var whine = new LoopOsc(1306.667f, n);
            var throb = new LoopOsc(4f, n);
            float[] b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float th = 0.78f + 0.22f * Sin01(throb.Next());
                b[i] = th * buzz[i]
                     + 0.06f * Sin01(whine.Next() + LoopFm(i, n, 6f, 9))
                     + 0.05f * Math.Min(1f, gate[i]) * grit[i];
                b[i] = SoftClip(1.3f * b[i]);
            }
            LowPassLoop(b, 6000f);
            return FinishLoop(b, -25f);
        }

        private static double PositiveFrac(double x) => x - Math.Floor(x);

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
            LowPass(b, 9500f);
            return FinishOneShot(b, -19f, 0.1f, 30f);
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
            var lp = new Svf(6500f, 0.7f);
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
                b[i] += 0.5f * env * level * field * lp.Lp(hp.Hp(r.Signed()));
            }
            AddImpact(b, Sec(0.296f), 0.2f, RelayModes, r, 3, 0.1f, 0.2f, 1.4f, 0.4f);
            return FinishOneShot(b, -22f, 0.1f, 30f);
        }

        /// <summary>Soft CCTV snow: filtered noise with a 60 Hz field buzz, gentle drift and a trace of hum.</summary>
        private static float[] CameraStatic(Rng r)
        {
            int n = Sec(2f);
            float[] b = WhiteNoise(n, r);
            var lp = new Svf(6500f, 0.6f);
            var lp2 = new OnePole(9000f);
            var hp = new Svf(180f, 0.7f);
            Circular(b, x => hp.Hp(lp.Lp(lp2.Lp(x))), Ms(50f));
            var field = new LoopOsc(60f, n);
            var m1 = new LoopOsc(0.5f, n);
            var m2 = new LoopOsc(1.5f, n, 0.3f);
            var m3 = new LoopOsc(3.5f, n, 0.7f);
            var h1 = new LoopOsc(60f, n, 0.1f);
            var h2 = new LoopOsc(120f, n, 0.6f);
            for (int i = 0; i < n; i++)
            {
                float buzz = 0.88f + 0.12f * Sin01(field.Next());
                float drift = 1f + 0.06f * Sin01(m1.Next()) + 0.04f * Sin01(m2.Next()) + 0.03f * Sin01(m3.Next());
                b[i] = b[i] * buzz * drift + 0.03f * (0.6f * Sin01(h1.Next()) + 0.4f * Sin01(h2.Next()));
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
            return FinishOneShot(b, -18f, 0.3f, 200f, 22f);
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
            AddReverb(b, 1.6f, 0.9f, 0.6f, 2.2f, 0.35f, 28f, 1800f);
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
            AddFlutter(b, 9.5f, 6, 0.45f, 0.72f, 1800f);
            AddReverb(b, 1.15f, 0.85f, 0.55f, 1.6f, 0.4f, 12f, 2200f);
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
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt, tc = t - cut;
                bool on = tc < 0f;
                float mains = on ? 1f : Decay(tc, 0.05f);
                float sag = on ? 1f : 1f - 0.3f * (1f - Decay(tc, 0.08f));
                float flick = on ? 1f : 0.6f * Bump(tc, 0.06f, 0.025f) + 0.35f * Bump(tc, 0.15f, 0.02f);
                float spin = on ? 1f : Decay(tc, 0.55f);
                if ((i & 31) == 0) fanBp.Set(250f + 450f * spin, 0.7f);
                float s = 0.30f * mains * hum.At(hp.Advance(60f * sag))
                        + 0.12f * flick * buzz.At(bz.Advance(120f * sag))
                        + 0.020f * spin * wh.Sin(6500f * (0.25f + 0.75f * spin))
                        + 0.10f * spin * fanBp.Bp(r.Signed())
                        + 0.05f * spin * blade.Sin(280f * spin);
                if (!on) s += 0.9f * AttackDecay(tc, 0.004f, 0.25f) * whump.Sin(34f + 36f * Decay(tc, 0.06f));
                b[i] = s;
            }
            AddImpact(b, Sec(cut), 1f, BreakerModes, r, 12, 0.5f, 2f);

            float[] relays = Buf(len);
            AddImpact(relays, Sec(cut + 0.19f), 0.5f, RelayModes, r, 5, 0.3f, 0.4f, 0.8f);
            AddImpact(relays, Sec(cut + 0.37f), 0.35f, RelayModes, r, 5, 0.3f, 0.4f, 0.7f);
            AddImpact(relays, Sec(cut + 0.71f), 0.22f, RelayModes, r, 6, 0.3f, 0.4f, 0.65f);
            LowPass(relays, 3500f);
            Mix(b, relays, 1f);
            AddReverb(b, 1.3f, 0.86f, 0.5f, 0.6f);
            return FinishOneShot(b, -22f, 30f, 300f);
        }

        /// <summary>A3 pair beating slowly, a sub-octave, a tritone shadow and a glassy A6 that arrives late;
        /// everything sags ~20 cents as it fades.</summary>
        private static float[] EndTone(Rng r)
        {
            const float len = 6f;
            float[] b = Buf(len);
            Phasor a = default, a2 = default, sub = default, tri = default, h3 = default, glass = default, vib = default;
            a2.Phase = r.Float();
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float sag = 1f - 0.012f * SmoothStep(3f, 6f, t);
                float env = SmoothStep(0f, 1.3f, t) * (1f - SmoothStep(3.4f, 5.95f, t));
                float glassEnv = SmoothStep(1.2f, 2.8f, t) * (1f - SmoothStep(3.6f, 5.8f, t));
                float v = 1f + 0.0018f * vib.Sin(4.7f);
                b[i] = env * (0.42f * a.Sin(220f * sag) + 0.42f * a2.Sin(220.45f * sag)
                            + 0.22f * sub.Sin(110f * sag) + 0.12f * tri.Sin(311.13f * sag)
                            + 0.05f * h3.Sin(660f * sag) + 0.05f * glassEnv * glass.Sin(1760f * sag * v));
            }
            AddReverb(b, 1.5f, 0.88f, 0.45f, 0.5f);
            return FinishOneShot(b, -24f, 10f, 400f);
        }
    }
}
