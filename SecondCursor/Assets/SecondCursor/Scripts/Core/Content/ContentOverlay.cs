using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Content
{
    /// <summary>
    /// Merges a night's overlay content onto the base content (expansion spec 2.1). Pure functions: the
    /// inputs are never modified, a new object is returned (entries themselves are shared, not copied).
    /// A null overlay returns the base unchanged; a null base counts as empty.
    ///
    ///  - strings: by key, the overlay value replaces;
    ///  - story: a non-empty overlay array replaces the base array, splashTagline replaces if non-empty,
    ///    cameras merge by id;
    ///  - folders, files, emails: by id, the overlay entry replaces the whole base entry, "removed" deletes it;
    ///  - employees, work orders, tasks: by id, replace or add;
    ///  - dialogue: exchanges and lineSets by id, replace or add; panic, camera and record lines replace
    ///    only if the overlay's are non-empty.
    /// Replaced entries keep the base position; new entries are appended in overlay order.
    /// </summary>
    public static class ContentOverlay
    {
        public static StringTableData Apply(StringTableData b, StringTableData o)
        {
            if (o == null) return b;
            return new StringTableData { entries = MergeById(b?.entries, o.entries, e => e.key, null) };
        }

        public static StoryData Apply(StoryData b, StoryData o)
        {
            if (o == null) return b;
            b = b ?? new StoryData();
            return new StoryData
            {
                biosLines = NonEmpty(o.biosLines, b.biosLines),
                splashTagline = string.IsNullOrEmpty(o.splashTagline) ? b.splashTagline : o.splashTagline,
                cameras = MergeById(b.cameras, o.cameras, c => c.id, null),
                endingLines = NonEmpty(o.endingLines, b.endingLines),
                anomalyNotes = NonEmpty(o.anomalyNotes, b.anomalyNotes),
            };
        }

        public static FileSystemData Apply(FileSystemData b, FileSystemData o)
        {
            if (o == null) return b;
            return new FileSystemData
            {
                folders = MergeById(b?.folders, o.folders, f => f.id, f => f.removed),
                files = MergeById(b?.files, o.files, f => f.id, f => f.removed),
            };
        }

        public static EmailsData Apply(EmailsData b, EmailsData o)
        {
            if (o == null) return b;
            return new EmailsData { emails = MergeById(b?.emails, o.emails, e => e.id, e => e.removed) };
        }

        public static EmployeesData Apply(EmployeesData b, EmployeesData o)
        {
            if (o == null) return b;
            return new EmployeesData { employees = MergeById(b?.employees, o.employees, e => e.id, null) };
        }

        public static WorkOrdersData Apply(WorkOrdersData b, WorkOrdersData o)
        {
            if (o == null) return b;
            return new WorkOrdersData { orders = MergeById(b?.orders, o.orders, x => x.id, null) };
        }

        public static TasksData Apply(TasksData b, TasksData o)
        {
            if (o == null) return b;
            return new TasksData { tasks = MergeById(b?.tasks, o.tasks, t => t.id, null) };
        }

        public static DialogueData Apply(DialogueData b, DialogueData o)
        {
            if (o == null) return b;
            b = b ?? new DialogueData();
            return new DialogueData
            {
                exchanges = MergeById(b.exchanges, o.exchanges, x => x.id, null),
                panicLines = NonEmpty(o.panicLines, b.panicLines),
                cameraLines = NonEmpty(o.cameraLines, b.cameraLines),
                recordLines = NonEmpty(o.recordLines, b.recordLines),
                lineSets = MergeById(b.lineSets, o.lineSets, l => l.id, null),
            };
        }

        static string[] NonEmpty(string[] overlay, string[] fallback)
        {
            if (overlay != null && overlay.Length > 0) return (string[])overlay.Clone();
            return fallback != null ? (string[])fallback.Clone() : Array.Empty<string>();
        }

        /// <summary>
        /// Keyed merge. For an id listed twice in the overlay the last entry wins. Entries without an id are
        /// kept from the base and ignored in the overlay (they cannot be addressed).
        /// </summary>
        static T[] MergeById<T>(T[] b, T[] o, Func<T, string> key, Func<T, bool> removed) where T : class
        {
            b = b ?? Array.Empty<T>();
            o = o ?? Array.Empty<T>();
            var overlay = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var e in o)
            {
                string k = e != null ? key(e) : null;
                if (!string.IsNullOrEmpty(k)) overlay[k] = e;
            }
            var result = new List<T>(b.Length + o.Length);
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in b)
            {
                if (e == null) continue;
                string k = key(e);
                if (string.IsNullOrEmpty(k) || !overlay.TryGetValue(k, out var replacement))
                {
                    result.Add(e);
                    continue;
                }
                if (!done.Add(k)) continue; // a duplicated base id is replaced once
                if (removed == null || !removed(replacement)) result.Add(replacement);
            }
            foreach (var e in o)
            {
                string k = e != null ? key(e) : null;
                if (string.IsNullOrEmpty(k) || !done.Add(k)) continue;
                var winner = overlay[k];
                if (removed == null || !removed(winner)) result.Add(winner);
            }
            return result.ToArray();
        }
    }

    /// <summary>
    /// The eight content files of one folder (the base or a night overlay), before they become a
    /// <see cref="ContentDatabase"/>. Missing files stay null.
    /// </summary>
    public sealed class ContentPack
    {
        public StringTableData Strings;
        public StoryData Story;
        public FileSystemData FileSystem;
        public EmailsData Emails;
        public EmployeesData Employees;
        public WorkOrdersData WorkOrders;
        public TasksData Tasks;
        public DialogueData Dialogue;

        public bool IsEmpty => Strings == null && Story == null && FileSystem == null && Emails == null &&
                               Employees == null && WorkOrders == null && Tasks == null && Dialogue == null;

        /// <summary>This pack with <paramref name="overlay"/> applied on top (a new pack; this one is unchanged).</summary>
        public ContentPack Overlay(ContentPack overlay)
        {
            if (overlay == null) return this;
            return new ContentPack
            {
                Strings = ContentOverlay.Apply(Strings, overlay.Strings),
                Story = ContentOverlay.Apply(Story, overlay.Story),
                FileSystem = ContentOverlay.Apply(FileSystem, overlay.FileSystem),
                Emails = ContentOverlay.Apply(Emails, overlay.Emails),
                Employees = ContentOverlay.Apply(Employees, overlay.Employees),
                WorkOrders = ContentOverlay.Apply(WorkOrders, overlay.WorkOrders),
                Tasks = ContentOverlay.Apply(Tasks, overlay.Tasks),
                Dialogue = ContentOverlay.Apply(Dialogue, overlay.Dialogue),
            };
        }

        public ContentDatabase Build() => new ContentDatabase(Strings, Story, FileSystem, Emails, Employees, WorkOrders, Tasks, Dialogue);
    }
}
