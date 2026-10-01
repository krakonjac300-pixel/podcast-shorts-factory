using System;

namespace SecondCursor.Core.Audio
{
    /// <summary>Why an ambient scare may not play right now (Phase M). Flags, so a slot can say which ones it ignores.</summary>
    [Flags]
    public enum ScareGate
    {
        None = 0,
        Paused = 1 << 0,
        Climax = 1 << 1,
        Budget = 1 << 2,
        Tug = 1 << 3,
        Dialog = 1 << 4,
        Typing = 1 << 5,
        Reply = 1 << 6,
        Drag = 1 << 7,
        Tip = 1 << 8,
        Tutorial = 1 << 9,
        WatchingSelf = 1 << 10,
        BeatStart = 1 << 11,
        Early = 1 << 12,
        StingerQuiet = 1 << 13,
        EventNear = 1 << 14,
        Cooldown = 1 << 15,
    }

    /// <summary>Where a pool scare is heard: the middle, where the player's cursor is, one ear, or instead of the player's next click.</summary>
    public enum ScarePan { Centre, Cursor, OneEar, NextClick }

    /// <summary>One sound the "from time to time" pool can pick.</summary>
    public readonly struct PoolSound
    {
        public readonly string Id;
        public readonly float Volume;
        public readonly ScarePan Pan;

        public PoolSound(string id, float volume, ScarePan pan) { Id = id; Volume = volume; Pan = pan; }
    }

    /// <summary>What the game looks like when a scare wants to play (filled in by the runtime scheduler every frame it tries).</summary>
    public struct ScareContext
    {
        public bool Paused, Climax, Tug, Dialog, Typing, AwaitingReply, Dragging, TipShowing, TutorialOpen, WatchingSelf, Finale;
        public float NightSeconds, BeatSeconds, SinceScare, SinceStinger, SinceEvent;
        public int Played, PoolPlayed;
    }

    /// <summary>One night's scare plan: its budget, cooldowns and the pool that fills long quiet stretches.</summary>
    public sealed class ScareNight
    {
        public int Night;
        /// <summary>Ambient scares that may play tonight, scripted and pool together (stingers and climaxes are not counted).</summary>
        public int Budget;
        /// <summary>At most this many of them from the pool, so the scripted slots always have room.</summary>
        public int PoolLimit;
        public float Cooldown, FinaleCooldown;
        /// <summary>No pool scare before this many seconds of the night (scripted slots are placed by the story, never that early).</summary>
        public float Earliest;
        public float PoolEveryMin, PoolEveryMax;
        public string[] PoolBeats = Array.Empty<string>();
        public PoolSound[] Pool = Array.Empty<PoolSound>();
        /// <summary>Phase Q1: quiet scares born from the player's own actions (no sound of their own) that may happen tonight.</summary>
        public int ActionBudget = 2;
    }

    /// <summary>
    /// The rules every ambient scare obeys (SoundDesign.md section 4): rare, per-night budgets, a global cooldown, and never over
    /// the things the game is about (a tug, a dialog, typing and reading, a file on the pointer, a first-time tip, the tutorial).
    /// Engine-free; the runtime <c>ScareScheduler</c> fills in a <see cref="ScareContext"/> and asks <see cref="Check"/>.
    /// </summary>
    public static class ScareRules
    {
        public const float BeatStartSeconds = 8f, StingerQuietSeconds = 30f, EventNearSeconds = 6f;
        /// <summary>A story sound at this call volume or more counts as an event nothing may crowd.</summary>
        public const float EventMinVolume = 0.4f;
        /// <summary>
        /// A slot that is part of a story event ignores everything but these (Review M3: a tug or a dialog too; such a slot waits up to
        /// <see cref="StoryEventWindow"/> for them to end).
        /// </summary>
        public const ScareGate StoryEventGates = ScareGate.Paused | ScareGate.Climax | ScareGate.Budget | ScareGate.Tug | ScareGate.Dialog;
        public const ScareGate IgnoreAllButStory = ~StoryEventGates;
        public const float StoryEventWindow = 10f;

        static readonly ScareNight[] Nights =
        {
            new ScareNight { Night = 1, Budget = 2, PoolLimit = 0, Cooldown = 90f, FinaleCooldown = 90f, Earliest = 180f },
            new ScareNight
            {
                Night = 2, Budget = 6, PoolLimit = 2, Cooldown = 60f, FinaleCooldown = 60f, Earliest = 120f, PoolEveryMin = 110f, PoolEveryMax = 170f,
                PoolBeats = new[] { "help", "asks", "third" },
                Pool = new[] { new PoolSound("chair_creak", 0.45f, ScarePan.Centre), new PoolSound("key_tap_rev", 0.5f, ScarePan.Cursor), new PoolSound("whisper_burst", 0.45f, ScarePan.OneEar) },
            },
            new ScareNight
            {
                Night = 3, Budget = 9, PoolLimit = 2, Cooldown = 40f, FinaleCooldown = 25f, Earliest = 120f, PoolEveryMin = 80f, PoolEveryMax = 130f,
                PoolBeats = new[] { "work", "ruth" },
                Pool = new[]
                {
                    new PoolSound("chair_creak", 0.55f, ScarePan.Centre), new PoolSound("key_tap_rev", 0.6f, ScarePan.Cursor),
                    new PoolSound("whisper_burst", 0.55f, ScarePan.OneEar), new PoolSound("click_wrong", 1f, ScarePan.NextClick),
                },
            },
        };

