using System;

namespace SecondCursor.Core.Game
{
    /// <summary>The state of Night 1's ordinary work, before the first cursor confrontation.</summary>
    [Serializable]
    public sealed class OpeningCheckpoint
    {
        public bool quickStartDismissed;
        public OpeningFile[] files = Array.Empty<OpeningFile>();
        public OpeningMail[] mail = Array.Empty<OpeningMail>();
        public OpeningOrder[] orders = Array.Empty<OpeningOrder>();
        public OpeningTask[] tasks = Array.Empty<OpeningTask>();

        public static bool Supports(int night, string beat) => night == 1 && (beat == "work" || beat == "anomaly");
    }

    [Serializable]
    public sealed class OpeningFile
    {
        public string id, folder, name, content, movedBy, shredCredit;
        public bool shredded, hidden;
    }

    [Serializable]
    public sealed class OpeningMail
    {
        public string id, date, credit;
        public bool read, live;
    }

    [Serializable]
    public sealed class OpeningOrder
    {
        public string id, decision, credit;
    }

    [Serializable]
    public sealed class OpeningTask
    {
        public string id, finishedBy;
        public bool completed;
    }
}
