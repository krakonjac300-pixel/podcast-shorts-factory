using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Input;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Phase H (blind playtest): when another session undoes or refuses something the player did, NEXUS says so in its
    /// own dry voice and names who did it: a shred cancelled by session 017, a window (the Camera Viewer, File Manager)
    /// closed by session 017, a file dragged out from under the pointer. Nothing here changes what happens, only that
    /// it is said.
    /// </summary>
    public static class SystemNotices
    {
        /// <summary>The same notice at most this often (a fight over the viewer can close it every few seconds).</summary>
        const float Repeat = 6f;
        /// <summary>Phase N: how long a raced shred's result stays up (unless clicked).</summary>
        public const float RaceNoticeSeconds = 20f;

        public static void Attach(GameServices g)
        {
            var last = new Dictionary<string, float>();
            bool Due(string key)
            {
                if (last.TryGetValue(key, out float at) && Time.time - at < Repeat) return false;
                last[key] = Time.time;
                return true;
            }

            g.Shred.Cancelled += (fileId, by) =>
            {
                // Phase N (finding 2): every end of a raced shred is said, and how (No first, Cancel, or the player's own answer), and it
                // stays up for a while: the tester looked back 30 s later and found no dialog and no reason.
                bool other = by != null && by.IsEntity;
                if (!other && (by == null || !g.Shred.Raced)) return;
                var file = g.Files.GetFile(fileId);
                string name = file != null ? file.Name : fileId;
                string key = (g.Shred.CancelledAtConfirm ? "shred.cancelled.no" : "shred.cancelled.cancel") + (other ? "" : ".you");
                float until = Time.time + RaceNoticeSeconds;
                g.Notifications.Show(g.Content.Text("app.disposal"), g.Content.Format(key, name, SessionOf(g, by)), "icon_error", null, "sys_warning",
                    true, () => Time.time < until, other ? by.Actor : Core.Game.NoticeKind.Plain);
                GameLog.Info(LogChannel.OS, "Notice: shred of " + name + " cancelled by " + (other ? SessionOf(g, by) : "the player") + (g.Shred.CancelledAtConfirm ? " at the confirm" : " during the shred"));
            };
            g.Shred.Completed += (fileId, by) =>
            {
                if (!g.Shred.Raced || by == null || !by.IsPlayer) return;
                var file = g.Files.GetFile(fileId);
                float until = Time.time + RaceNoticeSeconds;
                g.Notifications.Show(g.Content.Text("app.disposal"),
                    g.Content.Format(ContestCopy.ShredKey(true, false), file != null ? file.Name : fileId),
                    "icon_info", null, "ui_select", true, () => Time.time < until);
            };
            g.Windows.ClosedEvent += (w, by) =>
            {
                // Dialogs have their own notices (a shred cancelled); an app window closed by another cursor is named.
                if (w == null || by == null || !by.IsEntity || w.AppId == "dialog" || w.AppId == "progress") return;
                string app = g.Content.Text("app." + w.AppId, w.Title);
                bool camera = w.AppId == AppIds.Camera;
                if (!camera && !Due("close:" + w.AppId)) return;
                // Phase K: during rounds it says why (it showed Custodial) and how to get the viewer back.
                var rounds = g.Rounds;
                bool custodial = camera && rounds != null && rounds.Running && rounds.Model != null && (w.Owner as Apps.CameraApp)?.CurrentCamera == rounds.Model.FigureCamera;
                // Phase N (finding 4): where Custodial is now, and the camera a reopen comes back on (never Custodial's).
                string body = !camera ? g.Content.Format("window.closed.by", app, SessionOf(g, by))
                    : !custodial ? g.Content.Format("camera.closed.by", SessionOf(g, by))
                    : g.Content.Format(rounds.ClosedNoticeKey ?? "camera.closed.custodial", SessionOf(g, by),
                        Story.RoundsSystem.CameraName(g, rounds.Model.FigureCamera), Story.RoundsSystem.CameraName(g, Apps.CameraApp.CameraOnOpen(g)));
                bool duringRounds = rounds != null && rounds.Running;
                g.Notifications.Show(app, body, camera ? "icon_camera" : "icon_info", null, "ui_select", false,
                    camera ? () => (!duringRounds || rounds.Running) && g.Apps.Find<Apps.CameraApp>() == null : (System.Func<bool>)null,
                    by.Actor, urgent: camera);
                GameLog.Info(LogChannel.OS, "Notice: " + app + " closed by " + SessionOf(g, by));
            };
            // Phase R (sixth blind playtest: "Camera Viewer and Personnel opened by themselves with no author"): a program another session
            // opens is named, like a window it closes. Jotter has its own title and prompt; the code prompt and the viewers are the player's.
            g.Apps.Launched += (appId, by) =>
            {
                if (by == null || !by.IsEntity || !NamesOpening(appId)) return;
                bool camera = appId == AppIds.Camera;
                if (!camera && !Due("open:" + appId)) return;
                string app = g.Content.Text("app." + appId, appId);
                string who = Capital(SessionOf(g, by));
                var viewer = camera ? g.Apps.Find<Apps.CameraApp>() : null;
                string shownCamera = viewer != null ? viewer.CurrentCamera : null;
                g.Notifications.Show(app, g.Content.Format("window.opened.by", who, app), camera ? "icon_camera" : "icon_info", null, "ui_select", false,
                    camera ? () => viewer != null && viewer.IsOpen && !viewer.Window.IsMinimized && viewer.CurrentCamera == shownCamera : (System.Func<bool>)null,
                    by.Actor, urgent: camera);
                GameLog.Info(LogChannel.OS, "Notice: " + app + " opened by " + who);
            };
            g.Files.FileMoved += (file, from, to, actor) =>
            {
                if (file == null || actor == Core.FileSystem.Actor.System) return;
                var c = g.Content;
                string toName = g.Files.GetFolder(to)?.Name ?? to, fromName = g.Files.GetFolder(from)?.Name ?? from;
                if (actor == Core.FileSystem.Actor.Player)
                {
                    // Phase K: a file dropped on the desktop leaves the File Manager list: say where it went.
                    if (to == ContentIds.FolderDesktop && from != ContentIds.FolderDesktop)
                        g.Notifications.Show(c.Text("os.name"), c.Format("files.moved.desktop", file.Name, fromName), "icon_info", null, "ui_select");
                    return;
                }
                // Phase K (finding 16): help by another session with a file a task needs is never silent, nor is undoing it.
                var task = Apps.FilesApp.TaskFor(g, file.Id);
                if (task == null) return;
                bool into = to == task.Data.param;
                // The last file of a batch completes the task at once: that help counts too. Taking one out only matters while it is open.
                if (!into && (from != task.Data.param || task.State != Core.Tasks.TaskState.Active)) return;
                string key = into ? "files.help.by" : "files.unhelp.by";
                var helper = file.MovedBy == SessionOf(g, g.GaryAgent) ? Core.Game.NoticeKind.Gary : Core.Game.NoticeKind.Entity;
                g.Notifications.Show(c.Text("app.workqueue"), c.Format(key, file.Name, to == task.Data.param ? toName : fromName, file.MovedBy, task.Title, task.ProgressText),
                    "icon_task_active", a => g.Apps.Launch(AppIds.WorkQueue, a), "ui_select", false, null, helper);
                GameLog.Info(LogChannel.OS, "Notice: " + file.Name + " moved " + from + " -> " + to + " by " + file.MovedBy + " (" + task.Id + " " + task.ProgressText + ")");
            };
            if (g.Entity != null && g.Entity.Brain != null)
            {
                g.Entity.Brain.Defended += how =>
                {
                    // Snatched off the desktop just as the player reached for it: the file moved, and it was not the player.
                    if (how != "keepaway" || !Due("keepaway")) return;
                    var file = g.Files.GetFile(g.Entity.Brain.ProtectedFileId);
                    if (file == null) return;
                    g.Notifications.Show(g.Content.Text("os.name"), g.Content.Format("file.moved.by", file.Name, SessionOf(g, g.EntityAgent)), "icon_info", null, "ui_select", false, null, Core.Game.NoticeKind.Entity);
                    GameLog.Info(LogChannel.OS, "Notice: " + file.Name + " moved by session 017");
                    // Phase I: beside the file itself, in the fight's own panel: it was taken while nobody was holding it.
                    var icon = g.Desktop != null ? g.Desktop.IconForFile(file.Id) : null;
                    if (icon != null && g.Conflict != null && g.Conflict.Hud != null)
                        g.Conflict.Hud.ShowMessage(g.Content.Text("tug.snatch", "SESSION 017 TOOK THE FILE WHILE YOU WEREN'T HOLDING IT."), icon.Hit.Center);
                };
            }
        }

        /// <summary>The programs whose opening by another session is named (not Jotter, the code prompt or the file viewers).</summary>
        static bool NamesOpening(string appId) =>
            appId == AppIds.Camera || appId == AppIds.Staff || appId == AppIds.Files || appId == AppIds.Mail
            || appId == AppIds.WorkOrders || appId == AppIds.WorkQueue || appId == AppIds.Disposal;

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);


        /// <summary>"session 017" for the second cursor, "session 209" for the third, else "a remote session".</summary>
        public static string SessionOf(GameServices g, CursorAgent a)
        {
            if (a != null && a == g.EntityAgent) return g.Content.Text("session.entity", "session 017");
            if (a != null && a == g.GaryAgent) return g.Content.Text("session.gary", "a remote session");
            return g.Content.Text("session.remote", "a remote session");
        }
    }
}
