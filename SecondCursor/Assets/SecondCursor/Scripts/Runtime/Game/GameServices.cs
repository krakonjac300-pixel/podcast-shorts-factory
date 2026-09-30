using SecondCursor.Apps;
using SecondCursor.Audio;
using SecondCursor.CameraFeed;
using SecondCursor.Core.Content;
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
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Explicit references to every game system, built once by <see cref="GameRoot"/> and handed to the
    /// systems that need them (no GameObject.Find, no hidden singletons). Holds no logic of its own.
    /// </summary>
    public sealed class GameServices
    {
        // Presentation
        public ScreenRig Screen;
        public OSLayers Layers;
        public VisualFx Fx;
        public AudioManager Audio;

        // Input
        public IInputBackend Input;
        public PointerRouter Router;
        public DragDropSystem DragDrop;
        public CursorAgent Player;
        public CursorAgent EntityAgent;
        public CursorView PlayerView;
        public CursorView EntityView;
        /// <summary>The third pointer (Gary, Night 2 on). Registered after the second cursor, before the player.</summary>
        public CursorAgent GaryAgent;
        public CursorView GaryView;

        // Simulation (engine-free core)
        public ContentDatabase Content;
        public VirtualFileSystem Files;
        public NarrativeFlags Flags;
        public WorkTaskManager Tasks;
        public GameClock Clock;
        public EntityMemory Memory;
        public CursorRecorder Recorder;
        public DialogueEngine Dialogue;

        // Fake OS
        public WindowManager Windows;
        public Desktop Desktop;
        public Taskbar Taskbar;
        public Notifications Notifications;
        /// <summary>Phase L: one-time tips beside the thing they explain.</summary>
        public Tips Tips;
        public AppManager Apps;
        public ShredService Shred;
        public MailService Mail;
        public WorkOrderService Orders;

        // Story / entity
        public EntityController Entity;
        /// <summary>Gary's controller: no brain, never in a tug-of-war, can block the second cursor's clicks.</summary>
        public EntityController Gary;
        public ConflictSystem Conflict;
        public NightDirector Director;
        public SecurityCameraRig CameraRig;
        /// <summary>Custodial rounds on the cameras (the watch meter).</summary>
        public RoundsSystem Rounds;
        /// <summary>Phase M: the rare ambient scares (budgets, cooldown, the gate).</summary>
        public ScareScheduler Scares;

        // Night and difficulty
        /// <summary>The night being played (1-3).</summary>
        public int Night = 1;
        /// <summary>This night's challenge values (difficulty table column, Normal or Story).</summary>
        public DifficultyProfile Difficulty;
        /// <summary>Eases the second cursor after repeated losses, toughens it after easy wins.</summary>
        public AdaptiveAssist Assist;
        /// <summary>
        /// progress.json as it was when this night was built (difficulty, memory, checkpoint). Read-only:
        /// SaveSystem re-reads the file before every write, because achievements also write it.
        /// </summary>
        public SaveData Save;

        public MonoBehaviour CoroutineHost;

        /// <summary>Game time used by simulation code (scaled by the pause menu / debug speed).</summary>
        public float Now => Time.time;

        // Records and achievements

        /// <summary>Unlocks achievements for this run (null until the night is built).</summary>
        public AchievementWatcher AchievementWatch;
        /// <summary>This night was started from Night Select (it began from the night's first-start memory).</summary>
        public bool FromNightSelect;
        /// <summary>This root shows the title menu inside its boot beat (consumed when the boot shows it).</summary>
        public bool ShowTitle;

        /// <summary>
        /// M9: when each remote (entity-authored) Work Queue item was given and after how many seconds it is withdrawn
        /// (x = Time.time given, y = lifetime). The Work Queue fades and blinks the row as the time runs out.
        /// </summary>
        public readonly System.Collections.Generic.Dictionary<string, Vector2> RemoteTaskLife = new System.Collections.Generic.Dictionary<string, Vector2>();

        bool _armed;
        bool _forced;
        string _disarmReason;

        /// <summary>
        /// This run counts for achievements, best times and tug totals: it was started from the title, an end card,
        /// or a checkpoint saved in such a run, and nothing debug has touched it since. A tester can force it on.
        /// </summary>
        public bool RecordsArmed => _forced || (_armed && _disarmReason == null);

        /// <summary>Why the run does not count (null while armed).</summary>
        public string RecordsHeldReason => RecordsArmed ? null : _disarmReason ?? "not started from the title";

        /// <summary>Set once when the root is built.</summary>
        public void Arm(bool armed, string why)
        {
            _armed = armed;
            _disarmReason = null;
            Core.GameLog.Info(Core.LogChannel.System, armed ? "Records armed: " + why : "Records held: " + why);
            if (!armed) _disarmReason = why;
        }

        /// <summary>
        /// Test runs: count this run anyway (bridge "achievements on", the debug panel's Arm records). It overrides every
        /// disarm until <see cref="Unforce"/>; the per-event gates (a forced tug, Prepare) still hold what they hold.
        /// </summary>
        public void ForceArm()
        {
            if (_forced) return;
            _forced = true;
            Core.GameLog.Info(Core.LogChannel.System, "Records armed: forced for testing");
        }

        /// <summary>Something debug happened (a jump, a forced tug, the debug panel): records stop counting for this run.</summary>
        public void Disarm(string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "debug";
            bool naturallyArmed = _armed && _disarmReason == null;
            if (_disarmReason == null) _disarmReason = reason;
            if (naturallyArmed) Core.GameLog.Info(Core.LogChannel.System, "Records held: " + reason + (_forced ? " (still forced on for testing)" : ""));
        }

        /// <summary>Ends a <see cref="ForceArm"/> (bridge "achievements off").</summary>
        public void Unforce(string reason)
        {
            _forced = false;
            Disarm(reason);
        }
    }
}
