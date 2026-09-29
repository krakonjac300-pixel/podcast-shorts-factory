using UnityEngine;
using UnityEngine.SceneManagement;

namespace SecondCursor.Game
{
    /// <summary>
    /// Starts SECOND CURSOR in whatever scene is playing: press Play in any scene (even an empty one) and
    /// the whole game builds itself from code. Add a <see cref="DisableAutoBoot"/> component to a scene
    /// to opt that scene out.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Object.FindAnyObjectByType<DisableAutoBoot>() != null) return;
            if (Object.FindAnyObjectByType<GameRoot>() != null) return;
            // Statics survive Play sessions when domain reload is off.
            GameRoot.StartBeat = CommandLineArg("-scbeat");
            GameRoot.StartNight = int.TryParse(CommandLineArg("-scnight"), out int night) ? Mathf.Clamp(night, 1, 3) : 1;
            GameRoot.StartFromCheckpoint = false;
            Entity.ConflictSystem.ForcedOutcome = Core.Entity.TugOutcome.None;
            Core.GameLog.ClearHistory();
            var go = new GameObject("SECOND CURSOR");
            go.AddComponent<GameRoot>();
            Debug.Log("[SYSTEM] SECOND CURSOR booted in scene '" + SceneManager.GetActiveScene().name + "'");
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

        /// <summary>
        /// Start a fresh shift of the current night without reloading the scene (works even when the scene is
        /// not in Build Settings): tear down the whole game object tree and build a new one. With
        /// <paramref name="startBeat"/> the new shift skips straight to that story beat.
        /// </summary>
        public static void Restart() => Restart(CurrentNight, null);

        public static void Restart(string startBeat) => Restart(CurrentNight, startBeat);

        /// <summary>
        /// Start <paramref name="night"/> fresh (at <paramref name="beat"/> if given), or resume its saved
        /// checkpoint when <paramref name="fromCheckpoint"/> is set (falls back to the night's start).
        /// </summary>
        public static void Restart(int night, string beat = null, bool fromCheckpoint = false)
        {
            GameRoot.StartNight = Mathf.Clamp(night, 1, 3);
            GameRoot.StartBeat = beat;
            GameRoot.StartFromCheckpoint = fromCheckpoint;
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
