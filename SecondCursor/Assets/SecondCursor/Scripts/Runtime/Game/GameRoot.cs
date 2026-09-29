using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Audio;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Entity;
using SecondCursor.FX;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.Story;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Composition root and frame driver. Builds every system in dependency order, then runs the
    /// per-frame pipeline in a fixed order: input -> pointer routing -> drag/conflict -> OS -> story.
    /// One explicit update order (instead of many independent Update methods) keeps the two cursors and
    /// the UI in lockstep, which matters for a game about two cursors fighting over the same object.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GameRoot : MonoBehaviour
    {
        public GameServices G { get; private set; }
        public static GameRoot Instance { get; private set; }

        /// <summary>Beat the next root starts at instead of boot (debug "Jump to beat" on a fresh shift).</summary>
        internal static string StartBeat;

        bool _built;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            GameLog.Output = e =>
            {
                string line = e.ToString();
                if (e.Level == LogLevel.Error) Debug.LogError(line);
                else if (e.Level == LogLevel.Warning) Debug.LogWarning(line);
                else Debug.Log(line);
            };
            GameLog.Clock = () => Time.unscaledTime;
            Application.targetFrameRate = 60;
            // Builds pause on focus loss (PauseMenu); in the Editor keep running so tools and testing work.
            if (Application.isEditor) Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            Build();
        }

        void Build()
        {
            var g = new GameServices { CoroutineHost = this };
            G = g;

            // Presentation
            g.Screen = gameObject.AddComponent<ScreenRig>();
            g.Screen.Build();
            g.Layers = new OSLayers(g.Screen.OsRoot);
            g.Fx = VisualFx.Create(g.Screen);
            g.Fx.SetBlack(true); // nothing is shown until the boot sequence (or a debug jump) starts
            g.Audio = AudioManager.Create(transform);
            EnsureAudioListener();
            var save = SaveSystem.Load();
            g.Audio.MasterVolume = save.masterVolume;
            g.Fx.CrtEnabled = save.crtEffects;
            g.Fx.ReduceFlashing = save.reduceFlashing;

            // Content + simulation
            g.Content = ContentLoader.Load();
            g.Files = new VirtualFileSystem(g.Content.FileSystem);
            g.Flags = new NarrativeFlags();
            g.Clock = new GameClock(1, 52);
            g.Memory = new EntityMemory();
            g.Recorder = new CursorRecorder();
            g.Dialogue = new DialogueEngine(g.Content);

            // Input: two cursors, one router
            g.Input = InputBackendFactory.Create();
            g.Router = new PointerRouter();
            g.Player = new CursorAgent(AgentKind.Player, "Player");
            g.EntityAgent = new CursorAgent(AgentKind.Entity, "Entity");
            g.Router.Register(g.EntityAgent);
            g.Router.Register(g.Player);
            g.Router.AnyPointerDown += PopupMenu.HandlePointerDown;
            g.DragDrop = new DragDropSystem(g.Layers.Drag, g.Router);
            g.EntityView = CursorView.Create(g.Layers.Cursors, g.EntityAgent, true);
            g.PlayerView = CursorView.Create(g.Layers.Cursors, g.Player, false);

            // Fake OS
            g.Windows = new WindowManager(g.Layers.Windows, g.Layers.Effects, g.Router);
            g.Notifications = Notifications.Create(g.Layers.Notifications);
            g.Mail = new MailService(g);
            g.Orders = new WorkOrderService(g);
            g.Tasks = new WorkTaskManager(g.Content.Tasks.tasks, new TaskWorld(g));
            g.Shred = new ShredService(g);
            g.Apps = new AppManager(g);
            g.Desktop = Desktop.Create(g);
            g.Taskbar = Taskbar.Create(g);
            g.Files.FileMoved += (f, from, to, actor) => g.Tasks.Evaluate();
            g.Files.FileShredded += (f, actor) => g.Tasks.Evaluate();

            // Story / entity
            g.CameraRig = SecurityCameraRig.Create(transform, g);
            g.Entity = EntityController.Create(g, transform);
            g.Conflict = ConflictSystem.Create(g, transform);
            g.Director = EventDirector.Create(g, transform);

            DebugOverlay.Create(g, transform);
            PauseMenu.Create(g, transform);

            _built = true;
            StartCoroutine(StartGame());
        }

        IEnumerator StartGame()
        {
            yield return G.Audio.GenerateAll();
            // A debug jump made while the sounds were generating has already started the story.
            string beat = StartBeat;
            StartBeat = null;
            if (!string.IsNullOrEmpty(G.Director.CurrentBeat)) yield break;
            if (System.Array.IndexOf(EventDirector.Beats, beat) >= 0) G.Director.JumpTo(beat);
            else G.Director.Begin();
        }

        void EnsureAudioListener()
        {
            var listeners = SceneObjects.All<AudioListener>();
            bool any = false;
            foreach (var l in listeners)
            {
                // A listener on a previous (restarting) game root is about to be destroyed: ignore it.
                var owner = l.GetComponentInParent<GameRoot>();
                if (l.enabled && l.gameObject.activeInHierarchy && (owner == null || owner == this)) any = true;
            }
            if (!any) gameObject.AddComponent<AudioListener>();
            // Other scene cameras would only render underneath the overlay: switch them off to save GPU time.
            foreach (var cam in SceneObjects.All<Camera>())
            {
                if (cam.transform.IsChildOf(transform)) continue;
                cam.enabled = false;
            }
        }

        void Update()
        {
            if (!_built) return;
            var g = G;
            float dt = Time.deltaTime;
            float unscaledDt = Time.unscaledDeltaTime;

            // 1. Real input -> the player's cursor.
            g.Input.Poll();
            var player = g.Player;
            if (player.Enabled)
            {
                player.Position = ScreenRig.ClampToScreen(g.Screen.ScreenToVirtual(g.Input.MouseScreenPosition));
                if (DebugOverlay.MouseOverPanel)
                {
                    player.SetButtonEdges(false, false, player.Held);
                }
                else
                {
                    player.SetButtonEdges(g.Input.LeftHeld, g.Input.LeftDown, g.Input.LeftUp);
                    player.RightClickEdge(g.Input.RightDown, g.Input.RightUp);
                    player.Scroll = g.Input.Scroll;
                }
            }
            player.UpdateVelocity(unscaledDt);
            g.Recorder.Add(Time.time, player.Position.ToCore(), player.Held);

            // 2. Pointer routing for both cursors (entity state was set by its coroutines last frame).
            g.Router.Process(Time.unscaledTime);

            // 3. Carried objects and cursor fights.
            g.DragDrop.Update(dt);
            g.Conflict.Tick(dt);

            // 4. The OS.
            g.Windows.Update(dt);
            g.Apps.Tick(dt);
            g.Shred.Tick(dt);
            if (!PauseMenu.IsPaused) g.Apps.RouteKeyboard(g.Input, player);
            g.Clock.Tick(dt);

            // 5. Feed the security camera the player's hand position (the seated figure mirrors it).
            if (g.CameraRig != null)
                g.CameraRig.PlayerHand = new Vector2(player.Position.x / ScreenRig.Width * 2f - 1f, player.Position.y / ScreenRig.Height * 2f - 1f);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) Cursor.visible = true;
        }

        /// <summary>Used by restart: the next GameRoot becomes the instance before this one is destroyed.</summary>
        internal static void ReleaseInstance() => Instance = null;

        void OnDestroy()
        {
            // The input backend belongs to this root (it may hold Input System subscriptions): always release it.
            G?.Input?.Dispose();
            // Only the current instance owns the global hooks (a restart creates the new root first).
            if (Instance != this && Instance != null) return;
            if (Instance == this) Instance = null;
            Cursor.visible = true;
            Sfx.Handler = null;
            GameLog.Output = null;
        }
    }
}
