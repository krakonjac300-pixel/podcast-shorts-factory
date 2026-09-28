using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Input
{
    /// <summary>
    /// Draws a CursorAgent inside the fake screen (so it gets the CRT treatment like everything else).
    /// The second cursor uses an inverted palette, leaves faint afterimages when it moves fast, and can
    /// fade, flicker and jitter - small cues that something that is not you is moving.
    /// </summary>
    public sealed class CursorView : MonoBehaviour
    {
        const int TrailLength = 3;

        CursorAgent _agent;
        bool _entityStyle;
        Image _image;
        RectTransform _rt;
        CursorShape _shownShape = (CursorShape)(-1);
        string _shownSprite;
        readonly Image[] _trail = new Image[TrailLength];
        readonly Vector2[] _history = new Vector2[16];
        int _historyIndex;

        /// <summary>0..1 visibility multiplier (fade in/out).</summary>
        public float Alpha = 1f;
        /// <summary>Random per-frame offset amplitude in px (entity agitation).</summary>
        public float Jitter;
        /// <summary>0..1 chance per frame of skipping a frame (glitchy flicker).</summary>
        public float Flicker;

        public CursorAgent Agent => _agent;

        public static CursorView Create(RectTransform layer, CursorAgent agent, bool entityStyle)
        {
            var rt = UIBuilder.Rect(agent.Name + " Cursor", layer);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            var view = rt.gameObject.AddComponent<CursorView>();
            view._agent = agent;
            view._entityStyle = entityStyle;
            view._rt = rt;

            if (entityStyle)
            {
                for (int i = 0; i < TrailLength; i++)
                {
                    var trt = UIBuilder.Rect("Trail " + i, layer);
                    trt.anchorMin = trt.anchorMax = Vector2.zero;
                    trt.pivot = new Vector2(0f, 1f);
                    trt.SetSiblingIndex(rt.GetSiblingIndex());
                    var img = trt.gameObject.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.enabled = false;
                    view._trail[i] = img;
                }
            }

            view._image = rt.gameObject.AddComponent<Image>();
            view._image.raycastTarget = false;
            view.Apply(CursorShape.Arrow);
            return view;
        }

        static string SpriteFor(CursorShape shape, float time)
        {
            switch (shape)
            {
                case CursorShape.Hand: return "cursor_hand";
                case CursorShape.IBeam: return "cursor_ibeam";
                case CursorShape.Move: return "cursor_move";
                case CursorShape.Busy: return ((int)(time * 6f) & 1) == 0 ? "cursor_busy_0" : "cursor_busy_1";
                case CursorShape.No: return "cursor_no";
                case CursorShape.Drag: return "cursor_drag";
                case CursorShape.Grab: return "cursor_grab";
                default: return "cursor_arrow";
            }
        }

        Sprite Resolve(string spriteName)
        {
            if (!_entityStyle) return SpriteLibrary.Get(spriteName);
            return SpriteLibrary.GetVariant(spriteName, "entity", c =>
            {
                if (c == 'K') return Palette.EntityOutline;
                if (c == 'W') return Palette.EntityFill;
                return null;
            });
        }

        void Apply(CursorShape shape)
        {
            string spriteName = SpriteFor(shape, Time.unscaledTime);
            if (shape == _shownShape && spriteName == _shownSprite) return;
            _shownShape = shape;
            _shownSprite = spriteName;
            var sprite = Resolve(spriteName);
            _image.sprite = sprite;
            var size = SpriteLibrary.Size(spriteName);
            _rt.sizeDelta = new Vector2(size.x, size.y);
            for (int i = 0; i < TrailLength; i++)
            {
                if (_trail[i] == null) continue;
                _trail[i].sprite = sprite;
                _trail[i].rectTransform.sizeDelta = _rt.sizeDelta;
            }
        }

        void LateUpdate()
        {
            if (_agent == null) return;
            Apply(_agent.Shape);

            var hot = SpriteLibrary.Hotspot(_shownSprite);
            Vector2 p = _agent.Position;
            if (Jitter > 0f) p += Random.insideUnitCircle * Jitter;
            Vector2 topLeft = new Vector2(Mathf.Round(p.x - hot.x), Mathf.Round(p.y + hot.y));
            _rt.anchoredPosition = topLeft;

            bool visible = _agent.Visible && Alpha > 0.01f && !(Flicker > 0f && Random.value < Flicker);
            _image.enabled = visible;
            var c = _image.color;
            c.a = Mathf.Clamp01(Alpha);
            _image.color = c;

            if (!_entityStyle) return;
            _history[_historyIndex] = topLeft;
            _historyIndex = (_historyIndex + 1) % _history.Length;
            float speed = _agent.Velocity.magnitude;
            bool trail = visible && speed > 500f;
            for (int i = 0; i < TrailLength; i++)
            {
                var img = _trail[i];
                if (img == null) continue;
                img.enabled = trail;
                if (!trail) continue;
                int back = (i + 1) * 2;
                var pos = _history[(_historyIndex - 1 - back + _history.Length * 4) % _history.Length];
                img.rectTransform.anchoredPosition = pos;
                img.color = new Color(1f, 1f, 1f, Alpha * (0.28f - i * 0.08f) * Mathf.Clamp01((speed - 500f) / 700f));
            }
        }
    }
}
