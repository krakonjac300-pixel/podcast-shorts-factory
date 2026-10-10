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
    public static partial class SecondCursorTestBridge
    {
        const string Help =
            "Editor: refresh | play | stop | build | status | errors | clearerrors | warnings | help\n" +
            "Game:   jump [NIGHT] NAME (fresh shift) | night N (fresh shift, boot) | beat NAME (in place) | waittext TEXT [timeout] | waitbeat NAME [timeout] | waitlog TEXT [timeout] | waitflag FLAG [timeout] | waittask ID [timeout]\n" +
            "        waitidle [timeout] | wait SECONDS | speed X | crt on|off | restart | realinput | scriptinput\n" +
            "State:  setflag NAME | clearflag NAME | trust VALUE | assist LEVEL | tug win|lose|real | setclock H M | difficulty normal|story | checkpoint save|load | save\n" +
            "        stage N (Custodial rounds) | waitending ID [timeout]\n" +
            "        shot NAME | gameshot NAME | dump | ids [FILTER] | texts [FILTER] | log [N] | windows\n" +
            "Mouse:  move X Y [DUR] | down | up | click X Y | dclick X Y | rclick X Y | drag X1 Y1 X2 Y2 [DUR] | scroll N\n" +
            "        clickid ID | dclickid ID | rclickid ID | moveid ID | dragid ID X Y [DUR] | dragto ID TARGETID [DUR]\n" +
            "        clicktext TEXT | dclicktext TEXT\n" +
            "Keys:   key NAME (GameKey) | type TEXT (\\n = Enter, \\b = Backspace, \\s = Ctrl+S)\n" +
            ProgressHelp +
            BalanceHelp +
            PhaseGHelp +
            AudioHelp +
            PhaseOHelp +
            PhaseQ4Help +
            HaulHelp +
            "Coordinates are virtual pixels (960x540, origin bottom-left).";

        static readonly string Dir = Path.GetFullPath("Library/SecondCursorBridge");
        static string CmdPath => Path.Combine(Dir, "cmd.txt");
        static string OutPath => Path.Combine(Dir, "out.txt");
        static string ResumePath => Path.Combine(Dir, "resume.txt");
        static string PartialPath => Path.Combine(Dir, "partial.txt");
        static string CompileErrorsPath => Path.Combine(Dir, "compile_errors.txt");
        static string CompileWarningsPath => Path.Combine(Dir, "compile_warnings.txt");
        public static string ShotDir => Path.Combine(Dir, "shots");

        static readonly List<string> ConsoleErrors = new List<string>();

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
            RestoreTestState();
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationStarted += _ =>
            {
                if (!Enabled) return;
                if (File.Exists(CompileErrorsPath)) File.Delete(CompileErrorsPath);
                if (File.Exists(CompileWarningsPath)) File.Delete(CompileWarningsPath);
            };
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
            // The game's own warnings from the last compilation ("warnings" lists them).
            var warnings = messages.Where(m => m.type == CompilerMessageType.Warning && m.message.Contains("SecondCursor")).Select(m => m.message).ToArray();
            if (warnings.Length > 0) File.AppendAllLines(CompileWarningsPath, warnings);
        }

        static void Update()
        {
            if (!Enabled) return;
            ClockMonitorTick();
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
                _current = FlattenBridgeRoutine(Run(line));
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

        // EditorApplication.update drives this bridge, so it must execute nested waiters itself.
        static IEnumerator FlattenBridgeRoutine(IEnumerator routine)
        {
            var pending = new Stack<IEnumerator>();
            pending.Push(routine);
            try
            {
                while (pending.Count > 0)
                {
                    var current = pending.Peek();
                    if (!current.MoveNext())
                    {
                        pending.Pop();
                        (current as IDisposable)?.Dispose();
                        continue;
                    }
                    if (current.Current is IEnumerator nested) pending.Push(nested);
                    else yield return current.Current;
                }
            }
            finally
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
            }
        }

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
                case "warnings":
                {
                    var lines = File.Exists(CompileWarningsPath) ? File.ReadAllLines(CompileWarningsPath).Distinct().ToList() : new List<string>();
                    foreach (var l in lines) Say(l);
                    Say(lines.Count + " compiler warning(s) in the game's scripts (last compilation)");
                    return Done();
                }
                case "refresh": return Refresh();
                case "play": return Play(true);
                case "stop": return Play(false);
                case "realinput": _scripted = _sticky = false; AttachInput(); return Done();
                case "scriptinput": _scripted = _sticky = true; AttachInput(); return Done();
                case "wait": return WaitSeconds(F(a, 1, 1f));
                case "build": return Build();
            }
            var progress = TryEditorProgressCommand(cmd, a, rest) ?? TryEditorPhaseGCommand(cmd, a, rest) ?? TryEditorPhaseOCommand(cmd, a, rest);
            return progress ?? GameCommand(cmd, a, rest);
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
            // Debug commands stop the run counting for records and achievements (arm on purpose afterwards).
            if (Disarming.Contains(cmd)) g.Disarm("bridge " + cmd);
            IEnumerator inner = null;
            switch (cmd)
            {
                case "finalchoicecheck": FinalChoiceCheck(); break;
                case "bridgewaitcheck": inner = BridgeWaitCheck(); break;
                case "finalconsentcheck": inner = FinalConsentCheck(); break;
                case "finalreplaycheck": inner = FinalReplayCheck(); break;
                case "uireviewfixcheck": inner = UiReviewFixCheck(); break;
                case "storyreviewcheck": inner = StoryReviewFixesCheck(); break;
                case "continuitycheck": inner = ContinuityCheck(); break;
                case "readabilitycheck": inner = ReadabilityCheck(); break;
                case "noticereview": NoticeReviewCheck(); break;
                case "experiencecheck": inner = PlayerExperienceCheck(); break;
                case "openingcheck": inner = RunOpeningChecks(); break;
                case "beat": g.Director.JumpTo(a[1]); inner = WaitSeconds(0.3f); break;
                case "jump":
                {
                    // jump BEAT keeps the current night; jump N BEAT starts night N at that beat.
                    bool withNight = a.Length > 2 && int.TryParse(a[1], out _);
                    int night = withNight ? int.Parse(a[1]) : g.Night;
                    string beat = withNight ? a[2] : a[1];
                    GameBootstrap.Restart(night, beat);
                    inner = WaitFor(() => G != null && G.Director != null && G.Night == night && G.Director.CurrentBeat == beat, 20f, "fresh shift at night " + night + " " + beat);
                    break;
                }
                case "night":
                {
                    int night = (int)F(a, 1, 1f);
                    GameBootstrap.Restart(night);
                    inner = WaitFor(() => G != null && G.Director != null && G.Night == night && !string.IsNullOrEmpty(G.Director.CurrentBeat), 20f, "night " + night);
                    break;
                }
                case "setflag": g.Flags.Set(a[1]); break;
                case "clearflag": g.Flags.Clear(a[1]); break;
                case "trust": g.Memory.Seed(F(a, 1, 0f)); Say("trust=" + g.Memory.Trust.ToString("0.00")); break;
                case "assist": g.Assist.SetLevel((int)F(a, 1, 0f)); Say(AssistLine(g)); break;
                case "tug":
                {
                    string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "real";
                    Entity.ConflictSystem.ForcedOutcome = mode == "win" ? Core.Entity.TugOutcome.PlayerWins : mode == "lose" ? Core.Entity.TugOutcome.EntityWins : Core.Entity.TugOutcome.None;
                    Say("next contests: " + (Entity.ConflictSystem.ForcedOutcome == Core.Entity.TugOutcome.None ? "real" : Entity.ConflictSystem.ForcedOutcome.ToString()));
                    break;
                }
                case "setclock": g.Clock.Reset((int)F(a, 1, 1f), (int)F(a, 2, 52f)); Say("clock " + g.Clock.Format12()); break;
                case "difficulty":
                {
                    // Read when a night is built: save it and restart the current beat.
                    var mode = Core.Entity.DifficultyTable.ParseMode(a.Length > 1 ? a[1] : "normal");
                    SaveSystem.SetDifficulty(mode);
                    int night = g.Night;
                    string beat = g.Director.CurrentBeat;
                    GameBootstrap.Restart(night, beat);
                    inner = WaitFor(() => G != null && G.Director != null && G.Difficulty.Mode == mode && G.Director.CurrentBeat == beat, 20f, "difficulty " + mode + " at " + beat);
                    break;
                }
                case "checkpoint":
                    if (a.Length > 1 && a[1] == "load")
                    {
                        int night = g.Night;
                        GameBootstrap.Restart(night, null, true);
                        inner = WaitFor(() => G != null && G.Director != null && !string.IsNullOrEmpty(G.Director.CurrentBeat), 20f, "continue night " + night);
                    }
                    else
                    {
                        SaveSystem.SaveCheckpoint(g, g.Director.CurrentBeat);
                    }
                    break;
                case "save": SaveLines(); break;
                case "stage":
                    if (g.Rounds == null || g.Rounds.Model == null) { Say("ERROR: no Custodial round running"); break; }
                    g.Rounds.ForceStage((int)F(a, 1, 0f));
                    Say("rounds stage=" + g.Rounds.Model.Stage + " (" + g.Rounds.Model.FigureStage + ")");
                    break;
                case "waitending":
                {
                    // Also true if the ending was reached before this command started.
                    string wanted = "ending '" + (a.Length > 1 ? a[1] : "") + "'";
                    inner = WaitFor(() => GameLog.Recent(400).Any(e => e.ToString().IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0), F(a, 2, 300f), wanted);
                    break;
                }
                // The current root each frame (a menu choice builds a new one while the command waits).
                case "waitbeat": inner = WaitFor(() => G.Director != null && G.Director.CurrentBeat == a[1], F(a, 2, 120f), "beat " + a[1]); break;
                case "waitflag": inner = WaitFor(() => G.Flags.Has(a[1]), F(a, 2, 120f), "flag " + a[1]); break;
                case "waittask": inner = WaitFor(() => G.Tasks.IsCompleted(a[1]), F(a, 2, 120f), "task " + a[1]); break;
                case "waitidle": inner = WaitFor(() => G.Entity != null && !G.Entity.Busy, F(a, 1, 60f), "entity idle"); break;
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
                case "type": _input.TypeText(rest.Replace("\\n", "\n").Replace("\\b", "\b").Replace("\\s", ControlChars.Save.ToString())); inner = Drain(); break;
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
                default:
                    inner = TryGameProgressCommand(g, cmd, a, rest);
                    if (inner == null) Say("ERROR: unknown command '" + cmd + "' (try help)");
                    break;
            }
            if (inner != null) yield return inner;
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
            yield return wait;
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
                Say(AssistLine(g));
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
            try
            {
                var report = SecondCursorBuild.BuildWindows();
                Say(SecondCursorBuild.Summary(report).Replace('\n', ' '));
            }
            catch (UnityEditor.Build.BuildFailedException e)
            {
                Say("ERROR: build refused: " + e.Message);
            }
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
            if (!play) SaveSystem.Flush();   // Phase Q4 (CH8): the writer is empty before a tool reads the files
            EditorApplication.isPlaying = play;
            // Entering play mode reloads the domain; the script continues from TryResume.
            double end = EditorApplication.timeSinceStartup + 60;
            while (EditorApplication.isPlaying != play && EditorApplication.timeSinceStartup < end) yield return null;
            ClearPersisted();
        }
    }
}
