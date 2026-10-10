using System;

namespace SecondCursor.Core.Entity
{
    public enum DifficultyMode { Normal, Story }

    /// <summary>
    /// Every challenge value for one night at one difficulty (expansion spec, section 7): the tug-of-war
    /// feel, how hard the second cursor defends its file, the adaptive assist's start and floor, and hint
    /// timings. Values are for assist level 0; <see cref="AdaptiveAssist"/> scales them per contest.
    /// Built by <see cref="DifficultyTable.For"/>; Night 1 Normal keeps the vertical slice's tug-of-war settings,
    /// with the Phase F balance on top (grip growth, reaction delays, hint timing). An EntityTuningAsset in
    /// Resources may still override Night 1 for designers.
    /// </summary>
    public sealed class DifficultyProfile
    {
        /// <summary>"Never" for the defense-count thresholds below.</summary>
        public const int Never = int.MaxValue;

        public int Night = 1;
        public DifficultyMode Mode = DifficultyMode.Normal;

        // ---------------------------------------------------------------- tug-of-war (7.2)
        public TugOfWarSettings Tug = new TugOfWarSettings();

        // ---------------------------------------------------------------- entity (7.2)
        public float GripBase = 0.62f;
        /// <summary>Grip added per tug the player has lost this night (Phase F: lost tugs only, not every defense).</summary>
        public float GripGrowth = 0.06f;
        public float GripCap = 1.35f;
        /// <summary>Movement reaction-time multiplier (lower = faster).</summary>
        public float ReactionScale = 1f;
        public float RaceToNoDelayMin = 0.40f;
        public float RaceToNoDelayMax = 0.60f;
        /// <summary>Seconds from noticing a drag of its file to lunging for it (fading in, if it was not on screen, counts toward it).</summary>
        public float InterceptDelay = 0.20f;
        public int GuardYesAfter = 2;
        public float GuardYesHoldMin = 5f;
        public float GuardYesHoldMax = 8f;
        /// <summary>How close (px) the player's cursor gets to Yes before it drags the dialog away (0 = never).</summary>
        public float DragDialogRadius = 70f;
        /// <summary>Shred speed while it fights over Cancel.</summary>
        public float CancelCrawl = 0.35f;
        public float CancelPatience = 6f;
        /// <summary>Reaction before it goes for Cancel once the shred has started.</summary>
        public float CancelDelayMin = 0.35f;
        public float CancelDelayMax = 0.55f;
        public int KeepAwayAfter = 2;
        /// <summary>
        /// Phase P (T6): the contest after this many over the same file in one beat is a mercy contest (0 = no cap: Night 1, whose
        /// defense cap is a story beat). The Night 3 finale has its own rule.
        /// </summary>
        public int TugContestCap;
        public float UrgencyPerDefense = 0.15f;
        public float UrgencyCap = 2.2f;

        // ---------------------------------------------------------------- adaptive assist (7.3)
        /// <summary>Lowest assist level this difficulty allows (Story never drops below +2).</summary>
        public int AssistFloor = AdaptiveAssist.MinLevel;
        /// <summary>Tug losses at level 3 before the next contest is a mercy contest.</summary>
        public int MercyAfterLosses = 2;

        // ---------------------------------------------------------------- hints (7.6)
        public float TaskHintFirst = 30f;
        public float BriefingHintFirst = 25f;
        public float TaskHintRepeat = 40f;
        /// <summary>
        /// Phase Q1: nothing is force-completed any more. After "Not now" the offer to finish a task comes back after this many seconds of
        /// active (not reading) time; a choice order still lapses this long after its first hint.
        /// </summary>
        public float TaskForceAfterHint = 150f;
        public float EntityTaskNudge = 35f;
        public float EntityTaskWithdraw = 75f;
        public float CodeHint1Delay = 60f;
        public int CodeFormatAfterFailures = 3;
        public float CodeGaryHint = 120f;
        public float CodeHint2Delay = 180f;

        /// <summary>The assist level a night starts at, given the level the previous night ended on.</summary>
        public int StartLevel(int previousNightFinalLevel)
        {
            if (Mode == DifficultyMode.Story) return Math.Max(2, AssistFloor);
            if (Night <= 1) return 0;
            return AdaptiveAssist.CarryLevel(previousNightFinalLevel);
        }

