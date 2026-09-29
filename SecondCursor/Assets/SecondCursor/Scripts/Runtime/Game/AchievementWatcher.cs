using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Input;

namespace SecondCursor.Game
{
    /// <summary>
    /// Watches one night's systems and unlocks achievements through <see cref="Achievements.Unlock(GameServices,string)"/>
    /// using <see cref="AchievementRules"/>. It is attached after NightSetup, so the world set-up (which may set memory
    /// flags) never reaches it; checkpoint restores and memory merges raise no flag events; and every handler ignores
    /// what a director's Prepare does. The director calls <see cref="OnNightComplete"/>, <see cref="OnReply"/> and
    /// <see cref="OnRoundsSafe"/> itself.
    /// </summary>
    public sealed class AchievementWatcher
    {
        readonly GameServices _g;

        AchievementWatcher(GameServices g)
        {
            _g = g;
        }

        public static AchievementWatcher Attach(GameServices g)
        {
            var w = new AchievementWatcher(g);
            g.Flags.FlagSet += w.OnFlag;
            g.Flags.CounterChanged += w.OnCounter;
            if (g.Conflict != null) g.Conflict.TugEnded += w.OnTugEnded;
            if (g.Orders != null) g.Orders.Decided += w.OnOrderDecided;
            return w;
        }

        bool Preparing => _g.Director != null && _g.Director.IsPreparing;

        void Unlock(string id)
        {
            if (id != null) Achievements.Unlock(_g, id);
        }

        void OnFlag(string flag)
        {
            if (Preparing) return;
            Unlock(AchievementRules.OnFlag(flag));
            // CameraApp sets this only when the player selects CAM 00 (never a forced open or another cursor).
            if (flag == Flags.N3Cam00Viewed) Unlock(AchievementRules.OnCameraSelected(ContentIds.Cam00, true));
        }

        void OnCounter(string key, int value)
        {
            if (Preparing) return;
            Unlock(AchievementRules.OnCounter(key, value));
        }

        void OnOrderDecided(string orderId, string decision, CursorAgent by)
        {
            if (Preparing) return;
            Unlock(AchievementRules.OnOrderDecided(orderId, decision, by != null && by.IsPlayer));
        }

        void OnTugEnded(DragPayload payload, TugOutcome outcome)
        {
            if (Preparing) return;
            bool won = outcome == TugOutcome.PlayerWins;
            bool forced = _g.Conflict != null && _g.Conflict.LastOutcomeForced;
            // Tug totals and both tug achievements only count real fights in runs that count.
            if (forced)
            {
                if (won) GameLog.Info(LogChannel.System, "Achievement held (forced tug outcome): " + AchievementIds.FirmGrip);
                return;
            }
            if (!_g.RecordsArmed)
            {
                if (won) Unlock(AchievementRules.OnTugWon(false));
                return;
            }
            int wins = SaveSystem.RecordTug(won);
            if (!won) return;
            Unlock(AchievementRules.OnTugWon(false));
            SteamBridge.SetTugWins(wins);
            if (wins == AchievementIds.TugWinsProgressAt) SteamBridge.IndicateProgress(AchievementIds.WhiteKnuckles, wins, AchievementIds.TugWinsGoal);
            Unlock(AchievementRules.OnTugTotal(wins));
        }

        /// <summary>The night was completed (after its progress was saved, so endingsSeen includes this ending).</summary>
        public void OnNightComplete(int night, string endingId)
        {
            var seen = SaveSystem.Load().endingsSeen;
            foreach (var id in AchievementRules.OnNightComplete(night, endingId, seen)) Unlock(id);
        }

        /// <summary>The player's typed reply was answered (<paramref name="voice"/>: the exchange's voice, "" for Ellen).</summary>
        public void OnReply(string voice, string tag)
        {
            if (Preparing || string.IsNullOrEmpty(tag)) return;
            Unlock(AchievementRules.OnReply(voice, tag));
        }

        /// <summary>A Custodial round ended safe (the seat was never cleared) with this highest stage.</summary>
        public void OnRoundsSafe(int night, int maxStage)
        {
            if (Preparing) return;
            Unlock(AchievementRules.OnRoundsSafe(night, maxStage));
        }
    }
}
