using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.FX
{
    /// <summary>
    /// Phase P (review board R1 item 4): during a tug the rest of the screen dims about 28% so nothing competes with the fight. A clear circle stays
    /// over the fight (the track, both pointers and the bin), with a two-step pixel edge. Built from one 64x64 point-filtered texture for the
    /// circle and four black quads around it. Fades in over 80 ms and out over 200 ms. Lives at the bottom of the Effects layer (under the
    /// rope, the panel, the arrows and the cursors). Presentation only.
    /// </summary>
    public sealed class FocusDim : MonoBehaviour
    {
        const int Tex = 64, ClearTexels = 24, RingTexels = 4;
        /// <summary>
        /// The screen blends in linear colour: black at 0.5 there darkens a desktop or window pixel by about 28% as it is seen (measured on the
        /// bridge: Work Queue grey 194 to 140), which is the review board's "28% dim"; the ring is half of it.
        /// </summary>
        const float DimAlpha = 0.5f, RingAlpha = 0.25f, FadeIn = 0.08f, FadeOut = 0.2f;

        static Texture2D _texture;
        RectTransform _root;
        RawImage _hole;
        readonly Image[] _quads = new Image[4];
        CanvasGroup _group;
        float _alpha, _target;
        Vector2 _centre;
        float _radius = 120f;

        public bool Showing => _target > 0f;

        public static FocusDim Create(RectTransform layer)
        {
            var root = UIBuilder.Rect("Focus Dim", layer);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.SetAsFirstSibling();
            var d = root.gameObject.AddComponent<FocusDim>();
            d._root = root;
            d._group = root.gameObject.AddComponent<CanvasGroup>();
            d._group.blocksRaycasts = false;
            d._group.interactable = false;
            // One texture for every game root (a restart builds a new root).
            if (_texture == null) _texture = BuildTexture();
            d._hole = UIBuilder.Raw(root, _texture, "Hole");
            d._hole.raycastTarget = false;
            var rt = d._hole.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                d._quads[i] = UIBuilder.Solid(root, new Color(0f, 0f, 0f, DimAlpha), "Dim " + i);
                var q = d._quads[i].rectTransform;
                q.anchorMin = q.anchorMax = Vector2.zero;
                q.pivot = Vector2.zero;
            }
            d._group.alpha = 0f;
            root.gameObject.SetActive(false);
            return d;
        }

        /// <summary>A black square at the dim's alpha with a clear disc in the middle and a ring at half the alpha around it.</summary>
        static Texture2D BuildTexture()
        {
            var t = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "FocusDimHole" };
            var px = new Color32[Tex * Tex];
            float c = (Tex - 1) * 0.5f;
            for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = r < ClearTexels ? 0f : r < ClearTexels + RingTexels ? RingAlpha : DimAlpha;
                    px[y * Tex + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Dims everything outside a circle of <paramref name="radius"/> px around <paramref name="centre"/> (virtual px).</summary>
        public void Show(Vector2 centre, float radius)
        {
            _centre = centre;
            _radius = Mathf.Max(24f, radius);
            _target = 1f;
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            Layout();
        }

        public void Hide() => _target = 0f;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _alpha = Mathf.MoveTowards(_alpha, _target, dt / (_target > _alpha ? FadeIn : FadeOut));
            _group.alpha = _alpha;
            if (_alpha <= 0f && _target <= 0f && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        /// <summary>The texture's disc of <see cref="ClearTexels"/> covers the radius; the four quads cover the rest of the screen.</summary>
        void Layout()
        {
            float scale = _radius / ClearTexels;
            float size = Mathf.Round(Tex * scale);
            float x0 = Mathf.Round(_centre.x - size * 0.5f), y0 = Mathf.Round(_centre.y - size * 0.5f);
            var rt = _hole.rectTransform;
            rt.anchoredPosition = new Vector2(x0 + size * 0.5f, y0 + size * 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            float w = ScreenRig.Width, h = ScreenRig.Height;
            SetQuad(0, 0f, 0f, w, Mathf.Max(0f, y0));                                   // below
            SetQuad(1, 0f, y0 + size, w, Mathf.Max(0f, h - y0 - size));                 // above
            SetQuad(2, 0f, y0, Mathf.Max(0f, x0), size);                                // left
            SetQuad(3, x0 + size, y0, Mathf.Max(0f, w - x0 - size), size);              // right
        }

        void SetQuad(int i, float x, float y, float w, float h)
        {
            var q = _quads[i].rectTransform;
            q.anchoredPosition = new Vector2(x, y);
            q.sizeDelta = new Vector2(w, h);
        }
    }
}
