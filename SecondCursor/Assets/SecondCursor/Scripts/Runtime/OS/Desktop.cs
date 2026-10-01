using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.OS
{
    /// <summary>A desktop icon: an app shortcut or a file living in the Desktop folder.</summary>
    public sealed class DesktopIcon : MonoBehaviour
    {
        public const int CellW = 74;
        public const int CellH = 60;

        public string AppId { get; private set; }
        public string FileId { get; private set; }
        public bool IsFile => FileId != null;
        public Interactable Hit { get; private set; }
        public RectTransform Rect { get; private set; }

        Image _icon;
        PixelText _label;
        Image _labelBg;
        bool _selected;
        bool _dimmed;

        internal static DesktopIcon Create(RectTransform parent, string label, string sprite, string appId, string fileId)
        {
            var rt = UIBuilder.Rect("Icon " + label, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(CellW, CellH);
            var icon = rt.gameObject.AddComponent<DesktopIcon>();
            icon.Rect = rt;
            icon.AppId = appId;
            icon.FileId = fileId;

            var holder = UIBuilder.Rect("Glyph", rt).At((CellW - 32) / 2, 2, 32, 32);
            icon._icon = UIBuilder.Icon(holder, sprite, 2);
            icon._labelBg = UIBuilder.Solid(rt, Palette.Selection, "Label Background");
            icon._labelBg.enabled = false;
            icon._label = UIBuilder.Text(rt, label, Palette.DesktopLabel);
            icon._label.Wrap = true;
            icon._label.Align = TextAlign.Center;
            icon._label.Shadow = true;
            icon._label.ShadowColor = Palette.DesktopLabelShadow;
            icon._label.rectTransform.At(0, 37, CellW, 24);
            icon.LayoutLabel();

            icon.Hit = UIBuilder.Hit(rt.gameObject, fileId != null ? "file:" + fileId : "app:" + appId);
            icon.Hit.draggable = true;
            icon.Hit.Tag = icon;
            return icon;
        }

        public string Label => _label.text;
        public string Sprite { get; private set; }

        public void SetSprite(string sprite)
        {
            Sprite = sprite;
            UIBuilder.SetIcon(_icon, sprite, 2);
        }

        public void SetLabel(string label)
        {
            _label.text = label;
            LayoutLabel();
        }

        void LayoutLabel()
        {
            var size = PixelFont.Measure(_label.text, CellW, false, 1);
            int w = Mathf.Min(CellW, size.x + 4);
            _labelBg.rectTransform.At((CellW - w) / 2, 36, w, size.y + 3);
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                _labelBg.enabled = value;
                _label.Shadow = !value;
                _label.color = value ? Palette.SelectionText : Palette.DesktopLabel;
                Refresh();
            }
        }

        public bool Dimmed
        {
            get => _dimmed;
            set { _dimmed = value; Refresh(); }
        }

        void Refresh()
        {
            Color c = _selected ? new Color(0.55f, 0.78f, 0.74f, 1f) : Color.white;
            if (_dimmed) c.a = 0.45f;
            _icon.color = c;
        }

        /// <summary>Desktop coordinates (x from left, y from top).</summary>
        public Vector2 TopLeft
        {
            get => new Vector2(Rect.anchoredPosition.x, -Rect.anchoredPosition.y);
            set => Rect.anchoredPosition = new Vector2(Mathf.Round(value.x), -Mathf.Round(value.y));
        }

        /// <summary>World-space top-left of the 32x32 glyph.</summary>
        public Vector2 GlyphWorldTopLeft
        {
            get
            {
                var r = ((RectTransform)_icon.transform.parent).WorldRect();
                return new Vector2(r.xMin, r.yMax);
            }
        }

        public Rect GlyphWorldRect => ((RectTransform)_icon.transform.parent).WorldRect();
    }

    /// <summary>
    /// The desktop: tiled wallpaper with the company watermark, app shortcuts, files in the Desktop folder
    /// (kept in sync with the virtual file system), selection, drag-and-drop and the Disposal bin.
    /// </summary>
    public sealed class Desktop : MonoBehaviour
    {
        GameServices _g;
        RectTransform _root;
        RectTransform _iconLayer;
        readonly List<DesktopIcon> _icons = new List<DesktopIcon>();
        readonly Dictionary<string, DesktopIcon> _fileIcons = new Dictionary<string, DesktopIcon>();
        readonly Dictionary<string, Vector2> _filePositions = new Dictionary<string, Vector2>();
        int _fsRevision = -1;
        bool _binFull;

        public Interactable Background { get; private set; }
        public DesktopIcon DisposalIcon { get; private set; }
        public IReadOnlyList<DesktopIcon> Icons => _icons;
        public RectTransform Root => _root;

        public static Desktop Create(GameServices g)
        {
            var root = g.Layers.Desktop;
            var d = root.gameObject.AddComponent<Desktop>();
            d._g = g;
            d._root = root;

            var pattern = SpriteLibrary.TextureOf("pattern_desktop");
            pattern.wrapMode = TextureWrapMode.Repeat;
            var bg = UIBuilder.Raw(root, pattern, "Wallpaper");
            bg.rectTransform.Stretch();
            var ps = SpriteLibrary.Size("pattern_desktop");
            bg.uvRect = new Rect(0f, 0f, ScreenRig.Width / (float)Mathf.Max(1, ps.x), ScreenRig.Height / (float)Mathf.Max(1, ps.y));

            // Faint company watermark in the middle of the desktop.
            var mark = UIBuilder.Rect("Watermark", root).At(ScreenRig.Width / 2 - 64, 150, 128, 170);
            var logo = UIBuilder.Icon(mark, "logo_company", 4);
            logo.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            logo.color = new Color(1f, 1f, 1f, 0.07f);
            var name = UIBuilder.Text(mark, g.Content.Text("company.name").ToUpperInvariant(), Palette.DesktopLabel);
            name.rectTransform.At(-100, 136, 328, 12);
            name.Align = TextAlign.Center;
            name.color = new Color(0.95f, 0.94f, 0.9f, 0.10f);

            d.Background = UIBuilder.Hit(bg.gameObject, "desktop");
            d.Background.Click += (a, n) => d.SelectOnly(null);
            d.Background.RightClick += d.ShowDesktopMenu;
            d.Background.AcceptsDrop = (a, p) => p.Kind == PayloadKind.Other || (p.Kind == PayloadKind.File && g.Files.Exists(p.FileId));
            d.Background.Drop += d.OnDropOnDesktop;

            d._iconLayer = UIBuilder.Rect("Icons", root).Stretch();

            var c = g.Content;
            int y = 8;
            d.AddApp(AppIds.Workstation, c.Text("app.workstation"), "icon_workstation", 6, y); y += DesktopIcon.CellH + 2;
            d._mailIcon = d.AddApp(AppIds.Mail, c.Text("app.mail"), "icon_mail", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.WorkQueue, c.Text("app.workqueue"), "icon_workorders", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.WorkOrders, c.Text("app.workorders"), "icon_file_log", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.Staff, c.Text("app.staff"), "icon_staff", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.Notepad, c.Text("app.notepad"), "icon_notepad", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.Camera, c.Text("app.camera"), "icon_camera", 6, y); y += DesktopIcon.CellH + 2;
            d.AddApp(AppIds.Help, c.Text("app.help"), "icon_help", 6, y);

            d.DisposalIcon = d.AddApp(AppIds.Disposal, c.Text("app.disposal"), "icon_disposal_empty",
                ScreenRig.Width - DesktopIcon.CellW - 8, ScreenRig.Height - WindowManager.TaskbarHeight - DesktopIcon.CellH - 8);
            d.DisposalIcon.Hit.AcceptsDrop = (a, p) => p.Kind == PayloadKind.File && g.Files.Exists(p.FileId);
            d.DisposalIcon.Hit.Drop += (a, p) => g.Shred.Request(p.FileId, a);
            d.DisposalIcon.Hit.DropHover += (a, p, entering) => d.DisposalIcon.Selected = entering;

            g.DragDrop.PayloadFinished += (p, accepted, by) => d.UndimAll();
            return d;
        }

        DesktopIcon AddApp(string appId, string label, string sprite, int x, int y)
        {
            var icon = DesktopIcon.Create(_iconLayer, label, sprite, appId, null);
            icon.SetSprite(sprite);
            icon.TopLeft = new Vector2(x, y);
            Wire(icon);
            _icons.Add(icon);
            return icon;
        }

        void Wire(DesktopIcon icon)
        {
            icon.Hit.Click += (a, n) =>
            {
                SelectOnly(icon);
                if (n == 2) Open(icon, a);
            };
            icon.Hit.RightClick += a =>
            {
                SelectOnly(icon);
                ShowIconMenu(icon, a);
            };
            icon.Hit.DragBegin += a =>
            {
                SelectOnly(icon);
                var kind = icon.IsFile ? PayloadKind.File : PayloadKind.Other;
                string id = icon.IsFile ? icon.FileId : "app:" + icon.AppId;
                _g.DragDrop.BeginDrag(a, kind, id, icon.Label, icon.Sprite, icon.Hit, icon.GlyphWorldTopLeft);
                icon.Dimmed = true;
            };
        }

        void Open(DesktopIcon icon, CursorAgent a)
        {
            if (icon.IsFile) _g.Apps.OpenFile(icon.FileId, a, icon.GlyphWorldRect);
            else _g.Apps.Launch(icon.AppId, a, icon.GlyphWorldRect);
        }

        public void SelectOnly(DesktopIcon icon)
        {
            foreach (var i in _icons) if (i != null) i.Selected = i == icon;
        }

        void UndimAll()
        {
            foreach (var i in _icons) if (i != null) i.Dimmed = false;
        }

        public DesktopIcon IconForFile(string fileId) => fileId != null && _fileIcons.TryGetValue(fileId, out var i) ? i : null;

        DesktopIcon _attention;
        float _attentionUntil;

        /// <summary>Blink a file's icon for a moment so it reads even in a small video.</summary>
        public void Attention(string fileId, float seconds = 1.2f)
        {
            _attention = IconForFile(fileId);
            _attentionUntil = Time.time + seconds;
        }

        DesktopIcon _mailIcon;
        int _mailRevision = -1;

        /// <summary>Phase H: the Mail icon shows an unread envelope while anything in the inbox is unread.</summary>
        void UpdateMailIcon()
        {
            if (_mailIcon == null || _g.Mail == null || _mailRevision == _g.Mail.Revision) return;
            _mailRevision = _g.Mail.Revision;
            _mailIcon.SetSprite(_g.Mail.UnreadCount > 0 ? "icon_mail_unread" : "icon_mail");
        }

        void LateUpdate()
        {
            UpdateMailIcon();
            if (_attention == null) return;
            if (Time.time >= _attentionUntil)
            {
                _attention.Selected = false;
                _attention = null;
                return;
            }
            _attention.Selected = (Time.time * 6f) % 1f < 0.5f;
        }

        /// <summary>Phase Q1: the player dragged a program's icon to a new place (the icon, where it was before).</summary>
        public event Action<DesktopIcon, Vector2> AppIconMovedByPlayer;

        public DesktopIcon IconForApp(string appId)
        {
            foreach (var i in _icons) if (i != null && !i.IsFile && i.AppId == appId) return i;
            return null;
        }

        void OnDropOnDesktop(CursorAgent a, DragPayload p)
        {
            Vector2 world = p.GhostPosition;
            var desktop = OSLayers.WorldToDesktop(world);
            var topLeft = new Vector2(desktop.x - (DesktopIcon.CellW - 32) / 2f, desktop.y - 2f);
            topLeft = new Vector2(Mathf.Clamp(topLeft.x, 0, ScreenRig.Width - DesktopIcon.CellW), Mathf.Clamp(topLeft.y, 0, ScreenRig.Height - WindowManager.TaskbarHeight - DesktopIcon.CellH));

            if (p.Kind == PayloadKind.Other && p.FileId != null && p.FileId.StartsWith("app:"))
            {
                var icon = IconForApp(p.FileId.Substring(4));
                if (icon == null) return;
                var from = icon.TopLeft;
                icon.TopLeft = topLeft;
                if (a != null && a.IsPlayer && from != icon.TopLeft) AppIconMovedByPlayer?.Invoke(icon, from);
                return;
            }
            if (p.Kind != PayloadKind.File) return;
            _filePositions[p.FileId] = topLeft;
            var actor = a.IsEntity ? Actor.Entity : Actor.Player;
            if (_g.Files.FolderOf(p.FileId) != ContentIds.FolderDesktop) _g.Files.Move(p.FileId, ContentIds.FolderDesktop, actor, a.IsEntity ? SystemNotices.SessionOf(_g, a) : null);
            var existing = IconForFile(p.FileId);
            if (existing != null) existing.TopLeft = topLeft;
        }

        /// <summary>Put a file icon at a specific place (e.g. where the entity dropped it).</summary>
        public void SetFilePosition(string fileId, Vector2 desktopTopLeft)
        {
            _filePositions[fileId] = desktopTopLeft;
            var icon = IconForFile(fileId);
            if (icon != null) icon.TopLeft = desktopTopLeft;
        }

        void Update()
        {
            if (_g == null) return;
            if (_fsRevision != _g.Files.Revision || _binFull != _g.Shred.AnyShredded)
            {
                _fsRevision = _g.Files.Revision;
                _binFull = _g.Shred.AnyShredded;
                SyncFiles();
                DisposalIcon.SetSprite(_g.Shred.AnyShredded ? "icon_disposal_full" : "icon_disposal_empty");
            }
        }

        void SyncFiles()
        {
            var files = _g.Files.FilesIn(ContentIds.FolderDesktop);
            var keep = new HashSet<string>();
            foreach (var f in files)
            {
                keep.Add(f.Id);
                if (_fileIcons.TryGetValue(f.Id, out var icon) && icon != null)
                {
                    icon.SetLabel(f.Name);
                    icon.SetSprite(FileIcons.SpriteFor(f));
                    continue;
                }
                icon = DesktopIcon.Create(_iconLayer, f.Name, FileIcons.SpriteFor(f), null, f.Id);
                icon.SetSprite(FileIcons.SpriteFor(f));
                icon.TopLeft = _filePositions.TryGetValue(f.Id, out var pos) ? pos : NextFreeSlot();
                _filePositions[f.Id] = icon.TopLeft;
                Wire(icon);
                _icons.Add(icon);
                _fileIcons[f.Id] = icon;
            }
            var remove = new List<string>();
            foreach (var kv in _fileIcons) if (!keep.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                var icon = _fileIcons[id];
                _fileIcons.Remove(id);
                _icons.Remove(icon);
                if (icon != null)
                {
                    icon.gameObject.SetActive(false);
                    Destroy(icon.gameObject);
                }
            }
        }

        Vector2 NextFreeSlot()
        {
            for (int col = 1; col < 8; col++)
            {
                for (int row = 0; row < 7; row++)
                {
                    var p = new Vector2(6 + col * (DesktopIcon.CellW + 6), 8 + row * (DesktopIcon.CellH + 2));
                    bool taken = false;
                    foreach (var i in _icons)
                    {
                        if (i == null) continue;
                        if (Vector2.Distance(i.TopLeft, p) < 30f) { taken = true; break; }
                    }
                    if (!taken) return p;
                }
            }
            return new Vector2(300, 200);
        }

        void ShowDesktopMenu(CursorAgent a)
        {
            if (!a.IsPlayer) return;
            var items = new List<MenuItem>
            {
                MenuItem.Of("Arrange Icons", x => ArrangeFiles()),
                MenuItem.Of("Refresh", x => _fsRevision = -1),
                MenuItem.Sep(),
                MenuItem.Of("Properties", null, enabled: false),
            };
            PopupMenu.Show(_g.Layers.Popups, a.Position, items, 130);
        }

        void ShowIconMenu(DesktopIcon icon, CursorAgent a)
        {
            if (!a.IsPlayer) return;
            var items = new List<MenuItem> { new MenuItem { Label = "Open", Bold = true, Action = x => Open(icon, x) } };
            if (icon.IsFile)
            {
                items.Add(MenuItem.Of("Shred", x => _g.Shred.Request(icon.FileId, x), "icon_disposal_empty"));
                items.Add(MenuItem.Sep());
                items.Add(MenuItem.Of("Properties", x => _g.Apps.ShowProperties(icon.FileId, x)));
            }
            PopupMenu.Show(_g.Layers.Popups, a.Position, items, 130);
        }

        void ArrangeFiles()
        {
            int i = 0;
            foreach (var kv in _fileIcons)
            {
                if (kv.Value == null) continue;
                var p = new Vector2(6 + (1 + i / 7) * (DesktopIcon.CellW + 6), 8 + (i % 7) * (DesktopIcon.CellH + 2));
                kv.Value.TopLeft = p;
                _filePositions[kv.Key] = p;
                i++;
            }
        }
    }

    /// <summary>Which icon a file gets.</summary>
    public static class FileIcons
    {
        public static string SpriteFor(VFile f)
        {
            if (f == null) return "icon_file_txt";
            if (f.Corrupted) return "icon_file_corrupt";
            switch (f.Kind)
            {
                case FileKind.Data: return "icon_file_dat";
                case FileKind.Log: return "icon_file_log";
                case FileKind.Temp: return "icon_file_tmp";
                case FileKind.Config: return "icon_file_cfg";
                case FileKind.Executable: return "icon_file_exe";
                default: return "icon_file_txt";
            }
        }

        public static string SpriteFor(VFolder f, VirtualFileSystem fs)
        {
            if (f == null) return "icon_folder";
            if (f.Id == ContentIds.FolderRoot) return "icon_drive";
            if (f.Id == ContentIds.FolderDisposal) return "icon_disposal_empty";
            if (f.Locked) return "icon_folder_locked";
            return "icon_folder";
        }
    }
}
