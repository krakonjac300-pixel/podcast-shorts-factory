using System.IO;
using System.Linq;
using SecondCursor.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// One-time project setup when SECOND CURSOR is first imported: creates the game scene
    /// (Assets/SecondCursor/Scenes/SecondCursor.unity) containing the GameRoot, puts it first in Build
    /// Settings and sets sensible player settings. Also adds a "SECOND CURSOR" menu.
    /// The game itself does not depend on this: pressing Play in ANY scene boots it.
    /// </summary>
    [InitializeOnLoad]
    public static class SecondCursorProjectSetup
    {
        public const string ScenePath = "Assets/SecondCursor/Scenes/SecondCursor.unity";
        const string PrefKeyPrefix = "SecondCursor.SetupDone.";

        static SecondCursorProjectSetup()
        {
            EditorApplication.delayCall += RunOnce;
        }

        static string PrefKey => PrefKeyPrefix + Application.dataPath.GetHashCode();

        static void RunOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorPrefs.GetBool(PrefKey, false) && File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(PrefKey, true);
            ConfigurePlayer();
            if (!File.Exists(ScenePath)) CreateScene(openAfter: true);
            else AddToBuildSettings();
            Debug.Log("[SYSTEM] SECOND CURSOR project setup complete. Open " + ScenePath + " and press Play.");
        }

        [MenuItem("SECOND CURSOR/Open Game Scene", priority = 1)]
        static void OpenScene()
        {
            if (!File.Exists(ScenePath)) CreateScene(openAfter: false);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("SECOND CURSOR/Rebuild Game Scene", priority = 2)]
        static void Rebuild()
        {
            CreateScene(openAfter: true);
        }

        /// <summary>The same settings as every build gets (one list: this used to set runInBackground the opposite way round).</summary>
        [MenuItem("SECOND CURSOR/Apply Player Settings", priority = 20)]
        static void ConfigurePlayer() => SecondCursorBuild.ApplyReleaseSettings();

        [MenuItem("SECOND CURSOR/Open README", priority = 40)]
        static void OpenReadme()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "SecondCursor/README.md"));
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
            else Debug.LogWarning("README not found at " + path);
        }

        static void CreateScene(bool openAfter)
        {
            if (openAfter && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/SecondCursor/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, openAfter ? NewSceneMode.Single : NewSceneMode.Additive);
            var root = new GameObject("SECOND CURSOR");
            root.AddComponent<GameRoot>();
            SceneManager.MoveGameObjectToScene(root, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!openAfter) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            AddToBuildSettings();
        }

        static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
