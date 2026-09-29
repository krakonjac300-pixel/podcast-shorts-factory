using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Content
{
    /// <summary>
    /// Read-only access to all authored content (strings, files, emails, employees, work orders, tasks,
    /// dialogue, story text) with lookups by ID. Missing required content is replaced by clearly-marked
    /// placeholders and reported in <see cref="Problems"/>, so a broken JSON file degrades the prototype
    /// instead of crashing it.
    /// </summary>
    public sealed class ContentDatabase
    {
        public readonly StoryData Story;
        public readonly FileSystemData FileSystem;
        public readonly EmailsData Emails;
        public readonly EmployeesData Employees;
        public readonly WorkOrdersData WorkOrders;
        public readonly TasksData Tasks;
        public readonly DialogueData Dialogue;

        /// <summary>Human-readable content problems (missing ids, placeholders created). Logged at boot.</summary>
        public readonly List<string> Problems = new List<string>();

        readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, EmailData> _emails = new Dictionary<string, EmailData>(StringComparer.Ordinal);
        readonly Dictionary<string, EmployeeData> _employees = new Dictionary<string, EmployeeData>(StringComparer.Ordinal);
        readonly Dictionary<string, WorkOrderData> _orders = new Dictionary<string, WorkOrderData>(StringComparer.Ordinal);
        readonly Dictionary<string, TaskData> _tasks = new Dictionary<string, TaskData>(StringComparer.Ordinal);
        readonly Dictionary<string, ExchangeData> _exchanges = new Dictionary<string, ExchangeData>(StringComparer.Ordinal);
        readonly Dictionary<string, CameraData> _cameras = new Dictionary<string, CameraData>(StringComparer.Ordinal);
        readonly Dictionary<string, LineSetData> _lineSets = new Dictionary<string, LineSetData>(StringComparer.Ordinal);

        public ContentDatabase(StringTableData strings, StoryData story, FileSystemData fileSystem, EmailsData emails,
            EmployeesData employees, WorkOrdersData workOrders, TasksData tasks, DialogueData dialogue)
        {
            strings = strings ?? new StringTableData();
            Story = story ?? new StoryData();
            FileSystem = fileSystem ?? new FileSystemData();
            Emails = emails ?? new EmailsData();
            Employees = employees ?? new EmployeesData();
            WorkOrders = workOrders ?? new WorkOrdersData();
            Tasks = tasks ?? new TasksData();
            Dialogue = dialogue ?? new DialogueData();

            strings.Sanitize();
            Story.Sanitize();
            FileSystem.Sanitize();
            Emails.Sanitize();
            Employees.Sanitize();
            WorkOrders.Sanitize();
            Tasks.Sanitize();
            Dialogue.Sanitize();

            foreach (var kv in DefaultStrings.All) _strings[kv.Key] = kv.Value;
            foreach (var e in strings.entries)
                if (e != null && !string.IsNullOrEmpty(e.key)) _strings[e.key] = e.value;

            EnsureRequired();
            Index();
            Validate();
        }

        // ---------------------------------------------------------------- strings

        public string Text(string key) => Text(key, key);

        public string Text(string key, string fallback)
        {
            if (key != null && _strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            return fallback ?? string.Empty;
        }

        public string Format(string key, params object[] args)
        {
            string pattern = Text(key);
            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }

        public bool HasText(string key) => key != null && _strings.ContainsKey(key);

        // ---------------------------------------------------------------- lookups

        public EmailData Email(string id) => id != null && _emails.TryGetValue(id, out var v) ? v : null;
        public EmployeeData Employee(string id) => id != null && _employees.TryGetValue(id, out var v) ? v : null;
        public WorkOrderData Order(string id) => id != null && _orders.TryGetValue(id, out var v) ? v : null;
        public TaskData Task(string id) => id != null && _tasks.TryGetValue(id, out var v) ? v : null;
        public ExchangeData Exchange(string id) => id != null && _exchanges.TryGetValue(id, out var v) ? v : null;
        public CameraData Camera(string id) => id != null && _cameras.TryGetValue(id, out var v) ? v : null;
        public LineSetData LineSet(string id) => id != null && _lineSets.TryGetValue(id, out var v) ? v : null;

        /// <summary>The lines of a line set, or none if it does not exist.</summary>
        public string[] Lines(string id) => LineSet(id)?.lines ?? Array.Empty<string>();

        public EmployeeData EmployeeByNumber(string number)
        {
            if (string.IsNullOrEmpty(number)) return null;
            string n = number.TrimStart('0', '#', ' ');
            foreach (var e in Employees.employees)
            {
                if (e == null) continue;
                if (string.Equals(e.number, number, StringComparison.OrdinalIgnoreCase)) return e;
                if (e.number.TrimStart('0', '#', ' ') == n && n.Length > 0) return e;
            }
            return null;
        }

        // ---------------------------------------------------------------- setup

        void Index()
        {
            foreach (var e in Emails.emails) if (e != null && e.id.Length > 0) _emails[e.id] = e;
            foreach (var e in Employees.employees) if (e != null && e.id.Length > 0) _employees[e.id] = e;
            foreach (var o in WorkOrders.orders) if (o != null && o.id.Length > 0) _orders[o.id] = o;
            foreach (var t in Tasks.tasks) if (t != null && t.id.Length > 0) _tasks[t.id] = t;
            foreach (var x in Dialogue.exchanges) if (x != null && x.id.Length > 0) _exchanges[x.id] = x;
            foreach (var c in Story.cameras) if (c != null && c.id.Length > 0) _cameras[c.id] = c;
            foreach (var l in Dialogue.lineSets) if (l != null && l.id.Length > 0 && !_lineSets.ContainsKey(l.id)) _lineSets[l.id] = l;
        }

        /// <summary>Content mistakes the loader can report without failing (listed in <see cref="Problems"/>).</summary>
        void Validate()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in Dialogue.lineSets)
            {
                if (l == null) continue;
                if (l.id.Length == 0) Problems.Add("Line set without an id");
                else if (!seen.Add(l.id)) Problems.Add("Duplicate line set id '" + l.id + "'");
            }
            foreach (var t in Tasks.tasks)
            {
                if (t == null || t.type != "ViewEmployee") continue;
                foreach (var target in t.targets)
                    if (Employee(target) == null) Problems.Add("Task '" + t.id + "' views unknown employee '" + target + "'");
            }
        }

        void EnsureRequired()
        {
            var fs = FileSystem;
            var folders = new List<FolderData>(fs.folders);
            var files = new List<FileData>(fs.files);

            void Folder(string id, string name, string parent, bool locked = false)
            {
                if (folders.Exists(f => f != null && f.id == id)) return;
                folders.Add(new FolderData { id = id, name = name, parent = parent, locked = locked });
                Problems.Add("Missing folder '" + id + "' (placeholder created)");
            }

            void File(string id, string name, string type, string folder, string content)
            {
                if (files.Exists(f => f != null && f.id == id)) return;
                files.Add(new FileData
                {
                    id = id, name = name, type = type, folder = folder, size = "4 KB", modified = "1998-11-03 23:10",
                    content = content, tags = Array.Empty<string>()
                });
                Problems.Add("Missing file '" + id + "' (placeholder created)");
            }

            Folder(ContentIds.FolderRoot, "WS-04 (C:)", "");
            Folder(ContentIds.FolderDesktop, "Desktop", ContentIds.FolderRoot);
            Folder(ContentIds.FolderIntake, "Intake", ContentIds.FolderRoot);
            Folder(ContentIds.FolderArchive, "Archive", ContentIds.FolderRoot);
            Folder(ContentIds.FolderDocuments, "Documents", ContentIds.FolderRoot);
            Folder(ContentIds.FolderRestricted, "Restricted", ContentIds.FolderRoot, true);
            Folder(ContentIds.FolderSystem, "System", ContentIds.FolderRoot);
            Folder(ContentIds.FolderDisposal, "Disposal", ContentIds.FolderRoot);

            File(ContentIds.File017, "employee_017.dat", "dat", ContentIds.FolderIntake, "EMPLOYEE 017\nSESSION ACTIVE\nDO NOT");
            File(ContentIds.FileLedger, "ledger_1994.dat", "dat", ContentIds.FolderIntake, "LEDGER 1994");
            File(ContentIds.FileCache, "cache_0412.tmp", "tmp", ContentIds.FolderIntake, "~~~~");
            File(ContentIds.FileBatchA, "batch_a.dat", "dat", ContentIds.FolderIntake, "BATCH A");
            File(ContentIds.FileBatchB, "batch_b.dat", "dat", ContentIds.FolderIntake, "BATCH B");
            File(ContentIds.FileBatchC, "batch_c.dat", "dat", ContentIds.FolderIntake, "BATCH C");

            fs.folders = folders.ToArray();
            fs.files = files.ToArray();

            var mails = new List<EmailData>(Emails.emails);
            void Mail(string id, string subject, bool preload)
            {
                if (mails.Exists(m => m != null && m.id == id)) return;
                mails.Add(new EmailData { id = id, from = "Records Supervisor", to = "Night Operator", subject = subject, date = "", body = subject, preload = preload });
                Problems.Add("Missing email '" + id + "' (placeholder created)");
            }
            Mail(ContentIds.MailWelcome, "Shift briefing", true);
            Mail(ContentIds.MailIt, "Scheduled maintenance tonight", false);
            Mail(ContentIds.MailUrgent, "URGENT: shred employee_017.dat", false);
            Mail(ContentIds.MailSupervisorCheck, "Is it done?", false);
            Mail(ContentIds.MailNoSender, "", false);
            Emails.emails = mails.ToArray();

            var orders = new List<WorkOrderData>(WorkOrders.orders);
            void Order(string id, string correct)
            {
                if (orders.Exists(o => o != null && o.id == id)) return;
                orders.Add(new WorkOrderData { id = id, title = "Work order " + id, correct = correct, fields = Array.Empty<WorkOrderField>() });
                Problems.Add("Missing work order '" + id + "' (placeholder created)");
            }
            Order(ContentIds.Order3317, "approve");
            Order(ContentIds.Order3318, "reject");
            WorkOrders.orders = orders.ToArray();

            var tasks = new List<TaskData>(Tasks.tasks);
            void Task(string id, string title, string type, string param, params string[] targets)
            {
                if (tasks.Exists(t => t != null && t.id == id)) return;
                tasks.Add(new TaskData { id = id, title = title, type = type, param = param, targets = targets, description = title, hint = "" });
                Problems.Add("Missing task '" + id + "' (placeholder created)");
            }
            Task(ContentIds.TaskReadBriefing, "Read the shift briefing", "ReadEmail", "", ContentIds.MailWelcome);
            Task(ContentIds.TaskArchiveLedger, "Archive the ledger", "MoveFile", ContentIds.FolderArchive, ContentIds.FileLedger);
            Task(ContentIds.TaskVerify3317, "Verify work order 3317", "DecideOrder", "", ContentIds.Order3317);
            Task(ContentIds.TaskVerify3318, "Verify work order 3318", "DecideOrder", "", ContentIds.Order3318);
            Task(ContentIds.TaskShredCache, "Shred the temp file", "DeleteFile", "", ContentIds.FileCache);
            Task(ContentIds.TaskArchiveBatch, "Archive the batch", "MoveFile", ContentIds.FolderArchive,
                ContentIds.FileBatchA, ContentIds.FileBatchB, ContentIds.FileBatchC);
            Task(ContentIds.TaskShred017, "Shred employee_017.dat", "DeleteFile", "", ContentIds.File017);
            Tasks.tasks = tasks.ToArray();

            var exchanges = new List<ExchangeData>(Dialogue.exchanges);
            if (!exchanges.Exists(x => x != null && x.id == ContentIds.ExchangeStop))
            {
                exchanges.Insert(0, new ExchangeData
                {
                    id = ContentIds.ExchangeStop, entityLines = new[] { "STOP" }, fallback = new[] { "DONT" },
                    silence = new[] { "PLEASE" }, responses = Array.Empty<ResponseData>(), next = ""
                });
                Problems.Add("Missing dialogue exchange 'ex_stop' (placeholder created)");
            }
            Dialogue.exchanges = exchanges.ToArray();

            var cams = new List<CameraData>(Story.cameras);
            void Cam(string id, string label)
            {
                if (cams.Exists(c => c != null && c.id == id)) return;
                cams.Add(new CameraData { id = id, label = label, location = label });
                Problems.Add("Missing camera '" + id + "' (placeholder created)");
            }
            Cam(ContentIds.Cam01, "CAM 01 - LOBBY");
            Cam(ContentIds.Cam02, "CAM 02 - CORRIDOR");
            Cam(ContentIds.Cam03, "CAM 03 - OFFICE 4");
            Cam(ContentIds.Cam04, "CAM 04 - SERVER ROOM");
            Story.cameras = cams.ToArray();

            if (Story.biosLines.Length == 0)
                Story.biosLines = new[] { "NEXUS BIOS v2.11", "Memory Test: 32768K OK", "Detecting drives... WS-04", "Booting NEXUS OS..." };
            if (Story.endingLines.Length == 0)
                Story.endingLines = new[] { "I TRIED", "SEE YOU TOMORROW NIGHT" };
        }
    }

    /// <summary>
    /// Built-in fallback strings for every key the code uses. strings.json overrides these. This list is
    /// also the documentation of which string keys exist.
    /// </summary>
    public static class DefaultStrings
    {
        public static readonly Dictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "company.name", "Halverson Data Reclamation" },
            { "company.short", "HALVERSON" },
            { "company.tagline", "Nothing is ever truly lost." },
            { "os.name", "NEXUS OS" },
            { "os.version", "4.1" },
            { "os.vendor", "Nexus Systems Corp." },
            { "login.username", "NIGHT_OP" },
            { "login.domain", "HDR-NET" },
            { "login.welcome", "Welcome" },
            { "app.mail", "Mail" },
            { "app.files", "Files" },
            { "app.notepad", "Jotter" },
            { "app.staff", "Staff Directory" },
            { "app.camera", "SecureView" },
            { "app.workorders", "Work Orders" },
            { "app.workqueue", "Work Queue" },
            { "app.disposal", "Disposal" },
            { "app.workstation", "Workstation" },
            { "app.help", "Help" },
            { "app.dataviewer", "Data Viewer" },
            { "start.programs", "Programs" },
            { "start.documents", "Documents" },
            { "start.help", "Help" },
            { "start.shutdown", "Shut Down..." },
            { "shutdown.denied.title", "Shut Down" },
            { "shutdown.denied.body", "You cannot shut down this workstation during an active shift." },
            { "camera.denied.title", "Access Denied" },
            { "camera.denied.body", "SecureView requires Security clearance level 3.\nContact your supervisor." },
            { "camera.nosignal", "NO SIGNAL" },
            { "shred.confirm.title", "Confirm Shred" },
            { "shred.confirm.body", "Permanently shred {0}?\nThis cannot be undone." },
            { "shred.progress.title", "Shredding" },
            { "shred.progress.body", "Shredding {0}..." },
            { "shred.done.body", "{0} was shredded." },
            { "error.inuse.title", "Cannot Shred File" },
            { "error.inuse.body", "{0} is in use by another user." },
            { "restricted.denied.title", "Access Denied" },
            { "restricted.denied.body", "You do not have permission to open this folder." },
            { "notify.newmail", "You have {0} new message(s)." },
            { "notify.newtask", "New task in your Work Queue." },
            { "workqueue.header", "Tonight's work queue" },
            { "workqueue.empty", "No tasks assigned." },
            { "help.body", "Double-click an icon to open it.\nDrag a window's title bar to move it.\nDrag files onto folders or onto the Disposal bin.\nRight-click a file for more options." },
            { "disclaimer.body", "SECOND CURSOR is a work of fiction.\nIt does not read your files, move your real mouse,\nor use your camera. Everything happens inside the game." },
            { "end.card.title", "SECOND CURSOR" },
            { "end.card.subtitle", "The shift is not over." },
            { "end.card.cta", "WISHLIST NOW" },
            { "end.card.thanks", "Thank you for playing the prototype." },
            { "pause.title", "Paused" },
        };
    }
}
