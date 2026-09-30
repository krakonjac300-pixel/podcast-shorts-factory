using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;

namespace SecondCursor.Core.Tasks
{
    /// <summary>
    /// Wait: an information line the story completes itself ("Wait for Custodial rounds (3:00 AM)"); nothing the world
    /// does ever completes it.
    /// </summary>
    public enum TaskType { ReadEmail, MoveFile, DeleteFile, DecideOrder, OpenFile, ViewEmployee, Wait, Unknown }

    /// <summary>Withdrawn: taken back out of the queue (an entity task that expired); it never comes back.</summary>
    public enum TaskState { Hidden, Active, Completed, Withdrawn }

    /// <summary>World queries a task needs to judge completion. Implemented by the runtime game state.</summary>
    public interface ITaskWorld
    {
        bool IsEmailRead(string emailId);
        /// <summary>Folder id the file is in, or null if it is shredded/missing.</summary>
        string FolderOf(string fileId);
        bool IsShredded(string fileId);
        /// <summary>"approve", "reject" or null if undecided.</summary>
        string DecisionFor(string orderId);
        /// <summary>The player (not another cursor) has opened this file at least once.</summary>
        bool IsFileOpenedByPlayer(string fileId);
        /// <summary>The player has looked at this employee's record in Personnel.</summary>
        bool IsEmployeeViewedByPlayer(string employeeId);
        /// <summary>Phase K: the other session that last moved this file ("session 017"), or null when the player (or the system) did.</summary>
        string MovedBy(string fileId);
    }

    public sealed class WorkTask
    {
        public readonly TaskData Data;
        public readonly TaskType Type;
        public TaskState State;
        public int Progress;
        public int Goal;
        /// <summary>The task was in the queue at some point (a task withdrawn before it was ever given stays out of sight).</summary>
        public bool WasShown;
        /// <summary>Why a shown company task was withdrawn ("missed 3:00 AM"); the Work Queue keeps it, struck through. Null = it vanishes.</summary>
        public string WithdrawNote;
        /// <summary>What a finished task filed ("1 approved, 2 rejected"); the Work Queue shows it after the title.</summary>
        public string ResultNote;
        /// <summary>
        /// Phase I: the story rewrote the task's words (a shred that nobody can do while another session holds the file says
        /// so). Null = the authored text.
        /// </summary>
        public string TitleOverride, DescriptionOverride, HintOverride;
        /// <summary>
        /// Phase K: how many of the done targets another session did, by session ("session 017" = 1), in the order they were
        /// first seen. Empty when the player did all of it.
        /// </summary>
        public readonly List<KeyValuePair<string, int>> HelpedBy = new List<KeyValuePair<string, int>>();
        /// <summary>Phase K: a line the story files under one target (the shelf check says what each decision was checked against).</summary>
        public readonly Dictionary<string, string> TargetNotes = new Dictionary<string, string>(StringComparer.Ordinal);

        public WorkTask(TaskData data)
        {
            Data = data;
            Type = ParseType(data.type);
            Goal = Math.Max(1, data.targets.Length);
        }

        public string Id => Data.id;
        public string Title => TitleOverride ?? Data.title;
        public string Description => DescriptionOverride ?? Data.description;
        public string Hint => HintOverride ?? Data.hint;
        /// <summary>"3/4" or "3/4, 1 by session 017" (see <see cref="TaskProgress.Format"/>).</summary>
        public string ProgressText => TaskProgress.Format(Progress, Goal, HelpedBy);
        public bool IsDone => State == TaskState.Completed;
        public bool IsWithdrawn => State == TaskState.Withdrawn;
        /// <summary>Written into the Work Queue by the second cursor, not by the company.</summary>
        public bool IsEntityAuthored => string.Equals(Data.author, "entity", StringComparison.OrdinalIgnoreCase);

