namespace SecondCursor.Core.Content
{
    /// <summary>
    /// Content IDs that game code refers to. Everything else in the JSON content is free-form and can be
    /// added without touching C#. Keeping these in one place avoids magic strings scattered in systems.
    /// </summary>
    public static class ContentIds
    {
        // Folders
        public const string FolderRoot = "root";
        public const string FolderDesktop = "desktop";
        public const string FolderIntake = "intake";
        public const string FolderArchive = "archive";
        public const string FolderDocuments = "documents";
        public const string FolderRestricted = "restricted";
        public const string FolderSystem = "system";
        public const string FolderDisposal = "disposal";

        // Files
        public const string File017 = "employee_017";
        public const string FileLedger = "ledger_1994";
        public const string FileCache = "cache_tmp";
        public const string FileBatchA = "batch_a";
        public const string FileBatchB = "batch_b";
        public const string FileBatchC = "batch_c";
        public const string FilePrevNotes = "prev_operator_notes";
        public const string FilePolicy = "policy_retention";
        public const string FileSessionLog = "session_log";

        // Emails
        public const string MailWelcome = "mail_welcome";
        public const string MailIt = "mail_it_maintenance";
        public const string MailUrgent = "mail_urgent_017";
        public const string MailSupervisorCheck = "mail_supervisor_check";
        public const string MailNoSender = "mail_no_sender";

        // Employees / work orders
        public const string Employee017 = "017";
        public const string Order3317 = "wo_3317";
        public const string Order3318 = "wo_3318";

        // Tasks (in shift order)
        public const string TaskReadBriefing = "t_read_briefing";
        public const string TaskArchiveLedger = "t_archive_ledger";
        public const string TaskVerify3317 = "t_verify_3317";
        public const string TaskVerify3318 = "t_verify_3318";
        public const string TaskShredCache = "t_shred_cache";
        public const string TaskArchiveBatch = "t_archive_batch";
        public const string TaskShred017 = "t_shred_017";

        // Dialogue
        public const string ExchangeStop = "ex_stop";

        // Cameras
        public const string Cam01 = "cam01";
        public const string Cam02 = "cam02";
        public const string Cam03 = "cam03";
        public const string Cam04 = "cam04";
        public const string Cam00 = "cam00";

        // ---------------------------------------------------------------- Night 2 (expansion spec 11.5)
        public const string MailN2Briefing = "mail_n2_briefing", MailN2Castell = "mail_n2_castell", MailN2Facilities = "mail_n2_facilities";
        public const string MailN2RuthWarning = "mail_n2_ruth_warning", MailN2Urgent209 = "mail_n2_urgent_209", MailN2SecurityRounds = "mail_n2_security_rounds";
        public const string File209 = "employee_209", File214 = "employee_214", FileCacheN2 = "cache_tmp_n2", FileDoorLog = "b7_door_log";
        public const string Order3319 = "wo_3319", Order3321 = "wo_3321";
        public const string TaskN2Briefing = "t2_read_briefing", TaskN2Batch45 = "t2_archive_batch45", TaskN2Verify3319 = "t2_verify_3319",
            TaskN2Verify3321 = "t2_verify_3321", TaskN2Cache = "t2_shred_cache", TaskN2Batch46 = "t2_archive_batch46",
            TaskE2DoorLog = "e2_door_log", TaskE2Lookup163 = "e2_lookup_163", TaskE2Hide214 = "e2_hide_214",
            TaskN2Shred209 = "t2_shred_209", TaskE2Archive209 = "e2_archive_209";
        public const string ExchangeN2Back = "ex2_back", ExchangeN2GaryOne = "ex2_gary_one";

        // ---------------------------------------------------------------- Night 3
        public const string MailN3Briefing = "mail_n3_briefing", MailN3Undeliverable = "mail_n3_undeliverable", MailN3RuthComment = "mail_n3_ruth_comment";
        public const string MailN3SecurityRounds = "mail_n3_security_rounds", MailN3NoSubject = "mail_n3_nosubject";
        public const string FileBatch47B = "batch47_b", FileCacheN3 = "cache_tmp_n3", FileSessionCfg = "session_cfg", FileCamviewCfg = "camview_cfg", FileSeatB7 = "seat_b7";
        public const string Order3330 = "wo_3330", Order3331 = "wo_3331", Order3340 = "wo_3340", Order3341 = "wo_3341", Order3342 = "wo_3342";
        public const string TaskN3Briefing = "t3_read_briefing", TaskN3Batch47 = "t3_archive_batch47", TaskN3Verify3330 = "t3_verify_3330",
            TaskN3Verify3331 = "t3_verify_3331", TaskN3Cache = "t3_shred_cache", TaskN3Batch48 = "t3_archive_batch48", TaskN3Shelf = "t3_shelf_check";
        public const string ExchangeN3Ruth = "ex3_ruth", ExchangeN3Final = "ex3_final", ExchangeN3Confirm = "ex3_confirm";
        public const string Employee000 = "000", Employee001 = "001", Employee118 = "118", Employee209 = "209";

        // ---------------------------------------------------------------- endings
        public const string EndingN1Blackout = "n1_blackout";
    }

    /// <summary>Application identifiers used by the fake OS.</summary>
    public static class AppIds
    {
        public const string Mail = "mail";
        public const string Files = "files";
        public const string Notepad = "notepad";
        public const string Staff = "staff";
        public const string Camera = "camera";
        public const string WorkOrders = "workorders";
        public const string WorkQueue = "workqueue";
        public const string Disposal = "disposal";
        public const string Workstation = "workstation";
        public const string Help = "help";
        public const string DataViewer = "dataviewer";
    }
}
