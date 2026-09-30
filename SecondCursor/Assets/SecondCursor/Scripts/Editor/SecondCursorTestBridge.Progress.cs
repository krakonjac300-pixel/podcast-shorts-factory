using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Story;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Phase E bridge commands: a test save folder, editing the save, settings, the simulated Steam Deck and store,
    /// scripting defines (SC_DEMO), the demo build, the title screens, achievements and the Steam overlay.
    /// </summary>
    public static partial class SecondCursorTestBridge
    {
        const string ProgressHelp =
            "Save:   savedir PATH|off | resetsave | saveset FIELD VALUE (nightUnlocked|currentNight|lastCompleted|endings a,b|achievements a,b|none|tugwins N|tuglosses N|difficulty M|flashing chosen|checkpoint clear) | settings\n" +
            "Phase E: title [main|select|records|credits] | achievements list|on|off|next | haslog TEXT | forceexit shred|keep|logoff | overlay | deck on|off | store on|off | define NAME on|off | builddemo | saveproject\n";

        /// <summary>Game commands that stop the run counting for records (a forced tug disarms when it decides a fight).</summary>
        static readonly HashSet<string> Disarming = new HashSet<string>
        {
            "beat", "jump", "night", "restart", "setflag", "clearflag", "trust", "assist", "setclock", "difficulty", "checkpoint", "stage", "speed", "scareforce",
        };

        const string TestStoreUrl = "https://store.steampowered.com/app/480/ (bridge dry run)";
        const string SaveDirKey = "SecondCursor.Bridge.SaveDir", DeckKey = "SecondCursor.Bridge.Deck", StoreKey = "SecondCursor.Bridge.Store";

        /// <summary>A recompile resets statics: the test folder, the simulated Deck and the store dry run come back from SessionState.</summary>
        static void RestoreTestState()
        {
            string dir = SessionState.GetString(SaveDirKey, "");
            if (dir.Length > 0) SaveSystem.DirOverride = dir;
            SteamBridge.SimulateDeck = SessionState.GetBool(DeckKey, false);
            if (SessionState.GetBool(StoreKey, false)) SteamBridge.TestStoreUrl = TestStoreUrl;
        }

        // ------------------------------------------------------------------ editor level (no game needed)

        static IEnumerator TryEditorProgressCommand(string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "savedir": return SaveDir(rest);
                case "resetsave":
                    if (!SaveSystem.DeleteAllInOverride()) Say("ERROR: resetsave needs a test folder first (savedir PATH)");
                    else Say("save files deleted in " + SaveSystem.DirOverride);
                    return Done();
                case "saveset": SaveSet(a); return Done();
                case "settings": SettingsLines(); return Done();
                case "deck":
                    SteamBridge.SimulateDeck = a.Length < 2 || a[1] != "off";
                    SessionState.SetBool(DeckKey, SteamBridge.SimulateDeck);
                    Say("simulated Steam Deck " + (SteamBridge.SimulateDeck ? "on (wording applies to the next night built)" : "off"));
                    return Done();
                case "store":
                    SteamBridge.TestStoreUrl = a.Length < 2 || a[1] != "off" ? TestStoreUrl : null;
                    SessionState.SetBool(StoreKey, SteamBridge.TestStoreUrl != null);
                    Say("store button " + (SteamBridge.TestStoreUrl != null ? "on (dry run, logs only)" : "off"));
                    return Done();
                case "define":
                    if (a.Length < 2) { Say("ERROR: define NAME on|off"); return Done(); }
                    return Define(a[1], a.Length < 3 || a[2] != "off");
                case "builddemo": return BuildDemo();
                case "saveproject":
                    AssetDatabase.SaveAssets();
                    Say("project saved (product name " + PlayerSettings.productName + ")");
                    return Done();
                default: return null;
            }
        }

        static IEnumerator SaveDir(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "off")
            {
                SaveSystem.DirOverride = null;
                SessionState.EraseString(SaveDirKey);
                Say("saves: the default folder (" + Application.persistentDataPath + ")");
                yield break;
            }
            if (!Path.IsPathRooted(path)) { Say("ERROR: savedir needs a full path"); yield break; }
            Directory.CreateDirectory(path);
            SaveSystem.DirOverride = path;
            SessionState.SetString(SaveDirKey, path);
            Say("saves: " + path);
        }

        static void SaveSet(string[] a)
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride)) { Say("ERROR: saveset needs a test folder first (savedir PATH)"); return; }
            if (a.Length < 2) { Say("ERROR: saveset FIELD VALUE"); return; }
            string field = a[1].ToLowerInvariant(), value = a.Length > 2 ? a[2] : "";
            int n = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
            string[] list = value == "none" || value.Length == 0 ? Array.Empty<string>() : value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (field == "flashing")
            {
                var s = SaveSystem.LoadSettings();
                s.flashingChosen = value != "unchosen";
                SaveSystem.SaveSettings(s);
                Say("settings flashingChosen=" + s.flashingChosen);
                return;
            }
            var d = SaveSystem.Load();
            switch (field)
            {
                case "nightunlocked": d.nightUnlocked = Mathf.Clamp(n, 1, SaveData.Nights + 1); break;
                case "currentnight": d.currentNight = Mathf.Clamp(n, 1, SaveData.Nights); break;
                case "lastcompleted": d.lastCompletedNight = Mathf.Clamp(n, 0, SaveData.Nights); break;
                case "endings": d.endingsSeen = list; break;
                case "achievements": d.achievements = list; break;
                case "tugwins": d.tugWinsTotal = Mathf.Max(0, n); break;
                case "tuglosses": d.tugLossesTotal = Mathf.Max(0, n); break;
                case "difficulty": d.difficulty = value == "story" ? "story" : "normal"; break;
                case "checkpoint":
                    if (value != "clear") { Say("ERROR: saveset checkpoint clear"); return; }
                    d.SetCheckpoint(new Checkpoint());
                    break;
                default: Say("ERROR: unknown save field '" + a[1] + "'"); return;
            }
            SaveSystem.Save(d);
            SaveLines();
        }

        static void SettingsLines()
        {
            var s = SaveSystem.LoadSettings();
            Say("settings volume=" + s.masterVolume.ToString("0.0", CultureInfo.InvariantCulture) + " crt=" + s.crtEffects + " reduceFlashing=" + s.reduceFlashing
                + " flashingChosen=" + s.flashingChosen + " fullscreen=" + s.fullscreen + " frameRate=" + s.frameRate + " largeText=" + s.largeText
                + " largeTextChosen=" + s.largeTextChosen);
            Say("live frameRate=" + DisplaySettings.FrameRate + " vSyncCount=" + QualitySettings.vSyncCount + " targetFrameRate=" + Application.targetFrameRate
                + " readingScale=" + DisplaySettings.ReadingScale + " folder=" + SaveSystem.Folder);
        }

        /// <summary>Adds or removes a Standalone scripting define and waits for the recompile (the domain reload resumes the script).</summary>
        static IEnumerator Define(string name, bool on)
        {
            if (EditorApplication.isPlaying) { Say("ERROR: stop Play mode before changing defines"); yield break; }
            var target = NamedBuildTarget.Standalone;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(x => x.Length > 0).ToList();
            if (defines.Contains(name) == on)
            {
                Say("define " + name + " already " + (on ? "on" : "off"));
                yield break;
            }
            if (on) defines.Add(name);
            else defines.Remove(name);
            Say("define " + name + " " + (on ? "on" : "off") + " (recompiling)");
            Persist();
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            CompilationPipeline.RequestScriptCompilation();
            double settle = EditorApplication.timeSinceStartup + 5;
            while (!EditorApplication.isCompiling && EditorApplication.timeSinceStartup < settle) yield return null;
            double end = EditorApplication.timeSinceStartup + 240;
            while ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < end) yield return null;
            ClearPersisted();
            AppendCompileErrors();
        }

        static IEnumerator BuildDemo()
        {
            if (EditorApplication.isPlaying) { Say("ERROR: stop Play mode before building"); yield break; }
            try
            {
                var report = SecondCursorBuild.BuildWindowsDemo();
                Say(report != null ? SecondCursorBuild.Summary(report, SecondCursorBuild.DemoExePath).Replace('\n', ' ') : "ERROR: demo build did not run");
            }
            catch (UnityEditor.Build.BuildFailedException e)
            {
                Say("ERROR: demo build refused: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ game level

        static IEnumerator TryGameProgressCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "title":
                {
                    var screen = TitleScreenId.Main;
                    string which = a.Length > 1 ? a[1].ToLowerInvariant() : "main";
                    if (which == "select") screen = TitleScreenId.NightSelect;
                    else if (which == "records") screen = TitleScreenId.Records;
                    else if (which == "credits") screen = TitleScreenId.Credits;
                    GameBootstrap.ToTitle(screen);
                    return WaitFor(() => TitleMenu.Current != null && TitleMenu.Current.Services == G, 30f, "title " + which);
                }
                case "achievements":
                {
                    string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "list";
                    if (mode == "on") g.ForceArm();
                    else if (mode == "off") g.Unforce("bridge achievements off");
                    else if (mode == "next") GameRoot.ForceArmNext = true;
                    else AchievementLines();
                    Say("records " + (g.RecordsArmed ? "armed" : "held (" + g.RecordsHeldReason + ")") + (GameRoot.ForceArmNext ? ", next root armed" : ""));
                    return Done();
                }
#if !SC_DEMO
                case "forceexit":
                {
                    // Night 3's debug exits (like the F1 panel's Force buttons, but without holding records: arm first).
                    if (!(g.Director is Night3Director n3)) { Say("ERROR: forceexit needs Night 3"); return Done(); }
                    string which = a.Length > 1 ? a[1].ToLowerInvariant() : "keep";
                    var exit = which == "shred" ? Core.Story.Night3Exit.Shred : which == "logoff" ? Core.Story.Night3Exit.LogOff : Core.Story.Night3Exit.Keep;
                    n3.ForceExit(exit);
                    return WaitFor(() => G.Director != null && G.Director.CurrentBeat == "ending", 30f, "Night 3 ending (" + which + ")");
                }
#endif
                case "haslog":
                {
                    // Like waitlog, but over the whole recent log (a line written before this command also counts).
                    bool found = GameLog.Recent(400).Any(e => e.ToString().IndexOf(rest, StringComparison.OrdinalIgnoreCase) >= 0);
                    Say((found ? "found" : "NOT FOUND") + " log '" + rest + "'");
                    return Done();
                }
                case "overlay":
                    SteamBridge.RaiseOverlayActivated();
                    Say("Steam overlay activated (simulated)");
                    return WaitSeconds(0.2f);
                default: return TryGameBalanceCommand(g, cmd, a, rest);
            }
        }

        static void AchievementLines()
        {
            var d = SaveSystem.Load();
            foreach (var def in AchievementIds.All)
                Say(def.Id + (Array.IndexOf(d.achievements, def.Id) >= 0 ? " UNLOCKED" : " locked") + (def.Hidden ? " (hidden)" : ""));
            Say(AchievementIds.CountUnlocked(d.achievements) + " of " + AchievementIds.All.Length + " unlocked; tug wins " + d.tugWinsTotal);
        }
    }
}
