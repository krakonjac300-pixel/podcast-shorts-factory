using System;
using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Progress bookkeeping shared by every night: when the night really starts (after the title), how long it has
    /// been played (checkpoints carry it), the Prepare guard for achievements, and a difficulty change from the pause
    /// menu, which takes effect at the next checkpoint beat.
    /// </summary>
    public abstract partial class NightDirector
    {
        bool _startMarked;

        /// <summary>Nights started or resumed in this app launch (the Welcome back refresher is for the first one).</summary>
        internal static int NightsThisLaunch;

        /// <summary>
        /// LaunchAudit 18: a player who opens Night 2 or 3 as the first night of a fresh session (Continue, Night Select)
        /// gets a short refresher before the first task, in the OS's own voice.
        /// </summary>
        protected IEnumerator WelcomeBack()
        {
            if (Night <= 1 || NightsThisLaunch > 1 || !_g.RecordsArmed) yield break;
            var box = Dialogs.Message(_g, _g.Content.Text("welcome.title"), _g.Content.Text("welcome.body"), "icon_info", new[] { "Begin" }, null);
            GameLog.Info(LogChannel.Story, "Welcome back refresher shown");
            float until = Time.time + WelcomeBackSeconds;
            while (box.IsOpen && Time.time < until) yield return null;
            if (box.IsOpen) box.Window.Close(null);
            yield return Wait(0.6f);
        }

        const float WelcomeBackSeconds = 90f;

        /// <summary>True while Prepare sets the world up for a jump (nothing it does may unlock an achievement).</summary>
        public bool IsPreparing { get; private set; }

        /// <summary>Seconds of this night played so far (from the night card, or carried by a checkpoint).</summary>
        public float NightElapsed => Mathf.Max(0f, Time.time - NightStartedAt);

        /// <summary>
        /// The night starts for real (the boot beat, after any title menu): record its start in the save (first-start
        /// memory and trust, Continue's night) and start the play-time clock. Once per root.
        /// </summary>
        public void MarkNightStarted()
        {
            if (_startMarked) return;
            _startMarked = true;
            NightsThisLaunch++;
            NightStartedAt = Time.time;
            if (IsStandIn) return;
            SaveSystem.RecordNightStart(_g);
            GameLog.Info(LogChannel.System, "Night " + Night + " started" + (_g.FromNightSelect ? " from Night Select" : ""));
        }

        /// <summary>Continue from a checkpoint: the play time before it counts too.</summary>
        public void ResumeElapsed(float seconds)
        {
            if (!_startMarked) NightsThisLaunch++;
            _startMarked = true;
            NightStartedAt = Time.time - Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// The pause menu saved a different difficulty: switch to it now (a checkpoint beat, where no contest or round is
        /// running). A new profile, and a new assist that keeps the current level (never below the new floor).
        /// </summary>
        void ApplyPendingDifficulty()
        {
            var g = _g;
            if (g.Difficulty == null) return;
            var mode = DifficultyTable.ParseMode(SaveSystem.Load().difficulty);
            if (mode == g.Difficulty.Mode) return;
            var profile = GameRoot.MakeDifficulty(g.Night, mode);
            var old = g.Assist;
            var assist = new AdaptiveAssist(Math.Max(profile.AssistFloor, old != null ? old.Level : 0), profile.AssistFloor, profile.MercyAfterLosses);
            if (old != null) old.FirstRaise -= ShowConflictToast;
            assist.FirstRaise += ShowConflictToast;
            g.Difficulty = profile;
            g.Assist = assist;
            if (g.Entity != null)
            {
                g.Entity.Personality.grip = profile.GripBase;
                g.Entity.Personality.reactionScale = profile.ReactionScale;
            }
            GameLog.Info(LogChannel.System, "Difficulty applied: " + mode + " (assist level " + assist.Level + ")");
        }
    }
}
