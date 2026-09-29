using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
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
                if (by == null || !by.IsEntity) return;
                var file = g.Files.GetFile(fileId);
                string name = file != null ? file.Name : fileId;
                g.Notifications.Show(g.Content.Text("app.disposal"), g.Content.Format("shred.cancelled.by", name, SessionOf(g, by)), "icon_error", null, "sys_warning");
                GameLog.Info(LogChannel.OS, "Notice: shred of " + name + " cancelled by " + SessionOf(g, by));
            };
            g.Windows.ClosedEvent += (w, by) =>
            {
                // Dialogs have their own notices (a shred cancelled); an app window closed by another cursor is named.
                if (w == null || by == null || !by.IsEntity || w.AppId == "dialog" || w.AppId == "progress") return;
                string app = g.Content.Text("app." + w.AppId, w.Title);
                if (!Due("close:" + w.AppId)) return;
                bool camera = w.AppId == AppIds.Camera;
                g.Notifications.Show(app, camera ? g.Content.Format("camera.closed.by", SessionOf(g, by)) : g.Content.Format("window.closed.by", app, SessionOf(g, by)),
                    camera ? "icon_camera" : "icon_info", null, "ui_select");
                GameLog.Info(LogChannel.OS, "Notice: " + app + " closed by " + SessionOf(g, by));
            };
            if (g.Entity != null && g.Entity.Brain != null)
            {
                g.Entity.Brain.Defended += how =>
                {
                    // Snatched off the desktop just as the player reached for it: the file moved, and it was not the player.
                    if (how != "keepaway" || !Due("keepaway")) return;
                    var file = g.Files.GetFile(g.Entity.Brain.ProtectedFileId);
                    if (file == null) return;
                    g.Notifications.Show(g.Content.Text("os.name"), g.Content.Format("file.moved.by", file.Name, SessionOf(g, g.EntityAgent)), "icon_info", null, "ui_select");
                    GameLog.Info(LogChannel.OS, "Notice: " + file.Name + " moved by session 017");
                };
            }
        }

        /// <summary>"session 017" for the second cursor, "session 209" for the third, else "a remote session".</summary>
        public static string SessionOf(GameServices g, CursorAgent a)
        {
            if (a != null && a == g.EntityAgent) return g.Content.Text("session.entity", "session 017");
            if (a != null && a == g.GaryAgent) return g.Content.Text("session.gary", "a remote session");
            return g.Content.Text("session.remote", "a remote session");
        }
    }
}
