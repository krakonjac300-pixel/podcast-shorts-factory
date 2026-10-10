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
    /// Fiction disclaimer (once per launch) -> title menu (when the root was built for it) or the night card -> BIOS
    /// POST -> NEXUS OS splash -> log-on dialog. Built on the fullscreen layer; the player's cursor works normally (the
    /// log-on button is a real button).
    /// </summary>
    public sealed class BootSequence
    {
        readonly GameServices _g;
        RectTransform _panel;
        bool _clicked;

        /// <summary>The fiction and photosensitivity disclaimer was shown in this app launch (it shows once per launch).</summary>
        internal static bool DisclaimerShownThisLaunch;

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

        bool SkipPressed => !Game.PauseMenu.IsPaused && Time.frameCount != Game.PauseMenu.StateChangeFrame
                            && (_clicked || _g.Input.KeyDown(GameKey.Space) || _g.Input.KeyDown(GameKey.Enter));

        IEnumerator WaitOrSkip(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (SkipPressed) { _clicked = false; yield break; }
                yield return null;
            }
        }

        int _night = 1;

        public IEnumerator Run(bool quick) => Run(quick, 1);

        /// <summary>
        /// The disclaimer once per launch; then either the title menu (a title root: the menu's choice builds a new
        /// root, so this never goes further) or the night: its start is recorded, then the night card (every night,
        /// spec 3.2), BIOS, splash and the night's log-on.
        /// </summary>
        public IEnumerator Run(bool quick, int night)
        {
            _night = night;
            _g.Player.Enabled = true;
            if (!quick && !DisclaimerShownThisLaunch)
            {
                yield return Disclaimer();
                DisclaimerShownThisLaunch = true;
            }
            if (_g.ShowTitle)
            {
                _g.ShowTitle = false;
                var menu = new TitleMenu(_g, NewPanel(Palette.Black, "Title"));
                _g.Player.Visible = true;
                yield return menu.Run();
                yield break;
            }
            _g.Director.MarkNightStarted();
            if (!quick) yield return NightCard(night);
            yield return Bios(quick);
            yield return Splash(quick);
            yield return Login();
            Clear();
        }

        /// <summary>Black, the night and its date, 2.5 s (click or key skips).</summary>
        IEnumerator NightCard(int night)
        {
            var p = NewPanel(Palette.Black, "Night Card");
            _g.Player.Visible = false;
            var t = UIBuilder.Text(p, _g.Content.Text("night.card." + night, "NIGHT " + night), Palette.BiosBright, true);
            t.Scale = 2;
            t.rectTransform.Stretch(0, 0, 0, 0);
            t.Align = TextAlign.Center;
            t.VAlign = TextVAlign.Middle;
            _g.Audio.Play("low_thump", 0.5f, 0.9f);
            float a = 0f;
            while (a < 1f) { a += Time.deltaTime * 2f; t.color = new Color(1f, 1f, 0.96f, Mathf.Min(1f, a)); yield return null; }
            // Phase N (finding 17): the date is always seen: a click or key skips only after the card has been up a full second.
            float readable = Time.time + 1f;
            while (Time.time < readable) yield return null;
            _clicked = false;
            yield return WaitOrSkip(1.0f);
            while (a > 0f) { a -= Time.deltaTime * 3f; t.color = new Color(1f, 1f, 0.96f, Mathf.Max(0f, a)); yield return null; }
            GameLog.Info(LogChannel.Story, "Night card: night " + night);
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
            var settings = Game.SaveSystem.LoadSettings();
            PixelText prompt = null;
            if (!settings.flashingChosen)
            {
                bool chosen = false;
                var full = UiButton.Create(p, "Full effects", a => { _g.Fx.ReduceFlashing = false; chosen = true; }, "button:FullEffects");
                ((RectTransform)full.transform).At(ScreenRig.Width / 2 - 150, 400, 140, 24);
                var reduced = UiButton.Create(p, "Reduce flashing", a => { _g.Fx.ReduceFlashing = true; chosen = true; }, "button:ReduceFlashing");
                ((RectTransform)reduced.transform).At(ScreenRig.Width / 2 + 10, 400, 140, 24);
                // Phase M: the choice also decides how hard the jump scares hit.
                var note = UIBuilder.Text(p, _g.Content.Text("disclaimer.choice.note"), Palette.BiosText);
                note.rectTransform.At(0, 432, ScreenRig.Width, 12);
                note.Align = TextAlign.Center;
                // Phase Q4 (A6): sound captions are offered here too (off unless chosen), and the whole screen works from the keyboard (A7).
                UiButton captions = null;
                captions = UiButton.Create(p, CaptionsLabel(), a =>
                {
                    Game.AccessSettings.SetCaptions(!Game.AccessSettings.Captions);
                    captions.SetLabel(CaptionsLabel());
                }, "button:Captions");
                ((RectTransform)captions.transform).At(ScreenRig.Width / 2 - 120, 452, 240, 24);
                var keys = UIBuilder.Text(p, _g.Content.Text("disclaimer.keys", "Keyboard: arrow keys to move, Enter to choose."), Palette.BiosText);
                keys.rectTransform.At(0, 486, ScreenRig.Width, 12);
                keys.Align = TextAlign.Center;
                var nav = new MenuNav(_g) { RingColor = Palette.BiosBright };
                nav.Add(full);
                nav.Add(reduced);
                nav.Add(captions);
                nav.Focus(reduced);   // the safer choice is the one a key press reaches first
                while (!chosen)
                {
                    nav.Tick();
                    yield return null;
                }
                nav.Clear();
                // Options may have saved other settings while the choice was open (Esc): write back a fresh copy.
                settings = Game.SaveSystem.LoadSettings();
                settings.reduceFlashing = _g.Fx.ReduceFlashing;
                settings.flashingChosen = true;
                Game.AccessSettings.Save(settings);
                Game.SaveSystem.SaveSettings(settings);
                full.gameObject.SetActive(false);
                reduced.gameObject.SetActive(false);
                captions.gameObject.SetActive(false);
                keys.gameObject.SetActive(false);
                note.gameObject.SetActive(false);
                _clicked = false;
            }
            else
            {
                // Later launches: the screen moves on by itself after 6.5 s, and says how to skip it.
                prompt = UIBuilder.Text(p, _g.Content.Text("disclaimer.continue", "Click to continue"), Palette.BiosText);
                prompt.rectTransform.At(0, 440, ScreenRig.Width, 12);
                prompt.Align = TextAlign.Center;
                prompt.color = new Color(0.72f, 0.72f, 0.69f, 0.55f);
                yield return WaitOrSkip(6.5f); // long enough to read the photosensitivity warning
            }
            while (a > 0f)
            {
                a -= Time.deltaTime * 2f;
                t.color = new Color(0.72f, 0.72f, 0.69f, Mathf.Max(0f, a));
                if (prompt != null) prompt.color = new Color(0.72f, 0.72f, 0.69f, Mathf.Max(0f, a) * 0.55f);
                yield return null;
            }
        }

        string CaptionsLabel() => _g.Content.Format("pause.captions", _g.Content.Text(Game.AccessSettings.Captions ? "pause.on" : "pause.off", Game.AccessSettings.Captions ? "On" : "Off"));

        IEnumerator Bios(bool quick)
        {
            var p = NewPanel(Palette.Black, "BIOS");
            // No pointer before the OS has loaded one: it comes back at the log-on screen.
            _g.Player.Visible = false;
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
            _g.Player.Visible = true;
            var c = _g.Content;
            const int w = 360, h = 190;
            var box = UIBuilder.Rect("Log On", p).At((ScreenRig.Width - w) / 2, (ScreenRig.Height - h) / 2 - 20, w, h);
            // Phase S (Large text tester: the log-on box stayed at normal size): the whole box grows with the Reading text size, about its centre.
            float readingFactor = DisplaySettings.ReadingFactor;
            if (readingFactor > 1f)
            {
                var centre = box.anchoredPosition + new Vector2(w / 2f, -h / 2f);
                box.pivot = new Vector2(0.5f, 0.5f);
                box.anchoredPosition = centre;
                box.localScale = new Vector3(readingFactor, readingFactor, 1f);
            }
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

            // Phase Q1 (A T5, the minute-one hook): on Night 1 the name field already holds 017, and nobody retypes it.
            bool retype = _night == 1;
            var user = Field(box, "User name:", retype ? RetypedFrom : c.Text("login.username"), 72);
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

            if (retype)
            {
                ok.Enabled = false;
                yield return Retype(user, c.Text("login.username"));
                // Phase R (sixth blind playtest: "the game typed for me, I did not know who"): once the scare has landed, the box says who it was.
                status.rectTransform.At(14, 144, 330, 12);
                status.text = c.Text("login.autofill", "");
                ok.Enabled = true;
                loggedIn = false; // a click on the greyed button while it typed does not count
            }
            _clicked = false;
            while (!loggedIn)
            {
                if (_g.Input.KeyDown(GameKey.Enter)) loggedIn = true;
                if (retype) user.SetCaret(true, user.text.Length, (Time.time % 1.06f) < 0.53f);
                yield return null;
            }
            user.SetCaret(false, -1, false);
            ok.Enabled = false;
            cancel.Enabled = false;
            // Night 1 keeps its original line; later nights restore their settings "from a copy".
            status.text = _night > 1 ? c.Text("login.progress") : "Applying your personal settings...";
            if (_night > 1) status.rectTransform.At(14, 144, 330, 12); // the longer line gets the free row above the buttons
            _g.Player.ShapeOverride = CursorShape.Busy;
            _g.Audio.Play("hdd_seek", 0.8f);
            yield return Waits.Seconds(1.6f);
            _g.Audio.Play("hdd_seek", 0.6f);
            yield return Waits.Seconds(0.8f);
            _g.Player.ShapeOverride = null;
            GameLog.Info(LogChannel.Player, "Logged on as " + c.Text("login.username"));
        }

        PixelText Field(RectTransform box, string label, string value, int y)
        {
            var l = UIBuilder.Text(box, label, Palette.Text);
            l.rectTransform.At(90, y + 4, 70, 12);
            var f = UIBuilder.Bevel(box, BevelStyle.Sunken, label);
            f.rectTransform.At(162, y, 180, 20);
            var t = UIBuilder.Text(f.rectTransform, value, Palette.Text);
            t.rectTransform.Stretch(4, 0, 4, 0);
            t.VAlign = TextVAlign.Middle;
            return t;
        }

        /// <summary>What the Night 1 name field holds before it is retyped (017 is already on screen that night as employee_017.dat).</summary>
        const string RetypedFrom = "017";
        const float RetypeHold = 1.0f, RetypeDelete = 0.12f, RetypeType = 0.08f;

        /// <summary>
        /// Phase Q1 (A T5): 017 sits in the field for a second, is deleted a character at a time (keys played backwards) and CROURKE is typed in
        /// (about 1.9 s in all) with a caret, by nobody. The pointer moves freely; Log On works once it is done.
        /// </summary>
        IEnumerator Retype(PixelText field, string name)
        {
            string text = RetypedFrom;
            field.SetCaret(true, text.Length, true);
            yield return Waits.Seconds(RetypeHold);
            while (text.Length > 0)
            {
                text = text.Substring(0, text.Length - 1);
                field.text = text;
                field.SetCaret(true, text.Length, true);
                _g.Audio.Play("key_tap_rev", 0.3f, Random.Range(0.95f, 1.05f));
                yield return Waits.Seconds(RetypeDelete);
            }
            foreach (char ch in name)
            {
                text += ch;
                field.text = text;
                field.SetCaret(true, text.Length, true);
                _g.Audio.Play("key_tap", 0.3f, Random.Range(0.95f, 1.05f));
                yield return Waits.Seconds(RetypeType);
            }
            GameLog.Info(LogChannel.Story, "Log on: the user name was retyped " + RetypedFrom + " -> " + name + " by nobody");
        }
    }
}
