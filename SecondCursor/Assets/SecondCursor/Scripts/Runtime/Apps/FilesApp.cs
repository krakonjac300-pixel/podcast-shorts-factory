using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Tasks;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// File manager: folder list on the left, file list on the right, drag files onto folders to move
    /// them or onto the Disposal bin to shred. Rows are addressable as "file:&lt;id&gt;" and
    /// "folder:&lt;id&gt;" so the second cursor can find a file wherever it is shown.
    /// </summary>
    public sealed class FilesApp : App, IKeyboardTarget
    {
        string _folderId;
        ListView _folders;
        ListView _files;
        PixelText _address;
        PixelText _status;
        UiButton _up;
        Interactable _filesBackground;
        int _revision = -1;

        public override string AppId => AppIds.Files;
        public string FolderId => _folderId;

        public FilesApp(string folderId = ContentIds.FolderIntake)
        {
            _folderId = folderId;
        }

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            var win = CreateWindow(G.Content.Text("app.files"), "icon_folder_open", 200, 44, 540, 330, WindowFlags.Standard, zoomFrom);
            var client = win.Client;

            // Address bar
            _up = UiButton.CreateIcon(client, "glyph_arrow_up", a => GoUp(a), "files.up");
            ((RectTransform)_up.transform).At(2, 2, 22, 20);
            var addrFrame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Address");
            addrFrame.rectTransform.TopStrip(2, 20, 28, 2);
            _address = UIBuilder.Text(addrFrame.rectTransform, "", Palette.Text);
            _address.rectTransform.Stretch(5, 0, 4, 0);
            _address.VAlign = TextVAlign.Middle;

            // Folder pane
            _folders = new ListView(client, "Folders", new[] { 136 }, null, true);
            _folders.Root.anchorMin = new Vector2(0f, 0f);
            _folders.Root.anchorMax = new Vector2(0f, 1f);
            _folders.Root.pivot = new Vector2(0f, 1f);
            _folders.Root.offsetMin = new Vector2(2f, 20f);
            _folders.Root.offsetMax = new Vector2(140f, -26f);
            _folders.RowSelected += (row, a) => Navigate((string)row.Tag, a);

            // File pane
            _files = new ListView(client, "Files", new[] { 180, 60, 110 }, new[] { "Name", "Size", "Modified" }, true);
            _files.Root.Stretch(144, 26, 2, 20);
            _files.RowActivated += (row, a) => G.Apps.OpenFile((string)row.Tag, a);
            _files.RowRightClicked += (row, a) => FileMenu((string)row.Tag, a);
            _files.RowDragBegin += (row, a) =>
            {
                var file = G.Files.GetFile((string)row.Tag);
                if (file == null) return;
                var iconRect = row.Icon != null ? row.Icon.rectTransform.WorldRect() : row.Rect.WorldRect();
                G.DragDrop.BeginFileDrag(a, file.Id, file.Name, FileIcons.SpriteFor(file), row.Hit, new Vector2(iconRect.xMin - 8, iconRect.yMax + 8));
            };
            _filesBackground = _files.Scroll.Viewport.GetComponent<Interactable>();
            _filesBackground.AcceptsDrop = (a, p) => p.Kind == PayloadKind.File && CanDropInto(p.FileId, _folderId);
            _filesBackground.Drop += (a, p) => DropInto(p.FileId, _folderId, a);
            _filesBackground.Click += (a, n) => _files.Select(-1, a);

            // Status bar
            var status = UIBuilder.Bevel(client, BevelStyle.StatusField, "Status");
            status.rectTransform.BottomStrip(0, 18, 2, 2);
            _status = UIBuilder.Text(status.rectTransform, "", Palette.Text);
            _status.rectTransform.Stretch(4, 0, 4, 0);
            _status.VAlign = TextVAlign.Middle;

            Navigate(_folderId, by, true);
        }

        public bool CanDropInto(string fileId, string folderId)
        {
            if (folderId == ContentIds.FolderDisposal) return G.Files.Exists(fileId);
            return G.Files.CanMove(fileId, folderId, out _);
        }

        void DropInto(string fileId, string folderId, CursorAgent a)
        {
            if (folderId == ContentIds.FolderDisposal)
            {
                G.Shred.Request(fileId, a);
                return;
            }
            var folder = G.Files.GetFolder(folderId);
            if (folder != null && folder.Locked)
            {
                Denied(a);
                return;
            }
            G.Files.Move(fileId, folderId, a.IsEntity ? Actor.Entity : Actor.Player);
            G.Tasks.Evaluate();
        }

        public void Navigate(string folderId, CursorAgent by, bool force = false)
        {
            var folder = G.Files.GetFolder(folderId);
            if (folder == null) return;
            if (folder.Locked && by != null && by.IsPlayer && !force)
            {
                Denied(by);
                _folders.SelectWhere(r => (string)r.Tag == _folderId, null);
                return;
            }
            if (folderId == _folderId && _revision == G.Files.Revision && !force) return;
            _folderId = folderId;
            _revision = -1;
            Refresh();
        }

        void Denied(CursorAgent by)
        {
            var c = G.Content;
            Dialogs.Message(G, c.Text("restricted.denied.title"), c.Text("restricted.denied.body"), "icon_lock", new[] { "OK" }, null);
        }

        void GoUp(CursorAgent a)
        {
            var f = G.Files.GetFolder(_folderId);
            if (f != null && !string.IsNullOrEmpty(f.ParentId)) Navigate(f.ParentId, a);
        }

        void Refresh()
        {
            _revision = G.Files.Revision;
            var folder = G.Files.GetFolder(_folderId);
            Window.SetTitle(G.Content.Text("app.files") + " - " + (folder != null ? folder.Name : ""));
            _address.text = G.Files.PathOf(folder);

            // Folder list: the drive, then its folders (the Desktop folder is shown as its own entry).
            string selectedFile = _files.Selected != null ? (string)_files.Selected.Tag : null;
            _folders.Clear();
            var root = G.Files.GetFolder(ContentIds.FolderRoot);
            if (root != null) AddFolderRow(root);
            foreach (var sub in G.Files.SubFolders(ContentIds.FolderRoot)) AddFolderRow(sub);
            _folders.SelectWhere(r => (string)r.Tag == _folderId, null);

            _files.Clear();
            int count = 0;
            if (folder != null)
            {
                foreach (var sub in G.Files.SubFolders(folder.Id))
                {
                    var row = _files.AddRow(FileIcons.SpriteFor(sub, G.Files), "folder:" + sub.Id, "folder:" + sub.Id, sub.Name, "", "Folder");
                    row.Hit.Click += (a, n) => { if (n == 2) Navigate(sub.Id, a); };
                    row.Hit.AcceptsDrop = (a, p) => p.Kind == PayloadKind.File && CanDropInto(p.FileId, sub.Id);
                    row.Hit.Drop += (a, p) => DropInto(p.FileId, sub.Id, a);
                    count++;
                }
                foreach (var file in G.Files.FilesIn(folder.Id))
                {
                    var row = _files.AddRow(FileIcons.SpriteFor(file), file.Id, "file:" + file.Id, file.Name, file.Size, file.Modified);
                    row.Hit.draggable = true;
                    count++;
                }
            }
            if (selectedFile != null) _files.SelectWhere(r => (string)r.Tag == selectedFile, null);
            _up.Enabled = folder != null && !string.IsNullOrEmpty(folder.ParentId);
            if (_folderId == ContentIds.FolderDisposal)
                _status.text = "Shredded files cannot be recovered.";
            else
                _status.text = count + " object(s)";
        }

        void AddFolderRow(VFolder f)
        {
            string label = f.Id == ContentIds.FolderRoot ? f.Name : f.Name;
            var row = _folders.AddRow(FileIcons.SpriteFor(f, G.Files), f.Id, "folder:" + f.Id, label);
            row.Hit.AcceptsDrop = (a, p) => p.Kind == PayloadKind.File && CanDropInto(p.FileId, f.Id);
            row.Hit.Drop += (a, p) => DropInto(p.FileId, f.Id, a);
            row.Hit.DropHover += (a, p, entering) =>
            {
                row.Background.enabled = entering || _folders.Selected == row;
            };
        }

        /// <summary>Row showing a file in this window (null if not visible here).</summary>
        public ListView.Row RowFor(string fileId)
        {
            foreach (var r in _files.Rows) if (r.Tag is string s && s == fileId) return r;
            return null;
        }

        public void SelectFile(string fileId, CursorAgent by) => _files.SelectWhere(r => r.Tag is string s && s == fileId, by);

        /// <summary>The folder pane's row for a folder (a drop target a cursor can drag files onto), or null.</summary>
        public Interactable FolderRowFor(string folderId)
        {
            foreach (var r in _folders.Rows) if (r.Tag is string s && s == folderId) return r.Hit;
            return null;
        }

        public string SelectedFileId => _files.Selected?.Tag as string;

        void FileMenu(string tag, CursorAgent a)
        {
            if (!a.IsPlayer || tag == null) return;
            if (tag.StartsWith("folder:"))
            {
                string id = tag.Substring(7);
                PopupMenu.Show(G.Layers.Popups, a.Position, new List<MenuItem> { new MenuItem { Label = "Open", Bold = true, Action = x => Navigate(id, x) } }, 120);
                return;
            }
            var items = new List<MenuItem>
            {
                new MenuItem { Label = "Open", Bold = true, Action = x => G.Apps.OpenFile(tag, x) },
                MenuItem.Of("Shred", x => G.Shred.Request(tag, x), "icon_disposal_empty"),
                MenuItem.Sep(),
                MenuItem.Of("Properties", x => G.Apps.ShowProperties(tag, x)),
            };
            PopupMenu.Show(G.Layers.Popups, a.Position, items, 130);
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Files.Revision) Refresh();
            GuideTick();
        }

        // ------------------------------------------------------------------ tutorial guide

        /// <summary>First-shift file tasks: the file to drag and where it goes blink softly until it is done.</summary>
        static readonly HashSet<string> GuidedTasks = new HashSet<string>
        {
            ContentIds.TaskArchiveLedger, ContentIds.TaskShredCache, ContentIds.TaskArchiveBatch,
        };
        static readonly Color32 GuideColor = new Color32(0xFF, 0xE9, 0x9A, 0xFF);
        readonly List<ListView.Row> _guided = new List<ListView.Row>();

        void GuideTick()
        {
            // Put back whatever blinked last frame (rows may have been rebuilt since).
            foreach (var r in _guided)
            {
                if (r.Rect == null || r.Background == null) continue;
                r.Background.color = Palette.Selection;
                r.Background.enabled = r == _files.Selected || r == _folders.Selected;
            }
            _guided.Clear();

            var task = G.Tasks.Current;
            if (task == null || !GuidedTasks.Contains(task.Id) || G.Player.Payload != null) return;
            if ((Time.unscaledTime % 1.2f) > 0.6f) return;
            foreach (var target in task.Data.targets) Blink(RowFor(target), _files);
            string destination = task.Type == TaskType.MoveFile ? task.Data.param : ContentIds.FolderDisposal;
            foreach (var r in _folders.Rows)
                if (r.Tag is string id && id == destination) Blink(r, _folders);
        }

        void Blink(ListView.Row r, ListView list)
        {
            if (r == null || r.Rect == null || r.Background == null || r == list.Selected) return;
            r.Background.color = GuideColor;
            r.Background.enabled = true;
            _guided.Add(r);
        }

        public void OnTyped(string text, CursorAgent by) { }

        public void OnKey(GameKey key, CursorAgent by)
        {
            string sel = SelectedFileId;
            if (key == GameKey.Delete && sel != null && !sel.StartsWith("folder:")) G.Shred.Request(sel, by);
            else if (key == GameKey.Up && _files.Selected != null) _files.Select(Mathf.Max(0, _files.Selected.Index - 1), by);
            else if (key == GameKey.Down && _files.Selected != null) _files.Select(Mathf.Min(_files.Rows.Count - 1, _files.Selected.Index + 1), by);
        }
    }
}
