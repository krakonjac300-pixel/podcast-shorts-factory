using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>The bridge's inspection commands (screenshots, dump, ids, texts, save) and its scripted input backend.</summary>
    public static partial class SecondCursorTestBridge
    {
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
            Say("fight=" + g.Conflict.IsFighting + (g.Conflict.IsMercyContest ? " (mercy)" : "") + " shredBusy=" + g.Shred.Busy);
            if (g.Gary != null)
                Say("gary " + Fmt(g.Gary.Agent.Position) + " visible=" + g.Gary.IsVisible + " alpha=" + g.Gary.View.Alpha.ToString("0.00", CultureInfo.InvariantCulture)
                    + " action=" + (g.Gary.CurrentAction ?? "-") + " devices=" + g.Taskbar.PointingDevices);
            if (g.Rounds != null && g.Rounds.Model != null)
                Say("rounds running=" + g.Rounds.Running + " stage=" + g.Rounds.Model.Stage + " (" + g.Rounds.Model.FigureStage + ") meter="
                    + g.Rounds.Model.Meter.ToString("0.0", CultureInfo.InvariantCulture) + " t=" + g.Rounds.Elapsed.ToString("0", CultureInfo.InvariantCulture));
            Say(AssistLine(g));
            Say("records " + (g.RecordsArmed ? "armed" : "held (" + g.RecordsHeldReason + ")") + " fromNightSelect=" + g.FromNightSelect
                + " elapsed=" + g.Director.NightElapsed.ToString("0", CultureInfo.InvariantCulture) + " deck=" + SteamBridge.OnDeck);
            Windows(g);
            Say("tasks: " + string.Join(" ", g.Tasks.Tasks.Where(t => t.State != Core.Tasks.TaskState.Hidden).Select(t => t.Id + "=" + t.State)));
            Say("flags: " + string.Join(" ", g.Flags.AllFlags));
            foreach (var l in GameLog.Recent(12)) Say("log " + l.Time.ToString("0.0") + " " + l);
        }

        /// <summary>Night, difficulty, assist state, grip, trust and the forced tug outcome on one line.</summary>
        static string AssistLine(GameServices g)
        {
            var s = g.Assist;
            return "night=" + g.Night + " difficulty=" + g.Difficulty.Mode + " assist=L" + s.Level + " lossStreak=" + s.LossStreak.ToString("0.0", CultureInfo.InvariantCulture)
                   + " winStreak=" + s.WinStreak + " mercyArmed=" + s.MercyArmed + " grip=" + g.Entity.Brain.Grip.ToString("0.00", CultureInfo.InvariantCulture)
                   + " trust=" + g.Memory.Trust.ToString("0.00", CultureInfo.InvariantCulture) + " clock=" + g.Clock.Format12()
                   + " tug=" + (Entity.ConflictSystem.ForcedOutcome == Core.Entity.TugOutcome.None ? "real" : Entity.ConflictSystem.ForcedOutcome.ToString());
        }

        /// <summary>The progression part of progress.json.</summary>
        static void SaveLines()
        {
            var d = SaveSystem.Load();
            Say("version=" + d.version + " difficulty=" + d.difficulty + " nightUnlocked=" + d.nightUnlocked + " currentNight=" + d.currentNight
                + " trust=" + d.entityTrust.ToString("0.00", CultureInfo.InvariantCulture) + " assistCarry=" + d.assistCarry
                + " tugs=" + d.tugWinsTotal + "W/" + d.tugLossesTotal + "L endings=" + string.Join(",", d.endingsSeen));
            var cp = d.checkpoint;
            Say("checkpoint valid=" + cp.valid + " night=" + cp.night + " beat=" + cp.beat + " clock=" + cp.clockMinutes + " trust=" + cp.trust.ToString("0.00", CultureInfo.InvariantCulture)
                + " assist=" + cp.assistLevel + " flags=" + cp.flags.flags.Length);
            Say("memory flags: " + string.Join(" ", d.memory.flags) + " | counters: " + string.Join(" ", d.memory.counterKeys.Select((k, i) => k + "=" + d.memory.counterValues[i])));
            Say("playerLines: " + string.Join(" | ", d.playerLines) + " | nightStarts=" + string.Join(",", d.nightStarts));
            Say("checkpoint armed=" + cp.armed + " elapsed=" + cp.elapsed.ToString("0", CultureInfo.InvariantCulture) + " lastCompleted=" + d.lastCompletedNight
                + " best=" + string.Join(",", d.bestNightSeconds.Select(x => x.ToString("0", CultureInfo.InvariantCulture)))
                + " seconds=" + string.Join(",", d.nightSeconds.Select(x => x.ToString("0", CultureInfo.InvariantCulture)))
                + " startTrust=" + string.Join(",", d.nightStartTrust.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture))));
            Say("achievements (" + d.achievements.Length + "): " + string.Join(",", d.achievements) + " | folder=" + SaveSystem.Folder);
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

            /// <summary>
            /// Each frame <paramref name="step"/> gets the cursor position and dt and returns where the cursor goes
            /// next, or null to finish (Phase F balance commands steer a tug this way).
            /// </summary>
            public void Steer(Func<Vector2, float, Vector2?> step)
            {
                _steps.Enqueue(dt =>
                {
                    var next = step(_pos, dt);
                    if (!next.HasValue) return Next;
                    _pos = next.Value;
                    return Continue;
                });
            }

            /// <summary>Phase P: the button goes down or up this frame, from inside a steer (a slip and a re-grip in the middle of a pattern).</summary>
            public void ButtonNow(bool down)
            {
                _held = down;
                _down = down;
                _up = !down;
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
