using System;
using System.Collections.Generic;
using SecondCursor.Core;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Achievements: stored in the save file, and mirrored to Steam when <see cref="SteamBridge"/> is live.
    /// The game never depends on Steam: without it everything still unlocks locally.
    /// </summary>
    public static class Achievements
    {
        /// <summary>Raised once per achievement, the first time it unlocks.</summary>
        public static event Action<string> Unlocked;

        public static bool Has(string id) => Array.IndexOf(SaveSystem.Load().achievements, id) >= 0;

        public static void Unlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var data = SaveSystem.Load();
            if (Array.IndexOf(data.achievements, id) >= 0)
            {
                SteamBridge.Unlock(id); // keep Steam in sync if it was unlocked offline
                return;
            }
            var list = new List<string>(data.achievements) { id };
            data.achievements = list.ToArray();
            SaveSystem.Save(data);
            GameLog.Info(LogChannel.System, "Achievement unlocked: " + id);
            SteamBridge.Unlock(id);
            Unlocked?.Invoke(id);
        }
    }

    /// <summary>
    /// Optional Steamworks connection. Compiled in only when the project defines STEAMWORKS_NET (add the
    /// Steamworks.NET package, put the game's App ID in steam_appid.txt next to the executable and in
    /// <see cref="AppId"/>). Without it every call is a no-op, so the game runs anywhere.
    /// </summary>
    public static class SteamBridge
    {
        /// <summary>The game's Steam App ID (480 is Valve's public test app until the real one exists).</summary>
        public const uint AppId = 480;

        /// <summary>Store page for the end card's Wishlist button. Leave empty until the page is public (the button hides).</summary>
        public const string StoreUrl = "";

        /// <summary>Opens the store page: in the Steam overlay/client when Steam is running, else in the browser.</summary>
        public static void OpenStorePage()
        {
            if (string.IsNullOrEmpty(StoreUrl)) return;
            Application.OpenURL(Ready ? "steam://store/" + AppId : StoreUrl);
        }

        public static bool Ready { get; private set; }

#if STEAMWORKS_NET
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Ready) return;
            try
            {
                if (!Application.isEditor && Steamworks.SteamAPI.RestartAppIfNecessary(new Steamworks.AppId_t(AppId)))
                {
                    Application.Quit();
                    return;
                }
                Ready = Steamworks.SteamAPI.Init();
            }
            catch (Exception e)
            {
                Ready = false;
                GameLog.Warn(LogChannel.System, "Steam not available: " + e.Message);
            }
            if (!Ready) return;
            var go = new GameObject("Steam Callbacks") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Pump>();
        }

        sealed class Pump : MonoBehaviour
        {
            void Update() { if (Ready) Steamworks.SteamAPI.RunCallbacks(); }

            void OnApplicationQuit()
            {
                if (!Ready) return;
                Steamworks.SteamAPI.Shutdown();
                Ready = false;
            }
        }

        public static void Unlock(string id)
        {
            if (!Ready) return;
            Steamworks.SteamUserStats.SetAchievement(id);
            Steamworks.SteamUserStats.StoreStats();
        }
#else
        public static void Unlock(string id) { }
#endif
    }
}
