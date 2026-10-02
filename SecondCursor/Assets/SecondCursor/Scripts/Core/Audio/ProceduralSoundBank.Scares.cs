// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, part 2: the scares (Phase M)
//  Same toolkit and rules as part 1. What is wrong is physical and close: a knock on your door, your chair,
//  a breath at the microphone, a step behind you; and one pre-mixed hit, the loudest sound in the game.
//  Reference implementation and measurements: _work/2026-09-29/sound/new_sounds.py, report_new.txt.
// ============================================================================

using System;

namespace SecondCursor.Core.Audio
{
    public static partial class ProceduralSoundBank
    {
        // ---------------- Scares: rare, quiet, close ----------------

        private static readonly Mode[] KnockModes =
        {
            new Mode(96f, 70f, 1.00f),    // hollow-core door panel
            new Mode(178f, 45f, 0.75f),
            new Mode(262f, 32f, 0.60f),
            new Mode(415f, 20f, 0.55f),
            new Mode(690f, 11f, 0.45f),
            new Mode(1140f, 6f, 0.30f),
            new Mode(2050f, 3.5f, 0.15f),
        };

        private static readonly Mode[] KnuckleModes = { new Mode(3200f, 1.5f, 0.35f), new Mode(4700f, 0.9f, 0.20f) };

        /// <summary>Three slow knuckle knocks on the office door behind you, inside the room, a little muffled.</summary>
        private static float[] KnockDoor(Rng r)
        {
            float[] b = Buf(2.2f);
            float[] at = { 0.05f, 0.58f, 1.12f }, amp = { 1f, 0.9f, 0.78f };
            for (int k = 0; k < at.Length; k++)
            {
                float p = r.Jitter(0.02f);
                AddImpact(b, Sec(at[k]), amp[k], KnockModes, r, 30, 0.25f, 1.2f, p);
                AddImpact(b, Sec(at[k]), amp[k] * 0.8f, KnuckleModes, r, 4, 0.2f, 0.3f, p);
            }
            LowPass(b, 2600f);
            AddRoom(b, 1f, 0.25f);
            AddReverb(b, 0.6f, 0.78f, 0.55f, 0.18f);
            return FinishOneShot(b, -27f, 1f, 250f, 6000f, true);
        }

        private static readonly Mode[] CreakModes =
        {
            new Mode(165f, 45f, 0.35f),   // spring
            new Mode(410f, 18f, 0.50f),   // plastic shell
            new Mode(730f, 14f, 0.80f),
            new Mode(1180f, 10f, 1.00f),
            new Mode(1720f, 8f, 0.70f),
            new Mode(2650f, 5f, 0.40f),
            new Mode(3900f, 3f, 0.20f),
        };

        /// <summary>Your chair's backrest taking weight: stick-slip pulses gliding 14 -> 38 -> 22 Hz through spring and shell modes.</summary>
        private static float[] ChairCreak(Rng r)
        {
            float[] b = Buf(1.4f);
            int n = b.Length;
            var pulses = new float[n];
            for (float t = 0.12f; t < 1.25f;)
            {
                float rate = 14f + 24f * Bump(t, 0.6f, 0.55f) + 4f * r.Signed();
                float env = Bump(t, 0.62f, 0.62f) * (0.7f + 0.3f * r.Float());
                if (!r.Chance(0.12f))
                {
                    int i0 = Sec(t);
                    for (int j = 0; j < 3 && i0 + j < n; j++) pulses[i0 + j] += env * Sin01(0.5 * (j + 0.5) / 3);
                }
                t += 1f / Math.Max(rate, 5f);
            }
            var res = new Resonator[CreakModes.Length];
            for (int k = 0; k < res.Length; k++) res[k] = new Resonator(CreakModes[k].Freq, CreakModes[k].TauMs * 0.001f, CreakModes[k].Gain);
            var smooth = new OnePole(80f);
            var friction = new Svf(2200f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float x = pulses[i], s = 0f;
                foreach (Resonator q in res) s += q.Process(x);
                b[i] = s + 0.06f * smooth.Lp(MathF.Abs(x) * 12f) * friction.Bp(r.Signed());
            }
            LowPass4(b, 6500f);
            AddRoom(b, 0.8f, 0.12f);
            return FinishOneShot(b, -28f, 2f, 120f, 7000f, true);
        }

