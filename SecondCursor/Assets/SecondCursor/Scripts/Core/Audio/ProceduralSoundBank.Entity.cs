// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, sound designs: the entity
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
    }
}
