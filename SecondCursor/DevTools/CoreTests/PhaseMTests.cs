using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase M (the sound audit, the scares and the ending climax): the new clips and their levels, the scare rules (budgets,
    /// cooldowns, every reason a scare waits), the master limiter, and the content notes about sudden loud sounds.
    /// </summary>
    public class PhaseMTests
    {
        static readonly Dictionary<string, float> NewClips = new Dictionary<string, float>
        {
            ["knock_door"] = 2.2f, ["chair_creak"] = 1.4f, ["breath_near"] = 2.4f, ["whisper_burst"] = 1.3f, ["key_tap_rev"] = 0.72f,
            ["click_wrong"] = 0.4f, ["metal_scrape"] = 2.8f, ["step_near"] = 0.7f, ["sub_swell"] = 4f, ["crt_whine_rise"] = 3.5f,
            ["scare_hit"] = 2.6f, ["scare_hit_soft"] = 2.6f, ["ear_ring"] = 4.5f, ["static_burst"] = 0.5f, ["notify_task"] = 0.55f,
        };

        static readonly Dictionary<string, float[]> Cache = new Dictionary<string, float[]>();

        static float[] Clip(string id)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(id, out var b)) Cache[id] = b = ProceduralSoundBank.Generate(id, 1);
                return b;
            }
        }

        static double LoudestDb(float[] x, int window)
        {
            double s = 0, best = 0;
            for (int i = 0; i < x.Length; i++)
            {
                s += (double)x[i] * x[i];
                if (i >= window) s -= (double)x[i - window] * x[i - window];
                if (i >= window - 1) best = Math.Max(best, s);
            }
            return 10 * Math.Log10(Math.Max(best / window, 1e-24));
        }

        static int Ms(double ms) => (int)(ms * ProceduralSoundBank.SampleRate / 1000.0);

        // ------------------------------------------------------------------ the clips

        [Fact]
        public void TheNewClipsExistAreDeterministicAndStayUnderTheCeiling()
        {
            foreach (var kv in NewClips)
            {
                Assert.True(ProceduralSoundBank.Has(kv.Key), kv.Key);
                Assert.False(ProceduralSoundBank.IsLoop(kv.Key), kv.Key);
                float[] a = Clip(kv.Key);
                Assert.True(a.SequenceEqual(ProceduralSoundBank.Generate(kv.Key, 1)), kv.Key + " is not deterministic");
                Assert.InRange(a.Length / (float)ProceduralSoundBank.SampleRate, kv.Value - 0.01f, kv.Value + 0.01f);
                Assert.True(a.Max(Math.Abs) <= 0.8901f, kv.Key + " peaks over 0.89");
                Assert.True(Math.Abs(a[0]) < 1e-3f && Math.Abs(a[a.Length - 1]) < 1e-3f, kv.Key + " clicks at an end");
            }
        }

        [Fact]
        public void OnlyTheHitsMayPlayAtFullVolume()
        {
            foreach (string id in ProceduralSoundBank.Ids)
            {
                bool hit = id == "scare_hit" || id == "scare_hit_soft";
                Assert.True(hit ? ProceduralSoundBank.DefaultVolume(id) == 1f : ProceduralSoundBank.DefaultVolume(id) < 1f, id);
            }
        }

        [Fact]
        public void TheHitIsTheLoudestThingInTheMixAndItsSoftTwinClearlySofter()
        {
            double Mix(string id) => LoudestDb(Clip(id), Ms(50)) + 20 * Math.Log10(ProceduralSoundBank.DefaultVolume(id));
            double hit = Mix("scare_hit");
            Assert.InRange(LoudestDb(Clip("scare_hit"), Ms(50)), -8.0, -6.0);
            foreach (string id in new[] { "low_thump", "power_down", "crt_off", "grab_snap", "step_near", "knock_door", "sys_warning" })
                Assert.True(hit - Mix(id) >= 4.0, id + " is within 4 dB of the hit");
            Assert.True(hit - Mix("scare_hit_soft") >= 6.0);
            // The ambient scares are a key tap or quieter at their loudest (the knocks and the step are small stingers).
            foreach (string id in new[] { "chair_creak", "breath_near", "whisper_burst", "key_tap_rev", "metal_scrape", "ear_ring" })
                Assert.True(Mix(id) <= -20.0, id);
        }

        [Fact]
        public void TheHitLandsAfterItsPreRoll()
        {
            foreach (string id in new[] { "scare_hit", "scare_hit_soft" })
            {
                float[] x = Clip(id);
                float peak = x.Max(Math.Abs);
                int loudest = Array.FindIndex(x, v => Math.Abs(v) == peak);
                Assert.InRange(loudest, Ms(100), Ms(400));
                Assert.True(x.Take(Ms(90)).Max(Math.Abs) < 0.35f * peak, id + ": loud before the hit");
            }
        }

        [Fact]
        public void TheBuildUpsEndAtTheirLoudest()
        {
            foreach (string id in new[] { "sub_swell", "crt_whine_rise" })
            {
                float[] x = Clip(id);
                var end = x.Skip(x.Length - Ms(330)).Take(Ms(300)).ToArray();
                Assert.True(LoudestDb(x, Ms(300)) - LoudestDb(end, end.Length) <= 1.0, id);
            }
        }

        // ------------------------------------------------------------------ the rules

        static ScareContext Clear() => new ScareContext
        {
            NightSeconds = 1000f, BeatSeconds = 100f, SinceScare = 1000f, SinceStinger = 1000f, SinceEvent = 1000f,
        };

        [Fact]
        public void EachNightHasItsBudgetAndCooldown()
        {
            var n1 = ScareRules.For(1);
            var n2 = ScareRules.For(2);
            var n3 = ScareRules.For(3);
            Assert.Equal((2, 0, 90f), (n1.Budget, n1.PoolLimit, n1.Cooldown));
            Assert.Equal((6, 2, 60f), (n2.Budget, n2.PoolLimit, n2.Cooldown));
            Assert.Equal((9, 2, 40f, 25f), (n3.Budget, n3.PoolLimit, n3.Cooldown, n3.FinaleCooldown));
            Assert.Same(n3, ScareRules.For(4));
            Assert.Empty(n1.Pool);
            Assert.All(n2.Pool.Concat(n3.Pool), p => Assert.True(ProceduralSoundBank.Has(p.Id), p.Id));
        }

        [Fact]
        public void AClearMomentLetsAScarePlay()
        {
            Assert.Equal(ScareGate.None, ScareRules.Check(Clear(), ScareRules.For(2), false));
            Assert.Equal(ScareGate.None, ScareRules.Check(Clear(), ScareRules.For(2), true));
        }

        [Fact]
        public void EveryReasonToWaitIsNamed()
        {
            var n = ScareRules.For(3);
            var cases = new List<(ScareGate, Func<ScareContext, ScareContext>)>
            {
                (ScareGate.Paused, c => { c.Paused = true; return c; }),
                (ScareGate.Climax, c => { c.Climax = true; return c; }),
                (ScareGate.Budget, c => { c.Played = 9; return c; }),
                (ScareGate.Tug, c => { c.Tug = true; return c; }),
                (ScareGate.Dialog, c => { c.Dialog = true; return c; }),
                (ScareGate.Typing, c => { c.Typing = true; return c; }),
                (ScareGate.Reply, c => { c.AwaitingReply = true; return c; }),
                (ScareGate.Drag, c => { c.Dragging = true; return c; }),
                (ScareGate.Tip, c => { c.TipShowing = true; return c; }),
                (ScareGate.Tutorial, c => { c.TutorialOpen = true; return c; }),
                (ScareGate.WatchingSelf, c => { c.WatchingSelf = true; return c; }),
                (ScareGate.BeatStart, c => { c.BeatSeconds = 3f; return c; }),
                (ScareGate.StingerQuiet, c => { c.SinceStinger = 12f; return c; }),
                (ScareGate.EventNear, c => { c.SinceEvent = 2f; return c; }),
                (ScareGate.Cooldown, c => { c.SinceScare = 30f; return c; }),
            };
            foreach (var (gate, set) in cases) Assert.Equal(gate, ScareRules.Check(set(Clear()), n, false));
            // A pause outranks everything else; a tug outranks typing.
            var both = Clear();
            both.Paused = both.Tug = both.Typing = true;
            Assert.Equal(ScareGate.Paused, ScareRules.Check(both, n, false));
            both.Paused = false;
            Assert.Equal(ScareGate.Tug, ScareRules.Check(both, n, false));
        }

        [Fact]
        public void TheCooldownIsPerNightAndShorterInTheFinale()
        {
            var c = Clear();
            c.SinceScare = 30f;
            Assert.Equal(ScareGate.Cooldown, ScareRules.Check(c, ScareRules.For(3), false));
            c.Finale = true;
            Assert.Equal(ScareGate.None, ScareRules.Check(c, ScareRules.For(3), false));
            c.SinceScare = 80f;
            c.Finale = false;
            Assert.Equal(ScareGate.Cooldown, ScareRules.Check(c, ScareRules.For(1), false));
            Assert.Equal(ScareGate.None, ScareRules.Check(c, ScareRules.For(2), false));
        }

        [Fact]
        public void ThePoolWaitsForTheNightAndLeavesRoomForTheStory()
        {
            var n = ScareRules.For(2);
            var c = Clear();
            c.NightSeconds = 60f;
            Assert.Equal(ScareGate.Early, ScareRules.Check(c, n, true));
            Assert.Equal(ScareGate.None, ScareRules.Check(c, n, false));
            c = Clear();
            c.Played = 3;
            c.PoolPlayed = 2;
            Assert.Equal(ScareGate.Budget, ScareRules.Check(c, n, true));
            Assert.Equal(ScareGate.None, ScareRules.Check(c, n, false));
            Assert.True(ScareRules.PoolBeat(n, "help") && ScareRules.PoolBeat(n, "third") && !ScareRules.PoolBeat(n, "finish") && !ScareRules.PoolBeat(n, "rounds"));
            Assert.False(ScareRules.PoolBeat(ScareRules.For(3), "finale"));
            Assert.Equal(110f, ScareRules.PoolDelay(n, 0f));
            Assert.Equal(170f, ScareRules.PoolDelay(n, 1f));
        }

        [Fact]
        public void AStoryMomentIgnoresTheSmallGatesButNeverAPauseAClimaxOrTheBudget()
        {
            var n = ScareRules.For(3);
            var c = Clear();
            c.Tug = c.Dragging = c.Typing = c.AwaitingReply = c.Dialog = true;
            c.SinceEvent = 0.5f;
            c.SinceScare = 1f;
            Assert.Equal(ScareGate.None, ScareRules.Check(c, n, false, ScareRules.IgnoreAllButStory));
            foreach (var hard in new[] { ScareGate.Paused, ScareGate.Climax, ScareGate.Budget })
            {
                var h = c;
                h.Paused = hard == ScareGate.Paused;
                h.Climax = hard == ScareGate.Climax;
                h.Played = hard == ScareGate.Budget ? 9 : 0;
                Assert.Equal(hard, ScareRules.Check(h, n, false, ScareRules.IgnoreAllButStory));
            }
        }

        [Fact]
        public void ThePoolNeverPlaysTheSameSoundTwiceInARow()
        {
            var n = ScareRules.For(3);
            foreach (var last in n.Pool.Select(p => p.Id).Append(null))
                for (int i = 0; i < 100; i++)
                    Assert.NotEqual(last, ScareRules.PickPool(n, i / 100f, last).Id);
            Assert.Equal(n.Pool.Select(p => p.Id).OrderBy(x => x), Enumerable.Range(0, 100).Select(i => ScareRules.PickPool(n, i / 100f, null).Id).Distinct().OrderBy(x => x));
        }

        // ------------------------------------------------------------------ the limiter

        [Fact]
        public void TheLimiterHoldsTheWorstOverlapAtTheCeilingAndLeavesNormalPlayAlone()
        {
            // A hit on top of power_down, grab_snap, sys_warning and low_thump at their default volumes (SoundDesign.md 6.3).
            string[] ids = { "scare_hit", "power_down", "grab_snap", "sys_warning", "low_thump" };
            int n = ids.Max(id => Clip(id).Length);
            var stereo = new float[n * 2];
            foreach (string id in ids)
            {
                float[] x = Clip(id);
                float v = ProceduralSoundBank.DefaultVolume(id);
                for (int i = 0; i < x.Length; i++) { stereo[2 * i] += v * x[i]; stereo[2 * i + 1] += v * x[i]; }
            }
            Assert.True(stereo.Max(Math.Abs) > 1f);
            var limiter = new PeakLimiter(ProceduralSoundBank.SampleRate);
            limiter.Process(stereo, 2);
            Assert.True(stereo.Max(Math.Abs) <= PeakLimiter.Ceiling + 1e-5f);
            Assert.True(limiter.EngagedSamples > 0);

            var quiet = Clip("knock_door").Select(s => s * 0.9f).ToArray();
            var copy = (float[])quiet.Clone();
            var idle = new PeakLimiter(ProceduralSoundBank.SampleRate);
            idle.Process(quiet, 1);
            Assert.Equal(0, idle.EngagedSamples);
            Assert.Equal(copy, quiet);
        }

        // ------------------------------------------------------------------ the content notes

        static readonly string ContentDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));

        [Fact]
        public void TheDisclaimerSaysSuddenLoudSoundsAndThatReduceFlashingSoftensThem()
        {
            string path = Path.Combine(ContentDir, "strings.json");
            if (!File.Exists(path)) return;
            var table = JsonSerializer.Deserialize<StringTableData>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
            string Text(string key) => table.entries.First(e => e.key == key).value;
            foreach (string key in new[] { "disclaimer.body", "disclaimer.body.deck" })
            {
                Assert.Contains("sudden loud sounds", Text(key));
                Assert.Contains("softens the loudest sudden sounds", Text(key));
            }
            Assert.Contains("softens the sudden loud sounds", Text("disclaimer.choice.note"));
            char em = (char)0x2014, en = (char)0x2013;
            Assert.DoesNotContain(table.entries, e => e.value.IndexOf(em) >= 0 || e.value.IndexOf(en) >= 0);
        }
    }
}
