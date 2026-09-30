using System.Text;
using SecondCursor.Core.Content;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Hex/ASCII viewer for .dat and .tmp files. Readable fragments surface out of the byte noise.
    /// Damaged/story files occasionally shimmer: a line of bytes briefly rewrites itself. Phase L: those files also have a
    /// "Recovered text" button that shows their readable reconstruction (no glitch characters) in the window's full width; the
    /// hex view stays the default.
    /// </summary>
    public sealed class DataViewerApp : App
    {
        const int BytesPerRow = 16;
        readonly string _fileId;
        ScrollArea _scroll;
        PixelText _dump;
        PixelText _header;
        ScrollArea _textScroll;
        PixelText _recovered;
        UiButton _toggle;
        string _clean;
        string[] _lines;
        float _glitchTimer;
        int _glitchLine = -1;
        float _glitchHold;
        bool _showRecovered;

        public override string AppId => AppIds.DataViewer;

        public DataViewerApp(string fileId)
        {
            _fileId = fileId;
        }

        /// <summary>Damaged, protected and story files shimmer (and, when their text is glitchy, have the recovered-text view).</summary>
        bool Unstable
        {
            get
            {
                var file = G.Files.GetFile(_fileId);
                return file != null && (file.Corrupted || file.Protected || file.HasTag("story"));
            }
        }

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            var file = G.Files.GetFile(_fileId);
            string title = (file != null ? file.Name : "?") + " - " + G.Content.Text("app.dataviewer");
            int offset = G.Apps.OpenApps.Count * 12 % 80;
            CreateWindow(title, FileIcons.SpriteFor(file), 170 + offset, 60 + offset, 500, 320, WindowFlags.Standard, zoomFrom);

            _header = UIBuilder.Text(Window.Client, "Offset    00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F   ASCII", Palette.Shadow);
            _header.MonospaceAdvance = 6;
            _header.rectTransform.TopStrip(2, 12, 6, 2);

            var frame = UIBuilder.Bevel(Window.Client, BevelStyle.Sunken, "Dump Frame");
            _scroll = ScrollArea.Create(frame.rectTransform, "Dump Scroll");
            ((RectTransform)_scroll.transform).Stretch(2, 2, 2, 2);
            _dump = UIBuilder.Text(_scroll.Content, "", Palette.Text);
            _dump.MonospaceAdvance = 6;

            if (Unstable && RecoveredText.IsGlitched(file.Content))
            {
                // A button row above the text: the toggle sits at the right, the hex header at the left.
                frame.rectTransform.Stretch(0, 20, 0, 0);
                _header.rectTransform.TopStrip(4, 12, 6, 110);
                _toggle = UiButton.Create(Window.Client, "Recovered text", a => SetRecovered(!_showRecovered), "button:Recovered");
                ((RectTransform)_toggle.transform).TopRight(2, 1, 104, 17);
                _textScroll = ScrollArea.Create(frame.rectTransform, "Recovered Scroll");
                ((RectTransform)_textScroll.transform).Stretch(2, 2, 2, 2);
                _textScroll.gameObject.SetActive(false);
                _recovered = UIBuilder.Text(_textScroll.Content, "", Palette.Text);
                _recovered.Wrap = true;
                Window.Resized += _ => { if (_showRecovered) LayoutRecovered(); };
            }
            else
            {
                frame.rectTransform.Stretch(0, 16, 0, 0);
            }
            Build(file != null ? file.Content : "");
        }

        void SetRecovered(bool on)
        {
            _showRecovered = on;
            _scroll.gameObject.SetActive(!on);
            _textScroll.gameObject.SetActive(on);
            _header.gameObject.SetActive(!on);
            _toggle.Label.text = on ? "Hex view" : "Recovered text";
            if (on)
            {
                var file = G.Files.GetFile(_fileId);
                _recovered.text = RecoveredText.From(file != null ? file.Content : "");
                LayoutRecovered();
                _textScroll.ScrollTo(0f);
            }
            else _dump.text = _clean;
        }

        /// <summary>The recovered text wraps to the window's width (and the Reading text size).</summary>
        void LayoutRecovered()
        {
            int scale = Game.DisplaySettings.ReadingScale;
            _recovered.Scale = scale;
            Canvas.ForceUpdateCanvases();
            int width = Mathf.Max(80, Mathf.FloorToInt(_textScroll.Viewport.rect.width) - 12);
            var size = PixelFont.Measure(_recovered.text, width, false, scale);
            _recovered.rectTransform.At(6, 6, width, size.y + 4);
            _textScroll.ContentHeight = size.y + 14;
        }

        void Build(string content)
        {
            var bytes = Encoding.ASCII.GetBytes(content ?? "");
            int rows = Mathf.Max(1, (bytes.Length + BytesPerRow - 1) / BytesPerRow);
            _lines = new string[rows];
            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                _lines[r] = RowString(bytes, r * BytesPerRow);
                sb.Append(_lines[r]);
                if (r < rows - 1) sb.Append('\n');
            }
            _clean = sb.ToString();
            _dump.text = _clean;
            _dump.rectTransform.At(4, 3, 460, rows * PixelFont.LineHeight + 4);
            _scroll.ContentHeight = rows * PixelFont.LineHeight + 8;
        }

        static string RowString(byte[] bytes, int start)
        {
            var sb = new StringBuilder(80);
            sb.Append(start.ToString("X8")).Append("  ");
            for (int i = 0; i < BytesPerRow; i++)
            {
                int idx = start + i;
                sb.Append(idx < bytes.Length ? bytes[idx].ToString("X2") : "  ").Append(' ');
            }
            sb.Append("  ");
            for (int i = 0; i < BytesPerRow; i++)
            {
                int idx = start + i;
                if (idx >= bytes.Length) break;
                byte b = bytes[idx];
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }
            return sb.ToString();
        }

        public override void Tick(float dt)
        {
            if (_showRecovered && _recovered.Scale != Game.DisplaySettings.ReadingScale) LayoutRecovered();
            if (_showRecovered || !Unstable || _lines == null || _lines.Length == 0) return;

            if (_glitchLine >= 0)
            {
                _glitchHold -= dt;
                if (_glitchHold <= 0f)
                {
                    _glitchLine = -1;
                    _dump.text = _clean;
                }
                return;
            }
            _glitchTimer -= dt;
            if (_glitchTimer > 0f) return;
            _glitchTimer = Random.Range(0.8f, 2.6f);
            _glitchLine = Random.Range(0, _lines.Length);
            _glitchHold = Random.Range(0.05f, 0.14f);
            var noisy = new StringBuilder(_clean);
            int lineStart = 0;
            for (int i = 0; i < _glitchLine; i++) lineStart += _lines[i].Length + 1;
            for (int i = 10; i < _lines[_glitchLine].Length; i++)
            {
                char c = noisy[lineStart + i];
                if (c == ' ') continue;
                if (Random.value < 0.5f) noisy[lineStart + i] = "0123456789ABCDEF#%&?"[Random.Range(0, 20)];
            }
            _dump.text = noisy.ToString();
        }
    }
}
