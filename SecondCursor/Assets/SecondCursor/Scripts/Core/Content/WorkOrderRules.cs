using SecondCursor.Core.Story;

namespace SecondCursor.Core.Content
{
    /// <summary>
    /// Phase L: work orders the player can decide either way. Such an order has a written result for each decision; the
    /// decision is remembered under a per-night key so a later night can read it back.
    /// </summary>
    public static class WorkOrderRules
    {
        public static bool IsChoice(WorkOrderData o) => o != null && o.resultApprove.Length > 0 && o.resultReject.Length > 0;

        /// <summary>"m.n2.wo3320.reject": the decision of one order on one night.</summary>
        public static string MemoryKey(int night, string orderId, string decision) =>
            MemoryFlags.Prefix + "n" + night + "." + orderId.Replace("_", "") + "." + decision;

        /// <summary>Keep every filed decision, including ordinary orders and work completed by another session.</summary>
        public static void Remember(NarrativeFlags flags, int night, string orderId, string decision, string credit = null)
        {
            if (decision != "approve" && decision != "reject") return;
            flags.Clear(MemoryKey(night, orderId, decision == "approve" ? "reject" : "approve"));
            flags.Set(MemoryKey(night, orderId, decision));
            flags.SetChoice(MemoryKey(night, orderId, "credit"), credit ?? "");
        }

        public static string RememberedCredit(NarrativeFlags flags, int night, string orderId) =>
            flags.GetChoice(MemoryKey(night, orderId, "credit"));

        /// <summary>The decision the player made on that night ("approve" or "reject"), or null (none was made).</summary>
        public static string Remembered(NarrativeFlags flags, int night, string orderId)
        {
            foreach (var decision in new[] { "approve", "reject" })
                if (flags.Has(MemoryKey(night, orderId, decision))) return decision;
            return null;
        }

        /// <summary>What a decision did ("" for no decision: an order nobody decided).</summary>
        public static string ResultFor(WorkOrderData o, string decision) => decision == "approve" ? o.resultApprove : decision == "reject" ? o.resultReject : "";

        public static string NoteFor(WorkOrderData o, string decision) => decision == "approve" ? o.noteApprove : decision == "reject" ? o.noteReject : "";
    }
}
