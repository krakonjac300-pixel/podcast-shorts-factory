using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using SecondCursor.Game;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Phase G bridge commands: save robustness checks (a locked file is retried, a damaged one is set aside), the QA
    /// launch's read-only progress, settings edits for the disclaimer check, and the store art renders.
    /// </summary>
    public static partial class SecondCursorTestBridge
    {
        const string PhaseGHelp =
            "Phase G: savecheck (test folder: a locked progress.json is retried, a damaged one is set aside) | qaread on|off (progress.json read-only, like a -scnight launch)\n" +
            "         settingsset frameRate|largeText|volume|reduceFlashing|tugAssist|clickLock VALUE (edit settings.json on disk) | storeart (render Builds/StoreArt) | storeshot NAME (1920x1080 game shot into Builds/StoreArt/screenshots)\n" +
            "         storeshotafter SECONDS NAME (the same, taken later while the next commands run) | savecheck | buildguard | democrash | contentfolders | reload | steamcheck\n";

        static IEnumerator TryEditorPhaseGCommand(string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "savecheck": SaveCheck(); return Done();
                case "qaread":
                    SaveSystem.ProgressReadOnly = a.Length < 2 || a[1] != "off";
                    Say("progress.json " + (SaveSystem.ProgressReadOnly ? "read-only (QA launch rules, until the next Play)" : "writable"));
                    return Done();
                case "settingsset": SettingsSet(a); return Done();
                case "storeart":
                {
                    string dir = SecondCursorStoreArt.Render();
                    Say("store art rendered into " + dir);
                    return Done();
                }
                case "buildguard":
                    try
                    {
                        new SecondCursorBuildGuard().OnPreprocessBuild(null);
                        Say("build guard: a full build would pass");
                    }
                    catch (UnityEditor.Build.BuildFailedException e)
                    {
                        Say("build guard: refused: " + e.Message);
                    }
                    return Done();
                case "democrash":
                    if (EditorApplication.isPlaying) { Say("ERROR: stop Play mode first"); return Done(); }
                    SecondCursorBuild.SimulateInterruptedDemoBuild();
                    Say("moved the demo-excluded content to " + SecondCursorBuild.DemoExcludedFolder + " (as after a crash mid-build); content folders: " + ContentFolders());
                    return Done();
                case "contentfolders":
                    Say("content folders: " + ContentFolders() + "; " + SecondCursorBuild.DemoExcludedFolder + (AssetDatabase.IsValidFolder(SecondCursorBuild.DemoExcludedFolder) ? " EXISTS" : " absent")
                        + "; demo build running flag=" + SecondCursorBuild.DemoBuildRunning + ", building=" + BuildPipeline.isBuildingPlayer);
                    return Done();
                case "reload": return Reload();
                case "steamcheck":
                {
                    // What a build would say if Steamworks.NET were compiled in (the package is not installed yet).
                    string full = SecondCursorBuild.SteamReleaseProblem(false, true), demo = SecondCursorBuild.SteamReleaseProblem(true, true);
                    Say("full build with STEAMWORKS_NET: " + (full ?? "passes"));
                    Say("demo build with STEAMWORKS_NET: " + (demo ?? "passes"));
                    Say("without STEAMWORKS_NET: " + (SecondCursorBuild.SteamReleaseProblem(false, false) ?? "passes"));
                    return Done();
                }
                default: return null;
            }
        }

        static string ContentFolders()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var n in SecondCursorBuild.DemoExcludedContent)
                names.Add(n + (AssetDatabase.IsValidFolder(SecondCursorBuild.ContentFolder + "/" + n) ? " in Resources" : " MISSING"));
            return string.Join(", ", names);
        }

        /// <summary>A domain reload (as when Unity starts): the bridge script continues after it.</summary>
        static IEnumerator Reload()
        {
            if (EditorApplication.isPlaying) { Say("ERROR: stop Play mode first"); yield break; }
            Say("domain reload requested");
            Persist();
            EditorUtility.RequestScriptReload();
            double end = EditorApplication.timeSinceStartup + 120;
            while (EditorApplication.timeSinceStartup < end) yield return null;
        }

        static IEnumerator TryGamePhaseGCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "storeshot": return StoreShot(a.Length > 1 ? a[1] : "shot");
                case "storeshotafter":
                {
                    // A shot taken while the next commands run (a tug is over before a blocking command returns).
                    float delay = F(a, 1, 0.5f);
                    string name = a.Length > 2 ? a[2] : "shot";
                    double at = EditorApplication.timeSinceStartup + delay;
                    DebugOverlay.HideHint = true;
                    EditorApplication.CallbackFunction cb = null;
                    cb = () =>
                    {
                        if (EditorApplication.timeSinceStartup < at) return;
                        EditorApplication.update -= cb;
                        UnityEngine.Debug.Log("[SYSTEM] " + CaptureStoreShot(name));
                        DebugOverlay.HideHint = false;
                    };
                    EditorApplication.update += cb;
                    Say("store shot '" + name + "' scheduled in " + delay.ToString("0.00", CultureInfo.InvariantCulture) + " s");
                    return Done();
                }
                default: return TryGameAudioCommand(g, cmd, a, rest);
            }
        }

        // ------------------------------------------------------------------ saves

        /// <summary>
        /// In the test folder only: (1) progress.json locked by another handle for 120 ms is still read (the retry) and
        /// is not set aside; (2) a progress.json that is not JSON is renamed .corrupt and the .bak is used.
        /// </summary>
        static void SaveCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride)) { Say("ERROR: savecheck needs a test folder first (savedir PATH)"); return; }
            string path = Path.Combine(SaveSystem.DirOverride, "progress.json"), bad = path + ".corrupt", bak = path + ".bak";
            if (File.Exists(bad)) File.Delete(bad);
            var d = SaveSystem.Load();
            d.tugWinsTotal = 7;
            SaveSystem.Save(d);
            d.tugWinsTotal = 8;
            SaveSystem.Save(d); // progress.json = 8, .bak = 7

            // 1. A short exclusive lock (antivirus, cloud sync).
            var locked = new ManualResetEventSlim(false);
            var holder = new Thread(() =>
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    locked.Set();
                    Thread.Sleep(120);
                }
            });
            holder.Start();
            locked.Wait(2000);
            var sw = Stopwatch.StartNew();
            var read = SaveSystem.Load();
            sw.Stop();
            holder.Join();
            Say("locked read: tugWinsTotal=" + read.tugWinsTotal + " after " + sw.ElapsedMilliseconds + " ms, corrupt file=" + File.Exists(bad)
                + (read.tugWinsTotal == 8 && !File.Exists(bad) ? " -> OK (retried, not quarantined)" : " -> FAIL"));

            // 1b. A lock that outlasts the retries: the older .bak is used for reading, and nothing is written over
            // the newer file until it can be read again.
            var longLock = new ManualResetEventSlim(false);
            var longHolder = new Thread(() =>
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    longLock.Set();
                    Thread.Sleep(700);
                }
            });
            longHolder.Start();
            longLock.Wait(2000);
            var stale = SaveSystem.Load();
            stale.tugWinsTotal = 99;
            SaveSystem.Save(stale);
            longHolder.Join();
            var after = SaveSystem.Load();
            bool kept = after.tugWinsTotal == 8;
            Say("long lock: read the backup (tugWinsTotal=" + stale.tugWinsTotal + " after edit), file after unlock=" + after.tugWinsTotal
                + (kept ? " -> OK (not overwritten while unreadable)" : " -> FAIL"));

            // 2. A damaged file.
            File.WriteAllText(path, "{ this is not json");
            var fallback = SaveSystem.Load();
            bool ok = File.Exists(bad) && fallback.tugWinsTotal == 7 && SaveSystem.CorruptThisLaunch;
            Say("damaged read: tugWinsTotal=" + fallback.tugWinsTotal + " (from .bak), corrupt file=" + File.Exists(bad) + ", notice=" + SaveSystem.CorruptThisLaunch
                + (ok ? " -> OK (set aside, backup used)" : " -> FAIL"));
            if (File.Exists(bad)) File.Delete(bad);
            if (File.Exists(bak) && !File.Exists(path)) File.Copy(bak, path);
        }

        static void SettingsSet(string[] a)
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride)) { Say("ERROR: settingsset needs a test folder first (savedir PATH)"); return; }
            if (a.Length < 3) { Say("ERROR: settingsset FIELD VALUE"); return; }
            var s = SaveSystem.LoadSettings();
            string field = a[1].ToLowerInvariant(), value = a[2];
            switch (field)
            {
                case "framerate": s.frameRate = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "largetext": s.largeText = value == "on" || value == "true"; break;
                case "volume": s.masterVolume = float.Parse(value, CultureInfo.InvariantCulture); break;
                case "reduceflashing": s.reduceFlashing = value == "on" || value == "true"; break;
                // Phase P (A2): saved, and applied to a running game from its next contest (or drag).
                case "tugassist": s.tugAssist = value == "hold" ? "hold" : "off"; AccessSettings.SetTugAssist(value == "hold"); break;
                case "clicklock": s.clickLock = value == "on" || value == "true"; AccessSettings.SetClickLock(s.clickLock); break;
                default: Say("ERROR: unknown settings field '" + a[1] + "'"); return;
            }
            SaveSystem.SaveSettings(s);
            SettingsLines();
        }

        // ------------------------------------------------------------------ store screenshots

        /// <summary>
        /// A 1920x1080 shot of the Game view (CRT effects included): captured supersized, the letterbox cut away, then
        /// resampled to exactly 1920x1080. Saved to Builds/StoreArt/screenshots/NAME.png.
        /// </summary>
        static IEnumerator StoreShot(string name)
        {
            // No developer hint in a store shot; the bridge steps its scripts from the editor loop, so give the Game
            // view a few ticks to draw without it.
            DebugOverlay.HideHint = true;
            for (int i = 0; i < 4; i++) yield return null;
            Say(CaptureStoreShot(name));
            DebugOverlay.HideHint = false;
        }

        /// <summary>Captures the Game view now and writes Builds/StoreArt/screenshots/NAME.png at 1920x1080.</summary>
        static string CaptureStoreShot(string name)
        {
            int super = Mathf.Clamp(Mathf.CeilToInt(1920f / Mathf.Max(1, Screen.width)), 1, 4);
            var shot = ScreenCapture.CaptureScreenshotAsTexture(super);
            if (shot == null) return "ERROR: capture failed (is the Game view visible?)";
            string dir = Path.Combine(SecondCursorStoreArt.OutputDir, "screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            var framed = SecondCursorStoreArt.CropTo16x9(shot);
            var final = SecondCursorStoreArt.Resize(framed, 1920, 1080);
            File.WriteAllBytes(path, final.EncodeToPNG());
            string result = "saved " + path + " (captured " + shot.width + "x" + shot.height + ", x" + super + ")";
            UnityEngine.Object.DestroyImmediate(shot);
            if (framed != shot) UnityEngine.Object.DestroyImmediate(framed);
            UnityEngine.Object.DestroyImmediate(final);
            return result;
        }
    }
}