        private static readonly Mode[] MouthClickModes = { new Mode(2400f, 1.2f, 0.5f), new Mode(3800f, 0.8f, 0.3f) };

        /// <summary>A short inhale, a gap, then a slow exhale right at the microphone ("hhh" darkening to "haa"). Dry: it is next to you.</summary>
        private static float[] BreathNear(Rng r)
        {
            float[] b = Buf(2.4f);
            var pink = new PinkFilter();
            var turbulence = new OnePole(12f);
            Svf f1 = new Svf(420f, 3f), f2 = new Svf(1650f, 4f), f3 = new Svf(2600f, 5f);
            Svf airHp = new Svf(300f), airLp = new Svf(5000f), proximity = new Svf(180f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float inhale = 0.35f * Bump(t, 0.55f, 0.4f);
                float exhale = t < 1.05f ? 0f : t < 1.23f ? Rise((t - 1.05f) / 0.18f) : Decay(t - 1.23f, 0.45f);
                if ((i & 31) == 0) f2.Set(t < 1f ? 1650f : 1650f - 450f * SmoothStep(1.05f, 2.2f, t), 4f);
                float w = pink.Process(r.Signed()) * 4f;
                float x = w * (1f + 7f * turbulence.Lp(r.Signed()));
                float voiced = 0.5f * f1.Bp(x) + 0.35f * f2.Bp(x) + 0.2f * f3.Bp(x);
                b[i] = (inhale + exhale) * (voiced + 0.35f * airLp.Lp(airHp.Hp(x)) + 0.25f * proximity.Lp(x));
            }
            AddImpact(b, Sec(1.02f), 0.15f, MouthClickModes, r, 3, 0.3f, 0.3f);
            return FinishOneShot(b, -33f, 20f, 200f, 7000f);
        }

        private static readonly float[] VowelF1 = { 300f, 500f, 700f, 400f, 350f };
        private static readonly float[] VowelF2 = { 2300f, 1500f, 1100f, 900f, 1900f };

        /// <summary>Half-heard unvoiced syllables (formant-filtered noise, some with an s or a t), played backwards.</summary>
        private static float[] WhisperBurst(Rng r)
        {
            float[] b = Buf(1.3f);
            int n = b.Length;
            float t = 0.10f;
            for (int k = 0; t < 1.12f && k < 6; k++)
            {
                float dur = r.Range(0.09f, 0.19f);
                int v = r.Int(0, VowelF1.Length);
                var bp1 = new Svf(VowelF1[v], r.Range(5f, 7f));
                var bp2 = new Svf(VowelF2[v], r.Range(5f, 7f));
                float level = 1f - 0.5f * t / 1.12f, consonant = r.Float();
                Svf s = new Svf(4500f), tk = new Svf(2500f);
                int i0 = Sec(t), len = Sec(dur + 0.05f);
                for (int j = 0; j < len && i0 + j < n; j++)
                {
                    float tt = j * Dt;
                    float env = tt < dur + 0.04f ? Rise(tt / 0.012f) * (1f - Rise((tt - dur) / 0.04f)) : 0f;
                    float w = r.Signed();
                    float y = 0.7f * bp1.Bp(w) + 0.5f * bp2.Bp(w);
                    if (consonant < 0.3f && tt < 0.04f) y += 0.5f * s.Hp(w) * (1f - tt / 0.04f);
                    else if (consonant > 0.8f && tt < 0.006f) y += 0.6f * tk.Hp(w);
                    b[i0 + j] += level * env * y;
                }
                t += dur + r.Range(0.03f, 0.08f);
            }
            var hp = new Svf(250f);
            for (int i = 0; i < n; i++) b[i] = hp.Hp(b[i]);
            LowPass4(b, 7000f);
            AddReverb(b, 0.35f, 0.7f, 0.6f, 0.12f);
            Array.Reverse(b);
            return FinishOneShot(b, -32f, 5f, 120f);
        }

