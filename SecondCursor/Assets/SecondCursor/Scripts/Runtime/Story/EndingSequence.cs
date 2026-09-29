using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Story
{
    /// <summary>
    /// What an ending shows (expansion spec 6): the lines typed in the dark, an optional faint goodnight from
    /// Gary, and the card with its title, subtitle and buttons.
    /// </summary>
    public sealed class EndingSpec
    {
        public string Id = "";
        /// <summary>Lines the second cursor types in the dark (null = story.json endingLines, Night 1).</summary>
        public string[] Lines;
        /// <summary>Typed after them by Gary's faint cursor, small and lowercase (Night 2 KEEP).</summary>
        public string[] GaryLines;
        public string TitleKey = "end.card.title";
        public string SubtitleKey = "end.card.subtitle";
        /// <summary>Night 1's demo card: big title, WISHLIST NOW, Start a new shift.</summary>
        public bool DemoCard = true;
        /// <summary>Night offered by a "Continue to Night N" button (0 = none).</summary>
        public int ContinueNight;

        /// <summary>Night 1: the slice's blackout, unchanged, plus Continue to Night 2 once it is unlocked (not in demo builds).</summary>
        public static EndingSpec Night1()
        {
            var spec = new EndingSpec { Id = Core.Content.ContentIds.EndingN1Blackout };
#if !SC_DEMO
            if (SaveSystem.Load().nightUnlocked >= 2) spec.ContinueNight = 2;
#endif
            return spec;
        }
    }

    /// <summary>
    /// The blackout: the monitor dies, silence, then in the dark the second cursor types its last lines,
    /// followed by the end card (Night 1: SECOND CURSOR / WISHLIST NOW; later nights: the night's own card).
    /// </summary>
    public sealed class EndingSequence
    {
        readonly GameServices _g;
        readonly EndingSpec _spec;
        RectTransform _room;

        public EndingSequence(GameServices g) : this(g, null) { }

        public EndingSequence(GameServices g, EndingSpec spec)
        {
            _g = g;
            _spec = spec ?? EndingSpec.Night1();
        }

        /// <summary>Remove the ending's screens (debug jump away from the ending).</summary>
        public void Clear()
        {
            if (_room != null) Object.Destroy(_room.gameObject);
            _room = null;
        }

        public IEnumerator Run()
        {
            var g = _g;
            // Progress was saved by the night's director just before this (NightDirector.CompleteNight).
            g.Flags.Set(Flags.Ending);
            g.Entity.Brain.Enabled = false;
            g.Entity.Interrupt();
            if (g.Gary != null)
            {
                g.Gary.Interrupt();
                g.Gary.SetPresent(false, 0.3f);
            }
            g.Player.Enabled = false;
            g.Player.Visible = false;

            g.Audio.StopAllLoops(0.2f);
            g.Audio.Play("power_down");
            if (g.CameraRig != null) g.CameraRig.LightsOn = false;
            yield return Waits.Seconds(0.5f);
            g.Audio.Play("crt_off");
            yield return g.Fx.PowerOff(0.9f);
            g.Fx.SetBlack(true);
            yield return Waits.Seconds(2.6f);

            // In the dark: a black OS screen with only the second cursor.
            var black = UIBuilder.Rect("Black Room", g.Layers.Fullscreen).Stretch();
            _room = black;
            var bg = black.gameObject.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;
            UIBuilder.Hit(black.gameObject, "ending"); // nothing behind the dark screen can be clicked
            g.Windows.CloseAll();
            g.Fx.SetBlack(false);
            g.Fx.PowerOn();

            g.Entity.Teleport(new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.62f));
            g.Entity.State = Core.Entity.EntityState.Communicating;
            yield return g.Entity.Appear(null, 1.5f, false);
            yield return Waits.Seconds(1.2f);

            var text = UIBuilder.Text(black, "", Palette.EntityText, true);
            text.Scale = 2;
            text.rectTransform.At(80, 200, ScreenRig.Width - 160, 200);
            text.Align = TextAlign.Center;
            var sb = new System.Text.StringBuilder();
            float top = ScreenRig.Height - 200f;
            int lineIndex = 0;
            foreach (var line in _spec.Lines ?? g.Content.Story.endingLines)
            {
                var current = new System.Text.StringBuilder();
                foreach (char c in line)
                {
                    sb.Append(c);
                    current.Append(c);
                    text.text = sb.ToString();
                    g.Audio.Play(c == ' ' ? "key_space" : "key_tap", 0.9f, Random.Range(0.9f, 1.05f), AudioPanFor(g));
                    // The cursor rides just after the last letter, jittering with each keystroke, never on the words.
                    float w = PixelFont.Measure(current.ToString(), 0, true, text.Scale).x;
                    var caret = new Vector2(ScreenRig.Width * 0.5f + w * 0.5f + 5f, top - lineIndex * text.LineHeightPx - 3f);
                    g.Entity.Teleport(caret + Random.insideUnitCircle * 0.8f);
                    yield return Waits.Seconds(c == ' ' ? 0.16f : Random.Range(0.07f, 0.16f));
                }
                yield return Waits.Seconds(1.6f);
                sb.Append('\n');
                text.text = sb.ToString();
                lineIndex++;
            }
            PixelText small = null;
            if (_spec.GaryLines != null && _spec.GaryLines.Length > 0 && g.Gary != null)
            {
                yield return Waits.Seconds(1.2f);
                small = UIBuilder.Text(black, "", Palette.GaryOutline);
                small.rectTransform.At(80, 200 + lineIndex * text.LineHeightPx + 14, ScreenRig.Width - 160, 40);
                small.Align = TextAlign.Center;
                yield return GaryTypes(small, top - lineIndex * text.LineHeightPx - 14f);
            }
            yield return Waits.Seconds(2.5f);
            if (small != null) g.Gary.SetPresent(false, 1.5f);
            yield return g.Entity.Vanish(1.5f);
            float a = 1f;
            while (a > 0f)
            {
                a -= Time.deltaTime * 0.8f;
                text.color = new Color(0.91f, 0.9f, 0.87f, Mathf.Max(0f, a));
                if (small != null) small.color = new Color(0.85f, 0.66f, 0.25f, Mathf.Max(0f, a) * 0.8f);
                yield return null;
            }
            yield return Waits.Seconds(1f);
            yield return EndCard(black);
        }

        static float AudioPanFor(GameServices g) => Audio.AudioManager.PanFor(g.EntityAgent.Position.x);

        /// <summary>Gary's faint cursor types his goodnight under the lines, small, slow, with its tremble.</summary>
        IEnumerator GaryTypes(PixelText small, float y)
        {
            var g = _g;
            var gary = g.Gary;
            gary.MaxAlpha = 0.45f;
            gary.Teleport(new Vector2(ScreenRig.Width * 0.5f, y - 30f));
            yield return gary.Appear(null, 1.2f, false);
            var sb = new System.Text.StringBuilder();
            foreach (var line in _spec.GaryLines)
            {
                var current = new System.Text.StringBuilder();
                foreach (char c in line)
                {
                    sb.Append(c);
                    current.Append(c);
                    small.text = sb.ToString();
                    g.Audio.Play(c == ' ' ? "key_space" : "key_tap", 0.45f, Random.Range(0.85f, 0.95f), Audio.AudioManager.PanFor(gary.Agent.Position.x));
                    float w = PixelFont.Measure(current.ToString(), 0, false, 1).x;
                    gary.Teleport(new Vector2(ScreenRig.Width * 0.5f + w * 0.5f + 4f, y) + Random.insideUnitCircle * 1.6f);
                    yield return Waits.Seconds(c == ' ' ? 0.3f : Random.Range(0.22f, 0.38f));
                }
                sb.Append('\n');
                yield return Waits.Seconds(1.2f);
            }
        }

        IEnumerator EndCard(RectTransform parent)
        {
            var g = _g;
            var c = g.Content;
            g.Audio.Play("end_tone");
            g.Audio.Play("low_thump", 0.8f);

            var title = UIBuilder.Text(parent, c.Text(_spec.TitleKey), Palette.BiosBright, true);
            title.Scale = 5;
            title.rectTransform.At(0, 150, ScreenRig.Width, 60);
            title.Align = TextAlign.Center;
            var sub = UIBuilder.Text(parent, c.Text(_spec.SubtitleKey), Palette.BiosText);
            sub.rectTransform.At(0, 222, ScreenRig.Width, 12);
            sub.Align = TextAlign.Center;
            if (!_spec.DemoCard)
            {
                yield return NightCardButtons(parent);
                yield break;
            }
            var cta = UIBuilder.Text(parent, c.Text("end.card.cta"), new Color32(0xE8, 0xC4, 0x5A, 0xFF), true);
            cta.Scale = 3;
            cta.rectTransform.At(0, 290, ScreenRig.Width, 36);
            cta.Align = TextAlign.Center;
            var thanks = UIBuilder.Text(parent, c.Text("end.card.thanks"), Palette.BiosText);
            thanks.rectTransform.At(0, 350, ScreenRig.Width, 12);
            thanks.Align = TextAlign.Center;

            g.Player.Enabled = true;
            g.Player.Visible = true;
            bool store = !string.IsNullOrEmpty(SteamBridge.StoreUrl);
            bool next = _spec.ContinueNight > 0;
            int buttons = 2 + (store ? 1 : 0) + (next ? 1 : 0);
            int x0 = ScreenRig.Width / 2 - buttons * 75;
            if (next)
            {
                int night = _spec.ContinueNight;
                var cont = UiButton.Create(parent, c.Format("end.card.continue", night), a => GameBootstrap.Restart(night), "button:Continue", true);
                ((RectTransform)cont.transform).At(x0, 420, 140, 24);
                x0 += 150;
            }
            var again = UiButton.Create(parent, "Start a new shift", a => GameBootstrap.Restart(), "button:Restart");
            ((RectTransform)again.transform).At(x0, 420, 140, 24);
            if (store)
            {
                var wish = UiButton.Create(parent, "Wishlist on Steam", a => SteamBridge.OpenStorePage(), "button:Wishlist", true);
                ((RectTransform)wish.transform).At(x0 + 150, 420, 140, 24);
                x0 += 150;
            }
            var quit = UiButton.Create(parent, "Quit", a => Quit(), "button:Quit");
            ((RectTransform)quit.transform).At(x0 + 150, 420, 140, 24);

            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                cta.enabled = (t % 1.6f) < 1.1f;
                // The title's second letter pair occasionally doubles, the way the cursor did.
                if (Random.value < 0.01f) g.Fx.Glitch(0.06f, 0.5f);
                yield return null;
            }
        }

        /// <summary>Nights 2 and 3: the night's card with Continue to the next night, Title and Quit.</summary>
        IEnumerator NightCardButtons(RectTransform parent)
        {
            var g = _g;
            var c = g.Content;
            g.Player.Enabled = true;
            g.Player.Visible = true;
            bool next = _spec.ContinueNight > 0;
            int x0 = ScreenRig.Width / 2 - (next ? 225 : 150);
            if (next)
            {
                int night = _spec.ContinueNight;
                var cont = UiButton.Create(parent, c.Format("end.card.continue", night), a => GameBootstrap.Restart(night), "button:Continue", true);
                ((RectTransform)cont.transform).At(x0, 420, 140, 24);
                x0 += 150;
            }
            var menu = UiButton.Create(parent, c.Text("end.card.menu"), a => GameBootstrap.ToTitle(), "button:Title");
            ((RectTransform)menu.transform).At(x0, 420, 140, 24);
            var quit = UiButton.Create(parent, "Quit", a => Quit(), "button:Quit");
            ((RectTransform)quit.transform).At(x0 + 150, 420, 140, 24);
            while (true)
            {
                if (Random.value < 0.006f) g.Fx.Glitch(0.05f, 0.4f);
                yield return null;
            }
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
