using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;

namespace SecondCursor.Core.FileSystem
{
    /// <summary>Who caused an action. Every OS mutation is attributed so the entity/story can react.</summary>
    public enum Actor { System, Player, Entity }

    public enum FileKind { Text, Data, Log, Temp, Config, Executable, Unknown }

    public sealed class VFolder
    {
        public string Id;
        public string Name;
        public string ParentId;
        public bool Hidden;
        public bool Locked;
    }

    public sealed class VFile
    {
        public string Id;
        public string Name;
        public string Extension;
        public FileKind Kind;
        public string FolderId;
        public string Size;
        public string Modified;
        public string Content;
        public bool Hidden;
        public bool Protected;
        public bool Corrupted;
        public bool Shredded;
        public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasTag(string tag) => Tags.Contains(tag);
        public override string ToString() => Name;
    }

    /// <summary>
    /// The fictional machine's file system. Files and folders are addressed by stable IDs (never by UI
    /// position) so both the player UI and the entity can reference "employee_017" wherever it currently is.
    /// Shredding marks a file as gone but keeps the object so the story can bring it back.
    /// </summary>
    public sealed class VirtualFileSystem
    {
        readonly Dictionary<string, VFolder> _folders = new Dictionary<string, VFolder>(StringComparer.Ordinal);
        readonly Dictionary<string, VFile> _files = new Dictionary<string, VFile>(StringComparer.Ordinal);
        readonly List<string> _folderOrder = new List<string>();
        readonly List<string> _fileOrder = new List<string>();

        public event Action<VFile, string, string, Actor> FileMoved;   // file, fromFolder, toFolder, actor
        public event Action<VFile, Actor> FileShredded;
        public event Action<VFile, Actor> FileRestored;
        public event Action<VFile, Actor> FileCreated;
        public event Action<VFile> FileChanged;

        /// <summary>Increments on every mutation; UI views compare against it to know when to refresh.</summary>
        public int Revision { get; private set; }

        public VirtualFileSystem() { }

        public VirtualFileSystem(FileSystemData data)
        {
            Load(data);
        }

        public void Load(FileSystemData data)
        {
            _folders.Clear();
            _files.Clear();
            _folderOrder.Clear();
            _fileOrder.Clear();
            if (data == null) return;
            foreach (var f in data.folders)
            {
                if (f == null || f.removed || string.IsNullOrEmpty(f.id) || _folders.ContainsKey(f.id)) continue;
                _folders[f.id] = new VFolder { Id = f.id, Name = f.name, ParentId = f.parent ?? "", Hidden = f.hidden, Locked = f.locked };
                _folderOrder.Add(f.id);
            }
            foreach (var d in data.files)
            {
                if (d == null || d.removed || string.IsNullOrEmpty(d.id) || _files.ContainsKey(d.id)) continue;
                var file = FromData(d);
                if (!_folders.ContainsKey(file.FolderId))
                    file.FolderId = _folders.ContainsKey(ContentIds.FolderIntake) ? ContentIds.FolderIntake : FirstFolderId();
                _files[file.Id] = file;
                _fileOrder.Add(file.Id);
            }
            Revision++;
        }

        static VFile FromData(FileData d)
        {
            var file = new VFile
            {
                Id = d.id,
                Name = string.IsNullOrEmpty(d.name) ? d.id : d.name,
                Extension = (d.type ?? "txt").ToLowerInvariant(),
                FolderId = d.folder ?? "",
                Size = string.IsNullOrEmpty(d.size) ? "1 KB" : d.size,
                Modified = d.modified ?? "",
                Content = d.content ?? "",
                Hidden = d.hidden,
                Protected = d.@protected,
                Corrupted = d.corrupted,
            };
            file.Kind = KindFromExtension(file.Extension);
            if (d.tags != null) foreach (var t in d.tags) if (!string.IsNullOrEmpty(t)) file.Tags.Add(t);
            return file;
        }

        public static FileKind KindFromExtension(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case "txt": return FileKind.Text;
                case "dat": return FileKind.Data;
                case "log": return FileKind.Log;
                case "tmp": return FileKind.Temp;
                case "cfg": case "ini": return FileKind.Config;
                case "exe": case "com": return FileKind.Executable;
                default: return FileKind.Unknown;
            }
        }

        string FirstFolderId() => _folderOrder.Count > 0 ? _folderOrder[0] : "";

        // ---------------------------------------------------------------- queries

        public VFolder GetFolder(string id) => id != null && _folders.TryGetValue(id, out var f) ? f : null;
        public VFile GetFile(string id) => id != null && _files.TryGetValue(id, out var f) ? f : null;
        public bool Exists(string fileId) { var f = GetFile(fileId); return f != null && !f.Shredded; }

        public IEnumerable<VFile> AllFiles
        {
            get { foreach (var id in _fileOrder) yield return _files[id]; }
        }

        public IEnumerable<VFolder> AllFolders
        {
            get { foreach (var id in _folderOrder) yield return _folders[id]; }
        }

