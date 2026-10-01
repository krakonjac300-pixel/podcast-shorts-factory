using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.UI
{
    /// <summary>
    /// Clipped vertical scroll view with a period scrollbar (arrow buttons, track, draggable thumb).
    /// Owners put rows into <see cref="Content"/> (anchored top-left, y downward) and set
    /// <see cref="ContentHeight"/>. The mouse wheel reaches it through the PointerRouter.
    /// </summary>
    public sealed class ScrollArea : MonoBehaviour
    {
        const int BarWidth = 16;

        public RectTransform Viewport { get; private set; }
        public RectTransform Content { get; private set; }
        public float WheelStep = 36f;
        public float LineStep = 12f;

        float _contentHeight;
        float _offset;
        RectTransform _bar;
        RectTransform _thumb;
        Interactable _trackHit;
        UiButton _up, _down;
        bool _showBar = true;
        float _dragStartOffset;
        float _dragStartY;

        public float ContentHeight
        {
            get => _contentHeight;
            set { _contentHeight = Mathf.Max(0f, value); Clamp(); }
        }

        public float Offset => _offset;
        public float ViewportHeight => Viewport.rect.height;
        public float MaxOffset => Mathf.Max(0f, _contentHeight - ViewportHeight);

        public static ScrollArea Create(Transform parent, string name = "Scroll", bool scrollbar = true)
        {
            var root = UIBuilder.Rect(name, parent);
            var sa = root.gameObject.AddComponent<ScrollArea>();
            sa._showBar = scrollbar;

            sa.Viewport = UIBuilder.Rect("Viewport", root).Stretch(0, 0, scrollbar ? BarWidth : 0, 0);
            UIBuilder.Clip(sa.Viewport);
            // Background catcher so the wheel and clicks on empty space land inside this area.
            UIBuilder.Hit(sa.Viewport.gameObject, name + ":background");

            sa.Content = UIBuilder.Rect("Content", sa.Viewport);
            sa.Content.anchorMin = new Vector2(0f, 1f);
            sa.Content.anchorMax = new Vector2(1f, 1f);
            sa.Content.pivot = new Vector2(0f, 1f);
            sa.Content.offsetMin = new Vector2(0f, -10f);
            sa.Content.offsetMax = Vector2.zero;

            if (scrollbar)
            {
                sa._bar = UIBuilder.Rect("Scrollbar", root);
                sa._bar.anchorMin = new Vector2(1f, 0f);
                sa._bar.anchorMax = new Vector2(1f, 1f);
                sa._bar.pivot = new Vector2(1f, 1f);
                sa._bar.offsetMin = new Vector2(-BarWidth, 0f);
                sa._bar.offsetMax = Vector2.zero;
                var track = UIBuilder.Bevel(sa._bar, BevelStyle.Flat, "Track");
                track.Fill = Palette.Midlight;
                track.rectTransform.Stretch();
                sa._trackHit = UIBuilder.Hit(track.gameObject, name + ":track");
                sa._trackHit.Click += (a, n) => sa.PageTowards(a.Position);

                sa._up = UiButton.CreateIcon(sa._bar, "glyph_arrow_up", a => sa.ScrollBy(-sa.LineStep * 2), name + ":up");
                sa._up.ClickSound = "";
                ((RectTransform)sa._up.transform).TopStrip(0, BarWidth);
                sa._down = UiButton.CreateIcon(sa._bar, "glyph_arrow_down", a => sa.ScrollBy(sa.LineStep * 2), name + ":down");
                sa._down.ClickSound = "";
                ((RectTransform)sa._down.transform).BottomStrip(0, BarWidth);

                var thumbFace = UIBuilder.Bevel(sa._bar, BevelStyle.Raised, "Thumb");
                sa._thumb = thumbFace.rectTransform;
                sa._thumb.anchorMin = new Vector2(0f, 1f);
                sa._thumb.anchorMax = new Vector2(1f, 1f);
                sa._thumb.pivot = new Vector2(0f, 1f);
                var thumbHit = UIBuilder.Hit(thumbFace.gameObject, name + ":thumb");
                thumbHit.draggable = true;
                thumbHit.dragThreshold = 0f;
                thumbHit.DragBegin += a => { sa._dragStartOffset = sa._offset; sa._dragStartY = a.Position.y; };
                thumbHit.Drag += (a, d) => sa.DragThumb(a.Position.y);
            }
            return sa;
        }

        /// <summary>Phase Q1: when the player last scrolled anything (wheel, arrows, track or thumb), unscaled seconds; reading is not being stuck.</summary>
        public static float LastPlayerScrollAt { get; private set; } = -1000f;

        public void ScrollBy(float delta)
        {
            _offset += delta;
            Clamp();
            LastPlayerScrollAt = Time.unscaledTime;
        }

        public void ScrollTo(float offset)
        {
            _offset = offset;
            Clamp();
        }

        public void ScrollToBottom() => ScrollTo(MaxOffset);

        /// <summary>Make sure the span [top, top+height] (content px) is visible.</summary>
        public void Reveal(float top, float height)
        {
            if (top < _offset) ScrollTo(top);
            else if (top + height > _offset + ViewportHeight) ScrollTo(top + height - ViewportHeight);
        }

        void PageTowards(Vector2 worldPoint)
        {
            if (_thumb == null) return;
            float thumbCenter = _thumb.WorldCenter().y;
            ScrollBy(worldPoint.y > thumbCenter ? -ViewportHeight + LineStep : ViewportHeight - LineStep);
        }

        void DragThumb(float pointerY)
        {
            float trackH = _bar.rect.height - BarWidth * 2;
            float thumbH = _thumb.rect.height;
            float range = trackH - thumbH;
            if (range <= 1f) return;
            // Absolute mapping from where the drag started: no accumulated rounding, thumb stays under the pointer.
            ScrollTo(_dragStartOffset + (_dragStartY - pointerY) * MaxOffset / range);
            LastPlayerScrollAt = Time.unscaledTime;
        }

        void Clamp()
        {
            // Keep sub-pixel precision; only the on-screen position is rounded (in Layout).
            _offset = Mathf.Clamp(_offset, 0f, MaxOffset);
            Layout();
        }

        void LateUpdate() => Layout();

        void Layout()
        {
            if (Content == null) return;
            Content.anchoredPosition = new Vector2(0f, Mathf.Floor(_offset + 0.5f));
            Content.sizeDelta = new Vector2(0f, Mathf.Max(_contentHeight, 1f));
            if (_bar == null) return;

            float vh = ViewportHeight;
            bool needed = _contentHeight > vh + 0.5f;
            _thumb.gameObject.SetActive(needed && _showBar);
            _up.Enabled = needed;
            _down.Enabled = needed;
            if (!needed) return;
            float trackH = Mathf.Max(8f, _bar.rect.height - BarWidth * 2);
            float thumbH = Mathf.Max(8f, Mathf.Round(trackH * vh / Mathf.Max(1f, _contentHeight)));
            float t = MaxOffset > 0f ? Mathf.Floor(_offset + 0.5f) / MaxOffset : 0f;
            float y = BarWidth + Mathf.Round((trackH - thumbH) * t);
            _thumb.offsetMin = new Vector2(0f, -y - thumbH);
            _thumb.offsetMax = new Vector2(0f, -y);
        }
    }
}
