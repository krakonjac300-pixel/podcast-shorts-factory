using System.Collections;
using SecondCursor.Core;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Story
{
    /// <summary>
    /// Fiction disclaimer -> title card -> BIOS POST -> NEXUS OS splash -> log-on dialog. Built on the
    /// fullscreen layer; the player's cursor works normally (the log-on button is a real button).
    /// </summary>
    public sealed class BootSequence
    {
        readonly GameServices _g;
        RectTransform _panel;
        bool _clicked;

        public BootSequence(GameServices g)
        {
            _g = g;
        }

        RectTransform NewPanel(Color32 color, string name)
        {
            Clear();
            _panel = UIBuilder.Rect(name, _g.Layers.Fullscreen).Stretch();
            var bg = _panel.gameObject.AddComponent<Image>();
            bg.color = color;
            bg.raycastTarget = false;
            _g.Fx.SetBlack(false);
            var hit = UIBuilder.Hit(_panel.gameObject, "boot:" + name);
            hit.Click += (a, n) => _clicked = true;
            _clicked = false;
            return _panel;
        }

        public void Clear()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null;
        }

        bool SkipPressed => _clicked || _g.Input.KeyDown(GameKey.Space) || _g.Input.KeyDown(GameKey.Enter) || _g.Input.KeyDown(GameKey.Escape);

        IEnumerator WaitOrSkip(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (SkipPressed) { _clicked = false; yield break; }
                yield return null;
            }
        }

        public IEnumerator Run(bool quick)
        {
            _g.Player.Enabled = true;
            if (!quick)
            {
                yield return Disclaimer();
                yield return Title();
            }
            yield return Bios(quick);
            yield return Splash(quick);
            yield return Login();
            Clear();
        }

        IEnumerator Disclaimer()
        {
            var p = NewPanel(Palette.Black, "Disclaimer");
            var t = UIBuilder.Text(p, _g.Content.Text("disclaimer.body"), Palette.BiosText);
            t.rectTransform.Stretch(120, 0, 120, 0);
            t.Align = TextAlign.Center;
            t.VAlign = TextVAlign.Middle;
            t.Wrap = true;
            float a = 0f;
            while (a < 1f) { a += Time.deltaTime; t.color = new Color(0.72f, 0.72f, 0.69f, a); yield return null; }
            yield return WaitOrSkip(4.5f);
            while (a > 0f) { a -= Time.deltaTime * 2f; t.color = new Color(0.72f, 0.72f, 0.69f, Mathf.Max(0f, a)); yield return null; }
        }

        IEnumerator Title()
        {
            var p = NewPanel(Palette.Black, "Title");
            var ghost = UIBuilder.Text(p, "SECOND CURSOR", new Color32(0xC0, 0x39, 0x2B, 0xFF), true);
            ghost.Scale = 4;
            ghost.rectTransform.At(0, 206, ScreenRig.Width, 40);
            ghost.Align = TextAlign.Center;
            var title = UIBuilder.Text(p, "SECOND CURSOR", Palette.BiosBright, true);
            title.Scale = 4;
            title.rectTransform.At(0, 204, ScreenRig.Width, 40);
            title.Align = TextAlign.Center;
            var sub = UIBuilder.Text(p, "a night shift prototype", Palette.BiosText);
            sub.rectTransform.At(0, 256, ScreenRig.Width, 12);
            sub.Align = TextAlign.Center;
            var prompt = UIBuilder.Text(p, "Click to begin your shift", Palette.BiosText);
            prompt.rectTransform.At(0, 380, ScreenRig.Width, 12);
            prompt.Align = TextAlign.Center;
            var hint = UIBuilder.Text(p, "Headphones recommended.", new Color32(0x6A, 0x6A, 0x66, 0xFF));
            hint.rectTransform.At(0, 500, ScreenRig.Width, 12);
            hint.Align = TextAlign.Center;

            _g.Audio.PlayLoop("drone_tension", 0.35f, 2f);
            float t = 0f;
            _clicked = false;
            while (!SkipPressed)
            {
                t += Time.deltaTime;
                prompt.enabled = (t % 1.4f) < 0.9f;
                // The "second cursor" motif: a red twin of the title slips out of alignment now and then.
                bool slip = Random.value < 0.03f;
                ghost.rectTransform.anchoredPosition = new Vector2(slip ? Random.Range(-6f, 6f) : 0f, -(slip ? 206f + Random.Range(-2f, 2f) : 204f));
                ghost.enabled = slip || Random.value < 0.3f;
                if (slip) _g.Fx.Glitch(0.05f, 0.4f);
                yield return null;
            }
            _clicked = false;
            _g.Audio.StopLoop("drone_tension", 1.5f);
            _g.Audio.Play("ui_click");
        }

        IEnumerator Bios(bool quick)
        {
            var p = NewPanel(Palette.Black, "BIOS");
            var text = UIBuilder.Text(p, "", Palette.BiosText);
            text.rectTransform.Stretch(24, 20, 24, 20);
            var sb = new System.Text.StringBuilder();
            _g.Audio.Play("crt_on");
            yield return Waits.Seconds(quick ? 0.2f : 0.9f);
            _g.Audio.Play("bios_beep");
            var lines = _g.Content.Story.biosLines;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("Memory", System.StringComparison.OrdinalIgnoreCase) >= 0 && !quick)
                {
                    // Count memory up like an old POST screen.
                    for (int k = 0; k <= 32768; k += 2048)
                    {
                        text.text = sb + "Memory Test: " + k + "K";
                        yield return Waits.Seconds(0.03f);
                    }
                }
                sb.Append(line).Append('\n');
                text.text = sb.ToString();
                if (i % 3 == 1) _g.Audio.Play("hdd_seek", 0.7f);
                if (SkipPressed) quick = true;
                yield return Waits.Seconds(quick ? 0.02f : Random.Range(0.12f, 0.35f));
            }
            _g.Audio.Play("hdd_spinup", 0.6f);
            yield return Waits.Seconds(quick ? 0.2f : 1.0f);
        }

        IEnumerator Splash(bool quick)
        {
            var p = NewPanel(Palette.Black, "Splash");
            var logoHolder = UIBuilder.Rect("Logo", p).At(ScreenRig.Width / 2 - 72, 150, 144, 144);
            var logo = UIBuilder.Icon(logoHolder, "logo_nexus", 3);
            logo.rectTransform.anchoredPosition = Vector2.zero;
            var name = UIBuilder.Text(p, _g.Content.Text("os.name") + " " + _g.Content.Text("os.version"), Palette.BiosBright, true);
            name.Scale = 2;
            name.rectTransform.At(0, 308, ScreenRig.Width, 24);
            name.Align = TextAlign.Center;
            var tag = UIBuilder.Text(p, _g.Content.Story.splashTagline, Palette.BiosText);
            tag.rectTransform.At(0, 336, ScreenRig.Width, 12);
            tag.Align = TextAlign.Center;
            var vendor = UIBuilder.Text(p, "(c) 1998 " + _g.Content.Text("os.vendor"), new Color32(0x6A, 0x6A, 0x66, 0xFF));
            vendor.rectTransform.At(0, 500, ScreenRig.Width, 12);
            vendor.Align = TextAlign.Center;

            var bar = UIBuilder.Bevel(p, BevelStyle.Sunken, "Load Bar");
            bar.Fill = Palette.Black;
            bar.rectTransform.At(ScreenRig.Width / 2 - 100, 370, 200, 14);
            var blocks = new Image[6];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = UIBuilder.Solid(bar.rectTransform, Palette.TitleActiveB, "Block");
                blocks[i].rectTransform.At(0, 3, 10, 8);
            }
            float t = 0f, dur = quick ? 0.8f : 3.2f;
            while (t < dur)
            {
                t += Time.deltaTime;
                for (int i = 0; i < blocks.Length; i++)
                {
                    float x = Mathf.Repeat(t * 110f + i * 14f, 230f) - 20f;
                    blocks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(x, 3f, 187f)), -3f);
                    blocks[i].enabled = x > 0f && x < 190f;
                }
                if (SkipPressed) t = dur;
                yield return null;
            }
        }

        IEnumerator Login()
        {
            var p = NewPanel(Palette.DesktopA, "Login");
            var c = _g.Content;
            const int w = 360, h = 190;
            var box = UIBuilder.Rect("Log On", p).At((ScreenRig.Width - w) / 2, (ScreenRig.Height - h) / 2 - 20, w, h);
            var frame = box.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            var cap = UIBuilder.Bevel(box, BevelStyle.Gradient, "Caption");
            cap.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            cap.rectTransform.TopStrip(3, 18, 3, 3);
            var capText = UIBuilder.Text(cap.rectTransform, "Log On to " + c.Text("os.name"), Palette.TitleText, true);
            capText.rectTransform.Stretch(6, 0, 4, 0);
            capText.VAlign = TextVAlign.Middle;

            var logoHolder = UIBuilder.Rect("Company Logo", box).At(14, 32, 64, 64);
            var logo = UIBuilder.Icon(logoHolder, "logo_company", 2);
            logo.rectTransform.anchoredPosition = Vector2.zero;
            var welcome = UIBuilder.Text(box, c.Text("login.welcome") + "\n" + c.Text("company.name"), Palette.Text);
            welcome.Wrap = true;
            welcome.rectTransform.At(90, 34, 250, 28);

            Field(box, "User name:", c.Text("login.username"), 72);
            Field(box, "Password:", "********", 96);
            Field(box, "Domain:", c.Text("login.domain"), 120);

            var status = UIBuilder.Text(box, "", Palette.Text);
            status.rectTransform.At(14, 158, 170, 12);

            bool loggedIn = false;
            var ok = UiButton.Create(box, "Log On", a => loggedIn = true, "button:Log On");
            ((RectTransform)ok.transform).At(w - 172, h - 32, 78, 22);
            ok.IsDefault = true;
            var cancel = UiButton.Create(box, "Cancel", a => { status.text = "You must log on to begin your shift."; Sfx.Play("sys_warning"); }, "button:Cancel");
            ((RectTransform)cancel.transform).At(w - 88, h - 32, 78, 22);

            _clicked = false;
            while (!loggedIn)
            {
                if (_g.Input.KeyDown(GameKey.Enter)) loggedIn = true;
                yield return null;
            }
            ok.Enabled = false;
            cancel.Enabled = false;
            status.text = "Applying your personal settings...";
            _g.Player.ShapeOverride = CursorShape.Busy;
            _g.Audio.Play("hdd_seek", 0.8f);
            yield return Waits.Seconds(1.6f);
            _g.Audio.Play("hdd_seek", 0.6f);
            yield return Waits.Seconds(0.8f);
            _g.Player.ShapeOverride = null;
            GameLog.Info(LogChannel.Player, "Logged on as " + c.Text("login.username"));
        }

        void Field(RectTransform box, string label, string value, int y)
        {
            var l = UIBuilder.Text(box, label, Palette.Text);
            l.rectTransform.At(90, y + 4, 70, 12);
            var f = UIBuilder.Bevel(box, BevelStyle.Sunken, label);
            f.rectTransform.At(162, y, 180, 20);
            var t = UIBuilder.Text(f.rectTransform, value, Palette.Text);
            t.rectTransform.Stretch(4, 0, 4, 0);
            t.VAlign = TextVAlign.Middle;
        }
    }
}
