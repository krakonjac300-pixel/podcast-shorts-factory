using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q2 (memory made loud): the player's own words and name in her lines ({LINE1}, {NAME}...; every echo passes
    /// <see cref="EchoFilter"/>), a name given in a Jotter, the "Session 017 kept a copy." notice on the inputs that really carry
    /// over (and only those), and the night's measurements (<see cref="CaptureMeter"/>).
    /// </summary>
    public abstract partial class NightDirector
    {
        Dictionary<string, string> _tokens;
        readonly HashSet<string> _keptNoticed = new HashSet<string>();
        /// <summary>What this night measures for Capture Profile 214 and the Retention Record.</summary>
        protected CaptureMeter Meter { get; private set; }

        /// <summary>The briefing mail whose opens the profile counts (each night's own).</summary>
        protected virtual string BriefingMailId => ContentIds.MailWelcome;

        void InitMemory()
        {
            Meter = new CaptureMeter(_g, BriefingMailId);
        }

        /// <summary>The token values (built from the save once, again when the player gives a name).</summary>
        protected Dictionary<string, string> Tokens
        {
            get
            {
                if (_tokens == null) RefreshTokens();
                return _tokens;
            }
        }

        protected void RefreshTokens()
        {
            _tokens = NightTemplates.ForSave(_g.Flags, _g.Save);
            AddNightTokens(_tokens);
        }

        /// <summary>Tokens only this night knows (Night 3's last input).</summary>
        protected virtual void AddNightTokens(Dictionary<string, string> tokens) { }

        /// <summary>Lines with their tokens filled; a line whose tokens all came out empty is left out.</summary>
        protected string[] Fill(string[] lines) => lines == null ? null : NightTemplates.FillLines(lines, Tokens);

        protected string[] Fill(IEnumerable<string> lines) => lines == null ? null : Fill(new List<string>(lines).ToArray());

        /// <summary>
        /// V3: the player's line read for a name. A name is kept (the save, at once) and answered by the exchange's name group; one of
        /// the story's names is answered in her voice and never kept. Returns the lines to type instead of the usual reply, or null.
        /// </summary>
        string[] NameReply(ExchangeData exchange, string said, bool toSession017, out bool kept)
        {
            kept = false;
            var group = DialogueEngine.NameResponse(exchange);
            if (group == null) return null;
            var r = NameCapture.Read(said);
            if (r.Kind == NameKind.Forbidden)
            {
                // Gary was told Casey too; only her voice answers the story's names.
                if (!string.IsNullOrEmpty(exchange.voice)) return null;
                var lines = _g.Content.Lines("name_" + r.ForbiddenKey);
                GameLog.Info(LogChannel.Story, "Name check: a story name (" + r.ForbiddenKey + "), not kept");
                return lines.Length > 0 ? lines : null;
            }
            if (r.Kind != NameKind.Name) return null;
            kept = true;
            bool changed = SaveSystem.SetPlayerName(_g, r.Name);
            RefreshTokens();
            // What the player typed is theirs: the log only says that a name was kept.
            GameLog.Info(LogChannel.Story, "Name given (" + r.Name.Length + " characters)" + (changed ? ", kept" : ", same as before"));
            // The notice is session 017's: a name told to Gary is kept by the save but not announced as hers.
            if (changed && toSession017) KeptCopy("name");
            return Fill(group.reply);
        }

        /// <summary>
        /// V7: "Session 017 kept a copy." after an input that really carries over (Night 1's first reply and the file in the bin, a name,
        /// Gary finished or kept, 214 put in Archive): once each per night, never for anything cosmetic.
        /// </summary>
        protected void KeptCopy(string what)
        {
            if (IsStandIn || IsPreparing || !_keptNoticed.Add(what)) return;
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Text("notify.kept.copy"), "icon_info", null, "ui_select");
            GameLog.Info(LogChannel.Story, "Kept a copy: " + what);
        }

        /// <summary>The night's measurements for the save.</summary>
        protected CaptureStats NightCapture() => Meter != null ? Meter.Result(_g.Flags, NightElapsed, !_partialNight) : null;

        /// <summary>
        /// Review Q2 (HIGH 2): the night did not run from its first beat (Continue from a checkpoint, a jump), so the meter saw only part of
        /// it. Its figures are then saved as not recorded: nothing is shown rather than a number that is not true.
        /// </summary>
        bool _partialNight;

        /// <summary>Called for every start of the flow: only a start at the first beat measures the whole night.</summary>
        void NoteFlowStart(int index)
        {
            if (index > 0) _partialNight = true;
        }

        /// <summary>Night 1's archive drag for the title's second pointer (null = none).</summary>
        protected virtual int[] GhostPath() => null;

        /// <summary>The rows under this night's card, from what it just measured (the save may be read-only in a QA launch).</summary>
        protected List<RecordRow> CardRows()
        {
            var d = new SaveData();
            d.capture[ClampNight(Night) - 1] = NightCapture() ?? new CaptureStats();
            d.memory = _g.Flags.Snapshot(MemoryFlags.Prefix);
            var c = _g.Content;
            return RetentionRecord.Card(Night, d, (k, a) => a == null ? c.Text(k) : c.Format(k, a));
        }

        static int ClampNight(int night) => night < 1 ? 1 : night > SaveData.Nights ? SaveData.Nights : night;
    }
}
