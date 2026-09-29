using System;
using SecondCursor.Core.Entity;

namespace SecondCursor.Core.Story
{
    /// <summary>What happens when the watch meter fills on the last stage of a route.</summary>
    public enum RoundsFinalRule
    {
        /// <summary>The round ends early (Night 2: the figure reached the doorway).</summary>
        EndEarly,
        /// <summary>The seat is cleared (Night 3: the figure stood behind the chair long enough).</summary>
        ClearSeat,
    }

    /// <summary>
    /// One Custodial round (expansion spec 7.4): the route of figure stages with the camera that shows each,
    /// how many watched seconds move it one stage, the reopen penalty, forced opens and Ellen's close reaction.
    /// </summary>
    public sealed class RoundsConfig
    {
        public string Id = "";
        /// <summary>Figure stage names in route order (the camera rig's stage names, e.g. "HallFar").</summary>
        public string[] Stages = Array.Empty<string>();
        /// <summary>The camera that shows each stage.</summary>
        public string[] Cameras = Array.Empty<string>();
        public int StartStage;
        /// <summary>Watched seconds that move the figure one stage.</summary>
        public float WatchSeconds = 5f;
        /// <summary>Added at once when the player opens or restores the viewer onto the figure's camera.</summary>
        public float ReopenPenalty = 0.5f;
        public RoundsFinalRule AtLastStage = RoundsFinalRule.EndEarly;
        /// <summary>Stage index the figure never moves past (-1 = none). Story clamps at "Middle".</summary>
        public int ClampStage = -1;
        /// <summary>Seconds the round lasts (0 = until it is stopped).</summary>
        public float Duration = 90f;
        /// <summary>Seconds after the start at which Security forces the viewer open on the figure's camera.</summary>
        public float[] ForcedOpenTimes = Array.Empty<float>();
        /// <summary>After the listed times, forced opens repeat every Min..Max seconds (0 = no repeats).</summary>
        public float ForcedOpenRepeatMin;
        public float ForcedOpenRepeatMax;
        /// <summary>How long Ellen waits before closing a viewer that shows the figure.</summary>
        public float CloseReactionMin = 1.2f;
        public float CloseReactionMax = 2.0f;

        public int IndexOf(string stage) => Array.IndexOf(Stages, stage);

        /// <summary>Night 2's first round: the near end of the hall, the office door, the doorway. 90 s.</summary>
        public static RoundsConfig Night2(DifficultyMode mode, float trust)
        {
            var c = new RoundsConfig
            {
                Id = "n2_round",
                Stages = new[] { "HallFar", "Corridor", "Doorway" },
                Cameras = new[] { "cam02", "cam02", "cam03" },
                StartStage = 0,
                WatchSeconds = 5f,
                ReopenPenalty = 0.5f,
                AtLastStage = RoundsFinalRule.EndEarly,
                Duration = 90f,
                ForcedOpenTimes = new[] { 0f, 45f },
            };
            ApplyCloseReaction(c, mode, trust);
            if (mode == DifficultyMode.Story) ApplyStory(c);
            return c;
        }

        /// <summary>
        /// Night 3's full round, 3:00 to 3:30 (360 s): from Sublevel C up to the seat. Watching the last stage
        /// long enough clears the seat. Starts in the Lobby if the figure was watched to the door on Night 2;
        /// slower if 214's source file was hidden. Security forces the viewer open at once, then every 22-30 s.
        /// </summary>
        public static RoundsConfig Night3(DifficultyMode mode, float trust, bool watchedToDoor, bool hid214)
        {
            var c = new RoundsConfig
            {
                Id = "n3_round",
                Stages = new[] { "SublevelC", "Lobby", "HallFar", "Corridor", "Doorway", "Middle", "BehindChair" },
                Cameras = new[] { "cam04", "cam01", "cam02", "cam02", "cam03", "cam03", "cam03" },
                StartStage = watchedToDoor ? 1 : 0,
                WatchSeconds = hid214 ? 5f : 4f,
                ReopenPenalty = 1f,
                AtLastStage = RoundsFinalRule.ClearSeat,
                Duration = 360f,
                ForcedOpenTimes = new[] { 0f },
                ForcedOpenRepeatMin = 22f,
                ForcedOpenRepeatMax = 30f,
            };
            ApplyCloseReaction(c, mode, trust);
            if (mode == DifficultyMode.Story) ApplyStory(c);
            return c;
        }

        /// <summary>
        /// Night 3's finale feed (6:41 until an exit): the corridor to the seat, 3 s per stage; a cleared seat is
        /// KEEP. The director forces the viewer open by the clock (6:50, 6:55, 7:00, 7:02), not by elapsed time.
        /// </summary>
        public static RoundsConfig Night3Finale(DifficultyMode mode, float trust)
        {
            var c = new RoundsConfig
            {
                Id = "n3_finale",
                Stages = new[] { "Corridor", "Doorway", "Middle", "BehindChair" },
                Cameras = new[] { "cam02", "cam03", "cam03", "cam03" },
                StartStage = 0,
                WatchSeconds = 3f,
                ReopenPenalty = 1f,
                AtLastStage = RoundsFinalRule.ClearSeat,
                Duration = 0f,
            };
            ApplyCloseReaction(c, mode, trust);
            if (mode == DifficultyMode.Story)
            {
                // Same as ApplyStory, but the clock still decides the forced opens.
                c.WatchSeconds = 10f;
                c.ReopenPenalty = 0f;
                c.ClampStage = c.IndexOf("Middle");
            }
            return c;
        }

