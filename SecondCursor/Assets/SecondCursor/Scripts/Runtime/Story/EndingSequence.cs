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
    /// <summary>Which ending sequence plays (spec 6.2).</summary>
    public enum EndingKind
    {
        /// <summary>Nights 1 and 2: power down, the second cursor types in the dark, the card.</summary>
        Blackout,
        /// <summary>Night 3 SHRED: the player's own arrow types in the dark.</summary>
        Shred,
        /// <summary>Night 3 KEEP: Ellen and the player's arrow type together.</summary>
        Keep,
        /// <summary>Night 3 LOG OFF: log-off screen, the lobby at dawn, the log, Ellen alone.</summary>
        LogOff,
    }

    /// <summary>
    /// What an ending shows (expansion spec 6): the lines typed in the dark, an optional faint goodnight from
    /// Gary, and the card with its title, subtitle and buttons.
    /// </summary>
    public sealed class EndingSpec
    {
        public string Id = "";
        public EndingKind Kind = EndingKind.Blackout;
        /// <summary>Who types each line (entity, casey, both, system); null = the second cursor types them all.</summary>
        public string[] Speakers;
        /// <summary>Typed first, in the BIOS colour with no cursor (LOG OFF's log lines).</summary>
        public string[] SystemLines;
        /// <summary>The CAM 00 stinger plays before the card (the operator override was saved).</summary>
        public bool Stinger;
        /// <summary>A line under the subtitle ("" = none).</summary>
        public string ThanksKey = "";
        /// <summary>The last night's card: Title, Night Select and Quit, no Continue.</summary>
        public bool FinalCard;
        /// <summary>The card's buttons (None = the defaults for its kind of card, see <see cref="EndCard.ButtonsFor"/>).</summary>
        public EndCardButtons Buttons;
        /// <summary>Lines the second cursor types in the dark (null = story.json endingLines, Night 1).</summary>
        public string[] Lines;
        /// <summary>Typed after them by Gary's faint cursor, small and lowercase (Night 2 KEEP).</summary>
        public string[] GaryLines;
        public string TitleKey = "end.card.title";
        public string SubtitleKey = "end.card.subtitle";
        /// <summary>The demo's card: big title, WISHLIST NOW, Wishlist, Title and Quit.</summary>
        public bool DemoCard = true;
        /// <summary>Night offered by a "Continue to Night N" button (0 = none).</summary>
        public int ContinueNight;
        /// <summary>Phase H: one line under the subtitle saying how the night ended for you ("" = none).</summary>
        public string Outcome = "";
        /// <summary>Phase M: a climax's hit already killed the tube: no power down, no second collapse, only the dark.</summary>
        public bool AfterHit;
        /// <summary>Phase Q2 (V7): "Session 017 kept a copy of: ..." under the outcome ("" = none).</summary>
        public string KeptLine = "";
        /// <summary>Phase Q2 (T2): the Retention Record's rows under the card (Nights 1 and 2), or null.</summary>
        public System.Collections.Generic.List<Core.Game.RecordRow> RecordRows;
        /// <summary>Phase Q2 (T2): the whole Retention Record, shown on its own page before the card (Night 3), or null.</summary>
        public System.Collections.Generic.List<Core.Game.RecordRow> RecordPage;

        /// <summary>
        /// Night 1: the slice's blackout. The demo keeps the WISHLIST card; the full game shows the night's own card
        /// with Continue to Night 2 (completing the night has just unlocked it).
        /// </summary>
        public static EndingSpec Night1()
        {
            var spec = new EndingSpec { Id = Core.Content.ContentIds.EndingN1Blackout };
#if !SC_DEMO
            spec.DemoCard = false;
            spec.TitleKey = "end.n1.title";
            spec.SubtitleKey = "end.n1.subtitle";
            spec.ContinueNight = 2;
#endif
            return spec;
        }
    }

    /// <summary>
    /// The blackout: the monitor dies, silence, then in the dark the second cursor types its last lines,
    /// followed by the end card (Night 1: SECOND CURSOR / WISHLIST NOW; later nights: the night's own card).
    /// </summary>
    public sealed partial class EndingSequence
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
#if !SC_DEMO
            ClearNight3();
#endif
        }

        public IEnumerator Run()
        {
#if !SC_DEMO
            if (_spec.Kind != EndingKind.Blackout)
            {
                yield return RunNight3();
                yield break;
            }
#endif
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

            yield return PowerDown();

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
            yield return ShowCard(black);
        }

        /// <summary>
        /// The hum stops, the tube dies, black (Phase M: after a climax's hit only the dark; toast chimes come back once it is dark).
        /// </summary>
        IEnumerator PowerDown()
        {
            var g = _g;
            g.Player.Visible = false;
            g.Audio.StopAllLoops(0.2f);
            if (g.CameraRig != null) g.CameraRig.LightsOn = false;
            if (!_spec.AfterHit)
            {
                g.Audio.Play("power_down");
                yield return Waits.Seconds(0.5f);
                g.Audio.Play("crt_off");
                yield return g.Fx.PowerOff(0.9f);
            }
            g.Fx.SetBlack(true);
            yield return Waits.Seconds(2.6f);
            g.Audio.UiMuted = false;
        }

        /// <summary>The card and its buttons (<see cref="Story.EndCard"/>); never returns (a button starts something new).</summary>
        IEnumerator ShowCard(RectTransform parent) => Story.EndCard.Run(_g, _spec, parent);

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
    }
}
