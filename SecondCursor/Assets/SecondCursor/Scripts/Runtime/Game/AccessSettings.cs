using SecondCursor.Core;
using SecondCursor.Core.Game;

namespace SecondCursor.Game
{
    /// <summary>
    /// Phase P (review board A2): the two motor-access options. "Tug assist: Hold" makes holding the button enough to win a tug-of-war (read at
    /// every contest, so it can be switched mid-night); "Click lock" keeps a drag going after the button comes up, until the next press drops
    /// it. Both are saved with the settings and count for achievements like any other play.
    /// </summary>
    public static class AccessSettings
    {
        public static bool TugAssistHold;
        public static bool ClickLockOn;
        /// <summary>The player's click lock filter (the runtime feeds the raw button through it every frame).</summary>
        public static readonly ClickLock Lock = new ClickLock();

        public static void Load(SettingsData s)
        {
            if (s == null) return;
            TugAssistHold = s.tugAssist == "hold";
            ClickLockOn = s.clickLock;
            Lock.Enabled = ClickLockOn;
            if (!ClickLockOn) Lock.Clear();
        }

        public static void Save(SettingsData s)
        {
            if (s == null) return;
            s.tugAssist = TugAssistHold ? "hold" : "off";
            s.clickLock = ClickLockOn;
        }

        public static void SetTugAssist(bool hold)
        {
            TugAssistHold = hold;
            GameLog.Info(LogChannel.Player, "Tug assist: " + (hold ? "Hold" : "Off"));
        }

        public static void SetClickLock(bool on)
        {
            ClickLockOn = on;
            Lock.Enabled = on;
            if (!on) Lock.Clear();
            GameLog.Info(LogChannel.Player, "Click lock: " + (on ? "On" : "Off"));
        }
    }
}
