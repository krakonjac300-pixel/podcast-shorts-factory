// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, sound designs: the player's own actions (OS / UI) and the physical input devices
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
        //  3. Sound designs
        // ====================================================================

        // ---------------- OS / UI: the player's own actions. Clean, dry, digital. ----------------

        /// <summary>Soft rounded "tk": a gliding sine tick over a warm body, no room at all.</summary>
        private static float[] UiClick(Rng r) => FinishOneShot(UiClickRaw(r, 1f), -21f, 0.2f, 6f, 9000f);

        /// <summary>The click before finishing; <paramref name="speed"/> &lt; 1 plays it slower and lower (click_wrong's answer).</summary>
        private static float[] UiClickRaw(Rng r, float speed)
        {
            float[] b = Buf(0.034f / speed);
            var edge = new Svf(3600f * speed, 1.1f);
            Phasor tick = default, body = default, low = default;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt * speed;
                float f = 1900f + 500f * Decay(t, 0.003f);
                b[i] = 0.90f * AttackDecay(t, 0.0004f, 0.0032f) * tick.Sin(f * speed)
                     + 0.45f * AttackDecay(t, 0.0006f, 0.0060f) * body.Sin(1040f * speed)
                     + 0.20f * AttackDecay(t, 0.0010f, 0.0085f) * low.Sin(430f * speed)
                     + 0.28f * AttackDecay(t, 0.0002f, 0.0011f) * edge.Bp(r.Signed());
            }
            return b;
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
    }
}
