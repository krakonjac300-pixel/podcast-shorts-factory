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
            else if (g.Night == 3) ForNight3(g);
        }

        /// <summary>Where Gary dropped his own record on Night 2 (desktop, kept branch).</summary>
        public static readonly UnityEngine.Vector2 Gary209Spot = new UnityEngine.Vector2(560f, 220f);

        /// <summary>
        /// Night 3 (spec 5.7): both earlier nights' end state, their mail read, their orders decided, 214 and 209
        /// where Night 2 left them, the shelf checks held back for the round, templates filled from memory.
        /// </summary>
        public static void ForNight3(GameServices g)
        {
            var f = g.Files;
            // A Night 3 without a Night 2 behind it (debug) keeps Gary, like a jump past Night 2's choice.
            if (!g.Flags.Has(MemoryFlags.N2FinishedGary) && !g.Flags.Has(MemoryFlags.N2KeptGary)) g.Flags.Set(MemoryFlags.N2KeptGary);
            bool finished = g.Flags.Has(MemoryFlags.N2FinishedGary);

            // 1. Night 1 and Night 2's end state.
            foreach (var id in new[]
                     {
                         ContentIds.FileLedger, ContentIds.FileBatchA, ContentIds.FileBatchB, ContentIds.FileBatchC,
                         ContentIds.Batch45A, ContentIds.Batch45B, ContentIds.Batch45C,
                         ContentIds.Batch46A, ContentIds.Batch46B, ContentIds.Batch46C, ContentIds.Batch46D,
                     })
                if (f.Exists(id) && f.FolderOf(id) == ContentIds.FolderIntake) f.Move(id, ContentIds.FolderArchive, Actor.System);
            f.SetHidden(ContentIds.File214, false);
            if (g.Flags.Has(MemoryFlags.N2Hid214) && f.Exists(ContentIds.File214)) f.Move(ContentIds.File214, ContentIds.FolderArchive, Actor.System);
            f.SetHidden(ContentIds.File209, false);
            if (finished)
            {
                if (f.Exists(ContentIds.File209) && f.Shred(ContentIds.File209, Actor.System)) g.Shred.MarkShredded();
                // Custodial emptied the bin on Night 2: it starts empty tonight.
                g.Shred.ResetBin();
                var gary = g.Content.Employee(ContentIds.Employee209);
                if (gary != null)
                {
                    gary.status = "RETAINED";
                    gary.office = "Sublevel C (shelf 16)";
                    gary.notes = "Profile complete 11/19/98. Retained per Policy 7.4.";
                }
            }
            else if (g.Flags.Has(MemoryFlags.N2ArchivedGary))
            {
                if (f.Exists(ContentIds.File209)) f.Move(ContentIds.File209, ContentIds.FolderArchive, Actor.System);
            }
            else if (f.Exists(ContentIds.File209))
            {
                f.Move(ContentIds.File209, ContentIds.FolderDesktop, Actor.System);
                g.Desktop.SetFilePosition(ContentIds.File209, OS.OSLayers.WorldToDesktop(Gary209Spot) - new UnityEngine.Vector2(37f, 16f));
            }

            // 2. The earlier nights' mail is in the inbox, already read.
            foreach (var id in new[]
                     {
                         ContentIds.MailIt, ContentIds.MailUrgent, ContentIds.MailSupervisorCheck, ContentIds.MailNoSender,
                         ContentIds.MailN2RuthWarning, ContentIds.MailN2Urgent209, ContentIds.MailN2SecurityRounds,
                     })
                g.Mail.Deliver(id, false);
            foreach (var id in new[]
                     {
                         ContentIds.MailWelcome, ContentIds.MailIt, ContentIds.MailUrgent, ContentIds.MailSupervisorCheck, ContentIds.MailNoSender,
                         ContentIds.MailN2Briefing, ContentIds.MailN2Castell, ContentIds.MailN2Facilities,
                         ContentIds.MailN2RuthWarning, ContentIds.MailN2Urgent209, ContentIds.MailN2SecurityRounds,
                     })
                g.Mail.MarkRead(id, null);
            g.Mail.SortByDate();
            // Earlier orders are decided history; the shelf checks arrive with the round.
            foreach (var id in new[] { ContentIds.Order3317, ContentIds.Order3318, ContentIds.Order3319, ContentIds.Order3321 })
            {
                var order = g.Content.Order(id);
                if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
            }
            // Night 2's choice orders are history too, as the player decided them (an order nobody decided stays cancelled).
            foreach (var id in new[] { ContentIds.Order3320, ContentIds.Order3322 })
            {
                if (g.Orders.DecisionFor(id) != null) continue;
                string decision = WorkOrderRules.Remembered(g.Flags, 2, id);
                if (decision == null) g.Orders.Cancel(id);
                else g.Orders.Decide(id, decision, null);
            }
            // The shelf checks arrive with the round; tonight's choice orders with their tasks.
            foreach (var id in new[] { ContentIds.Order3340, ContentIds.Order3341, ContentIds.Order3342, ContentIds.Order3332, ContentIds.Order3333 }) g.Orders.SetHidden(id, true);

            // 3. Her record has been open since Night 1; employee_017.dat is "in use" until the finale.
            g.Flags.Set(Flags.Staff017Revealed);
            g.Shred.IsInUse = id => id == ContentIds.File017;

            // 4. Template tokens (the BIOS line for device 3 too).
            FillTemplates(g);
            var tokens = NightTemplates.Tokens(g.Flags, g.Save != null ? g.Save.playerLines : null, g.Save != null ? g.Save.playerLineMinutes : null);
            var bios = g.Content.Story.biosLines;
            for (int i = 0; i < bios.Length; i++) bios[i] = NightTemplates.Fill(bios[i], tokens);
            GameLog.Info(LogChannel.Story, "Night 3 set up (Gary " + (finished ? "finished" : "kept") + ")");
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
            g.Mail.SortByDate();
            // Last night's work orders are decided history (not pending again in tonight's list).
            foreach (var id in new[] { ContentIds.Order3317, ContentIds.Order3318 })
            {
                var order = g.Content.Order(id);
                if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
            }
            // Tonight's choice orders stay out of Work Orders until their tasks are given.
            g.Orders.SetHidden(ContentIds.Order3320, true);
            g.Orders.SetHidden(ContentIds.Order3322, true);
            // 3. Her record has been open since Night 1. The cameras and Restricted stay locked; the bin is empty.
            g.Flags.Set(Flags.Staff017Revealed);
            // 4. employee_017.dat is "in use" all night.
            g.Shred.IsInUse = id => id == ContentIds.File017;
            // 5. Template tokens.
            FillTemplates(g);
            GameLog.Info(LogChannel.Story, "Night 2 set up (" + g.Save.playerLines.Length + " remembered line(s))");
        }

        /// <summary>Fill the {tokens} of every file tagged "template" (spec 2.4) and of the Personnel notes (Phase L).</summary>
        public static void FillTemplates(GameServices g)
        {
            var tokens = NightTemplates.Tokens(g.Flags, g.Save != null ? g.Save.playerLines : null, g.Save != null ? g.Save.playerLineMinutes : null);
            foreach (var e in g.Content.Employees.employees)
                if (e != null && e.notes.IndexOf('{') >= 0) e.notes = NightTemplates.Fill(e.notes, tokens);
            foreach (var f in g.Files.AllFiles)
            {
                if (!f.HasTag("template")) continue;
                string filled = NightTemplates.Fill(f.Content, tokens);
                if (filled != f.Content) g.Files.SetContent(f.Id, filled);
            }
        }
    }
}