        /// <summary>Live (non-shredded) files in a folder, sorted by name for a stable listing.</summary>
        public List<VFile> FilesIn(string folderId, bool includeHidden = false)
        {
            var list = new List<VFile>();
            foreach (var id in _fileOrder)
            {
                var f = _files[id];
                if (f.Shredded || f.FolderId != folderId) continue;
                if (f.Hidden && !includeHidden) continue;
                list.Add(f);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public List<VFolder> SubFolders(string parentId, bool includeHidden = false)
        {
            var list = new List<VFolder>();
            foreach (var id in _folderOrder)
            {
                var f = _folders[id];
                if (f.ParentId != parentId) continue;
                if (f.Hidden && !includeHidden) continue;
                list.Add(f);
            }
            return list;
        }

        public string FolderOf(string fileId)
        {
            var f = GetFile(fileId);
            return f == null || f.Shredded ? null : f.FolderId;
        }

        public string PathOf(VFolder folder)
        {
            if (folder == null) return "";
            var parts = new List<string>();
            var cur = folder;
            int guard = 0;
            while (cur != null && guard++ < 32)
            {
                parts.Insert(0, cur.Name);
                cur = GetFolder(cur.ParentId);
            }
            return string.Join("\\", parts);
        }

        public string PathOf(VFile file) => file == null ? "" : PathOf(GetFolder(file.FolderId)) + "\\" + file.Name;

        public bool IsInsideLocked(string folderId)
        {
            var cur = GetFolder(folderId);
            int guard = 0;
            while (cur != null && guard++ < 32)
            {
                if (cur.Locked) return true;
                cur = GetFolder(cur.ParentId);
            }
            return false;
        }

        // ---------------------------------------------------------------- mutations

        public bool CanMove(string fileId, string folderId, out string reason)
        {
            reason = null;
            var file = GetFile(fileId);
            if (file == null || file.Shredded) { reason = "missing"; return false; }
            var folder = GetFolder(folderId);
            if (folder == null) { reason = "no folder"; return false; }
            if (folderId == ContentIds.FolderDisposal) { reason = "use shred"; return false; }
            if (file.FolderId == folderId) { reason = "same folder"; return false; }
            if (folder.Locked) { reason = "locked"; return false; }
            return true;
        }

        public bool Move(string fileId, string folderId, Actor actor)
        {
            if (!CanMove(fileId, folderId, out _)) return false;
            var file = _files[fileId];
            string from = file.FolderId;
            file.FolderId = folderId;
            Revision++;
            GameLog.Info(actor == Actor.Entity ? LogChannel.Entity : LogChannel.OS,
                (actor == Actor.Player ? "Player" : actor.ToString()) + " moved " + file.Name + " " + from + " -> " + folderId);
            FileMoved?.Invoke(file, from, folderId, actor);
            return true;
        }

        public bool Shred(string fileId, Actor actor)
        {
            var file = GetFile(fileId);
            if (file == null || file.Shredded) return false;
            file.Shredded = true;
            Revision++;
            GameLog.Info(actor == Actor.Entity ? LogChannel.Entity : LogChannel.OS, "Shredded " + file.Name + " (" + actor + ")");
            FileShredded?.Invoke(file, actor);
            return true;
        }

        /// <summary>Brings a shredded file back (the file "returns").</summary>
        public bool Restore(string fileId, string folderId, Actor actor)
        {
            var file = GetFile(fileId);
            if (file == null || !file.Shredded) return false;
            file.Shredded = false;
            if (GetFolder(folderId) != null) file.FolderId = folderId;
            Revision++;
            GameLog.Info(LogChannel.OS, "Restored " + file.Name + " to " + file.FolderId + " (" + actor + ")");
            FileRestored?.Invoke(file, actor);
            return true;
        }

        public VFile CreateFile(string id, string name, string extension, string folderId, string content, Actor actor)
        {
            if (string.IsNullOrEmpty(id) || _files.ContainsKey(id)) return GetFile(id);
            var file = new VFile
            {
                Id = id, Name = name, Extension = extension ?? "txt", FolderId = folderId, Size = "1 KB",
                Modified = "", Content = content ?? ""
            };
            file.Kind = KindFromExtension(file.Extension);
            _files[id] = file;
            _fileOrder.Add(id);
            Revision++;
            FileCreated?.Invoke(file, actor);
            return file;
        }

        public void SetHidden(string fileId, bool hidden) => Mutate(fileId, f => f.Hidden = hidden);
        public void SetCorrupted(string fileId, bool corrupted) => Mutate(fileId, f => f.Corrupted = corrupted);
        public void Rename(string fileId, string newName) => Mutate(fileId, f => f.Name = newName);
        public void SetContent(string fileId, string content) => Mutate(fileId, f => f.Content = content ?? "");

        public void SetFolderLocked(string folderId, bool locked)
        {
            var f = GetFolder(folderId);
            if (f == null || f.Locked == locked) return;
            f.Locked = locked;
            Revision++;
        }

        void Mutate(string fileId, Action<VFile> change)
        {
            var file = GetFile(fileId);
            if (file == null) return;
            change(file);
            Revision++;
            FileChanged?.Invoke(file);
        }
    }
}
