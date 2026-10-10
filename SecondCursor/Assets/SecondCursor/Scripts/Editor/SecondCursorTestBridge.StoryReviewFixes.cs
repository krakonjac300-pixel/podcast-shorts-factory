using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using SecondCursor.Apps;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Story;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        // This deliberately changes test state and must never target the normal player profile.
        static IEnumerator StoryReviewFixesCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("storyreviewcheck requires savedir PATH first");
            SaveSystem.SetDifficulty(DifficultyMode.Normal);
            var previous = G;
            GameBootstrap.Restart(1, "work");
            yield return RegressionWaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "ordinary recovery test shift");
            var g = G;
            g.Tasks.Activate(ContentIds.TaskArchiveLedger);
            string fileName = g.Files.GetFile(ContentIds.FileLedger).Name;
            ExperienceAssert(g.Files.Shred(ContentIds.FileLedger, Actor.Player), "test shreds an ordinary required file");
            var restore = typeof(NightDirector).GetMethod("KeepTaskFilesAlive", BindingFlags.NonPublic | BindingFlags.Instance);
            restore.Invoke(g.Director, new object[] { ContentIds.TaskArchiveLedger });
            ExperienceAssert(g.Files.FolderOf(ContentIds.FileLedger) == ContentIds.FolderIntake, "required ordinary file returns to Intake");
            ExperienceAssert(g.Notifications.History.Entries.Any(e => e.Title == "Night Operations" && e.Body.Contains(fileName)
                && e.Body.Contains("Intake") && e.Body.Contains("backup")), "ordinary recovery names its actor, reason, file, and location");
            ExperienceAssert(!g.Tasks.IsCompleted(ContentIds.TaskArchiveLedger), "restoration does not finish an unaccepted archive task");

