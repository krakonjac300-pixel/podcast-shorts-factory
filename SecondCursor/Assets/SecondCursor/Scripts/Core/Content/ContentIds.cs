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
