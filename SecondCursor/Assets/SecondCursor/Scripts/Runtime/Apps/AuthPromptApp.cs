using System.Text;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// The code prompt for a locked folder that has an authorization code (expansion spec 5.5): a small window
    /// with one sunken field (at most 8 printable characters), OK and Cancel; Enter submits. A wrong code
    /// shakes the window and is counted; after a few failures the expected format is shown. The right code
    /// unlocks the folder and File Manager goes into it. Only the player gets this prompt: a cursor opens
    /// locked folders without a code.
    /// </summary>
    public sealed class AuthPromptApp : App, IKeyboardTarget, ITextEntryTarget
    {
        public const string Id = "authprompt";
        const int MaxChars = 8;
        const int W = 320, H = 152;

        readonly string _folderId;
        readonly StringBuilder _input = new StringBuilder();
        PixelText _body;
        PixelText _field;
        PixelText _format;
        float _caretBlink;

        public override string AppId => Id;
        public string FolderId => _folderId;
        public string Input => _input.ToString();

        /// <summary>Steam Deck keyboard: numeric, for as long as the prompt is open.</summary>
        public bool WantsTextEntry => IsOpen;

        public Game.TextEntryMode EntryMode => Game.TextEntryMode.Numeric;

        public Rect EntryRectVirtual => Window != null ? Window.WorldRect : default;

        public AuthPromptApp(string folderId)
        {
            _folderId = folderId;
        }

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            var c = G.Content;
            var at = WindowManager.Centered(W, H);
            CreateWindow(c.Text("auth.title"), "icon_lock", at.x, at.y - 30, W, H, WindowFlags.CanClose | WindowFlags.AlwaysOnTop, zoomFrom);
            var client = Window.Client;

            var icon = UIBuilder.Icon(client, "icon_lock", 2);
            icon.rectTransform.anchoredPosition = new Vector2(10f, -10f);
            _body = UIBuilder.Text(client, c.Text("auth.body"), Palette.Text);
            _body.Wrap = true;
            _body.rectTransform.At(52, 10, W - 66, 26);

            var frame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Field");
            frame.rectTransform.At(52, 42, 120, 20);
            UIBuilder.Hit(frame.gameObject, "auth:field", CursorShape.IBeam);
            _field = UIBuilder.Text(frame.rectTransform, "", Palette.Text);
            _field.rectTransform.Stretch(4, 0, 4, 0);
            _field.VAlign = TextVAlign.Middle;

            _format = UIBuilder.Text(client, c.Text("auth.format"), Palette.Shadow);
            _format.rectTransform.At(52, 68, W - 66, 12);
            _format.enabled = G.Flags.Get(Flags.CounterAuthFail) >= G.Difficulty.CodeFormatAfterFailures;

            var ok = UiButton.Create(client, "OK", a => Submit(a), "button:OK");
            ((RectTransform)ok.transform).At(W / 2 - 80, H - 18 - 9 - 34, 72, 22);
            ok.IsDefault = true;
            var cancel = UiButton.Create(client, "Cancel", a => Window.Close(a), "button:Cancel");
            ((RectTransform)cancel.transform).At(W / 2 + 2, H - 18 - 9 - 34, 72, 22);
            Sfx.Play("sys_warning");
            GameLog.Info(LogChannel.OS, "Code prompt opened for " + _folderId);
        }

        /// <summary>OK or Enter: the folder opens, or the attempt is logged and the window shakes.</summary>
        public void Submit(CursorAgent by)
        {
            if (!IsOpen) return;
            var c = G.Content;
            string typed = _input.ToString();
            var folder = G.Files.GetFolder(_folderId);
            // Someone unlocked it while the prompt was open (kept Gary at 6:48): the right code still works.
            bool alreadyOpen = folder != null && !folder.Locked && folder.HasCode && Core.FileSystem.VirtualFileSystem.CodeMatches(folder.Code, typed);
            if (alreadyOpen || G.Files.TryUnlock(_folderId, typed))
            {
                GameLog.Info(LogChannel.Player, "Code accepted for " + _folderId);
                if (_folderId == ContentIds.FolderRestricted && by != null && by.IsPlayer) G.Flags.Set(MemoryFlags.N3RestrictedOpen);
                G.Notifications.Show(c.Text("os.name"), c.Text("auth.ok"), "icon_lock", null, "ui_select");
                Window.Close(by);
                G.Apps.OpenFolder(_folderId, by);
                return;
            }
            int fails = G.Flags.Increment(Flags.CounterAuthFail);
            GameLog.Info(LogChannel.Player, "Wrong code for " + _folderId + " (" + fails + ")");
            _body.text = c.Text("auth.failed");
            _input.Length = 0;
            _field.text = "";
            if (fails >= G.Difficulty.CodeFormatAfterFailures) _format.enabled = true;
            Window.Shake(0.35f, 4f);
            Sfx.Play("sys_error");
        }

        public void OnTyped(string text, CursorAgent by)
        {
            foreach (char ch in text)
            {
                if (ch == '\n')
                {
                    Submit(by);
                    return;
                }
                if (ch == '\b')
                {
                    if (_input.Length > 0) _input.Length -= 1;
                }
                else if (ch >= 32 && ch <= 126 && _input.Length < MaxChars)
                {
                    _input.Append(ch);
                }
                else continue;
                Sfx.Play(ch == ' ' ? "key_space" : "key_tap", by);
            }
            _field.text = _input.ToString();
            _caretBlink = 0f;
        }

        public void OnKey(GameKey key, CursorAgent by)
        {
            if (key != GameKey.Escape) return;
            // Esc closes the prompt, and only the prompt: the pause menu must not open on the same key.
            AppManager.MarkEscapeHandled();
            Window.Close(by);
        }

        public override void Tick(float dt)
        {
            _caretBlink += dt;
            bool focused = Window.IsActive;
            _field.SetCaret(focused, _input.Length, (_caretBlink % 1.06f) < 0.53f);
        }
    }
}
