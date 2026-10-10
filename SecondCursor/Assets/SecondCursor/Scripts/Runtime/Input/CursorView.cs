using SecondCursor.Core;
using SecondCursor.Core.Game;
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
        /// <summary>Pointer speed (px/s) from which session 017 leaves its smear (was 500).</summary>
        const float TrailSpeed = 250f;

        CursorAgent _agent;
        bool _entityStyle;
        string _variant;
        Image _image;
        RectTransform _rt;
        CursorShape _shownShape = (CursorShape)(-1);
        string _shownSprite;
        readonly Image[] _trail = new Image[TrailLength];
        readonly Vector2[] _history = new Vector2[16];
        int _historyIndex;
        // Per-frame rolls at 60 Hz (the flicker, the jitter, the smear's samples) run on a 1/60 s step: the same at any frame rate.
        StepTimer _flickerTimer, _jitterTimer, _trailTimer;
        bool _flickerHidden;
        Vector2 _jitterOffset;
        /// <summary>Phase Q4 (A9): the pointers are drawn at this whole-number size (2 with the large cursor option).</summary>
        int _size = 1;
        /// <summary>Phase Q4 (R5): session 017 is drawn with a 1 px dark halo, so its pale rim reads on cream windows as well as on the teal desktop (Gary's amber hand reads on its own).</summary>
        static readonly Color32 HaloColor = new Color32(0x0B, 0x0E, 0x0D, 0xB2);

        /// <summary>0..1 visibility multiplier (fade in/out).</summary>
        public float Alpha = 1f;
        /// <summary>Random offset amplitude in px, re-rolled every 1/60 s (entity agitation).</summary>
        public float Jitter;
        /// <summary>0..1 chance per 1/60 s of skipping that moment (glitchy flicker).</summary>
        public float Flicker;
        /// <summary>Draw this cursor in the second cursor's palette (M4: the player's arrow for one frame in the SHRED ending).</summary>
        public bool ShowEntityPalette;
        bool _shownPalette;
        /// <summary>Visual-only displacement (e.g. the pull of a tug-of-war, a "flinch"). Hit-testing ignores it.</summary>
        public Vector2 VisualOffset;
        Vector2 _flinch;
        float _flinchTime;

        /// <summary>The cursor twitches by itself for a moment, then settles back (did I do that?).</summary>
        public void Flinch(Vector2 offset, float settleSeconds = 0.8f)
        {
            _flinch = offset;
            _flinchTime = settleSeconds;
        }

        public CursorAgent Agent => _agent;
        /// <summary>Palette variant: null (player or Ellen) or "gary".</summary>
        public string Variant => _variant;

        /// <summary>A shape shown whatever the pointer is over (Gary is a hand while he is held). Null = normal.</summary>
        [System.NonSerialized] public CursorShape? ForcedShape;

        public static CursorView Create(RectTransform layer, CursorAgent agent, bool entityStyle) => Create(layer, agent, entityStyle, null);

        /// <summary>
        /// <paramref name="variant"/> "gary" draws the third pointer in its own amber palette, as a hand.
        /// </summary>
        public static CursorView Create(RectTransform layer, CursorAgent agent, bool entityStyle, string variant)
        {
            var rt = UIBuilder.Rect(agent.Name + " Cursor", layer);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            var view = rt.gameObject.AddComponent<CursorView>();
            view._size = AccessOptions.CursorScale(Game.AccessSettings.LargeCursor);
            Game.AccessSettings.Changed += view.OnAccessChanged;
            view._agent = agent;
            view._entityStyle = entityStyle;
            view._variant = variant;
            view._rt = rt;
            if (variant == GaryVariant) view.ForcedShape = CursorShape.Hand;

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

        public const string GaryVariant = "gary";

        void OnAccessChanged()
        {
            int size = AccessOptions.CursorScale(Game.AccessSettings.LargeCursor);
            if (size == _size) return;
            _size = size;
            _shownSprite = null;   // redrawn at the new size next frame
        }

        void OnDestroy() => Game.AccessSettings.Changed -= OnAccessChanged;

        /// <summary>True when the sprite shown is the haloed one (one pixel larger on every side).</summary>
        bool _haloed;

        Sprite Resolve(string spriteName)
        {
            _haloed = false;
            if (_variant == GaryVariant)
            {
                return SpriteLibrary.GetVariant(spriteName, GaryVariant, c =>
                {
                    if (c == 'K') return Palette.GaryOutline;
                    if (c == 'W') return Palette.GaryFill;
                    return null;
                });
            }
            if (!_entityStyle && !ShowEntityPalette) return SpriteLibrary.Get(spriteName);
            // Phase Q4 (R5): session 017 reads as a black arrow with a pale rim and a dark halo on every background.
            _haloed = true;
            return SpriteLibrary.GetHalo(spriteName, "entity", c =>
            {
                if (c == 'K') return Palette.EntityOutline;
                if (c == 'W') return Palette.EntityFill;
                return null;
            }, HaloColor);
        }

        void Apply(CursorShape shape)
        {
            string spriteName = SpriteFor(shape, Time.unscaledTime);
            if (shape == _shownShape && spriteName == _shownSprite && ShowEntityPalette == _shownPalette) return;
            _shownPalette = ShowEntityPalette;
            _shownShape = shape;
            _shownSprite = spriteName;
            var sprite = Resolve(spriteName);
            _image.sprite = sprite;
            var size = SpriteLibrary.Size(spriteName);
            int grow = _haloed ? 2 : 0;
            _rt.sizeDelta = new Vector2((size.x + grow) * _size, (size.y + grow) * _size);
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
            Apply(ForcedShape ?? _agent.Shape);

            var hot = SpriteLibrary.Hotspot(_shownSprite);
            // The halo shifts the hotspot one pixel in; a larger cursor scales both (the click point stays on the tip).
            if (_haloed) hot += Vector2Int.one;
            hot *= _size;
            Vector2 p = _agent.Position + VisualOffset;
            if (_flinchTime > 0f)
            {
                _flinchTime -= Time.deltaTime;
                p += _flinch * Mathf.Clamp01(_flinchTime / 0.8f);
            }
            if (Jitter > 0f)
            {
                if (_jitterTimer.Tick(Time.unscaledDeltaTime)) _jitterOffset = Random.insideUnitCircle;
                p += _jitterOffset * Jitter;
            }
            Vector2 topLeft = new Vector2(Mathf.Floor(p.x - hot.x + 0.5f), Mathf.Floor(p.y + hot.y + 0.5f));
            _rt.anchoredPosition = topLeft;

            if (_flickerTimer.Tick(Time.unscaledDeltaTime)) _flickerHidden = Flicker > 0f && Random.value < Flicker;
            bool visible = _agent.Visible && Alpha > 0.01f && !(Flicker > 0f && _flickerHidden);
            _image.enabled = visible;
            var c = _image.color;
            c.a = Mathf.Clamp01(Alpha);
            _image.color = c;

            if (!_entityStyle) return;
            if (_trailTimer.Tick(Time.unscaledDeltaTime))
            {
                _history[_historyIndex] = topLeft;
                _historyIndex = (_historyIndex + 1) % _history.Length;
            }
            float speed = _agent.Velocity.magnitude;
            // Phase Q4 (R5): her ordinary moves smear a little from 250 px/s (the player never does): the motion signature of "not me".
            bool trail = visible && speed > TrailSpeed;
            for (int i = 0; i < TrailLength; i++)
            {
                var img = _trail[i];
                if (img == null) continue;
                img.enabled = trail;
                if (!trail) continue;
                // Consecutive recent 1/60 s samples (50 ms in all) fading fast: a smear behind the cursor, never extra cursors.
                int back = i + 1;
                var pos = _history[(_historyIndex - 1 - back + _history.Length * 4) % _history.Length];
                img.rectTransform.anchoredPosition = pos;
                img.color = new Color(1f, 1f, 1f, Alpha * (0.22f - i * 0.07f) * Mathf.Clamp01((speed - TrailSpeed) / 700f));
            }
        }
    }
}
