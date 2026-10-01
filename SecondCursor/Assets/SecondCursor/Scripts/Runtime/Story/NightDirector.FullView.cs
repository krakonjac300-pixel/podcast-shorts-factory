using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Entity;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q3 (board C V4): the full-size signature scares. On four beats the Camera Viewer fills the desktop for a few seconds with her
    /// line as a big caption in the feed's middle (a frame that works as a thumbnail and survives a 9:16 crop). It is her action when her
    /// pointer is free (she double-clicks the viewer's title bar) and a plain cut otherwise (Security opens the viewer with no pointer too).
    /// It never takes over what the player needs: it does not start with a dialog, a tug, a shred, a held file or a reply being typed, and it
    /// ends at once on any click or key of the player's, a dialog or a fight, and when the beat is over.
    /// </summary>
    public abstract partial class NightDirector
    {
        CameraApp _fullCam;

        /// <summary>A dialog (an always-on-top window: a confirm, the Log Off progress, a message) is open.</summary>
        bool AnyDialogOpen()
        {
            foreach (var w in _g.Windows.Windows)
                if (w != null && !w.IsClosed && !w.IsMinimized && w.AlwaysOnTop) return true;
            return false;
        }

        /// <summary>A Jotter the player typed in during the last <paramref name="seconds"/> (a reply is being written: it stays in view).</summary>
        bool PlayerTypedRecently(float seconds)
        {
            foreach (var s in _speakers)
                if (s.Pad != null && s.Pad.IsOpen && s.Pad.LastPlayerKeyTime >= 0f && Time.time - s.Pad.LastPlayerKeyTime < seconds) return true;
            return false;
        }

        /// <summary>The viewer shows the player's office and nothing the player needs would be covered by a full view.</summary>
        bool FullViewAllowed(CameraApp cam) =>
            cam != null && cam.IsShowing(ContentIds.Cam03) && !cam.Window.IsMaximized && !AnyDialogOpen()
            && !_g.Conflict.IsFighting && !_g.Shred.Busy && _g.Player.Payload == null && !PlayerTypedRecently(FullViewTypingGrace);

        const float FullViewTypingGrace = 4f;

        /// <summary>
        /// The viewer fills the desktop with <paramref name="caption"/> (null or empty: no caption). <paramref name="by"/> double-clicks its title
        /// bar when she is free (visible, not blocked); with no pointer it is a cut. Returns once it is big, or at once when a full view is not
        /// allowed now. It stays for at most <paramref name="maxSeconds"/> and ends earlier on the player's click or key, a dialog, a tug or a
        /// shred; the beat calls <see cref="EndFullView"/> when its moment is over.
        /// </summary>
        protected IEnumerator FullSizeFeed(string caption, EntityController by, float maxSeconds)
        {
            var cam = _g.Apps.Find<CameraApp>();
            if (!FullViewAllowed(cam)) yield break;
            EndFullView();
            cam.BeginFullView(caption);
            if (by != null && by.IsVisible && !by.Busy)
            {
                var clicked = new bool[1];
                yield return by.ClickElement(cam.Window.TitleHit, MovementProfiles.Hesitant, clicked, 3f, true);
                // The player's pointer was in the way, or the viewer went: no full view.
                if (!clicked[0] || !cam.IsOpen || cam.Window.IsMinimized) { if (cam.IsOpen) cam.EndFullView(); yield break; }
                if (!cam.Window.IsMaximized && FullViewAllowed(cam)) cam.Window.ToggleMaximize(by.Agent);
            }
            else if (!cam.Window.IsMaximized) cam.Window.ToggleMaximize(null);
            if (!cam.IsFullView) { cam.EndFullView(); yield break; }
            _fullCam = cam;
            _g.Windows.Front(cam.Window);
            RunSide(HoldFullView(cam, maxSeconds), "full-view");
            GameLog.Info(LogChannel.Story, "Full-size feed" + (string.IsNullOrEmpty(caption) ? "" : ": " + caption));
        }

        /// <summary>Like <see cref="FullSizeFeed"/> for a beat that cannot wait on its start (the finale's 6:55 feed): tries each frame until it may.</summary>
        protected IEnumerator FullSizeFeedWhenAllowed(string caption, float maxSeconds, float tryFor)
        {
            float until = Time.time + tryFor;
            while (Time.time < until)
            {
                var cam = _g.Apps.Find<CameraApp>();
                if (FullViewAllowed(cam))
                {
                    yield return FullSizeFeed(caption, null, maxSeconds);
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>Keeps the full view on top (a dialog stays above it) and ends it when the player acts or something needs the desktop.</summary>
        IEnumerator HoldFullView(CameraApp cam, float maxSeconds)
        {
            float end = Time.time + maxSeconds, playerChoice = _g.Windows.PlayerChoiceAt;
            while (Time.time < end && cam != null && cam.IsOpen && cam.IsFullView && cam.IsShowing(ContentIds.Cam03))
            {
                var input = _g.Input;
                bool playerActed = input != null && _g.Player.Enabled && (input.LeftDown || input.RightDown || !string.IsNullOrEmpty(input.TypedText));
                if (playerActed || _g.Conflict.IsFighting || _g.Shred.Busy || AnyDialogOpen()) break;
                var w = cam.Window;
                if (_g.Windows.Active != w && _g.Windows.PlayerChoiceAt <= playerChoice) _g.Windows.Front(w);
                yield return null;
            }
            EndFullView();
        }

        /// <summary>The full view is over: the viewer is restored to its own size (nothing if it is not full or already gone).</summary>
        protected void EndFullView()
        {
            var cam = _fullCam;
            _fullCam = null;
            if (cam == null || !cam.IsOpen) return;
            bool wasFull = cam.IsFullView;
            cam.EndFullView();
            if (wasFull) cam.Window.ToggleMaximize(null);
        }
    }
}
