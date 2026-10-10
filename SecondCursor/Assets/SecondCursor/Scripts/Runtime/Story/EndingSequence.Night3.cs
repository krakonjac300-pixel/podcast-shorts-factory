// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
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
    /// Night 3's endings (expansion spec 6.2). SHRED and KEEP: the monitor dies after the final image on CAM 03
    /// (played by the director), and in the dark the lines are typed by whoever speaks them (the player's own
    /// arrow, Ellen, or both taking turns letter by letter). LOG OFF: the session closes, the lobby camera shows
    /// the morning with nobody leaving, the log is typed in the BIOS colour, then Ellen alone. Any of them can
    /// end on the CAM 00 stinger before the card.
    /// </summary>
    public sealed partial class EndingSequence
    {
        const string Entity = "entity", Casey = "casey", Both = "both", SystemVoice = "system";

        CursorAgent _caseyAgent;
        CursorView _caseyView;
        PixelText _systemText;
        readonly List<GameObject> _temp = new List<GameObject>();

        void ClearNight3()
        {
            _heaterOn = false;
            if (_g.PlayerView != null) _g.PlayerView.ShowEntityPalette = false;
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
            if (_caseyView != null) Object.Destroy(_caseyView.gameObject);
            _caseyView = null;
            _caseyAgent = null;
            if (_g.CameraRig != null)
            {
                _g.CameraRig.AdminPointer = -1f;
                _g.CameraRig.SetViewing(_g.Apps.Find<Apps.CameraApp>() != null);
            }
        }

        IEnumerator RunNight3()
        {
            var g = _g;
            g.Flags.Set(Flags.Ending);
            g.Entity.Brain.Enabled = false;
            g.Entity.Interrupt();
            if (g.Gary != null)
            {
                g.Gary.Interrupt();
                g.Gary.SetPresent(false, 0.3f);
            }
            g.Player.Enabled = false;
            GameLog.Info(LogChannel.Story, "Ending sequence " + _spec.Id);

            if (_spec.Kind == EndingKind.LogOff) yield return LogOffOpening();
            else yield return PowerDown();
            // M4: in the SHRED dark the heater clicks about once a second until the card ("the heater is clicking").
            if (_spec.Kind == EndingKind.Shred)
            {
                _heaterOn = true;
                g.CoroutineHost.StartCoroutine(HeaterTicks());
            }

            var room = Room();
            yield return ShowResolution(room);
            if (Core.Game.EndingResult.For(_spec.Id) == Core.Game.EndingResultKind.Lost)
            {
                // Keep the record available from the title, without delaying the retry behind an epilogue.
                if (_spec.RecordPage != null && _spec.RecordPage.Count > 0) SaveSystem.SaveRecord(_spec.RecordPage, _spec.Id);
                g.Entity.SetPresent(false, 0f);
                yield return ShowCard(room);
                yield break;
            }
            if (_spec.SystemLines != null && _spec.SystemLines.Length > 0) yield return TypeSystemLines(room, _spec.SystemLines);
            yield return TypeSpoken(room, _spec.Lines ?? System.Array.Empty<string>(), _spec.Speakers);
            if (_spec.Stinger) yield return Cam00Stinger(room);
            _heaterOn = false;
            if (_spec.RecordPage != null && _spec.RecordPage.Count > 0) yield return ShowRecordPage(room);
            yield return ShowCard(room);
        }

        /// <summary>
        /// Phase Q2 (T2): the Retention Record fills the screen until Continue (after the card's own 1.5 s guard, so keys still being
        /// typed at the Jotter never skip it), and is kept for Records.
        /// </summary>
        IEnumerator ShowRecordPage(RectTransform room)
        {
            var g = _g;
            var c = g.Content;
            g.Audio.Play("end_tone", 0.6f);
            var page = RecordView.Page(room, _spec.RecordPage, k => c.Text(k));
            SaveSystem.SaveRecord(_spec.RecordPage, _spec.Id);
            GameLog.Info(LogChannel.Story, "Retention Record shown (" + _spec.RecordPage.Count + " rows)");
            g.Player.Enabled = true;
            g.Player.Visible = true;
            yield return Waits.Seconds(1.5f);
            bool done = false;
            var nav = new MenuNav(g) { RingColor = Palette.BiosBright };
            var next = UiButton.Create(page, c.Text("record.next"), a => done = true, "button:RecordNext", true);
            ((RectTransform)next.transform).At((ScreenRig.Width - 150) / 2, 480, 150, 24);
            nav.Add(next);
            nav.Focus(next);
            while (!done)
            {
                nav.Tick();
                yield return null;
            }
            Object.Destroy(page.gameObject);
        }

        bool _heaterOn;

        IEnumerator HeaterTicks()
        {
            while (_heaterOn)
            {
                _g.Audio.Play("key_tap", 0.16f, Random.Range(0.5f, 0.56f), 0.35f);
                float next = Time.time + Random.Range(0.9f, 1.1f);
                while (_heaterOn && Time.time < next) yield return null;
            }
        }

        /// <summary>The black screen everything after the power down happens on (nothing behind it is clickable).</summary>
        RectTransform Room()
        {
            var g = _g;
            var black = UIBuilder.Rect("Black Room", g.Layers.Fullscreen).Stretch();
            _room = black;
            var bg = black.gameObject.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;
            UIBuilder.Hit(black.gameObject, "ending");
            g.Windows.CloseAll();
            g.Fx.SetBlack(false);
            if (g.Fx.IsPoweredOff) g.Fx.PowerOn();
            return black;
        }

        /// <summary>
        /// LOG OFF: every window closes, the log-on colour fills the screen with "Session closed", the tube
        /// goes off, then the lobby camera at 7:02 in the morning light: nobody crosses it.
        /// </summary>
        IEnumerator LogOffOpening()
        {
            var g = _g;
            var c = g.Content;
            g.Player.Visible = false;
            g.Entity.SetPresent(false, 0.2f);
            g.Windows.CloseAll();
            g.Audio.StopAllLoops(0.6f);
            GameLog.Info(LogChannel.Story, "Ending: session closed");
            var panel = TempRect("Logged Off", g.Layers.Fullscreen);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = Palette.DesktopA;
            bg.raycastTarget = false;
            UIBuilder.Hit(panel.gameObject, "ending");
            var done = UIBuilder.Text(panel, c.Text("logoff.done"), Palette.TitleText, true);
            done.rectTransform.Stretch(0, 0, 0, 0);
            done.Align = TextAlign.Center;
            done.VAlign = TextVAlign.Middle;
            g.Audio.Play("sys_startup", 0.5f, 0.8f);
            yield return Waits.Seconds(3f);
            g.Audio.Play("crt_off");
            g.Fx.SetBlack(true);
            yield return Waits.Seconds(1.5f);
            Object.Destroy(panel.gameObject);
            _temp.Remove(panel.gameObject);

            // The lobby at 7:02: the glass doors are bright, the lamp is off, and nobody leaves.
            var rig = g.CameraRig;
            if (rig != null)
            {
                GameLog.Info(LogChannel.Story, "Ending: the lobby at 7:02");
                g.Clock.Set(7, 2);
                rig.DawnLevel = 0.8f;
                rig.Figure = CameraFeed.FigureStage.None;
                rig.FeedMinutes = g.Clock.ExactMinutes;
                var feed = FullscreenFeed(ContentIds.Cam01, out var time);
                g.Fx.SetBlack(false);
                float t = 0f;
                while (t < 5f)
                {
                    t += Time.deltaTime;
                    time.text = GameClock.FormatCamera(7 * 60 + 2 + t / 60.0);
                    yield return null;
                }
                g.Fx.SetBlack(true);
                Object.Destroy(feed.gameObject);
                _temp.Remove(feed.gameObject);
                rig.SetViewing(false);
                yield return Waits.Seconds(0.8f);
            }
        }

        /// <summary>A camera filling the screen (4:3 in the middle, black bars), with its label and a time overlay.</summary>
        RectTransform FullscreenFeed(string camId, out PixelText time)
        {
            var g = _g;
            var rig = g.CameraRig;
            var holder = TempRect("Feed " + camId, g.Layers.Fullscreen);
            var bg = holder.gameObject.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;
            UIBuilder.Hit(holder.gameObject, "ending");
            rig.SetCamera(camId);
            rig.SetViewing(true);
            var raw = UIBuilder.Raw(holder, rig.Feed, "Feed");
            raw.rectTransform.At((ScreenRig.Width - 720) / 2, 0, 720, ScreenRig.Height);
            raw.color = new Color(0.86f, 0.93f, 0.88f, 1f);
            var cam = g.Content.Camera(camId);
            var label = UIBuilder.Text(holder, cam != null ? cam.label : camId, Palette.BiosBright, true);
            label.Scale = 2;
            label.Shadow = true;
            label.rectTransform.At((ScreenRig.Width - 720) / 2 + 20, 20, 500, 24);
            time = UIBuilder.Text(holder, "", Palette.BiosBright);
            time.Scale = 2;
            time.Shadow = true;
            time.Align = TextAlign.Right;
            time.rectTransform.At((ScreenRig.Width - 720) / 2 + 220, ScreenRig.Height - 44, 480, 24);
            return holder;
        }

        RectTransform TempRect(string name, RectTransform parent)
        {
            var rt = UIBuilder.Rect(name, parent).Stretch();
            _temp.Add(rt.gameObject);
            return rt;
        }

        /// <summary>The system's own log, left-aligned in the BIOS colour at 20 characters a second, no cursor.</summary>
        IEnumerator TypeSystemLines(RectTransform room, string[] lines)
        {
            var g = _g;
            GameLog.Info(LogChannel.Story, "Ending: the log");
            var text = UIBuilder.Text(room, "", Palette.BiosText);
            _systemText = text;
            text.rectTransform.At(60, 60, ScreenRig.Width - 120, 80);
            var sb = new System.Text.StringBuilder();
            g.Audio.Play("hdd_seek", 0.5f);
            foreach (var line in lines)
            {
                foreach (char ch in line)
                {
                    sb.Append(ch);
                    text.text = sb.ToString();
                    yield return Waits.Seconds(1f / 20f);
                }
                sb.Append('\n');
                text.text = sb.ToString();
                g.Audio.Play("hdd_seek", 0.35f);
                yield return Waits.Seconds(0.9f);
            }
            yield return Waits.Seconds(1.5f);
        }

        /// <summary>
        /// The lines typed in the dark, each by its speaker: Ellen's cursor, the player's own arrow (SHRED: your
        /// cursor, in her colour of text; KEEP: your arrow drawn in her palette), or both taking turns one letter
        /// each. Whoever is not typing waits at their side.
        /// </summary>
        IEnumerator TypeSpoken(RectTransform room, string[] lines, string[] speakers)
        {
            var g = _g;
            bool ellen = false, casey = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string who = SpeakerAt(speakers, i);
                ellen |= who == Entity || who == Both;
                casey |= who == Casey || who == Both;
            }
            float top = ScreenRig.Height - 200f;
            var ellenHome = new Vector2(ScreenRig.Width * 0.24f, top - 6f);
            var caseyHome = new Vector2(ScreenRig.Width * 0.76f, top - 6f);
            if (casey) ShowCasey(caseyHome);
            if (ellen)
            {
                g.Entity.Teleport(_spec.Kind == EndingKind.Keep ? ellenHome : new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.62f));
                g.Entity.State = Core.Entity.EntityState.Communicating;
                yield return g.Entity.Appear(null, 1.5f, false);
            }
            else if (casey)
            {
                yield return Waits.Seconds(1.5f);
            }
            yield return Waits.Seconds(1.2f);

            var text = UIBuilder.Text(room, "", Palette.EntityText, true);
            text.Scale = 2;
            text.rectTransform.At(80, 200, ScreenRig.Width - 160, 240);
            text.Align = TextAlign.Center;
            var sb = new System.Text.StringBuilder();
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                string who = SpeakerAt(speakers, lineIndex);
                float y = top - lineIndex * text.LineHeightPx - 3f;
                var current = new System.Text.StringBuilder();
                int letter = 0;
                foreach (char ch in line)
                {
                    sb.Append(ch);
                    current.Append(ch);
                    text.text = sb.ToString();
                    bool ellenTypes = who == Entity || (who == Both && letter % 2 == 0);
                    float w = PixelFont.Measure(current.ToString(), 0, true, text.Scale).x;
                    var caret = new Vector2(ScreenRig.Width * 0.5f + w * 0.5f + 5f, y) + Random.insideUnitCircle * 0.8f;
                    if (ellenTypes)
                    {
                        g.Entity.Teleport(caret);
                        if (casey) MoveCasey(new Vector2(caseyHome.x, y));
                    }
                    else
                    {
                        MoveCasey(caret);
                        if (ellen && _spec.Kind == EndingKind.Keep) g.Entity.Teleport(new Vector2(ellenHome.x, y));
                    }
                    float pan = Audio.AudioManager.PanFor(ellenTypes ? g.EntityAgent.Position.x : caret.x);
                    g.Audio.Play(ch == ' ' ? "key_space" : "key_tap", 0.9f, Random.Range(0.9f, 1.05f), pan);
                    // M4: on the one frame your own arrow types the C of your name, it wears her colours.
                    if (_spec.Kind == EndingKind.Shred && !ellenTypes && letter == line.Length - 1 && line.EndsWith(" C") && g.PlayerView != null)
                    {
                        g.PlayerView.ShowEntityPalette = true;
                        yield return null;
                        g.PlayerView.ShowEntityPalette = false;
                    }
                    letter++;
                    yield return Waits.Seconds(ch == ' ' ? 0.16f : Random.Range(0.07f, 0.16f));
                }
                yield return Waits.Seconds(1.6f);
                sb.Append('\n');
                text.text = sb.ToString();
            }
            // Phase K (finding 17): the last line stays at full strength for 4.1 s (1.6 s above and this) before it fades.
            GameLog.Info(LogChannel.Story, "Ending: last line typed");
            yield return Waits.Seconds(2.5f);
            if (ellen) g.Entity.SetPresent(false, 1.5f);
            if (casey && _caseyView != null) _caseyAgent.Visible = false;
            if (_spec.Kind == EndingKind.Shred) g.Player.Visible = false;
            float a = 1f;
            while (a > 0f)
            {
                a -= Time.deltaTime * 0.8f;
                text.color = new Color(0.91f, 0.9f, 0.87f, Mathf.Max(0f, a));
                if (_systemText != null) _systemText.color = new Color(0.72f, 0.72f, 0.69f, Mathf.Max(0f, a));
                yield return null;
            }
            yield return Waits.Seconds(1f);
        }

        static string SpeakerAt(string[] speakers, int i) =>
            speakers != null && i < speakers.Length && !string.IsNullOrEmpty(speakers[i]) ? speakers[i] : Entity;

        /// <summary>
        /// The player's arrow in the dark, moved by script (input stays off). SHRED shows your own cursor; KEEP
        /// draws it in her palette, beside hers.
        /// </summary>
        void ShowCasey(Vector2 at)
        {
            var g = _g;
            if (_spec.Kind == EndingKind.Keep)
            {
                _caseyAgent = new CursorAgent(AgentKind.Entity, "Casey") { Position = at, Visible = true, Enabled = false };
                _caseyView = CursorView.Create(g.Layers.Cursors, _caseyAgent, true);
                return;
            }
            g.Player.Enabled = false;
            g.Player.Position = at;
            g.Player.Visible = true;
        }

        void MoveCasey(Vector2 to)
        {
            if (_caseyAgent != null) _caseyAgent.Position = to;
            else _g.Player.Position = to;
        }

        /// <summary>
        /// CAM 00 (the operator override was saved): four seconds of Admin 1, the Custodian seated at a glowing CRT,
        /// head bowed; in the last second a tiny bright point crosses its screen.
        /// </summary>
        IEnumerator Cam00Stinger(RectTransform room)
        {
            var g = _g;
            var rig = g.CameraRig;
            if (rig == null) yield break;
            GameLog.Info(LogChannel.Story, "Ending: CAM 00 stinger");
            rig.Figure = CameraFeed.FigureStage.None;
            rig.AdminPointer = -1f;
            var feed = FullscreenFeed(ContentIds.Cam00, out var time);
            // Phase M: a burst at the cut, the feed's own hiss for its four seconds, and one click in that room when the point stops.
            g.Audio.Play("static_burst", 0.8f);
            g.Audio.PlayLoop("camera_static", 0.12f, 0.3f);
            float t = 0f;
            bool clicked = false;
            while (t < 4f)
            {
                t += Time.deltaTime;
                time.text = GameClock.FormatCamera(g.Clock.ExactMinutes + t / 60.0);
                rig.AdminPointer = t >= 3f ? Mathf.Clamp01(t - 3f) : -1f;
                if (!clicked && t >= 3.95f)
                {
                    clicked = true;
                    g.Audio.Play("mouse_click", 0.5f, 0.8f);
                }
                yield return null;
            }
            g.Audio.StopLoop("camera_static", 0.1f);
            rig.AdminPointer = -1f;
            Object.Destroy(feed.gameObject);
            _temp.Remove(feed.gameObject);
            rig.SetViewing(false);
            g.Audio.Play("low_thump", 0.6f);
            yield return Waits.Seconds(0.8f);
        }
    }
}
#endif
