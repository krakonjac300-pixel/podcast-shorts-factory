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
    public sealed class NotepadApp : App, IKeyboardTarget
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

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
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
                items.Add(MenuItem.Of("Save", null, enabled: false));
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
                items.Add(MenuItem.Of(label == "Help" ? "About Notepad" : "Find...", null, enabled: false));
            }
            var rt = Window.Client.Find("Menu Bar/Button " + label) as RectTransform;
            Vector2 pos = rt != null ? new Vector2(rt.WorldRect().xMin, rt.WorldRect().yMin) : a.Position;
            PopupMenu.Show(G.Layers.Popups, pos, items, 120);
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
            var size = PixelFont.Measure(_view.text + "W", width, false, 1);
            _view.rectTransform.At(3, 3, width, size.y + 4);
            _scroll.ContentHeight = size.y + 10;
            if (scrollToEnd) _scroll.ScrollToBottom();
        }

        /// <summary>Entity types text character by character. Yield on the returned enumerator.</summary>
        public IEnumerator TypeAsEntity(string text, float charsPerSecond, CursorAgent entity)
        {
            EntityTyping = true;
            bool previous = PlayerCanType;
            PlayerCanType = false;
            foreach (char c in text)
            {
                if (!IsOpen) break;
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

        public void OnTyped(string text, CursorAgent by)
        {
            if (!PlayerCanType || EntityTyping) return;
            foreach (char c in text)
            {
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
                        if (line.Length > 0) LineSubmitted?.Invoke(line, by);
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

        /// <summary>The player's partially typed line in conversation mode.</summary>
        public string PendingInput => _text.ToString(_inputStart, _text.Length - _inputStart);

        public void OnKey(GameKey key, CursorAgent by) { }

        public override void Tick(float dt)
        {
            _caretBlink += dt;
            bool focused = Window.IsActive && PlayerCanType && !EntityTyping;
            bool typingCaret = EntityTyping;
            _view.SetCaret(focused || typingCaret, _text.Length, typingCaret || (_caretBlink % 1.06f) < 0.53f);
        }
    }
}
