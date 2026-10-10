using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Input
{
    /// <summary>
    /// Phase R (sixth blind playtest: "two pointers, I could not tell which one was me"): small name tags that follow the pointers. A cream
    /// YOU tag rides the player's pointer for the first 30 s of each shift and throughout any shared-cursor scene;
    /// SESSION 017 (and, on the nights Gary exists, SESSION 209) rides that session's pointer in its own colours whenever it is on screen.
    /// The tags sit to the right of the arrow's tip (to the left near the screen edge), under the pointers, and never take a click.
    /// </summary>
    public sealed class PointerTags : MonoBehaviour
    {
        const int TagHeight = 11, PadX = 3, Gap = 3, ArrowWidth = 12;
        /// <summary>A second pointer counts as on screen once it is this opaque.</summary>
        const float OpaqueEnough = 0.25f;

        sealed class Tag
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Edge, Fill;
            public PixelText Text;
            public string Shown = "";
            public int Width;
        }

        GameServices _g;
        Tag _you, _entity, _gary;
        float _youLeft;
        bool _loggedSeen, _entitySeen, _garySeen;

        /// <summary>Seconds the YOU tag still shows (0 = hidden).</summary>
        public float YouLeft => _youLeft;

        public static PointerTags Create(GameServices g)
        {
            var root = UIBuilder.Rect("Pointer Tags", g.Layers.Cursors);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.SetAsFirstSibling();   // the pointers are drawn over their tags
            var tags = root.gameObject.AddComponent<PointerTags>();
            tags._g = g;
            tags._you = tags.Make(root, "You Tag", Palette.Highlight, Palette.Dark);
            tags._entity = tags.Make(root, "Session Tag", Palette.EntityFill, Palette.EntityOutline);
            tags._gary = tags.Make(root, "Gary Tag", Palette.GaryFill, Palette.GaryOutline);
            return tags;
        }

        Tag Make(RectTransform parent, string name, Color32 fill, Color32 edge)
        {
            var t = new Tag { Root = UIBuilder.Rect(name, parent) };
            t.Root.anchorMin = t.Root.anchorMax = Vector2.zero;
            t.Root.pivot = new Vector2(0f, 1f);
            t.Group = t.Root.gameObject.AddComponent<CanvasGroup>();
            t.Group.blocksRaycasts = false;
            t.Group.interactable = false;
            t.Edge = UIBuilder.Solid(t.Root, edge, "Edge");
            t.Fill = UIBuilder.Solid(t.Root, fill, "Fill");
            // The text takes the edge colour on the dark tags and the dark colour on the cream one.
            t.Text = UIBuilder.Text(t.Root, "", fill.r > 0x80 ? Palette.Dark : edge, true, "Label");
            t.Text.VAlign = TextVAlign.Middle;
            t.Root.gameObject.SetActive(false);
            return t;
        }

        /// <summary>The YOU tag shows (again) for at least <paramref name="seconds"/>; a running tag is never shortened.</summary>
        public void ShowYou(float seconds) => _youLeft = PointerTagRules.Extend(_youLeft, seconds);

        void LateUpdate()
        {
            var g = _g;
            if (g == null || g.Player == null || g.Content == null) return;
            if (!_loggedSeen && g.Flags != null && g.Flags.Has(Flags.LoggedIn))
            {
                _loggedSeen = true;
                ShowYou(PointerTagRules.YouFirstSeconds);
            }
            bool entityOn = IsShown(g.EntityAgent, g.EntityView), garyOn = IsShown(g.GaryAgent, g.GaryView);
            bool first = PointerTagRules.IsFirstAppearance(entityOn, ref _entitySeen);
            first |= PointerTagRules.IsFirstAppearance(garyOn, ref _garySeen);
            if (first && _loggedSeen) ShowYou(PointerTagRules.YouAgainSeconds);
            // The clock runs only while the player can use the pointer: a Quick Start box or a pause does not eat the 30 s.
            if (_youLeft > 0f && g.Player.Visible && g.Player.Enabled && !AnyDialog(g)) _youLeft -= Time.deltaTime;

            string you = g.Content.Text("pointer.tag.you", "YOU");
            // Not while the story has taken the pointer (a climax) or the ending's screens are up: it only helps where the player can act.
            bool playing = g.Flags != null && g.Flags.Has(Flags.LoggedIn) && !g.Flags.Has(Flags.Ending);
            Show(_you, g.Player, null, you, g.Player.Visible && g.Player.Enabled
                ? PointerTagRules.VisibleYouAlpha(_youLeft, playing && (entityOn || garyOn)) : 0f);
            Show(_entity, g.EntityAgent, g.EntityView, g.Content.Text("pointer.tag.entity", "SESSION 017"), entityOn ? Opacity(g.EntityView) : 0f);
            // Session 209 is a Night 2 and 3 string: no text, no tag (Night 1 and the demo never show one).
            Show(_gary, g.GaryAgent, g.GaryView, g.Content.Text("pointer.tag.gary", ""), garyOn ? Opacity(g.GaryView) : 0f);
        }

        static bool IsShown(CursorAgent agent, CursorView view) => agent != null && agent.Visible && view != null && view.Alpha > OpaqueEnough;

        static float Opacity(CursorView view) => Mathf.Clamp01(view.Alpha);

        static bool AnyDialog(GameServices g)
        {
            if (g.Windows == null) return false;
            foreach (var w in g.Windows.Windows)
                if (w != null && !w.IsClosed && !w.IsMinimized && w.AlwaysOnTop) return true;
            return false;
        }

        void Show(Tag tag, CursorAgent agent, CursorView view, string text, float alpha)
        {
            if (alpha <= 0.01f || string.IsNullOrEmpty(text))
            {
                if (tag.Root.gameObject.activeSelf) tag.Root.gameObject.SetActive(false);
                return;
            }
            if (!tag.Root.gameObject.activeSelf) tag.Root.gameObject.SetActive(true);
            if (tag.Shown != text) Resize(tag, text);
            tag.Group.alpha = alpha;
            int size = AccessOptions.CursorScale(AccessSettings.LargeCursor);
            Vector2 p = agent.Position + (view != null ? view.VisualOffset : Vector2.zero);
            float x = p.x + ArrowWidth * size + Gap;
            if (x + tag.Width > ScreenRig.Width - 2f) x = p.x - tag.Width - Gap;
            float y = Mathf.Clamp(p.y - 2f, TagHeight + 2f, ScreenRig.Height - 2f);
            tag.Root.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        static void Resize(Tag tag, string text)
        {
            tag.Shown = text;
            tag.Width = PixelFont.MeasureLine(text, true, 1) + PadX * 2 + 2;
            tag.Root.sizeDelta = new Vector2(tag.Width, TagHeight);
            tag.Edge.rectTransform.At(0, 0, tag.Width, TagHeight);
            tag.Fill.rectTransform.At(1, 1, tag.Width - 2, TagHeight - 2);
            tag.Text.text = text;
            tag.Text.rectTransform.At(PadX + 1, 1, tag.Width - PadX * 2, TagHeight - 2);
        }
    }
}
