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
            var go = new GameObject("SECOND CURSOR");
            go.AddComponent<GameRoot>();
            Debug.Log("[SYSTEM] SECOND CURSOR booted in scene '" + SceneManager.GetActiveScene().name + "'");
        }

        /// <summary>
        /// Start a fresh shift without reloading the scene (works even when the scene is not in Build
        /// Settings): tear down the whole game object tree and build a new one.
        /// </summary>
        public static void Restart()
        {
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
