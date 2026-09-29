using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;

namespace SecondCursor.Core.Tasks
{
    public enum TaskType { ReadEmail, MoveFile, DeleteFile, DecideOrder, OpenFile, ViewEmployee, Unknown }

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
    }

    public sealed class WorkTask
    {
        public readonly TaskData Data;
        public readonly TaskType Type;
        public TaskState State;
        public int Progress;
        public int Goal;

        public WorkTask(TaskData data)
        {
            Data = data;
            Type = ParseType(data.type);
            Goal = Math.Max(1, data.targets.Length);
        }

        public string Id => Data.id;
        public string Title => Data.title;
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
                default: return TaskType.Unknown;
            }
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
            if (t.State == TaskState.Hidden) { t.State = TaskState.Active; TaskActivated?.Invoke(t); }
            Complete(t);
        }

        /// <summary>
        /// Take a task back out of the queue (an entity task that expired, a suspended order). A withdrawn task is
        /// hidden, never completes, and cannot be activated again. Completed tasks stay completed.
        /// </summary>
        public void Withdraw(string id)
        {
            var t = Get(id);
            if (t == null || t.State == TaskState.Completed || t.State == TaskState.Withdrawn) return;
            t.State = TaskState.Withdrawn;
            Revision++;
            GameLog.Info(LogChannel.Task, "Withdrew " + t.Id);
            TaskWithdrawn?.Invoke(t);
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
                int progress = Measure(t);
                if (progress != t.Progress)
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
                    foreach (var id in targets) if (_world.FolderOf(id) == t.Data.param) n++;
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
