using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q2 (board C V5, A T2): measures what Capture Profile 214 and the Retention Record show, from the player's own input only:
    /// the night's first archive drag (its length and longest stillness), how often the briefing was opened, the time from a shred
    /// confirm to the player's Yes, CAM 03 on screen (how often, how long), the lines typed and the words sent to session 017.
    /// Nothing is guessed: what was not measured stays unmeasured.
    /// </summary>
    public sealed class CaptureMeter
    {
        /// <summary>A drop counts as the archive drag if the file lands in Archive within this many seconds (a move may ask first).</summary>
        const float LandWithin = 15f;

        readonly GameServices _g;
        readonly string _briefing;
        readonly CaptureStats _s = new CaptureStats();
        readonly DragMeter _drag = new DragMeter();
        DragPayload _carried;
        bool _dragDone;
        string _pendingFile;
        float _pendingSeconds, _pendingPause, _pendingAt;
        bool _camOn;
        readonly HashSet<MessageBox> _confirms = new HashSet<MessageBox>();

        public CaptureMeter(GameServices g, string briefingMailId)
        {
            _g = g;
            _briefing = briefingMailId;
            g.Shred.ConfirmShown += OnConfirm;
            g.Mail.Opened += OnMailOpened;
            g.DragDrop.PayloadFinished += OnPayloadFinished;
            g.Windows.Restored += (w, by) => { if (w != null && w.Owner is CameraApp camera) camera.Touch(by); };
        }

        /// <summary>Every frame: the drag being carried and the CAM 03 feed.</summary>
        public void Tick()
        {
            var g = _g;
            if (g.Player == null) return;
            var p = g.Player.Payload;
            if (!_dragDone && p != null && p != _carried && p.Kind == PayloadKind.File)
            {
                _carried = p;
                _drag.Begin(Time.time, g.Player.Position.x, g.Player.Position.y);
            }
            else if (_carried != null && p == _carried)
            {
                _drag.Sample(Time.time, g.Player.Position.x, g.Player.Position.y);
            }
            else if (_carried != null && p != _carried)
            {
                // Taken away without a finish (a tug lost, a jump): not a drag of the player's own.
                _drag.Cancel();
                _carried = null;
            }
            if (_pendingFile != null)
            {
                if (g.Files.FolderOf(_pendingFile) == ContentIds.FolderArchive) CommitDrag();
                else if (Time.time - _pendingAt > LandWithin) _pendingFile = null;
            }
            var cam = g.Apps?.Find<CameraApp>();
            bool on = cam != null && cam.IsOpen && !cam.Window.IsMinimized && cam.CurrentCamera == ContentIds.Cam03;
            if (on && !_camOn)
            {
                _s.camLooks++;
                // Phase R: a look the player brought about (opened the viewer, restored it, switched to CAM 03) is their own; one a session opened is not.
                if (cam.LastTouchedBy != null && cam.LastTouchedBy.IsPlayer) _s.camOpened++;
            }
            if (on) _s.camSeconds += Time.deltaTime;
            _camOn = on;
        }

        void OnPayloadFinished(DragPayload p, bool accepted, CursorAgent by)
        {
            if (p != _carried) return;
            _carried = null;
            var (seconds, pause) = _drag.End(Time.time);
            if (!accepted || by == null || !by.IsPlayer || _dragDone || seconds < 0f) return;
            _pendingFile = p.FileId;
            _pendingSeconds = seconds;
            _pendingPause = pause;
            _pendingAt = Time.time;
            if (_g.Files.FolderOf(p.FileId) == ContentIds.FolderArchive) CommitDrag();
        }

        void CommitDrag()
        {
            _dragDone = true;
            _s.dragSeconds = _pendingSeconds;
            _s.longestPause = _pendingPause;
            _s.dragFile = _g.Files.GetFile(_pendingFile)?.Name ?? "";
            GameLog.Info(LogChannel.Story, "Capture: first archive drag " + CaptureProfile.Seconds(_pendingSeconds) + " s (pause " + CaptureProfile.Seconds(_pendingPause) + " s)");
            _pendingFile = null;
        }

        void OnMailOpened(string id, CursorAgent by)
        {
            if (id == _briefing && by != null && by.IsPlayer) _s.mailReads++;
        }

        void OnConfirm(string fileId, MessageBox box)
        {
            if (box == null || !_confirms.Add(box)) return;
            float shownAt = Time.time;
            box.Answered += (label, by) =>
            {
                if (label != "Yes" || by == null || !by.IsPlayer) return;
                _s.yesSeconds += Time.time - shownAt;
                _s.yesCount++;
            };
        }

        /// <summary>A line the player sent to a remote session (<paramref name="toSession017"/>: to her).</summary>
        public void OnTyped(string line, bool toSession017)
        {
            string clean = SaveData.SanitizePlayerLine(line);
            if (clean.Length == 0) return;
            _s.lines++;
            if (toSession017) _s.words017 += CaptureProfile.Words(clean);
            if (_s.firstLine.Length == 0) _s.firstLine = clean;
        }

        /// <summary>What the night measured, with its tugs and length; not a record unless the whole night was measured.</summary>
        public CaptureStats Result(NarrativeFlags flags, float seconds, bool wholeNight)
        {
            var r = _s.Copy();
            // Only a night measured from its first beat is a record (Continue and jumps see part of it).
            r.recorded = wholeNight;
            r.tugWins = flags != null ? flags.Get(Flags.CounterPlayerWins) : 0;
            r.tugLosses = flags != null ? flags.Get(Flags.CounterTugLosses) : 0;
            r.seconds = seconds;
            return r;
        }
    }
}
