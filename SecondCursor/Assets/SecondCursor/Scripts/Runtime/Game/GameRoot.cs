using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Audio;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
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
        /// <summary>Night the next root plays (1-3).</summary>
        internal static int StartNight = 1;
        /// <summary>The next root resumes the saved checkpoint of its night (Continue).</summary>
        internal static bool StartFromCheckpoint;
        /// <summary>The next root shows the title menu inside its boot beat instead of starting the night.</summary>
        internal static bool ShowTitle;
        /// <summary>The next root counts for records and achievements (a start chosen from the title or an end card).</summary>
        internal static bool NextRunArmed;
        /// <summary>Why the next root is armed or held (logged).</summary>
        internal static string NextRunReason;
        /// <summary>The next root was started from Night Select (it begins from the night's first-start memory).</summary>
        internal static bool FromNightSelect;
        /// <summary>Test bridge "achievements next": the next root counts even if a debug command started it.</summary>
        internal static bool ForceArmNext;

        bool _built;
        DeckKeyboard _deckKeyboard;
        /// <summary>The Windows pointer is showing (the operating system's default until the first LateUpdate).</summary>
        bool _pointerShown = true;

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
            // Builds pause on focus loss (PauseMenu); in the Editor keep running so tools and testing work.
            if (Application.isEditor) Application.runInBackground = true;
            var settings = SaveSystem.LoadSettings();
            if (SteamBridge.OnDeck && !settings.largeTextChosen)
            {
                // First launch on a Steam Deck: large reading text (the player can change it in Options).
                settings.largeText = true;
                settings.largeTextChosen = true;
                SaveSystem.SaveSettings(settings);
            }
            DisplaySettings.Apply(settings);
            if (settings.frameRate != DisplaySettings.FrameRate)
            {
                // An old save's "unlimited" (or a value no option has) was mapped to a real option: write that back.
                settings.frameRate = DisplaySettings.FrameRate;
                SaveSystem.SaveSettings(settings);
            }
            if (!string.IsNullOrEmpty(SaveSystem.DirOverride)) GameLog.Warn(LogChannel.System, "Saves are in a test folder: " + SaveSystem.DirOverride);
            if (SteamBridge.SimulateDeck) GameLog.Info(LogChannel.System, "Steam Deck simulated (test bridge)");
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
            var settings = SaveSystem.LoadSettings();
            g.Audio.MasterVolume = settings.masterVolume;
            g.Fx.CrtEnabled = settings.crtEffects;
            g.Fx.ReduceFlashing = settings.reduceFlashing;
            if (!Application.isEditor && Screen.fullScreen != settings.fullscreen) Display(settings.fullscreen);

            // Night, difficulty and memory
            g.Save = SaveSystem.Load();
            g.Night = Mathf.Clamp(StartNight, 1, GameBootstrap.MaxNight);
            g.Difficulty = MakeDifficulty(g.Night, DifficultyTable.ParseMode(g.Save.difficulty));
            g.Assist = AdaptiveAssist.ForNight(g.Difficulty, g.Save.assistCarry);
            GameLog.Info(LogChannel.System, "Night " + g.Night + ", difficulty " + g.Difficulty.Mode + ", assist level " + g.Assist.Level);

            // Records: a start chosen from the title or an end card counts; debug starts are held.
            bool armed = (NextRunArmed && string.IsNullOrEmpty(StartBeat)) || ForceArmNext;
            g.Arm(armed, ForceArmNext ? "armed for testing (bridge achievements next)" : !string.IsNullOrEmpty(NextRunReason) ? NextRunReason : armed ? "menu" : "debug start");
            ForceArmNext = false;
            NextRunArmed = false;
            NextRunReason = null;
            g.FromNightSelect = FromNightSelect;
            FromNightSelect = false;
            g.ShowTitle = ShowTitle;
            ShowTitle = false;

            // Content + simulation
            g.Content = ContentLoader.Load(g.Night);
            g.Content.Variant = SteamBridge.OnDeck ? "deck" : null;
            g.Files = new VirtualFileSystem(g.Content.FileSystem);
            g.Flags = new NarrativeFlags();
            g.Clock = new GameClock(1, 52);
            g.Memory = new EntityMemory();
            // What the earlier nights remember and the trust carried over (Night Select: from the night's first start).
            g.Save.StartStateFor(g.Night, g.FromNightSelect, out var memoryAtStart, out float trustAtStart);
            g.Flags.Merge(memoryAtStart);
            if (g.Night > 1) g.Memory.Seed(trustAtStart);
            g.Recorder = new CursorRecorder();
            g.Dialogue = new DialogueEngine(g.Content);

            // Input: up to three cursors, one router. Gary registers after the second cursor and before the
            // player, so the player's cursor still wins hit-test ties.
            g.Input = InputBackendFactory.Create();
            g.Router = new PointerRouter();
            g.Player = new CursorAgent(AgentKind.Player, "Player");
            g.EntityAgent = new CursorAgent(AgentKind.Entity, "Entity");
            g.GaryAgent = new CursorAgent(AgentKind.Entity, "Gary");
            g.Router.Register(g.EntityAgent);
            g.Router.Register(g.GaryAgent);
            g.Router.Register(g.Player);
            g.Router.AnyPointerDown += PopupMenu.HandlePointerDown;
            g.DragDrop = new DragDropSystem(g.Layers.Drag, g.Router);
            // Only the player and the second cursor ever fight over a file; Gary lets go.
            g.DragDrop.CanContest = (holder, grabber) =>
                (holder == g.Player && grabber == g.EntityAgent) || (holder == g.EntityAgent && grabber == g.Player);
            g.GaryView = CursorView.Create(g.Layers.Cursors, g.GaryAgent, true, CursorView.GaryVariant);
            g.EntityView = CursorView.Create(g.Layers.Cursors, g.EntityAgent, true);
            g.PlayerView = CursorView.Create(g.Layers.Cursors, g.Player, false);

            // Fake OS
            g.Windows = new WindowManager(g.Layers.Windows, g.Layers.Effects, g.Router);
            g.Notifications = Notifications.Create(g.Layers.Notifications);
            g.Notifications.Ceiling = () => g.Windows != null ? g.Windows.NoticeCeiling() : float.MaxValue;
            g.Notifications.Avoid = () => g.Windows.NoticeAvoid(g.Player.Position);
            g.Mail = new MailService(g);
            g.Orders = new WorkOrderService(g);
            g.Tasks = new WorkTaskManager(g.Content.Tasks.tasks, new TaskWorld(g));
            g.Shred = new ShredService(g);
            g.Apps = new AppManager(g);
            g.Desktop = Desktop.Create(g);
            g.Taskbar = Taskbar.Create(g);
            g.Tips = Tips.Create(g);
            g.Files.FileMoved += (f, from, to, actor) => g.Tasks.Evaluate();
            g.Files.FileShredded += (f, actor) => g.Tasks.Evaluate();

            // Story / entity
            g.CameraRig = SecurityCameraRig.Create(transform, g);
            g.Entity = EntityController.Create(g, transform);
            g.Entity.Personality.grip = g.Difficulty.GripBase;
            g.Entity.Personality.reactionScale = g.Difficulty.ReactionScale;
            g.Gary = EntityController.Create(g, transform, g.GaryAgent, g.GaryView, false, null);
            g.Gary.DeviceIndex = 3;
            g.Gary.MaxAlpha = 0.75f;
            g.Gary.BaseFlicker = 0.08f;
            g.Gary.TypoRate = 0.08f;
            g.GaryView.Flicker = 0.08f;
            g.Conflict = ConflictSystem.Create(g, transform);
            g.Rounds = RoundsSystem.Create(g, transform);
            NightSetup.ForNight(g);
            g.Director = NightDirector.Create(g, transform, g.Night);
            g.Scares = ScareScheduler.Create(g, transform);
            SystemNotices.Attach(g);
            // After the world set-up (which may set memory flags): only what happens from here can unlock anything.
            g.AchievementWatch = AchievementWatcher.Attach(g);
            Achievements.Reconcile(g);

            DebugOverlay.Create(g, transform);
            PauseMenu.Create(g, transform);
            _deckKeyboard = new DeckKeyboard(g);

            _built = true;
            StartCoroutine(StartGame());
        }

        IEnumerator StartGame()
        {
            yield return G.Audio.GenerateAll();
            // A debug jump made while the sounds were generating has already started the story.
            string beat = StartBeat;
            StartBeat = null;
            bool resume = StartFromCheckpoint;
            StartFromCheckpoint = false;
            if (!string.IsNullOrEmpty(G.Director.CurrentBeat)) yield break;
            if (resume && RestoreCheckpoint(out string checkpointBeat)) G.Director.JumpTo(checkpointBeat);
            else if (G.Director.HasBeat(beat)) G.Director.JumpTo(beat);
            else G.Director.Begin();
        }

        /// <summary>
        /// Continue: put back the flags, trust, assist level and clock saved when the checkpoint beat began.
        /// The director's Prepare then rebuilds the world for that beat from the flags.
        /// </summary>
        bool RestoreCheckpoint(out string beat)
        {
            var g = G;
            var cp = SaveSystem.Load().CheckpointFor(g.Night);
            beat = cp?.beat;
            if (cp == null || !g.Director.HasBeat(beat))
            {
                GameLog.Warn(LogChannel.System, "No checkpoint for night " + g.Night + ": starting the night from the beginning");
                return false;
            }
            if (!cp.armed) g.Disarm("checkpoint saved in a debug run");
            g.Flags.Restore(cp.flags);
            g.Memory.Seed(cp.trust);
            g.Director.ResumeElapsed(cp.elapsed);
            g.Assist.SetLevel(Mathf.Max(g.Difficulty.AssistFloor, cp.assistLevel));
            g.Clock.Reset(cp.clockMinutes / 60, cp.clockMinutes % 60);
            g.Audio.SetAmbience(true, 2f);
            GameLog.Info(LogChannel.System, "Resuming night " + g.Night + " at checkpoint '" + beat + "'");
            return true;
        }

        void EnsureAudioListener()
        {
            AudioListener active = null;
            foreach (var l in SceneObjects.All<AudioListener>())
            {
                // A listener on a previous (restarting) game root is about to be destroyed: ignore it.
                var owner = l.GetComponentInParent<GameRoot>();
                if (l.enabled && l.gameObject.activeInHierarchy && (owner == null || owner == this)) active = l;
            }
            if (active == null) active = gameObject.AddComponent<AudioListener>();
            // Phase M: the safety limiter sits on the listener that hears the game.
            if (active.GetComponent<MasterLimiter>() == null) active.gameObject.AddComponent<MasterLimiter>();
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

            // One failing stage must not stop the rest of the frame (or fill Player.log): each is caught and reported once (FaultLog).
            // 1. Real input -> the player's cursor.
            try
            {
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
            }
            catch (Exception e) { FaultLog.Report("input", e); }

            // 2. Pointer routing for both cursors (entity state was set by its coroutines last frame).
            try { g.Router.Process(Time.unscaledTime); }
            catch (Exception e) { FaultLog.Report("pointer routing", e); }

            // 3. Carried objects and cursor fights.
            try { g.DragDrop.Update(dt); }
            catch (Exception e) { FaultLog.Report("drag and drop", e); }
            try { g.Conflict.Tick(dt); }
            catch (Exception e) { FaultLog.Report("tug of war", e); }

            // 4. The OS.
            try { g.Windows.Update(dt); }
            catch (Exception e) { FaultLog.Report("windows", e); }
            try { g.Apps.Tick(dt); }
            catch (Exception e) { FaultLog.Report("apps", e); }
            try { g.Shred.Tick(dt); }
            catch (Exception e) { FaultLog.Report("shred", e); }
            try
            {
                if (!PauseMenu.IsPaused) g.Apps.RouteKeyboard(g.Input, g.Player);
                _deckKeyboard?.Tick();
            }
            catch (Exception e) { FaultLog.Report("keyboard", e); }
            try { g.Clock.Tick(dt); }
            catch (Exception e) { FaultLog.Report("clock", e); }

            // 5. Feed the security camera the player's hand position (the seated figure mirrors it).
            try
            {
                if (g.CameraRig != null)
                    g.CameraRig.PlayerHand = new Vector2(g.Player.Position.x / ScreenRig.Width * 2f - 1f, g.Player.Position.y / ScreenRig.Height * 2f - 1f);
            }
            catch (Exception e) { FaultLog.Report("camera", e); }
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) Cursor.visible = true;
        }

        void LateUpdate()
        {
            // The only place that hides the Windows pointer (the game draws its own): shown for the developer panel and while the window is not focused.
            bool showPointer = DebugOverlay.PanelOpen || !Application.isFocused;
            Cursor.visible = showPointer;
            if (showPointer != _pointerShown)
            {
                _pointerShown = showPointer;
                GameLog.Info(LogChannel.System, showPointer ? "Windows pointer shown (" + (DebugOverlay.PanelOpen ? "developer panel open" : "window not focused") + ")"
                    : "Windows pointer hidden (the game draws its own)");
            }
            // A hard tug-of-war yank must not fling the real pointer onto another monitor: while playing,
            // the pointer stays inside the game window (never in the Editor, never while paused).
            var want = !Application.isEditor && Application.isFocused && !PauseMenu.IsPaused ? CursorLockMode.Confined : CursorLockMode.None;
            if (Cursor.lockState != want) Cursor.lockState = want;
        }

        /// <summary>Borderless fullscreen at the monitor's own resolution, or the largest whole-number window that fits.</summary>
        public static void Display(bool fullscreen)
        {
            if (fullscreen)
            {
                Screen.SetResolution(UnityEngine.Display.main.systemWidth, UnityEngine.Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                return;
            }
            int scale = Mathf.Max(1, Mathf.Min((UnityEngine.Display.main.systemWidth - 80) / ScreenRig.Width, (UnityEngine.Display.main.systemHeight - 120) / ScreenRig.Height));
            Screen.SetResolution(ScreenRig.Width * scale, ScreenRig.Height * scale, FullScreenMode.Windowed);
        }

        /// <summary>
        /// A night's difficulty profile. Night 1 on Normal takes the optional EntityTuningAsset override (the debug
        /// tuning asset), exactly as at boot, so a difficulty switch mid-night builds the same profile.
        /// </summary>
        public static DifficultyProfile MakeDifficulty(int night, DifficultyMode mode)
        {
            var p = DifficultyTable.For(night, mode);
            var tuning = EntityTuningAsset.LoadOptional();
            if (tuning != null && night == 1 && mode == DifficultyMode.Normal) tuning.ApplyTo(p);
            p.Tug.model = TugModel;
            // Steam Deck trackpad fairness: the drag speed that counts as a full pull (speed model), the reel's gain (Phase P).
            if (SteamBridge.OnDeck)
            {
                p.Tug.pullSpeedForFullStrength *= DeckPullSpeedScale;
                p.Tug.reel.reelGain *= DeckReelGainScale;
            }
            return p;
        }

        /// <summary>Scales the speed model's full-strength drag speed on a Steam Deck (1 until trackpad drags are measured).</summary>
        public const float DeckPullSpeedScale = 1f;
        /// <summary>Phase P: scales the reel's gain on a Steam Deck (1 until the real-hands test, review board E8, measures it).</summary>
        public const float DeckReelGainScale = 1f;

        /// <summary>
        /// Phase P: which tug the game plays: the reel ("haul it to the bin") unless the launch says <c>-sctug speed</c> (any build, so the
        /// two can be compared on a Steam Deck); the developer panel and the test bridge change it for the next contests.
        /// </summary>
        public static TugModel TugModel = TugModel.Reel;

        /// <summary>Used by restart: the next GameRoot becomes the instance before this one is destroyed.</summary>
        internal static void ReleaseInstance() => Instance = null;

        void OnDestroy()
        {
            // The input backend belongs to this root (it may hold Input System subscriptions): always release it.
            G?.Input?.Dispose();
            _deckKeyboard?.Dismiss();
            // Only the current instance owns the global hooks (a restart creates the new root first).
            if (Instance != this && Instance != null) return;
            if (Instance == this) Instance = null;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Sfx.Handler = null;
            GameLog.Output = null;
        }
    }
}
