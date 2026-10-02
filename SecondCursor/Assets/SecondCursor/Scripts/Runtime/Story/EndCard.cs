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
        /// <summary>Seconds the card shows before its buttons take any input.</summary>
        const float ButtonsAfter = 1.5f;

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

        /// <summary>The three Night 3 endings as slots: the ones seen by name in bright, the others as "???" (an empty slot is a reason to play again).</summary>
        static void EndingSlots(RectTransform parent, Core.Content.ContentDatabase c, string[] seenIds, int y)
        {
            var ids = Core.Game.AchievementIds.Night3Endings;
            string[] keys = { "end.shred.title", "end.keep.title", "end.logoff.title" };
            const int slotW = 140, gap = 14;
            int x = (ScreenRig.Width - (ids.Length * slotW + (ids.Length - 1) * gap)) / 2;
            for (int i = 0; i < ids.Length; i++)
            {
                bool seen = seenIds != null && Array.IndexOf(seenIds, ids[i]) >= 0;
                string label = seen ? "[x] " + c.Text(keys[i]) : "[ ] " + c.Text("end.slot.empty", "???");
                var slot = UIBuilder.Text(parent, label, seen ? Palette.BiosBright : new Color32(0x6A, 0x6A, 0x66, 0xFF), seen, "Ending Slot " + i);
                slot.rectTransform.At(x + i * (slotW + gap), y, slotW, 12);
                slot.Align = TextAlign.Center;
            }
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
            if (!string.IsNullOrEmpty(spec.Outcome))
            {
                // How the night ended for you, under the subtitle (the demo card too: above WISHLIST NOW).
                // Phase Q4 (R8): the cause line is the one line that explains the ending, so it is bright and bold (it was #8A8A84, dimmer than the thanks).
                var outcome = UIBuilder.Text(parent, spec.Outcome, Palette.BiosBright, true);
                outcome.rectTransform.At(0, 244, ScreenRig.Width, 12);
                outcome.Align = TextAlign.Center;
                GameLog.Info(LogChannel.Story, "End card outcome: " + spec.Outcome);
            }
            PixelText cta = null;
            // Phase J: the last night's card has a cause line too, so the thanks and the endings count move down under it.
            int below = string.IsNullOrEmpty(spec.Outcome) ? 0 : 18;
            // Phase Q2: what session 017 kept (V7) and the night's Retention Record rows (T2) under the outcome.
            int y = 244 + below;
            // Phase R: what the outcome means, in plain words (the demo's card keeps room for WISHLIST NOW: its first line only).
            if (spec.OutcomeDetail != null)
            {
                int shown = 0;
                foreach (var key in spec.OutcomeDetail)
                {
                    if (string.IsNullOrEmpty(key) || (spec.DemoCard && shown >= 1)) continue;
                    var detail = UIBuilder.Text(parent, key, Palette.BiosText);
                    detail.rectTransform.At(0, y, ScreenRig.Width, 12);
                    detail.Align = TextAlign.Center;
                    GameLog.Info(LogChannel.Story, "End card: " + key);
                    y += 14;
                    shown++;
                }
            }
            if (!string.IsNullOrEmpty(spec.KeptLine))
            {
                var kept = UIBuilder.Text(parent, spec.KeptLine, new Color32(0x8A, 0x8A, 0x84, 0xFF));
                kept.rectTransform.At(0, y, ScreenRig.Width, 12);
                kept.Align = TextAlign.Center;
                GameLog.Info(LogChannel.Story, "End card: " + spec.KeptLine);
                y += spec.OutcomeDetail == null ? 18 : 14;
            }
            if (!string.IsNullOrEmpty(spec.CarryLine) && !spec.DemoCard)
            {
                var carry = UIBuilder.Text(parent, spec.CarryLine, new Color32(0x8A, 0x8A, 0x84, 0xFF));
                carry.rectTransform.At(0, y, ScreenRig.Width, 12);
                carry.Align = TextAlign.Center;
                GameLog.Info(LogChannel.Story, "End card: " + spec.CarryLine);
                y += 14;
            }
            if (spec.RecordRows != null && spec.RecordRows.Count > 0) y = RecordView.CardRows(parent, spec.RecordRows, y + 4) + 6;
            if (spec.DemoCard)
            {
                int ctaY = Mathf.Max(290, y + 6);
                cta = UIBuilder.Text(parent, c.Text("end.card.cta"), new Color32(0xE8, 0xC4, 0x5A, 0xFF), true);
                cta.Scale = 3;
                cta.rectTransform.At(0, ctaY, ScreenRig.Width, 36);
                cta.Align = TextAlign.Center;
                var thanks = UIBuilder.Text(parent, c.Text("end.card.thanks"), Palette.BiosText);
                thanks.rectTransform.At(0, Mathf.Min(ctaY + 46, ButtonTop - 20), ScreenRig.Width, 12);
                thanks.Align = TextAlign.Center;
            }
            else if (!string.IsNullOrEmpty(spec.ThanksKey))
            {
                var line = UIBuilder.Text(parent, c.Text(spec.ThanksKey), new Color32(0x8A, 0x8A, 0x84, 0xFF));
                line.rectTransform.At(0, 250 + below, ScreenRig.Width, 12);
                line.Align = TextAlign.Center;
            }
            if (spec.FinalCard)
            {
                // The endings seen so far, under the thanks line: an invitation to Night Select. Phase Q4 (R8): three slots, not a count.
                EndingSlots(parent, c, SaveSystem.Load().endingsSeen, 270 + below);
            }

            g.Player.Enabled = true;
            g.Player.Visible = true;
            // Phase K: the card is read before it can be left. The fourth blind tester's typing (meant for a Jotter that had just gone)
            // reached the KEEP card, focused Quit and pressed it: the session ended before the card was seen. The buttons come a
            // moment later, and Quit asks first.
            yield return Waits.Seconds(ButtonsAfter);
            var nav = new MenuNav(g) { RingColor = Palette.BiosBright };
            var buttons = Buttons(g, spec, parent, nav);
            nav.Focus(buttons.Count > 0 ? buttons[0] : null);
            GameLog.Info(LogChannel.Story, "End card: " + spec.Id + " [" + ButtonsFor(spec) + "]");
            // M12: on the demo's card the second cursor waits beside WISHLIST and politely steps aside for yours.
            CourteousGhost ghost = null;
            if (spec.DemoCard && cta != null)
            {
                var wishlist = buttons.Find(b => b.Hit != null && b.Hit.elementId == "button:Wishlist");
                var anchor = wishlist != null ? ((RectTransform)wishlist.transform).WorldRect() : WishlistTextRect(cta);
                ghost = new CourteousGhost(g, new Vector2(anchor.xMax + 10f, anchor.center.y + 8f));
            }

            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                nav.Tick();
                ghost?.Tick(Time.deltaTime);
                if (cta != null)
                {
                    cta.enabled = (t % 1.6f) < 1.1f;
                    // The title's second letter pair occasionally doubles, the way the cursor did.
                    if (UnityEngine.Random.value < MathUtil.ChanceAt60(0.01f, Time.deltaTime)) g.Fx.Glitch(0.06f, 0.5f);
                }
                else if (UnityEngine.Random.value < MathUtil.ChanceAt60(0.006f, Time.deltaTime)) g.Fx.Glitch(0.05f, 0.4f);
                yield return null;
            }
        }

        /// <summary>The blinking WISHLIST NOW line's own extent (the text is centred in a full-width rect).</summary>
        static Rect WishlistTextRect(PixelText cta)
        {
            var r = cta.rectTransform.WorldRect();
            float w = PixelFont.MeasureLine(cta.text, true, cta.Scale);
            return new Rect(r.center.x - w * 0.5f, r.y, w, r.height);
        }

        /// <summary>
        /// The second cursor resting beside the call to action. When your cursor comes within 60 px it steps 30 px
        /// aside (away from yours) and drifts back once you leave. It never presses anything.
        /// </summary>
        sealed class CourteousGhost
        {
            const float Near = 60f, Step = 30f, Speed = 160f;
            readonly GameServices _g;
            readonly Vector2 _rest;
            bool _logged;

            public CourteousGhost(GameServices g, Vector2 rest)
            {
                _g = g;
                _rest = rest;
                g.Entity.Interrupt();
                g.Entity.Brain.Enabled = false;
                g.Entity.Teleport(rest);
                g.CoroutineHost.StartCoroutine(g.Entity.Appear(null, 1.2f, false));
            }

            public void Tick(float dt)
            {
                var me = _g.EntityAgent.Position;
                var you = _g.Player.Position;
                Vector2 target = _rest;
                if (Vector2.Distance(you, _rest) < Near || Vector2.Distance(you, me) < Near)
                {
                    float side = you.x <= _rest.x ? 1f : -1f;
                    target = _rest + new Vector2(side * Step, 0f);
                    if (!_logged)
                    {
                        _logged = true;
                        GameLog.Info(LogChannel.Entity, "End card: the second cursor steps aside from WISHLIST");
                    }
                }
                _g.Entity.Teleport(Vector2.MoveTowards(me, target, Speed * dt));
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
            var list = new List<UiButton>();
            if ((want & EndCardButtons.Quit) != 0) defs.Add((c.Text("end.card.quit"), "button:Quit", a => AskQuit(g, parent, nav, list)));
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

        /// <summary>Quit asks first, like the pause menu's: the card's buttons give way to "Quit SECOND CURSOR?" with Back focused.</summary>
        static void AskQuit(GameServices g, RectTransform parent, MenuNav nav, List<UiButton> buttons)
        {
            var c = g.Content;
            foreach (var b in buttons) b.gameObject.SetActive(false);
            var ask = UIBuilder.Text(parent, c.Text("end.card.quit.ask"), Palette.BiosBright, true);
            ask.rectTransform.At(0, ButtonTop - 22, ScreenRig.Width, 12);
            ask.Align = TextAlign.Center;
            var parts = new List<GameObject> { ask.gameObject };
            nav.Clear();
            int x = (ScreenRig.Width - (ButtonWidth * 2 + ButtonGap)) / 2;
            UiButton back = null;
            foreach (var (label, id, quit) in new[] { (c.Text("end.card.quit.back"), "button:QuitBack", false), (c.Text("end.card.quit"), "button:QuitYes", true) })
            {
                var b = UiButton.Create(parent, label, a =>
                {
                    if (quit) { PauseMenu.QuitGame(); return; }
                    foreach (var o in parts) UnityEngine.Object.Destroy(o);
                    nav.Clear();
                    foreach (var old in buttons) { old.gameObject.SetActive(true); nav.Add(old); }
                    nav.Focus(buttons.Count > 0 ? buttons[buttons.Count - 1] : null);
                }, id);
                ((RectTransform)b.transform).At(x, ButtonTop, ButtonWidth, ButtonHeight);
                parts.Add(b.gameObject);
                nav.Add(b);
                if (!quit) back = b;
                x += ButtonWidth + ButtonGap;
            }
            nav.Focus(back);
            GameLog.Info(LogChannel.Story, "End card: Quit asks first");
        }
    }
}
