using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase P (plan 2.1 and 2.5): what a reel fight looks like. A rope runs from the player's pointer to the file and on from the file to
    /// her pointer (taut and humming with strain, slack when the player is not pulling, red in a surge); a dotted track runs from her red line
    /// to the finish (the bin, or an amber tear line), filled from the grab to the file in pale (the player is ahead) or red (she is). A
    /// window over the bin gets an amber dotted ring around it, so the finish is never hidden. Pure presentation: nothing here changes the fight.
    /// </summary>
    public sealed class HaulView : MonoBehaviour
    {
        const int RopeDots = 60, TrackDots = 40, MarkDots = 10, RingDots = 110;
        const float RopeStep = 3f, TrackStep = 8f, FileHalf = 16f;
        /// <summary>The rope hangs slack until GET READY has run this long, then snaps taut.</summary>
        public const float TautAt = 0.20f;
        const float SlackNear = 40f, SlackSag = 6f, ReadySag = 10f;
        const float WhipSeconds = 0.12f;
        const float TrackAlpha = 0.35f, FillAlpha = 0.85f;
        static readonly Color RopeCalm = new Color(0.95f, 0.95f, 0.90f, 0.70f);
        static readonly Color RopeHot = new Color(0.95f, 0.22f, 0.20f, 0.95f);

        GameServices _g;
        readonly List<Image> _rope1 = new List<Image>(), _rope2 = new List<Image>(), _track = new List<Image>();
        readonly List<Image> _herMark = new List<Image>(), _tearMark = new List<Image>(), _ring = new List<Image>();
        float _whipLeft;
        Vector2 _whipFrom, _whipFile;
        float _whipStrain;
        float _coverCheck;
        bool _binCovered;

        public static HaulView Create(GameServices g)
        {
            var root = UIBuilder.Rect("Haul View", g.Layers.Effects);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            var v = root.gameObject.AddComponent<HaulView>();
            v._g = g;
            v.Pool(v._track, TrackDots, "Track");
            v.Pool(v._herMark, MarkDots, "Her Line");
            v.Pool(v._tearMark, MarkDots, "Tear Line");
            v.Pool(v._ring, RingDots, "Bin Ring");
            v.Pool(v._rope2, RopeDots, "Rope Her");
            v.Pool(v._rope1, RopeDots, "Rope You");
            return v;
        }

        void Pool(List<Image> into, int count, string name)
        {
            for (int i = 0; i < count; i++)
            {
                var dot = UIBuilder.Solid(transform, Color.clear, name + " " + i);
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, 2f);
                dot.enabled = false;
                into.Add(dot);
            }
        }

        /// <summary>One frame of a reel fight: <paramref name="player"/> and <paramref name="her"/> are the pointers, the file is at the reel's place.</summary>
        public void Show(TugReel m, Vector2 player, Vector2 her, float strain)
        {
            _whipLeft = 0f;
            Vector2 file = m.ObjectPosition.ToUnity();
            Vector2 axis = m.Axis.ToUnity();
            Vector2 origin = m.Origin.ToUnity();
            bool taut = m.Elapsed >= TautAt;
            bool surging = m.Surging;
            Color rope = surging ? RopeHot : Color.Lerp(RopeCalm, RopeHot, strain);
            float size = strain > 0.6f ? 3f : 2f;
            // Segment 1: your pointer to the file's near edge (slack when you are not pulling); segment 2: the far edge to her pointer.
            float sag1 = !taut ? ReadySag : Vector2.Distance(player, file) < SlackNear ? SlackSag : 0f;
            Rope(_rope1, player, Edge(file, player), sag1, taut ? strain : 0f, rope, size, 0f);
            Rope(_rope2, Edge(file, her), her, taut ? 0f : ReadySag, taut ? strain : 0f, rope, size, 0.9f);
            Track(m, origin, axis);
            Marks(m, origin, axis);
            Ring();
        }

        /// <summary>The fight is lost: the rope whips out of the player's hand (its end collapses onto the file) and is gone.</summary>
        public void Whip(Vector2 player, Vector2 file, float strain)
        {
            _whipLeft = WhipSeconds;
            _whipFrom = player;
            _whipFile = file;
            _whipStrain = strain;
            Hide(_rope2);
            HideMarks();
        }

        public void Hide()
        {
            _whipLeft = 0f;
            Hide(_rope1);
            Hide(_rope2);
            HideMarks();
        }

        void HideMarks()
        {
            Hide(_track);
            Hide(_herMark);
            Hide(_tearMark);
            Hide(_ring);
            _binCovered = false;
        }

        void Update()
        {
            if (_whipLeft <= 0f) return;
            _whipLeft -= Time.unscaledDeltaTime;
            if (_whipLeft <= 0f)
            {
                Hide(_rope1);
                return;
            }
            float k = 1f - _whipLeft / WhipSeconds;
            Vector2 end = Vector2.Lerp(_whipFrom, _whipFile, k * k);
            Rope(_rope1, end, _whipFile, 0f, _whipStrain, RopeHot, 2f, 0f);
        }

        static Vector2 Edge(Vector2 file, Vector2 toward)
        {
            Vector2 d = toward - file;
            float len = d.magnitude;
            return len < 1f ? file : file + d / len * Mathf.Min(FileHalf, len);
        }

        static void Hide(List<Image> dots)
        {
            foreach (var d in dots) if (d.enabled) d.enabled = false;
        }

        /// <summary>
        /// Dots every 3 px (wider when the rope is longer than the pool) from <paramref name="a"/> to <paramref name="b"/>. Taut, they hum across
        /// the rope with the strain (continuous time, so the same at any frame rate); slack, they sag in a curve.
        /// </summary>
        void Rope(List<Image> dots, Vector2 a, Vector2 b, float sag, float strain, Color color, float size, float phase)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / RopeStep), 0, dots.Count);
            Vector2 across = len > 0.5f ? new Vector2(-d.y, d.x) / len : Vector2.up;
            float time = Time.unscaledTime;
            for (int i = 0; i < dots.Count; i++)
            {
                var img = dots[i];
                if (i >= n) { if (img.enabled) img.enabled = false; continue; }
                float t = (i + 0.5f) / n;
                Vector2 p = a + d * t;
                p += sag > 0f ? Vector2.down * (sag * 4f * t * (1f - t)) : across * (Mathf.Sin(time * 60f + i * 1.7f + phase) * strain * 3f);
                Place(img, p, size, color);
            }
        }

        /// <summary>The track: faint dots from her line to the finish, the stretch from the grab to the file filled pale (yours) or red (hers).</summary>
        void Track(TugReel m, Vector2 origin, Vector2 axis)
        {
            float from = -m.HerLine, to = m.Finish, s = m.S;
            int n = Mathf.Min(_track.Count, Mathf.FloorToInt((to - from) / TrackStep) + 1);
            // Pale dots (the plan's dark text colour does not show on the dark desktop): faint for the way to go, bright where the file has been
            // reeled (yours) and red where she has dragged it back (hers).
            Color faint = Palette.Highlight, mine = Palette.Highlight, hers = Palette.Red;
            faint.a = TrackAlpha;
            mine.a = hers.a = FillAlpha;
            for (int i = 0; i < _track.Count; i++)
            {
                var img = _track[i];
                if (i >= n) { if (img.enabled) img.enabled = false; continue; }
                float at = from + i * TrackStep;
                bool filled = s >= 0f ? at >= 0f && at <= s : at <= 0f && at >= s;
                Place(img, origin + axis * at, 2f, filled ? (s >= 0f ? mine : hers) : faint);
            }
        }

        /// <summary>Her line (a short red tick across the track) and, short of the bin, the amber dashed tear line.</summary>
        void Marks(TugReel m, Vector2 origin, Vector2 axis)
        {
            Vector2 across = new Vector2(-axis.y, axis.x);
            Tick(_herMark, origin - axis * m.HerLine, across, 5, 2f, Palette.Red, false);
            if (m.FinishIsBin) Hide(_tearMark);
            else Tick(_tearMark, origin + axis * m.Finish, across, 9, 2f, Palette.Amber, true);
        }

        void Tick(List<Image> dots, Vector2 centre, Vector2 across, int count, float step, Color color, bool dashed)
        {
            for (int i = 0; i < dots.Count; i++)
            {
                var img = dots[i];
                // Dashed: every third square is left out (2 px on, 2 px on, a gap).
                if (i >= count || (dashed && i % 3 == 2)) { if (img.enabled) img.enabled = false; continue; }
                float k = (i - (count - 1) * 0.5f) * step;
                Place(img, centre + across * k, 2f, color);
            }
        }

        /// <summary>A window over the bin (the hit test at its centre is not the bin): a 1 px amber dotted ring shows where it is.</summary>
        void Ring()
        {
            var bin = _g.Desktop != null ? _g.Desktop.DisposalIcon : null;
            if (bin == null || bin.Hit == null) { Hide(_ring); return; }
            _coverCheck -= Time.unscaledDeltaTime;
            if (_coverCheck <= 0f)
            {
                _coverCheck = 0.1f;
                _binCovered = _g.Router.HitTest(bin.Hit.Center, _g.Player) != bin.Hit;
            }
            if (!_binCovered) { Hide(_ring); return; }
            Rect r = bin.Hit.WorldRect;
            r = new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f);
            float perimeter = 2f * (r.width + r.height);
            int n = Mathf.Min(_ring.Count, Mathf.FloorToInt(perimeter / 3f));
            for (int i = 0; i < _ring.Count; i++)
            {
                var img = _ring[i];
                if (i >= n) { if (img.enabled) img.enabled = false; continue; }
                Place(img, OnPerimeter(r, i * perimeter / n), 1f, Palette.Amber);
            }
        }

        static Vector2 OnPerimeter(Rect r, float d)
        {
            if (d < r.width) return new Vector2(r.xMin + d, r.yMin);
            d -= r.width;
            if (d < r.height) return new Vector2(r.xMax, r.yMin + d);
            d -= r.height;
            if (d < r.width) return new Vector2(r.xMax - d, r.yMax);
            return new Vector2(r.xMin, r.yMax - (d - r.width));
        }

        static void Place(Image img, Vector2 p, float size, Color color)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
            if (rt.sizeDelta.x != size) rt.sizeDelta = new Vector2(size, size);
            img.color = color;
            if (!img.enabled) img.enabled = true;
        }
    }
}