        /// <summary>
        /// Phase I: seconds after the grab in which the second cursor's pull does not count, for the first contest of a night
        /// (and the first after Story is chosen). A first-time player needs over a second to read the label; measured on the
        /// bridge, a pull that starts after 0.5 s of holding still otherwise loses every time.
        /// </summary>
        public const float ReadGraceSeconds = 1.2f;

        /// <summary>
        /// A fresh copy of the tug settings for one contest, scaled by the current assist level. <paramref name="readGrace"/>:
        /// the night's first contest, which starts with <see cref="ReadGraceSeconds"/> of standoff (the reel: her long fade-in).
        /// </summary>
        public TugOfWarSettings TugFor(AdaptiveAssist assist, bool mercy = false, bool readGrace = false)
        {
            var s = Tug.Clone();
            s.readGrace = readGrace ? ReadGraceSeconds : 0f;
            var fx = AdaptiveAssist.Effects(assist != null ? assist.Level : 0);
            s.pullSpeedForFullStrength *= fx.PullSpeed;
            s.rampPerSecond *= fx.Ramp;
            s.releaseGrace += fx.ReleaseGraceAdd;
            // Phase P, the reel: her pull follows the grip (assist and growth included); the assist's pull factor strengthens the reel.
            var r = s.reel;
            r.fade = readGrace ? r.fadeFirst : r.fadeLater;
            r.regrip += fx.ReleaseGraceAdd;
            r.reelGain /= fx.PullSpeed;
            r.reelRampPerSecond *= fx.Ramp;
            r.surge *= fx.Grip;
            r.gripBase = GripBase;
            if (mercy)
            {
                // A mercy contest never ramps up: the entity is about to let go.
                s.rampPerSecond = 0f;
                s.rampDelay = 99f;
                r.reelRampPerSecond = 0f;
                r.surge = 0f;
            }
            return s;
        }

        /// <summary>Phase P (T6): <paramref name="earlierContests"/> over the same file this beat make the next one a mercy contest.</summary>
        public bool MercyByCap(int earlierContests) => TugContestCap > 0 && earlierContests >= TugContestCap;

        /// <summary>
        /// Tug-of-war grip: min(cap, base * assist grip * trust * (1 + tugLosses * growth * assist growth)).
        /// Only lost tugs make it grow (Phase F): a dialog it won or an icon it snatched says nothing about how hard
        /// the next fight should be. <paramref name="trustMult"/> is 1 except in the Night 3 finale.
        /// </summary>
        public float Grip(int tugLosses, AdaptiveAssist assist, float trustMult = 1f)
        {
            var fx = AdaptiveAssist.Effects(assist != null ? assist.Level : 0);
            float grip = GripBase * fx.Grip * trustMult * (1f + tugLosses * GripGrowth * fx.Growth);
            return Math.Min(GripCap, grip);
        }

        /// <summary>Reaction before it goes for Cancel; <paramref name="random01"/> picks within the range.</summary>
        public float CancelDelay(float random01) => CancelDelayMin + (CancelDelayMax - CancelDelayMin) * MathUtil.Clamp01(random01);

        /// <summary>Reaction delay before it races you to No; <paramref name="random01"/> picks within the range.</summary>
        public float RaceToNoDelay(float random01, AdaptiveAssist assist)
        {
            float d = RaceToNoDelayMin + (RaceToNoDelayMax - RaceToNoDelayMin) * MathUtil.Clamp01(random01);
            return Math.Max(0f, d + AdaptiveAssist.Effects(assist != null ? assist.Level : 0).RaceToNoDelayAdd);
        }

        /// <summary>Whether it parks on Yes this time (it alternates with racing so it stays unpredictable).</summary>
        public bool GuardsYes(int defenses, AdaptiveAssist assist)
        {
            if (GuardYesAfter == Never || defenses < GuardYesAfter) return false;
            if (!AdaptiveAssist.Effects(assist != null ? assist.Level : 0).GuardYes) return false;
            return (defenses - GuardYesAfter) % 2 == 0;
        }

        public bool DragsDialog(AdaptiveAssist assist)
        {
            return DragDialogRadius > 0f && AdaptiveAssist.Effects(assist != null ? assist.Level : 0).DragDialog;
        }

        public bool KeepsAway(int defenses) => KeepAwayAfter != Never && defenses >= KeepAwayAfter;

        /// <summary>Movement speed-up as it keeps having to defend.</summary>
        public float Urgency(int defenses) => Math.Min(UrgencyCap, 1f + defenses * UrgencyPerDefense);

