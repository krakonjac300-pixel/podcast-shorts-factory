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
    public enum EndCardButtons { None = 0, Continue = 1, Title = 2, NightSelect = 4, Quit = 8, Wishlist = 16, Retry = 32, RestartRun = 64 }

    /// <summary>
    /// The card after an ending (expansion spec 6.3): the night's title and subtitle, and its buttons. Nights 1 and 2:
    /// Continue to the next night, Title, Quit. Night 3: Title, Night Select, Quit and the thanks line. The demo's
    /// WISHLIST card: Wishlist (when the store can open), Title, Quit. Keyboard and mouse both work; every button that
    /// starts something starts a run that counts.
    /// </summary>
    public static class EndCard
    {
        const int ButtonWidth = 150, ButtonHeight = 24, ButtonGap = 12, ButtonTop = 480;
        /// <summary>Seconds the card shows before its buttons take any input.</summary>
        const float ButtonsAfter = 1.5f;

        public static Color32 ResultColor(EndingSpec spec)
        {
            switch (Core.Game.EndingResult.For(spec.Id))
            {
                case Core.Game.EndingResultKind.Lost: return new Color32(235, 143, 129, 255);
                case Core.Game.EndingResultKind.Escaped: return new Color32(166, 221, 179, 255);
                default: return Palette.BiosBright;
            }
        }

        /// <summary>Phase S: one paragraph of the card's body, at the card's reading size.</summary>
        struct Para
        {
            public string Text, Name;
            public Color32 Color;
            public bool Bold;
            public int Before, After;
        }

        static int ParaHeight(string value, float f, bool bold)
        {
            const int margin = 60;
            return Mathf.Max(Mathf.CeilToInt(12 * f), PixelFont.Measure(value, ScreenRig.Width - margin * 2, bold, f).y);
        }

        static int Paragraph(RectTransform parent, string value, int y, Color32 color, bool bold, string name, float f = 1f)
        {
            if (string.IsNullOrEmpty(value)) return y;
            const int margin = 60;
            int width = ScreenRig.Width - margin * 2;
            var text = UIBuilder.Text(parent, value, color, bold, name);
            text.Wrap = true;
            text.Factor = f;
            text.Align = TextAlign.Center;
            int height = ParaHeight(value, f, bold);
            text.rectTransform.At(margin, y, width, height);
            return y + height + 5;
        }

        /// <summary>The spec's buttons, or the defaults its kind of card gets.</summary>
        public static EndCardButtons ButtonsFor(EndingSpec spec)
        {
            if (Core.Game.EndingResult.For(spec.Id) == Core.Game.EndingResultKind.Lost)
                return EndCardButtons.Retry | EndCardButtons.RestartRun | EndCardButtons.Title | EndCardButtons.Quit;
            if (spec.Buttons != EndCardButtons.None) return spec.Buttons;
            if (spec.DemoCard) return EndCardButtons.Wishlist | EndCardButtons.Title | EndCardButtons.Quit;
            if (spec.ContinueNight > 0 && !spec.FinalCard && spec.ContinueNight <= GameBootstrap.MaxNight)
                return EndCardButtons.Continue | EndCardButtons.Title | EndCardButtons.Quit;
            var b = EndCardButtons.Title | EndCardButtons.Quit;
            if (GameBootstrap.NightSelectAvailable) b |= EndCardButtons.NightSelect;
            return b;
        }

        /// <summary>The three Night 3 endings as slots: the ones seen by name in bright, the others as "???" (an empty slot is a reason to play again).</summary>
        static void EndingSlots(RectTransform parent, Core.Content.ContentDatabase c, string[] seenIds, int y, float f = 1f)
        {
            var ids = Core.Game.AchievementIds.Night3Endings;
            string[] keys = { "end.shred.title", "end.keep.title", "end.logoff.title" };
            int slotW = Mathf.Min(300, Mathf.CeilToInt(140 * f)), gap = 14;
            int x = (ScreenRig.Width - (ids.Length * slotW + (ids.Length - 1) * gap)) / 2;
            for (int i = 0; i < ids.Length; i++)
            {
                bool seen = seenIds != null && Array.IndexOf(seenIds, ids[i]) >= 0;
                string label = seen ? "[x] " + c.Text(keys[i]) : "[ ] " + c.Text("end.slot.empty", "???");
                var slot = UIBuilder.Text(parent, label, seen ? Palette.BiosBright : new Color32(0x6A, 0x6A, 0x66, 0xFF), seen, "Ending Slot " + i);
                slot.Factor = f;
                slot.rectTransform.At(x + i * (slotW + gap), y, slotW, Mathf.CeilToInt(12 * f));
                slot.Align = TextAlign.Center;
            }
        }

        public static IEnumerator Run(GameServices g, EndingSpec spec, RectTransform parent)
        {
            var c = g.Content;
            g.Audio.Play("end_tone");
            g.Audio.Play("low_thump", 0.8f);

            var nav = new MenuNav(g) { RingColor = Palette.BiosBright };
            var list = new List<UiButton>();
            var defs = ButtonDefs(g, spec, parent, nav, list);
            var plan = PlanButtons(defs);

            var resultKey = Core.Game.EndingResult.HeadingKey(spec.Id);
            bool sacrifice = spec.Id == Core.Content.ContentIds.EndingN3Shred;
            if (resultKey != null && !sacrifice)
            {
                var result = UIBuilder.Text(parent, c.Text(resultKey), ResultColor(spec), true, "Player Result");
                result.Scale = 3;
                result.Align = TextAlign.Center;
                result.rectTransform.At(40, 48, ScreenRig.Width - 80, 36);
            }
            var title = UIBuilder.Text(parent, c.Text(spec.TitleKey), Palette.BiosBright, true);
            title.Scale = 4;
            title.rectTransform.At(0, 98, ScreenRig.Width, 50);
            title.Align = TextAlign.Center;

            // Phase S (Large text tester: the end card carries the plot in 8 px type): the card's body follows the Reading text size, at the
            // largest size at which the whole card still fits above its buttons (Large, then Medium, then Normal).
            var paras = new List<Para>();
            void AddPara(string text, Color32 color, bool bold, string name, int before = 0, int after = 0)
            {
                if (!string.IsNullOrEmpty(text)) paras.Add(new Para { Text = text, Color = color, Bold = bold, Name = name, Before = before, After = after });
            }
            if (resultKey != null) AddPara(c.Text(Core.Game.EndingResult.ExplanationKey(spec.Id)), Palette.BiosBright, true, "Player Result Explanation", 0, 8);
            // Phase Q4 (R8): the cause line is the one line that explains the ending, so it is bright and bold.
            AddPara(spec.Outcome, Palette.BiosBright, true, "Outcome Cause");
            if (spec.OutcomeDetail != null)
            {
                int shown = 0;
                foreach (var key in spec.OutcomeDetail)
                {
                    if (string.IsNullOrEmpty(key) || (spec.DemoCard && shown >= 1)) continue;
                    AddPara(key, Palette.BiosText, false, "Outcome Detail");
                    shown++;
                }
            }
            AddPara(spec.KeptLine, Palette.BiosText, false, "Kept Record");
            if (!spec.DemoCard) AddPara(spec.CarryLine, Palette.BiosText, false, "Next Night");
            bool hasRows = spec.RecordRows != null && spec.RecordRows.Count > 0;
            string thanksText = !spec.DemoCard && !string.IsNullOrEmpty(spec.ThanksKey) ? c.Text(spec.ThanksKey) : null;
            string hintText = null;
            if (g.Night == 3 && SaveSystem.Load().FinalDecisionForReplay() != null)
                hintText = c.Text("end.card.return.final.hint", "Return keeps earlier choices. Replay Night 3 starts the whole night.");
            else if (Core.Game.EndingResult.For(spec.Id) == Core.Game.EndingResultKind.Lost)
                hintText = c.Text(g.Night == 1 && SaveSystem.Load().CheckpointFor(1) != null ? "end.card.retry.checkpoint.hint" : "end.card.retry.hint");

            float rf = DisplaySettings.ReadingFactor, fit = 1f;
            for (float tryF = rf; tryF > 1f; tryF -= 0.5f)
            {
                if (BodyBottom(paras, spec, hasRows, thanksText, tryF) <= BodyLimit(plan, hintText, tryF)) { fit = tryF; break; }
            }
            var sub = UIBuilder.Text(parent, c.Text(spec.SubtitleKey), Palette.BiosText);
            sub.Factor = fit;
            sub.rectTransform.At(0, 154, ScreenRig.Width, Mathf.CeilToInt(12 * fit));
            sub.Align = TextAlign.Center;
            int y = BodyTop(fit);
            foreach (var p in paras)
            {
                y = Paragraph(parent, p.Text, y + p.Before, p.Color, p.Bold, p.Name, fit) + p.After;
                GameLog.Info(LogChannel.Story, "End card: " + p.Text);
            }
            PixelText cta = null;
            if (hasRows) y = RecordView.CardRows(parent, spec.RecordRows, y + 4, fit) + 6;
            if (spec.DemoCard)
            {
                int ctaY = Mathf.Max(290, y + 6);
                cta = UIBuilder.Text(parent, c.Text("end.card.cta"), new Color32(0xE8, 0xC4, 0x5A, 0xFF), true);
                cta.Scale = 3;
                cta.rectTransform.At(0, ctaY, ScreenRig.Width, 36);
                cta.Align = TextAlign.Center;
                var thanks = UIBuilder.Text(parent, c.Text("end.card.thanks"), Palette.BiosText);
                thanks.Factor = fit;
                thanks.rectTransform.At(0, Mathf.Min(ctaY + 46, plan.Top - 20), ScreenRig.Width, Mathf.CeilToInt(12 * fit));
                thanks.Align = TextAlign.Center;
            }
            else if (thanksText != null)
            {
                y = Paragraph(parent, thanksText, y + 10, Palette.BiosText, false, "Thanks", fit);
            }
            // The endings seen so far, under the thanks line: an invitation to Night Select. Phase Q4 (R8): three slots, not a count.
            if (spec.FinalCard) EndingSlots(parent, c, SaveSystem.Load().endingsSeen, y + 12, fit);
            if (hintText != null)
                Paragraph(parent, hintText, plan.Top - 6 - ParaHeight(hintText, fit, false), Palette.BiosText, false, "Retry Explanation", fit);

            g.Player.Enabled = true;
            g.Player.Visible = true;
            // Phase K: the card is read before it can be left. The fourth blind tester's typing (meant for a Jotter that had just gone)
            // reached the KEEP card, focused Quit and pressed it: the session ended before the card was seen. The buttons come a
            // moment later, and Quit asks first.
            yield return Waits.Seconds(ButtonsAfter);
            var buttons = Buttons(g, defs, plan, parent, nav, list);
            nav.Focus(buttons.Count > 0 ? buttons[0] : null);
            GameLog.Info(LogChannel.Story, "End card: " + spec.Id + " [" + ButtonsFor(spec) + "] text " + fit.ToString("0.0") + "x");
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

        /// <summary>Where the card's body starts under the subtitle at <paramref name="f"/>.</summary>
        static int BodyTop(float f) => 154 + Mathf.CeilToInt(12 * f) + 14;

        /// <summary>The y under the card's body when it is laid out at <paramref name="f"/> (the same flow as <see cref="Run"/>).</summary>
        static int BodyBottom(List<Para> paras, EndingSpec spec, bool hasRows, string thanksText, float f)
        {
            int y = BodyTop(f);
            foreach (var p in paras) y += p.Before + ParaHeight(p.Text, f, p.Bold) + 5 + p.After;
            if (hasRows) y += 4 + RecordView.CardRowsHeight(spec.RecordRows, f) + 6;
            if (spec.DemoCard) return Mathf.Max(290, y + 6) + 58;
            if (thanksText != null) y += 10 + ParaHeight(thanksText, f, false) + 5;
            if (spec.FinalCard) y += 12 + Mathf.CeilToInt(12 * f);
            return y;
        }

        /// <summary>The lowest y the body may reach: above the retry hint, or above the buttons.</summary>
        static int BodyLimit(ButtonPlan plan, string hintText, float f) =>
            hintText != null ? plan.Top - 6 - ParaHeight(hintText, f, false) - 6 : plan.Top - 8;

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

        /// <summary>Phase S: how the card's buttons are laid out at the reading size (rows, widths from their labels, top).</summary>
        struct ButtonPlan
        {
            public float Factor;
            public int Height, Top, Rows;
            public int[] Widths, RowOf;
        }

        static int ButtonWidthFor(string label, float f) => Mathf.Max(ButtonWidth, PixelFont.MeasureLine(label, true, f) + 16);

        static ButtonPlan PlanButtons(List<(string label, string id, Action<CursorAgent> click)> defs)
        {
            var plan = new ButtonPlan { Widths = new int[defs.Count], RowOf = new int[defs.Count] };
            int maxRow = ScreenRig.Width - 40;
            float rf = DisplaySettings.ReadingFactor, f = rf;
            for (; ; f -= 0.5f)
            {
                int row = 0, used = 0;
                for (int i = 0; i < defs.Count; i++)
                {
                    int w = ButtonWidthFor(defs[i].label, f);
                    if (used > 0 && used + ButtonGap + w > maxRow) { row++; used = 0; }
                    plan.Widths[i] = w;
                    plan.RowOf[i] = row;
                    used += (used > 0 ? ButtonGap : 0) + w;
                }
                plan.Rows = row + 1;
                if (plan.Rows <= 2 || f <= 1f) break;
            }
            plan.Factor = f;
            plan.Height = f > 1f ? Mathf.CeilToInt(PixelFont.GlyphHeight * f) + 12 : ButtonHeight;
            int total = plan.Rows * plan.Height + (plan.Rows - 1) * ButtonGap;
            plan.Top = Mathf.Min(ButtonTop - (plan.Rows > 1 ? 12 : 0), 530 - total);
            return plan;
        }

        static List<(string label, string id, Action<CursorAgent> click)> ButtonDefs(GameServices g, EndingSpec spec, RectTransform parent, MenuNav nav, List<UiButton> list)
        {
            var c = g.Content;
            var want = ButtonsFor(spec);
            if (!SteamBridge.CanOpenStore) want &= ~EndCardButtons.Wishlist;
            var defs = new List<(string label, string id, Action<CursorAgent> click)>();
            bool canReturn = g.Night == 3 && !spec.DemoCard && SaveSystem.Load().FinalDecisionForReplay() != null;
            if (canReturn)
                defs.Add((c.Text("end.card.return.final", "Return to final decision"), "button:ReturnFinal", a => GameBootstrap.ReturnToFinalDecision(g.RecordsArmed, g.FromNightSelect)));
            if (canReturn && (want & EndCardButtons.Retry) == 0)
                defs.Add((c.Text("end.card.replay.n3", "Replay Night 3"), "button:Retry", a =>
                {
                    SaveSystem.ClearCheckpoint();
                    GameBootstrap.StartFromMenu(3, false, true);
                }));
            if ((want & EndCardButtons.Retry) != 0)
            {
                int night = g.Night;
                bool resume = night == 1 && SaveSystem.Load().CheckpointFor(night) != null;
                defs.Add((resume ? c.Text("end.card.retry.checkpoint") : night == 3 ? c.Text("end.card.replay.n3", "Replay Night 3") : c.Format("end.card.retry", night), "button:Retry", a =>
                {
                    if (resume) GameBootstrap.RestartFromCheckpoint(night, g.RecordsArmed, g.FromNightSelect);
                    else
                    {
                        SaveSystem.ClearCheckpoint();
                        GameBootstrap.StartFromMenu(night, false, true);
                    }
                }));
            }
            if ((want & EndCardButtons.Continue) != 0)
            {
                int night = spec.ContinueNight;
                defs.Add((c.Format("end.card.continue", night), "button:Continue", a => GameBootstrap.StartFromMenu(night)));
            }
            if ((want & EndCardButtons.RestartRun) != 0 && g.Night > 1)
                defs.Add((c.Text("end.card.restart"), "button:RestartRun", a => AskRestartRun(g, parent, nav, list)));
            if ((want & EndCardButtons.Wishlist) != 0) defs.Add((c.Text("end.card.wishlist"), "button:Wishlist", a => SteamBridge.OpenStorePage()));
            if ((want & EndCardButtons.Title) != 0) defs.Add((c.Text("end.card.menu"), "button:Title", a => GameBootstrap.ToTitle()));
            if ((want & EndCardButtons.NightSelect) != 0) defs.Add((c.Text("end.card.select"), "button:NightSelect", a => GameBootstrap.ToNightSelect()));
            if ((want & EndCardButtons.Quit) != 0) defs.Add((c.Text("end.card.quit"), "button:Quit", a => AskQuit(g, parent, nav, list)));
            return defs;
        }

        static List<UiButton> Buttons(GameServices g, List<(string label, string id, Action<CursorAgent> click)> defs, ButtonPlan plan, RectTransform parent, MenuNav nav, List<UiButton> list)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var (label, id, click) = defs[i];
                int row = plan.RowOf[i], inRowTotal = 0, before = 0, count = 0;
                for (int k = 0; k < defs.Count; k++)
                {
                    if (plan.RowOf[k] != row) continue;
                    inRowTotal += plan.Widths[k] + (count > 0 ? ButtonGap : 0);
                    if (k < i) before += plan.Widths[k] + ButtonGap;
                    count++;
                }
                int x = (ScreenRig.Width - inRowTotal) / 2 + before;
                int top = plan.Top + row * (plan.Height + ButtonGap);
                var b = UiButton.Create(parent, label, click, id, id == "button:ReturnFinal" || id == "button:Retry" || id == "button:Continue" || id == "button:Wishlist");
                ((RectTransform)b.transform).At(x, top, plan.Widths[i], plan.Height);
                nav.Add(b);
                list.Add(b);
            }
            return list;
        }

        static void AskRestartRun(GameServices g, RectTransform parent, MenuNav nav, List<UiButton> buttons)
        {
            var c = g.Content;
            foreach (var button in buttons) button.gameObject.SetActive(false);
            var panel = UIBuilder.Rect("Restart Run Confirmation", parent).Stretch();
            var shade = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            shade.color = Color.black;
            shade.raycastTarget = false;
            UIBuilder.Hit(panel.gameObject, "restart-run-confirmation");
            float f = Mathf.Min(DisplaySettings.ReadingFactor, 1.5f);
            Paragraph(panel, c.Text("end.card.restart.ask"), 220, Palette.BiosBright, true, "Restart Run Explanation", f);
            nav.Clear();
            string backLabel = c.Text("end.card.restart.back"), startLabel = c.Text("end.card.restart.confirm");
            int bw = Mathf.Max(ButtonWidthFor(backLabel, f), ButtonWidthFor(startLabel, f)), bh = f > 1f ? Mathf.CeilToInt(PixelFont.GlyphHeight * f) + 12 : ButtonHeight;
            int bx = (ScreenRig.Width - (bw * 2 + ButtonGap)) / 2;
            var back = UiButton.Create(panel, backLabel, a =>
            {
                UnityEngine.Object.Destroy(panel.gameObject);
                nav.Clear();
                foreach (var button in buttons) { button.gameObject.SetActive(true); nav.Add(button); }
                nav.Focus(buttons[0]);
            }, "button:RestartBack");
            ((RectTransform)back.transform).At(bx, 300, bw, bh);
            var start = UiButton.Create(panel, startLabel, a =>
            {
                SaveSystem.NewGame();
                GameBootstrap.StartFromMenu(1);
            }, "button:RestartConfirm");
            ((RectTransform)start.transform).At(bx + bw + ButtonGap, 300, bw, bh);
            nav.Add(back);
            nav.Add(start);
            nav.Focus(back);
        }

        /// <summary>Quit asks first, like the pause menu's: the card's buttons give way to "Quit SECOND CURSOR?" with Back focused.</summary>
        static void AskQuit(GameServices g, RectTransform parent, MenuNav nav, List<UiButton> buttons)
        {
            var c = g.Content;
            foreach (var b in buttons) b.gameObject.SetActive(false);
            float f = Mathf.Min(DisplaySettings.ReadingFactor, 1.5f);
            int bh = f > 1f ? Mathf.CeilToInt(PixelFont.GlyphHeight * f) + 12 : ButtonHeight, top = Mathf.Min(ButtonTop, 530 - bh);
            var ask = UIBuilder.Text(parent, c.Text("end.card.quit.ask"), Palette.BiosBright, true);
            ask.Factor = f;
            ask.rectTransform.At(0, top - 10 - Mathf.CeilToInt(12 * f), ScreenRig.Width, Mathf.CeilToInt(12 * f));
            ask.Align = TextAlign.Center;
            var parts = new List<GameObject> { ask.gameObject };
            nav.Clear();
            string backLabel = c.Text("end.card.quit.back"), quitLabel = c.Text("end.card.quit");
            int bw = Mathf.Max(ButtonWidthFor(backLabel, f), ButtonWidthFor(quitLabel, f));
            int x = (ScreenRig.Width - (bw * 2 + ButtonGap)) / 2;
            UiButton back = null;
            foreach (var (label, id, quit) in new[] { (backLabel, "button:QuitBack", false), (quitLabel, "button:QuitYes", true) })
            {
                var b = UiButton.Create(parent, label, a =>
                {
                    if (quit) { PauseMenu.QuitGame(); return; }
                    foreach (var o in parts) UnityEngine.Object.Destroy(o);
                    nav.Clear();
                    foreach (var old in buttons) { old.gameObject.SetActive(true); nav.Add(old); }
                    nav.Focus(buttons.Count > 0 ? buttons[buttons.Count - 1] : null);
                }, id);
                ((RectTransform)b.transform).At(x, top, bw, bh);
                parts.Add(b.gameObject);
                nav.Add(b);
                if (!quit) back = b;
                x += bw + ButtonGap;
            }
            nav.Focus(back);
            GameLog.Info(LogChannel.Story, "End card: Quit asks first");
        }
    }
}
