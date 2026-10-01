// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, the DSP toolkit, part 3: buffers, loops, impacts, tonal blocks and finishing
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
    }
}
