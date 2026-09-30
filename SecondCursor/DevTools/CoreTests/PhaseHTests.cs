using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase H (blind playtest fixes): due-time countdowns, tasks that stay listed when taken back, information lines, the
    /// tug meter's value, and the new on-screen text (tug label, notices, Quick Start, remote-task hints, the shelf check).
    /// </summary>
    public class PhaseHTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));

        static T Read<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = Read<StringTableData>(folder, "strings"), Story = Read<StoryData>(folder, "story"),
            FileSystem = Read<FileSystemData>(folder, "filesystem"), Emails = Read<EmailsData>(folder, "emails"),
            Employees = Read<EmployeesData>(folder, "employees"), WorkOrders = Read<WorkOrdersData>(folder, "workorders"),
            Tasks = Read<TasksData>(folder, "tasks"), Dialogue = Read<DialogueData>(folder, "dialogue"),
        };

        static ContentDatabase Night(int night)
        {
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        static ContentDatabase Demo() => Pack("").Build();

        static Dictionary<string, string> Table(string folder)
        {
            var d = new Dictionary<string, string>();
            var t = Read<StringTableData>(folder, "strings");
            if (t != null) foreach (var e in t.entries) d[e.key] = e.value;
            return d;
        }

        // ------------------------------------------------------------------ due times

        [Theory]
        [InlineData("3:00 AM", 180)]
        [InlineData("3:30 AM", 210)]
        [InlineData("12:05 AM", 5)]
        [InlineData("1:00 PM", 780)]
        [InlineData("7:00", 420)]
        [InlineData("", -1)]
        [InlineData("soon", -1)]
        [InlineData("25:00 AM", -1)]
        public void DueTimesAreReadAsMinutesSinceMidnight(string deadline, int minutes)
        {
            Assert.Equal(minutes, TaskDeadline.Minutes(deadline));
        }

        [Fact]
        public void MinutesLeftCountDownAndNeverGoNegative()
        {
            Assert.Equal(11, TaskDeadline.MinutesLeft("3:00 AM", 2 * 60 + 49));
            Assert.Equal(0, TaskDeadline.MinutesLeft("3:00 AM", 3 * 60 + 2));
            Assert.Equal(30, TaskDeadline.MinutesLeft("3:30 AM", 3 * 60));
            Assert.Equal(-1, TaskDeadline.MinutesLeft("", 100));
        }

        // ------------------------------------------------------------------ the Work Queue's lines

        sealed class World : ITaskWorld
        {
            public bool IsEmailRead(string emailId) => false;
            public string FolderOf(string fileId) => null;
            public bool IsShredded(string fileId) => false;
            public string DecisionFor(string orderId) => null;
            public bool IsFileOpenedByPlayer(string fileId) => false;
            public bool IsEmployeeViewedByPlayer(string employeeId) => false;
            public string MovedBy(string fileId) => null;
        }

        static WorkTaskManager Manager(params TaskData[] tasks) => new WorkTaskManager(tasks, new World());

        static TaskData Task(string id, string type, params string[] targets) =>
            new TaskData { id = id, title = id, type = type, targets = targets ?? Array.Empty<string>() };

        [Fact]
        public void AWaitLineNeverCompletesByItselfOnlyByTheStory()
        {
            var tm = Manager(Task("wait", "Wait"));
            Assert.Equal(TaskType.Wait, tm.Get("wait").Type);
            tm.Activate("wait");
            tm.Evaluate();
            Assert.True(tm.IsActive("wait"));
            Assert.Equal("wait", tm.Current.Id);
            tm.ForceComplete("wait");
            Assert.True(tm.IsCompleted("wait"));
        }

        [Fact]
        public void ATaskTakenBackAfterItWasShownStaysListedWithItsReason()
        {
            var tm = Manager(Task("shred", "DeleteFile", "f"), Task("hidden", "DeleteFile", "g"), Task("remote", "MoveFile", "h"));
            tm.Activate("shred");
            int before = tm.Revision;
            tm.Withdraw("shred", "missed 3:00 AM");
            var t = tm.Get("shred");
            Assert.True(tm.Revision > before);
            Assert.True(t.IsWithdrawn);
            Assert.True(WorkTaskManager.IsListedWithdrawn(t));
            Assert.Equal("missed 3:00 AM", t.WithdrawNote);
            Assert.Null(tm.Current);

            // Never shown: taken back quietly, whatever the reason.
            tm.Withdraw("hidden", "cancelled");
            Assert.False(WorkTaskManager.IsListedWithdrawn(tm.Get("hidden")));

            // Shown, taken back without a reason (a remote item that expired): it vanishes as before.
            tm.Activate("remote");
            tm.Withdraw("remote");
            Assert.False(WorkTaskManager.IsListedWithdrawn(tm.Get("remote")));
        }

        [Fact]
        public void AFinishedTaskCanSayWhatItFiled()
        {
            var tm = Manager(Task("check", "DecideOrder", "o1"));
            tm.Activate("check");
            tm.ForceComplete("check");
            int before = tm.Revision;
            tm.SetResult("check", "Shelf check filed: 1 approved, 2 rejected");
            Assert.True(tm.Revision > before);
            Assert.Equal("Shelf check filed: 1 approved, 2 rejected", tm.Get("check").ResultNote);
            before = tm.Revision;
            tm.SetResult("check", "Shelf check filed: 1 approved, 2 rejected");
            Assert.Equal(before, tm.Revision);
        }

        // ------------------------------------------------------------------ the tug meter

        [Fact]
        public void ThePullMeterStartsEvenAndFollowsTheFight()
        {
            var s = new TugOfWarSettings();
            var tug = new TugOfWar(s);
            Assert.Equal(0.5f, tug.PlayerLead, 3);

            // Holding still: the second cursor gains, the meter falls toward 0.
            var still = new TugOfWar(s);
            var p = new Vec2(300f, 300f);
            var e = new Vec2(340f, 300f);
            for (int i = 0; i < 20 && !still.IsOver; i++) still.Step(0.02f, p, true, e, 0.62f);
            Assert.True(still.PlayerLead < 0.5f);

            // Yanking away at 450 px/s: the meter rises toward 1, and a win shows it full.
            var yank = new TugOfWar(s);
            var q = new Vec2(300f, 300f);
            float last = yank.PlayerLead;
            for (int i = 0; i < 200 && !yank.IsOver; i++)
            {
                q = new Vec2(q.x - 9f, q.y);
                e = new Vec2(e.x + 2f, e.y);
                yank.Step(0.02f, q, true, e, 0.62f);
                if (!yank.IsOver) last = yank.PlayerLead;
            }
            Assert.Equal(TugOutcome.PlayerWins, yank.Outcome);
            Assert.True(last > 0.5f);
            Assert.Equal(1f, yank.PlayerLead, 3);
        }

        // ------------------------------------------------------------------ what the screen says

        [Fact]
        public void TheTugExplainsItselfInTheBaseStrings()
        {
            if (!Present) return;
            var db = Demo();
            foreach (var key in new[] { "tug.label", "tug.won", "tug.lost.release", "tug.refused", "tug.you", "tug.them", "shred.cancelled.by", "camera.closed.by",
                                        "window.closed.by", "file.moved.by", "session.entity", "notepad.status.typing", "notepad.status.away",
                                        "workqueue.mail", "workqueue.deadline.left", "taskbar.due", "taskbar.duenow", "mail.more",
                                        "end.n1.outcome.shredded", "end.n1.outcome.kept" })
                Assert.True(db.HasText(key), "missing base string " + key);
            Assert.StartsWith("SESSION 017 IS PULLING", db.Text("tug.label"));
            Assert.Contains("HOLD AND DRAG {0}", db.Text("tug.label"));   // Phase K: the label names the arrow's direction
            Assert.Contains("session 017", db.Text("notify.conflict"));
            Assert.Contains("HOLD THE BUTTON", db.Text("tug.label.first"));   // Phase L: the first fight carries the lesson; the Quick Start no longer does
            Assert.Contains("hold the button", db.Text("welcome.body"));
            Assert.Contains("|| button", db.Text("quickstart.body"));
            Assert.Contains("Options", db.Text("quickstart.body"));
            Assert.Equal("Nexus", db.Text("start.button"));
            Assert.Contains("OPTIONS", db.Text("pause.title"));
            Assert.Contains("{0}", db.Text("end.n1.outcome.kept"));
            // Steam Deck wording names the trigger, not the mouse button.
            db.Variant = "deck";
            foreach (var key in new[] { "tug.label", "tug.lost.release", "tug.refused", "notify.conflict" })
                Assert.Contains("R2", db.Text(key));
            Assert.Contains("R2", db.Text("tug.label.first"));
            Assert.Contains("R2", db.Text("welcome.body"));
        }

        [Fact]
        public void NightTwoRemoteItemsHaveHintsAndTheRoundHasAQueueLine()
        {
            if (!Present || !File.Exists(Path.Combine(Dir, "night2", "tasks.json"))) return;
            var db = Night(2);
            foreach (var t in db.Tasks.tasks.Where(t => t.author == "entity"))
                Assert.False(string.IsNullOrEmpty(t.hint), "remote item " + t.id + " has no hint");
            var watch = db.Task(ContentIds.TaskN2RoundsWatch);
            Assert.NotNull(watch);
            Assert.Equal("Wait", watch.type);
            Assert.Contains("Camera Viewer", watch.title);
            Assert.Equal("3:00 AM", db.Task(ContentIds.TaskN2Shred209).deadline);
            Assert.Contains("3:00 AM", db.Text("notify.order.missed"));
            Assert.Contains("{0}", db.Text("workqueue.withdrawn.missed"));
            Assert.Equal("session 209", db.Text("session.gary"));
        }

        [Fact]
        public void NightThreesShelfCheckSaysHowTheCameraWorksAndWhatWasFiled()
        {
            if (!Present || !File.Exists(Path.Combine(Dir, "night3", "tasks.json"))) return;
            var db = Night(3);
            var shelf = db.Task(ContentIds.TaskN3Shelf);
            Assert.Contains("12 to 19", shelf.hint);
            Assert.Contains("NEXT", shelf.hint);
            Assert.Equal("Wait", db.Task(ContentIds.TaskN3WaitRounds).type);
            Assert.Contains("3:00 AM", db.Task(ContentIds.TaskN3WaitRounds).title);
            Assert.Equal("Shelf check filed: 1 approved, 2 rejected", db.Format("workorders.shelf.filed", 1, 2));
            Assert.Equal(new[] { "THAT ONE IS YOU", "YOU DONT HAVE TO SIGN IT" }, db.Lines("n3_shelf_you"));
            Assert.Contains("ALLOW_LOGOFF=1", db.Text("policy.logoff.on"));
            Assert.Contains("last ALLOW_LOGOFF line", db.Text("policy.logoff.off"));
            Assert.Contains("{1}", db.Text("file.saved.by"));
        }

        [Fact]
        public void TheNightOneBriefingSaysWhatToDoIfTheWorkstationGoesDown()
        {
            if (!Present) return;
            Assert.Contains("goes down", Demo().Email(ContentIds.MailWelcome).body);
            Assert.Contains("opens by itself", Demo().Task(ContentIds.TaskReadBriefing).hint);
        }
    }
}