        private static readonly Mode[] KeyTapRevModes =
        {
            new Mode(290f, 14f, 0.40f), new Mode(1230f, 6f, 0.55f), new Mode(2080f, 4.5f, 0.80f),
            new Mode(3240f, 3f, 0.60f), new Mode(4650f, 2f, 0.35f), new Mode(6100f, 1.3f, 0.18f),
        };

        /// <summary>One of her keystrokes (a key tap with a big tail) played backwards: it swells into the key.</summary>
        private static float[] KeyTapRev(Rng r)
        {
            float[] fwd = Buf(0.7f);
            AddImpact(fwd, Ms(20f), 1f, KeyTapRevModes, r, 8, 0.35f, 1.5f, 0.85f, 1f, 0.15f);
            AddImpact(fwd, Ms(22.5f), 0.3f, KeyTapRevModes, r, 8, 0.2f, 1f, 0.85f * 1.03f, 0.8f);
            AddReverb(fwd, 0.9f, 0.86f, 0.45f, 1f, 0.8f);
            float[] b = Buf(0.72f);
            int m = fwd.Length;
            for (int i = 0; i < m; i++)
            {
                float u = (float)i / m;
                b[i] = fwd[m - 1 - i] * u * u;
            }
            return FinishOneShot(b, -28f, 5f, 8f, 9000f, true);
        }

        /// <summary>Your own clean click, answered 85 ms later by a slower, crushed, lower copy out in the room (a mouse you did not press).</summary>
        private static float[] ClickWrong(Rng r)
        {
            float[] b = Buf(0.4f);
            Mix(b, UiClickRaw(r, 1f), 0.6f);
            float[] wrong = UiClickRaw(r, 0.62f);
            var bp = new Svf(900f, 0.9f);
            float held = 0f;
            for (int j = 0; j < wrong.Length; j++)
            {
                if (j % 4 == 0) held = Crush(wrong[j] * 1.6f, 10f);
                wrong[j] = 0.7f * bp.Bp(held) + 0.3f * held;
            }
            float[] room = Buf(0.4f);
            int at = Ms(85f);
            for (int j = 0; j < wrong.Length && at + j < room.Length; j++) room[at + j] += 0.9f * wrong[j];
            AddImpact(room, at, 0.3f, MouseSwitchModes, r, 5, 0.2f, 0.4f, 0.55f);
            AddRoom(room, 1f, 0.1f);
            Mix(b, room, 1f);
            return FinishOneShot(b, -27f, 0.2f, 30f, 9000f, true);
        }

        private static readonly float[] ShelfRatios = { 1f, 1.593f, 2.136f, 2.296f, 2.653f, 3.6f };

        /// <summary>Something heavy dragged over a steel shelf far below (Sublevel C): friction through plate modes, one squeal, a concrete room.</summary>
        private static float[] MetalScrape(Rng r)
        {
            float[] b = Buf(2.8f);
            var hz = new float[ShelfRatios.Length];
            var gains = new float[hz.Length];
            for (int k = 0; k < hz.Length; k++)
            {
                hz[k] = 233f * ShelfRatios[k] * r.Jitter(0.004f);
                gains[k] = 0.12f / (1f + 0.35f * k);
            }
            var plate = new ResonatorBank(hz, 0.25f, gains);
            var friction = new Svf(1800f, 0.8f);
            Phasor slip = default, squeal = default;
            float slipRate = 55f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                if ((i & 255) == 0) slipRate = Math.Max(40f, Math.Min(75f, slipRate + 3f * r.Signed()));
                float gate = 0.55f + 0.45f * slip.Sin(slipRate);
                float env = Rise(t / 0.25f) * (0.7f + 0.3f * SinWrap(0.7f * t)) * (1f - SmoothStep(2.3f, 2.65f, t));
                float x = friction.Bp(r.Signed()) * gate * env;
                plate.Process(x);
                float s = 0f;
                foreach (float y in plate.Out) s += y;
                float squ = 0.08f * Bump(t, 1.4f, 0.3f) * squeal.Sin(1870f + 12f * SinWrap(5.5f * t));
                b[i] = 3f * s + 0.35f * x + 0.5f * SoftClip(2f * squ);
            }
            LowPass(b, 2400f);
            AddReverb(b, 1.4f, 0.88f, 0.55f, 1.2f, 0.55f, 25f, 2200f);
            return FinishOneShot(b, -31f, 30f, 400f);
        }

