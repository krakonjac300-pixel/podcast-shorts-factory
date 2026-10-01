using System;
using System.Collections.Generic;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase P, "haul it to the bin" (<see cref="TugReel"/>): holding still loses on time, strokes toward the bin win, hand over hand works and
    /// sideways never gains, the keep point and the re-grip window, GET READY, the seeded and warned surges, the near-bin rule, the finish
    /// and her line, the meter, the assist, trust and Deck scaling, Story, mercy, the easy win, the hold assist, the finale's LetGo hold, the
    /// first-time pattern at assist +3, frame-rate independence and the coach. The same cases run in the balance port
    /// (_work/2026-09-29/balance/check_reel_core.py) with the same inputs.
    /// </summary>
    public class ReelTests
    {
        const float Dt = 1f / 60f;
        /// <summary>ConflictSystem.ReadySeconds.</summary>
        const float Ready = 0.4f;
        static readonly ScreenBounds Screen = new ScreenBounds(960f, 540f, 28f);
        static readonly Vec2 Bin = new Vec2(915f, 66f);
        /// <summary>A file grabbed in open desktop on the way to the bin, 240 px from its edge (a tear-line finish).</summary>
        static readonly Vec2 Grab = new Vec2(700f, 230f);
        /// <summary>Where her pointer pressed and where the player's pointer was at the grab, from the file's centre.</summary>
        static readonly Vec2 HerOffset = new Vec2(4f, -3f), StartOffset = new Vec2(-6f, 8f);

        static TugOfWarSettings Settings(int night, bool first, int level = 0, DifficultyMode mode = DifficultyMode.Normal, bool mercy = false, int seed = 1)
        {
            var s = DifficultyTable.For(night, mode).TugFor(new AdaptiveAssist(level, AdaptiveAssist.MinLevel), mercy, first);
            s.model = TugModel.Reel;
            s.readySeconds = Ready;
            s.reel.seed = seed;
            return s;
        }

        static float Grip(int night, int level = 0, int losses = 0, DifficultyMode mode = DifficultyMode.Normal, float trust = 1f) =>
            DifficultyTable.For(night, mode).Grip(losses, new AdaptiveAssist(level, AdaptiveAssist.MinLevel), trust);

        static TugReel Begin(TugOfWarSettings s, Vec2? grab = null)
        {
            var m = new TugReel(s);
            var g = grab ?? Grab;
            m.Begin(g, g + HerOffset, Bin, Screen);
            return m;
        }

        static TugReel ReelTug(int night, bool first, int level = 0, int seed = 1) => Begin(Settings(night, first, level, seed: seed));

        /// <summary>
        /// Plays a reel: the pointer starts where it was at the grab and moves <paramref name="along"/> the axis to the bin (and
        /// <paramref name="across"/> it), as functions of the seconds since the grab; <paramref name="hold"/> is the button (held if null).
        /// <paramref name="each"/> sees the model after every undecided step.
        /// </summary>
        static (TugOutcome outcome, float time) Play(TugReel m, float grip, Func<float, float> along, Func<float, float> across = null,
            Func<float, bool> hold = null, float dt = Dt, float limit = 25f, Action<TugReel> each = null)
        {
            Vec2 start = m.GrabOrigin + StartOffset, side = new Vec2(-m.Axis.y, m.Axis.x);
            for (int i = 1; i * dt < limit; i++)
            {
                float t = i * dt;
                Vec2 p = start + m.Axis * along(t) + side * (across != null ? across(t) : 0f);
                var o = m.Step(dt, p, hold == null || hold(t), Vec2.Zero, grip);
                if (o != TugOutcome.None) return (o, t);
                each?.Invoke(m);
            }
            return (TugOutcome.None, limit);
        }

        static float Still(float t) => 0f;

        static Func<float, float> Stream(float speed, float from) => t => Math.Max(0f, t - from) * speed;

        /// <summary>From <paramref name="from"/> s: strokes of <paramref name="stroke"/> px at <paramref name="speed"/> toward the bin, a return at
        /// <paramref name="back"/> times the speed (0: none, the pointer stays where the stroke ended), then <paramref name="pause"/> s still.</summary>
        static Func<float, float> Strokes(float speed, float stroke, float back, float pause, float from)
        {
            float fwd = stroke / speed, ret = back > 0f ? stroke / (speed * back) : 0f, period = fwd + ret + pause;
            float net = back > 0f ? 0f : stroke;
            return t =>
            {
                if (t <= from) return 0f;
                float u = t - from;
                int k = (int)(u / period);
                float r = u - k * period;
                float d = r < fwd ? r * speed : r < fwd + ret ? stroke - (r - fwd) * speed * back : back > 0f ? 0f : stroke;
                return k * net + d;
            };
        }

        static float Median(List<float> v)
        {
            v.Sort();
            return v[v.Count / 2];
        }

        // ------------------------------------------------------------------ holding on, pulling

        [Fact]
        public void HoldingStillLosesOnTime()
        {
            // From the grab, over 20 seeded surge schedules: the median in section 1.5's band, every one within 0.3 s of its table time.
            foreach (var (night, first, lo, hi, table) in new[] { (1, true, 3.2f, 3.6f, 3.4f), (1, false, 2.7f, 3.3f, 3.0f), (2, true, 2.2f, 2.8f, 2.5f), (3, true, 2.0f, 2.5f, 2.2f) })
            {
                var times = new List<float>();
                for (int seed = 1; seed <= 20; seed++)
                {
                    var (o, t) = Play(ReelTug(night, first, seed: seed), Grip(night), Still);
                    Assert.Equal(TugOutcome.EntityWins, o);
                    Assert.InRange(t, table - 0.3f, table + 0.3f);
                    times.Add(t);
                }
                Assert.InRange(Median(times), lo, hi);
            }
        }

        [Fact]
        public void ASteadyStreamWins()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var n1 = Play(ReelTug(1, true, seed: seed), Grip(1), Stream(150f, 0.5f));
                Assert.Equal(TugOutcome.PlayerWins, n1.outcome);
                Assert.True(n1.time <= 2.5f, "N1 " + n1.time);
                var n3 = Play(ReelTug(3, true, seed: seed), Grip(3), Stream(150f, 0.5f));
                Assert.Equal(TugOutcome.PlayerWins, n3.outcome);
                Assert.True(n3.time <= 3.5f, "N3 " + n3.time);
            }
        }

        [Fact]
        public void HandOverHandAlongTheAxisWins()
        {
            // 40 px strokes at 5 Hz, toward the bin and back with the button held: the return strokes cost nothing.
            Assert.Equal(TugOutcome.PlayerWins, Play(ReelTug(1, true), Grip(1), Strokes(400f, 40f, 1f, 0f, 0.5f)).outcome);
            // The same motion sideways, or a stream away from the bin, never gains an inch.
            foreach (var (along, across) in new (Func<float, float>, Func<float, float>)[] { (Still, Strokes(400f, 40f, 1f, 0f, 0.5f)), (Stream(-150f, 0.5f), null) })
            {
                var m = ReelTug(1, true);
                float most = 0f;
                var r = Play(m, Grip(1), along, across, each: x => most = Math.Max(most, x.S));
                Assert.Equal(TugOutcome.EntityWins, r.outcome);
                Assert.True(most <= 0f, "gained " + most);
            }
        }

        // ------------------------------------------------------------------ letting go

        [Fact]
        public void ReleasingPastHalfWayKeepsTheFile()
        {
            var m = ReelTug(1, true);
            bool keepsBefore = false;
            bool held = true;
            var r = Play(m, Grip(1), Stream(150f, 0.5f), hold: t => held, each: x =>
            {
                if (!held || !x.KeepsOnRelease) return;
                held = false;
                keepsBefore = x.S >= 0.5f * x.Finish && x.PlayerLead >= 0.75f;
            });
            Assert.True(keepsBefore);
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
            Assert.True(m.FinalLead >= 0.75f && m.S < m.Finish, "kept at " + m.S + " of " + m.Finish);
        }

        [Fact]
        public void ASlipCaughtInTimeKeepsFighting()
        {
            // Let go 0.2 s into the fight (well below half way) while still pulling; press again after the gap.
            (TugOutcome, float, TugReel) Slip(int level, float gap)
            {
                var m = ReelTug(1, true, level);
                var r = Play(m, Grip(1, level), Stream(200f, 0.5f), hold: t => !(t > 0.6f && t <= 0.6f + gap));
                return (r.outcome, r.time, m);
            }
            var (caught, _, kept) = Slip(0, 0.3f);
            Assert.Equal(TugOutcome.PlayerWins, caught);
            Assert.Equal(1, kept.Regrips);
            var (late, lateAt, lost) = Slip(0, 0.6f);
            Assert.Equal(TugOutcome.EntityWins, late);
            Assert.Equal(TugLossReason.LetGo, lost.EndReason);
            Assert.InRange(lateAt, 0.6f + 0.5f - 2f * Dt, 0.6f + 0.5f + 2f * Dt);
            // Assist +3 adds 0.12 s: a 0.6 s gap is caught, 0.65 s is not.
            Assert.Equal(0.62f, Settings(1, true, 3).reel.regrip, 3);
            Assert.Equal(TugOutcome.PlayerWins, Slip(3, 0.6f).Item1);
            Assert.Equal(TugOutcome.EntityWins, Slip(3, 0.65f).Item1);
            // Dragged back to her line while the button is up, inside the window, is still letting go.
            var dragged = ReelTug(3, false);
            var d = Play(dragged, Grip(3), Still, hold: t => t < 1.5f);
            Assert.Equal(TugOutcome.EntityWins, d.outcome);
            Assert.True(d.time < 1.5f + 0.5f, "lost at " + d.time);
            Assert.Equal(TugLossReason.LetGo, dragged.EndReason);
        }

        [Fact]
        public void GetReadyScoresNothing()
        {
            var m = ReelTug(1, true);
            var start = m.GrabOrigin + StartOffset;
            for (float t = Dt; t < Ready - Dt / 2f; t += Dt)
            {
                // A hard yank toward the bin during the beat moves nothing.
                Assert.Equal(TugOutcome.None, m.Step(Dt, start + m.Axis * (600f * t), true, Vec2.Zero, Grip(1)));
                Assert.True(m.InReady && m.InReadGrace);
                Assert.Equal(0f, m.S);
            }
            Assert.Equal(m.Origin, m.ObjectPosition);
            // Letting go during GET READY still loses after the night's short release grace.
            var early = ReelTug(1, true);
            var r = Play(early, Grip(1), Still, hold: t => t < 0.1f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.Equal(TugLossReason.LetGo, early.EndReason);
            Assert.True(r.time < Ready, "lost at " + r.time);
            // A press inside that grace grabs it again; meanwhile the press is a re-grip (the router swallows it).
            var story = Begin(Settings(1, true, 2, DifficultyMode.Story));
            bool regrip = false;
            var s = Play(story, Grip(1, 2, 0, DifficultyMode.Story), Still, hold: t => !(t > 0.1f && t <= 0.3f), limit: 1f, each: x => regrip |= x.InReady && x.InRegrip);
            Assert.Equal(TugOutcome.None, s.outcome);
            Assert.True(regrip);
        }

        // ------------------------------------------------------------------ her surges

        [Fact]
        public void SurgesAreSeededWarnedAndFaded()
        {
            List<(float t, bool surge, bool warn, float pull)> Trace(int seed)
            {
                var m = ReelTug(1, true, seed: seed);
                var trace = new List<(float, bool, bool, float)>();
                Play(m, Grip(1), Stream(60f, 0.5f), limit: 4f, each: x => trace.Add((x.Elapsed, x.Surging, x.Telegraph, x.Pull)));
                return trace;
            }
            var a = Trace(7);
            Assert.Equal(a, Trace(7));
            Assert.NotEqual(a.Select(x => x.surge), Trace(8).Select(x => x.surge));
            int first = a.FindIndex(x => x.surge);
            Assert.True(first > 0);
            float since = a[first].t - Ready;
            Assert.InRange(since, 0.6f, 1.0f + Dt);
            // Warned 0.15 s ahead (her twitch), and faded in like her pull: 60 px/s on top of 25, both times k.
            int warned = 0;
            for (int i = first - 1; i >= 0 && a[i].warn; i--) warned++;
            Assert.InRange(warned, 8, 10);
            float k = since / 1.2f;
            Assert.Equal((25f + 60f) * k, a[first].pull, 1);
            // None in Story or a mercy contest.
            foreach (var s in new[] { Settings(1, true, 2, DifficultyMode.Story), Settings(1, true, mercy: true) })
            {
                var m = Begin(s);
                bool any = false;
                Play(m, AdaptiveAssist.MercyGrip, Still, limit: 6f, each: x => any |= x.Surging || x.Telegraph);
                Assert.False(any);
            }
        }

        // ------------------------------------------------------------------ geometry

        [Fact]
        public void TheNearBinRuleMovesTheFileBack()
        {
            Vec2 away = (Grab - Bin).Normalized;
            Vec2 grab = Bin + away * (HaulGeometry.BinRadius + 60f);
            var m = Begin(Settings(1, false), grab);
            Assert.Equal(110f, m.Finish);
            Assert.True(m.FinishIsBin);
            Assert.Equal(50f, Vec2.Distance(m.Origin, grab), 2);
            Assert.True(Vec2.Dot(m.Origin - grab, away) > 0f);
            var p = grab + StartOffset;
            for (int i = 0; i < 9; i++) m.Step(Dt, p, true, Vec2.Zero, Grip(1));
            float half = Vec2.Distance(m.ObjectPosition, grab);
            Assert.InRange(half, 15f, 35f);
            for (int i = 0; i < 9; i++) m.Step(Dt, p, true, Vec2.Zero, Grip(1));
            Assert.Equal(0f, Vec2.Distance(m.ObjectPosition, m.Origin), 2);
        }

        [Fact]
        public void FinishIsTheBinOrATearLine()
        {
            var far = Begin(Settings(1, false));
            Assert.Equal(140f, far.Finish);
            Assert.False(far.FinishIsBin);
            Assert.Equal(160f, Begin(Settings(3, false)).Finish);
            Vec2 grab = Bin + (Grab - Bin).Normalized * (HaulGeometry.BinRadius + 125f);
            var near = Begin(Settings(1, false), grab);
            Assert.Equal(125f, near.Finish, 2);
            Assert.True(near.FinishIsBin);
        }

        [Fact]
        public void HerLineShrinksAtTheEdgeToFifty()
        {
            Assert.Equal(90f, Begin(Settings(1, false)).HerLine);
            Assert.Equal(50f, Begin(Settings(1, false), new Vec2(60f, 500f)).HerLine);
            var mid = new Vec2(120f, 420f);
            var m = Begin(Settings(1, false), mid);
            Assert.InRange(m.HerLine, 51f, 89f);
            Assert.Equal(HaulGeometry.RoomAlong(mid, -m.Axis, Screen) - HaulGeometry.HerLineMargin, m.HerLine, 3);
        }

        [Fact]
        public void TheMeterMapsTheTrack()
        {
            // Half at the grab, 1 at the finish, 0 at her line: the bar fills toward the bin and empties toward her.
            var m = ReelTug(1, true);
            Assert.Equal(0.5f, m.PlayerLead);
            bool mapped = true;
            void Check(TugReel x) => mapped &= Math.Abs(x.PlayerLead - (x.S >= 0f ? 0.5f + 0.5f * x.S / x.Finish : 0.5f + 0.5f * x.S / x.HerLine)) < 1e-4f;
            Play(m, Grip(1), Stream(150f, 0.5f), each: Check);
            Assert.Equal(1f, m.FinalLead);
            var lost = ReelTug(1, true);
            Play(lost, Grip(1), Still, each: Check);
            Assert.True(mapped);
            Assert.Equal(0f, lost.FinalLead);
        }

        // ------------------------------------------------------------------ difficulty

        [Fact]
        public void GripAssistTrustAndDeckScaleTheReel()
        {
            // Grip growth on lost tugs: two lost tugs on Night 1 and she drags a held file back sooner.
            float fresh = Play(ReelTug(1, false), Grip(1), Still).time;
            Assert.True(Play(ReelTug(1, false), Grip(1, 0, 2), Still).time < fresh - 0.1f);
            // The assist table: the reel gain grows with the pull factor, surges and the ramp shrink, the re-grip window grows.
            foreach (int level in new[] { -1, 0, 1, 2, 3 })
            {
                var fx = AdaptiveAssist.Effects(level);
                var r = Settings(1, false, level).reel;
                Assert.Equal(1.5f / fx.PullSpeed, r.reelGain, 4);
                Assert.Equal(60f * fx.Grip, r.surge, 3);
                Assert.Equal(15f * fx.Ramp, r.reelRampPerSecond, 3);
                Assert.Equal(0.5f + fx.ReleaseGraceAdd, r.regrip, 4);
            }
            Assert.Equal(1.5f * 1.43f, Settings(1, false, 3).reel.reelGain, 1);
            // Trust (the finale's Fight mode grips 10% harder) and the Deck's reel gain (MakeDifficulty scales the profile).
            Assert.True(Play(ReelTug(3, false), Grip(3, trust: 1.1f), Still).time < Play(ReelTug(3, false), Grip(3), Still).time);
            var deck = DifficultyTable.For(1, DifficultyMode.Normal);
            deck.Tug.reel.reelGain *= 1.2f;
            Assert.Equal(1.8f, deck.TugFor(new AdaptiveAssist()).reel.reelGain, 4);
        }

        [Fact]
        public void StoryHoldingStillNeverLosesAndPullingWins()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var s = Settings(1, true, 2, DifficultyMode.Story, seed: seed);
                float grip = Grip(1, 2, 0, DifficultyMode.Story);
                var still = Play(Begin(s), grip, Still, limit: 30f);
                Assert.Equal(TugOutcome.PlayerWins, still.outcome);
                Assert.InRange(still.time, 15f, 20f);
                // Hand over hand at 100 px/s: 60 px strokes, back at 0.8x.
                var pulled = Play(Begin(s), grip, Strokes(100f, 60f, 0.8f, 0f, 0.5f));
                Assert.Equal(TugOutcome.PlayerWins, pulled.outcome);
                Assert.InRange(pulled.time, 2f, 5f);
            }
        }

        [Fact]
        public void MercyLetsGoAfterAHoldOrAPull()
        {
            // Holding still: her mercy pull cannot take it in 2.5 s, and she lets go then. Pulling at 60 px/s: after a second of it.
            foreach (var (along, lo, hi) in new[] { ((Func<float, float>)Still, 2.5f - Dt, 2.5f + Dt), (Stream(60f, 0.5f), 1.4f, 1.8f) })
            {
                var m = Begin(Settings(1, false, mercy: true));
                var mercy = new MercyRelease();
                float letGo = -1f;
                Play(m, AdaptiveAssist.MercyGrip, along, limit: 4f, each: x =>
                {
                    if (letGo < 0f && mercy.Step(Dt, true, x.Effort)) letGo = x.Elapsed;
                });
                Assert.InRange(letGo, lo, hi);
            }
        }

        [Fact]
        public void ReelEasyWinLowersTheLevel()
        {
            // A fast haul (300 px/s right after GET READY) is an easy win: the level drops at once.
            var m = ReelTug(1, false);
            Assert.Equal(TugOutcome.PlayerWins, Play(m, Grip(1), Stream(300f, Ready)).outcome);
            Assert.True(m.ActiveElapsed < AdaptiveAssist.ReelEasyWinSeconds && m.PeakEffort * TugReel.EffortSpeed >= AdaptiveAssist.ReelEasyWinStroke);
            var assist = new AdaptiveAssist(1);
            assist.ReportTug(true, m.ActiveElapsed, m.PeakEffort, TugModel.Reel);
            Assert.Equal(0, assist.Level);
            // A slower one is not (one win is not a streak), nor a quick one with a weak stroke.
            var slow = ReelTug(1, false);
            Play(slow, Grip(1), Stream(150f, 0.5f));
            var a2 = new AdaptiveAssist(1);
            a2.ReportTug(true, slow.ActiveElapsed, slow.PeakEffort, TugModel.Reel);
            a2.ReportTug(false, 1f, 0f, TugModel.Reel);
            a2.ReportTug(true, 0.5f, 250f / TugReel.EffortSpeed, TugModel.Reel);
            Assert.Equal(1, a2.Level);
        }

        [Fact]
        public void HoldAssistWinsByHoldingOnEveryNight()
        {
            for (int night = 1; night <= 3; night++)
            {
                var s = Settings(night, true);
                DifficultyTable.HoldAssist(s);
                var m = Begin(s);
                Assert.Equal(TugVariant.Hold, m.Variant);
                var r = Play(m, Grip(night), Still);
                Assert.Equal(TugOutcome.PlayerWins, r.outcome);
                Assert.True(r.time < 4f, "night " + night + ": " + r.time);
                // Letting go past the 1 s window still loses.
                var slip = Begin(s);
                Assert.Equal(TugOutcome.EntityWins, Play(slip, Grip(night), Still, hold: t => t < 0.6f).outcome);
                Assert.Equal(TugLossReason.LetGo, slip.EndReason);
            }
            // Assisted wins never lower the level; assisted losses count as usual.
            var assist = new AdaptiveAssist(1);
            assist.ReportTug(true, 0.2f, 2f, TugModel.Reel, true);
            assist.ReportTug(true, 0.2f, 2f, TugModel.Reel, true);
            Assert.Equal(1, assist.Level);
            assist.ReportTug(false, 3f, 0f, TugModel.Reel, true);
            assist.ReportTug(false, 3f, 0f, TugModel.Reel, true);
            Assert.Equal(2, assist.Level);
        }

        /// <summary>The finale's LetGo hold as Night3Director's Customize makes it (Phase P-b): no pull, a creep to the finish, a trembling floor.</summary>
        static TugOfWarSettings LetGo(float seconds)
        {
            var s = Settings(3, false);
            var r = s.reel;
            r.holdSeconds = seconds;
            r.releaseNeverLoses = true;
            r.herPull = r.surge = r.reelRampPerSecond = 0f;
            r.strainFloor = 0.45f;
            return s;
        }

        [Fact]
        public void LetGoHoldWinsAndAnEarlyReleaseStealsNothing()
        {
            var m = Begin(LetGo(Core.Story.Night3Rules.LetGoHoldSeconds));
            Assert.Equal(TugVariant.LetGo, m.Variant);
            float floor = 1f;
            var r = Play(m, Grip(3), Still, each: x => floor = Math.Min(floor, x.Strain));
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
            Assert.InRange(r.time, 3.3f, 3.6f);
            Assert.True(floor >= 0.45f);
            // Let go early: nobody wins, nothing is taken; the retry hold is shorter.
            var early = Begin(LetGo(Core.Story.Night3Rules.LetGoHoldSeconds));
            Assert.Equal(TugOutcome.Released, Play(early, Grip(3), Still, hold: t => t < 1f).outcome);
            Assert.Null(early.EndReason);
            var retry = Play(Begin(LetGo(Core.Story.Night3Rules.LetGoRetryHoldSeconds)), Grip(3), Still);
            Assert.Equal(TugOutcome.PlayerWins, retry.outcome);
            Assert.InRange(retry.time, 1.8f, 2.1f);
        }

        [Fact]
        public void AssistL3FirstTimePatternWinsTheN3Finale()
        {
            // 0.6 s to react, then 120 px strokes at 320 px/s with 0.25 s pauses: wins the finale's first fight at +3, whatever the surges.
            for (int seed = 1; seed <= 20; seed++)
                Assert.Equal(TugOutcome.PlayerWins, Play(ReelTug(3, true, 3, seed), Grip(3, 3), Strokes(320f, 120f, 0f, 0.25f, 0.6f)).outcome);
        }

        [Fact]
        public void TheReelIsFrameRateIndependent()
        {
            float lost60 = Play(ReelTug(1, true), Grip(1), Still).time;
            float won60 = Play(ReelTug(1, true), Grip(1), Stream(150f, 0.5f)).time;
            foreach (float hz in new[] { 30f, 144f, 240f })
            {
                Assert.Equal(lost60, Play(ReelTug(1, true), Grip(1), Still, dt: 1f / hz).time, 0.05f);
                Assert.Equal(won60, Play(ReelTug(1, true), Grip(1), Stream(150f, 0.5f), dt: 1f / hz).time, 0.05f);
            }
        }

        // ------------------------------------------------------------------ the coach

        [Fact]
        public void CoachReel()
        {
            var axis = new Vec2(0.8f, -0.6f);
            var side = new Vec2(0.6f, 0.8f);
            TugCoach Feed(Vec2 way, float seconds, float reelSpeed, TugCoach c = null)
            {
                c = c ?? new TugCoach(axis);
                var p = new Vec2(400f, 300f);
                for (float t = 0f; t < seconds; t += Dt)
                {
                    // 40 px back and forth at 5 Hz along the given way.
                    p = p + way * ((t % 0.2f) < 0.1f ? 400f * Dt : -400f * Dt);
                    c.Step(Dt, p, true, reelSpeed);
                }
                return c;
            }
            // Hand over hand toward the bin is not the wrong way (half of it goes toward the bin); sideways is.
            Assert.Equal(TugLossReason.Overpowered, Feed(axis, 2f, 200f).ClassifyReel(false, 300f, 50f));
            Assert.Equal(TugLossReason.WrongWay, Feed(side, 2f, 0f).ClassifyReel(false, 0f, 50f));
            // Too slow: her mean pull within 30 px/s of the mean reel.
            Assert.Equal(TugLossReason.TooSlow, Feed(axis, 2f, 200f).ClassifyReel(false, 70f, 50f));
            // Stopped is judged on the smoothed reel: the returns of hand over hand are not stops, 0.6 s under 40 px/s at the end is.
            var stopped = Feed(axis, 2f, 200f);
            for (float t = 0f; t < 0.7f; t += Dt) stopped.Step(Dt, new Vec2(400f, 300f), true, 10f);
            Assert.Equal(TugLossReason.Stopped, stopped.ClassifyReel(false, 300f, 50f));
            var lifted = Feed(axis, 2f, 200f);
            for (float t = 0f; t < 0.3f; t += Dt) lifted.Step(Dt, new Vec2(400f, 300f), true, 10f);
            Assert.NotEqual(TugLossReason.Stopped, Feed(axis, 0.5f, 200f, lifted).ClassifyReel(false, 300f, 50f));
            Assert.Equal(TugLossReason.LetGo, new TugCoach(axis).ClassifyReel(true, 0f, 0f));
            Assert.Equal(TugLossReason.HeldStill, Feed(axis, 0f, 0f).ClassifyReel(false, 0f, 25f));
        }
    }
}
