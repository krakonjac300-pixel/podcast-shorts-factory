// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, sound designs: CCTV and the building
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
        /// A desk phone ring: 440 + 480 Hz with a 20 Hz amplitude warble (the bell striking), 2.0 s, a little
        /// room, levelled like notify_mail.
        /// </summary>
        private static float[] PhoneRing(Rng r)
        {
            float[] b = Buf(2.0f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float warble = 0.55f + 0.45f * MathF.Sin(TwoPi * 20f * t);
                float tone = 0.5f * (MathF.Sin(TwoPi * 440f * t) + MathF.Sin(TwoPi * 480f * t));
                b[i] = 0.6f * warble * tone;
            }
            LowPass(b, 3400f);
            AddReverb(b, 0.35f, 0.6f, 0.5f, 0.18f);
            return FinishOneShot(b, -20f, 8f, 60f);
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