        public DifficultyProfile Clone()
        {
            var p = (DifficultyProfile)MemberwiseClone();
            p.Tug = Tug.Clone();
            return p;
        }
    }

    /// <summary>The tables of section 7: one column per night, plus Story (all nights).</summary>
    public static class DifficultyTable
    {
        /// <summary>Story's tension limit: far beyond the screen, so the cursors never snap apart.</summary>
        public const float StoryNoSnap = 99999f;

        public static DifficultyProfile For(int night, DifficultyMode mode)
        {
            night = Math.Max(1, Math.Min(3, night));
            var p = new DifficultyProfile { Night = night, Mode = mode };
            if (night == 2) Night2(p);
            else if (night == 3) Night3(p);
            if (mode == DifficultyMode.Story) Story(p);
            return p;
        }

        public static DifficultyMode ParseMode(string s) =>
            string.Equals(s, "story", StringComparison.OrdinalIgnoreCase) ? DifficultyMode.Story : DifficultyMode.Normal;

        public static string ModeId(DifficultyMode mode) => mode == DifficultyMode.Story ? "story" : "normal";

        static void Night2(DifficultyProfile p)
        {
            var t = p.Tug;
            t.startShare = 0.52f;
            t.pullSpeedForFullStrength = 440f;
            // Phase J: 300 -> 315. With the read grace and the release rule an average player won the night's first tug 96% of the
            // time (by the cursors coming apart inside the grace); 315 brings it back to about 90% (balance run_phasej.py).
            t.maxTension = 315f;
            t.rampDelay = 2.5f;
            t.rampPerSecond = 0.18f;
            t.releaseGrace = 0.08f;
            t.reel.herPull = 40f;
            t.reel.surge = 90f;
            t.reel.reelRampPerSecond = 25f;
            t.reel.finishMax = 150f;
            p.GripBase = 0.68f;
            p.GripGrowth = 0.05f;
            p.GripCap = 1.40f;
            p.ReactionScale = 0.90f;
            p.RaceToNoDelayMin = 0.20f;
            p.RaceToNoDelayMax = 0.40f;
            p.InterceptDelay = 0.15f;
            p.DragDialogRadius = 80f;
            p.TugContestCap = 3;
            p.CancelCrawl = 0.30f;
            p.CancelDelayMin = 0.30f;
            p.CancelDelayMax = 0.50f;
            p.TaskHintFirst = 40f;
            p.BriefingHintFirst = 30f;
            p.TaskHintRepeat = 45f;
        }

        static void Night3(DifficultyProfile p)
        {
            var t = p.Tug;
            t.startShare = 0.55f;
            t.entityWinShare = 0.90f;
            t.pullSpeedForFullStrength = 460f;
            t.maxTension = 330f;   // Phase J: 320 -> 330, the same reason as Night 2 (94% -> about 88%)
            t.rampDelay = 2.0f;
            t.rampPerSecond = 0.22f;
            t.releaseGrace = 0.08f;
            t.reel.herPull = 55f;
            t.reel.surge = 120f;
            t.reel.reelRampPerSecond = 35f;
            t.reel.finishMax = 160f;
            p.GripBase = 0.74f;
            p.GripGrowth = 0.05f;
            p.GripCap = 1.50f;
            p.ReactionScale = 0.80f;
            p.RaceToNoDelayMin = 0.18f;
            p.RaceToNoDelayMax = 0.35f;
            p.InterceptDelay = 0.12f;
            p.GuardYesAfter = 1;
            p.GuardYesHoldMin = 6f;
            p.GuardYesHoldMax = 9f;
            p.DragDialogRadius = 90f;
            p.CancelCrawl = 0.25f;
            p.CancelPatience = 7f;
            p.CancelDelayMin = 0.25f;
            p.CancelDelayMax = 0.45f;
            p.KeepAwayAfter = 1;
            p.TugContestCap = 3;
            p.UrgencyPerDefense = 0.18f;
            p.UrgencyCap = 2.4f;
            p.TaskHintFirst = 45f;
            p.BriefingHintFirst = 30f;
            p.TaskHintRepeat = 50f;
        }

