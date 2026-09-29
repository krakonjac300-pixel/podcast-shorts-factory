using SecondCursor.Apps;
using SecondCursor.Audio;
using SecondCursor.CameraFeed;
using SecondCursor.Core.Content;
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
        public ConflictSystem Conflict;
        public EventDirector Director;
        public SecurityCameraRig CameraRig;

        public MonoBehaviour CoroutineHost;

        /// <summary>Game time used by simulation code (scaled by the pause menu / debug speed).</summary>
        public float Now => Time.time;
    }
}
