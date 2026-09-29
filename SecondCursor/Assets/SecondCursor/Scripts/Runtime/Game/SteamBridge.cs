using System;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>What kind of text the Steam Deck's floating keyboard is opened for.</summary>
    public enum TextEntryMode { SingleLine, MultiLine, Numeric }

    /// <summary>
    /// Optional Steamworks connection. Compiled in only when the project defines STEAMWORKS_NET (the Steamworks.NET
    /// package plus the asmdef reference and version define, see Docs/HANDOFF.md). Without it every call is a no-op or
    /// a local fallback, so the game runs anywhere. Demo builds (SC_DEMO) use the demo's App ID, send the store button
    /// to the full game's page, and keep achievements local until the demo app has its own.
    /// </summary>
    public static class SteamBridge
    {
        /// <summary>The full game's Steam App ID (480 is Valve's public test app until the real one exists).</summary>
        public const uint FullGameAppId = 480;
        /// <summary>The demo's own App ID (a separate Steam app; 480 until it exists).</summary>
        public const uint DemoAppId = 480;

#if SC_DEMO
        public static uint AppId => DemoAppId;
#else
        public static uint AppId => FullGameAppId;
#endif

        /// <summary>The full game's store page for the Wishlist buttons outside Steam. Empty until the page is public.</summary>
        public const string StoreUrl = "";

        /// <summary>Test bridge "store on": the store button only logs this URL (never opens a browser).</summary>
        internal static string TestStoreUrl;
        /// <summary>Test bridge "deck on": behave as on a Steam Deck (wording, large text, keyboard log lines).</summary>
        internal static bool SimulateDeck;

        static bool DeckHardware { get; set; }
        static float _textEntryShownAt = -100f;

        public static bool Ready { get; private set; }

        /// <summary>
        /// Running on a Steam Deck: Steam says so, or Steam's own SteamDeck=1 environment variable is set (a build
        /// without Steamworks), or the test bridge simulates it.
        /// </summary>
        public static bool OnDeck => SimulateDeck || DeckHardware || DeckEnvironment;

        static bool DeckEnvironment
        {
            get
            {
                try { return Environment.GetEnvironmentVariable("SteamDeck") == "1"; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>A Wishlist button can do something: the Steam overlay, a store URL, or the test dry run.</summary>
        public static bool CanOpenStore => Ready || !string.IsNullOrEmpty(TestStoreUrl) || !string.IsNullOrEmpty(StoreUrl);

        /// <summary>The Steam overlay opened (the game pauses itself when a shift is running).</summary>
        public static event Action OverlayActivated;

        /// <summary>Steam closed the floating keyboard itself (the player dismissed it).</summary>
        public static bool TextEntryDismissedBySteam { get; set; }

        /// <summary>The full game's store page: in the Steam overlay when Steam runs, else the store URL.</summary>
        public static void OpenStorePage()
        {
            if (!string.IsNullOrEmpty(TestStoreUrl))
            {
                GameLog.Info(LogChannel.System, "Store page (dry run): " + TestStoreUrl);
                return;
            }
#if STEAMWORKS_NET
            if (Ready)
            {
                Steamworks.SteamFriends.ActivateGameOverlayToStore(new Steamworks.AppId_t(FullGameAppId), Steamworks.EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
                GameLog.Info(LogChannel.System, "Store page opened in the Steam overlay");
                return;
            }
#endif
            if (string.IsNullOrEmpty(StoreUrl)) return;
            Application.OpenURL(StoreUrl);
            GameLog.Info(LogChannel.System, "Store page opened: " + StoreUrl);
        }

        /// <summary>Test bridge "overlay": behave as if the Steam overlay just opened.</summary>
        internal static void RaiseOverlayActivated() => OverlayActivated?.Invoke();

        static void OnOverlay(bool active)
        {
            // On hardware the floating keyboard may raise the overlay callback: ignore it right after showing it.
            if (!active || Time.unscaledTime - _textEntryShownAt < 0.5f) return;
            OverlayActivated?.Invoke();
        }

#if STEAMWORKS_NET
        // The demo keeps achievements local until the demo app has its own Steam achievements.
#if SC_DEMO
        static readonly bool PushRecords = false;
#else
        static readonly bool PushRecords = true;
#endif
        static Steamworks.Callback<Steamworks.GameOverlayActivated_t> _overlayCallback;
        static Steamworks.Callback<Steamworks.FloatingGamepadTextInputDismissed_t> _dismissCallback;

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
                var result = Steamworks.SteamAPI.InitEx(out string error);
                Ready = result == Steamworks.ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                // GameLog is not wired to the console yet this early: warn through Unity (Player.log).
                if (!Ready) Debug.LogWarning("[SYSTEM] Steam not available (" + result + "): " + error);
            }
            catch (Exception e)
            {
                Ready = false;
                Debug.LogWarning("[SYSTEM] Steam not available: " + e.Message);
            }
            if (!Ready) return;
            var go = new GameObject("Steam Callbacks") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Pump>();
            DeckHardware = Steamworks.SteamUtils.IsSteamRunningOnSteamDeck();
            _overlayCallback = Steamworks.Callback<Steamworks.GameOverlayActivated_t>.Create(e => OnOverlay(e.m_bActive != 0));
            _dismissCallback = Steamworks.Callback<Steamworks.FloatingGamepadTextInputDismissed_t>.Create(e => TextEntryDismissedBySteam = true);
            PushAll();
        }

        sealed class Pump : MonoBehaviour
        {
            void Update()
            {
                if (Ready) Steamworks.SteamAPI.RunCallbacks();
            }

            void OnApplicationQuit()
            {
                if (!Ready) return;
                Steamworks.SteamAPI.Shutdown();
                Ready = false;
            }
        }

        /// <summary>Every achievement already in the save, and the tug stat, pushed again (covers offline unlocks).</summary>
        static void PushAll()
        {
            if (!PushRecords) return;
            var data = SaveSystem.Load();
            foreach (var id in data.achievements)
                if (AchievementIds.IsKnown(id)) Steamworks.SteamUserStats.SetAchievement(id);
            Steamworks.SteamUserStats.SetStat(AchievementIds.TugWinsStat, Math.Min(data.tugWinsTotal, AchievementIds.TugWinsGoal));
            Steamworks.SteamUserStats.StoreStats();
        }

        public static void Unlock(string id)
        {
            if (!Ready || !PushRecords) return;
            Steamworks.SteamUserStats.SetAchievement(id);
            Steamworks.SteamUserStats.StoreStats();
        }

        /// <summary>The TUG_WINS stat (increment only, capped at the White Knuckles goal).</summary>
        public static void SetTugWins(int total)
        {
            if (!Ready || !PushRecords) return;
            Steamworks.SteamUserStats.SetStat(AchievementIds.TugWinsStat, Math.Min(total, AchievementIds.TugWinsGoal));
            Steamworks.SteamUserStats.StoreStats();
        }

        /// <summary>Steam's "5 of 10" progress popup for an achievement with a stat.</summary>
        public static void IndicateProgress(string id, int current, int max)
        {
            if (!Ready || !PushRecords) return;
            Steamworks.SteamUserStats.IndicateAchievementProgress(id, (uint)Math.Max(0, current), (uint)Math.Max(1, max));
        }

        /// <summary>Opens the Deck's floating keyboard next to a text field (pixels from the top-left of the window).</summary>
        public static bool ShowTextEntry(TextEntryMode mode, RectInt field)
        {
            _textEntryShownAt = Time.unscaledTime;
            TextEntryDismissedBySteam = false;
            if (!Ready) return false;
            var m = mode == TextEntryMode.Numeric ? Steamworks.EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeNumeric
                : mode == TextEntryMode.MultiLine ? Steamworks.EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeMultipleLines
                : Steamworks.EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine;
            return Steamworks.SteamUtils.ShowFloatingGamepadTextInput(m, field.x, field.y, field.width, field.height);
        }

        public static void DismissTextEntry()
        {
            if (Ready) Steamworks.SteamUtils.DismissFloatingGamepadTextInput();
        }
#else
        public static void Unlock(string id) { }

        public static void SetTugWins(int total) { }

        public static void IndicateProgress(string id, int current, int max) { }

        public static bool ShowTextEntry(TextEntryMode mode, RectInt field)
        {
            _textEntryShownAt = Time.unscaledTime;
            TextEntryDismissedBySteam = false;
            return false;
        }

        public static void DismissTextEntry() { }
#endif
    }
}
