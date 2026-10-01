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

            // 1) The door: it's ajar after a moment of static.
            yield return WaitWatching(8f, 25f);
            yield return StaticCut(() => rig.DoorOpen = 0.3f);

            // 2) Someone is standing in the doorway. The office goes quiet.
            yield return WaitWatching(8f, 30f);
            yield return StaticCut(() => { rig.DoorOpen = 0.85f; rig.Figure = FigureStage.Doorway; });
            _g.Flags.Set(Flags.FigureSeen);
            _g.Audio.SetAmbience(false, 4f);
            _g.Audio.PlayLoop("drone_tension", DroneVolume, 6f);
            _g.Audio.Play("door_distant", 0.5f, 1f, -0.3f);
            // Phase Q1 (owner 5): the figure in the doorway is part of the first reveal: 6 s of looking before she fights for the feed (was 4).
            yield return WaitWatching(FirstViewSeconds, 14f);

            // 3) The entity panics and fights to shut the feed. Each time you reopen it, it is closer.
            E.State = EntityState.Panicked;
            var panic = _g.Content.Dialogue.panicLines;
            int panicLine = 0;
            float lastOpen = Time.time;
            int seenReopens = _cameraReopens;
            bool ducked = false;
            while (Time.time - revealStart < 150f)
            {
                // M6: while the feed is shut (its timestamp frozen) the drone sinks 6 dB and a semitone; it snaps back on.
                var shown = _g.Apps.Find<CameraApp>();
                bool feedShut = shown == null || !shown.IsOpen || shown.Window.IsMinimized;
                if (feedShut != ducked)
                {
                    ducked = feedShut;
                    _g.Audio.SetLoopVolume("drone_tension", ducked ? DroneVolume * 0.5f : DroneVolume, ducked ? 0.25f : 0f);
                    _g.Audio.SetLoopPitch("drone_tension", ducked ? OneSemitoneDown : 1f);
                }
                // Reopened (or restored) since we last looked - even mid-typing: it advances.
                if (_cameraReopens > seenReopens)
                {
                    seenReopens = _cameraReopens;
                    var reopened = _g.Apps.Find<CameraApp>();
                    if (reopened != null) reopened.Select(ContentIds.Cam03, null);
                    // M6: the new position resolves out of half a second of static instead of a hard cut (the rig's step: closer every time).
                    var next = rig.Figure == FigureStage.Doorway ? FigureStage.Middle : FigureStage.BehindChair;
                    yield return StaticResolve(() => rig.Figure = next, 0.5f);
                    yield return WaitWatching(3f, 6f);
                    continue;
                }
                cam = _g.Apps.Find<CameraApp>();
                if (cam != null && cam.IsOpen && !cam.Window.IsMinimized)
                {
                    lastOpen = Time.time;
                    if (rig.Figure == FigureStage.BehindChair && cam.CurrentCamera == ContentIds.Cam03)
                    {
                        yield return WaitWatching(5f, 8f);
                        break;
                    }
                    var close = cam.Window.CloseButton;
                    var closed = new bool[1];
                    if (close != null) yield return E.ClickElement(close.Hit, MovementProfiles.Panicked, closed, 4f);
                    // You blocking the close box is a fight it keeps losing (it tries again next time round);
                    // anything else in the way (another window) it simply pushes past.
                    if (!closed[0] && cam.IsOpen && !cam.Window.IsMinimized && close != null && !E.IsBlockedByPlayer(close.Hit))
                    {
                        cam.Window.Focus(E.Agent);
                        yield return Wait(0.2f);
                        if (close != null && cam.IsOpen) yield return E.ClickElement(close.Hit, MovementProfiles.Panicked, closed, 2f);
                        if (!closed[0] && cam.IsOpen)
                        {
                            cam.Window.Close(E.Agent);
                            _g.Fx.Glitch(0.15f, 0.7f);
                            GameLog.Info(LogChannel.Entity, "Entity forced the camera feed shut");
                        }
                    }
                    if (panicLine < panic.Length) yield return TypeLines(_ellen, new[] { panic[panicLine++] }, 6f);
                }
                else
                {
                    // Closed: wait to see whether you look again (handled at the top of the loop).
                    yield return WaitUntil(() => _cameraReopens > seenReopens || Time.time - lastOpen > 12f, 13f);
                    if (_cameraReopens <= seenReopens)
                    {
                        // You obeyed. It shows you why instead - and then the feed opens by itself.
                        yield return ShowEmployee017();
                        rig.Figure = FigureStage.BehindChair;
                        rig.DoorOpen = 1f;
                        var self = (CameraApp)_g.Apps.Launch(AppIds.Camera, null);
                        self?.Select(ContentIds.Cam03, null);
                        if (panicLine < panic.Length) yield return TypeLines(_ellen, new[] { panic[panicLine++] }, 7f);
                        yield return WaitWatching(4f, 8f);
                        break;
                    }
                }
                yield return null;
            }

            if (ducked)
            {
                _g.Audio.SetLoopVolume("drone_tension", DroneVolume, 0f);
                _g.Audio.SetLoopPitch("drone_tension", 1f);
            }

            // Final image: it's right behind you, and "you" turn to look at the camera (Phase M: the demo's last scare, 5.3).
            cam = _g.Apps.Find<CameraApp>();
            if (cam == null || !cam.IsOpen) cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, null);
            else cam.Window.Restore(null);
            if (cam != null)
            {
                // Review M5: the climax plays on CAM 03 in front, whatever the viewer was doing.
                _g.Windows.Front(cam.Window);
                if (cam.CurrentCamera != ContentIds.Cam03) cam.Select(ContentIds.Cam03, null);
            }
            yield return RevealClimax(DroneVolume);
            _afterHit = true;
        }

        const float DroneVolume = 0.25f;

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
