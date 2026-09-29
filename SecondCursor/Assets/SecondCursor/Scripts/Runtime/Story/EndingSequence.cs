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
    /// The blackout: the monitor dies, silence, then in the dark the second cursor types its last lines,
    /// followed by the end card (SECOND CURSOR / WISHLIST NOW).
    /// </summary>
    public sealed class EndingSequence
    {
        readonly GameServices _g;
        RectTransform _room;

        public EndingSequence(GameServices g)
        {
            _g = g;
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
            g.Flags.Set(Flags.Ending);
            SaveSystem.RecordEnding(g, "night1_blackout");
            g.Entity.Brain.Enabled = false;
            g.Entity.Interrupt();
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
            foreach (var line in g.Content.Story.endingLines)
            {
                foreach (char c in line)
                {
                    sb.Append(c);
                    text.text = sb.ToString();
                    g.Audio.Play(c == ' ' ? "key_space" : "key_tap", 0.9f, Random.Range(0.9f, 1.05f), AudioPanFor(g));
                    // The cursor jitters with each keystroke.
                    g.Entity.Teleport(g.Entity.Agent.Position + Random.insideUnitCircle * 0.8f);
                    yield return Waits.Seconds(c == ' ' ? 0.16f : Random.Range(0.07f, 0.16f));
                }
                yield return Waits.Seconds(1.6f);
                sb.Append('\n');
                text.text = sb.ToString();
            }
            yield return Waits.Seconds(2.5f);
            yield return g.Entity.Vanish(1.5f);
            float a = 1f;
            while (a > 0f)
            {
                a -= Time.deltaTime * 0.8f;
                text.color = new Color(0.91f, 0.9f, 0.87f, Mathf.Max(0f, a));
                yield return null;
            }
            yield return Waits.Seconds(1f);
            yield return EndCard(black);
        }

        static float AudioPanFor(GameServices g) => Audio.AudioManager.PanFor(g.EntityAgent.Position.x);

        IEnumerator EndCard(RectTransform parent)
        {
            var g = _g;
            var c = g.Content;
            g.Audio.Play("end_tone");
            g.Audio.Play("low_thump", 0.8f);

            var title = UIBuilder.Text(parent, c.Text("end.card.title"), Palette.BiosBright, true);
            title.Scale = 5;
            title.rectTransform.At(0, 150, ScreenRig.Width, 60);
            title.Align = TextAlign.Center;
            var sub = UIBuilder.Text(parent, c.Text("end.card.subtitle"), Palette.BiosText);
            sub.rectTransform.At(0, 222, ScreenRig.Width, 12);
            sub.Align = TextAlign.Center;
            var cta = UIBuilder.Text(parent, c.Text("end.card.cta"), new Color32(0xE8, 0xC4, 0x5A, 0xFF), true);
            cta.Scale = 3;
            cta.rectTransform.At(0, 290, ScreenRig.Width, 36);
            cta.Align = TextAlign.Center;
            var thanks = UIBuilder.Text(parent, c.Text("end.card.thanks"), Palette.BiosText);
            thanks.rectTransform.At(0, 350, ScreenRig.Width, 12);
            thanks.Align = TextAlign.Center;

            g.Player.Enabled = true;
            g.Player.Visible = true;
            var again = UiButton.Create(parent, "Start a new shift", a => GameBootstrap.Restart(), "button:Restart");
            ((RectTransform)again.transform).At(ScreenRig.Width / 2 - 150, 420, 140, 24);
            var quit = UiButton.Create(parent, "Quit", a => Quit(), "button:Quit");
            ((RectTransform)quit.transform).At(ScreenRig.Width / 2 + 10, 420, 140, 24);

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
