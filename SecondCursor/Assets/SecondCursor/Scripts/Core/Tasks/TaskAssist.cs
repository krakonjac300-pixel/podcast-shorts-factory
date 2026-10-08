using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;

namespace SecondCursor.Core.Tasks
{
    /// <summary>What the assist ladder asks the story to do this frame.</summary>
    public enum AssistStep { None, Hint, RepeatHint, Offer }

    /// <summary>
    /// Phase Q1 (owner's "assistance before takeover"): a task the player is stuck on is never finished silently. The ladder is a contextual
    /// hint, a repeat hint (with the next step spelled out), then an offer the player answers ("Night Operations can finish this task for
    /// you. Finish it / Not now"). Only active time counts as being stuck: time spent reading (see <see cref="ReadingRule"/>) does not, and
    /// any progress starts the count again. After "Not now" the hints go on and the offer comes back after <see cref="ReOffer"/> seconds.
    /// A task the player must decide (a choice order) gets hints only. Engine-free; the night director ticks it.
    /// </summary>
    public sealed class TaskAssist
    {
        public readonly float FirstHint, Repeat, ReOffer;
        public readonly bool OfferAllowed;

        /// <summary>Seconds of active (not reading) time since the task was given, the last progress or the last "Not now".</summary>
        public float StuckSeconds { get; private set; }
        /// <summary>Active seconds since the task was given (progress and "Not now" do not reset it): a choice order lapses on this.</summary>
        public float ActiveSeconds { get; private set; }
        public int HintsShown { get; private set; }
        public int OffersMade { get; private set; }
        public int Declines { get; private set; }
        public bool OfferOpen { get; private set; }
        public bool Accepted { get; private set; }

        float _nextHint, _nextOffer;
        int _progress = -1;

        public TaskAssist(float firstHint, float repeat, float reOffer, bool offerAllowed)
        {
            FirstHint = Math.Max(1f, firstHint);
            Repeat = Math.Max(1f, repeat);
            ReOffer = Math.Max(Repeat, reOffer);
            OfferAllowed = offerAllowed;
            Restart();
        }

        /// <summary>The offer comes after the first hint and one repeat hint have had their time.</summary>
        public float FirstOfferAt => FirstHint + 2f * Repeat;

        /// <summary>The player asks for help directly, without waiting for the timed hint ladder.</summary>
        public bool RequestOffer()
        {
            if (!OfferAllowed || Accepted) return false;
            if (!OfferOpen) { OfferOpen = true; OffersMade++; }
            return true;
        }

        void Restart()
        {
            StuckSeconds = 0f;
            _nextHint = FirstHint;
            _nextOffer = FirstOfferAt;
        }

        /// <param name="dt">Seconds since the last tick.</param>
        /// <param name="reading">The player is reading (it does not count as being stuck).</param>
        /// <param name="canOffer">Nothing is in the way of a dialog right now (no tug, shred, other dialog or climax).</param>
        /// <param name="progress">The task's done count (a rise means the player is not stuck).</param>
        public AssistStep Tick(float dt, bool reading, bool canOffer, int progress)
        {
            if (Accepted) return AssistStep.None;
            if (_progress >= 0 && progress > _progress && !OfferOpen) Restart();
            _progress = progress;
            if (OfferOpen || reading || dt <= 0f) return AssistStep.None;
            StuckSeconds += dt;
            ActiveSeconds += dt;
            if (OfferAllowed && canOffer && StuckSeconds >= _nextOffer)
            {
                OfferOpen = true;
                OffersMade++;
                return AssistStep.Offer;
            }
            if (StuckSeconds < _nextHint) return AssistStep.None;
            _nextHint = StuckSeconds + Repeat;
            return HintsShown++ == 0 ? AssistStep.Hint : AssistStep.RepeatHint;
        }

        /// <summary>The player chose "Not now": hints go on at the repeat interval, the offer returns after <see cref="ReOffer"/> active seconds.</summary>
        public void Decline()
        {
            if (!OfferOpen) return;
            OfferOpen = false;
            Declines++;
            StuckSeconds = 0f;
            _nextHint = Repeat;
            _nextOffer = ReOffer;
        }

        /// <summary>The player chose "Finish it".</summary>
        public void Accept()
        {
            if (!OfferOpen) return;
            OfferOpen = false;
            Accepted = true;
        }

        /// <summary>"Finish it" could not be done through the world (nothing was ticked): the ladder goes on and offers again later.</summary>
        public void AcceptFailed()
        {
            if (!Accepted) return;
            Accepted = false;
            StuckSeconds = 0f;
            _nextHint = Repeat;
            _nextOffer = ReOffer;
        }

