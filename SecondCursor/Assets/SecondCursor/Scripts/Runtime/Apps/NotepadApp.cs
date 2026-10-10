using System;
using System.Collections;
using System.Text;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Plain text editor. Opens text files, lets the player type, and is the second cursor's voice: it can
    /// type into the document character by character (with keyboard sounds you are not making), and in
    /// conversation mode it hands each line the player types (on Enter) to the story.
    /// </summary>
    public sealed class NotepadApp : App, IKeyboardTarget, ITextEntryTarget
    {
        readonly string _fileId;
        readonly StringBuilder _text = new StringBuilder();
        ScrollArea _scroll;
        PixelText _view;
        float _caretBlink;
        int _inputStart;

        /// <summary>When false the player cannot type (e.g. while the entity is typing).</summary>
        public bool PlayerCanType = true;
        /// <summary>In conversation mode, Enter submits the player's line instead of just inserting a newline.</summary>
        public bool ConversationMode;
        public bool EntityTyping { get; private set; }
        /// <summary>A conversation that waits for the player's next line right now (it is the player's turn).</summary>
        public bool WaitsForPlayer => IsOpen && ConversationMode && PlayerCanType && !EntityTyping;
        /// <summary>A conversation whose other side is typing or thinking (a reply typed now waits for the turn).</summary>
        public bool IsTalking => IsOpen && ConversationMode && (EntityTyping || ThinkingCaret);
        /// <summary>The speaker is "thinking" before a reply: the caret blinks although nobody types.</summary>
        public bool ThinkingCaret;
        /// <summary>Seconds until silence answers this turn; negative means no active reply deadline.</summary>
        public float ReplySecondsRemaining = -1f;
        public bool HasPendingReply => ConversationMode && !string.IsNullOrWhiteSpace(PendingInput);
        public event Action<string, CursorAgent> LineSubmitted;
        public float LastPlayerKeyTime { get; private set; } = -100f;
        /// <summary>Phase K: who is on the other side of a conversation ("session 017"), for its title and status line.</summary>
        public string SessionLabel = "the remote session";

        /// <summary>
        /// Phase K (suggestion 3): a conversation names its session and person in the title and wears that cursor's colours on its
        /// caption, so two Jotters at once never look alike ("Session 017: Ellen Marsh - Jotter").
        /// </summary>
        public void SetConversation(string session, string title, Color32 captionA, Color32 captionB, Color32 titleText)
        {
            SessionLabel = session;
            _baseTitle = title + " - " + G.Content.Text("app.notepad");
            Window.SetTitle(_baseTitle);
            Window.SetCaptionColors(captionA, captionB, titleText);
        }

        public override string AppId => AppIds.Notepad;
        public string Text => _text.ToString();
        public string FileId => _fileId;

        public NotepadApp(string fileId = null)
        {
            _fileId = fileId;
        }

        /// <summary>
        /// Steam Deck keyboard: for a whole conversation (typed-ahead keys are held, so it does not flicker every
        /// turn), and for a document the player can type into (an untitled page or an editable file).
        /// </summary>
        public bool WantsTextEntry => IsOpen && (ConversationMode || ((_fileId == null ? _openedByPlayer : CanSave) && PlayerCanType && !EntityTyping));

        /// <summary>The player opened this page (a cursor's own Notepad becomes a conversation instead).</summary>
        bool _openedByPlayer;

        public Game.TextEntryMode EntryMode => ConversationMode ? Game.TextEntryMode.SingleLine : Game.TextEntryMode.MultiLine;

        public Rect EntryRectVirtual => Window != null ? Window.WorldRect : default;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            _openedByPlayer = by != null && by.IsPlayer;
            var file = _fileId != null ? G.Files.GetFile(_fileId) : null;
            string title = (file != null ? file.Name : "Untitled") + " - " + G.Content.Text("app.notepad");
            int offset = G.Apps.OpenApps.Count * 14 % 90;
            CreateWindow(title, "icon_notepad", 240 + offset, 70 + offset, 420, 300, WindowFlags.Standard, zoomFrom);
            var client = Window.Client;

            var menu = UIBuilder.Rect("Menu Bar", client).TopStrip(0, 18);
            int mx = 2;
            foreach (var label in new[] { "File", "Edit", "Search", "Help" })
            {
                var item = UiButton.Create(menu, label, a => MenuFor(label, a), "menu:" + label);
                item.Flat = true;
                item.ClickSound = "";
                int w = PixelFont.MeasureLine(label, false) + 12;
                ((RectTransform)item.transform).At(mx, 1, w, 16);
                mx += w;
            }

            // An editable file (Night 3's config files) gets a status line: how typing works and that Save applies it.
            bool editable = CanSave;
            var frame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Text Frame");
            _frameBottom = editable ? 15 : 0;
            frame.rectTransform.Stretch(0, 20, 0, _frameBottom);
            _frame = frame.rectTransform;
            if (editable)
            {
                _status = UIBuilder.Text(client, G.Content.Text("notepad.editable.hint", "Text is added at the end. File > Save to apply."), Palette.Shadow);
                _status.rectTransform.BottomStrip(1, 12, 4, 4);
                Window.CloseGuard = ConfirmClose;
            }
            _baseTitle = title;
            _scroll = ScrollArea.Create(frame.rectTransform, "Text Scroll");
            ((RectTransform)_scroll.transform).Stretch(2, 2, 2, 2);
            _view = UIBuilder.Text(_scroll.Content, "", Palette.Text);
            _view.Wrap = true;
            var hit = _scroll.Viewport.GetComponent<Interactable>();
            hit.cursor = CursorShape.IBeam;
            // Phase S: a click on the page while she types shows the rest of the line (never the first line of an exchange).
            hit.PointerDown += a => { if (a != null && a.IsPlayer) RequestSkip(); };

            if (file != null) SetText(file.Content);
            _inputStart = _text.Length;
            // Phase L: a document opens at its beginning (typing and a conversation still follow the end), and its text follows the
            // window's width when it is resized or snapped to a half.
            _scroll.ScrollTo(0f);
            Window.Resized += _ => _resized = true;

            // Phase H: a conversation says when your typing waits (the other session is typing) or goes nowhere.
            _convStatus = UIBuilder.Rect("Conversation Status", client).BottomStrip(2, 15, 3, 19);
            var face = _convStatus.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            _convStatusText = UIBuilder.Text(_convStatus, "", Palette.Text);
            _convStatusText.rectTransform.Stretch(5, 1, 4, 1);
            _convStatusText.VAlign = TextVAlign.Middle;
            _convStatusText.Wrap = true;
            _convStatus.gameObject.SetActive(false);
        }

        RectTransform _convStatus;
        PixelText _convStatusText;
        RectTransform _frame;
        int _frameBottom;
        /// <summary>The last moment the other side was typing, thinking, or waiting for your reply (or you sent one).</summary>
        float _remoteActiveAt;
        /// <summary>
        /// A conversation nobody has answered for this long is not listening: a reply typed ahead is sent (Phase N). Long enough for a
        /// reply that is on its way (the think pause and the cursor reaching its pad after you sent a line).
        /// </summary>
        const float NotListeningAfter = 3f;

        bool NobodyListening => ConversationMode && !PlayerCanType && !EntityTyping && !ThinkingCaret && Time.time - _remoteActiveAt > NotListeningAfter;

        /// <summary>The held reply as the status line shows it: its last line, with a caret.</summary>
        string PendingHeld()
        {
            string h = _held.ToString().TrimEnd('\n');
            int nl = h.LastIndexOf('\n');
            if (nl >= 0) h = h.Substring(nl + 1);
            if (h.Length > 40) h = "..." + h.Substring(h.Length - 40);
            return h + "_";
        }

        /// <summary>Phase N: a reply typed while the other side typed, sent once it stopped: on its own line, like any sent line.</summary>
        void SendHeld(string line)
        {
            if (_text.Length > 0 && _text[_text.Length - 1] != '\n') _text.Append('\n');
            _text.Append(line).Append('\n');
            _inputStart = _text.Length;
            Changed(true);
            Sfx.Play("key_enter", _heldBy);
            _remoteActiveAt = Time.time;
            GameLog.Info(LogChannel.Player, "Jotter: reply sent after " + SessionLabel + " stopped typing (no turn)");
            LineSubmitted?.Invoke(line, _heldBy);
        }

        void UpdateConversationStatus()
        {
            if (_convStatus == null) return;
            if (!ConversationMode || PlayerCanType || EntityTyping || ThinkingCaret)
            {
                _remoteActiveAt = Time.time;
            }
            int enter = _held.ToString().IndexOf('\n');
            if (enter >= 0 && NobodyListening)
            {
                // Phase N (fifth blind playtest, finding 13): the other side stopped without giving you a turn: the reply typed while it
                // typed is sent now, as your line on the page (Phase K kept it grey, "(not sent)"). Whether it is answered is the story's.
                string line = _held.ToString(0, enter).Trim();
                _held.Remove(0, enter + 1);
                if (line.Length > 0) SendHeld(line);
            }
            // Phase K (finding 4): what you type while it is not your turn is echoed here at once, with why it waits.
            // Phase Q4 (CH7): the text and its height are rebuilt only when what they say changed (they used to be formatted and measured every
            // frame while a reply was awaited).
            bool convHeld = ConversationMode && _held.Length > 0;
            bool nobody = NobodyListening;
            bool turn = !convHeld && WaitsForPlayer;
            int hash = 0;
            if (convHeld)
            {
                hash = _held.Length;
                for (int i = 0; i < _held.Length; i++) hash = hash * 31 + _held[i];
            }
            hash = hash * 31 + (SessionLabel != null ? SessionLabel.GetHashCode() : 0);
            // Phase S (Large text tester: "Your turn" in 8 px type): the status strip follows the Reading text size too.
            float statusFactor = Game.DisplaySettings.ReadingFactor;
            hash = hash * 31 + Mathf.RoundToInt(statusFactor * 10f);
            int replySeconds = ReplySecondsRemaining >= 0f ? Mathf.CeilToInt(ReplySecondsRemaining) : -1;
            hash = hash * 31 + replySeconds;
            bool skipHint = !convHeld && ConversationMode && EntityTyping && SkipAllowed;
            int key = (convHeld ? 1 : 0) | (nobody ? 2 : 0) | (turn ? 4 : 0) | (skipHint ? 8 : 0);
            float statusWidth = _convStatus.rect.width;
            if (key != _statusKey || hash != _statusHash || !Mathf.Approximately(statusWidth, _statusWidth))
            {
                string made = null;
                if (convHeld) made = G.Content.Format(nobody ? "notepad.status.held" : "notepad.status.typing", SessionLabel, PendingHeld());
                else if (skipHint) made = G.Content.Format("notepad.status.skip", SessionLabel);
                else if (turn)
                {
                    made = G.Content.Text("notepad.status.turn");
                    if (replySeconds >= 0) made += " Silence answers in " + replySeconds + "s.";
                }
                if (made != null && made.Length > 0) made = char.ToUpperInvariant(made[0]) + made.Substring(1);
                _statusText = made;
                // Phase K: the strip grows to a second line for the longer reasons (it used to be cut at the window's edge).
                _convStatusText.Factor = statusFactor;
                _statusHeight = made != null ? Mathf.Max(15, PixelFont.Measure(made, Mathf.FloorToInt(statusWidth) - 9, false, statusFactor).y + 4) : 0;
                _statusKey = key;
                _statusHash = hash;
                _statusWidth = statusWidth;
            }
            string text = _statusText;
            bool show = text != null;
            int height = _statusHeight;
            if (show) _convStatusText.text = text;
            if (_convStatus.gameObject.activeSelf == show && height == _convStatusHeight) return;
            _convStatusHeight = height;
            _convStatus.gameObject.SetActive(show);
            if (show) _convStatus.BottomStrip(2, height, 3, 19);
            // The strip must not hide the newest line: the text frame gives it room while it shows.
            if (_frame != null)
            {
                _frame.offsetMin = new Vector2(_frame.offsetMin.x, show ? Mathf.Max(_frameBottom, height + 2) : _frameBottom);
                Changed(true);
            }
        }

        int _convStatusHeight;
        int _statusKey = -1, _statusHash, _statusHeight;
        float _statusWidth = -1f;
        string _statusText;

        PixelText _status;
        string _baseTitle;
        bool _dirty;
        MessageBox _savePrompt;

        /// <summary>The player changed an editable file and has not saved it (the title shows a *).</summary>
        public bool HasUnsavedChanges => _dirty;

        void SetDirty(bool dirty)
        {
            if (_dirty == dirty || Window == null) return;
            _dirty = dirty;
            var file = _fileId != null ? G.Files.GetFile(_fileId) : null;
            string name = file != null ? file.Name : G.Content.Text("notepad.untitled", "Untitled");
            Window.SetTitle(dirty ? name + "* - " + G.Content.Text("app.notepad") : _baseTitle);
        }

        /// <summary>
        /// The player closes an edited file: ask first (Yes saves and closes, No closes without saving, Cancel keeps it
        /// open), so a fixed config file is never thrown away silently.
        /// </summary>
        bool ConfirmClose(CursorAgent by)
        {
            if (!_dirty || !CanSave) return true;
            if (_savePrompt != null && _savePrompt.IsOpen)
            {
                _savePrompt.Window.Focus(by);
                return false;
            }
            var file = G.Files.GetFile(_fileId);
            string name = file != null ? file.Name : "Untitled";
            _savePrompt = Dialogs.Message(G, G.Content.Text("app.notepad"), G.Content.Format("notepad.save.prompt", name),
                "icon_question", new[] { "Yes", "No", "Cancel" }, (result, a) =>
                {
                    _savePrompt = null;
                    if (!IsOpen) return;
                    if (result == "Yes")
                    {
                        if (Save(a ?? by)) Window.Close(a ?? by);
                    }
                    else if (result == "No") Window.Close(a ?? by);
                });
            GameLog.Info(LogChannel.Player, "Jotter asks to save " + name + " before closing");
            return false;
        }

        protected override void OnClosed(CursorAgent by)
        {
            if (_savePrompt != null && _savePrompt.IsOpen) _savePrompt.Window.Close(by);
            _savePrompt = null;
        }

        void MenuFor(string label, CursorAgent a)
        {
            var items = new System.Collections.Generic.List<MenuItem>();
            if (label == "File")
            {
                items.Add(MenuItem.Of("New", x => { if (!EntityTyping) SetText(""); }));
                items.Add(MenuItem.Of("Save", x => Save(x), enabled: CanSave));
                items.Add(MenuItem.Sep());
                items.Add(MenuItem.Of("Exit", x => Window.RequestClose(x)));
            }
            else if (label == "Edit")
            {
                items.Add(MenuItem.Of("Undo", null, enabled: false));
                items.Add(MenuItem.Of("Select All", null, enabled: false));
            }
            else
            {
                items.Add(MenuItem.Of(label == "Help" ? "About " + G.Content.Text("app.notepad") : "Find...", null, enabled: false));
            }
            var rt = Window.Client.Find("Menu Bar/Button " + label) as RectTransform;
            Vector2 pos = rt != null ? new Vector2(rt.WorldRect().xMin, rt.WorldRect().yMin) : a.Position;
            PopupMenu.Show(G.Layers.Popups, pos, items, 120);
        }

        /// <summary>Only files tagged "editable" can be saved (Night 3's config files); everything else stays read-only.</summary>
        public bool CanSave
        {
            get
            {
                var f = _fileId != null ? G.Files.GetFile(_fileId) : null;
                return f != null && !f.Shredded && f.HasTag("editable");
            }
        }

        /// <summary>
        /// File, Save: the text goes back into the virtual file system (never to a real file) and the OS says so.
        /// Also used by scripted saves (a cursor saving a file it edited). Returns false if the file cannot be saved.
        /// </summary>
        public bool Save(CursorAgent by)
        {
            if (!IsOpen || !CanSave || (EntityTyping && (by == null || by.IsPlayer))) return false;
            var file = G.Files.GetFile(_fileId);
            string text = _text.ToString();
            G.Files.SetContent(_fileId, text);
            SetDirty(false);
            bool remote = by != null && by.IsEntity;
            // Phase H: the notice names who saved it and what the saved file now decides (session.cfg: Log Off at 7:00).
            string msg = !remote ? G.Content.Format("file.saved", file.Name)
                : G.Content.HasText("file.saved.by") ? G.Content.Format("file.saved.by", file.Name, SystemNotices.SessionOf(G, by))
                : G.Content.Format("file.saved.remote", file.Name);
            string note = G.Apps.SavedNote?.Invoke(_fileId, text);
            if (!string.IsNullOrEmpty(note)) msg += "\n" + note;
            G.Notifications.Show(G.Content.Text("os.name"), msg, "icon_notepad", null, "ui_select", false, null, remote ? by.Actor : Core.Game.NoticeKind.Plain);
            G.Apps.RaiseFileSaved(_fileId, text, by);
            return true;
        }

        public void SetText(string text)
        {
            _text.Length = 0;
            _text.Append(text ?? "");
            _inputStart = _text.Length;
            Changed(true);
        }

        public void Append(string text)
        {
            _text.Append(text);
            _inputStart = _text.Length;
            Changed(true);
        }

        /// <summary>The window changed size: wrap the text to the new width (a conversation stays at its newest line).</summary>
        void Reflow()
        {
            Canvas.ForceUpdateCanvases();
            Changed(ConversationMode);
        }

        void Changed(bool scrollToEnd)
        {
            if (_view == null) return;
            _view.text = _text.ToString();
            int width = Mathf.Max(60, Mathf.FloorToInt(_scroll.Viewport.rect.width) - 6);
            var size = PixelFont.Measure(_view.text + "W", width, false, _view.Factor);
            _view.rectTransform.At(3, 3, width, size.y + 4);
            _scroll.ContentHeight = size.y + 10;
            if (scrollToEnd) _scroll.ScrollToBottom();
        }

        /// <summary>Entity types text character by character. Yield on the returned enumerator.</summary>
        public IEnumerator TypeAsEntity(string text, float charsPerSecond, CursorAgent entity) => TypeAsEntity(text, charsPerSecond, entity, 0f);

        /// <summary>
        /// Like <see cref="TypeAsEntity(string,float,CursorAgent)"/>; with <paramref name="typoRate"/> above 0 each
        /// word has that chance of one wrong letter that is typed, noticed and backspaced (presentation only: the
        /// text ends up exactly as given).
        /// </summary>
        public IEnumerator TypeAsEntity(string text, float charsPerSecond, CursorAgent entity, float typoRate)
        {
            // A routine stopped in the middle of an interjection never cleared the flag; a new line always can cut in again.
            _interjecting = false;
            _skip = false;
            EntityTyping = true;
            bool previous = PlayerCanType;
            PlayerCanType = false;
            int typoAt = PickTypo(text, 0, typoRate);
            for (int i = 0; i < text.Length; i++)
            {
                if (_interjections.Count > 0 && !_interjecting)
                {
                    // Phase P (T1): a line that cannot wait cuts in: the line being typed breaks off and goes on below it.
                    var cut = Interjections(entity);
                    while (cut.MoveNext()) yield return cut.Current;
                    while (i < text.Length && text[i] == ' ') i++;
                    if (i >= text.Length) break;
                }
                char c = text[i];
                if (!IsOpen) break;
                if (_skip && SkipAllowed)
                {
                    // Phase S: the player asked to read the rest of this line now (a click, or Enter). The text ends up exactly as given.
                    for (int j = i; j < text.Length; j++)
                    {
                        if (text[j] == '\b') { if (_text.Length > 0) _text.Length -= 1; }
                        else _text.Append(text[j]);
                    }
                    _skip = false;
                    Changed(true);
                    Sfx.Play("key_enter", entity);
                    break;
                }
                if (c == '\b')
                {
                    // A scripted Backspace (code only, never content): the last character goes.
                    if (_text.Length > 0) _text.Length -= 1;
                    Changed(true);
                    Sfx.Play("key_tap", entity);
                    yield return Waits.Seconds(Mathf.Max(0.2f, 1.5f / Mathf.Max(1f, charsPerSecond)));
                    continue;
                }
                if (i > 0 && text[i - 1] == ' ') typoAt = PickTypo(text, i, typoRate);
                if (i == typoAt)
                {
                    // A wrong neighbouring letter, a beat, then it is taken back.
                    _text.Append(WrongLetter(c));
                    Changed(true);
                    Sfx.Play("key_tap", entity);
                    yield return Waits.Seconds(Mathf.Max(0.25f, 2.2f / Mathf.Max(1f, charsPerSecond)));
                    if (!IsOpen) break;
                    _text.Length -= 1;
                    Changed(true);
                    Sfx.Play("key_tap", entity);
                    yield return Waits.Seconds(Mathf.Max(0.15f, 1.2f / Mathf.Max(1f, charsPerSecond)));
                    if (!IsOpen) break;
                }
                _text.Append(c);
                Changed(true);
                Sfx.Play(c == ' ' ? "key_space" : (c == '\n' ? "key_enter" : "key_tap"), entity);
                float delay = 1f / Mathf.Max(1f, charsPerSecond);
                if (c == ' ') delay *= 1.6f;
                if (c == '\n') delay *= 3f;
                delay *= UnityEngine.Random.Range(0.7f, 1.4f);
                yield return Waits.Seconds(delay);
            }
            _inputStart = _text.Length;
            PlayerCanType = previous;
            EntityTyping = false;
        }

        readonly System.Collections.Generic.Queue<(string line, float cps)> _interjections = new System.Collections.Generic.Queue<(string, float)>();
        bool _interjecting;

        /// <summary>
        /// Phase P (T1): types <paramref name="line"/> as soon as possible: a line being typed breaks off for it (and goes on below it). For
        /// lines that belong to a moment (her hold lines in the finale) while a long conversation line is still being typed.
        /// </summary>
        public void Interject(string line, float charsPerSecond) => _interjections.Enqueue((line, charsPerSecond));

        /// <summary>Lines waiting to cut in.</summary>
        public int PendingInterjections => _interjections.Count;

        /// <summary>Lines that could not cut in in time are dropped (they belonged to a moment that has passed).</summary>
        public void CancelInterjections() => _interjections.Clear();

        IEnumerator Interjections(CursorAgent entity)
        {
            _interjecting = true;
            while (_interjections.Count > 0 && IsOpen)
            {
                var (line, cps) = _interjections.Dequeue();
                if (_text.Length > 0 && _text[_text.Length - 1] != '\n') _text.Append('\n');
                foreach (char c in line)
                {
                    if (!IsOpen) break;
                    _text.Append(c);
                    Changed(true);
                    Sfx.Play(c == ' ' ? "key_space" : "key_tap", entity);
                    yield return Waits.Seconds((c == ' ' ? 1.6f : 1f) / Mathf.Max(1f, cps));
                }
                _text.Append('\n');
                Changed(true);
                yield return Waits.Seconds(0.3f);
            }
            _interjecting = false;
        }

        /// <summary>Index of the letter in the word starting at <paramref name="start"/> that gets a typo, or -1.</summary>
        static int PickTypo(string text, int start, float rate)
        {
            if (rate <= 0f || UnityEngine.Random.value >= rate) return -1;
            int end = text.IndexOf(' ', start);
            if (end < 0) end = text.Length;
            var letters = new System.Collections.Generic.List<int>();
            for (int i = start; i < end; i++) if (char.IsLetter(text[i])) letters.Add(i);
            return letters.Count == 0 ? -1 : letters[UnityEngine.Random.Range(0, letters.Count)];
        }

        const string KeyRows = "qwertyuiopasdfghjklzxcvbnm";

        /// <summary>A letter next to the intended one on the keyboard (same case).</summary>
        static char WrongLetter(char c)
        {
            int k = KeyRows.IndexOf(char.ToLowerInvariant(c));
            char w = k < 0 ? 'e' : KeyRows[k == KeyRows.Length - 1 ? k - 1 : k + 1];
            return char.IsUpper(c) ? char.ToUpperInvariant(w) : w;
        }

        public void OnTyped(string text, CursorAgent by)
        {
            if (!PlayerCanType || EntityTyping)
            {
                // Mid-conversation keystrokes are not lost: they are typed ahead and play out on your line when it is your turn, one
                // line (up to its Enter) per turn. Phase K: with nobody reading they are shown too, and go on the page as not sent.
                if (ConversationMode)
                {
                    HoldKeys(text, by);
                    Sfx.Play("key_tap", by);
                }
                return;
            }
            bool edited = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                LastPlayerKeyTime = Time.time;
                if (c == ControlChars.Save)
                {
                    // Ctrl+S: File > Save for an editable file; never typed as text.
                    if (!ConversationMode && CanSave) Save(by);
                    continue;
                }
                if (!ConversationMode) edited = true;
                // Phase I: typing into a page that ends without a line break starts a new line ("ALLOW_LOGOFF=0ALLOW_LOGOFF=1"
                // was one glued line the blind tester could not take apart).
                if (!ConversationMode && CanSave && c != '\b' && c != '\n' && _text.Length > 0 && _text.Length <= _inputStart && _text[_text.Length - 1] != '\n')
                {
                    _text.Append('\n');
                    _inputStart = _text.Length;
                }
                if (c == '\b')
                {
                    if (_text.Length > (ConversationMode ? _inputStart : 0)) _text.Length -= 1;
                    // Review J1: after a Backspace a page is edited in place (ALLOW_LOGOFF=0, Backspace, 1 stays one line).
                    if (!ConversationMode) _inputStart = 0;
                }
                else if (c == '\n')
                {
                    if (ConversationMode)
                    {
                        string line = _text.ToString(_inputStart, _text.Length - _inputStart).Trim();
                        _text.Append('\n');
                        _inputStart = _text.Length;
                        Changed(true);
                        Sfx.Play("key_enter", by);
                        if (line.Length > 0)
                        {
                            // A sent line ends your turn at once: later keys (even this frame's) are typed ahead.
                            PlayerCanType = false;
                            _remoteActiveAt = Time.time;   // a reply is on its way: the idle clock starts now
                            LineSubmitted?.Invoke(line, by);
                            if (i + 1 < text.Length) HoldKeys(text.Substring(i + 1), by);
                            break;
                        }
                        continue;
                    }
                    _text.Append('\n');
                }
                else
                {
                    if (ConversationMode && _text.Length - _inputStart > 60) continue;
                    _text.Append(c);
                }
                Sfx.Play(c == ' ' ? "key_space" : (c == '\n' ? "key_enter" : "key_tap"), by);
            }
            if (edited && CanSave && by != null && by.IsPlayer) SetDirty(true);
            Changed(true);
        }

        readonly System.Text.StringBuilder _held = new System.Text.StringBuilder();
        CursorAgent _heldBy;

        /// <summary>Phase S: the line being typed may be shown at once (false for the first line of an exchange, which keeps its pace).</summary>
        public bool SkipAllowed;
        bool _skip;

        /// <summary>Phase S: show the rest of the line now (a click on the page, or Enter with nothing typed ahead).</summary>
        public void RequestSkip()
        {
            if (ConversationMode && EntityTyping && SkipAllowed) _skip = true;
        }

        void HoldKeys(string text, CursorAgent by)
        {
            _heldBy = by;
            foreach (char c in text)
            {
                LastPlayerKeyTime = Time.time;
                if (c == ControlChars.Save) continue;
                // Enter with nothing typed ahead is not a reply: it shows the rest of her line.
                if (c == '\n' && _held.Length == 0 && SkipAllowed && EntityTyping) { _skip = true; continue; }
                if (c == '\b') { if (_held.Length > 0 && _held[_held.Length - 1] != '\n') _held.Length -= 1; }
                else if (_held.Length < 120) _held.Append(c);
            }
        }

        void ReleaseHeldKeys()
        {
            if (_held.Length == 0 || !PlayerCanType || EntityTyping || !ConversationMode) return;
            // One line per turn: the typed-ahead text up to and including its first Enter. Anything after
            // it waits for the next turn, so two replies never merge or overwrite each other.
            string all = _held.ToString();
            int enter = all.IndexOf('\n');
            string now = enter >= 0 ? all.Substring(0, enter + 1) : all;
            _held.Remove(0, now.Length);
            OnTyped(now, _heldBy);
        }

        /// <summary>The player's partially typed line in conversation mode.</summary>
        public string PendingInput => _text.ToString(_inputStart, _text.Length - _inputStart);

        public void OnKey(GameKey key, CursorAgent by) { }

        /// <summary>The window was resized since the last frame (the grip fires on every pointer move).</summary>
        bool _resized;

        public override void Tick(float dt)
        {
            if (_resized)
            {
                _resized = false;
                Reflow();
            }
            _caretBlink += dt;
            // A conversation is shown at double size: it has to read on a phone screen in a clip. Documents follow
            // the Reading text option (Large on a Steam Deck).
            float scale = ConversationMode ? 2f : Game.DisplaySettings.ReadingFactor;
            if (_view != null && _view.Factor != scale)
            {
                _view.Factor = scale;
                Changed(ConversationMode);
            }
            // The status first: it sees the turn that ReleaseHeldKeys may end on this frame (a typed-ahead line sent).
            UpdateConversationStatus();
            ReleaseHeldKeys();
            bool focused = Window.IsActive && PlayerCanType && !EntityTyping;
            bool typingCaret = EntityTyping;
            // Thinking (before a keyword reply): the caret blinks where it will type, with no key sounds.
            _view.SetCaret(focused || typingCaret || ThinkingCaret, _text.Length, typingCaret || (_caretBlink % 1.06f) < 0.53f);
        }
    }
}
