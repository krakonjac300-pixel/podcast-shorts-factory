using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>The title's sub-screens: Night Select, Records and Credits (expansion spec 8.1).</summary>
    public sealed partial class TitleMenu
    {
        const float CreditsScrollSpeed = 160f;
        ScrollArea _credits;

        void TickScreen()
        {
            if (_screen != Screen.Credits || _credits == null) return;
            var input = _g.Input;
            if (input.KeyHeld(GameKey.Down)) _credits.ScrollBy(CreditsScrollSpeed * Time.unscaledDeltaTime);
            if (input.KeyHeld(GameKey.Up)) _credits.ScrollBy(-CreditsScrollSpeed * Time.unscaledDeltaTime);
        }

        void Heading(string text) => Label(text, Palette.BiosBright, 0, 44, ScreenRig.Width, 24, TextAlign.Center, true, 2);

        UiButton BackButton(int y)
        {
            var back = MenuButton(_g.Content.Text("title.back"), "title:back", a => ShowMain(), (ScreenRig.Width - 120) / 2, y, 120, RowHeight);
            _nav.Back = ShowMain;
            return back;
        }

        // ------------------------------------------------------------------ Night Select

        void ShowNightSelect()
        {
            Begin(Screen.NightSelect);
            var c = _g.Content;
            var d = SaveSystem.Load();
            Heading(c.Text("select.title"));
            int unlocked = Math.Min(d.nightUnlocked, GameBootstrap.MaxNight);
            int y = 150;
            UiButton focus = null, first = null;
            for (int n = 1; n <= SaveData.Nights; n++)
            {
                int night = n;
                bool open = night <= unlocked;
                string label = c.Text("select.night" + n) + (open ? "" : "    " + c.Text("select.locked"));
                var row = MenuButton(label, "select:night" + n, a => PickNight(night), (ScreenRig.Width - 440) / 2, y, 440, 28);
                row.Label.Align = TextAlign.Left;
                row.Label.rectTransform.Stretch(10, 0, 120, 0);
                float best = d.bestNightSeconds[n - 1];
                var time = UIBuilder.Text(row.transform, best > 0f ? c.Format("select.best", TitleMenuModel.FormatDuration(best)) : c.Text("select.nobest"), open ? Palette.Text : Palette.TextDisabled);
                time.rectTransform.Stretch(300, 0, 10, 0);
                time.Align = TextAlign.Right;
                time.VAlign = TextVAlign.Middle;
                row.Enabled = open;
                if (open && first == null) first = row;
                if (open && night == d.currentNight) focus = row;
                y += 36;
            }

            var seen = new List<string>();
            foreach (var id in AchievementIds.Night3Endings)
                if (Array.IndexOf(d.endingsSeen, id) >= 0) seen.Add(c.Text("records.ending." + id));
            Label(c.Format("select.endings", seen.Count), Palette.BiosText, 0, 280, ScreenRig.Width, 12);
            if (seen.Count > 0) Label(c.Format("select.seen", string.Join(", ", seen)), Palette.BiosText, 0, 296, ScreenRig.Width, 12);
            BackButton(360);
            _nav.Focus(focus ?? first);
        }

        void PickNight(int night)
        {
            if (_leaving) return;
            var d = SaveSystem.Load();
            if (d.checkpoint != null && d.checkpoint.valid)
            {
                ShowSelectConfirm(night);
                return;
            }
            GameLog.Info(LogChannel.Player, "Night Select: night " + night);
            Leave(() => GameBootstrap.StartFromMenu(night, false, true));
        }

        void ShowSelectConfirm(int night)
        {
            Begin(Screen.SelectConfirm);
            var c = _g.Content;
            Label(c.Text("select.replace.confirm"), Palette.BiosBright, 0, 250, ScreenRig.Width, 30);
            int x = ScreenRig.Width / 2;
            MenuButton(c.Text("title.yes"), "title:yes", a =>
            {
                GameLog.Info(LogChannel.Player, "Night Select: night " + night + " (checkpoint replaced)");
                Leave(() =>
                {
                    SaveSystem.ClearCheckpoint();
                    GameBootstrap.StartFromMenu(night, false, true);
                });
            }, x - 110, 310, 100, RowHeight);
            var no = MenuButton(c.Text("title.no"), "title:no", a => ShowNightSelect(), x + 10, 310, 100, RowHeight);
            _nav.Focus(no);
            _nav.Back = ShowNightSelect;
        }

        // ------------------------------------------------------------------ Records

        void ShowRecords()
        {
            Begin(Screen.Records);
            var c = _g.Content;
            var d = SaveSystem.Load();
            Heading(c.Text("records.title"));

            // Left: endings, shifts, the achievement count.
            const int left = 70, width = 340;
            int y = 96;
            Label(c.Text("records.endings"), Palette.BiosBright, left, y, width, 12, TextAlign.Left, true);
            y += 18;
            foreach (var id in AchievementIds.Night3Endings)
            {
                bool seen = Array.IndexOf(d.endingsSeen, id) >= 0;
                Label(seen ? c.Text("records.ending." + id) : c.Text("records.unknown"), Palette.BiosText, left + 10, y, width, 12, TextAlign.Left);
                y += 14;
            }
            y += 14;
            Label(c.Text("records.stats"), Palette.BiosBright, left, y, width, 12, TextAlign.Left, true);
            y += 18;
            Label(c.Format("records.tugs", d.tugWinsTotal, d.tugLossesTotal), Palette.BiosText, left + 10, y, width, 12, TextAlign.Left);
            y += 14;
            for (int n = 1; n <= SaveData.Nights; n++)
            {
                float best = d.bestNightSeconds[n - 1], total = d.nightSeconds[n - 1];
                string notime = c.Text("records.notime");
                Label(c.Format("records.night", n, best > 0f ? TitleMenuModel.FormatDuration(best) : notime, total > 0f ? TitleMenuModel.FormatDuration(total) : notime),
                    Palette.BiosText, left + 10, y, width, 12, TextAlign.Left);
                y += 14;
            }
            y += 14;
            Label(c.Format("records.achievements", AchievementIds.CountUnlocked(d.achievements), AchievementIds.All.Length), Palette.BiosBright, left, y, width, 12, TextAlign.Left, true);
            // Phase Q2 (D9, T2): the profile as the record keeps it, and the last Retention Record.
            string profile = ProfileValue(d);
            if (profile.Length > 0)
            {
                y += 24;
                Label(c.Format("records.profile", profile), Palette.BiosText, left, y, width, 12, TextAlign.Left);
            }
            if (d.lastRecord.Length > 0)
            {
                y += 20;
                var view = MenuButton(c.Text("records.record"), "records:record", a => ShowRecord(), left, y, 220, RowHeight);
                view.Label.Align = TextAlign.Left;
            }

            // Right: the 19 achievements in a list box; the focused one's description underneath.
            const int listX = 450, listW = 440, rowH = 16;
            var box = UIBuilder.Bevel(_content, BevelStyle.Sunken, "Achievements");
            box.rectTransform.At(listX, 92, listW, AchievementIds.All.Length * rowH + 8);
            int ry = 96;
            UiButton first = null;
            var descriptions = new Dictionary<UiButton, string>();
            foreach (var a in AchievementIds.All)
            {
                bool has = Array.IndexOf(d.achievements, a.Id) >= 0;
                string name = has || !a.Hidden ? c.Text(a.NameKey) : c.Text("records.unknown");
                var row = MenuButton((has ? "[x] " : "[ ] ") + name, "records:" + a.Id, null, listX + 4, ry, listW - 8, rowH);
                row.Flat = true;
                row.ClickSound = "";
                row.Label.Align = TextAlign.Left;
                row.Label.rectTransform.Stretch(4, 0, 4, 0);
                if (!has) row.Label.color = Palette.TextDisabled;
                descriptions[row] = has || !a.Hidden ? c.Text(a.DescKey) : c.Text("records.hidden");
                if (first == null) first = row;
                ry += rowH;
            }
            var desc = Label("", Palette.BiosText, listX, ry + 14, listW, 24, TextAlign.Left);
            desc.Wrap = true;
            _onFocus = b => desc.text = b != null && descriptions.TryGetValue(b, out var text) ? text : "";
            BackButton(496);
            _nav.Focus(first);
        }

        /// <summary>Retention profile 214: the last record's ending (HOLDER, KEPT, NOT FOUND), else the run's percentage so far ("" = nothing yet).</summary>
        string ProfileValue(SaveData d)
        {
            var c = _g.Content;
            if (d.lastRecordEnding.Length > 0 && c.HasText("records.profile." + d.lastRecordEnding)) return c.Text("records.profile." + d.lastRecordEnding);
            int done = Math.Min(d.lastCompletedNight, SaveData.Nights);
            if (done <= 0) return "";
            bool hid = Array.IndexOf(d.memory?.flags ?? Array.Empty<string>(), Core.Story.MemoryFlags.N2Hid214) >= 0;
            return c.Format("record.pct", CaptureProfile.Percent(done, hid, ""));
        }

        /// <summary>Phase Q2 (T2): the last Retention Record, as it was shown after its ending.</summary>
        void ShowRecord()
        {
            Begin(Screen.Record);
            var c = _g.Content;
            var d = SaveSystem.Load();
            var rows = new List<RecordRow>();
            foreach (var packed in d.lastRecord) rows.Add(RecordRow.Unpack(packed));
            RecordView.Page(_content, rows, k => c.Text(k));
            var back = MenuButton(c.Text("title.back"), "title:back", a => ShowRecords(), (ScreenRig.Width - 120) / 2, 480, 120, RowHeight);
            _nav.Back = ShowRecords;
            _nav.Focus(back);
        }

        // ------------------------------------------------------------------ Credits

        void ShowCredits()
        {
            Begin(Screen.Credits);
            var c = _g.Content;
            Heading(c.Text("credits.title"));
            string text = c.Text("credits.body");
#if STEAMWORKS_NET
            // Steamworks.NET's MIT license must ship with the game (copied into strings.json from the package's LICENSE).
            if (c.HasText("credits.steamworks")) text += "\n\n" + c.Text("credits.notices") + "\n\n" + c.Text("credits.steamworks");
#endif
            const int w = 560, h = 360;
            var frame = UIBuilder.Rect("Credits Frame", _content).At((ScreenRig.Width - w) / 2, 90, w, h);
            _credits = ScrollArea.Create(frame, "Credits Scroll");
            ((RectTransform)_credits.transform).Stretch();
            var body = UIBuilder.Text(_credits.Content, text, Palette.BiosText);
            body.Wrap = true;
            int width = w - 24;
            var size = PixelFont.Measure(text, width, false, 1);
            body.rectTransform.At(6, 4, width, size.y + 4);
            _credits.ContentHeight = size.y + 12;
            BackButton(470);
            _nav.Focus(_nav.Buttons.Count > 0 ? _nav.Buttons[0] : null);
        }
    }
}
