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
            GameRoot.StartBeat = CommandLineBeat();
            Core.GameLog.ClearHistory();
            var go = new GameObject("SECOND CURSOR");
            go.AddComponent<GameRoot>();
            Debug.Log("[SYSTEM] SECOND CURSOR booted in scene '" + SceneManager.GetActiveScene().name + "'");
        }

        /// <summary>QA: "SecondCursor.exe -scbeat reveal" starts a fresh shift at that story beat.</summary>
        static string CommandLineBeat()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-scbeat") return args[i + 1];
            return null;
        }

        /// <summary>
        /// Start a fresh shift without reloading the scene (works even when the scene is not in Build
        /// Settings): tear down the whole game object tree and build a new one. With
        /// <paramref name="startBeat"/> the new shift skips straight to that story beat.
        /// </summary>
        public static void Restart() => Restart(null);

        public static void Restart(string startBeat)
        {
            GameRoot.StartBeat = startBeat;
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
