// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
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

            // Ellen closes the viewer whenever it shows Custodial (and says why). She stops lurking first, so
            // the first forced open finds her hand free.
            var brain = E.Brain;
            brain.AllowIdleLurk = false;
            brain.AllowKeepAway = false;
            brain.AllowCloseCamera = true;
            brain.Enabled = true;
            E.Interrupt();
            E.State = EntityState.Observing;

            bool cleared = false, timeUp = false, teachSaid = false, personnelSaid = false, doorSaid = false;
            int advances = 0;
            var model = (CustodialRounds)null;
            UnhookRounds();
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
            var config = RoundsConfig.Night3(g.Difficulty.Mode, g.Memory.Trust, g.Flags.Has(MemoryFlags.N2WatchedToDoor), g.Flags.Has(MemoryFlags.N2Hid214));
            // The spec's 1.2-2.0 s is the whole close (7.4 budget); her hand needs about 0.4 s to get there.
            if (g.Difficulty.Mode == DifficultyMode.Normal) config.Hasten(0.4f);
            g.Rounds.Begin(config);
            model = g.Rounds.Model;

            float start = Time.time;
            while (!cleared && !timeUp && Time.time - start < RoundsCap) yield return null;
            int maxStage = model.MaxStage;
            g.Rounds.Stop();
            UnhookRounds();
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
        /// Kept Gary's lesson on the first forced open: he shows it first (a camera that does not show it), then
        /// says it: don't look at it, look where it isn't.
        /// </summary>
        IEnumerator GaryTeaches()
        {
            yield return GaryPresentFor(8f);
            yield return GaryLooksAway();
            yield return Say(_gary, Lines("g3_teach"), GaryCps);
        }

        /// <summary>
        /// The seat is cleared: the viewer cuts to CAM 03 with the figure behind the chair, the lights go for three
        /// seconds, and when they come back four minutes have passed. She kept a copy.
        /// </summary>
        IEnumerator SeatCleared()
        {
            var g = _g;
            var rig = g.CameraRig;
            GameLog.Info(LogChannel.Story, "Rounds: seat cleared");
            g.Flags.Set(MemoryFlags.N3SeatCleared);
            var cam = g.Apps.Find<CameraApp>();
            if (cam == null) cam = g.Apps.Launch(AppIds.Camera, null) as CameraApp;
            else cam.Window.Restore(null);
            cam?.Select(ContentIds.Cam03, null);
            if (rig != null)
            {
                rig.Figure = FigureStage.BehindChair;
                rig.SeatedMimicsPlayer = false;
            }
            g.Rounds.PatchPersonnelFor("BehindChair");
            yield return Wait(1.5f);
            g.Fx.SetBlack(true);
            g.Audio.Play("low_thump", 0.9f);
            g.Audio.SetAmbience(false, 0.2f);
            g.Audio.PlayLoop("drone_tension", 0.3f, 0.5f);
            yield return Wait(3f);
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
            _onForcedOpen = _onStage = null;
            _onPlayerReopen = _onSeatCleared = _onTimeUp = null;
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
            yield return Wait(1.5f);
            // The taskbar clock rolls through the missing hours instead of jumping (an instant set reads as a bug).
            yield return RollClock(Night3Rules.FinaleStart, ClockRollSeconds);
            // Whatever was open at 3:31 did not survive the pause: the desktop comes back bare.
            g.Windows.CloseAll();
            g.Audio.SetAmbience(true, 2f);
            g.Flags.Set(Flags.N3Lost);
            GameLog.Info(LogChannel.Story, "Lost time: the clock reads 6:41");
            // The notice stays until clicked, and its duration line lands on its own beat.
            string recover = g.Content.Text("lost.recover");
            int split = recover.IndexOf('\n');
            var notice = g.Notifications.Show(g.Content.Text("os.name"), split > 0 ? recover.Substring(0, split) : recover, "icon_warning", null, "sys_warning", true);
            if (split > 0)
            {
                yield return Wait(DurationLineDelay);
                if (notice.IsShowing)
                {
                    notice.SetBody(recover);
                    g.Audio.Play("ui_select", 0.5f, 0.8f);
                }
            }
            yield return Wait(2f);
            yield return EnsureEllenPresentStill();
            yield return Say(_ellen, Lines("n3_lost"), 3.5f);
            if (!GaryFinished)
            {
                yield return GaryPresentFor(6f);
                yield return Say(_gary, Lines("g3_lost"), GaryCps);
            }
            g.Flags.Set(Flags.LogoffItem);
            // Say where it is: a click on the notice opens the Nexus menu.
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("logoff.added", "Log Off CROURKE... added to the Nexus menu."), "icon_shutdown",
                a => g.Taskbar.StartMenu.OpenFromElsewhere(a), "ui_select");
            GameLog.Info(LogChannel.Story, "Log Off added to the Nexus menu");
            yield return Wait(6f);
        }

        /// <summary>M15: seconds for the 3:31 to 6:41 roll, and the pause before the notice's duration line.</summary>
        const float ClockRollSeconds = 1.2f, DurationLineDelay = 0.6f;

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
