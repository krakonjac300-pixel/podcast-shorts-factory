using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q4 (review board R9): the title's key visual, the store capsule drawn in the engine. A dark teal desktop behind the menu, and above the
    /// title two pointers (yours, white; hers, black with a pale rim) at 4x on either side of a file, joined to it by a red dotted line that
    /// marches and tightens while the file sways in a slow tug. Everything is slow and nothing flashes. It lives under the main screen's
    /// content, so any other title screen (Options, Records, Credits) clears it.
    /// </summary>
    public sealed class TitleKeyArt : MonoBehaviour
    {
        const int Scale = 4, FileScale = 3, DotCount = 40, DotSize = 4, DotGap = 9;
        const float SwayPixels = 7f, SwaySeconds = 7f, MarchPerSecond = 5f;
        static readonly Color32 Red = new Color32(0xC0, 0x39, 0x2B, 0xFF);
        static readonly Color32 Top = new Color32(0x16, 0x2E, 0x2A, 0xFF), Bottom = new Color32(0x08, 0x10, 0x0E, 0xFF);

        RectTransform _file, _left, _right;
        readonly Image[] _dots = new Image[DotCount];
        // The scene in virtual pixels from the top left of the screen: the file's rest position and the two pointer tips.
        Vector2 _fileCenter = new Vector2(480f, 104f), _leftTip = new Vector2(352f, 70f), _rightTip = new Vector2(608f, 70f);
        float _t;

        /// <summary>Phase R: the legend under the pointers (yours is the white arrow, hers the dark one), at this height from the top.</summary>
        const int LegendTop = 146;

        public static TitleKeyArt Create(RectTransform parent, string youLegend = "", string sessionLegend = "")
        {
            var root = UIBuilder.Rect("Key Art", parent).Stretch();
            root.SetAsFirstSibling();
            var art = root.gameObject.AddComponent<TitleKeyArt>();
            art.Build(root);
            art.Legend(root, youLegend, art._leftTip.x - 22f, Palette.BiosBright);
            art.Legend(root, sessionLegend, art._rightTip.x + 24f, Palette.EntityOutline);
            return art;
        }

        /// <summary>One caption centred on x, in the colour of the pointer it names (nothing when the text is empty).</summary>
        void Legend(RectTransform root, string text, float centreX, Color32 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = PixelFont.MeasureLine(text, true) + 4;
            var label = UIBuilder.Text(root, text, color, true, "Legend");
            label.rectTransform.At(centreX - w * 0.5f, LegendTop, w, 12);
            label.Align = TextAlign.Center;
        }

        void Build(RectTransform root)
        {
            // The desktop: a vertical gradient, teal at the top and nearly black at the bottom (the dread stays, the capsule's colour is there).
            var tex = OwnedAssets.Own(gameObject, SpriteLibrary.MakeTexture(1, 64, (x, y) => Color32.Lerp(Bottom, Top, y / 63f), FilterMode.Bilinear, TextureWrapMode.Clamp));
            var back = UIBuilder.Raw(root, tex, "Desktop");
            back.rectTransform.Stretch();

            for (int i = 0; i < DotCount; i++)
            {
                _dots[i] = UIBuilder.Solid(root, Red, "Dot " + i);
                _dots[i].rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
                Anchor(_dots[i].rectTransform);
            }
            _file = MakeIcon(root, "icon_file_dat", FileScale);
            // Hers is the arrow as it is (tip up and left); yours is mirrored so both point in at the file.
            _left = MakeSprite(root, SpriteLibrary.Get("cursor_arrow"), "cursor_arrow", true);
            _right = MakeSprite(root, ActorSprites.For("cursor_arrow", Core.Game.NoticeKind.Entity), "cursor_arrow", false);
            Place(0f);
        }

        static void Anchor(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        static RectTransform MakeIcon(RectTransform parent, string sprite, int scale)
        {
            var img = UIBuilder.Icon(parent, sprite, scale, "Key " + sprite);
            Anchor(img.rectTransform);
            return img.rectTransform;
        }

        static RectTransform MakeSprite(RectTransform parent, Sprite sprite, string name, bool mirror)
        {
            var img = UIBuilder.Icon(parent, name, Scale, "Key Pointer");
            img.sprite = sprite;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            // The tip is the top corner: pivot there so the tip is the point we place (and a mirror turns about it).
            rt.pivot = new Vector2(0f, 1f);
            rt.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
            return rt;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            Place(_t);
        }

        void Place(float t)
        {
            // The slow tug: the file sways toward one pointer then the other, and the line is a little tighter on the side that is winning.
            float sway = Mathf.Sin(t * Mathf.PI * 2f / SwaySeconds) * SwayPixels;
            var file = _fileCenter + new Vector2(sway, Mathf.Sin(t * 1.3f) * 1.5f);
            _file.anchoredPosition = new Vector2(file.x, -file.y);
            _left.anchoredPosition = new Vector2(_leftTip.x + sway * 0.5f, -_leftTip.y);
            _right.anchoredPosition = new Vector2(_rightTip.x + sway * 0.5f, -_rightTip.y);
            float half = 16f * FileScale * 0.5f + 6f;
            Line(0, DotCount / 2, new Vector2(_leftTip.x + sway * 0.5f, _leftTip.y + 4f), new Vector2(file.x - half, file.y), t);
            Line(DotCount / 2, DotCount, new Vector2(file.x + half, file.y), new Vector2(_rightTip.x + sway * 0.5f, _rightTip.y + 4f), -t);
        }

        /// <summary>Dots along a segment, every few pixels, marching slowly (the dots outside the segment hide).</summary>
        void Line(int from, int to, Vector2 a, Vector2 b, float march)
        {
            float length = Vector2.Distance(a, b);
            var dir = length > 0.01f ? (b - a) / length : Vector2.right;
            float offset = Mathf.Repeat(march * MarchPerSecond, DotGap);
            for (int i = from; i < to; i++)
            {
                float d = (i - from) * DotGap + offset;
                bool on = d <= length;
                _dots[i].enabled = on;
                if (!on) continue;
                var p = a + dir * d;
                _dots[i].rectTransform.anchoredPosition = new Vector2(Mathf.Round(p.x), -Mathf.Round(p.y));
            }
        }
    }
}
