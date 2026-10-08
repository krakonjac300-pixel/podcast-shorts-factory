using System;

namespace SecondCursor.Core.Game
{
    /// <summary>A readable warning window: sustained viewing causes capture, two seconds away prevents it.</summary>
    public sealed class CameraExposure
    {
        public const float CaptureSeconds = 9f;
        public const float EscapeSeconds = 2f;
        public float WatchedSeconds { get; private set; }
        public float AwaySeconds { get; private set; }
        public bool Captured => WatchedSeconds >= CaptureSeconds;
        public bool Escaped => AwaySeconds >= EscapeSeconds;
        public bool Finished => Captured || Escaped;

        public void Tick(bool watching, float seconds)
        {
            if (Finished || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
            if (watching)
            {
                AwaySeconds = 0f;
                WatchedSeconds = Math.Min(CaptureSeconds, WatchedSeconds + seconds);
            }
            else AwaySeconds = Math.Min(EscapeSeconds, AwaySeconds + seconds);
        }
    }
}