        /// <summary>Ellen's close reaction from trust (7.4): she protects a player she trusts sooner.</summary>
        public static void ApplyCloseReaction(RoundsConfig c, DifficultyMode mode, float trust)
        {
            if (mode == DifficultyMode.Story) { c.CloseReactionMin = 0.6f; c.CloseReactionMax = 0.9f; }
            else if (trust >= 0.3f) { c.CloseReactionMin = 0.8f; c.CloseReactionMax = 1.4f; }
            else if (trust <= -0.3f) { c.CloseReactionMin = 2.0f; c.CloseReactionMax = 3.0f; }
            else { c.CloseReactionMin = 1.2f; c.CloseReactionMax = 2.0f; }
        }

        /// <summary>Story difficulty: slower Custodial, no reopen penalty, never past "Middle".</summary>
        static void ApplyStory(RoundsConfig c)
        {
            c.WatchSeconds = 10f;
            c.ReopenPenalty = 0f;
            c.ClampStage = c.IndexOf("Middle");
            c.ForcedOpenRepeatMin = c.ForcedOpenRepeatMax = 45f;
            if (c.ForcedOpenTimes.Length > 1) c.ForcedOpenTimes = new[] { c.ForcedOpenTimes[0] };
        }

        public float CloseReaction(float random01) => CloseReactionMin + (CloseReactionMax - CloseReactionMin) * MathUtil.Clamp01(random01);

        /// <summary>Ellen closes the viewer <paramref name="seconds"/> sooner from now on (Night 3: once it reaches the doorway).</summary>
        public void Hasten(float seconds)
        {
            CloseReactionMin = Math.Max(0.2f, CloseReactionMin - seconds);
            CloseReactionMax = Math.Max(CloseReactionMin, CloseReactionMax - seconds);
        }
    }

    /// <summary>
    /// The Custodial watch meter (engine-free, expansion spec 7.4). While the Camera Viewer shows the figure's
    /// current camera, watched time accumulates; when it reaches the threshold the figure cuts to the next
    /// stage. It never moves while nobody watches it and never goes back. Opening or restoring the viewer
    /// onto its camera adds the reopen penalty at once (looking again brings it closer).
    /// </summary>
    public sealed class CustodialRounds
    {
        public readonly RoundsConfig Config;

        public int Stage { get; private set; }
        /// <summary>Watched seconds on the current stage.</summary>
        public float Meter { get; private set; }
        /// <summary>The round is over (the last stage filled). Elapsed time is the caller's business.</summary>
        public bool Finished { get; private set; }
        /// <summary>The furthest stage reached so far.</summary>
        public int MaxStage { get; private set; }

        /// <summary>The figure moved on: the new stage index.</summary>
        public event Action<int> StageAdvanced;
        /// <summary>The last stage filled on a route that ends early.</summary>
        public event Action ReachedFinal;
        /// <summary>The last stage filled on a route that clears the seat.</summary>
        public event Action SeatCleared;

        public CustodialRounds(RoundsConfig config)
        {
            Config = config ?? new RoundsConfig();
            if (Config.Stages.Length == 0) Finished = true;
            Stage = Math.Max(0, Math.Min(Config.StartStage, Math.Max(0, Config.Stages.Length - 1)));
            MaxStage = Stage;
        }

        public string FigureStage => Stage < Config.Stages.Length ? Config.Stages[Stage] : "";
        public string FigureCamera => Stage < Config.Cameras.Length ? Config.Cameras[Stage] : "";
        public bool IsLastStage => Stage >= Config.Stages.Length - 1;
        /// <summary>0..1 of the current stage's threshold.</summary>
        public float Progress => Config.WatchSeconds <= 0f ? 1f : MathUtil.Clamp01(Meter / Config.WatchSeconds);

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds with the viewer showing <paramref name="viewedCamera"/>
        /// (null when the viewer is closed or minimized). Returns true if the figure moved.
        /// </summary>
        public bool Tick(float dt, string viewedCamera)
        {
            if (Finished || dt <= 0f || string.IsNullOrEmpty(viewedCamera) || viewedCamera != FigureCamera) return false;
            return Add(dt);
        }

        /// <summary>The player opened or restored the viewer onto <paramref name="camera"/> themselves.</summary>
        public bool NotifyReopen(string camera)
        {
            if (Finished || Config.ReopenPenalty <= 0f || string.IsNullOrEmpty(camera) || camera != FigureCamera) return false;
            return Add(Config.ReopenPenalty);
        }

        /// <summary>Debug: put the figure on a stage (never past the route's end).</summary>
        public void ForceStage(int stage)
        {
            if (Config.Stages.Length == 0) return;
            Stage = Math.Max(0, Math.Min(stage, Config.Stages.Length - 1));
            MaxStage = Math.Max(MaxStage, Stage);
            Meter = 0f;
            StageAdvanced?.Invoke(Stage);
        }

        bool Add(float seconds)
        {
            Meter += seconds;
            if (Meter < Config.WatchSeconds) return false;
            if (Config.ClampStage >= 0 && Stage >= Config.ClampStage)
            {
                Meter = Config.WatchSeconds; // Story: it stops here, however long you watch
                return false;
            }
            if (IsLastStage)
            {
                Meter = Config.WatchSeconds;
                Finished = true;
                if (Config.AtLastStage == RoundsFinalRule.ClearSeat) SeatCleared?.Invoke();
                else ReachedFinal?.Invoke();
                return false;
            }
            Meter = 0f;
            Stage++;
            MaxStage = Math.Max(MaxStage, Stage);
            StageAdvanced?.Invoke(Stage);
            return true;
        }
    }
}