        /// <summary>
        /// Story changes challenge only, never content (7.5). Its tug is a real 5 to 8 s struggle that holding on
        /// cannot lose: no tension snap, a player strength just above her grip, a slow share, and a slip shorter
        /// than half a second is forgiven. Holding still drifts toward the player very slowly; pulling decides.
        /// Phase P, the reel: holding on creeps the file to the bin (her pull is -8 px/s), with no surges, no ramp and a gentler reel.
        /// </summary>
        static void Story(DifficultyProfile p)
        {
            var t = p.Tug;
            StorySpeedTug(t);
            t.reel.herPull = -8f;
            t.reel.surge = 0f;
            t.reel.reelRampPerSecond = 0f;
            t.reel.finishMax = 140f;
            t.reel.reelCap = 150f;
            t.reel.reelGain = 1f;
            p.GripBase = 0.41f;
            p.GripGrowth = 0f;
            p.GripCap = 0.41f;
            p.ReactionScale = 1.60f;
            p.RaceToNoDelayMin = 0.90f;
            p.RaceToNoDelayMax = 1.30f;
            p.InterceptDelay = 0.35f;
            p.CancelDelayMin = 0.80f;
            p.CancelDelayMax = 1.20f;
            p.GuardYesAfter = DifficultyProfile.Never;
            p.DragDialogRadius = 0f;
            p.CancelCrawl = 0.60f;
            p.CancelPatience = 3f;
            p.KeepAwayAfter = DifficultyProfile.Never;
            p.UrgencyPerDefense = 0.05f;
            p.UrgencyCap = 1.3f;
            p.AssistFloor = 2;
            p.MercyAfterLosses = 1;
            p.TaskHintFirst = 15f;
            p.BriefingHintFirst = 15f;
            p.TaskHintRepeat = 25f;
            p.TaskForceAfterHint = 90f;
            p.EntityTaskNudge = 20f;
            p.CodeHint1Delay = 30f;
            p.CodeFormatAfterFailures = 1;
            p.CodeGaryHint = 60f;
            p.CodeHint2Delay = 60f;
        }

        /// <summary>Story's speed-model tug, which the hold assist also plays in Speed mode.</summary>
        static void StorySpeedTug(TugOfWarSettings t)
        {
            t.startShare = 0.50f;
            t.playerWinShare = 0.15f;
            t.entityWinShare = 0.95f;
            t.shareRate = 0.30f;
            t.playerBaseStrength = 0.30f;
            t.pullSpeedForFullStrength = 250f;
            t.jiggleCredit = 0.0001f;
            t.maxPlayerStrength = 0.65f;
            t.effortSmoothing = 0.12f;
            t.maxTension = StoryNoSnap;
            t.strainTension = 300f;
            t.rampDelay = 99f;
            t.rampPerSecond = 0f;
            t.releaseGrace = 0.45f;
        }

        /// <summary>
        /// Phase P (A2), "Tug assist: Hold", applied to a contest's settings after <see cref="DifficultyProfile.TugFor"/>: holding the button
        /// brings the file to the finish in 3.5 s by itself and pulling makes it faster (a gentle reel); she neither pulls, surges nor ramps,
        /// and a slip has 1 s to grab it again. In Speed mode the contest plays Story's tug.
        /// </summary>
        public static void HoldAssist(TugOfWarSettings s)
        {
            StorySpeedTug(s);
            var r = s.reel;
            r.holdSeconds = 3.5f;
            r.herPull = 0f;
            r.surge = 0f;
            r.reelRampPerSecond = 0f;
            r.reelCap = 150f;
            r.reelGain = 1f;
            r.regrip = 1f;
        }
    }

    /// <summary>
    /// The entity's own let-go in a mercy contest (7.3): after <see cref="AdaptiveAssist.MercyHoldSeconds"/> of the
    /// player pulling with at least <see cref="AdaptiveAssist.MercyEffort"/>, or after
    /// <see cref="AdaptiveAssist.MercyAnyHoldSeconds"/> of simply holding on. Fed every frame by the conflict.
    /// </summary>
    public sealed class MercyRelease
    {
        float _pull;
        float _held;

        /// <summary>True when the entity lets go this step.</summary>
        public bool Step(float dt, bool playerHolding, float effort)
        {
            if (!playerHolding || dt <= 0f) return false;
            if (effort >= AdaptiveAssist.MercyEffort) _pull += dt;
            _held += dt;
            return _pull >= AdaptiveAssist.MercyHoldSeconds || _held >= AdaptiveAssist.MercyAnyHoldSeconds;
        }
    }

