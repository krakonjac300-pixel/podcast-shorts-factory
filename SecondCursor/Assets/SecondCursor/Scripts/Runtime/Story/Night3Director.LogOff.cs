// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 3's way out by the book (split from Night3Director.Finale.cs): the end-of-shift rule at 6:41, Log Off CROURKE... with its
    /// confirm and progress, and what the player last tried (the KEEP card names it).
    /// </summary>
    public sealed partial class Night3Director
    {
        // ------------------------------------------------------------------ the end-of-shift rule (Phase N)

        /// <summary>
        /// What the player last tried to leave with, for the KEEP card when 7:05 comes ("letgo": a shred of employee_017.dat or a fight over
        /// it; "logoffdenied": Log Off refused by session.cfg; "logoffearly": Log Off before 7:00; "logofftried": a log off that did not go
        /// through). Null = nothing.
        /// </summary>
        string _lastTry;

        /// <summary>
        /// Phase N (fifth blind playtest, finding 1): right after the lost hours the Work Queue and one notice give the whole rule: log off
        /// between 7:00 and 7:05, keep Custodial off the camera until then, and what doing nothing means. The tester first heard the camera
        /// part at 7:01. The task stays until an exit happens.
        /// </summary>
        void EndOfShiftRule()
        {
            var g = _g;
            // The notice below is the task's announcement (one notice, not the usual task toast as well).
            bool given = g.Tasks.IsActive(ContentIds.TaskN3LogOffBy);
            g.Tasks.Activate(ContentIds.TaskN3LogOffBy);
            if (given || g.Clock.TotalMinutes >= Night3Rules.LogOffTime) return;
            RunSide(EndOfShiftNotice(), "end-of-shift-notice");
        }

        /// <summary>The notice comes a moment after the task, once the Work Queue has grown to show the rule (it would sit on its end).</summary>
        IEnumerator EndOfShiftNotice()
        {
            yield return Wait(1f);
            var g = _g;
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("notify.endofshift"), "icon_shutdown", a => g.Apps.Launch(AppIds.WorkQueue, a),
                "sys_warning", true, () => _exit == Night3Exit.None && g.Clock.TotalMinutes < Night3Rules.LogOffTime);
            GameLog.Info(LogChannel.Story, "6:41: end-of-shift rule given");
        }

        // ------------------------------------------------------------------ log off

        /// <summary>
        /// Start, Log Off CROURKE...: too early before 7:00, refused while session.cfg says ALLOW_LOGOFF=0,
        /// otherwise the confirm (Yes, No), then 6 s of "Logging off" with Cancel.
        /// </summary>
        public override void RequestLogOff(CursorAgent a)
        {
            var g = _g;
            var c = g.Content;
            if (CurrentBeat == "ending" || _exit != Night3Exit.None) return;
            if (LogOffRunning)
            {
                var w = _logOffConfirm != null && _logOffConfirm.IsOpen ? _logOffConfirm.Window : _logOffProgress?.Window;
                if (w != null) { w.Focus(a); w.Shake(0.2f, 2f); }
                return;
            }
            string title = c.Text("logoff.item").TrimEnd('.');
            var check = Night3Rules.CheckLogOff(g.Clock.TotalMinutes, g.Flags.Has(MemoryFlags.N3LogoffEnabled));
            GameLog.Info(LogChannel.Player, "Log off requested: " + check);
            if (check == LogOffCheck.Early)
            {
                // Before 7:00 the policy is named too, while there is still time to change it.
                bool enabled = g.Flags.Has(MemoryFlags.N3LogoffEnabled);
                Dialogs.Message(g, title, c.Text(enabled ? "logoff.early" : "logoff.early.disabled"), enabled ? "icon_info" : "icon_lock", new[] { "OK" }, null);
                _lastTry = enabled ? "logoffearly" : "logoffdenied";
                return;
            }
            if (check == LogOffCheck.Disabled)
            {
                Dialogs.Message(g, title, c.Text("logoff.disabled"), "icon_lock", new[] { "OK" }, null);
                _lastTry = "logoffdenied";
                return;
            }
            _lastTry = "logofftried";
            // Phase J: with the feed up, Custodial can reach the chair before the log off finishes (the tester's KEEP): say so here.
            string body = c.Text("logoff.confirm") + (g.Rounds.ViewedCamera() != null ? "\n" + c.Text("logoff.confirm.watched") : "");
            _logOffConfirm = Dialogs.Message(g, title, body, "icon_question", new[] { "Yes", "No" }, OnLogOffAnswer, 1, null, ShredService.RaceStatusHeight);
            // Phase N: kept Gary holds No for you, finished Gary races you to it; the dialog says which.
            ConfirmRace.Attach(g, _logOffConfirm, GaryFinished ? Gary : null, () => Gary.CurrentAction == "gary-race-no", "race.idle.logoff");
            // Phase M: from the first confirm on, nothing scary: no scare, and the drone goes.
            _logOffAsked = true;
            g.Scares.CancelAll();
            g.Audio.StopLoop("drone_tension", 1f);
            var box = _logOffConfirm;
            RunSide(Say(_ellen, Lines(Night3Rules.LogOffLineSet(g.Memory.Trust)), 4.5f), "logoff-line");
            if (GaryFinished) Gary.Run(GaryRacesToNo(box), "gary-race-no");
            else Gary.Run(GaryGuard(() => box.IsOpen ? box.Button("No")?.Hit : null, 3f), "gary-guard-logoff");
        }

        void OnLogOffAnswer(string result, CursorAgent by)
        {
            GameLog.Info(LogChannel.Story, "Log off confirm: " + result + " by " + (by?.Name ?? "System"));
            var c = _g.Content;
            // Phase J: another session's No is named, like its Cancel below.
            if (result == "No" && by != null && by.IsEntity && _exit == Night3Exit.None) CancelledByNotice(by);
            if (result != "Yes" || _exit != Night3Exit.None) return;
            _logOffProgress = Dialogs.Progress(_g, c.Text("logoff.item").TrimEnd('.'), c.Text("logoff.progress"), "icon_shutdown");
            var progress = _logOffProgress;
            bool cancelled = false;
            progress.Cancelled += a =>
            {
                cancelled = true;
                GameLog.Info(LogChannel.Story, "Log off cancelled by " + (a?.Name ?? "System"));
                progress.Close(a);
                // Phase H: another session's Cancel is named, so it never looks like the log off simply failed.
                if (a != null && a.IsEntity) CancelledByNotice(a);
            };
            RunSide(LogOffProgress(progress, () => cancelled), "logoff-progress");
            if (GaryFinished) Gary.Run(GaryCancelsLogOff(progress), "gary-cancel-logoff");
        }

        /// <summary>Phase N: like a raced shred's result, it stays up for a while (or until clicked: it opens the Nexus menu).</summary>
        void CancelledByNotice(CursorAgent by)
        {
            float until = Time.time + SystemNotices.RaceNoticeSeconds;
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Format("logoff.cancelled.by", SystemNotices.SessionOf(_g, by)),
                "icon_shutdown", x => _g.Taskbar.StartMenu.OpenFromElsewhere(x), "sys_warning", true, () => Time.time < until);
            GameLog.Info(LogChannel.Story, "Notice: log off cancelled by " + SystemNotices.SessionOf(_g, by));
        }

        IEnumerator LogOffProgress(ProgressDialog progress, Func<bool> cancelled)
        {
            float t = 0f;
            _g.Audio.Play("hdd_seek", 0.7f);
            while (progress.IsOpen && !cancelled() && t < LogOffSeconds)
            {
                t += Time.deltaTime;
                progress.Progress = t / LogOffSeconds;
                yield return null;
            }
            if (cancelled() || t < LogOffSeconds || _exit != Night3Exit.None) yield break;
            progress.Close(null);
            _exit = Night3Exit.LogOff;
        }
    }
}
#endif
