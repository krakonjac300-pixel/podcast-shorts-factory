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

            var frame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Text Frame");
            frame.rectTransform.Stretch(0, 20, 0, 0);
            _scroll = ScrollArea.Create(frame.rectTransform, "Text Scroll");
            ((RectTransform)_scroll.transform).Stretch(2, 2, 2, 2);
            _view = UIBuilder.Text(_scroll.Content, "", Palette.Text);
            _view.Wrap = true;
            var hit = _scroll.Viewport.GetComponent<Interactable>();
            hit.cursor = CursorShape.IBeam;

            if (file != null) SetText(file.Content);
            _inputStart = _text.Length;
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
            bool remote = by != null && by.IsEntity;
            G.Notifications.Show(G.Content.Text("os.name"), G.Content.Format(remote ? "file.saved.remote" : "file.saved", file.Name), "icon_notepad", null, "ui_select");
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
                // Mid-conversation keystrokes are not lost: they are typed ahead and play out on your line
                // when it is your turn, one line (up to its Enter) per turn.
                if (ConversationMode) HoldKeys(text, by);
                return;
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                LastPlayerKeyTime = Time.time;
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
            ReleaseHeldKeys();
            bool focused = Window.IsActive && PlayerCanType && !EntityTyping;
            bool typingCaret = EntityTyping;
            _view.SetCaret(focused || typingCaret, _text.Length, typingCaret || (_caretBlink % 1.06f) < 0.53f);
        }
    }
}
