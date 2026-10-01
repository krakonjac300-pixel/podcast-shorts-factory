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
    /// Phase J (third blind playtest): the label says what winning looks like (the bar is yours past its line, then letting
    /// go drops the file), the arrow points toward open screen, and the result stays up longer and is also posted as a notice.
    /// Phase P: the reel's words (haul.*, <see cref="TugText"/>): the arrows point at the bin, the keep line sits at half way, and the
    /// re-grip window says GRAB IT.
    /// </summary>
    public sealed class TugHud : MonoBehaviour
    {
        const int Width = 212, MeterH = 8, Pad = 5, Gap = 18;
        const float ResultSeconds = 3.2f;

        GameServices _g;
        RectTransform _panel;
        PixelText _label;
        RectTransform _meter;
        Image _you, _them, _mark, _line;
        PixelText _youText, _themText;
        int _textH;
        float _resultUntil = -1f;
        Vector2 _anchor;
        bool _refusedShown;
        CursorAgent _winner;
        /// <summary>Pixel-art arrow on the contested file: dark backing squares under bright ones.</summary>
        readonly List<Image> _arrowBack = new List<Image>();
        readonly List<Image> _arrowDots = new List<Image>();
        /// <summary>Phase K: the same arrow at twice the size at the player's pointer for the first moments of every fight.</summary>
        readonly List<Image> _bigBack = new List<Image>();
        readonly List<Image> _bigDots = new List<Image>();
        const float BigArrowSeconds = 1f;
        /// <summary>Where the arrow was drawn last (it stays there, dimmed, while the result shows).</summary>
        Vector2 _arrowAt;

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
            // Phase J: the line past which the file is yours (letting go keeps it).
            _line = UIBuilder.Solid(_meter, Palette.Amber, "Keep Line");
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
            BuildDots(_arrowBack, _arrowDots, 1, "Tug Arrow");
            BuildDots(_bigBack, _bigDots, 2, "Tug Big Arrow");
        }

        void BuildDots(List<Image> backs, List<Image> dots, int scale, string name)
        {
            var layer = _g.Layers.Effects;
            for (int i = 0; i < ArrowShape.Length; i++)
            {
                var back = UIBuilder.Solid(layer, new Color(0f, 0f, 0f, 0.75f), name + " Back " + i);
                back.rectTransform.anchorMin = back.rectTransform.anchorMax = Vector2.zero;
                back.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                back.rectTransform.sizeDelta = new Vector2(6f * scale, 6f * scale);
                back.enabled = false;
                backs.Add(back);
                var dot = UIBuilder.Solid(layer, ArrowColor, name + " " + i);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = Vector2.zero;
                dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                dot.rectTransform.sizeDelta = new Vector2(4f * scale, 4f * scale);
                dot.enabled = false;
                dots.Add(dot);
            }
        }

        static readonly Color ArrowColor = new Color(1f, 0.93f, 0.55f, 1f);
        bool _arrowShown;
        /// <summary>The arrow's tip (the label keeps clear of it, like it does of the pointers).</summary>
        Vector2 _arrowTip;

        /// <summary>
        /// The arrow on the file: from its edge, the way to drag (Phase J: away from her, turned toward open screen, so a pull
        /// never runs into a corner). It only shows the fight's direction, so it cannot change what the fight does.
        /// </summary>
        void UpdateArrow(Vector2 file, bool show, float alpha = 1f)
        {
            _arrowShown = show && _g.Conflict.PullDirection.sqrMagnitude > 0.5f;
            if (_arrowShown)
            {
                _arrowAt = file;
                _arrowTip = file + _g.Conflict.PullDirection.normalized * 60f;
            }
            DrawArrow(_arrowBack, _arrowDots, file, 24f, 1, _arrowShown, alpha);
        }

        /// <summary>
        /// Phase K: for the first second of every fight the arrow also shows at twice the size at the player's own pointer, where the
        /// eyes are when the file is grabbed (the blind tester never found the small one in time).
        /// </summary>
        void UpdateBigArrow(bool show)
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f));
            _bigShown = show && _g.Conflict.PullDirection.sqrMagnitude > 0.5f;
            DrawArrow(_bigBack, _bigDots, _g.Player.Position, 14f, 2, _bigShown, pulse);
        }

        /// <summary>Phase N: the big arrow is up (the label keeps off its shaft, which runs about 90 px from the pointer).</summary>
        bool _bigShown;

        void DrawArrow(List<Image> backs, List<Image> dots, Vector2 from, float start, int scale, bool show, float alpha)
        {
            if (!show)
            {
                for (int i = 0; i < dots.Count; i++) { dots[i].enabled = false; backs[i].enabled = false; }
                return;
            }
            Vector2 dir = _g.Conflict.PullDirection.normalized;
            Vector2 side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < ArrowShape.Length; i++)
            {
                Vector2 q = ArrowShape[i] * scale;
                Vector2 at = from + dir * (start + q.x) + side * q.y;
                Vector2 p = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
                backs[i].rectTransform.anchoredPosition = p;
                dots[i].rectTransform.anchoredPosition = p;
                // A bright band runs along the shaft toward the tip so the direction reads even at a glance.
                float wave = Mathf.Repeat(Time.unscaledTime * 2.4f - q.x * 0.03f / scale, 1f);
                float k = q.x >= 30f * scale ? 1f : (wave < 0.4f ? 1f : 0.65f);
                dots[i].color = new Color(1f, 0.93f, 0.55f, k * alpha);
                backs[i].color = new Color(0f, 0f, 0f, 0.75f * alpha);
                backs[i].enabled = true;
                dots[i].enabled = true;
            }
        }

        string T(string key, string fallback) => _g.Content != null ? _g.Content.Text(key, fallback) : fallback;

        /// <summary>Phase L: the first fight on a save carries the whole lesson in its label (the Quick Start no longer does).</summary>
        bool _first;

        void OnStarted(DragPayload p)
        {
            if (p != null) _first = _g.Tips.Claim("tug");
            _refusedShown = false;
            _resultUntil = -1f;
            ShowFight(FightState());
            _panel.gameObject.SetActive(true);
            GameLog.Info(LogChannel.Entity, "Tug HUD shown");
            Place(_g.Conflict.ObjectPosition);
        }

        /// <summary>
        /// What the label says while a fight runs: Phase N's GET READY (amber), the fight's own words, Phase P's re-grip window (red) and,
        /// past the keep line, that letting go now keeps the file (green).
        /// </summary>
        enum FightText { None, Ready, Pull, Regrip, Ahead }
        FightText _shown;
        /// <summary>A deep amber that reads on the pale label (the taskbar clock's).</summary>
        static readonly Color32 ReadyText = new Color32(0xA8, 0x62, 0x00, 0xFF);

        /// <summary>Phase P: the words of the model in play (the speed model's tug.*, the reel's haul.*).</summary>
        TugModel Model => _g.Conflict.CurrentSettings.model;
        string Key(string part) => TugText.Key(Model, _g.Conflict.Reel != null ? _g.Conflict.Reel.Variant : TugVariant.Fight, part);

        FightText FightState()
        {
            var c = _g.Conflict;
            return c.InReady ? FightText.Ready : c.InRegrip ? FightText.Regrip : c.PlayerKeepsOnRelease ? FightText.Ahead : FightText.Pull;
        }

        void ShowFight(FightText state)
        {
            _shown = state;
            // Phase K: the speed model's lines name the arrow's direction ("HOLD AND DRAG DOWN-LEFT UNTIL THE BAR IS YOURS.").
            string way = _g.Conflict.ArrowDirection;
            bool fight = _g.Conflict.Reel == null || _g.Conflict.Reel.Variant == TugVariant.Fight;
            switch (state)
            {
                case FightText.Ready: SetText(F(Key("ready"), way), ReadyText, true); break;
                case FightText.Regrip: SetText(F(Key("regrip"), way), Palette.Red, true); break;
                case FightText.Ahead: SetText(F(Key("ahead"), way), Palette.Green, true); break;
                default: SetText(F(Key(_first && fight ? "label.first" : "label"), way), Palette.Text, true); break;
            }
        }

        string F(string key, params object[] args) => _g.Content != null ? _g.Content.Format(key, args) : key;

        /// <summary>Phase K: the result's own words and notice key for what the player's pointer did.</summary>
        static string ReasonKey(TugLossReason r)
        {
            switch (r)
            {
                case TugLossReason.LetGo: return "release";
                case TugLossReason.HeldStill: return "still";
                case TugLossReason.WrongWay: return "wrong";
                case TugLossReason.Stopped: return "stopped";
                case TugLossReason.TooSlow: return "slow";
                default: return "pulled";
            }
        }

        void OnEnded(DragPayload p, TugOutcome outcome)
        {
            var c = _g.Conflict;
            _first = false;
            _shown = FightText.None;
            _resultUntil = Time.unscaledTime + ResultSeconds;
            UpdateArrow(_arrowAt, true, 0.45f);
            UpdateBigArrow(false);
            if (outcome == TugOutcome.Released)
            {
                // Phase P: the finale's LetGo hold was let go early: nobody won, and the file lies where the pointer let go of it.
                SetText(T("haul.letgo.released", "YOU LET GO. THE FILE IS STILL HERE.\nGRAB IT AGAIN."), Palette.Text, false);
                _winner = _g.Player;
                GameLog.Info(LogChannel.Entity, "Tug HUD: released");
                return;
            }
            bool won = outcome == TugOutcome.PlayerWins;
            bool letGo = !won && c.LastLostByRelease;
            // Phase I: a lost fight says why. Phase K: from what the pointer really did, with the arrow's direction by name ("YOU
            // PULLED LEFT. THE ARROW POINTED DOWN."), and the bar stays up where it ended, so the player sees how close it was.
            // Phase P, the reel: a win says where the file is (in the bin, torn loose, or kept where it was let go).
            bool kept = won && Model == TugModel.Reel && c.LastKeptOnRelease;
            string reason = !won ? ReasonKey(c.LastLossReason) : Model == TugModel.Speed || c.LastWonIntoBin ? "won" : kept ? "kept" : "won.tear";
            SetText(won ? F(Key(reason)) : F(Key("lost." + reason), c.LastArrowDirection, c.LastPlayerDirection), won ? Palette.Green : Palette.Red, true);
            SetMeter(won && !kept ? 1f : c.LastFinalLead);
            _winner = won ? _g.Player : _g.EntityAgent;
            if (c.LastWonIntoBin)
            {
                // Phase P: the file is in the bin and Confirm Shred is opening under the pointer: the result stays by the bin.
                _winner = null;
                _anchor = _g.Desktop.DisposalIcon.Hit.Center;
            }
            GameLog.Info(LogChannel.Entity, "Tug HUD: " + (won ? "kept (" + reason + ")" : "taken (" + reason + ")"));
            string name = p != null && !string.IsNullOrEmpty(p.Label) ? p.Label : "the file";
            // Let go over the bin mid-fight: the bin did not ignore the drop, the other session still held the file.
            if (letGo && !_refusedShown && OverDisposal(_g.Conflict.LastEndPlayerPosition))
            {
                _refusedShown = true;
                _g.Notifications.Show(T("app.disposal", "Disposal"), _g.Content.Format(Key("refused"), name), "icon_error", null, "sys_error");
                GameLog.Info(LogChannel.OS, "Disposal refused " + name + ": still held by session 017");
                return;
            }
            // Phase J: the result is also a notice, for a player who was looking somewhere else when the fight ended.
            string key = TugText.NoticeKey(Model, reason == "pulled" ? "" : reason);
            _g.Notifications.Show(T("os.name", "NEXUS OS"), _g.Content.Format(key, name, c.LastArrowDirection, c.LastPlayerDirection),
                won ? "icon_info" : "icon_error", null, won ? "ui_select" : "sys_warning");
        }

        /// <summary>
        /// A line beside a point with no fight going on: a file she took while the player was not holding it. It uses the
        /// same panel and time as the result of a fight.
        /// </summary>
        public void ShowMessage(string text, Vector2 at)
        {
            if (_g.Conflict != null && _g.Conflict.IsFighting) return;
            SetText(text, Palette.Red, false);
            _resultUntil = Time.unscaledTime + ResultSeconds;
            _winner = null;
            _anchor = at;
            _panel.gameObject.SetActive(true);
            UpdateArrow(Vector2.zero, false);
            UpdateBigArrow(false);
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
            // The keep line stands out above and below the bar.
            _line.rectTransform.At(Mathf.Round(_g.Conflict.KeepLead * (inner - 62)), -3, 2, MeterH + 6);
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
            // and only move when a pointer comes under the label. Phase N: in a corner where every side covers something,
            // her pointer may go under the label, never the player's pointer or the arrow.
            if (Covers(Candidate(obj, _side), true))
            {
                int free = FreeSide(obj, true);
                if (free < 0 && Covers(Candidate(obj, _side), false)) free = FreeSide(obj, false);
                if (free >= 0) _side = free;
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

        int FreeSide(Vector2 obj, bool herPointer)
        {
            for (int s = 0; s < 4; s++)
                if (!Covers(Candidate(obj, s), herPointer)) return s;
            return -1;
        }

        bool Covers(Rect r, bool herPointer)
        {
            var grow = new Rect(r.x - 10f, r.y - 14f, r.width + 20f, r.height + 24f);
            Vector2 way = _g.Conflict.PullDirection.normalized;
            return grow.Contains(_g.Player.Position) || (herPointer && _g.EntityAgent.Visible && grow.Contains(_g.EntityAgent.Position))
                   || (_arrowShown && grow.Contains(_arrowTip))
                   || (_bigShown && (grow.Contains(_g.Player.Position + way * 50f) || grow.Contains(_g.Player.Position + way * 90f)));
        }

        void LateUpdate()
        {
            if (_g == null || _panel == null) return;
            var c = _g.Conflict;
            if (c != null && c.IsFighting)
            {
                if (!_meter.gameObject.activeSelf) OnStarted(null);
                if (FightState() != _shown) ShowFight(FightState());
                SetMeter(c.PlayerLead);
                Place(c.ObjectPosition);
                UpdateArrow(c.ObjectPosition, true);
                UpdateBigArrow(c.Elapsed < ConflictSystem.ReadySeconds + BigArrowSeconds);
                return;
            }
            if (_resultUntil > 0f && Time.unscaledTime < _resultUntil)
            {
                // The result follows the file: it is in the winner's hand now. The arrow and the bar stay as the fight ended.
                Place(_winner != null ? _winner.Position : _anchor);
                if (_arrowShown) UpdateArrow(_arrowAt, true, 0.45f);
                return;
            }
            if (_arrowShown) UpdateArrow(Vector2.zero, false);
            if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
            _resultUntil = -1f;
        }
    }
}
