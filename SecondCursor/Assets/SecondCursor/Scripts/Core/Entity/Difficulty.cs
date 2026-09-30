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
        /// <summary>A company task is force-completed this long after its first hint.</summary>
        public float TaskForceAfterHint = 150f;
        /// <summary>Night 1 style: the NEXUS conflict toast after the first lost tug (else only when the assist first rises).</summary>
        public bool ConflictToastOnFirstLoss = true;
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
        /// the night's first contest, which starts with <see cref="ReadGraceSeconds"/> of standoff.
        /// </summary>
        public TugOfWarSettings TugFor(AdaptiveAssist assist, bool mercy = false, bool readGrace = false)
        {
            var s = Tug.Clone();
            s.readGrace = readGrace ? ReadGraceSeconds : 0f;
            var fx = AdaptiveAssist.Effects(assist != null ? assist.Level : 0);
            s.pullSpeedForFullStrength *= fx.PullSpeed;
            s.rampPerSecond *= fx.Ramp;
            s.releaseGrace += fx.ReleaseGraceAdd;
            if (mercy)
            {
                // A mercy contest never ramps up: the entity is about to let go.
                s.rampPerSecond = 0f;
                s.rampDelay = 99f;
            }
            return s;
        }

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
            t.maxTension = 300f;
            t.rampDelay = 2.5f;
            t.rampPerSecond = 0.18f;
            t.releaseGrace = 0.08f;
            p.GripBase = 0.68f;
            p.GripGrowth = 0.05f;
            p.GripCap = 1.40f;
            p.ReactionScale = 0.90f;
            p.RaceToNoDelayMin = 0.20f;
            p.RaceToNoDelayMax = 0.40f;
            p.InterceptDelay = 0.15f;
            p.DragDialogRadius = 80f;
            p.CancelCrawl = 0.30f;
            p.CancelDelayMin = 0.30f;
            p.CancelDelayMax = 0.50f;
            p.TaskHintFirst = 40f;
            p.BriefingHintFirst = 30f;
            p.TaskHintRepeat = 45f;
            p.ConflictToastOnFirstLoss = true; // Phase G: a player resuming Night 2 in a new session is reminded too (text only)
        }

        static void Night3(DifficultyProfile p)
        {
            var t = p.Tug;
            t.startShare = 0.55f;
            t.entityWinShare = 0.90f;
            t.pullSpeedForFullStrength = 460f;
            t.maxTension = 320f;
            t.rampDelay = 2.0f;
            t.rampPerSecond = 0.22f;
            t.releaseGrace = 0.08f;
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
            p.UrgencyPerDefense = 0.18f;
            p.UrgencyCap = 2.4f;
            p.TaskHintFirst = 45f;
            p.BriefingHintFirst = 30f;
            p.TaskHintRepeat = 50f;
            p.ConflictToastOnFirstLoss = true; // Phase G: same for Night 3
        }

        /// <summary>
        /// Story changes challenge only, never content (7.5). Its tug is a real 5 to 8 s struggle that holding on
        /// cannot lose: no tension snap, a player strength just above her grip, a slow share, and a slip shorter
        /// than half a second is forgiven. Holding still drifts toward the player very slowly; pulling decides.
        /// </summary>
        static void Story(DifficultyProfile p)
        {
            var t = p.Tug;
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
            p.ConflictToastOnFirstLoss = true;
            p.EntityTaskNudge = 20f;
            p.CodeHint1Delay = 30f;
            p.CodeFormatAfterFailures = 1;
            p.CodeGaryHint = 60f;
            p.CodeHint2Delay = 60f;
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
        public bool RaisedThisNight { get; private set; }

        /// <summary>(old level, new level).</summary>
        public event Action<int, int> LevelChanged;
        /// <summary>The first time this night the level goes up (the conflict toast shows again).</summary>
        public event Action FirstRaise;

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

        /// <summary>A tug-of-war ended (<paramref name="peakEffort"/> from <see cref="TugOfWar.PeakEffort"/>).</summary>
        public void ReportTug(bool playerWon, float elapsed, float peakEffort)
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
                WinStreak++;
                bool easy = elapsed < EasyWinSeconds && peakEffort >= EasyWinEffort;
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
            int before = Level;
            SetLevelInternal(Math.Min(Level + 1, MaxLevel));
            if (Level > before && !RaisedThisNight)
            {
                RaisedThisNight = true;
                FirstRaise?.Invoke();
            }
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
