using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SecondCursor.Core;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Developer test bridge: lets an external tool (or a person with a text editor) drive the game in
    /// the Editor through a command file, for repeatable play-testing without touching the real mouse.
    ///
    /// It is inert unless the folder <c>Library/SecondCursorBridge</c> exists. Write commands (one per
    /// line) to <c>Library/SecondCursorBridge/cmd.txt</c>; the bridge runs them and writes
    /// <c>out.txt</c>, then deletes <c>cmd.txt</c>. Screenshots go to <c>Library/SecondCursorBridge/shots</c>.
    ///
    /// While attached, the player's cursor is driven by a scripted input backend (virtual 960x540 pixel
    /// coordinates, origin bottom-left) instead of the real mouse; real keys still work. See
    /// <see cref="Help"/> for the command list.
    /// </summary>
    [InitializeOnLoad]
    public static class SecondCursorTestBridge
    {
        const string Help =
            "Editor: refresh | play | stop | build | status | errors | clearerrors | help\n" +
            "Game:   jump NAME (fresh shift) | beat NAME (in place) | waittext TEXT [timeout] | waitbeat NAME [timeout] | waitlog TEXT [timeout] | waitflag FLAG [timeout] | waittask ID [timeout]\n" +
            "        waitidle [timeout] | wait SECONDS | speed X | crt on|off | restart | realinput | scriptinput\n" +
            "        shot NAME | gameshot NAME | dump | ids [FILTER] | texts [FILTER] | log [N] | windows\n" +
            "Mouse:  move X Y [DUR] | down | up | click X Y | dclick X Y | rclick X Y | drag X1 Y1 X2 Y2 [DUR] | scroll N\n" +
            "        clickid ID | dclickid ID | rclickid ID | moveid ID | dragid ID X Y [DUR] | dragto ID TARGETID [DUR]\n" +
            "        clicktext TEXT | dclicktext TEXT\n" +
            "Keys:   key NAME (GameKey) | type TEXT (\\n = Enter)\n" +
            "Coordinates are virtual pixels (960x540, origin bottom-left).";

        static readonly string Dir = Path.GetFullPath("Library/SecondCursorBridge");
        static string CmdPath => Path.Combine(Dir, "cmd.txt");
        static string OutPath => Path.Combine(Dir, "out.txt");
        static string ResumePath => Path.Combine(Dir, "resume.txt");
        static string PartialPath => Path.Combine(Dir, "partial.txt");
        static string CompileErrorsPath => Path.Combine(Dir, "compile_errors.txt");
        public static string ShotDir => Path.Combine(Dir, "shots");

        static readonly List<string> ConsoleErrors = new List<string>();
        static int _errorsReported;

        static List<string> _lines;
        static int _index;
        static StringBuilder _out;
        static IEnumerator _current;
        static double _nextPoll;
        // The real mouse drives the game unless a script is running a pointer/keyboard command
        // (or "scriptinput" made scripted input sticky), so manual Play is never left with a dead mouse.
        static bool _scripted;
        static bool _sticky;
        static ScriptedInput _input;

        static SecondCursorTestBridge()
        {
            EditorApplication.update += Update;
            Application.logMessageReceived += OnLog;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationStarted += _ => { if (Enabled && File.Exists(CompileErrorsPath)) File.Delete(CompileErrorsPath); };
        }

        static bool Enabled => Directory.Exists(Dir);

        // ------------------------------------------------------------------ plumbing

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.Contains("NoSubscription") || condition.Contains("DefaultScenario")) return; // Unity-internal noise, not the game
            string first = stackTrace?.Split('\n').FirstOrDefault(l => l.Contains("SecondCursor")) ?? "";
            ConsoleErrors.Add(type + ": " + condition + (first.Length > 0 ? "  @ " + first.Trim() : ""));
            if (ConsoleErrors.Count > 200) ConsoleErrors.RemoveAt(0);
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            if (!Enabled) return;
            var errors = messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message).ToArray();
            if (errors.Length > 0) File.AppendAllLines(CompileErrorsPath, errors);
        }

        static void Update()
        {
            if (!Enabled) return;
            AttachInput();
            if (_current == null && _lines == null)
            {
                if (EditorApplication.timeSinceStartup < _nextPoll) return;
                _nextPoll = EditorApplication.timeSinceStartup + 0.2;
                if (!TryResume() && !TryStart()) return;
            }
            if (_current == null)
            {
                if (_index >= _lines.Count)
                {
                    Finish();
                    return;
                }
                string line = _lines[_index++].Trim();
                if (line.Length == 0 || line.StartsWith("#")) return;
                _out.Append("> ").Append(line).Append('\n');
                _errorsAtCommand = ConsoleErrors.Count;
                _current = Run(line);
            }
            bool more;
            try
            {
                more = _current.MoveNext();
            }
            catch (Exception e)
            {
                _out.Append("  EXCEPTION ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
                more = false;
            }
            if (!more)
            {
                _current = null;
                for (int i = _errorsAtCommand; i < ConsoleErrors.Count; i++) _out.Append("  !! ").Append(ConsoleErrors[i]).Append('\n');
                _errorsAtCommand = ConsoleErrors.Count;
            }
        }

        static int _errorsAtCommand;

        static bool TryStart()
        {
            if (!File.Exists(CmdPath)) return false;
            string text;
            try { text = File.ReadAllText(CmdPath); }
            catch (IOException) { return false; }
            File.Delete(CmdPath);
            if (File.Exists(OutPath)) File.Delete(OutPath);
            _lines = text.Replace("\r", "").Split('\n').ToList();
            _index = 0;
            _out = new StringBuilder();
            return true;
        }

        static bool TryResume()
        {
            if (!File.Exists(ResumePath)) return false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return false;
            _lines = File.ReadAllText(ResumePath).Replace("\r", "").Split('\n').ToList();
            _index = 0;
            _out = new StringBuilder(File.Exists(PartialPath) ? File.ReadAllText(PartialPath) : "");
            File.Delete(ResumePath);
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
            AppendCompileErrors();
            return true;
        }

        /// <summary>Saves the rest of the script before something that may reload the domain.</summary>
        static void Persist()
        {
            File.WriteAllText(ResumePath, string.Join("\n", _lines.Skip(_index)));
            File.WriteAllText(PartialPath, _out.ToString());
        }

        static void ClearPersisted()
        {
            if (File.Exists(ResumePath)) File.Delete(ResumePath);
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
        }

        static void AppendCompileErrors()
        {
            if (!EditorUtility.scriptCompilationFailed || !File.Exists(CompileErrorsPath)) return;
            // Kept until the next compilation starts, so "play" can report them too.
            foreach (var l in File.ReadAllLines(CompileErrorsPath).Distinct()) _out.Append("  COMPILE ").Append(l).Append('\n');
        }

        static readonly HashSet<string> PointerCommands = new HashSet<string>
        {
            "move", "down", "up", "click", "dclick", "rclick", "drag", "scroll", "key", "type",
            "clickid", "dclickid", "rclickid", "moveid", "dragid", "dragto", "clicktext", "dclicktext",
        };

        static void Finish()
        {
            if (!_sticky)
            {
                _scripted = false;
                AttachInput();
            }
            _out.Append("= done\n");
            File.WriteAllText(OutPath + ".tmp", _out.ToString());
            if (File.Exists(OutPath)) File.Delete(OutPath);
            File.Move(OutPath + ".tmp", OutPath);
            _lines = null;
            _out = null;
        }

        static void Say(string s) => _out.Append("  ").Append(s).Append('\n');

        static GameServices G => GameRoot.Instance != null ? GameRoot.Instance.G : null;

        static void AttachInput()
        {
            var g = G;
            if (g == null || !EditorApplication.isPlaying) return;
            if (_scripted && !(g.Input is ScriptedInput))
            {
                _input = new ScriptedInput(g.Input, v => g.Screen.VirtualToScreen(v), g.Player.Position);
                g.Input = _input;
            }
            else if (!_scripted && g.Input is ScriptedInput s)
            {
                g.Input = s.Real;
                _input = null;
            }
        }

        // ------------------------------------------------------------------ commands

        static IEnumerator Run(string line)
        {
            string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string cmd = a[0].ToLowerInvariant();
            string rest = line.Length > a[0].Length ? line.Substring(a[0].Length).Trim() : "";
            switch (cmd)
            {
                case "help": Say(Help.Replace("\n", "\n  ")); return Done();
                case "status": return Status();
                case "errors":
                    foreach (var e in ConsoleErrors) Say(e);
                    Say(ConsoleErrors.Count + " error(s)");
                    return Done();
                case "clearerrors": ConsoleErrors.Clear(); _errorsAtCommand = 0; return Done();
                case "refresh": return Refresh();
                case "play": return Play(true);
                case "stop": return Play(false);
                case "realinput": _scripted = _sticky = false; AttachInput(); return Done();
                case "scriptinput": _scripted = _sticky = true; AttachInput(); return Done();
                case "wait": return WaitSeconds(F(a, 1, 1f));
                case "build": return Build();
            }
            return GameCommand(cmd, a, rest);
        }

        static IEnumerator GameCommand(string cmd, string[] a, string rest)
        {
            // Every game command waits for the game to exist (e.g. right after "play").
            double until = EditorApplication.timeSinceStartup + 30;
            while ((G == null || G.Director == null || string.IsNullOrEmpty(G.Director.CurrentBeat) || !EditorApplication.isPlaying)
                   && EditorApplication.timeSinceStartup < until) yield return null;
            var g = G;
            if (g == null)
            {
                Say("ERROR: game not running");
                yield break;
            }
            if (PointerCommands.Contains(cmd)) _scripted = true;
            AttachInput();
            IEnumerator inner = null;
            switch (cmd)
            {
                case "beat": g.Director.JumpTo(a[1]); inner = WaitSeconds(0.3f); break;
                case "jump": GameBootstrap.Restart(a[1]); inner = WaitFor(() => G != null && G.Director != null && G.Director.CurrentBeat == a[1], 20f, "fresh shift at " + a[1]); break;
                case "waitbeat": inner = WaitFor(() => g.Director.CurrentBeat == a[1], F(a, 2, 120f), "beat " + a[1]); break;
                case "waitflag": inner = WaitFor(() => g.Flags.Has(a[1]), F(a, 2, 120f), "flag " + a[1]); break;
                case "waittask": inner = WaitFor(() => g.Tasks.IsCompleted(a[1]), F(a, 2, 120f), "task " + a[1]); break;
                case "waitidle": inner = WaitFor(() => !g.Entity.Busy, F(a, 1, 60f), "entity idle"); break;
                case "waittext":
                {
                    // waittext TEXT... [timeout]: until that pixel text is visible on screen.
                    bool hasTimeout = a.Length > 2 && float.TryParse(a[a.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                    string wanted = hasTimeout ? string.Join(" ", a.Skip(1).Take(a.Length - 2)) : rest;
                    inner = WaitFor(() => FindText(wanted) != null, hasTimeout ? F(a, a.Length - 1, 60f) : 60f, "text '" + wanted + "'");
                    break;
                }
                case "waitlog": inner = WaitLog(a.Length > 2 && float.TryParse(a[a.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? string.Join(" ", a.Skip(1).Take(a.Length - 2)) : rest, a.Length > 2 ? F(a, a.Length - 1, 60f) : 60f); break;
                case "speed": Time.timeScale = F(a, 1, 1f); break;
                case "crt": g.Fx.CrtEnabled = a.Length < 2 || a[1] != "off"; break;
                case "audio":
                {
                    // Loudness of what the listener is outputting right now (0 = silence).
                    var buf = new float[2048];
                    AudioListener.GetOutputData(buf, 0);
                    float sum = 0f, peak = 0f;
                    foreach (float v in buf) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
                    Say("rms=" + Mathf.Sqrt(sum / buf.Length).ToString("0.0000") + " peak=" + peak.ToString("0.000") + " listenerVolume=" + AudioListener.volume + " paused=" + AudioListener.pause
                        + " sources=" + SceneObjects.All<AudioSource>().Count(s => s.isPlaying));
                    break;
                }
                case "fx":
                {
                    // fx            -> list the CRT overlay graphics and their state
                    // fx NAME on|off -> toggle one (by field name, e.g. _vignette)
                    foreach (var f in typeof(FX.VisualFx).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (!typeof(UnityEngine.UI.Graphic).IsAssignableFrom(f.FieldType)) continue;
                        var gr = (UnityEngine.UI.Graphic)f.GetValue(g.Fx);
                        if (gr == null) continue;
                        if (a.Length > 2 && f.Name.TrimStart('_').Equals(a[1].TrimStart('_'), StringComparison.OrdinalIgnoreCase)) gr.enabled = a[2] == "on";
                        Say(f.Name + " enabled=" + gr.enabled + " color=" + gr.color + " mat=" + (gr.materialForRendering != null ? gr.materialForRendering.shader.name : "-"));
                    }
                    break;
                }
                case "restart": GameBootstrap.Restart(); inner = WaitSeconds(0.5f); break;
                case "shot": SaveScreen(g, a.Length > 1 ? a[1] : "shot"); break;
                case "gameshot": inner = GameShot(a.Length > 1 ? a[1] : "gameshot"); break;
                case "dump": Dump(g); break;
                case "windows": Windows(g); break;
                case "ids": Ids(a.Length > 1 ? rest : null); break;
                case "texts": Texts(a.Length > 1 ? rest : null); break;
                case "log":
                    foreach (var e in GameLog.Recent((int)F(a, 1, 20f))) Say(e.Time.ToString("0.0") + " " + e);
                    break;
                case "move": _input.MoveTo(V(a, 1), F(a, 3, 0.25f)); inner = Drain(); break;
                case "down": _input.Press(); inner = Drain(); break;
                case "up": _input.Release(); inner = Drain(); break;
                case "click": _input.MoveTo(V(a, 1), 0.2f); _input.Click(1); inner = Drain(); break;
                case "dclick": _input.MoveTo(V(a, 1), 0.2f); _input.Click(2); inner = Drain(); break;
                case "rclick": _input.MoveTo(V(a, 1), 0.2f); _input.RightClick(); inner = Drain(); break;
                case "drag": _input.MoveTo(V(a, 1), 0.2f); _input.Drag(V(a, 3), F(a, 5, 0.5f)); inner = Drain(); break;
                case "scroll": _input.ScrollBy(F(a, 1, 1f)); inner = Drain(); break;
                case "key": _input.Key((GameKey)Enum.Parse(typeof(GameKey), a[1], true)); inner = Drain(); break;
                case "type": _input.TypeText(rest.Replace("\\n", "\n")); inner = Drain(); break;
                case "clickid":
                case "dclickid":
                case "rclickid":
                case "moveid":
                {
                    var it = FindId(g, a[1]);
                    if (it == null) { Say("ERROR: no element '" + a[1] + "'"); break; }
                    _input.MoveTo(VisiblePoint(g, it), 0.25f);
                    if (cmd == "clickid") _input.Click(1);
                    else if (cmd == "dclickid") _input.Click(2);
                    else if (cmd == "rclickid") _input.RightClick();
                    inner = Drain();
                    break;
                }
                case "dragid":
                case "dragto":
                {
                    var it = FindId(g, a[1]);
                    if (it == null) { Say("ERROR: no element '" + a[1] + "'"); break; }
                    Vector2 to;
                    float dur;
                    if (cmd == "dragto")
                    {
                        var target = FindId(g, a[2]);
                        if (target == null) { Say("ERROR: no element '" + a[2] + "'"); break; }
                        to = VisiblePoint(g, target);
                        dur = F(a, 3, 0.6f);
                    }
                    else
                    {
                        to = V(a, 2);
                        dur = F(a, 4, 0.6f);
                    }
                    _input.MoveTo(VisiblePoint(g, it), 0.25f);
                    _input.Drag(to, dur);
                    inner = Drain();
                    break;
                }
                case "clicktext":
                case "dclicktext":
                {
                    var t = FindText(rest);
                    if (t == null) { Say("ERROR: no text '" + rest + "'"); break; }
                    _input.MoveTo(t.rectTransform.WorldCenter(), 0.25f);
                    _input.Click(cmd == "clicktext" ? 1 : 2);
                    inner = Drain();
                    break;
                }
                default: Say("ERROR: unknown command '" + cmd + "' (try help)"); break;
            }
            if (inner != null) while (inner.MoveNext()) yield return inner.Current;
        }

        static IEnumerator Done() { yield break; }

        static IEnumerator WaitSeconds(float s)
        {
            double end = EditorApplication.timeSinceStartup + s;
            while (EditorApplication.timeSinceStartup < end) yield return null;
        }

        static IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            double start = EditorApplication.timeSinceStartup, end = start + timeout;
            while (EditorApplication.timeSinceStartup < end)
            {
                if (G == null) { Say("ERROR: game stopped"); yield break; }
                if (cond())
                {
                    Say("ok " + what + " after " + (EditorApplication.timeSinceStartup - start).ToString("0.0") + "s");
                    yield break;
                }
                yield return null;
            }
            Say("TIMEOUT waiting for " + what);
        }

        static IEnumerator WaitLog(string text, float timeout)
        {
            float since = Time.unscaledTime;
            var wait = WaitFor(() => GameLog.Recent(40).Any(e => e.Time >= since && e.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0), timeout, "log '" + text + "'");
            while (wait.MoveNext()) yield return null;
        }

        /// <summary>Waits until the scripted input has played every queued step.</summary>
        static IEnumerator Drain()
        {
            double end = EditorApplication.timeSinceStartup + 30;
            while (_input != null && _input.Pending > 0 && EditorApplication.timeSinceStartup < end) yield return null;
            // Two more frames so the router and the UI have reacted.
            int frame = Time.frameCount;
            while (Time.frameCount < frame + 2 && EditorApplication.timeSinceStartup < end) yield return null;
        }

        static IEnumerator Status()
        {
            Say("playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling + " updating=" + EditorApplication.isUpdating);
            var g = G;
            if (g != null)
            {
                Say("beat=" + g.Director.CurrentBeat + " t=" + Time.time.ToString("0.0") + " scale=" + Time.timeScale + " frame=" + Time.frameCount + " input=" + g.Input.GetType().Name
                    + " fps=" + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0") + " screen=" + Screen.width + "x" + Screen.height + " rtScale=" + g.Screen.TextureScale);
                Say("player=" + Fmt(g.Player.Position) + " entity=" + Fmt(g.Entity.Agent.Position) + " visible=" + g.Entity.IsVisible + " state=" + g.Entity.State + " action=" + (g.Entity.CurrentAction ?? "-"));
            }
            Say(ConsoleErrors.Count + " console error(s)");
            yield break;
        }

        static IEnumerator Refresh()
        {
            Persist();
            AssetDatabase.Refresh();
            // If scripts changed and compiled, the domain reloads and TryResume continues the script.
            // Compilation can start a few editor updates after the refresh returns.
            double settle = EditorApplication.timeSinceStartup + 3;
            while (!EditorApplication.isCompiling && EditorApplication.timeSinceStartup < settle) yield return null;
            double end = EditorApplication.timeSinceStartup + 180;
            while ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < end) yield return null;
            yield return WaitSeconds(0.5f);
            ClearPersisted();
            AppendCompileErrors();
            Say("refreshed (no reload)");
        }

        static IEnumerator Build()
        {
            if (EditorApplication.isPlaying) { Say("ERROR: stop Play mode before building"); yield break; }
            var report = SecondCursorBuild.BuildWindows();
            Say(SecondCursorBuild.Summary(report).Replace('\n', ' '));
        }

        static IEnumerator Play(bool play)
        {
            if (EditorApplication.isPlaying == play) { Say("already " + (play ? "playing" : "stopped")); yield break; }
            if (play && EditorUtility.scriptCompilationFailed)
            {
                Say("ERROR: scripts have compile errors, Play is blocked");
                AppendCompileErrors();
                yield break;
            }
            Persist();
            EditorApplication.isPlaying = play;
            // Entering play mode reloads the domain; the script continues from TryResume.
            double end = EditorApplication.timeSinceStartup + 60;
            while (EditorApplication.isPlaying != play && EditorApplication.timeSinceStartup < end) yield return null;
            ClearPersisted();
        }

        // ------------------------------------------------------------------ inspection

        static void SaveScreen(GameServices g, string name)
        {
            Directory.CreateDirectory(ShotDir);
            var rt = g.Screen.ScreenTexture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            int k = g.Screen.TextureScale;
            if (k > 1)
            {
                // The screen is supersampled: keep shots at the virtual 960x540 (one sample per virtual pixel).
                var src = tex.GetPixels32();
                var dst = new Color32[ScreenRig.Width * ScreenRig.Height];
                for (int y = 0; y < ScreenRig.Height; y++)
                    for (int x = 0; x < ScreenRig.Width; x++)
                        dst[y * ScreenRig.Width + x] = src[(y * k) * rt.width + x * k];
                UnityEngine.Object.DestroyImmediate(tex);
                tex = new Texture2D(ScreenRig.Width, ScreenRig.Height, TextureFormat.RGB24, false);
                tex.SetPixels32(dst);
                tex.Apply();
            }
            string path = Path.Combine(ShotDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Say("saved " + path);
        }

        static IEnumerator GameShot(string name)
        {
            Directory.CreateDirectory(ShotDir);
            string path = Path.Combine(ShotDir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            double end = EditorApplication.timeSinceStartup + 5;
            while (!File.Exists(path) && EditorApplication.timeSinceStartup < end) yield return null;
            Say(File.Exists(path) ? "saved " + path : "ERROR: game view capture failed (is the Game view visible?)");
        }

        static void Dump(GameServices g)
        {
            var e = g.Entity;
            Say("beat=" + g.Director.CurrentBeat + " (" + (Time.time - g.Director.BeatStartedAt).ToString("0.0") + "s)  t=" + Time.time.ToString("0.0") + " scale=" + Time.timeScale);
            Say("player " + Fmt(g.Player.Position) + " held=" + g.Player.Held + " hovered=" + Name(g.Player.Hovered) + " payload=" + (g.Player.Payload != null ? g.Player.Payload.FileId : "-"));
            Say("entity " + Fmt(e.Agent.Position) + " visible=" + e.IsVisible + " phase=" + e.Phase + " state=" + e.State + " action=" + (e.CurrentAction ?? "-") + " brain=" + e.Brain.Enabled + " defenses=" + e.Brain.Defenses);
            Say("fight=" + g.Conflict.IsFighting + " shredBusy=" + g.Shred.Busy);
            Windows(g);
            Say("tasks: " + string.Join(" ", g.Tasks.Tasks.Where(t => t.State != Core.Tasks.TaskState.Hidden).Select(t => t.Id + "=" + t.State)));
            Say("flags: " + string.Join(" ", g.Flags.AllFlags));
            foreach (var l in GameLog.Recent(12)) Say("log " + l.Time.ToString("0.0") + " " + l);
        }

        static void Windows(GameServices g)
        {
            foreach (var w in g.Windows.Windows)
            {
                if (w.IsClosed) continue;
                var r = w.WorldRect;
                Say("window '" + w.Title + "' app=" + w.AppId + " rect=" + Fmt(r) + (w.IsActive ? " ACTIVE" : "") + (w.IsMinimized ? " MIN" : "") + (w.IsMaximized ? " MAX" : ""));
            }
        }

        static void Ids(string filter)
        {
            var g = G;
            var list = Interactable.All.Where(i => i != null && i.isActiveAndEnabled && i.interactable && !string.IsNullOrEmpty(i.elementId))
                .Where(i => filter == null || i.elementId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(i => i.elementId).ToList();
            foreach (var i in list)
            {
                bool top = g != null && g.Router.HitTest(i.Center) == i;
                Say(i.elementId + " @" + Fmt(i.Center) + (top ? "" : " (covered)") + (WindowOf(i) != null ? " in " + WindowOf(i).AppId : ""));
            }
            Say(list.Count + " element(s)");
        }

        static void Texts(string filter)
        {
            var list = SceneObjects.All<PixelText>()
                .Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text) && t.color.a > 0.05f)
                .Where(t => filter == null || t.text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var t in list)
            {
                string s = t.text.Replace("\n", "\\n");
                if (s.Length > 90) s = s.Substring(0, 90) + "...";
                Say("\"" + s + "\" @" + Fmt(t.rectTransform.WorldRect()));
            }
            Say(list.Count + " text(s)");
        }

        /// <summary>
        /// Finds an element by id. "APP/ID" limits the search to that app's window (e.g. "mail/window.close");
        /// "ID#N" picks the Nth match (0-based, top-to-bottom then left-to-right). Uncovered elements in the
        /// active window win.
        /// </summary>
        static Interactable FindId(GameServices g, string id)
        {
            string app = null;
            int nth = -1;
            int hash = id.LastIndexOf('#');
            if (hash > 0 && int.TryParse(id.Substring(hash + 1), out int n)) { nth = n; id = id.Substring(0, hash); }
            int slash = id.IndexOf('/');
            if (slash > 0) { app = id.Substring(0, slash); id = id.Substring(slash + 1); }
            var pool = Interactable.All.Where(i => i != null && i.isActiveAndEnabled && i.interactable)
                .Where(i => app == null || WindowOf(i)?.AppId == app).ToList();
            var candidates = pool.Where(i => i.elementId == id).ToList();
            if (candidates.Count == 0) candidates = pool.Where(i => i.elementId.EndsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (nth >= 0)
                return candidates.OrderByDescending(i => Mathf.Round(i.Center.y)).ThenBy(i => i.Center.x).ElementAtOrDefault(nth);
            return candidates.Where(i => g.Router.HitTest(i.Center) == i).OrderByDescending(i => WindowOf(i) != null && WindowOf(i).IsActive).FirstOrDefault()
                   ?? candidates.FirstOrDefault();
        }

        /// <summary>Where a person would click: the centre if it is uncovered, else the visible part nearest to it.</summary>
        static Vector2 VisiblePoint(GameServices g, Interactable it)
        {
            Vector2 c = it.Center;
            if (g.Router.HitTest(c) == it) return c;
            Rect r = it.WorldRect;
            Vector2 best = c;
            float bestDistance = float.MaxValue;
            for (float y = r.yMin + 2f; y < r.yMax - 1f; y += 4f)
                for (float x = r.xMin + 2f; x < r.xMax - 1f; x += 4f)
                {
                    var p = new Vector2(x, y);
                    float d = (p - c).sqrMagnitude;
                    if (d < bestDistance && g.Router.HitTest(p) == it)
                    {
                        best = p;
                        bestDistance = d;
                    }
                }
            return best;
        }

        static OS.OSWindow WindowOf(Interactable i) => i.Window != null ? i.Window : i.GetComponentInParent<OS.OSWindow>();

        static PixelText FindText(string text)
        {
            var all = SceneObjects.All<PixelText>()
                .Where(t => t.isActiveAndEnabled && t.text != null && t.color.a > 0.05f).ToList();
            return all.FirstOrDefault(t => string.Equals(t.text.Trim(), text, StringComparison.OrdinalIgnoreCase))
                   ?? all.FirstOrDefault(t => t.text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static string Name(Interactable i) => i == null ? "-" : (string.IsNullOrEmpty(i.elementId) ? i.name : i.elementId);
        static string Fmt(Vector2 v) => "(" + v.x.ToString("0") + "," + v.y.ToString("0") + ")";
        static string Fmt(Rect r) => "(" + r.x.ToString("0") + "," + r.y.ToString("0") + " " + r.width.ToString("0") + "x" + r.height.ToString("0") + ")";

        static float F(string[] a, int i, float fallback) =>
            a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        static Vector2 V(string[] a, int i) => new Vector2(F(a, i, 0f), F(a, i + 1, 0f));

        // ------------------------------------------------------------------ scripted input

        /// <summary>
        /// Input backend that plays queued pointer/keyboard steps, one edge per frame, in virtual pixels.
        /// Real keys still pass through, so F1-F6 keep working while a script drives the mouse.
        /// </summary>
        sealed class ScriptedInput : IInputBackend
        {
            public readonly IInputBackend Real;
            readonly Func<Vector2, Vector2> _toScreen;
            readonly Queue<Func<float, int>> _steps = new Queue<Func<float, int>>();
            Vector2 _pos;
            bool _held, _down, _up, _rdown, _rup;
            float _scroll;
            string _typed = "";
            readonly HashSet<GameKey> _keys = new HashSet<GameKey>();

            const int Continue = 0, Next = 1, NextFrame = 2;

            public ScriptedInput(IInputBackend real, Func<Vector2, Vector2> toScreen, Vector2 start)
            {
                Real = real;
                _toScreen = toScreen;
                _pos = start;
            }

            public int Pending => _steps.Count;

            public Vector2 MouseScreenPosition => _toScreen(_pos);
            public bool LeftHeld => _held;
            public bool LeftDown => _down;
            public bool LeftUp => _up;
            public bool RightDown => _rdown;
            public bool RightUp => _rup;
            public float Scroll => _scroll;
            public string TypedText => _typed + Real.TypedText;
            public bool KeyDown(GameKey key) => _keys.Contains(key) || Real.KeyDown(key);
            public bool KeyHeld(GameKey key) => _keys.Contains(key) || Real.KeyHeld(key);

            public void Poll()
            {
                Real.Poll();
                _down = _up = _rdown = _rup = false;
                _scroll = 0f;
                _typed = "";
                _keys.Clear();
                float dt = Time.unscaledDeltaTime;
                while (_steps.Count > 0)
                {
                    int r = _steps.Peek()(dt);
                    if (r == Continue) break;
                    _steps.Dequeue();
                    if (r == NextFrame) break;
                    dt = 0f;
                }
            }

            public void Dispose() => Real.Dispose();

            public void MoveTo(Vector2 target, float duration)
            {
                bool started = false;
                Vector2 from = default;
                float t = 0f;
                _steps.Enqueue(dt =>
                {
                    if (!started) { started = true; from = _pos; }
                    t += dt;
                    float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
                    _pos = Vector2.Lerp(from, target, Mathf.SmoothStep(0f, 1f, k));
                    return k >= 1f ? Next : Continue;
                });
            }

            public void Press() => _steps.Enqueue(dt => { _held = true; _down = true; return NextFrame; });
            public void Release() => _steps.Enqueue(dt => { _held = false; _up = true; return NextFrame; });

            public void Pause(float seconds)
            {
                float t = 0f;
                _steps.Enqueue(dt => (t += dt) >= seconds ? Next : Continue);
            }

            public void Click(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Press();
                    Release();
                }
            }

            public void RightClick()
            {
                _steps.Enqueue(dt => { _rdown = true; return NextFrame; });
                _steps.Enqueue(dt => { _rup = true; return NextFrame; });
            }

            public void Drag(Vector2 to, float duration)
            {
                Press();
                Pause(0.08f);
                MoveTo(to, duration);
                Pause(0.12f);
                Release();
            }

            public void ScrollBy(float notches) => _steps.Enqueue(dt => { _scroll = notches; return NextFrame; });

            public void Key(GameKey key) => _steps.Enqueue(dt => { _keys.Add(key); return NextFrame; });

            public void TypeText(string text)
            {
                foreach (char c in text)
                {
                    char ch = c;
                    _steps.Enqueue(dt =>
                    {
                        _typed = ch.ToString();
                        if (ch == '\n') _keys.Add(GameKey.Enter);
                        return NextFrame;
                    });
                }
            }
        }
    }
}