        /// <summary>The plan for a night (nights past the last use the last one's).</summary>
        public static ScareNight For(int night) => Nights[Math.Max(1, Math.Min(Nights.Length, night)) - 1];

        /// <summary>
        /// The first reason (in the order of <see cref="ScareGate"/>) a scare may not play now, or <see cref="ScareGate.None"/>.
        /// <paramref name="pool"/>: a pool pick also needs room under <see cref="ScareNight.PoolLimit"/>.
        /// </summary>
        public static ScareGate Check(in ScareContext c, ScareNight n, bool pool, ScareGate ignore = ScareGate.None)
        {
            bool budget = c.Played >= n.Budget || (pool && c.PoolPlayed >= n.PoolLimit);
            if (Blocks(ScareGate.Paused, c.Paused, ignore)) return ScareGate.Paused;
            if (Blocks(ScareGate.Climax, c.Climax, ignore)) return ScareGate.Climax;
            if (Blocks(ScareGate.Budget, budget, ignore)) return ScareGate.Budget;
            if (Blocks(ScareGate.Tug, c.Tug, ignore)) return ScareGate.Tug;
            if (Blocks(ScareGate.Dialog, c.Dialog, ignore)) return ScareGate.Dialog;
            if (Blocks(ScareGate.Typing, c.Typing, ignore)) return ScareGate.Typing;
            if (Blocks(ScareGate.Reply, c.AwaitingReply, ignore)) return ScareGate.Reply;
            if (Blocks(ScareGate.Drag, c.Dragging, ignore)) return ScareGate.Drag;
            if (Blocks(ScareGate.Tip, c.TipShowing, ignore)) return ScareGate.Tip;
            if (Blocks(ScareGate.Tutorial, c.TutorialOpen, ignore)) return ScareGate.Tutorial;
            if (Blocks(ScareGate.WatchingSelf, c.WatchingSelf, ignore)) return ScareGate.WatchingSelf;
            if (Blocks(ScareGate.BeatStart, c.BeatSeconds < BeatStartSeconds, ignore)) return ScareGate.BeatStart;
            if (pool && Blocks(ScareGate.Early, c.NightSeconds < n.Earliest, ignore)) return ScareGate.Early;
            if (Blocks(ScareGate.StingerQuiet, c.SinceStinger < StingerQuietSeconds, ignore)) return ScareGate.StingerQuiet;
            if (Blocks(ScareGate.EventNear, c.SinceEvent < EventNearSeconds, ignore)) return ScareGate.EventNear;
            if (Blocks(ScareGate.Cooldown, c.SinceScare < (c.Finale ? n.FinaleCooldown : n.Cooldown), ignore)) return ScareGate.Cooldown;
            return ScareGate.None;
        }

        /// <summary>Phase Q1: an action scare keeps at least this far from any ambient scare (it has no cooldown of its own against the sounds).</summary>
        public const float ActionSeparation = 20f;

        /// <summary>
        /// Phase Q1: an action scare (the player's own icon back where it was, their own words in the inbox) obeys every no-scare gate and its own
        /// per-night <see cref="ScareNight.ActionBudget"/>; it does not spend the sound budget and keeps <see cref="ActionSeparation"/> from a
        /// sound scare and from the last action scare (<paramref name="sinceAction"/>).
        /// </summary>
        public static ScareGate CheckAction(in ScareContext c, ScareNight n, int actionsPlayed, float sinceAction, ScareGate ignore = ScareGate.None)
        {
            if (actionsPlayed >= n.ActionBudget && (ignore & ScareGate.Budget) == 0) return ScareGate.Budget;
            var gate = Check(c, n, false, ignore | ScareGate.Budget | ScareGate.Cooldown);
            if (gate != ScareGate.None) return gate;
            if (Blocks(ScareGate.Cooldown, c.SinceScare < ActionSeparation || sinceAction < ActionSeparation, ignore)) return ScareGate.Cooldown;
            return ScareGate.None;
        }

        static bool Blocks(ScareGate gate, bool condition, ScareGate ignore) => condition && (ignore & gate) == 0;

        /// <summary>True when the pool may run during this beat.</summary>
        public static bool PoolBeat(ScareNight n, string beat) => Array.IndexOf(n.PoolBeats, beat) >= 0;

        /// <summary>Seconds until the next pool try, for a uniform <paramref name="u"/> in [0, 1).</summary>
        public static float PoolDelay(ScareNight n, float u) => n.PoolEveryMin + (n.PoolEveryMax - n.PoolEveryMin) * u;

        /// <summary>A pool sound for a uniform <paramref name="u"/>, never the one played last (when there is a choice).</summary>
        public static PoolSound PickPool(ScareNight n, float u, string lastId)
        {
            int count = n.Pool.Length, skip = count > 1 ? Array.FindIndex(n.Pool, p => p.Id == lastId) : -1;
            int choices = skip >= 0 ? count - 1 : count;
            int i = Math.Min(choices - 1, (int)(u * choices));
            if (skip >= 0 && i >= skip) i++;
            return n.Pool[i];
        }
    }
}