        private static readonly Mode[] CarpetModes = { new Mode(70f, 60f, 1f), new Mode(140f, 35f, 0.6f), new Mode(240f, 22f, 0.35f), new Mode(520f, 10f, 0.15f) };
        private static readonly Mode[] FloorCreakModes = { new Mode(610f, 25f, 0.6f) };

        /// <summary>One heavy step on office carpet a metre behind the chair: soft heel, a floor creak, the sole's scuff.</summary>
        private static float[] StepNear(Rng r)
        {
            float[] b = Buf(0.7f);
            AddImpact(b, Ms(20f), 1f, CarpetModes, r, 110, 0.15f, 3f, r.Jitter(0.03f));
            AddImpact(b, Ms(60f), 0.35f, FloorCreakModes, r, 40, 0.1f, 1f);
            var bp = new Svf(1400f, 0.7f);
            var lp = new Svf(3000f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt - 0.045f;
                if (t >= 0f && t < 0.25f) b[i] += 0.25f * AttackDecay(t, 0.02f, 0.06f) * lp.Lp(bp.Bp(r.Signed()));
            }
            AddRoom(b, 0.9f, 0.08f);
            return FinishOneShot(b, -24f, 1f, 120f, 5000f, true);
        }

        // ---------------- Build-ups: they end at their loudest, where the silence before a hit begins ----------------

