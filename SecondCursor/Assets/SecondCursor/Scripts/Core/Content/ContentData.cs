using System;

// Plain data classes mirroring the JSON files in Resources/Content. They follow Unity JsonUtility rules:
// [Serializable], public instance fields, arrays instead of lists/dictionaries, no nulls expected.
// Every class has Sanitize() so a partially-authored JSON file never produces null references.
namespace SecondCursor.Core.Content
{
    [Serializable]
    public class StringEntry
    {
        public string key = "";
        public string value = "";
    }

    [Serializable]
    public class StringTableData
    {
        public StringEntry[] entries = Array.Empty<StringEntry>();

        public void Sanitize()
        {
            entries = entries ?? Array.Empty<StringEntry>();
            foreach (var e in entries)
            {
                if (e == null) continue;
                e.key = e.key ?? "";
                e.value = e.value ?? "";
            }
        }
    }

    [Serializable]
    public class CameraData
    {
        public string id = "";
        public string label = "";
        public string location = "";
        /// <summary>The camera button only shows after a story flag (CAM 00).</summary>
        public bool hidden;
    }

    [Serializable]
    public class StoryData
    {
        public string[] biosLines = Array.Empty<string>();
        public string splashTagline = "";
        public CameraData[] cameras = Array.Empty<CameraData>();
        public string[] endingLines = Array.Empty<string>();
        public string[] anomalyNotes = Array.Empty<string>();

        public void Sanitize()
        {
            biosLines = Clean(biosLines);
            splashTagline = splashTagline ?? "";
            cameras = cameras ?? Array.Empty<CameraData>();
            foreach (var c in cameras)
            {
                if (c == null) continue;
                c.id = c.id ?? "";
                c.label = c.label ?? "";
                c.location = c.location ?? "";
            }
            endingLines = Clean(endingLines);
            anomalyNotes = Clean(anomalyNotes);
        }

        internal static string[] Clean(string[] a)
        {
            if (a == null) return Array.Empty<string>();
            for (int i = 0; i < a.Length; i++) a[i] = a[i] ?? "";
            return a;
        }
    }

    [Serializable]
    public class FolderData
    {
        public string id = "";
        public string name = "";
        public string parent = "";
        public bool hidden;
        public bool locked;
        /// <summary>Authorization code: a locked folder with a code opens the code prompt instead of Access Denied.</summary>
        public string code = "";
        /// <summary>A night overlay deletes this folder.</summary>
        public bool removed;
    }

    [Serializable]
    public class FileData
    {
        public string id = "";
        public string name = "";
        public string type = "txt";
        public string folder = "";
        public string size = "";
        public string modified = "";
        public string content = "";
        public bool hidden;
        public bool @protected;
        public bool corrupted;
        public string[] tags = Array.Empty<string>();
        /// <summary>A night overlay deletes this file.</summary>
        public bool removed;
    }

    [Serializable]
    public class FileSystemData
    {
        public FolderData[] folders = Array.Empty<FolderData>();
        public FileData[] files = Array.Empty<FileData>();
        /// <summary>Files a night overlay deleted (set by ContentOverlay, never authored): no placeholder replaces them.</summary>
        [NonSerialized] public string[] removedFiles = Array.Empty<string>();

        public void Sanitize()
        {
            folders = folders ?? Array.Empty<FolderData>();
            files = files ?? Array.Empty<FileData>();
            removedFiles = removedFiles ?? Array.Empty<string>();
            foreach (var f in folders)
            {
                if (f == null) continue;
                f.id = f.id ?? "";
                f.name = f.name ?? "";
                f.parent = f.parent ?? "";
                f.code = f.code ?? "";
            }
            foreach (var f in files)
            {
                if (f == null) continue;
                f.id = f.id ?? "";
                f.name = f.name ?? f.id;
                f.type = string.IsNullOrEmpty(f.type) ? "txt" : f.type;
                f.folder = f.folder ?? "";
                f.size = f.size ?? "";
                f.modified = f.modified ?? "";
                f.content = f.content ?? "";
                f.tags = StoryData.Clean(f.tags);
            }
        }
    }

    [Serializable]
    public class EmailData
    {
        public string id = "";
        public string from = "";
        public string to = "";
        public string subject = "";
        public string date = "";
        public string body = "";
        public bool preload;
        public bool read;
        /// <summary>A night overlay deletes this mail.</summary>
        public bool removed;
    }

    [Serializable]
    public class EmailsData
    {
        public EmailData[] emails = Array.Empty<EmailData>();

        public void Sanitize()
        {
            emails = emails ?? Array.Empty<EmailData>();
            foreach (var e in emails)
            {
                if (e == null) continue;
                e.id = e.id ?? "";
                e.from = e.from ?? "";
                e.to = e.to ?? "";
                e.subject = e.subject ?? "";
                e.date = e.date ?? "";
                e.body = e.body ?? "";
            }
        }
    }

    [Serializable]
    public class EmployeeData
    {
        public string id = "";
        public string number = "";
        public string name = "";
        public string department = "";
        public string position = "";
        public string status = "";
        public string office = "";
        public string hired = "";
        public string lastLogin = "";
        public string supervisor = "";
        public string notes = "";
        public string photo = "none";
        public bool restricted;
    }

    [Serializable]
    public class EmployeesData
    {
        public EmployeeData[] employees = Array.Empty<EmployeeData>();