    /// <summary>What one assist level changes (7.3 table), applied on top of the profile.</summary>
    public readonly struct AssistEffects
    {
        public readonly float Grip;
        public readonly float Growth;
        public readonly float PullSpeed;
        public readonly float Ramp;
        public readonly float ReleaseGraceAdd;
        public readonly float RaceToNoDelayAdd;
        public readonly bool GuardYes;
        public readonly bool DragDialog;

        public AssistEffects(float grip, float growth, float pullSpeed, float ramp, float releaseGraceAdd, float raceToNoDelayAdd, bool guardYes, bool dragDialog)
        {
            Grip = grip;
            Growth = growth;
            PullSpeed = pullSpeed;
            Ramp = ramp;
            ReleaseGraceAdd = releaseGraceAdd;
            RaceToNoDelayAdd = raceToNoDelayAdd;
            GuardYes = guardYes;
            DragDialog = dragDialog;
        }
    }

    /// <summary>
    /// Adaptive assist (7.3): a level L from -1 to +3 for the current night that eases the second cursor
    /// after the player keeps losing and toughens it after easy wins. Engine-free and deterministic; fed by
    /// the conflict system (tugs) and the entity brain (other defenses).
    /// </summary>
    public sealed class AdaptiveAssist
    {
        public const int MinLevel = -1;
        public const int MaxLevel = 3;
        public const float TugLossWeight = 1f;
        /// <summary>A defense that ends the attempt as surely as a lost tug (a lost confirm race or Cancel fight).</summary>
        public const float AttemptLossWeight = 1f;
        /// <summary>A defense that only delays the player (a snatched icon, a closed window).</summary>
        public const float DefenseLossWeight = 0.5f;
        public const float LossStreakToRaise = 2f;
        public const int WinsToLower = 2;
        public const float EasyWinSeconds = 0.45f;
        public const float EasyWinEffort = 1.8f;
        /// <summary>Phase P, the reel's easy win: within this long after GET READY with a smoothed stroke of at least this (px/s).</summary>
        public const float ReelEasyWinSeconds = 0.8f;
        public const float ReelEasyWinStroke = 280f;
        /// <summary>Grip during a mercy contest (below every base strength: holding on cannot lose it).</summary>
        public const float MercyGrip = 0.15f;
        /// <summary>Player effort that counts toward the mercy release.</summary>
        public const float MercyEffort = 0.15f;
        /// <summary>Seconds of that effort after which the entity lets go by itself.</summary>
        public const float MercyHoldSeconds = 1.0f;
        /// <summary>Seconds of simply holding the button after which the entity lets go in a mercy contest.</summary>
        public const float MercyAnyHoldSeconds = 2.5f;

        /// <summary>
        /// Phase F: each level up is clearly easier (the third tug of a night needs about last night's first yank)
        /// and the confirm race opens up (an average player wins it at +1, a first-time player at +2).
        /// </summary>
        static readonly AssistEffects[] Table =
        {
            new AssistEffects(1.10f, 1.00f, 1.08f, 1.20f, 0.00f, -0.05f, true, true),   // -1
            new AssistEffects(1.00f, 1.00f, 1.00f, 1.00f, 0.00f, 0.00f, true, true),    //  0
            new AssistEffects(0.82f, 0.50f, 0.88f, 0.50f, 0.04f, 0.40f, true, true),    // +1
            new AssistEffects(0.68f, 0.25f, 0.78f, 0.20f, 0.08f, 0.80f, false, true),   // +2
            new AssistEffects(0.55f, 0.00f, 0.70f, 0.00f, 0.12f, 1.00f, false, false),  // +3
        };

        int _lossesAtMax;

        public int Level { get; private set; }
        public int Floor { get; }
        public int MercyAfterLosses { get; }
        /// <summary>Accumulated losses since the last level change (a tug, a lost confirm race or Cancel fight count 1, other defenses 0.5).</summary>
        public float LossStreak { get; private set; }
        public int WinStreak { get; private set; }
        /// <summary>The next contest is a mercy contest.</summary>
        public bool MercyArmed { get; private set; }
        /// <summary>The contest in progress is a mercy contest.</summary>
        public bool InMercyContest { get; private set; }

        /// <summary>(old level, new level).</summary>
        public event Action<int, int> LevelChanged;

        public AdaptiveAssist(int startLevel = 0, int floor = MinLevel, int mercyAfterLosses = 2)
        {
            Floor = Clamp(floor);
            MercyAfterLosses = Math.Max(1, mercyAfterLosses);
            Level = Math.Max(Floor, Clamp(startLevel));
        }

