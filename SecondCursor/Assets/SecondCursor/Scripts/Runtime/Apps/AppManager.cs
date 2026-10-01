using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.UI;
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

        /// <summary>Tick exceptions in the current ten-second window (see <see cref="AppManager"/>).</summary>
        internal int Faults;
        internal float FaultsSince;

        /// <summary>A window the player reads for a while (Mail) opens clear of the notices' column (Phase H).</summary>
        protected virtual bool AvoidsNotices => false;

        protected OSWindow CreateWindow(string title, string icon, int x, int y, int w, int h, WindowFlags flags, Rect? zoomFrom)
        {
            var at = G.Windows.PlaceAvoidingOverlap(x, y, w, h, AvoidsNotices);
            G.Windows.MakeRoomFor(at.x, at.y, w, h);
            Window = G.Windows.Create(AppId, title, icon, at.x, at.y, w, h, flags, zoomFrom);
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
        /// <summary>A file was saved from Jotter: file id, the saved text, who saved it.</summary>
        public event Action<string, string, CursorAgent> FileSaved;
        /// <summary>Return false to block a launch (e.g. SecureView before it is unlocked). Args: appId, agent.</summary>
        public Func<string, CursorAgent, bool> CanLaunch;
        /// <summary>Phase H: one more line for the "saved" notice (file id, saved text): what the file now decides. Null = none.</summary>
        public Func<string, string, string> SavedNote;

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
            Register(RecentNoticesApp.Id, () => new RecentNoticesApp());
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
            // Phase M: ui_window is an opening swell (it used to be heard only on close).
            if (app.Window != null) Sfx.Play("ui_window", by);
            if (app.Window != null) app.Window.Closed += (w, a) => _open.Remove(app);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " opened " + label);
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.OpenedApp, label, _g.Now);
            Launched?.Invoke(label, by);
            return app;
        }

        /// <summary>The code prompt for a locked folder that has a code (one at a time).</summary>
        public AuthPromptApp OpenAuthPrompt(string folderId, CursorAgent by)
        {
            var open = Find<AuthPromptApp>();
            if (open != null)
            {
                open.Window.Restore(by);
                open.Window.Focus(by);
                return open;
            }
            return (AuthPromptApp)Start(new AuthPromptApp(folderId), by, null, AuthPromptApp.Id);
        }

        /// <summary>Jotter saved a file (the file system already holds the new text).</summary>
        internal void RaiseFileSaved(string fileId, string text, CursorAgent by)
        {
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " saved " + fileId);
            FileSaved?.Invoke(fileId, text, by);
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
            if (by != null && by.IsPlayer)
            {
                // Only what the player opens counts for tasks like "open the door log" (never a cursor's own opens).
                _g.Flags.Increment(Core.Story.Flags.OpenedByPlayerPrefix + fileId);
                _g.Tasks.Evaluate();
            }

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
                try
                {
                    FaultInjector.Check("app");
                    app.Tick(dt);
                }
                catch (Exception e)
                {
                    FaultLog.Report("app " + app.AppId, e);
                    CloseIfFailing(app);
                }
            }
        }

        const int FaultsToClose = 3;
        const float FaultWindowSeconds = 10f;

        /// <summary>An app whose Tick keeps throwing (three times in 10 s) is closed with a notice, so it cannot sit broken on the screen.</summary>
        void CloseIfFailing(App app)
        {
            float now = Time.unscaledTime;
            if (now - app.FaultsSince > FaultWindowSeconds)
            {
                app.FaultsSince = now;
                app.Faults = 0;
            }
            if (++app.Faults < FaultsToClose) return;
            string title = app.Window.Title;
            GameLog.Warn(LogChannel.OS, title + " stopped responding: closed");
            try
            {
                app.Window.Close(null, true);
                _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Format("os.app.stopped", title), "icon_warning", null, "sys_error");
            }
            catch (Exception e)
            {
                // Its own close handler failed: take the window off the screen anyway.
                FaultLog.Report("closing " + app.AppId, e);
                if (app.Window != null) UnityEngine.Object.Destroy(app.Window.gameObject);
            }
        }

        /// <summary>The frame a focused window used Esc itself (the pause menu ignores that press).</summary>
        public static int EscapeHandledFrame { get; private set; } = -1;

        public static void MarkEscapeHandled() => EscapeHandledFrame = Time.frameCount;

        /// <summary>
        /// Route keyboard input to the focused window's app. Phase I: typing that would go nowhere (the Camera Viewer, or another
        /// remote session's Jotter, holds the focus) goes to the Jotter that is waiting for the player's reply instead, and that
        /// Jotter comes to the front. Before this, a reply typed while Security's viewer had the focus vanished without a trace.
        /// </summary>
        public void RouteKeyboard(IInputBackend input, CursorAgent player)
        {
            var win = _g.Windows.Active;
            var target = win != null ? win.Owner as IKeyboardTarget : null;
            if (!string.IsNullOrEmpty(input.TypedText))
            {
                var pad = ConversationPadFor(win != null ? win.Owner as App : null);
                if (pad != null && !ReferenceEquals(pad, target))
                {
                    pad.Window.Focus(player);
                    target = pad;
                    GameLog.Info(LogChannel.Player, "Typing went to the Jotter that is waiting for a reply (another window had the focus)");
                }
            }
            if (target == null)
            {
                // Phase Q4 (A7): with no window taking keys, Enter opens the selected desktop icon (never while a dialog is up).
                if (input.KeyDown(GameKey.Enter) && _g.Desktop != null && player.Enabled && player.Visible && _g.Flags.Has(Core.Story.Flags.LoggedIn)
                    && _g.Director != null && _g.Director.CurrentBeat != "ending" && !_g.Windows.AnyAlwaysOnTop())
                    _g.Desktop.OpenSelected(player);
                return;
            }
            if (!string.IsNullOrEmpty(input.TypedText)) target.OnTyped(input.TypedText, player);
            foreach (GameKey k in RoutedKeys)
                if (input.KeyDown(k)) target.OnKey(k, player);
        }

        /// <summary>
        /// The conversation Jotter that should get the player's typing instead of <paramref name="focused"/> (the app that has the
        /// focus): null when the focused app takes typing itself (a Jotter waiting for the player, a page being edited, the code
        /// prompt) or when no conversation is going on. Prefers the Jotter that waits for a line now, else one whose owner is
        /// still typing (the keys are typed ahead and sent when it stops).
        /// </summary>
        NotepadApp ConversationPadFor(App focused)
        {
            if (focused is NotepadApp here && (here.WaitsForPlayer || !here.ConversationMode)) return null;
            if (focused is AuthPromptApp) return null;
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i] is NotepadApp n && n.IsOpen && n.WaitsForPlayer) return n;
            // Somebody is typing to you and nothing has the focus for it: hold the keys in that Jotter.
            if (focused is NotepadApp talking && talking.IsTalking) return null;
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i] is NotepadApp n && n.IsOpen && n.IsTalking) return n;
            return null;
        }

        static readonly GameKey[] RoutedKeys = { GameKey.Delete, GameKey.Up, GameKey.Down, GameKey.Left, GameKey.Right, GameKey.Tab, GameKey.Escape, GameKey.Enter };
    }
}
