using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>Night 1's reveal on Camera 03 (split from Night1Director.cs, which was near the 800 line ceiling).</summary>
    public sealed partial class Night1Director
    {
        // ------------------------------------------------------------------ PHASE 6: reveal

        IEnumerator Reveal()
        {
            _caught = false;
            _afterHit = false;
            if (_g.Flags.Has(CameraDeclined))
            {
                DeliverFirstShiftMail("mail_camera_offline");
                yield return TypeLines(_ellen, new[] { "THEN KEEP IT CLOSED", "I WILL WATCH THE DOOR", "DO NOT TURN AROUND" }, 3.5f);
                yield return Wait(3f);
                yield break;
            }
            E.Phase = EntityPhase.Reveal;
            var rig = _g.CameraRig;
            _g.Flags.Set(Flags.CameraUnlocked);
            rig.SeatedMimicsPlayer = true;
            rig.DoorOpen = 0f;
            rig.Figure = FigureStage.None;
            rig.LightFlicker = 0.1f;
            rig.LightsOn = true;
            _cameraReopens = 0;

            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null)
            {
                cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, E.Agent);
                cam?.Select(ContentIds.Cam03, E.Agent);
            }
            // A jump straight here (or a viewer the escalation never showed) still gets the protected first look.
            if (!_firstViewGuarded) RunSide(GuardFirstView(), "first-view");
            // It is always here for this (a debug jump straight to the reveal skips its arrival).
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.5f), 0.4f, false);
            // It moves aside and waits, still, while you watch yourself.
            if (cam != null)
            {
                var r = cam.Window.WorldRect;
                yield return E.MoveTo(ScreenRig.ClampToScreen(new Vector2(r.xMax + 40f, r.center.y + 30f)), MovementProfiles.Hesitant, 30f);
            }
            E.State = EntityState.Observing;
            float revealStart = Time.time;
            RunSide(MoveYourHand(), "move-hand");

            // 1) The door: it's ajar after a moment of static.
            yield return WaitWatching(8f, 25f);
            yield return StaticCut(() => rig.DoorOpen = 0.3f);

            // 2) Someone is standing in the doorway. The office goes quiet.
            yield return WaitWatching(8f, 30f);
            // Phase Q3 (V4): she double-clicks the viewer's title bar: the feed fills the desktop for the doorway (her line types in over it).
            yield return FullSizeFeed(_g.Content.Dialogue.panicLines.Length > 0 ? _g.Content.Dialogue.panicLines[0] : null, E, FullViewSeconds);
            yield return StaticCut(() => { rig.DoorOpen = 0.85f; rig.Figure = FigureStage.Doorway; });
            _g.Flags.Set(Flags.FigureSeen);
            _g.Audio.SetAmbience(false, 4f);
            _g.Audio.PlayLoop("drone_tension", DroneVolume, 6f);
            _g.Audio.Play("door_distant", 0.5f, 1f, -0.3f);
            // Phase Q1 (owner 5): the figure in the doorway is part of the first reveal: 6 s of looking before she fights for the feed (was 4).
            yield return WaitWatching(FirstViewSeconds, 14f);
            EndFullView();

            // The warning is explicit before exposure starts. Ellen leaves the decision to the player.
            E.Interrupt();
            E.Brain.Enabled = false;
            E.State = EntityState.Panicked;
            var exposure = new Core.Game.CameraExposure();
            // Put the instruction in the live notice slot before typing it, so a crowded desktop cannot hide the survival rule.
            _g.Notifications.Show("SESSION 017", _g.Content.Text("capture.warning"), "icon_warning", null,
                "sys_warning", true, () => CurrentBeat == "reveal" && !exposure.Finished,
                Core.Game.NoticeKind.Entity, "camera.capture", urgent: true);
            if (EnsurePad(_ellen) == null) yield return OpenNotepadAs(_ellen);
            if (_ellen.Pad != null && _ellen.Pad.IsOpen)
            {
                _ellen.Pad.Window.Restore(E.Agent);
                _g.Windows.Front(_ellen.Pad.Window);
            }
            float warningChoiceAt = _g.Windows.PlayerChoiceAt;
            yield return TypeLines(_ellen, _g.Content.Lines("n1_capture_warning"), 2.5f);
            cam = _g.Apps.Find<CameraApp>();
            if (cam != null && cam.IsShowing(ContentIds.Cam03) && _g.Windows.PlayerChoiceAt <= warningChoiceAt) _g.Windows.Front(cam.Window);
            GameLog.Info(LogChannel.Story, "Capture warning: close CAM 03 to survive");
            while (!exposure.Finished)
            {
                cam = _g.Apps.Find<CameraApp>();
                bool watching = cam != null && cam.IsShowing(ContentIds.Cam03) && _g.Windows.Active == cam.Window;
                exposure.Tick(watching, Time.deltaTime);
                if (watching)
                {
                    var stage = exposure.WatchedSeconds >= 6f ? FigureStage.BehindChair
                        : exposure.WatchedSeconds >= 3f ? FigureStage.Middle : FigureStage.Doorway;
                    if (rig.Figure != stage) rig.Figure = stage;
                }
                yield return null;
            }
            if (exposure.Escaped)
            {
                rig.Figure = FigureStage.None;
                _g.Audio.StopLoop("drone_tension", 0.4f);
                _g.Audio.SetAmbience(true, 1f);
                GameLog.Info(LogChannel.Story, "Capture avoided: player looked away");
                yield return TypeLines(_ellen, _g.Content.Lines("n1_capture_safe"), 2.5f);
                yield break;
            }

            _caught = true;
            BeginClimax();
            _g.Player.Enabled = false;
            // From this point the capture is committed. The player had the whole warning window to close it.
            yield return FullSizeFeed(null, null, 4f);
            DropRoom(0.15f);
            CutToSilence(0.3f);
            yield return Wait(0.3f);
            yield return CaptureRush();
            yield return TubeDies(true, 0.6f, 0.9f, 0.8f);
            _afterHit = true;
        }

        const float DroneVolume = 0.25f;

        /// <summary>
        /// Phase Q3 (T7): the first time the viewer shows your office, once its arm answers your mouse (after the feed's 1.2 s dead time) and two
        /// seconds more, she types MOVE YOUR HAND: the game asks for the wave a streamer does anyway. Not if her Jotter is closed (she does not
        /// go and open it over the feed).
        /// </summary>
        IEnumerator MoveYourHand()
        {
            float watched = 0f;
            while (watched < MoveHandAfterSeconds)
            {
                var cam = _g.Apps.Find<CameraApp>();
                if (cam != null && cam.IsShowing(ContentIds.Cam03)) watched += Time.deltaTime;
                if (CurrentBeat != "reveal") yield break;
                yield return null;
            }
            var pad = EnsurePad(_ellen);
            if (pad == null) yield break;
            yield return MoveJotterClearOfTheFeed(pad);
            if (EnsurePad(_ellen) == null) yield break;
            yield return TypeLines(_ellen, _g.Content.Lines("n1_move_hand"), 3.5f);
        }

        /// <summary>
        /// The viewer opens over the lower half of her Jotter, where her new lines are typed: she drags the Jotter out to the right of the feed
        /// (her pointer, as a person would), or it is moved there if she cannot. Nothing moves when the two do not overlap.
        /// </summary>
        IEnumerator MoveJotterClearOfTheFeed(NotepadApp pad)
        {
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null || !cam.IsOpen || !pad.Window.WorldRect.Overlaps(cam.Window.WorldRect)) yield break;
            var win = pad.Window;
            var from = new Vector2(win.WorldRect.xMin, win.WorldRect.yMax);
            var to = new Vector2(ScreenRig.Width - win.Size.x - JotterMargin, ScreenRig.Height - JotterTop);
            if (E.IsVisible)
            {
                Vector2 grab = win.CaptionCenter + new Vector2(-30f, 0f);
                yield return E.DragWindow(win, grab + (to - from), MovementProfiles.Hesitant);
            }
            if (pad.IsOpen && win.WorldRect.Overlaps(cam.Window.WorldRect)) win.MoveTo(new Vector2(to.x, JotterTop), E.Agent);
        }

        const float JotterMargin = 6f, JotterTop = 40f;

        /// <summary>The feed's arm ignores the mouse for 1.2 s after it opens; she speaks 2 s after it starts to answer.</summary>
        const float MoveHandAfterSeconds = 3.2f;

        /// <summary>Phase Q3 (V4): the longest the doorway's full-size feed stays if nothing ends it first (the look is <see cref="FirstViewSeconds"/>).</summary>
        const float FullViewSeconds = 12f;

        /// <summary>Phase Q1 (owner 5): how long the first view of your own office on CAM 03 is kept in front, uncovered and unswitched.</summary>
        const float FirstViewSeconds = 6f;
        bool _firstViewGuarded;

        /// <summary>
        /// The first time the Camera Viewer shows the player's own office (Night 1): it comes to the front and, for <see cref="FirstViewSeconds"/>,
        /// no other pointer can close, minimize or switch it, no window the story opens covers it (it is brought back to the front unless the player
        /// picked another window), and no notice appears over it (they wait, and the ones up dim). The player's own clicks are never blocked.
        /// Later interruptions keep their aggression.
        /// </summary>
        IEnumerator GuardFirstView()
        {
            var cam = _g.Apps.Find<CameraApp>();
            if (_firstViewGuarded || cam == null || !cam.IsOpen) yield break;
            _firstViewGuarded = true;
            var w = cam.Window;
            w.Restore(null);
            _g.Windows.Front(w);
            if (cam.CurrentCamera != ContentIds.Cam03) cam.Select(ContentIds.Cam03, null);
            cam.HeldOn = ContentIds.Cam03;
            w.GuardedFromOthers = true;
            _guardedNoticesOff = w.KeepNoticesOff;
            w.KeepNoticesOff = true;
            _g.Notifications.HeldByStory = true;
            _guardedView = cam;
            float start = Time.time, playerChoice = _g.Windows.PlayerChoiceAt;
            GameLog.Info(LogChannel.Story, "First CAM 03 view: protected for " + FirstViewSeconds + " s");
            while (Time.time - start < FirstViewSeconds && cam != null && cam.IsOpen)
            {
                var active = _g.Windows.Active;
                bool playerPicked = _g.Windows.PlayerChoiceAt > playerChoice;
                if (!playerPicked && !w.IsMinimized && active != w && (active == null || !active.AlwaysOnTop)) _g.Windows.Front(w);
                yield return null;
            }
            EndFirstViewGuard();
        }

        CameraApp _guardedView;
        bool _guardedNoticesOff;

        /// <summary>The protection ends (its time is up, or a jump stops the routine that kept it).</summary>
        void EndFirstViewGuard()
        {
            _g.Notifications.HeldByStory = false;
            var cam = _guardedView;
            _guardedView = null;
            if (cam == null) return;
            if (cam.IsOpen)
            {
                cam.HeldOn = null;
                cam.Window.GuardedFromOthers = false;
                cam.Window.KeepNoticesOff = _guardedNoticesOff;
            }
            GameLog.Info(LogChannel.Story, "First CAM 03 view: protection over");
        }
        const float ShredDeadAir = 2.4f, BinRattleSeconds = 0.4f;

        /// <summary>The Disposal icon shakes on the spot (the shredded file is on its way back).</summary>
        IEnumerator RattleDisposal(float seconds)
        {
            var icon = _g.Desktop.DisposalIcon;
            var rt = icon != null ? (RectTransform)icon.transform : null;
            if (rt == null) yield break;
            Vector2 home = rt.anchoredPosition;
            float t = 0f, nextTick = 0f;
            int n = 0;
            while (t < seconds && rt != null)
            {
                if (t >= nextTick)
                {
                    nextTick = t + 0.12f;
                    _g.Audio.Play("mouse_release", 0.35f, 0.6f, Audio.AudioManager.PanFor(rt.position.x));
                }
                rt.anchoredPosition = home + new Vector2((n++ % 2 == 0) ? 2f : -2f, 0f);
                t += Time.deltaTime;
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = home;
        }
        /// <summary>2^(-1/12).</summary>
        const float OneSemitoneDown = 0.9439f;

        IEnumerator ShowEmployee017()
        {
            _g.Flags.Set(Flags.Staff017Revealed);
            yield return E.OpenApp(AppIds.Staff, MovementProfiles.Panicked);
            yield return Wait(0.5f);
            var staff = _g.Apps.Find<StaffApp>();
            if (staff != null)
            {
                staff.RefreshNames();
                var row = staff.Window.Element("employee:" + ContentIds.Employee017);
                if (row != null) yield return E.ClickElement(row, MovementProfiles.Hesitant, null, 3f);
                staff.ShowById(ContentIds.Employee017, E.Agent);
            }
            yield return E.Loiter(E.Agent.Position, 10f, 4f, MovementProfiles.Hesitant);
        }
    }
}