#if !SC_DEMO
            GameBootstrap.Restart(2, "asks");
            yield return RegressionWaitFor(() => G != null && G != g && G.Director.CurrentBeat == "asks", 30f, "prior evidence test shift");
            g = G;
            g.Flags.Increment(Flags.OpenedByPlayerPrefix + ContentIds.FileDoorLog);
            g.Flags.Increment(Flags.ViewedByPlayerPrefix + ContentIds.Employee163);
            yield return RegressionWaitFor(() => g.Tasks.IsCompleted(ContentIds.TaskE2DoorLog), 100f, "already-read door log acknowledged");
            ExperienceAssert(g.Flags.Get(Flags.OpenedByPlayerPrefix + ContentIds.FileDoorLog) > 0
                && g.Tasks.Get(ContentIds.TaskE2DoorLog).ResultNote.Contains("Already read"), "prior log discovery survives its request");
            yield return RegressionWaitFor(() => g.Tasks.IsCompleted(ContentIds.TaskE2Lookup163), 100f, "already-viewed employee acknowledged");
            ExperienceAssert(g.Flags.Get(Flags.ViewedByPlayerPrefix + ContentIds.Employee163) > 0, "prior personnel discovery survives its request");
            ExperienceAssert(!g.Tasks.IsCompleted(ContentIds.TaskE2Hide214), "prior investigation does not make the later file move automatic");

            GameBootstrap.Restart(2, "asks");
            yield return RegressionWaitFor(() => G != null && G != g && G.Director.CurrentBeat == "asks", 30f, "warning reading test shift");
            g = G;
            yield return RegressionWaitFor(() => g.RemoteTaskLife.ContainsKey(ContentIds.TaskE2DoorLog), 100f, "optional request timer started");
            g.Mail.Deliver(ContentIds.MailN2RuthWarning, false);
            var mail = g.Apps.Launch(AppIds.Mail, g.Player) as MailApp;
            mail.ShowMail(ContentIds.MailN2RuthWarning, g.Player);
            g.Windows.Front(mail.Window);
            yield return WaitSeconds(0.2f);
            float elapsed = Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x;
            yield return WaitSeconds(2f);
            float readingElapsed = Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x;
            ExperienceAssert(Mathf.Abs(readingElapsed - elapsed) < 0.35f, "first focused Ruth warning reading preserves request time");
            ExperienceAssert(!g.Tasks.IsCompleted(ContentIds.TaskE2DoorLog), "warning reading does not silently obey Ellen");
            var queue = g.Apps.Launch(AppIds.WorkQueue, g.Player);
            g.Windows.Front(queue.Window);
            yield return WaitSeconds(1f);
            yield return RegressionWaitFor(() => !g.Director.AnySpeakerTyping, 60f, "warning guidance finished");
            elapsed = Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x;
            yield return WaitSeconds(1f);
            ExperienceAssert(Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x > elapsed + 0.5f, "request resumes after leaving its first warning");
            g.Windows.Front(mail.Window);
            elapsed = Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x;
            yield return WaitSeconds(1f);
            ExperienceAssert(Time.time - g.RemoteTaskLife[ContentIds.TaskE2DoorLog].x > elapsed + 0.5f, "reopening the warning cannot pause a request indefinitely");

            GameBootstrap.Restart(2, "asks");
            yield return RegressionWaitFor(() => G != null && G != g && G.Director.CurrentBeat == "asks", 30f, "deliberate expiration test shift");
            g = G;
            g.Tasks.Get(ContentIds.TaskE2DoorLog).Data.timeout = 2f;
            yield return RegressionWaitFor(() => g.Tasks.IsWithdrawn(ContentIds.TaskE2DoorLog), 100f, "untouched optional request expires");
            ExperienceAssert(!g.Tasks.IsCompleted(ContentIds.TaskE2DoorLog), "optional timeout remains a refusal rather than automatic completion");

            GameBootstrap.Restart(3, "rounds");
            yield return RegressionWaitFor(() => G != null && G != g && G.Director.CurrentBeat == "rounds" && G.Rounds.Running, 30f, "shelf warning test shift");
            g = G;
            g.Orders.NotifyViewed(ContentIds.Order3342, g.Player);
            yield return RegressionWaitFor(() => g.Rounds.PresentationHeld != null && g.Rounds.PresentationHeld(), 100f, "personal shelf warning reaches the front");
            var warning = g.Apps.OpenApps.OfType<NotepadApp>().First(p => p.IsOpen && p.Text.Contains("YOU DONT HAVE TO SIGN IT"));
            ExperienceAssert(g.Windows.Active == warning.Window, "Ellen's personal evidence is in front of the camera");
            float roundElapsed = g.Rounds.Elapsed;
            int stage = g.Rounds.Model.Stage;
            yield return WaitSeconds(2f);
            ExperienceAssert(Mathf.Abs(g.Rounds.Elapsed - roundElapsed) < 0.1f && g.Rounds.Model.Stage == stage,
                "round progression pauses during its brief focused warning reading");
            queue = g.Apps.Launch(AppIds.WorkQueue, g.Player);
            g.Windows.Front(queue.Window);
            yield return WaitSeconds(1f);
            ExperienceAssert(g.Rounds.Elapsed > roundElapsed + 0.5f && !g.Rounds.PresentationHeld(), "changing window releases the warning hold");
            g.Orders.Decide(ContentIds.Order3342, "reject", g.Player);
            yield return WaitSeconds(0.2f);
            ExperienceAssert(g.Rounds.Model.Config.Duration < 360f && g.Rounds.Model.Config.Duration >= 120f,
                "filed shelf decision shortens repetition while retaining an initial round");
            ExperienceAssert(g.Rounds.Model.Config.WatchSeconds > 0f && g.Rounds.Model.Config.AtLastStage == RoundsFinalRule.ClearSeat,
                "shortened rounds preserve Custodial's actual danger");
            g.Director.JumpTo("lost");
            yield return WaitSeconds(0.2f);
            ExperienceAssert(g.Rounds.PresentationHeld == null, "jumping out of rounds clears its presentation hold");
#endif
            Say("PASS story review fixes integration checks complete");
            GameBootstrap.ToTitle();
        }
    }
}
