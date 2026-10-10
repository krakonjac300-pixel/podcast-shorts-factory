using System;
using System.IO;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Phase Q2 (board C V6): the demo's handoff file on disk (<see cref="DemoHandoff"/> holds the format). The demo writes it into its
    /// own save folder when Night 1 ends; the full game reads one fixed path, the demo's save folder beside its own in the same company
    /// folder (or the test save folder), at most once per save, and silently skips a file that is missing, too big or not a handoff.
    /// Nothing else on the computer is ever read.
    /// </summary>
    public static class DemoHandoffIO
    {
        /// <summary>The demo build: Night 1 ended, so its lines, name and whether the file was shredded are handed over.</summary>
        public static void WriteFromDemo(SaveData d, bool shredded017)
        {
#if SC_DEMO
            if (SaveSystem.ProgressReadOnly || d == null) return;
            string path = Path.Combine(SaveSystem.Folder, DemoHandoff.FileName), tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(SaveSystem.Folder);
                File.WriteAllText(tmp, DemoHandoff.Compose(d, shredded017));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                GameLog.Info(LogChannel.System, "Demo handoff written (" + (d.playerLines?.Length ?? 0) + " line(s))");
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Could not write the demo handoff: " + e.Message);
            }
#endif
        }

        /// <summary>The full game, on the title: take the demo's handoff into the save once, if there is one.</summary>
        public static void ImportOnce()
        {
#if !SC_DEMO
            try
            {
                var d = SaveSystem.Load();
                if (d.demoImported) return;
                string path = ReadPath();
                if (string.IsNullOrEmpty(path)) return;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > DemoHandoff.MaxBytes) return;
                var handoff = DemoHandoff.Parse(File.ReadAllText(path));
                if (handoff == null || !handoff.ApplyTo(d)) return;
                SaveSystem.Save(d);
                GameLog.Info(LogChannel.System, "Demo handoff imported (" + handoff.Lines.Length + " line(s)" + (handoff.Name.Length > 0 ? ", a name" : "") + ")");
            }
            catch (Exception e)
            {
                // Silently skipped for the player; the log says why.
                GameLog.Info(LogChannel.System, "Demo handoff not read: " + e.Message);
            }
#endif
        }

        /// <summary>The test save folder when one is set (the bridge, -scsavedir), else the demo's own folder in the company folder.</summary>
        static string ReadPath()
        {
            if (!string.IsNullOrEmpty(SaveSystem.DirOverride)) return Path.Combine(SaveSystem.DirOverride, DemoHandoff.FileName);
            string company = Path.GetDirectoryName(Application.persistentDataPath);
            return string.IsNullOrEmpty(company) ? null : Path.Combine(company, DemoHandoff.DemoFolder, DemoHandoff.FileName);
        }
    }
}
