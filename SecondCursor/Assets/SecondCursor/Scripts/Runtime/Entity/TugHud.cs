using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase H (blind playtest): the tug-of-war explains itself where it happens. While two cursors grip the same file,
    /// a NEXUS label above it says who is pulling and what to do, with a pull meter (YOU on the left, 017 on the right)
    /// that follows the fight; when it ends the label says who kept the file. A release over the Disposal bin during a
    /// fight gets a Disposal notice, so a lost tug never reads as a drop the bin ignored. Nothing here changes the fight.
    /// Phase I (second blind playtest): a small arrow on the file points away from her pointer (the way to drag), a lost
    /// fight says why (you let go, or she pulled harder), and a file she takes while you are not holding it says so.
    /// </summary>
    public sealed class TugHud : MonoBehaviour
    {
        const int Width = 212, MeterH = 8, Pad = 5, Gap = 18;
        const float WonSeconds = 1.6f, LostSeconds = 2.4f;

        GameServices _g;
        RectTransform _panel;
        PixelText _label;
        RectTransform _meter;
        Image _you, _them, _mark;
        PixelText _youText, _themText;
        int _textH;
        float _resultUntil = -1f;
        Vector2 _anchor;
        bool _refusedShown;
        CursorAgent _winner;
        /// <summary>Pixel-art arrow on the contested file: dark backing squares under bright ones.</summary>
        readonly List<Image> _arrowBack = new List<Image>();
        readonly List<Image> _arrowDots = new List<Image>();

        public static TugHud Create(GameServices g, ConflictSystem conflict)
        {
            var root = UIBuilder.Rect("Tug HUD", g.Layers.Effects);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0f);
            var hud = root.gameObject.AddComponent<TugHud>();
            hud._g = g;
            hud._panel = root;
            hud.Build();
            conflict.TugStarted += hud.OnStarted;
            conflict.TugEnded += hud.OnEnded;
            root.gameObject.SetActive(false);
            return hud;
        }

        void Build()
        {
            var face = _panel.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            _label = UIBuilder.Text(_panel, "", Palette.Text, true, "Label");
            _label.Align = TextAlign.Center;
            _label.Wrap = true;

            _meter = UIBuilder.Rect("Meter", _panel);
            var track = UIBuilder.Bevel(_meter, BevelStyle.Sunken, "Track");
            track.rectTransform.Stretch(0, 0, 0, 0);
            _you = UIBuilder.Solid(_meter, Palette.Selection, "You");
            _them = UIBuilder.Solid(_meter, Palette.Red, "Them");
            _mark = UIBuilder.Solid(_meter, Palette.Dark, "Mark");
            _youText = UIBuilder.Text(_panel, "", Palette.Selection, true, "You Label");
            _themText = UIBuilder.Text(_panel, "", Palette.Red, true, "Them Label");
            _themText.Align = TextAlign.Right;
            BuildArrow();
        }

        /// <summary>
        /// The arrow in its own frame, x along the direction to drag, y across (px): a 3 px thick shaft and a filled head, drawn
        /// with 4 px squares on a 3 px grid.
        /// </summary>
        static readonly Vector2[] ArrowShape = BuildShape();

        static Vector2[] BuildShape()
        {
            var pts = new List<Vector2>();
            for (int x = 0; x <= 27; x += 3) pts.Add(new Vector2(x, 0f));
            for (int y = -6; y <= 6; y += 3) pts.Add(new Vector2(30f, y));
            for (int y = -3; y <= 3; y += 3) pts.Add(new Vector2(33f, y));
            pts.Add(new Vector2(36f, 0f));
            return pts.ToArray();
        }

        void BuildArrow()
        {
            var layer = _g.Layers.Effects;
            for (int i = 0; i < ArrowShape.Length; i++)
            {
                var back = UIBuilder.Solid(layer, new Color(0f, 0f, 0f, 0.75f), "Tug Arrow Back " + i);
                back.rectTransform.anchorMin = back.rectTransform.anchorMax = Vector2.zero;
                back.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                back.rectTransform.sizeDelta = new Vector2(6f, 6f);
                back.enabled = false;
                _arrowBack.Add(back);
                var dot = UIBuilder.Solid(layer, new Color(1f, 0.93f, 0.55f, 1f), "Tug Arrow " + i);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = Vector2.zero;
                dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                dot.rectTransform.sizeDelta = new Vector2(4f, 4f);
                dot.enabled = false;
                _arrowDots.Add(dot);
            }
        }

        /// <summary>True while the arrow is drawn (the bridge and the tests read it).</summary>
        public bool ArrowShown { get; private set; }

        /// <summary>
        /// The arrow on the file: from its edge, straight away from her pointer (the way to drag). It only reads the two
        /// pointers, never the fight, so it cannot change what the fight does.
        /// </summary>
        void UpdateArrow(Vector2 file, bool show)
        {
            Vector2 away = _g.Player.Position - _g.EntityAgent.Position;
            show &= away.sqrMagnitude > 4f;
            ArrowShown = show;
            if (!show)
            {
                for (int i = 0; i < _arrowDots.Count; i++) { _arrowDots[i].enabled = false; _arrowBack[i].enabled = false; }
                return;
            }
            Vector2 dir = away.normalized;
            Vector2 side = new Vector2(-dir.y, dir.x);
            const float start = 24f;
            for (int i = 0; i < ArrowShape.Length; i++)
            {
                Vector2 q = ArrowShape[i];
                Vector2 at = file + dir * (start + q.x) + side * q.y;
                Vector2 p = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
                _arrowBack[i].rectTransform.anchoredPosition = p;
                _arrowDots[i].rectTransform.anchoredPosition = p;
                // A bright band runs along the shaft toward the tip so the direction reads even at a glance.
                float wave = Mathf.Repeat(Time.unscaledTime * 2.4f - q.x * 0.03f, 1f);
                float k = q.x >= 30f ? 1f : (wave < 0.4f ? 1f : 0.65f);
                _arrowDots[i].color = new Color(1f, 0.93f, 0.55f, k);
                _arrowBack[i].enabled = true;
                _arrowDots[i].enabled = true;
            }
        }

        string T(string key, string fallback) => _g.Content != null ? _g.Content.Text(key, fallback) : fallback;

        void OnStarted(DragPayload p)
        {
            _refusedShown = false;
            _resultUntil = -1f;
            SetText(T("tug.label", "SESSION 017 IS PULLING.\nHOLD THE BUTTON AND DRAG AWAY."), Palette.Text, true);
            _panel.gameObject.SetActive(true);
            GameLog.Info(LogChannel.Entity, "Tug HUD shown");
            Place(_g.Conflict.ObjectPosition);
        }

        void OnEnded(DragPayload p, TugOutcome outcome)
        {
            bool won = outcome == TugOutcome.PlayerWins;
            // Phase I: a lost fight says why. You let go of the button, or she pulled harder.
            string lost = _g.Conflict.LastLostByRelease
                ? T("tug.lost.release", "YOU LET GO.\nHOLD THE BUTTON UNTIL YOU KEPT THE FILE.")
                : T("tug.lost.pulled", "SESSION 017 PULLED HARDER.\nDRAG FASTER, AWAY FROM IT.");
            SetText(won ? T("tug.won", "YOU KEPT THE FILE.") : lost, won ? Palette.Green : Palette.Red, false);
            UpdateArrow(Vector2.zero, false);
            _resultUntil = Time.unscaledTime + (won ? WonSeconds : LostSeconds);
            _winner = won ? _g.Player : _g.EntityAgent;
            GameLog.Info(LogChannel.Entity, "Tug HUD: " + (won ? "kept" : (_g.Conflict.LastLostByRelease ? "taken (you let go)" : "taken (pulled harder)")));
            // Let go over the bin mid-fight: the bin did not ignore the drop, the other session still held the file.
            if (!won && _g.Conflict.LastLostByRelease && !_refusedShown && OverDisposal(_g.Conflict.LastEndPlayerPosition))
            {
                _refusedShown = true;
                string name = p != null && !string.IsNullOrEmpty(p.Label) ? p.Label : "The file";
                _g.Notifications.Show(T("app.disposal", "Disposal"), _g.Content.Format("tug.refused", name), "icon_error", null, "sys_error");
                GameLog.Info(LogChannel.OS, "Disposal refused " + name + ": still held by session 017");
            }
        }

        /// <summary>
        /// A line beside a point with no fight going on: a file she took while the player was not holding it. It uses the
        /// same panel and time as the result of a fight.
        /// </summary>
        public void ShowMessage(string text, Vector2 at)
        {
            if (_g.Conflict != null && _g.Conflict.IsFighting) return;
            SetText(text, Palette.Red, false);
            _resultUntil = Time.unscaledTime + LostSeconds;
            _winner = null;
            _anchor = at;
            _panel.gameObject.SetActive(true);
            UpdateArrow(Vector2.zero, false);
            Place(at);
            GameLog.Info(LogChannel.Entity, "Tug HUD: snatched (not holding)");
        }

        bool OverDisposal(Vector2 at)
        {
            var bin = _g.Desktop != null ? _g.Desktop.DisposalIcon : null;
            return bin != null && bin.Hit != null && bin.Hit.WorldRect.Contains(at);
        }

        void SetText(string text, Color32 color, bool meter)
        {
            _label.text = text ?? "";
            _label.color = color;
            int inner = Width - Pad * 2;
            _textH = PixelFont.Measure(_label.text, inner, true, 1).y;
            _label.rectTransform.At(Pad, Pad, inner, _textH + 2);
            _meter.gameObject.SetActive(meter);
            _youText.gameObject.SetActive(meter);
            _themText.gameObject.SetActive(meter);
            int h = Pad + _textH + (meter ? 4 + MeterH + 2 : 0) + Pad + 1;
            _panel.sizeDelta = new Vector2(Width, h);
            if (!meter) return;
            _youText.text = T("tug.you", "YOU");
            _themText.text = T("tug.them", "017");
            int y = Pad + _textH + 4;
            _youText.rectTransform.At(Pad, y - 2, 30, 12);
            _themText.rectTransform.At(Width - Pad - 30, y - 2, 30, 12);
            _meter.At(Pad + 30, y, inner - 60, MeterH);
        }

        void SetMeter(float lead)
        {
            float w = _meter.rect.width - 2f;
            float you = Mathf.Round(Mathf.Clamp01(lead) * w);
            _you.rectTransform.At(1, 1, you, MeterH - 2);
            _them.rectTransform.At(1 + you, 1, w - you, MeterH - 2);
            _mark.rectTransform.At(Mathf.Clamp(you, 0f, w), 0, 2, MeterH);
        }

        /// <summary>Where the label may sit around the file: above, below, left, right (the first that hides no cursor wins).</summary>
        int _side;

        void Place(Vector2 obj)
        {
            _anchor = obj;
            // Both pointers must stay visible (the player pulls away from hers): keep the side that covers neither,
            // and only move when a pointer comes under the label.
            if (Covers(Candidate(obj, _side)))
            {
                for (int s = 0; s < 4; s++)
                {
                    if (Covers(Candidate(obj, s))) continue;
                    _side = s;
                    break;
                }
            }
            var r = Candidate(obj, _side);
            _panel.anchoredPosition = new Vector2(Mathf.Round(r.center.x), Mathf.Round(r.yMin));
        }

        /// <summary>The label's rect (virtual px, y up) on one side of the file, kept on screen.</summary>
        Rect Candidate(Vector2 obj, int side)
        {
            float h = _panel.sizeDelta.y;
            float cx = obj.x, y;
            switch (side)
            {
                case 1: y = obj.y - Gap - 36f - h; break;                     // below the file and its label
                case 2: cx = obj.x - Width * 0.5f - 34f; y = obj.y - h * 0.5f; break;
                case 3: cx = obj.x + Width * 0.5f + 34f; y = obj.y - h * 0.5f; break;
                default: y = obj.y + Gap; break;                               // above
            }
            cx = Mathf.Clamp(cx, Width * 0.5f + 2f, ScreenRig.Width - Width * 0.5f - 2f);
            y = Mathf.Clamp(y, WindowManager.TaskbarHeight + 2f, ScreenRig.Height - h - 2f);
            return new Rect(cx - Width * 0.5f, y, Width, h);
        }

        bool Covers(Rect r)
        {
            var grow = new Rect(r.x - 10f, r.y - 14f, r.width + 20f, r.height + 24f);
            return grow.Contains(_g.Player.Position) || (_g.EntityAgent.Visible && grow.Contains(_g.EntityAgent.Position));
        }

        void LateUpdate()
        {
            if (_g == null || _panel == null) return;
            var c = _g.Conflict;
            if (c != null && c.IsFighting)
            {
                if (!_meter.gameObject.activeSelf) OnStarted(null);
                SetMeter(c.PlayerLead);
                Place(c.ObjectPosition);
                UpdateArrow(c.ObjectPosition, true);
                return;
            }
            if (_resultUntil > 0f && Time.unscaledTime < _resultUntil)
            {
                // The result follows the file: it is in the winner's hand now.
                Place(_winner != null ? _winner.Position : _anchor);
                return;
            }
            if (ArrowShown) UpdateArrow(Vector2.zero, false);
            if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
            _resultUntil = -1f;
        }
    }
}
