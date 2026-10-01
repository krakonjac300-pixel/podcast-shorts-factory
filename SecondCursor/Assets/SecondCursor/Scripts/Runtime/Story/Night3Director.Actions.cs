#if !SC_DEMO
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q1 (owner 4): Night 3's two scares born from the player's own actions, both through the scheduler's action gate, both quiet
    /// and deniable, neither touching a task. A Batch 47 file the player archived earlier is back in Intake with their own initials in its
    /// name; and once, for a few seconds, the tray clock reads 7:05 AM, the minute the shift does not end.
    /// </summary>
    public sealed partial class Night3Director
    {
        /// <summary>The player's initials (Casey Rourke, CROURKE at log on).</summary>
        const string PlayerInitials = "CR";
        const float ClockOnceSeconds = 4f;

        void ArmNight3Actions()
        {
            if (IsStandIn) return;
            _g.Scares.SlotAction("archived_copy", 50f, 360f, () => ArchivedByPlayer() != null && !LookingAtIntake() && _g.Player.Payload == null, () =>
            {
                var src = ArchivedByPlayer();
                if (src == null) return;
                string id = src.Id + "_cr";
                string name = src.Name.EndsWith(".dat") ? src.Name.Substring(0, src.Name.Length - 4) + "_" + PlayerInitials + ".dat" : src.Name + "_" + PlayerInitials;
                var copy = _g.Files.CreateFile(id, name, src.Extension, ContentIds.FolderIntake, src.Content, Actor.System);
                if (copy != null) copy.Size = src.Size;
                GameLog.Info(LogChannel.Story, "Anomaly: " + name + " in Intake (the player archived " + src.Name + ")");
            });
            _g.Scares.SlotAction("clock_705", 120f, 300f, () => _g.Clock.TotalMinutes < 6 * 60, () =>
            {
                _g.Taskbar.ShowClockOnce("7:05 AM", ClockOnceSeconds);
                GameLog.Info(LogChannel.Story, "Anomaly: the tray clock read 7:05 AM for " + ClockOnceSeconds + " s");
            });
        }

        static readonly string[] Batch47Files = { ContentIds.Batch47A, ContentIds.FileBatch47B, ContentIds.Batch47C };

        /// <summary>A Batch 47 file the player (not another session, not Night Operations) put in Archive, or null.</summary>
        VFile ArchivedByPlayer()
        {
            foreach (var id in Batch47Files)
            {
                var f = _g.Files.GetFile(id);
                if (f != null && !f.Shredded && f.FolderId == ContentIds.FolderArchive && string.IsNullOrEmpty(f.MovedBy) && !_g.Files.Exists(id + "_cr")) return f;
            }
            return null;
        }

        /// <summary>A File Manager shows Intake right now (the copy appears while nobody is looking).</summary>
        bool LookingAtIntake()
        {
            var fm = _g.Apps.Find<FilesApp>();
            return fm != null && fm.IsOpen && !fm.Window.IsMinimized && fm.FolderId == ContentIds.FolderIntake;
        }
    }
}
#endif
