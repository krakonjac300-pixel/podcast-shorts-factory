using SecondCursor.Core.Entity;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase P: the tug panel's words come from two sets of keys: the speed model's (tug.*, notify.conflict*) and the reel's (haul.*,
    /// notify.haul*). The hold assist and the finale's LetGo hold have their own ready and label lines.
    /// </summary>
    public static class TugText
    {
        /// <summary>The panel's key for <paramref name="part"/> ("ready", "label", "label.first", "ahead", "lost.still", "won"...).</summary>
        public static string Key(TugModel model, TugVariant variant, string part)
        {
            if (model == TugModel.Speed) return "tug." + part;
            if ((part == "ready" || part == "label") && variant != TugVariant.Fight) return "haul." + part + (variant == TugVariant.Hold ? ".hold" : ".letgo");
            return "haul." + part;
        }

        /// <summary>The result notice's key: "" (she pulled harder), "won", "won.tear", "kept", or a loss reason ("still", "release"...).</summary>
        public static string NoticeKey(TugModel model, string part)
        {
            string root = model == TugModel.Speed ? "notify.conflict" : "notify.haul";
            return part.Length == 0 ? root : root + "." + part;
        }
    }
}
