using SecondCursor.Story;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SecondCursor.Game
{
    /// <summary>
    /// Starts SECOND CURSOR in whatever scene is playing: press Play in any scene (even an empty one) and
    /// the whole game builds itself from code. Add a <see cref="DisableAutoBoot"/> component to a scene
    /// to opt that scene out. Every start (title, New Game, Continue, Night Select, an end card, a debug jump) builds
    /// a fresh game root through one path, <see cref="Rebuild"/>.
    /// </summary>
    public static class GameBootstrap
    {
#if SC_DEMO
        /// <summary>This is the free demo build: Night 1 only.</summary>
        public static readonly bool IsDemo = true;
#else
        /// <summary>This is the free demo build: Night 1 only.</summary>
        public static readonly bool IsDemo = false;
#endif

        /// <summary>The last night this build contains.</summary>
        public static int MaxNight => IsDemo ? 1 : Core.Game.SaveData.Nights;

        /// <summary>
        /// Every launch (every Play press) starts clean, before any scene object wakes: statics survive Play sessions
        /// when domain reload is off, and the game scene already contains a GameRoot. (The save folder override and
        /// the simulated Deck are set by the test bridge before Play, so they stay.)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PrepareLaunch()
        {
            ResetLaunchState();
            string beat = CommandLineArg("-scbeat");
            bool hasNight = int.TryParse(CommandLineArg("-scnight"), out int night);
            if (!string.IsNullOrEmpty(beat) || hasNight)
            {
                // QA launch: straight into a night (and beat), no title, records held.
                SetNext(hasNight ? night : 1, beat, false, false, false, false, hasNight ? "launched with -scnight" : "launched with -scbeat");
            }
            else
            {
                SetNext(1, null, false, true, true, false, "title");
            }
            Core.GameLog.ClearHistory();
        }

        /// <summary>A scene without a GameRoot (any scene) gets one.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Object.FindAnyObjectByType<DisableAutoBoot>() != null) return;
            if (Object.FindAnyObjectByType<GameRoot>() != null) return;
            var go = new GameObject("SECOND CURSOR");
            go.AddComponent<GameRoot>();
            Debug.Log("[SYSTEM] SECOND CURSOR booted in scene '" + SceneManager.GetActiveScene().name + "'");
        }

        static void ResetLaunchState()
        {
            GameRoot.StartFromCheckpoint = false;
            GameRoot.ShowTitle = false;
            GameRoot.NextRunArmed = false;
            GameRoot.FromNightSelect = false;
            GameRoot.ForceArmNext = false;
            BootSequence.DisclaimerShownThisLaunch = false;
            TitleMenu.StartScreen = TitleScreenId.Main;
            Entity.ConflictSystem.ForcedOutcome = Core.Entity.TugOutcome.None;
            SaveSystem.ResetLaunchState();
        }

        /// <summary>QA: "SecondCursor.exe -scnight 2 -scbeat conflict" starts a fresh shift at that night and story beat.</summary>
        static string CommandLineArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>The night being played (or about to be built).</summary>
        public static int CurrentNight => GameRoot.Instance != null && GameRoot.Instance.G != null ? GameRoot.Instance.G.Night : GameRoot.StartNight;

        /// <summary>The Night Select screen exists in this build (the last night's card offers it).</summary>
        public static bool NightSelectAvailable => !IsDemo;

        // ------------------------------------------------------------------ starts that count (armed)

        /// <summary>
        /// A start chosen by the player (title menu, end card): a fresh night, the saved checkpoint of that night, or a
        /// Night Select replay from the night's first-start memory. Records and achievements count.
        /// </summary>
        public static void StartFromMenu(int night, bool fromCheckpoint = false, bool fromNightSelect = false)
        {
            Rebuild(night, null, fromCheckpoint, true, false, fromNightSelect,
                fromNightSelect ? "night select" : fromCheckpoint ? "continue" : "menu");
        }

        /// <summary>The title menu (built on a Night 1 root, inside its boot beat), optionally on a sub-screen.</summary>
        public static void ToTitle(TitleScreenId screen = TitleScreenId.Main)
        {
            TitleMenu.StartScreen = screen;
            Rebuild(1, null, false, true, true, false, "title");
        }

        /// <summary>"Night Select" on the last night's card.</summary>
        public static void ToNightSelect() => ToTitle(TitleScreenId.NightSelect);

        /// <summary>
        /// The pause menu's Restart: this night's saved checkpoint, or the night from its start when it has none. The
        /// run keeps its armed state (a debug run stays held).
        /// </summary>
        public static void RestartFromCheckpoint(int night, bool armed, bool fromNightSelect)
        {
            bool hasCheckpoint = SaveSystem.Load().CheckpointFor(night) != null;
            Rebuild(night, null, hasCheckpoint, armed, false, fromNightSelect, "restart from the pause menu");
        }

        // ------------------------------------------------------------------ debug starts (records held)

        /// <summary>
        /// Start a fresh shift of the current night without reloading the scene (works even when the scene is
        /// not in Build Settings): tear down the whole game object tree and build a new one. With
        /// <paramref name="startBeat"/> the new shift skips straight to that story beat. Debug only: records are held.
        /// </summary>
        public static void Restart() => Restart(CurrentNight, null);

        public static void Restart(string startBeat) => Restart(CurrentNight, startBeat);

        /// <summary>
        /// Start <paramref name="night"/> fresh (at <paramref name="beat"/> if given), or resume its saved
        /// checkpoint when <paramref name="fromCheckpoint"/> is set (falls back to the night's start). Debug only:
        /// records are held.
        /// </summary>
        public static void Restart(int night, string beat = null, bool fromCheckpoint = false)
        {
            Rebuild(night, beat, fromCheckpoint, false, false, false, string.IsNullOrEmpty(beat) ? "debug restart" : "debug jump to " + beat);
        }

        // ------------------------------------------------------------------ the one path

        static void SetNext(int night, string beat, bool fromCheckpoint, bool armed, bool showTitle, bool fromNightSelect, string why)
        {
            GameRoot.StartNight = Mathf.Clamp(night, 1, MaxNight);
            GameRoot.StartBeat = beat;
            GameRoot.StartFromCheckpoint = fromCheckpoint;
            GameRoot.NextRunArmed = armed;
            GameRoot.NextRunReason = why;
            GameRoot.ShowTitle = showTitle;
            GameRoot.FromNightSelect = fromNightSelect;
        }

        static void Rebuild(int night, string beat, bool fromCheckpoint, bool armed, bool showTitle, bool fromNightSelect, string why)
        {
            SetNext(night, beat, fromCheckpoint, armed, showTitle, fromNightSelect, why);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            var old = GameRoot.Instance;
            GameRoot.ReleaseInstance();
            if (old != null) Object.Destroy(old.gameObject);
            var go = new GameObject("SECOND CURSOR");
            go.AddComponent<GameRoot>();
        }
    }
}