        public void Sanitize()
        {
            employees = employees ?? Array.Empty<EmployeeData>();
            foreach (var e in employees)
            {
                if (e == null) continue;
                e.id = e.id ?? "";
                e.number = e.number ?? "";
                e.name = e.name ?? "";
                e.department = e.department ?? "";
                e.position = e.position ?? "";
                e.status = e.status ?? "";
                e.office = e.office ?? "";
                e.hired = e.hired ?? "";
                e.lastLogin = e.lastLogin ?? "";
                e.supervisor = e.supervisor ?? "";
                e.notes = e.notes ?? "";
                e.photo = string.IsNullOrEmpty(e.photo) ? "none" : e.photo;
            }
        }
    }

    [Serializable]
    public class WorkOrderField
    {
        public string label = "";
        public string value = "";
    }

    [Serializable]
    public class WorkOrderData
    {
        public string id = "";
        public string title = "";
        public WorkOrderField[] fields = Array.Empty<WorkOrderField>();
        public string instructions = "";
        public string correct = "approve";
        public string employeeRef = "";
        /// <summary>"" = Personnel rule (approve only if TERMINATED), "shelf" = the CAM 04 shelf rule.</summary>
        public string rule = "";
    }

    [Serializable]
    public class WorkOrdersData
    {
        public WorkOrderData[] orders = Array.Empty<WorkOrderData>();

        public void Sanitize()
        {
            orders = orders ?? Array.Empty<WorkOrderData>();
            foreach (var o in orders)
            {
                if (o == null) continue;
                o.id = o.id ?? "";
                o.title = o.title ?? "";
                o.fields = o.fields ?? Array.Empty<WorkOrderField>();
                foreach (var f in o.fields)
                {
                    if (f == null) continue;
                    f.label = f.label ?? "";
                    f.value = f.value ?? "";
                }
                o.instructions = o.instructions ?? "";
                o.correct = string.IsNullOrEmpty(o.correct) ? "approve" : o.correct;
                o.employeeRef = o.employeeRef ?? "";
                o.rule = o.rule ?? "";
            }
        }
    }

    [Serializable]
    public class TaskData
    {
        public string id = "";
        public string title = "";
        public string description = "";
        public string hint = "";
        public string type = "";
        public string[] targets = Array.Empty<string>();
        public string param = "";
        /// <summary>"" = the company, "entity" = written into the Work Queue by the second cursor.</summary>
        public string author = "";
        /// <summary>Seconds before an entity task is withdrawn (0 = no limit).</summary>
        public float timeout;
        /// <summary>Shown in the Work Queue, e.g. "3:00 AM".</summary>
        public string deadline = "";
    }

    [Serializable]
    public class TasksData
    {
        public TaskData[] tasks = Array.Empty<TaskData>();

        public void Sanitize()
        {
            tasks = tasks ?? Array.Empty<TaskData>();
            foreach (var t in tasks)
            {
                if (t == null) continue;
                t.id = t.id ?? "";
                t.title = t.title ?? "";
                t.description = t.description ?? "";
                t.hint = t.hint ?? "";
                t.type = t.type ?? "";
                t.targets = StoryData.Clean(t.targets);
                t.param = t.param ?? "";
                t.author = t.author ?? "";
                t.deadline = t.deadline ?? "";
            }
        }
    }

    [Serializable]
    public class ResponseData
    {
        public string[] keywords = Array.Empty<string>();
        public string[] reply = Array.Empty<string>();
        /// <summary>Story tag returned with the reply (stay, letgo, go, name, glasses, confirm, cancel).</summary>
        public string tag = "";
    }

    [Serializable]
    public class ExchangeData
    {
        public string id = "";
        public string[] entityLines = Array.Empty<string>();
        public ResponseData[] responses = Array.Empty<ResponseData>();
        public string[] fallback = Array.Empty<string>();
        public string[] silence = Array.Empty<string>();
        public string next = "";
        /// <summary>"" = Ellen (the second cursor), "gary" = Gary.</summary>
        public string voice = "";
    }

    /// <summary>A named group of lines (endings, memory lines), optionally with a speaker per line.</summary>
    [Serializable]
    public class LineSetData
    {
        public string id = "";
        public string voice = "";
        public string[] lines = Array.Empty<string>();
        public string[] speakers = Array.Empty<string>();

        public void Sanitize()
        {
            id = id ?? "";
            voice = voice ?? "";
            lines = StoryData.Clean(lines);
            speakers = StoryData.Clean(speakers);
        }
    }

    [Serializable]
    public class DialogueData
    {
        public ExchangeData[] exchanges = Array.Empty<ExchangeData>();
        public string[] panicLines = Array.Empty<string>();
        public string[] cameraLines = Array.Empty<string>();
        public string[] recordLines = Array.Empty<string>();
        public LineSetData[] lineSets = Array.Empty<LineSetData>();

        public void Sanitize()
        {
            exchanges = exchanges ?? Array.Empty<ExchangeData>();
            foreach (var x in exchanges)
            {
                if (x == null) continue;
                x.id = x.id ?? "";
                x.entityLines = StoryData.Clean(x.entityLines);
                x.responses = x.responses ?? Array.Empty<ResponseData>();
                foreach (var r in x.responses)
                {
                    if (r == null) continue;
                    r.keywords = StoryData.Clean(r.keywords);
                    r.reply = StoryData.Clean(r.reply);
                    r.tag = r.tag ?? "";
                }
                x.fallback = StoryData.Clean(x.fallback);
                x.silence = StoryData.Clean(x.silence);
                x.next = x.next ?? "";
                x.voice = x.voice ?? "";
            }
            lineSets = lineSets ?? Array.Empty<LineSetData>();
            foreach (var l in lineSets) l?.Sanitize();
            panicLines = StoryData.Clean(panicLines);
            cameraLines = StoryData.Clean(cameraLines);
            recordLines = StoryData.Clean(recordLines);
        }
    }
}
