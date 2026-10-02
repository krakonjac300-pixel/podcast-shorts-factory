// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank, sound designs: ambience
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
    }
}