        /// <summary>A night's assist, starting where the profile says given the previous night's final level.</summary>
        public static AdaptiveAssist ForNight(DifficultyProfile p, int previousNightFinalLevel)
        {
            return new AdaptiveAssist(p.StartLevel(previousNightFinalLevel), p.AssistFloor, p.MercyAfterLosses);
        }

        /// <summary>Normal nights after the first start at clamp(previous final level - 1, 0, 1).</summary>
        public static int CarryLevel(int previousNightFinalLevel) => Math.Max(0, Math.Min(1, previousNightFinalLevel - 1));

        public static AssistEffects Effects(int level) => Table[Clamp(level) - MinLevel];

        public AssistEffects Current => Effects(Level);

        static int Clamp(int level) => Math.Max(MinLevel, Math.Min(MaxLevel, level));

        /// <summary>Called when a contest starts; true if it is a mercy contest.</summary>
        public bool BeginContest()
        {
            InMercyContest = MercyArmed;
            return InMercyContest;
        }

        /// <summary>
        /// A tug-of-war ended (<paramref name="peakEffort"/> from <see cref="ITugContest.PeakEffort"/>, <paramref name="elapsed"/> from its
        /// ActiveElapsed). Phase P: <paramref name="assisted"/> (the hold assist) wins never lower the level; its losses count as usual.
        /// </summary>
        public void ReportTug(bool playerWon, float elapsed, float peakEffort, TugModel model = TugModel.Speed, bool assisted = false)
        {
            bool mercy = InMercyContest;
            InMercyContest = false;
            if (mercy)
            {
                MercyArmed = false;
                _lossesAtMax = 0;
            }
            if (playerWon)
            {
                // Phase F: a won tug does not clear the loss streak, so an attempt that wins the tug and then loses
                // the confirm race still counts toward help.
                _lossesAtMax = 0;
                if (assisted) return;
                WinStreak++;
                bool easy = model == TugModel.Reel
                    ? elapsed < ReelEasyWinSeconds && peakEffort * TugReel.EffortSpeed >= ReelEasyWinStroke
                    : elapsed < EasyWinSeconds && peakEffort >= EasyWinEffort;
                if (WinStreak >= WinsToLower || easy)
                {
                    WinStreak = 0;
                    LossStreak = 0f; // a level change starts a fresh streak either way
                    SetLevelInternal(Math.Max(Level - 1, Floor));
                }
                return;
            }
            WinStreak = 0;
            // Story (floor +2) counts a loss at any level toward mercy; Normal only at the top.
            bool counts = Level == MaxLevel || Floor >= 2;
            if (counts && !mercy && ++_lossesAtMax >= MercyAfterLosses)
            {
                _lossesAtMax = 0;
                MercyArmed = true;
            }
            AddLoss(TugLossWeight);
        }

        /// <summary>A defense that was not a tug and only delayed the player (weight 0.5).</summary>
        public void ReportDefense() => ReportDefense(null);

        /// <summary>
        /// A defense that was not a tug, named as the brain names it: "no", "dialog", "guard" and "cancel" end the
        /// attempt (weight 1); anything else ("keepaway", "close", null) weighs 0.5.
        /// </summary>
        public void ReportDefense(string how)
        {
            WinStreak = 0;
            AddLoss(DefenseWeight(how));
        }

        public static float DefenseWeight(string how)
        {
            switch (how)
            {
                case "no":
                case "dialog":
                case "guard":
                case "cancel":
                    return AttemptLossWeight;
                default:
                    return DefenseLossWeight;
            }
        }

        /// <summary>Debug / checkpoint restore: force a level (streaks and mercy reset), never below the mode's floor.</summary>
        public void SetLevel(int level)
        {
            LossStreak = 0f;
            WinStreak = 0;
            _lossesAtMax = 0;
            MercyArmed = false;
            SetLevelInternal(Math.Max(Floor, Clamp(level)));
        }

        void AddLoss(float amount)
        {
            LossStreak += amount;
            if (LossStreak < LossStreakToRaise - 0.0001f) return;
            LossStreak = 0f;
            SetLevelInternal(Math.Min(Level + 1, MaxLevel));
        }

        void SetLevelInternal(int level)
        {
            if (level == Level) return;
            int old = Level;
            Level = level;
            GameLog.Info(LogChannel.Entity, "Assist level " + old + " -> " + level);
            LevelChanged?.Invoke(old, level);
        }
    }
}
