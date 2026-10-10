// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, sound designs: hardware
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
    }
}
