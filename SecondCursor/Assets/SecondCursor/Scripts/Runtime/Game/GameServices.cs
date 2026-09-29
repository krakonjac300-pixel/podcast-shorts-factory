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
    }
}
