using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Game;

namespace SecondCursor.Story
{
    /// <summary>
    /// The world a later night starts in (expansion spec 4.5 and 5.7): Night 1's end state applied to the
    /// fresh content, earlier mail already read, template tokens filled from what the player did before.
    /// Runs once, after content load and before the first beat. Memory and trust were merged by GameRoot.
    /// </summary>
    public static class NightSetup
    {
        public static void ForNight(GameServices g)
        {
            if (g.Night == 2) ForNight2(g);
        }

        public static void ForNight2(GameServices g)
        {
            // 1. Night 1's end state: the ledger and Batch 44 were archived (the temp file is gone via the overlay).
            foreach (var id in new[] { ContentIds.FileLedger, ContentIds.FileBatchA, ContentIds.FileBatchB, ContentIds.FileBatchC })
                if (g.Files.Exists(id) && g.Files.FolderOf(id) == ContentIds.FolderIntake) g.Files.Move(id, ContentIds.FolderArchive, Actor.System);
            // 2. Last night's mail is in the inbox, already read.
            foreach (var id in new[] { ContentIds.MailIt, ContentIds.MailUrgent, ContentIds.MailSupervisorCheck, ContentIds.MailNoSender })
                g.Mail.Deliver(id, false);
            foreach (var id in new[] { ContentIds.MailWelcome, ContentIds.MailIt, ContentIds.MailUrgent, ContentIds.MailSupervisorCheck, ContentIds.MailNoSender })
                g.Mail.MarkRead(id, null);
            // Last night's work orders are decided history (not pending again in tonight's list).
            foreach (var id in new[] { ContentIds.Order3317, ContentIds.Order3318 })
            {
                var order = g.Content.Order(id);
                if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
            }
            // 3. Her record has been open since Night 1. The cameras and Restricted stay locked; the bin is empty.
            g.Flags.Set(Flags.Staff017Revealed);
            // 4. employee_017.dat is "in use" all night.
            g.Shred.IsInUse = id => id == ContentIds.File017;
            // 5. Template tokens.
            FillTemplates(g);
            GameLog.Info(LogChannel.Story, "Night 2 set up (" + g.Save.playerLines.Length + " remembered line(s))");
        }

        /// <summary>Fill the {tokens} of every file tagged "template" (spec 2.4).</summary>
        public static void FillTemplates(GameServices g)
        {
            var tokens = NightTemplates.Tokens(g.Flags, g.Save != null ? g.Save.playerLines : null);
            foreach (var f in g.Files.AllFiles)
            {
                if (!f.HasTag("template")) continue;
                string filled = NightTemplates.Fill(f.Content, tokens);
                if (filled != f.Content) g.Files.SetContent(f.Id, filled);
            }
        }
    }
}
