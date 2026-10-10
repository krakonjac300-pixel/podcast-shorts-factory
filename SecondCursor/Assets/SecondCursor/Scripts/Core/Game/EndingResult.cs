using SecondCursor.Core.Content;

namespace SecondCursor.Core.Game
{
    public enum EndingResultKind { Unknown, ChapterComplete, Lost, Escaped }

    /// <summary>The player's result is separate from the collectible ending name and the fate of a file.</summary>
    public static class EndingResult
    {
        public const string CameraCapture = "camera_capture";
        public static EndingResultKind For(string endingId)
        {
            switch (endingId)
            {
                case ContentIds.EndingN1Blackout:
                case ContentIds.EndingN2Finished:
                case ContentIds.EndingN2Kept: return EndingResultKind.ChapterComplete;
                case ContentIds.EndingN3Shred:
                case ContentIds.EndingN3Keep:
                case CameraCapture: return EndingResultKind.Lost;
                case ContentIds.EndingN3LogOff: return EndingResultKind.Escaped;
                default: return EndingResultKind.Unknown;
            }
        }

        public static string HeadingKey(string endingId)
        {
            switch (For(endingId))
            {
                case EndingResultKind.ChapterComplete: return "result.chapter";
                case EndingResultKind.Lost: return "result.lost";
                case EndingResultKind.Escaped: return "result.escaped";
                default: return null;
            }
        }

        public static string ExplanationKey(string endingId) =>
            For(endingId) == EndingResultKind.Unknown ? null : "result." + endingId;
    }
}
