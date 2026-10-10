using System;

namespace SecondCursor.Core.Game
{
    /// <summary>One achievement: its Steamworks API id and whether Records hides it until it unlocks.</summary>
    public sealed class AchievementDef
    {
        public readonly string Id;
        public readonly bool Hidden;

        public AchievementDef(string id, bool hidden)
        {
            Id = id;
            Hidden = hidden;
        }

        /// <summary>strings.json key of the name ("ach.ACH_NIGHT_1.name").</summary>
        public string NameKey => "ach." + Id + ".name";
        /// <summary>strings.json key of the description.</summary>
        public string DescKey => "ach." + Id + ".desc";
    }

    /// <summary>
    /// The 19 achievements of expansion spec Section 9 (API ids are the Steamworks ids), the TUG_WINS stat, and the
    /// Night 3 ending ids that make up Every Way Out. Names and descriptions live in the base strings.json.
    /// </summary>
    public static class AchievementIds
    {
        public const string Night1 = "ACH_NIGHT_1", Night2 = "ACH_NIGHT_2", Night3 = "ACH_NIGHT_3";
        public const string EndShred = "ACH_END_SHRED", EndKeep = "ACH_END_KEEP", EndLogOff = "ACH_END_LOGOFF", AllEndings = "ACH_ALL_ENDINGS";
        public const string FirmGrip = "ACH_FIRM_GRIP", WhiteKnuckles = "ACH_WHITE_KNUCKLES";
        public const string DoNotRead = "ACH_DO_NOT_READ", RemoteSession = "ACH_REMOTE_SESSION", Finished = "ACH_FINISHED", Half = "ACH_HALF";
        public const string HisGlasses = "ACH_HIS_GLASSES", HerName = "ACH_HER_NAME", Authorized = "ACH_AUTHORIZED";
        public const string RemainSeated = "ACH_REMAIN_SEATED", NotOnMyShelf = "ACH_NOT_ON_MY_SHELF", Watchers = "ACH_WATCHERS";

        /// <summary>Steam INT stat: lifetime tug-of-war wins (increment only, max 10).</summary>
        public const string TugWinsStat = "TUG_WINS";
        public const int TugWinsGoal = 10;
        /// <summary>Steam shows White Knuckles' progress once at this many wins.</summary>
        public const int TugWinsProgressAt = 5;

        /// <summary>The Night 3 endings (Every Way Out needs all three).</summary>
        public static readonly string[] Night3Endings = { "n3_shred", "n3_keep", "n3_logoff" };

        /// <summary>All 19 in the order of Section 9 (the Records list order).</summary>
        public static readonly AchievementDef[] All =
        {
            new AchievementDef(Night1, false),
            new AchievementDef(Night2, false),
            new AchievementDef(Night3, false),
            new AchievementDef(EndShred, true),
            new AchievementDef(EndKeep, true),
            new AchievementDef(EndLogOff, true),
            new AchievementDef(AllEndings, false),
            new AchievementDef(FirmGrip, false),
            new AchievementDef(WhiteKnuckles, false),
            new AchievementDef(DoNotRead, true),
            new AchievementDef(RemoteSession, true),
            new AchievementDef(Finished, true),
            new AchievementDef(Half, true),
            new AchievementDef(HisGlasses, true),
            new AchievementDef(HerName, true),
            new AchievementDef(Authorized, true),
            new AchievementDef(RemainSeated, false),
            new AchievementDef(NotOnMyShelf, true),
            new AchievementDef(Watchers, true),
        };

        public static AchievementDef Find(string id)
        {
            foreach (var a in All) if (a.Id == id) return a;
            return null;
        }

        public static bool IsKnown(string id) => Find(id) != null;

        public static bool IsHidden(string id) => Find(id)?.Hidden ?? false;

        /// <summary>How many of the 19 are in <paramref name="unlocked"/> (unknown ids are ignored).</summary>
        public static int CountUnlocked(string[] unlocked)
        {
            int n = 0;
            foreach (var a in All) if (Array.IndexOf(unlocked ?? Array.Empty<string>(), a.Id) >= 0) n++;
            return n;
        }
    }
}
