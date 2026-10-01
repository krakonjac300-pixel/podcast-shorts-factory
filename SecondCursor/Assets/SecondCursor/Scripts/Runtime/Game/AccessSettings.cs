using SecondCursor.Core;
using SecondCursor.Core.Game;

namespace SecondCursor.Game
{
    /// <summary>
    /// The access options. Phase P (review board A2): "Tug assist: Hold" makes holding the button enough to win a tug-of-war (read at every
    /// contest, so it can be switched mid-night); "Click lock" keeps a drag going after the button comes up, until the next press drops it.
    /// Phase Q4 (A3, A4, A6, A7, A9, A10): notice time, relaxed timing, sound captions, a slower or single double-click, the large cursor, and
    /// the options that used to ride on Reduce flashing (sudden sounds, screen shake) plus mono audio. All are saved with the settings and
    /// count for achievements like any other play; every one is off by default (a stored -1 follows Reduce flashing, as before).
    /// </summary>
    public static class AccessSettings
    {
        public static bool TugAssistHold;
        public static bool ClickLockOn;
        public static NoticeTime NoticeTime;
        public static bool RelaxedTimingOn;
        public static bool Captions;
        /// <summary>The stored sudden-sounds value (-1 follows Reduce flashing).</summary>
        public static int SuddenSoundsStored = -1;
        /// <summary>The stored shake value (-1 follows Reduce flashing).</summary>
        public static int ShakeStored = -1;
        public static bool MonoAudio;
        public static bool LargeCursor;
        public static ClickSpeed ClickSpeed;
        /// <summary>The player's click lock filter (the runtime feeds the raw button through it every frame).</summary>
        public static readonly ClickLock Lock = new ClickLock();

        /// <summary>Raised when an option the drawing code caches (the large cursor) changed.</summary>
        public static event System.Action Changed;

        public static void Load(SettingsData s)
        {
            if (s == null) return;
            TugAssistHold = s.tugAssist == "hold";
            ClickLockOn = s.clickLock;
            Lock.Enabled = ClickLockOn;
            if (!ClickLockOn) Lock.Clear();
            NoticeTime = NoticeRules.Parse(s.noticeTime);
            RelaxedTimingOn = s.relaxedTiming;
            Captions = s.captions;
            SuddenSoundsStored = s.suddenSounds;
            ShakeStored = s.shake;
            MonoAudio = s.monoAudio;
            LargeCursor = s.largeCursor;
            ClickSpeed = AccessOptions.ClickFrom(s.clickSpeed);
            Changed?.Invoke();
        }

        public static void Save(SettingsData s)
        {
            if (s == null) return;
            s.tugAssist = TugAssistHold ? "hold" : "off";
            s.clickLock = ClickLockOn;
            s.noticeTime = NoticeRules.Id(NoticeTime);
            s.relaxedTiming = RelaxedTimingOn;
            s.captions = Captions;
            s.suddenSounds = SuddenSoundsStored;
            s.shake = ShakeStored;
            s.monoAudio = MonoAudio;
            s.largeCursor = LargeCursor;
            s.clickSpeed = (int)ClickSpeed;
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

        // ------------------------------------------------------------------ Phase Q4

        /// <summary>The factor on the real-time windows: 2 in Story mode or with Relaxed timing on, else 1.</summary>
        public static float TimeScale(bool story) => RelaxedTiming.Scale(story, RelaxedTimingOn);

        /// <summary>Sudden sounds softened: the explicit setting, else what Reduce flashing says.</summary>
        public static bool SoftSounds(bool reduceFlashing) => AccessOptions.SoftSoundsFrom(SuddenSoundsStored, reduceFlashing);

        public static ShakeLevel Shake(bool reduceFlashing) => AccessOptions.ShakeFrom(ShakeStored, reduceFlashing);

        public static void SetNoticeTime(NoticeTime mode)
        {
            NoticeTime = mode;
            GameLog.Info(LogChannel.Player, "Notice time: " + NoticeRules.Id(mode));
        }

        public static void SetRelaxedTiming(bool on)
        {
            RelaxedTimingOn = on;
            GameLog.Info(LogChannel.Player, "Relaxed timing: " + (on ? "On" : "Off"));
        }

        public static void SetCaptions(bool on)
        {
            Captions = on;
            GameLog.Info(LogChannel.Player, "Sound captions: " + (on ? "On" : "Off"));
        }

        public static void SetSuddenSoft(bool soft)
        {
            SuddenSoundsStored = soft ? 1 : 0;
            GameLog.Info(LogChannel.Player, "Sudden sounds: " + (soft ? "Softened" : "Normal"));
        }

        public static void SetShake(ShakeLevel level)
        {
            ShakeStored = (int)level;
            GameLog.Info(LogChannel.Player, "Screen shake: " + level);
        }

        public static void SetMonoAudio(bool on)
        {
            MonoAudio = on;
            GameLog.Info(LogChannel.Player, "Mono audio: " + (on ? "On" : "Off"));
        }

        public static void SetLargeCursor(bool on)
        {
            LargeCursor = on;
            GameLog.Info(LogChannel.Player, "Large cursor: " + (on ? "On" : "Off"));
            Changed?.Invoke();
        }

        public static void SetClickSpeed(ClickSpeed speed)
        {
            ClickSpeed = speed;
            GameLog.Info(LogChannel.Player, "Double-click: " + speed);
        }
    }
}
