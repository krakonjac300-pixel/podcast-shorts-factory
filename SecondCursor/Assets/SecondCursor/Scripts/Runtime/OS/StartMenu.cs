using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>The NEXUS menu: a header with the operator's name, the shift's programs, Help and Shut Down.</summary>
    public sealed class StartMenu
    {
        readonly GameServices _g;
        readonly UiButton _button;
        PopupMenu _menu;
        RectTransform _header;

        public bool IsOpen => _menu != null;

        public StartMenu(GameServices g, UiButton button)
        {
            _g = g;
            _button = button;
            g.Router.AnyPointerDown += (a, hit) =>
            {
                if (_menu == null || hit == null) { if (_menu != null && hit == null) Close(); return; }
                if (hit.transform.IsChildOf(_menu.transform) || hit.transform.IsChildOf(_button.transform)) return;
                Close();
            };
        }

        public void Toggle(CursorAgent a)
        {
            if (_menu != null) Close();
            else Open(a);
        }

        public void Open(CursorAgent a)
        {
            if (_menu != null) return;
            var c = _g.Content;
            var items = new List<MenuItem>
            {
                MenuItem.Of(c.Text("app.mail"), x => _g.Apps.Launch(AppIds.Mail, x), "icon_mail", elementId: "start:mail"),
                MenuItem.Of(c.Text("app.files"), x => _g.Apps.Launch(AppIds.Files, x), "icon_folder", elementId: "start:files"),
                MenuItem.Of(c.Text("app.workqueue"), x => _g.Apps.Launch(AppIds.WorkQueue, x), "icon_workorders", elementId: "start:workqueue"),
                MenuItem.Of(c.Text("app.workorders"), x => _g.Apps.Launch(AppIds.WorkOrders, x), "icon_workorders", elementId: "start:workorders"),
                MenuItem.Of(c.Text("app.staff"), x => _g.Apps.Launch(AppIds.Staff, x), "icon_staff", elementId: "start:staff"),
                MenuItem.Of(c.Text("app.notepad"), x => _g.Apps.Launch(AppIds.Notepad, x), "icon_notepad", elementId: "start:notepad"),
                MenuItem.Of(c.Text("app.camera"), x => _g.Apps.Launch(AppIds.Camera, x), "icon_camera", elementId: "start:camera"),
                MenuItem.Sep(),
                MenuItem.Of(c.Text("start.documents"), x => _g.Apps.OpenFolder(ContentIds.FolderDocuments, x), "icon_folder_open", elementId: "start:documents"),
                MenuItem.Of(c.Text("start.help"), x => _g.Apps.Launch(AppIds.Help, x), "icon_help", elementId: "start:help"),
                MenuItem.Sep(),
                MenuItem.Of(c.Text("start.shutdown"), ShutDown, "icon_shutdown", elementId: "start:shutdown"),
            };
            const int width = 176;
            const int headerH = 26;
            int bodyH = 4;
            foreach (var it in items) bodyH += it.Separator ? 8 : 18;
            var topLeft = new Vector2(2f, WindowManager.TaskbarHeight + bodyH + headerH);
            _menu = PopupMenu.Show(_g.Layers.Popups, topLeft, items, width);
            _menu.ManagedExternally = true;
            _menu.Rect.sizeDelta = new Vector2(width, bodyH + headerH);
            // Shift item rows down to make room for the header strip.
            foreach (Transform child in _menu.Rect)
            {
                var rt = (RectTransform)child;
                rt.offsetMin += new Vector2(0f, -headerH);
                rt.offsetMax += new Vector2(0f, -headerH);
            }
            _header = UIBuilder.Rect("Header", _menu.Rect).TopStrip(2, headerH - 2, 2, 2);
            var grad = _header.gameObject.AddComponent<BevelGraphic>();
            grad.Style = BevelStyle.Gradient;
            grad.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            grad.raycastTarget = false;
            var user = UIBuilder.Text(_header, c.Text("login.username"), Palette.TitleText, true);
            user.rectTransform.Stretch(6, 2, 4, 11);
            var os = UIBuilder.Text(_header, c.Text("os.name") + " " + c.Text("os.version"), Palette.TitleTextInactive);
            os.rectTransform.Stretch(6, 13, 4, 0);
            _menu.Closed += () => { _menu = null; _button.Toggled = false; };
            _button.Toggled = true;
            Sfx.Play("ui_click", a);
        }

        public void Close()
        {
            if (_menu != null) _menu.Close();
            _menu = null;
            _button.Toggled = false;
        }

        void ShutDown(CursorAgent a)
        {
            var c = _g.Content;
            Dialogs.Message(_g, c.Text("shutdown.denied.title"), c.Text("shutdown.denied.body"), "icon_warning", new[] { "OK" }, null);
        }
    }
}
