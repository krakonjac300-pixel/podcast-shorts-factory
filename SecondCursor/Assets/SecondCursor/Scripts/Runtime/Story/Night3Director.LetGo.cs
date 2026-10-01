// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase P (review board T1, plan 1.8): the finale's fight over employee_017.dat at the bin is shaped by the relationship. Unless trust is
    /// very low (and she was neither asked to let go nor called by her name), she lets it go: her hand still grabs the file, but the contest is
    /// a hold (no pull, no surges) that brings it to the bin in 3 s while she types I CANT STOP MY HAND, HOLD ON, DONT LET GO paced to it. An
    /// early release takes nothing (she types AGAIN and the next hold is 1.5 s). Won, she walks with the file to the bin, Confirm Shred is not
    /// raced, and her pointer rests on No, then on Yes beside yours (EntityBrain.LetGo.cs); kept Gary types "go on casey". A cruel player
    /// gets the Night 3 fight instead, and after the second fight she wins her hand gives up and the mode turns to LetGo. A hold under way
    /// holds the 7:05 KEEP (within its grace).
    /// </summary>
    public sealed partial class Night3Director
    {
        FinaleBinMode _binMode = FinaleBinMode.Fight;
        bool _saidLetGo, _letGoRetry;
        int _refusedContests, _letGoLinesSaid;
        Action<DragPayload> _onLetGoTug;
        Action<DragPayload, TugOutcome> _onLetGoEnded;

        /// <summary>A LetGo hold over 017 is under way (7:05 waits for it, within <see cref="Night3Rules.ExitGrace"/>).</summary>
        bool LetGoHoldRunning => _binMode == FinaleBinMode.LetGo && _g.Conflict.IsFighting && _g.Conflict.Reel != null
                                 && _g.Conflict.Reel.Variant == TugVariant.LetGo;

        void HookLetGo()
        {
            UnhookLetGo();
            var g = _g;
            _saidLetGo = _letGoRetry = false;
            _refusedContests = _letGoLinesSaid = 0;
            _binMode = FinaleBinMode.Fight;
            UpdateBinMode("finale start");
            g.Conflict.Customize = CustomizeFinale;
            g.Conflict.ContestCapOff = true;
            _onLetGoTug = OnLetGoTugStarted;
            _onLetGoEnded = OnLetGoTugEnded;
            g.Conflict.TugStarted += _onLetGoTug;
            g.Conflict.TugEnded += _onLetGoEnded;
        }

        void UnhookLetGo()
        {
            var g = _g;
            if (_onLetGoTug != null) g.Conflict.TugStarted -= _onLetGoTug;
            if (_onLetGoEnded != null) g.Conflict.TugEnded -= _onLetGoEnded;
            _onLetGoTug = null;
            _onLetGoEnded = null;
            g.Conflict.Customize = null;
            g.Conflict.ContestCapOff = false;
            E.Brain.LetsGo = false;
            // FollowFile and RestOnYes tremble her pointer; a behaviour stopped mid-way never set it back.
            if (E.View != null) E.View.Jitter = 0f;
        }

        /// <summary>Decides the mode from what is known now; once she lets go, it stays that way.</summary>
        void UpdateBinMode(string why)
        {
            if (_binMode == FinaleBinMode.LetGo && E.Brain.LetsGo) return;
            var mode = Night3Rules.BinMode(_g.Memory.Trust, _saidLetGo, MemoryFlags.SaidNameAny(_g.Flags), _refusedContests);
            bool changed = mode != _binMode || why == "finale start";
            _binMode = mode;
            var brain = E.Brain;
            brain.LetsGo = mode == FinaleBinMode.LetGo;
            if (brain.LetsGo) brain.AllowKeepAway = false;
            if (changed)
                GameLog.Info(LogChannel.Story, "Finale bin mode: " + mode + " (" + why + "; trust " + _g.Memory.Trust.ToString("0.00") + ", said let go " + _saidLetGo
                    + ", name " + MemoryFlags.SaidNameAny(_g.Flags) + ", refused contests " + _refusedContests + ")");
        }

        /// <summary>A reply in the final exchange asked her to let go: the next grab is a hold.</summary>
        void NoteFinalReplyForLetGo(DialogueReply r)
        {
            if (r == null) return;
            if (r.Tag == "letgo") _saidLetGo = true;
            if (r.Tag == "letgo" || r.Tag == "name") UpdateBinMode("reply " + r.Tag);
        }

        /// <summary>The LetGo variant for 017 (any tug model: the hold is the reel's LetGo variant); Fight mode keeps the night's contest.</summary>
        TugOfWarSettings CustomizeFinale(DragPayload p, TugOfWarSettings s)
        {
            if (p == null || p.FileId != ContentIds.File017 || _exit != Night3Exit.None || CurrentBeat != "finale") return s;
            UpdateBinMode("grab");
            if (_binMode != FinaleBinMode.LetGo) return s;
            s.model = TugModel.Reel;
            var r = s.reel;
            r.holdSeconds = _letGoRetry ? Night3Rules.LetGoRetryHoldSeconds : Night3Rules.LetGoHoldSeconds;
            r.releaseNeverLoses = true;
            r.herPull = 0f;
            r.surge = 0f;
            r.reelRampPerSecond = 0f;
            r.reelCap = 150f;
            r.reelGain = 1f;
            r.strainFloor = 0.45f;
            return s;
        }

        void OnLetGoTugStarted(DragPayload p)
        {
            if (p == null || p.FileId != ContentIds.File017 || _exit != Night3Exit.None) return;
            var reel = _g.Conflict.Reel;
            if (_binMode != FinaleBinMode.LetGo || reel == null || reel.Variant != TugVariant.LetGo) return;
            GameLog.Info(LogChannel.Story, "Finale: LetGo hold (" + reel.Finish.ToString("0") + " px in " + (_letGoRetry ? Night3Rules.LetGoRetryHoldSeconds : Night3Rules.LetGoHoldSeconds) + " s)");
            RunSide(LetGoLines(reel), "letgo-lines");
        }

        /// <summary>Her hold lines, each typed when the hold reaches its share of the way (a later hold goes on from the next line).</summary>
        IEnumerator LetGoLines(TugReel reel)
        {
            var lines = Lines(Night3Rules.TugLineSet(FinaleBinMode.LetGo));
            while (_letGoLinesSaid < lines.Length && _letGoLinesSaid < Night3Rules.LetGoLineAt.Length)
            {
                float at = Night3Rules.LetGoLineAt[_letGoLinesSaid];
                while (_g.Conflict.IsFighting && _g.Conflict.Reel == reel && reel.Progress < at) yield return null;
                if (!_g.Conflict.IsFighting || _g.Conflict.Reel != reel) yield break;
                string line = lines[_letGoLinesSaid++];
                var pad = _ellen.Pad;
                if (_ellen.Typing && pad != null && pad.IsOpen)
                {
                    // She is in the middle of a longer line (the final exchange): the hold line cuts in at once.
                    pad.Interject(line, LetGoCps);
                    float until = Time.time + 4f;
                    while (pad.PendingInterjections > 0 && Time.time < until && _g.Conflict.IsFighting) yield return null;
                    // Not typed while the hold lasted: it is not typed later, out of its moment.
                    if (pad.PendingInterjections > 0)
                    {
                        pad.CancelInterjections();
                        yield break;
                    }
                }
                else yield return Say(_ellen, new[] { line }, LetGoCps);
            }
        }

        /// <summary>Her hold lines are typed quickly, to keep up with a 3 s hold.</summary>
        const float LetGoCps = 16f;

        void OnLetGoTugEnded(DragPayload p, TugOutcome outcome)
        {
            if (p == null || p.FileId != ContentIds.File017 || _exit != Night3Exit.None || CurrentBeat != "finale") return;
            if (outcome == TugOutcome.Released)
            {
                _letGoRetry = true;
                RunSide(AfterRelease(), "letgo-again");
                return;
            }
            if (outcome != TugOutcome.EntityWins || _binMode != FinaleBinMode.Fight || _g.Conflict.LastOutcomeForced) return;
            _refusedContests++;
            GameLog.Info(LogChannel.Story, "Finale: she won Fight contest " + _refusedContests + " of " + Night3Rules.FightContestsBeforeLetGo);
            if (_refusedContests < Night3Rules.FightContestsBeforeLetGo) return;
            // Her hand gives up: from the next grab on, she lets it go.
            UpdateBinMode("refused contests");
            RunSide(Say(_ellen, Lines("n3_tug_giveup"), 5f), "letgo-giveup");
        }

        /// <summary>A released hold: the file blinks where it was let go and she types AGAIN.</summary>
        IEnumerator AfterRelease()
        {
            yield return null;
            _g.Desktop.Attention(ContentIds.File017, 1.6f);
            var pad = _ellen.Pad;
            if (_ellen.Typing && pad != null && pad.IsOpen)
            {
                // In the middle of the final exchange: AGAIN cuts in now, or not at all.
                foreach (var line in Lines("n3_tug_again")) pad.Interject(line, 6f);
                float until = Time.time + 4f;
                while (pad.PendingInterjections > 0 && Time.time < until) yield return null;
                pad.CancelInterjections();
            }
            else yield return Say(_ellen, Lines("n3_tug_again"), 6f);
        }
    }
}
#endif
