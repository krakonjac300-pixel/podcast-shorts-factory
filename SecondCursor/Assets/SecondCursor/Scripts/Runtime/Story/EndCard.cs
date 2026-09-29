using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>The buttons under a night's end card.</summary>
    [Flags]
    public enum EndCardButtons { None = 0, Continue = 1, Title = 2, NightSelect = 4, Quit = 8, Wishlist = 16 }

    /// <summary>
    /// The card after an ending (expansion spec 6.3): the night's title and subtitle, and its buttons. Nights 1 and 2:
    /// Continue to the next night, Title, Quit. Night 3: Title, Night Select, Quit and the thanks line. The demo's
    /// WISHLIST card: Wishlist (when the store can open), Title, Quit. Keyboard and mouse both work; every button that
    /// starts something starts a run that counts.
    /// </summary>
    public static class EndCard
    {
        const int ButtonWidth = 150, ButtonHeight = 24, ButtonGap = 12, ButtonTop = 420;

        /// <summary>The spec's buttons, or the defaults its kind of card gets.</summary>
        public static EndCardButtons ButtonsFor(EndingSpec spec)
        {
            if (spec.Buttons != EndCardButtons.None) return spec.Buttons;
            if (spec.DemoCard) return EndCardButtons.Wishlist | EndCardButtons.Title | EndCardButtons.Quit;
            if (spec.ContinueNight > 0 && !spec.FinalCard && spec.ContinueNight <= GameBootstrap.MaxNight)
                return EndCardButtons.Continue | EndCardButtons.Title | EndCardButtons.Quit;
            var b = EndCardButtons.Title | EndCardButtons.Quit;
            if (GameBootstrap.NightSelectAvailable) b |= EndCardButtons.NightSelect;
            return b;
        }

        public static IEnumerator Run(GameServices g, EndingSpec spec, RectTransform parent)
        {
            var c = g.Content;
            g.Audio.Play("end_tone");
            g.Audio.Play("low_thump", 0.8f);

            var title = UIBuilder.Text(parent, c.Text(spec.TitleKey), Palette.BiosBright, true);
            title.Scale = 5;
            title.rectTransform.At(0, 150, ScreenRig.Width, 60);
            title.Align = TextAlign.Center;
            var sub = UIBuilder.Text(parent, c.Text(spec.SubtitleKey), Palette.BiosText);
            sub.rectTransform.At(0, 222, ScreenRig.Width, 12);
            sub.Align = TextAlign.Center;
            PixelText cta = null;
            if (spec.DemoCard)
            {
                cta = UIBuilder.Text(parent, c.Text("end.card.cta"), new Color32(0xE8, 0xC4, 0x5A, 0xFF), true);
                cta.Scale = 3;
                cta.rectTransform.At(0, 290, ScreenRig.Width, 36);
                cta.Align = TextAlign.Center;
                var thanks = UIBuilder.Text(parent, c.Text("end.card.thanks"), Palette.BiosText);
                thanks.rectTransform.At(0, 350, ScreenRig.Width, 12);
                thanks.Align = TextAlign.Center;
            }
            else if (!string.IsNullOrEmpty(spec.ThanksKey))
            {
                var line = UIBuilder.Text(parent, c.Text(spec.ThanksKey), new Color32(0x8A, 0x8A, 0x84, 0xFF));
                line.rectTransform.At(0, 250, ScreenRig.Width, 12);
                line.Align = TextAlign.Center;
            }

            g.Player.Enabled = true;
            g.Player.Visible = true;
            var nav = new MenuNav(g) { RingColor = Palette.BiosBright };
            var buttons = Buttons(g, spec, parent, nav);
            nav.Focus(buttons.Count > 0 ? buttons[0] : null);
            GameLog.Info(LogChannel.Story, "End card: " + spec.Id + " [" + ButtonsFor(spec) + "]");

            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                nav.Tick();
                if (cta != null)
                {
                    cta.enabled = (t % 1.6f) < 1.1f;
                    // The title's second letter pair occasionally doubles, the way the cursor did.
                    if (UnityEngine.Random.value < 0.01f) g.Fx.Glitch(0.06f, 0.5f);
                }
                else if (UnityEngine.Random.value < 0.006f) g.Fx.Glitch(0.05f, 0.4f);
                yield return null;
            }
        }

        static List<UiButton> Buttons(GameServices g, EndingSpec spec, RectTransform parent, MenuNav nav)
        {
            var c = g.Content;
            var want = ButtonsFor(spec);
            if (!SteamBridge.CanOpenStore) want &= ~EndCardButtons.Wishlist;
            var defs = new List<(string label, string id, Action<CursorAgent> click)>();
            if ((want & EndCardButtons.Continue) != 0)
            {
                int night = spec.ContinueNight;
                defs.Add((c.Format("end.card.continue", night), "button:Continue", a => GameBootstrap.StartFromMenu(night)));
            }
            if ((want & EndCardButtons.Wishlist) != 0) defs.Add((c.Text("end.card.wishlist"), "button:Wishlist", a => SteamBridge.OpenStorePage()));
            if ((want & EndCardButtons.Title) != 0) defs.Add((c.Text("end.card.menu"), "button:Title", a => GameBootstrap.ToTitle()));
            if ((want & EndCardButtons.NightSelect) != 0) defs.Add((c.Text("end.card.select"), "button:NightSelect", a => GameBootstrap.ToNightSelect()));
            if ((want & EndCardButtons.Quit) != 0) defs.Add((c.Text("end.card.quit"), "button:Quit", a => PauseMenu.QuitGame()));

            var list = new List<UiButton>();
            int total = defs.Count * ButtonWidth + (defs.Count - 1) * ButtonGap;
            int x = (ScreenRig.Width - total) / 2;
            foreach (var (label, id, click) in defs)
            {
                var b = UiButton.Create(parent, label, click, id, id == "button:Continue" || id == "button:Wishlist");
                ((RectTransform)b.transform).At(x, ButtonTop, ButtonWidth, ButtonHeight);
                nav.Add(b);
                list.Add(b);
                x += ButtonWidth + ButtonGap;
            }
            return list;
        }
    }
}
