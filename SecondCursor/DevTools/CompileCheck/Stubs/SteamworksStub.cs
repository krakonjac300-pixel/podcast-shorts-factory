// Harness-only stub of the parts of Steamworks.NET (com.rlabrecque.steamworks.net, SDK 1.58+) that SecondCursor
// uses under STEAMWORKS_NET. Signatures mirror the real package; bodies are meaningless. NOT part of the game.
namespace Steamworks
{
    public struct AppId_t
    {
        public uint m_AppId;
        public AppId_t(uint value) { m_AppId = value; }
    }

    public enum ESteamAPIInitResult
    {
        k_ESteamAPIInitResult_OK = 0,
        k_ESteamAPIInitResult_FailedGeneric = 1,
        k_ESteamAPIInitResult_NoSteamClient = 2,
        k_ESteamAPIInitResult_VersionMismatch = 3,
    }

    public enum EOverlayToStoreFlag
    {
        k_EOverlayToStoreFlag_None = 0,
        k_EOverlayToStoreFlag_AddToCart = 1,
        k_EOverlayToStoreFlag_AddToCartAndShow = 2,
    }

    public enum EFloatingGamepadTextInputMode
    {
        k_EFloatingGamepadTextInputModeModeSingleLine = 0,
        k_EFloatingGamepadTextInputModeModeMultipleLines = 1,
        k_EFloatingGamepadTextInputModeModeEmail = 2,
        k_EFloatingGamepadTextInputModeModeNumeric = 3,
    }

    public static class SteamAPI
    {
        public static bool RestartAppIfNecessary(AppId_t unOwnAppID) => false;
        public static ESteamAPIInitResult InitEx(out string OutSteamErrMsg)
        {
            OutSteamErrMsg = "";
            return ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        }
        public static void RunCallbacks() { }
        public static void Shutdown() { }
    }

    public static class SteamUserStats
    {
        public static bool SetAchievement(string pchName) => true;
        public static bool SetStat(string pchName, int nData) => true;
        public static bool StoreStats() => true;
        public static bool IndicateAchievementProgress(string pchName, uint nCurProgress, uint nMaxProgress) => true;
        public static bool GetAchievement(string pchName, out bool pbAchieved)
        {
            pbAchieved = false;
            return true;
        }
    }

    public static class SteamUtils
    {
        public static bool IsSteamRunningOnSteamDeck() => false;
        public static AppId_t GetAppID() => new AppId_t(480);
        public static bool ShowFloatingGamepadTextInput(EFloatingGamepadTextInputMode eKeyboardMode, int nTextFieldXPosition, int nTextFieldYPosition,
            int nTextFieldWidth, int nTextFieldHeight) => true;
        public static bool DismissFloatingGamepadTextInput() => true;
    }

    public static class SteamFriends
    {
        public static void ActivateGameOverlayToStore(AppId_t nAppID, EOverlayToStoreFlag eFlag) { }
    }

    public struct GameOverlayActivated_t
    {
        public byte m_bActive;
        public bool m_bUserInitiated;
        public AppId_t m_nAppID;
    }

    public struct FloatingGamepadTextInputDismissed_t { }

    public enum EResult
    {
        k_EResultNone = 0,
        k_EResultOK = 1,
        k_EResultFail = 2,
    }

    public struct CSteamID
    {
        public ulong m_SteamID;
    }

    public struct UserStatsReceived_t
    {
        public ulong m_nGameID;
        public EResult m_eResult;
        public CSteamID m_steamIDUser;
    }

    public sealed class Callback<T>
    {
        public delegate void DispatchDelegate(T param);
        public static Callback<T> Create(DispatchDelegate func) => new Callback<T>();
    }
}
