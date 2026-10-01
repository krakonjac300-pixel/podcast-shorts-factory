// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using System.Linq;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 3, the full Custodial round (spec 5.3 N3.4) and the lost hours (N3.5). The round starts in
    /// Sublevel C, Security keeps forcing the viewer open on it, the shelf check needs CAM 04, and watching
    /// makes it move: 3:00 to 3:30, or until the seat is cleared.
    /// </summary>
    public sealed partial class Night3Director
    {
        const float RoundsCap = 420f;

        Action<int> _onForcedOpen, _onStage;
        Action _onPlayerReopen, _onSeatCleared, _onTimeUp;
        Action<string, CursorAgent> _onShelfLaunch;

        IEnumerator RoundsBeat()
        {
            var g = _g;
            _holdAt = -1;
            yield return EnsureClockAtLeast(3, 0, 5f);
            g.Clock.Frozen = false;
            g.Clock.Rate = RoundsRate;
            yield return EnsureEllenLurking();
            var rig = g.CameraRig;
            if (rig != null) rig.Cam04Online = true;
            if (g.Tasks.IsActive(ContentIds.TaskN3WaitRounds)) g.Tasks.ForceComplete(ContentIds.TaskN3WaitRounds);
            g.Mail.Deliver(ContentIds.MailN3SecurityRounds);
            g.Flags.Set(Flags.CameraUnlocked);
            yield return Wait(2f);
            foreach (var id in ShelfOrders) g.Orders.SetHidden(id, false);
            GiveTask(ContentIds.TaskN3Shelf);
            UnhookRounds();
            // Phase I: the viewer (top left) and the Work Orders (bottom right) get their own corners.
            _shelfPads = 0;
            TidyShelfCheckWindows();
            _onShelfLaunch = (appId, by) =>
            {
                if (appId == AppIds.WorkOrders) TidyShelfCheckWindows();
                // The remote sessions' Jotters open where they like; here they go to the bottom left, clear of the top of the feed.
                else if (appId == AppIds.Notepad && by != null && !by.IsPlayer) TuckAwayPad(g.Apps.Find<NotepadApp>());
            };
            g.Apps.Launched += _onShelfLaunch;

            // Ellen closes the viewer whenever it shows Custodial (and says why). She stops lurking first, so
            // the first forced open finds her hand free.
            var brain = E.Brain;
            brain.AllowIdleLurk = false;
            brain.AllowKeepAway = false;
            brain.AllowCloseCamera = true;
            brain.Enabled = true;
            E.Interrupt();
            E.State = EntityState.Observing;

            bool cleared = false, timeUp = false, teachSaid = false, personnelSaid = false, doorSaid = false, knocked = false;
            int advances = 0;
            var model = (CustodialRounds)null;
            _onForcedOpen = i =>
            {
                if (i == 0) SayLater(_ellen, "n3_rounds_start", 4.5f);
                if (GaryFinished)
                {
                    if (i == 0) SayLater(_gary, "g3c_feed", GaryCps);
                }
                else
                {
                    if (i == 0)
                    {
                        teachSaid = true;
                        RunSide(GaryTeaches(), "gary-teach");
                    }
                    else if (i % 2 == 0) RunSide(GaryLooksAway(), "gary-look-away");
                }
            };
            _onStage = stage =>
            {
                advances++;
                string name = model != null ? model.FigureStage : "";
                if (advances == 1) SayLater(_ellen, "n3_rounds_advanced", 4.5f);
                else if (advances == 2 && !teachSaid) SayLater(_ellen, "n3_rounds_teach", 4.5f);
                if (name == "HallFar" && !GaryFinished && !personnelSaid)
                {
                    personnelSaid = true;
                    RunSide(Say(_gary, Lines("g3_personnel"), GaryCps), "gary-personnel");
                }
                if (name == "Doorway" && !doorSaid)
                {
                    doorSaid = true;
                    model.Config.Hasten(0.4f);
                    SayLater(_ellen, "n3_rounds_door", 5f);
                }
                // Phase M (N3-4, N3-bed): at your door it knocks; the drone tightens as it comes (and bends down once it is in the room).
                if (name == "Corridor" && !knocked)
                {
                    knocked = true;
                    Scare("knock_door", 0.8f, -0.1f, 1.5f, 10f, ScareGate.EventNear | ScareGate.BeatStart);
                }
                float drone = RoundsDrone(name);
                if (drone > 0f)
                {
                    g.Audio.PlayLoop("drone_tension", drone, 2f);
                    g.Audio.SetLoopPitch("drone_tension", name == "Middle" ? 0.9439f : 1f);
                }
            };
            _onPlayerReopen = () => { };
            _onSeatCleared = () => cleared = true;
            _onTimeUp = () => timeUp = true;
            g.Rounds.ForcedOpen += _onForcedOpen;
            g.Rounds.StageAdvanced += _onStage;
            g.Rounds.PlayerReopened += _onPlayerReopen;
            g.Rounds.SeatCleared += _onSeatCleared;
            g.Rounds.TimeUp += _onTimeUp;
            g.Rounds.ForcedBy = null;
            g.Rounds.ForcedOpenHandler = GaryFinished ? GaryForcedOpen : (Action<string>)null;
            // Phase K: while the shelf check is open Security opens CAM 04 and she leaves it open (the blind tester had to hit CAM 04
            // within 0.2 s of every forced open for four minutes); once it is filed the round is as before.
            g.Rounds.PreferredCamera = () => _g.Tasks.IsActive(ContentIds.TaskN3Shelf) ? ContentIds.Cam04 : null;
            var config = RoundsConfig.Night3(g.Difficulty.Mode, g.Memory.Trust, g.Flags.Has(MemoryFlags.N2WatchedToDoor), g.Flags.Has(MemoryFlags.N2Hid214));
            // The spec's 1.2-2.0 s is the whole close (7.4 budget); her hand needs about 0.4 s to get there.
            if (g.Difficulty.Mode == DifficultyMode.Normal) config.Hasten(0.4f);
            g.Rounds.Begin(config);
            model = g.Rounds.Model;
            // Phase J: once the shelf check is filed the queue says there is nothing to do until the round ends.
            RunSide(RoundsWaitLine(), "rounds-wait-line");
            RunSide(ScrapeBelow(), "scrape-below");

            float start = Time.time;
            while (!cleared && !timeUp && Time.time - start < RoundsCap) yield return null;
            int maxStage = model.MaxStage;
            g.Rounds.Stop();
            UnhookRounds();
            if (!cleared) g.Audio.StopLoop("drone_tension", 2f);
            if (g.Tasks.IsActive(ContentIds.TaskN3RoundsUntil)) g.Tasks.ForceComplete(ContentIds.TaskN3RoundsUntil);
            g.Rounds.ForcedOpenHandler = null;
            brain.AllowCloseCamera = false;
            brain.Enabled = false;
            E.Interrupt();
            E.State = EntityState.Observing;
            Gary.Interrupt();
            g.Flags.SetCounter(MemoryFlags.N3MaxStage, maxStage);

            if (cleared) yield return SeatCleared();
            else
            {
                GameLog.Info(LogChannel.Story, "Rounds: safe at stage " + model.Stage + " (max " + maxStage + ")");
                g.Notifications.Show(g.Content.Text("app.camera"), g.Content.Text("rounds.end"), "icon_camera", null, "ui_select");
                if (rig != null) rig.Figure = FigureStage.None;
                g.Rounds.PatchPersonnelFor("SublevelC");
                if (maxStage <= 1) g.Flags.Set(Flags.N3RoundsSafe);
                g.AchievementWatch?.OnRoundsSafe(Night, maxStage);
                yield return Say(_ellen, Lines("n3_rounds_safe"), 4f);
            }

            // The shelf check is over either way: what was not decided is taken back (the task first, so the
            // cancelled orders do not complete it).
            bool cancelled = false;
            if (!Done(ContentIds.TaskN3Shelf))
            {
                g.Tasks.Withdraw(ContentIds.TaskN3Shelf, g.Content.Text("workqueue.withdrawn.cancelled"));
                cancelled = true;
            }
            foreach (var id in ShelfOrders)
            {
                if (g.Orders.DecisionFor(id) != null) continue;
                g.Orders.Cancel(id);
                cancelled = true;
            }
            if (cancelled) g.Notifications.Show(g.Content.Text("app.workorders"), g.Content.Text("rounds.shelf.cancelled"), "icon_info", null, "sys_warning");
            g.Flags.Clear(Flags.CameraUnlocked);
            g.Flags.Set(Flags.N3RoundsDone);
            yield return Wait(1.5f);
        }

        /// <summary>
        /// Phase I (finding 6): the shelf check needs the top of the CAM 04 feed (its label and the NEXT line) and the Work Orders
        /// side by side. The Work Orders go to the bottom right corner (its Approve and Reject are at its top right, clear of the
        /// viewer, which opens at the top left); windows are never closed, only moved.
        /// </summary>
        int _shelfPads;

        /// <summary>A remote session's Jotter goes to the bottom left corner (each further one a little up and right).</summary>
        void TuckAwayPad(NotepadApp pad)
        {
            if (pad == null || pad.Window == null || pad.Window.IsMaximized) return;
            var size = pad.Window.Size;
            float step = 18f * (_shelfPads++ % 3);
            pad.Window.MoveTo(new Vector2(92f + step, ScreenRig.Height - OS.WindowManager.TaskbarHeight - size.y - 2f - step));
        }

        void TidyShelfCheckWindows()
        {
            foreach (var app in _g.Apps.OpenApps.ToArray())
                if (app is NotepadApp pad && pad.IsOpen && pad.ConversationMode && !pad.Window.IsMinimized) TuckAwayPad(pad);
            var wo = _g.Apps.FindById(AppIds.WorkOrders);
            if (wo == null || wo.Window == null || wo.Window.IsMinimized || wo.Window.IsMaximized) return;
            var size = wo.Window.Size;
            wo.Window.MoveTo(new Vector2(ScreenRig.Width - size.x - 4f, ScreenRig.Height - OS.WindowManager.TaskbarHeight - size.y - 2f));
            GameLog.Info(LogChannel.Story, "Shelf check: Work Orders moved to the bottom right");
        }

        /// <summary>
        /// Kept Gary's lesson on the first forced open: he shows it first (a camera that does not show it), then
        /// says it: don't look at it, look where it isn't.
        /// </summary>
        IEnumerator GaryTeaches()
        {
            yield return GaryPresentFor(8f);
            yield return GaryLooksAway();
            yield return Say(_gary, Lines("g3_teach"), GaryCps);
        }

        /// <summary>Phase M: the round's drone by the figure's stage (0 = leave it as it is).</summary>
        static float RoundsDrone(string stage) => stage == "Corridor" ? 0.12f : stage == "Doorway" ? 0.2f : stage == "Middle" ? 0.28f : 0f;

        /// <summary>Phase M (N3-3, M10): the first time CAM 04 has been on screen for 6 s, something heavy moves on the shelves far below.</summary>
        IEnumerator ScrapeBelow()
        {
            yield return WaitWatching(6f, RoundsCap, ContentIds.Cam04);
            if (_g.Rounds.Running) Scare("metal_scrape", 0.7f, 0f, 0f, 20f);
        }

        /// <summary>
        /// The seat is cleared: the viewer cuts to CAM 03 with the figure behind the chair, the lights go for three
        /// seconds, and when they come back four minutes have passed. She kept a copy. Phase M (5.5): the room and the drone
        /// drop out, a breath, and the hit as the lights go; the ring in the dark.
        /// </summary>
        IEnumerator SeatCleared()
        {
            var g = _g;
            var rig = g.CameraRig;
            GameLog.Info(LogChannel.Story, "Rounds: seat cleared");
            BeginClimax();
            g.Flags.Set(MemoryFlags.N3SeatCleared);
            var cam = g.Apps.Find<CameraApp>();
            if (cam == null) cam = g.Apps.Launch(AppIds.Camera, null) as CameraApp;
            else cam.Window.Restore(null);
            if (cam != null) g.Windows.Front(cam.Window);
            cam?.Select(ContentIds.Cam03, null);
            if (rig != null)
            {
                rig.Figure = FigureStage.BehindChair;
                rig.SeatedMimicsPlayer = false;
            }
            g.Rounds.PatchPersonnelFor("BehindChair");
            DropRoom(0.3f);
            yield return Wait(0.2f);
            g.Audio.Play("breath_near", 0.8f);
            yield return Wait(1.3f - StingerPreRoll);
            if (rig != null) rig.Quiet = true;
            yield return Hit(0.7f, 0f, 3f, 0f, "ear_ring", "drone_tension");
            g.Fx.SetBlack(true);
            yield return Wait(0.5f);
            g.Audio.Play("ear_ring", 0.6f);
            yield return Wait(0.5f);
            g.Audio.PlayLoop("drone_tension", 0.2f, 0.5f);
            yield return Wait(2f);
            g.Audio.StopLoop("drone_tension", 1.5f);
            g.Clock.Set(g.Clock.Hour24, g.Clock.Minute + 4);
            if (rig != null)
            {
                rig.Figure = FigureStage.None;
                rig.SeatedMimicsPlayer = true;
            }
            cam = g.Apps.Find<CameraApp>();
            if (cam != null) cam.Window.Close(null);
            g.Fx.SetBlack(false);
            g.Audio.SetAmbience(true, 2f);
            EndClimax();
            yield return Wait(1.2f);
            yield return Say(_ellen, Lines("n3_rounds_cleared"), 3.5f);
        }

        void UnhookRounds()
        {
            var r = _g.Rounds;
            if (r == null) return;
            if (_onForcedOpen != null) r.ForcedOpen -= _onForcedOpen;
            if (_onStage != null) r.StageAdvanced -= _onStage;
            if (_onPlayerReopen != null) r.PlayerReopened -= _onPlayerReopen;
            if (_onSeatCleared != null) r.SeatCleared -= _onSeatCleared;
            if (_onTimeUp != null) r.TimeUp -= _onTimeUp;
            if (_onShelfLaunch != null) _g.Apps.Launched -= _onShelfLaunch;
            r.PreferredCamera = null;
            _onForcedOpen = _onStage = null;
            _onPlayerReopen = _onSeatCleared = _onTimeUp = null;
            _onShelfLaunch = null;
        }

        /// <summary>Phase J: the shelf check filed while the round goes on: a queue line says there is nothing to do but wait.</summary>
        IEnumerator RoundsWaitLine()
        {
            yield return WaitUntil(() => Done(ContentIds.TaskN3Shelf) || !_g.Rounds.Running, RoundsCap);
            if (_g.Rounds.Running && Done(ContentIds.TaskN3Shelf)) GiveTask(ContentIds.TaskN3RoundsUntil);
        }

        // ------------------------------------------------------------------ LOST

        /// <summary>A mail from yourself saying "remain seated", a pause, and it is 6:41. Three hours are gone.</summary>
        IEnumerator Lost()
        {
            var g = _g;
            _holdAt = -1;
            g.Clock.Frozen = true;
            yield return Wait(4f);
            g.Mail.Deliver(ContentIds.MailN3NoSubject);
            yield return Wait(2f);
            g.Fx.Glitch(0.5f, 1f);
            g.Audio.Play("glitch_burst", 0.7f);
            g.Audio.SetAmbience(false, 0.2f);
            // Phase M (N3-5, M15): something happened that you do not remember; your ears are still ringing.
            g.Audio.Play("ear_ring", 0.6f);
            yield return Wait(1.5f);
            // The taskbar clock rolls through the missing hours instead of jumping (an instant set reads as a bug).
            yield return RollClock(Night3Rules.FinaleStart, ClockRollSeconds);
            // Whatever was open at 3:31 did not survive the pause: the desktop comes back bare.
            g.Windows.CloseAll();
            g.Audio.SetAmbience(true, 2f);
            g.Flags.Set(Flags.N3Lost);
            GameLog.Info(LogChannel.Story, "Lost time: the clock reads 6:41");
            // The notice stays for a while (Phase J: no longer until clicked, it covered the desktop for the whole finale), and
            // its duration line lands on its own beat.
            string recover = g.Content.Text("lost.recover");
            int split = recover.IndexOf('\n');
            float until = Time.time + RecoverNoticeSeconds;
            var notice = g.Notifications.Show(g.Content.Text("os.name"), split > 0 ? recover.Substring(0, split) : recover, "icon_warning", null, "sys_warning",
                true, () => Time.time < until);
            if (split > 0)
            {
                yield return Wait(DurationLineDelay);
                if (notice.IsShowing)
                {
                    notice.SetBody(recover);
                    g.Audio.Play("ui_select", 0.5f, 0.8f);
                }
            }
            // Phase Q3 (D3): the page the pause saved (it seeds what the copy types when the seat is taken).
            RunSide(RecoveredPage(), "recovered-page");
            yield return Wait(2f);
            yield return EnsureEllenPresentStill();
            yield return Say(_ellen, Lines("n3_lost"), 3.5f);
            if (!GaryFinished)
            {
                yield return GaryPresentFor(6f);
                yield return Say(_gary, Lines("g3_lost"), GaryCps);
            }
            // Log Off CROURKE... is in the Nexus menu from here; Phase N: the finale's first notice says so with the whole end-of-shift rule.
            g.Flags.Set(Flags.LogoffItem);
            GameLog.Info(LogChannel.Story, "Log Off added to the Nexus menu");
            yield return Wait(1f);
        }

        /// <summary>Phase Q3 (D3): seconds after the recovery notice before the recovered page appears, and how long its notice stays.</summary>
        const float RecoveredAfter = 3f, RecoveredNoticeSeconds = 12f;

        /// <summary>
        /// Phase Q3 (D3): three seconds after the recovery notice, an unsaved Jotter page of Casey's from 5:58 AM is on the desktop, and its
        /// notice quotes the first line, so everyone reads it (SHRED's copy types the rest, and "MY NAME IS C"). Opened, she says she typed it too.
        /// </summary>
        IEnumerator RecoveredPage()
        {
            var g = _g;
            yield return Wait(RecoveredAfter);
            if (!g.Files.Exists(ContentIds.FileRecovered)) yield break;
            g.Files.SetHidden(ContentIds.FileRecovered, false);
            float until = Time.time + RecoveredNoticeSeconds;
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("lost.recovered"), "icon_info", a => g.Apps.OpenFile(ContentIds.FileRecovered, a), "ui_select",
                true, () => Time.time < until);
            GameLog.Info(LogChannel.Story, "Recovered page: recovered_0558.txt is on the desktop");
            yield return WaitUntil(() => g.Memory.Count(MemoryKind.OpenedFile, ContentIds.FileRecovered) > 0, 900f);
            if (CurrentBeat == "lost" || CurrentBeat == "finale") SayLater(_ellen, "n3_recovered", 4f, afterTurn: true);
        }

        /// <summary>M15: seconds for the 3:31 to 6:41 roll, the pause before the notice's duration line, and how long the notice stays.</summary>
        const float ClockRollSeconds = 1.2f, DurationLineDelay = 0.6f, RecoverNoticeSeconds = 20f;

        /// <summary>Runs the frozen clock forward to <paramref name="target"/> minutes over <paramref name="seconds"/>, with soft ticks.</summary>
        IEnumerator RollClock(int target, float seconds)
        {
            var clock = _g.Clock;
            int from = clock.TotalMinutes;
            if (target <= from)
            {
                clock.Set(target / 60, target % 60);
                yield break;
            }
            float t = 0f, nextTick = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                k = k * k * (3f - 2f * k);
                int m = from + Mathf.RoundToInt((target - from) * k);
                clock.Set(m / 60, m % 60);
                if (t >= nextTick)
                {
                    _g.Audio.Play("key_tap", 0.22f, 1.5f);
                    nextTick = t + 0.07f;
                }
                yield return null;
            }
            clock.Set(target / 60, target % 60);
        }

        /// <summary>Ellen on screen and still (brain off), e.g. between the round and the finale.</summary>
        IEnumerator EnsureEllenPresentStill()
        {
            E.Brain.Enabled = false;
            E.State = EntityState.Observing;
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.55f), 0.6f, false);
            _ellen.Direct = true;
        }
    }
}
#endif
