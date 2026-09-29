using System.Text;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Input;
using SecondCursor.Story;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Developer panel (F1). Jump to any story beat, summon/dismiss the second cursor, trigger its
    /// abilities, change its state, complete tasks, spawn files, change game speed, toggle CRT, reset.
    /// Also: F2 skip beat, F3 entity to mouse, F4 speed, F5 restart, F6 CRT. IMGUI on purpose: always
    /// crisp, independent of the fake OS it is debugging.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        GameServices _g;
        bool _open;
        Vector2 _scroll;
        int _speedIndex;
        static readonly float[] Speeds = { 1f, 2f, 4f, 0.5f };
        GUIStyle _box;
        GUIStyle _label;
        GUIStyle _hint;
        float _fps;
        // Button actions run in the next Update, never in the middle of an OnGUI pass: changing game state
        // (which also writes log lines) between IMGUI's Layout and input events breaks GUILayout.
        readonly System.Collections.Generic.List<System.Action> _pending = new System.Collections.Generic.List<System.Action>();
        System.Collections.Generic.List<LogEntry> _logSnapshot;

        void Defer(System.Action action) => _pending.Add(action);

        /// <summary>Keeps the panel open across the restart a beat jump makes.</summary>
        static bool _reopen;

        public static DebugOverlay Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Debug Overlay");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<DebugOverlay>();
            d._g = g;
            d._open = _reopen;
            _reopen = false;
            return d;
        }

        void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (_pending.Count > 0)
            {
                var run = _pending.ToArray();
                _pending.Clear();
                foreach (var action in run) action();
            }
            var input = _g.Input;
            // Developer keys exist only in the Editor and development builds; players never see the panel.
            if (Debug.isDebugBuild)
            {
                if (input.KeyDown(GameKey.F1)) _open = !_open;
                if (input.KeyDown(GameKey.F2)) _g.Director.SkipBeat();
                if (input.KeyDown(GameKey.F3)) SummonToMouse();
                if (input.KeyDown(GameKey.F4))
                {
                    _speedIndex = (_speedIndex + 1) % Speeds.Length;
                    if (!PauseMenu.IsPaused) Time.timeScale = Speeds[_speedIndex];
                    GameLog.Info(LogChannel.Debug, "Time scale " + Speeds[_speedIndex]);
                }
                if (input.KeyDown(GameKey.F5)) GameBootstrap.Restart();
            }
            if (input.KeyDown(GameKey.F6)) _g.Fx.CrtEnabled = !_g.Fx.CrtEnabled;
            Cursor.visible = _open || !Application.isFocused;
            // Keep clicks on the panel from also clicking the fake OS underneath.
            Vector2 m = input.MouseScreenPosition;
            MouseOverPanel = _open && m.x >= 10f && m.x <= 390f && m.y >= 10f && m.y <= Screen.height - 10f;
        }

        /// <summary>True while the real mouse is over the open debug panel.</summary>
        public static bool MouseOverPanel { get; private set; }

        public static float CurrentSpeed = 1f;

        void SummonToMouse()
        {
            var e = _g.Entity;
            e.Interrupt();
            e.Teleport(_g.Player.Position + new Vector2(40f, -20f));
            e.SetPresent(!e.IsVisible || e.View.Alpha < 0.5f, 0.2f);
        }

        void OnGUI()
        {
            var e = _g.Entity;
            GUI.color = Color.white;
            if (!_open)
            {
                // Developer hint only (Editor and development builds), faint and top-centre so it never
                // covers the taskbar or the Nexus button.
                if (!Debug.isDebugBuild) return;
                if (_hint == null) _hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 11 };
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, 2f, 300f, 18f), "F1 debug  |  " + _fps.ToString("0") + " fps", _hint);
                GUI.color = Color.white;
                return;
            }
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
                _label = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            }
            GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20), _box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("SECOND CURSOR debug  " + _fps.ToString("0") + " fps  x" + Time.timeScale, _label);
            GUILayout.Label("Beat: " + _g.Director.CurrentBeat + "   Phase: " + e.Phase + "   State: " + e.State +
                            "\nEntity action: " + (e.CurrentAction ?? "-") + "   Brain: " + (e.Brain.Enabled ? "ON" : "off") +
                            "  Defenses: " + e.Brain.Defenses + "  Grip: " + e.Brain.Grip.ToString("0.00") +
                            "\nFight: " + (_g.Conflict.IsFighting ? "YES strain " + _g.Conflict.Strain.ToString("0.00") + " share " + _g.Conflict.EntityShare.ToString("0.00") : "no") +
                            "\nTrust: " + _g.Memory.Trust.ToString("0.00") + "   Shred busy: " + _g.Shred.Busy, _label);

            GUILayout.Label("Jump to beat:", _label);
            GUILayout.BeginHorizontal();
            int col = 0;
            foreach (var beat in EventDirector.Beats)
            {
                // A fresh shift at that beat: jumping back never leaves later windows or entity state behind.
                if (GUILayout.Button(beat)) Defer(() => { _reopen = true; GameBootstrap.Restart(beat); });
                if (++col % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Entity:", _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Summon (F3)")) Defer(SummonToMouse);
            if (GUILayout.Button("Dismiss")) Defer(() => { e.Interrupt(); e.SetPresent(false, 0.3f); });
            if (GUILayout.Button(e.Brain.Enabled ? "Brain OFF" : "Brain ON")) Defer(() => e.Brain.Enabled = !e.Brain.Enabled);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close top window")) Defer(() =>
            {
                var w = _g.Windows.Active;
                if (w != null && w.CloseButton != null)
                {
                    e.SetPresent(true, 0.1f);
                    e.Run(e.ClickElement(w.CloseButton.Hit, MovementProfiles.Aggressive), "debug:close");
                }
            });
            if (GUILayout.Button("Type STOP")) Defer(() =>
            {
                e.SetPresent(true, 0.1f);
                e.Run(DebugType(), "debug:type");
            });
            if (GUILayout.Button("Mimic me")) Defer(() =>
            {
                e.SetPresent(true, 0.1f);
                e.Run(e.Replay(_g.Recorder.Last(5f, Time.time)), "debug:mimic");
            });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var st in new[] { EntityState.Observing, EntityState.Defensive, EntityState.Aggressive, EntityState.Panicked })
                if (GUILayout.Button(st.ToString())) Defer(() => e.State = st);
            GUILayout.EndHorizontal();

            GUILayout.Label("World:", _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Complete task")) Defer(() =>
            {
                var t = _g.Tasks.Current;
                if (t != null) _g.Tasks.ForceComplete(t.Id);
            });
            if (GUILayout.Button("Spawn file")) Defer(() =>
            {
                string id = "debug_" + Random.Range(1000, 9999);
                _g.Files.CreateFile(id, id + ".dat", "dat", ContentIds.FolderDesktop, "DEBUG FILE " + id, Actor.System);
            });
            if (GUILayout.Button("017 to desktop")) Defer(() =>
            {
                if (_g.Files.GetFile(ContentIds.File017)?.Shredded ?? false) _g.Files.Restore(ContentIds.File017, ContentIds.FolderDesktop, Actor.System);
                else _g.Files.Move(ContentIds.File017, ContentIds.FolderDesktop, Actor.System);
            });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Unlock camera")) Defer(() => _g.Flags.Set(Flags.CameraUnlocked));
            if (GUILayout.Button("CRT (F6)")) Defer(() => _g.Fx.CrtEnabled = !_g.Fx.CrtEnabled);
            if (GUILayout.Button("Glitch")) Defer(() => _g.Fx.Glitch(0.4f, 1f));
            if (GUILayout.Button("Restart (F5)")) Defer(GameBootstrap.Restart);
            GUILayout.EndHorizontal();

            if (_g.CameraRig != null)
            {
                GUILayout.Label("Camera 03 set:", _label);
                GUILayout.BeginHorizontal();
                foreach (CameraFeed.FigureStage s in System.Enum.GetValues(typeof(CameraFeed.FigureStage)))
                    if (GUILayout.Button(s.ToString())) Defer(() => _g.CameraRig.Figure = s);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Door", GUILayout.Width(40));
                _g.CameraRig.DoorOpen = GUILayout.HorizontalSlider(_g.CameraRig.DoorOpen, 0f, 1f);
                GUILayout.EndHorizontal();
            }

            var sb = new StringBuilder();
            sb.Append("Flags: ");
            foreach (var f in _g.Flags.AllFlags) sb.Append(f).Append(' ');
            sb.Append("\nTasks: ");
            foreach (var t in _g.Tasks.Tasks) sb.Append(t.Id).Append('=').Append(t.State).Append(' ');
            GUILayout.Label(sb.ToString(), _label);

            GUILayout.Label("Log:", _label);
            // Same number of rows in every event of this frame (IMGUI requires Layout and input passes to match).
            if (Event.current.type == EventType.Layout || _logSnapshot == null) _logSnapshot = GameLog.Recent(24);
            foreach (var line in _logSnapshot) GUILayout.Label(line.ToString(), _label);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        System.Collections.IEnumerator DebugType()
        {
            var n = _g.Apps.Find<NotepadApp>() ?? (NotepadApp)_g.Apps.Launch(AppIds.Notepad, _g.EntityAgent);
            yield return _g.Entity.Type(n, "STOP", 3f);
        }
    }
}
