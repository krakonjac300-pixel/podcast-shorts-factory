using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase P (plan 2.8, E9): the tug as a horror beat in the story. The second loss in a row over the same file types MINE once in her
    /// Jotter, on Night 2 and on Night 3 before the finale (Night 1's Jotter has not opened yet; in the finale her own lines take that role).
    /// </summary>
    public abstract partial class NightDirector
    {
        string _mineFile;
        int _mineLosses;
        bool _mineSaid;
        /// <summary>MINE is typed directly; a routine stopped mid-line (a jump) never put the speaker's way of typing back, so a jump does.</summary>
        Speaker _mineTyper;
        bool _mineDirectWas;

        /// <summary>Who types MINE (null: nobody tonight, or not in this beat).</summary>
        protected virtual Speaker MineSpeaker => null;

        void ResetMine()
        {
            _mineFile = null;
            _mineLosses = 0;
            RestoreMineTyper();
        }

        void RestoreMineTyper()
        {
            if (_mineTyper != null) _mineTyper.Direct = _mineDirectWas;
            _mineTyper = null;
        }

        void OnTugEndedForMine(DragPayload p, TugOutcome outcome)
        {
            if (p == null || outcome == TugOutcome.Released) return;
            if (outcome != TugOutcome.EntityWins || _g.Conflict.LastOutcomeForced)
            {
                ResetMine();
                return;
            }
            _mineLosses = p.FileId == _mineFile ? _mineLosses + 1 : 1;
            _mineFile = p.FileId;
            var s = MineSpeaker;
            if (_mineLosses < 2 || _mineSaid || s == null) return;
            _mineSaid = true;
            GameLog.Info(LogChannel.Story, "Second tug in a row lost over " + p.FileId + ": MINE");
            RunSide(TypeMine(s, _g.Content.Lines("tug_mine")), "tug-mine");
        }

        /// <summary>Typed straight into her Jotter (her hand is busy with the file), once she is free to type.</summary>
        IEnumerator TypeMine(Speaker s, string[] lines)
        {
            float wait = Time.time + 20f;
            while (s.Typing && Time.time < wait) yield return null;
            _mineTyper = s;
            _mineDirectWas = s.Direct;
            s.Direct = true;
            try { yield return TypeLines(s, lines, 6f); }
            finally { RestoreMineTyper(); }
        }
    }
}