        /// <summary>The offer went away unanswered (a jump, a climax): it counts as not answered, and comes back later like "Not now".</summary>
        public void Withdraw()
        {
            if (!OfferOpen) return;
            OfferOpen = false;
            StuckSeconds = 0f;
            _nextHint = Repeat;
            _nextOffer = ReOffer;
        }
    }

    /// <summary>
    /// Phase Q1: time spent reading is not time spent stuck. Reading is a focused Mail, document (Data Viewer, Jotter), Personnel record,
    /// Help or Camera Viewer window while the player is still there (any pointer, button, wheel or key input in the last
    /// <see cref="IdleLimit"/> seconds), or a scroll in the last <see cref="ScrollGrace"/> seconds. A player who has done nothing at all for
    /// longer is more likely lost than reading, and gets the hint (a hint is only a notice; the offer still asks).
    /// </summary>
    public static class ReadingRule
    {
        public const float ScrollGrace = 8f;
        public const float IdleLimit = 30f;

        static readonly string[] ReadingApps = { AppIds.Mail, AppIds.DataViewer, AppIds.Notepad, AppIds.Staff, AppIds.Help, AppIds.Camera, AppIds.WorkOrders, AppIds.Files };

        public static bool IsReadingApp(string appId) => !string.IsNullOrEmpty(appId) && Array.IndexOf(ReadingApps, appId) >= 0;

        public static bool IsReading(string focusedAppId, float secondsSinceScroll, float secondsSinceInput) =>
            (IsReadingApp(focusedAppId) && secondsSinceInput < IdleLimit) || (secondsSinceScroll >= 0f && secondsSinceScroll < ScrollGrace);
    }

    /// <summary>Phase Q1: who did a target that is not a file move (a file's move is kept on the file itself).</summary>
    public sealed class TaskCredits
    {
        readonly Dictionary<string, string> _by = new Dictionary<string, string>(StringComparer.Ordinal);

        static string Key(TaskType type, string id) => (int)type + ":" + id;

        /// <summary><paramref name="by"/> null or empty = the player did it (any earlier credit is dropped).</summary>
        public void Set(TaskType type, string id, string by)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (string.IsNullOrEmpty(by)) _by.Remove(Key(type, id));
            else _by[Key(type, id)] = by;
        }

        public string Get(TaskType type, string id) => id != null && _by.TryGetValue(Key(type, id), out var by) ? by : null;
    }

    /// <summary>What finishing one target of a task takes.</summary>
    public enum FinishKind { MoveFile, ShredFile, DecideOrder, ReadMail, OpenFile, ViewEmployee }

    public readonly struct FinishStep
    {
        public readonly FinishKind Kind;
        public readonly string Target;
        /// <summary>The folder a file goes to, or the decision an order gets.</summary>
        public readonly string Param;

        public FinishStep(FinishKind kind, string target, string param) { Kind = kind; Target = target; Param = param; }

        public override string ToString() => Kind + " " + Target + (string.IsNullOrEmpty(Param) ? "" : " -> " + Param);
    }

    /// <summary>
    /// Phase Q1: how an accepted offer finishes a task through the world (so the queue, the order list, the folders and the notice always
    /// agree): one step per target that is not done yet. An order gets the decision its own rule gives (<paramref name="ruleDecision"/>).
    /// </summary>
    public static class TaskFinisher
    {
        public const string NightOperations = "Night Operations";

        public static List<FinishStep> Plan(WorkTaskManager tasks, WorkTask t, Func<string, string> ruleDecision)
        {
            var steps = new List<FinishStep>();
            if (tasks == null || t == null || t.State != TaskState.Active) return steps;
            foreach (var id in t.Data.targets)
            {
                if (tasks.IsTargetDone(t, id)) continue;
                switch (t.Type)
                {
                    case TaskType.MoveFile: steps.Add(new FinishStep(FinishKind.MoveFile, id, t.Data.param)); break;
                    case TaskType.DeleteFile: steps.Add(new FinishStep(FinishKind.ShredFile, id, null)); break;
                    case TaskType.DecideOrder: steps.Add(new FinishStep(FinishKind.DecideOrder, id, ruleDecision?.Invoke(id) ?? "reject")); break;
                    case TaskType.ReadEmail: steps.Add(new FinishStep(FinishKind.ReadMail, id, null)); break;
                    case TaskType.OpenFile: steps.Add(new FinishStep(FinishKind.OpenFile, id, null)); break;
                    case TaskType.ViewEmployee: steps.Add(new FinishStep(FinishKind.ViewEmployee, id, null)); break;
                }
            }
            return steps;
        }

        /// <summary>Whether Night Operations may offer to finish a task: company work only (not a remote request, not a story line).</summary>
        public static bool CanOffer(WorkTask t) =>
            t != null && !t.IsEntityAuthored && t.Type != TaskType.Wait && t.Type != TaskType.Unknown && t.Data.targets.Length > 0;
    }
}