        /// <summary>4 s of pressure: 36 -> 46 Hz with a beating twin, saturated so small speakers hear it, rising fastest at the end.</summary>
        private static float[] SubSwell(Rng r)
        {
            float[] b = Buf(4f);
            Phasor p1 = default, p2 = default, p3 = default;
            var pink = new PinkFilter();
            var rumble = new Svf(220f);
            var hiss = new Svf(3200f, 0.8f);
            float e3 = MathF.Exp(3f) - 1f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float f = 36f + 10f * SmoothStep(0f, 4f, t);
                float env = (MathF.Exp(3f * t / 4f) - 1f) / e3;
                float tone = p1.Sin(f) + 0.8f * p2.Sin(f * 1.018f) + 0.25f * p3.Sin(f * 3.02f);
                float w = r.Signed();
                b[i] = SoftClip(1.1f * env * tone) + 0.3f * env * rumble.Lp(pink.Process(w) * 4f) + 0.04f * env * env * hiss.Bp(w);
            }
            return FinishOneShot(b, -26f, 400f, 25f, 0f, true, 20f);
        }

        /// <summary>The monitor's flyback whine climbing 7.87 -> 9.13 kHz with a slow beat and flutter, over a growing 60 Hz hum and sizzle.</summary>
        private static float[] CrtWhineRise(Rng r)
        {
            float[] b = Buf(3.5f);
            Wavetable hum = MainsHum(r, 0.8f);
            Phasor w1 = default, w2 = default, mains = default;
            var flutter = new OnePole(20f);
            var sizzle = new Svf(4500f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float f = 7870f * (1f + 0.16f * SmoothStep(0.3f, 3.4f, t)) + 8f * SinWrap(6f * t);
                float env = MathF.Pow(SmoothStep(0f, 3.3f, t), 1.5f);
                float fl = 1f - 0.3f * Math.Max(0f, flutter.Lp(r.Signed()) * 8f);
                float whine = w1.Sin(f) + 0.6f * w2.Sin(f * 1.0006f);
                b[i] = 0.05f * env * fl * whine + (0.2f + 0.8f * env) * 0.02f * hum.At(mains.Advance(60f)) + 0.03f * env * sizzle.Bp(r.Signed());
            }
            LowPass4(b, 11000f);
            return FinishOneShot(b, -31f, 200f, 20f);
        }

        // ---------------- The hit ----------------

        private static readonly Mode[] SlamModes =
        {
            new Mode(85f, 40f, 0.5f), new Mode(190f, 30f, 0.6f), new Mode(340f, 20f, 0.7f), new Mode(620f, 12f, 0.8f),
            new Mode(1100f, 7f, 0.7f), new Mode(1900f, 4f, 0.5f), new Mode(3200f, 2.5f, 0.3f),
        };

        private static readonly float[] ClusterNotes = { 110f, 116.54f, 164.81f, 311.13f, 783.99f, 830.61f };

        /// <summary>
        /// The stinger, the loudest sound in the game. The hit lands 0.10 s into the clip (a 90 ms reversed-noise pre-roll sucks
        /// into it): a sub kick 128 -> 38 Hz, a close body slam, a torn six-note cluster through a closing low-pass, a falling CRT
        /// shriek, the entity's crushed crunch, a dark tail. Callers play it 0.10 s before the visual cut.
        /// </summary>
        private static float[] ScareHit(Rng r) => BuildScareHit(r, false);

        /// <summary>The same hit for Reduce flashing: no pre-roll, no noise in the slam, no crunch, slower attacks, 8 dB quieter.</summary>
        private static float[] ScareHitSoft(Rng r) => BuildScareHit(r, true);

        private static float[] BuildScareHit(Rng r, bool soft)
        {
            float[] b = Buf(2.6f);
            int n = b.Length, hit = Sec(0.10f);
            if (!soft)
            {
                var bp = new Svf(2000f, 0.9f);
                int pre = Sec(0.09f);
                for (int j = 0; j < pre; j++)
                {
                    float u = (float)j / pre;
                    b[hit - pre + j] += 0.35f * u * u * u * bp.Bp(r.Signed());
                }
            }
            // Sub kick.
            float kick = soft ? 0.4f : 0.6f, kickAttack = soft ? 0.012f : 0.002f;
            Phasor k = default;
            for (int i = hit; i < n; i++)
            {
                float t = (i - hit) * Dt;
                b[i] += kick * SoftClip(1.8f * AttackDecay(t, kickAttack, 0.32f) * k.Sin(38f + 90f * Decay(t, 0.035f)));
            }
            // Body slam (mid-weighted: door modes measured quieter than a notification on small speakers).
            AddImpact(b, hit, soft ? 0.6f : 1f, SlamModes, r, soft ? 90 : 18, soft ? 0f : 0.9f, 6f, 1f, 1f, soft ? 0f : 0.5f);
            // Torn cluster: two voices per note, +/-9 cents, each saw band-limited to 11 kHz.
            var tables = new Wavetable[ClusterNotes.Length * 2];
            var hz = new float[tables.Length];
            var voices = new Phasor[tables.Length];
            for (int v = 0; v < tables.Length; v++)
            {
                hz[v] = ClusterNotes[v / 2] * MathF.Pow(2f, (v % 2 == 0 ? 9f : -9f) / 1200f);
                tables[v] = new Wavetable(SawAmps(Math.Min(24, (int)(11000f / hz[v])), 1.1f));
                voices[v].Phase = r.Float();
            }
            float attack = soft ? 0.025f : 0.004f, cutoff = soft ? 3500f : 6000f, cutoffEnd = soft ? 700f : 900f, gain = soft ? 1f : 1.2f;
            var lp = new Svf(cutoff, 0.9f);
            for (int i = hit; i < n; i++)
            {
                float t = (i - hit) * Dt;
                if ((i & 31) == 0) lp.Set((cutoff - cutoffEnd) * Decay(t, 0.35f) + cutoffEnd, 0.9f);
                float s = 0f;
                for (int v = 0; v < tables.Length; v++) s += tables[v].At(voices[v].Advance(hz[v]));
                float env = Rise(t / attack) * (1f + 1.2f * Decay(t, 0.025f)) * Decay(t, 0.6f);
                b[i] += gain * lp.Lp(s * env / 24f);
            }
            // CRT shriek: two detuned saws falling 1400 -> 700 Hz, band-passed at 1.8 kHz, saturated.
            var shriekBp = new Svf(1800f, 1.2f);
            float shriek = soft ? 0.2f : 0.35f, shriekAttack = soft ? 0.03f : 0.003f;
            Phasor q1 = default, q2 = default;
            for (int i = hit, end = Math.Min(n, hit + Sec(0.9f)); i < end; i++)
            {
                float t = (i - hit) * Dt, f = 700f + 700f * Decay(t, 0.25f);
                float saw = (2f * (float)q1.Advance(f) - 1f) + (2f * (float)q2.Advance(f * 1.0175f) - 1f);
                b[i] += shriek * SoftClip(2.5f * AttackDecay(t, shriekAttack, 0.22f) * shriekBp.Bp(saw));
            }
            if (!soft)
            {
                // The entity's crunch.
                AddGlitchTick(b, hit, 1f, r);
                AddGlitchTick(b, hit + Ms(18f), 0.6f, r);
                AddGlitchTick(b, hit + Ms(41f), 0.35f, r);
                var crunchBp = new Svf(1600f, 1f);
                float held = 0f;
                int len = Ms(120f);
                for (int j = 0; j < len; j++)
                {
                    if (j % 5 == 0) held = Crush(r.Signed(), 6f);
                    b[hit + j] += 0.5f * (1f - (float)j / len) * crunchBp.Bp(held);
                }
            }
            AddReverb(b, 1.3f, 0.87f, 0.5f, soft ? 0.7f : 0.55f, 1f, 8f, 3000f);
            return FinishOneShot(b, soft ? -23.5f : -20f, 0.3f, 500f, 10000f, true, 22f);
        }

        /// <summary>The ringing after the hit: 6.18 kHz with a slow wobble and a 15 Hz-beating partner, then hearing comes back as a muffled room.</summary>
        private static float[] EarRing(Rng r)
        {
            float[] b = Buf(4.5f);
            Phasor p1 = default, p2 = default;
            var pink = new PinkFilter();
            var lp = new Svf(250f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float env = Rise(t / 0.04f) * (t < 0.8f ? 1f : Decay(t - 0.8f, 1.2f));
                float f = 6180f + 4f * SinWrap(0.35f * t);
                b[i] = 0.1f * env * (p1.Sin(f) + 0.35f * p2.Sin(f + 15f))
                     + 0.5f * SmoothStep(1f, 4f, t) * (1f - SmoothStep(3.6f, 4.5f, t)) * lp.Lp(pink.Process(r.Signed()) * 4f);
            }
            return FinishOneShot(b, -36f, 40f, 600f);
        }

        /// <summary>Half a second of CCTV snow shaped to the feed's static cuts (camera_static is the viewer's own loop).</summary>
        private static float[] StaticBurst(Rng r)
        {
            float[] b = Buf(0.5f);
            var lp = new Lp4(5000f);
            var soft = new OnePole(7000f);
            var hp = new Svf(180f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i * Dt;
                float env = Rise(t / 0.004f) * (1f - SmoothStep(0.38f, 0.5f, t));
                b[i] = env * (0.88f + 0.12f * SinWrap(60f * t)) * hp.Hp(soft.Lp(lp.Process(r.Signed())));
            }
            return FinishOneShot(b, -27f, 0.5f, 30f);
        }

        /// <summary>A new task: two soft bells falling a fourth, D6 - A5 (new mail rises; a task settles).</summary>
        private static float[] NotifyTask(Rng r)
        {
            float[] b = Buf(0.55f);
            AddBell(b, 0.000f, 1174.66f, 0.75f, SoftBell, 0.8f);
            AddBell(b, 0.095f, 880.00f, 0.70f, SoftBell, 1.3f);
            AddReverb(b, 0.55f, 0.78f, 0.45f, 0.3f);
            return FinishOneShot(b, -21f, 0.5f, 100f);
        }
    }
}