        public static TaskType ParseType(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "reademail": return TaskType.ReadEmail;
                case "movefile": return TaskType.MoveFile;
                case "deletefile": case "shredfile": return TaskType.DeleteFile;
                case "decideorder": return TaskType.DecideOrder;
                case "openfile": return TaskType.OpenFile;
                case "viewemployee": return TaskType.ViewEmployee;
                case "wait": return TaskType.Wait;
                default: return TaskType.Unknown;
            }
        }
    }

    /// <summary>Phase K: a task's counter says who did the work ("3/4, 1 by session 017"), so help by another session is never silent.</summary>
    public static class TaskProgress
    {
        public static string Format(int progress, int goal, IReadOnlyList<KeyValuePair<string, int>> helpedBy)
        {
            string s = progress + "/" + goal;
            if (helpedBy == null) return s;
            foreach (var kv in helpedBy)
                if (kv.Value > 0) s += ", " + kv.Value + " by " + kv.Key;
            return s;
        }
    }

    /// <summary>A task's due time ("3:00 AM") against the shift clock, for the countdowns in the Work Queue and the taskbar.</summary>
    public static class TaskDeadline
    {
        /// <summary>"3:00 AM" -> 180 (minutes since midnight); -1 when it cannot be read.</summary>
        public static int Minutes(string deadline)
        {
            if (string.IsNullOrEmpty(deadline)) return -1;
            string s = deadline.Trim().ToUpperInvariant();
            bool pm = s.EndsWith("PM", StringComparison.Ordinal);
            bool am = s.EndsWith("AM", StringComparison.Ordinal);
            if (am || pm) s = s.Substring(0, s.Length - 2).Trim();
            int colon = s.IndexOf(':');
            if (colon <= 0 || !int.TryParse(s.Substring(0, colon), out int h) || !int.TryParse(s.Substring(colon + 1), out int m)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            if (pm && h < 12) h += 12;
            if (am && h == 12) h = 0;
            return h * 60 + m;
        }

        /// <summary>Whole minutes from <paramref name="clockMinutes"/> to the deadline (never below 0); -1 when there is none.</summary>
        public static int MinutesLeft(string deadline, int clockMinutes)
        {
            int due = Minutes(deadline);
            if (due < 0) return -1;
            return Math.Max(0, due - clockMinutes);
        }

        /// <summary>
        /// Phase K: real seconds until the deadline at the shift clock's current speed (game minutes per real second); -1 when
        /// that cannot be said (a held clock, no deadline). The fourth blind tester read "29 min left" and lost it in 3 minutes.
        /// </summary>
        public static float RealSeconds(string deadline, double clockMinutes, float minutesPerSecond, bool frozen)
        {
            int due = Minutes(deadline);
            if (due < 0 || frozen || minutesPerSecond <= 1e-4f) return -1f;
            return (float)Math.Max(0.0, (due - clockMinutes) / minutesPerSecond);
        }

        /// <summary>"about 2 min 30 s", "about 1 min", "about 40 s", "a few seconds" (minutes to the half, seconds to five).</summary>
        public static string Approx(float seconds)
        {
            if (seconds < 10f) return "a few seconds";
            if (seconds < 57.5f) return "about " + (int)(Math.Round(seconds / 5.0) * 5) + " s";
            int halves = (int)Math.Round(seconds / 30.0);
            int min = halves / 2;
            return "about " + min + " min" + (halves % 2 == 1 ? " 30 s" : "");
        }
    }

    /// <summary>
    /// Ordinary work: the thing the entity interrupts. Tasks are data (tasks.json); the story director
    /// activates them. Completion is judged against world state (so doing a task early still counts) and
    /// re-evaluated whenever the runtime reports a relevant event.
    /// </summary>
    public sealed class WorkTaskManager
    {
        readonly List<WorkTask> _tasks = new List<WorkTask>();
        readonly Dictionary<string, WorkTask> _byId = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
        readonly ITaskWorld _world;

        public event Action<WorkTask> TaskActivated;
        public event Action<WorkTask> TaskCompleted;
        public event Action<WorkTask> TaskProgressed;
        public event Action<WorkTask> TaskWithdrawn;

        public int Revision { get; private set; }

        public WorkTaskManager(IEnumerable<TaskData> tasks, ITaskWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (tasks != null)
            {
                foreach (var d in tasks)
                {
                    if (d == null || string.IsNullOrEmpty(d.id) || _byId.ContainsKey(d.id)) continue;
                    var t = new WorkTask(d);
                    _tasks.Add(t);
                    _byId[d.id] = t;
                }
            }
        }

        public IReadOnlyList<WorkTask> Tasks => _tasks;

        public WorkTask Get(string id) => id != null && _byId.TryGetValue(id, out var t) ? t : null;

        /// <summary>The first active (not completed) task in list order, or null.</summary>
        public WorkTask Current
        {
            get
            {
                foreach (var t in _tasks) if (t.State == TaskState.Active) return t;
                return null;
            }
        }

        public bool IsCompleted(string id) => Get(id)?.State == TaskState.Completed;
        public bool IsWithdrawn(string id) => Get(id)?.State == TaskState.Withdrawn;
        public bool IsActive(string id) => Get(id)?.State == TaskState.Active;

        /// <summary>Written into the Work Queue by the second cursor (see <see cref="WorkTask.IsEntityAuthored"/>).</summary>
        public static bool IsEntityAuthored(WorkTask task) => task != null && task.IsEntityAuthored;

        public void Activate(string id)
        {
            var t = Get(id);
            if (t == null || t.State != TaskState.Hidden) return;
            t.State = TaskState.Active;
            t.WasShown = true;
            Revision++;
            GameLog.Info(LogChannel.Task, "Activated " + t.Id + " \"" + t.Title + "\"");
            TaskActivated?.Invoke(t);
            Evaluate();
        }

        /// <summary>Debug / story override.</summary>
        public void ForceComplete(string id)
        {
            var t = Get(id);
            if (t == null || t.State == TaskState.Completed || t.State == TaskState.Withdrawn) return;
            if (t.State == TaskState.Hidden) { t.State = TaskState.Active; t.WasShown = true; TaskActivated?.Invoke(t); }
            Complete(t);
        }

        /// <summary>
        /// Take a task back out of the queue (an entity task that expired, a suspended order). A withdrawn task never
        /// completes and cannot be activated again; completed tasks stay completed. With a <paramref name="note"/>
        /// ("missed 3:00 AM") a task the player has seen stays listed, struck through, so it never vanishes silently.
        /// </summary>
        public void Withdraw(string id, string note = null)
        {
            var t = Get(id);
            if (t == null || t.State == TaskState.Completed || t.State == TaskState.Withdrawn) return;
            t.State = TaskState.Withdrawn;
            t.WithdrawNote = t.WasShown && !string.IsNullOrEmpty(note) ? note : null;
            Revision++;
            GameLog.Info(LogChannel.Task, "Withdrew " + t.Id + (t.WithdrawNote != null ? " (" + t.WithdrawNote + ")" : ""));
            TaskWithdrawn?.Invoke(t);
        }

        /// <summary>A withdrawn task the Work Queue still lists (shown before, withdrawn with a note).</summary>
        public static bool IsListedWithdrawn(WorkTask t) => t != null && t.State == TaskState.Withdrawn && t.WasShown && t.WithdrawNote != null;

        /// <summary>
        /// Phase I: rewrites what a task says while it stays the same task (the Night 1 order to shred employee_017.dat turns into
        /// "blocked: held by session 017" once the bin has refused it). Null leaves that part as authored.
        /// </summary>
        public void Rewrite(string id, string title, string description, string hint)
        {
            var t = Get(id);
            if (t == null) return;
            if (t.TitleOverride == title && t.DescriptionOverride == description && t.HintOverride == hint) return;
            t.TitleOverride = title;
            t.DescriptionOverride = description;
            t.HintOverride = hint;
            Revision++;
            GameLog.Info(LogChannel.Task, "Rewrote " + t.Id + " \"" + t.Title + "\"");
        }

        /// <summary>Phase K: a note under one of a task's targets in the Work Queue's checklist.</summary>
        public void SetTargetNote(string id, string target, string note)
        {
            var t = Get(id);
            if (t == null || string.IsNullOrEmpty(target)) return;
            t.TargetNotes[target] = note;
            Revision++;
        }

        /// <summary>What a finished task filed, shown after its title in the Work Queue.</summary>
        public void SetResult(string id, string note)
        {
            var t = Get(id);
            if (t == null || t.ResultNote == note) return;
            t.ResultNote = note;
            Revision++;
        }

        /// <summary>Re-check every active task against the world. Call after any relevant game event.</summary>
        public void Evaluate()
        {
            // Copy: completing a task can trigger listeners that activate further tasks.
            var active = new List<WorkTask>();
            foreach (var t in _tasks) if (t.State == TaskState.Active) active.Add(t);
            foreach (var t in active)
            {
                if (t.State != TaskState.Active) continue;
                string helpedBefore = t.ProgressText;
                int progress = Measure(t);
                if (progress != t.Progress || t.ProgressText != helpedBefore)
                {
                    t.Progress = progress;
                    Revision++;
                    TaskProgressed?.Invoke(t);
                }
                if (progress >= t.Goal) Complete(t);
            }
        }

        int Measure(WorkTask t)
        {
            var targets = t.Data.targets;
            int n = 0;
            switch (t.Type)
            {
                case TaskType.ReadEmail:
                    foreach (var id in targets) if (_world.IsEmailRead(id)) n++;
                    break;
                case TaskType.MoveFile:
                    t.HelpedBy.Clear();
                    foreach (var id in targets)
                    {
                        if (_world.FolderOf(id) != t.Data.param) continue;
                        n++;
                        string by = _world.MovedBy(id);
                        if (string.IsNullOrEmpty(by)) continue;
                        int at = t.HelpedBy.FindIndex(kv => kv.Key == by);
                        if (at < 0) t.HelpedBy.Add(new KeyValuePair<string, int>(by, 1));
                        else t.HelpedBy[at] = new KeyValuePair<string, int>(by, t.HelpedBy[at].Value + 1);
                    }
                    break;
                case TaskType.DeleteFile:
                    foreach (var id in targets) if (_world.IsShredded(id)) n++;
                    break;
                case TaskType.DecideOrder:
                    foreach (var id in targets) if (_world.DecisionFor(id) != null) n++;
                    break;
                case TaskType.OpenFile:
                    foreach (var id in targets) if (_world.IsFileOpenedByPlayer(id)) n++;
                    break;
                case TaskType.ViewEmployee:
                    foreach (var id in targets) if (_world.IsEmployeeViewedByPlayer(id)) n++;
                    break;
            }
            return n;
        }

        void Complete(WorkTask t)
        {
            t.State = TaskState.Completed;
            t.Progress = t.Goal;
            Revision++;
            GameLog.Info(LogChannel.Task, "Completed " + t.Id);
            TaskCompleted?.Invoke(t);
        }
    }
}
