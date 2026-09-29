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
        /// <summary>The speaker is "thinking" before a reply: the caret blinks although nobody types.</summary>
        public bool ThinkingCaret;
        public event Action<string, CursorAgent> LineSubmitted;
        public float LastPlayerKeyTime { get; private set; } = -100f;

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

            if (file != null) SetText(file.Content);
            _inputStart = _text.Length;

            // Phase H: a conversation says when your typing waits (the other session is typing) or goes nowhere.
            _convStatus = UIBuilder.Rect("Conversation Status", client).BottomStrip(2, 15, 3, 19);
            var face = _convStatus.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            _convStatusText = UIBuilder.Text(_convStatus, "", Palette.Text);
            _convStatusText.rectTransform.Stretch(5, 1, 4, 1);
            _convStatusText.VAlign = TextVAlign.Middle;
            _convStatus.gameObject.SetActive(false);
        }

        RectTransform _convStatus;
        PixelText _convStatusText;
        RectTransform _frame;
        int _frameBottom;
        /// <summary>The last moment the other side was typing, thinking, or waiting for your reply (or you sent one).</summary>
        float _remoteActiveAt;
        float _notReadingUntil = -1f;
        /// <summary>
        /// Typing into a conversation nobody has answered for this long goes nowhere, and says so. Long enough for a reply
        /// that is on its way (the think pause and the cursor reaching its pad after you sent a line).
        /// </summary>
        const float NotListeningAfter = 3f, NotReadingShow = 3f;

        bool NobodyListening => ConversationMode && !PlayerCanType && !EntityTyping && !ThinkingCaret && Time.time - _remoteActiveAt > NotListeningAfter;

        void UpdateConversationStatus()
        {
            if (_convStatus == null) return;
            if (!ConversationMode || PlayerCanType || EntityTyping || ThinkingCaret)
            {
                _remoteActiveAt = Time.time;
                _notReadingUntil = -1f;   // someone is there again: the "not reading" line goes at once
            }
            if (_held.Length > 0 && NobodyListening)
            {
                // The other side stopped without giving you a turn: a reply typed ahead would otherwise be sent much later,
                // as an answer to something else. It is dropped, and the Jotter says nobody is reading.
                _held.Length = 0;
                _notReadingUntil = Time.time + NotReadingShow;
                GameLog.Info(LogChannel.Player, "Jotter: typed-ahead reply dropped (the remote session is not reading)");
            }
            string text = null;
            if (ConversationMode && _held.Length > 0 && (EntityTyping || !PlayerCanType)) text = G.Content.Text("notepad.status.typing", "Remote session is typing. Your reply is sent when it stops.");
            else if (ConversationMode && Time.time < _notReadingUntil) text = G.Content.Text("notepad.status.away", "Remote session is not reading.");
            bool show = text != null;
            if (show) _convStatusText.text = text;
            if (_convStatus.gameObject.activeSelf == show) return;
            _convStatus.gameObject.SetActive(show);
            // The strip must not hide the newest line: the text frame gives it room while it shows.
            if (_frame != null)
            {
                _frame.offsetMin = new Vector2(_frame.offsetMin.x, show ? Mathf.Max(_frameBottom, 17) : _frameBottom);
                Changed(true);
            }
        }

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
            G.Notifications.Show(G.Content.Text("os.name"), msg, "icon_notepad", null, "ui_select");
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

        void Changed(bool scrollToEnd)
        {
            if (_view == null) return;
            _view.text = _text.ToString();
            int width = Mathf.Max(60, Mathf.FloorToInt(_scroll.Viewport.rect.width) - 6);
            var size = PixelFont.Measure(_view.text + "W", width, false, _view.Scale);
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
            EntityTyping = true;
            bool previous = PlayerCanType;
            PlayerCanType = false;
            int typoAt = PickTypo(text, 0, typoRate);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!IsOpen) break;
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
                if (ConversationMode && NobodyListening)
                {
                    // Nobody on the other side (Phase H): the keys go nowhere, and the Jotter says so instead of eating them.
                    if (Time.time >= _notReadingUntil) GameLog.Info(LogChannel.Player, "Jotter: typed while the remote session is not reading");
                    _notReadingUntil = Time.time + NotReadingShow;
                    Sfx.Play("key_tap", by);
                    return;
                }
                // Mid-conversation keystrokes are not lost: they are typed ahead and play out on your line
                // when it is your turn, one line (up to its Enter) per turn.
                if (ConversationMode) HoldKeys(text, by);
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
                if (c == '\b')
                {
                    if (_text.Length > (ConversationMode ? _inputStart : 0)) _text.Length -= 1;
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

        void HoldKeys(string text, CursorAgent by)
        {
            _heldBy = by;
            foreach (char c in text)
            {
                LastPlayerKeyTime = Time.time;
                if (c == ControlChars.Save) continue;
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

        public override void Tick(float dt)
        {
            _caretBlink += dt;
            // A conversation is shown at double size: it has to read on a phone screen in a clip. Documents follow
            // the Reading text option (Large on a Steam Deck).
            int scale = ConversationMode ? 2 : Game.DisplaySettings.ReadingScale;
            if (_view != null && _view.Scale != scale)
            {
                _view.Scale = scale;
                Changed(true);
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
