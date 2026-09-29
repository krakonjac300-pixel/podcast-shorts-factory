using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>Receives typed characters and keys while its window is focused.</summary>
    public interface IKeyboardTarget
    {
        void OnTyped(string text, CursorAgent by);
        void OnKey(GameKey key, CursorAgent by);
    }

    /// <summary>Base class for NEXUS applications. An app owns one window and builds its UI inside it.</summary>
    public abstract class App
    {
        protected GameServices G;
        public OSWindow Window { get; private set; }
        public abstract string AppId { get; }
        public bool IsOpen => Window != null && !Window.IsClosed;

        internal void Attach(GameServices g) => G = g;

        protected OSWindow CreateWindow(string title, string icon, int x, int y, int w, int h, WindowFlags flags, Rect? zoomFrom)
        {
            Window = G.Windows.Create(AppId, title, icon, x, y, w, h, flags, zoomFrom);
            Window.Owner = this;
            Window.Closed += (win, a) => OnClosed(a);
            return Window;
        }

        public abstract void Open(Rect? zoomFrom, CursorAgent by);
        public virtual void Tick(float dt) { }
        protected virtual void OnClosed(CursorAgent by) { }
    }

    /// <summary>
    /// Launches applications and opens files with the right viewer. Single-instance apps are focused
    /// instead of reopened. Every launch is attributed (player vs entity) and remembered by the entity.
    /// </summary>
    public sealed class AppManager
    {
        readonly GameServices _g;
        readonly Dictionary<string, Func<App>> _factories = new Dictionary<string, Func<App>>();
        readonly List<App> _open = new List<App>();

        public event Action<string, CursorAgent> Launched;
        /// <summary>Return false to block a launch (e.g. SecureView before it is unlocked). Args: appId, agent.</summary>
        public Func<string, CursorAgent, bool> CanLaunch;

        public AppManager(GameServices g)
        {
            _g = g;
            Register(AppIds.Files, () => new FilesApp());
            Register(AppIds.Mail, () => new MailApp());
            Register(AppIds.Notepad, () => new NotepadApp());
            Register(AppIds.Staff, () => new StaffApp());
            Register(AppIds.WorkOrders, () => new WorkOrdersApp());
            Register(AppIds.WorkQueue, () => new WorkQueueApp());
            Register(AppIds.Camera, () => new CameraApp());
            Register(AppIds.Help, () => new HelpApp());
            Register(AppIds.Disposal, () => new DisposalApp());
            Register(SystemMonitorApp.Id, () => new SystemMonitorApp());
        }

        public void Register(string appId, Func<App> factory) => _factories[appId] = factory;

        public IReadOnlyList<App> OpenApps => _open;

        public T Find<T>() where T : App
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i] is T t && t.IsOpen) return t;
            return null;
        }

        public App FindById(string appId)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].IsOpen && _open[i].AppId == appId) return _open[i];
            return null;
        }

        public App Launch(string appId, CursorAgent by, Rect? zoomFrom = null)
        {
            if (CanLaunch != null && !CanLaunch(appId, by)) return null;
            if (appId == AppIds.Workstation) return OpenFolder(ContentIds.FolderRoot, by, zoomFrom);
            // Single instance for everything except Notepad and the file viewers.
            if (appId != AppIds.Notepad)
            {
                var existing = FindById(appId);
                if (existing != null)
                {
                    existing.Window.Restore(by);
                    return existing;
                }
            }
            if (!_factories.TryGetValue(appId, out var factory)) return null;
            var app = factory();
            return Start(app, by, zoomFrom, appId);
        }

        App Start(App app, CursorAgent by, Rect? zoomFrom, string label)
        {
            app.Attach(_g);
            app.Open(zoomFrom, by);
            _open.Add(app);
            if (app.Window != null) app.Window.Closed += (w, a) => _open.Remove(app);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " opened " + label);
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.OpenedApp, label, _g.Now);
            Launched?.Invoke(label, by);
            return app;
        }

        public FilesApp OpenFolder(string folderId, CursorAgent by, Rect? zoomFrom = null)
        {
            var files = Find<FilesApp>();
            if (files == null)
            {
                files = new FilesApp(folderId);
                Start(files, by, zoomFrom, AppIds.Files);
            }
            else
            {
                files.Window.Restore(by);
                files.Navigate(folderId, by);
            }
            return files;
        }

        /// <summary>Opens a file in the viewer that fits its type.</summary>
        public App OpenFile(string fileId, CursorAgent by, Rect? zoomFrom = null)
        {
            var file = _g.Files.GetFile(fileId);
            if (file == null || file.Shredded) return null;
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.OpenedFile, fileId, _g.Now);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " opened file " + file.Name);
            _g.Flags.Increment("opened:" + fileId);

            switch (file.Kind)
            {
                case FileKind.Executable:
                    Dialogs.Message(_g, file.Name, file.Name + " is not a valid NEXUS application.", "icon_error", new[] { "OK" }, null);
                    return null;
                case FileKind.Data:
                case FileKind.Temp:
                    return Start(new DataViewerApp(fileId), by, zoomFrom, AppIds.DataViewer);
                default:
                    return Start(new NotepadApp(fileId), by, zoomFrom, AppIds.Notepad);
            }
        }

        public void ShowProperties(string fileId, CursorAgent by)
        {
            var f = _g.Files.GetFile(fileId);
            if (f == null) return;
            string text = "Name:      " + f.Name + "\nLocation:  " + _g.Files.PathOf(_g.Files.GetFolder(f.FolderId)) +
                          "\nSize:      " + f.Size + "\nModified:  " + f.Modified + "\nAttributes: " + (f.Protected ? "Protected " : "") +
                          (f.Hidden ? "Hidden " : "") + (f.Corrupted ? "Damaged" : "Normal");
            Dialogs.Message(_g, f.Name + " Properties", text, FileIcons.SpriteFor(f), new[] { "OK" }, null);
        }

        public void Tick(float dt)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                var app = _open[i];
                if (!app.IsOpen) { _open.RemoveAt(i); continue; }
                app.Tick(dt);
            }
        }

        /// <summary>Route keyboard input to the focused window's app.</summary>
        public void RouteKeyboard(IInputBackend input, CursorAgent player)
        {
            var win = _g.Windows.Active;
            if (win == null || !(win.Owner is IKeyboardTarget target)) return;
            if (!string.IsNullOrEmpty(input.TypedText)) target.OnTyped(input.TypedText, player);
            foreach (GameKey k in RoutedKeys)
                if (input.KeyDown(k)) target.OnKey(k, player);
        }

        static readonly GameKey[] RoutedKeys = { GameKey.Delete, GameKey.Up, GameKey.Down, GameKey.Left, GameKey.Right, GameKey.Tab, GameKey.Escape };
    }
}
