using System.Collections;
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
    /// Phase H (blind playtest): the tug-of-war explains itself where it happens: who is pulling, what to do, a meter, and who kept the file
    /// (also posted as a notice; a release over the Disposal bin mid-fight gets a Disposal notice). Phases I to N added the arrows, the reasons
    /// for a loss, the keep line and GET READY. Phase P (review board R1, both models): the panel reads in one glance. It wears 017's dark skin
    /// with one 2x word (PULL, ALMOST IN, GRAB IT!, HOLD, IN THE BIN, YOURS, 017 HAS IT), up to two 1x lines under it and a compact 140 px meter
    /// with the keep line; GET READY shows a 108 px arrow at the pointer toward the bin and no meter; the fight keeps a 72 px arrow at the pointer;
    /// passing the keep line flashes the meter and rings the file in green (the latch). The panel sits on the side of the file away from the bin,
    /// clear of both pointers, the arrow and the track, and only moves when something comes under it. The result's notice posts 0.6 s after the
    /// result (notices wait during a fight). Nothing here changes the fight.
    /// </summary>
    public sealed class TugHud : MonoBehaviour
    {
        const int MinWidth = 150, MaxWidth = 284, MeterW = 140, MeterH = 8, Pad = 6, TagW = 22;
        const float ResultSeconds = 3.2f, NoticeDelay = 0.6f, LatchFlash = 0.12f, LatchRing = 0.2f, Margin = 12f;
        /// <summary>Phase Q4 (A3): the result stays about 16 characters a second (at least <see cref="ResultSeconds"/>), so it can be read.</summary>
        const float ResultSecondsPerChar = 0.06f;
        /// <summary>Phase Q4 (A3): the result notice is only kept in Recent notices when the player stands this close to the panel that says the same thing.</summary>
        const float WatchRadius = 260f;
        float _resultSeconds = ResultSeconds;
        static readonly Color PanelFill = new Color(Palette.EntityFill.r / 255f, Palette.EntityFill.g / 255f, Palette.EntityFill.b / 255f, 0.92f);

        GameServices _g;
        RectTransform _panel;
        Image _fill;
        readonly Image[] _border = new Image[4], _ring = new Image[4];
        PixelText _word, _lines, _youText, _themText;
        RectTransform _meter;
        Image _you, _them, _keep, _keepEdge;
        TugArrow _small, _big, _bigReady;
        float _resultUntil = -1f, _latchAt = -10f;
        Vector2 _anchor;
        Rect _avoid;
        bool _refusedShown, _first, _wasAhead;
        CursorAgent _winner;
        Coroutine _notice;

        enum FightText { None, Ready, Pull, Regrip, Ahead }
        FightText _shown;
        bool _shownSurge, _shownBlink;

        public static TugHud Create(GameServices g, ConflictSystem conflict)
        {
            var root = UIBuilder.Rect("Tug HUD", g.Layers.Effects);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
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
            _fill = UIBuilder.Solid(_panel, PanelFill, "Fill");
            _fill.rectTransform.Stretch();
            for (int i = 0; i < 4; i++) _border[i] = UIBuilder.Solid(_panel, Palette.EntityOutline, "Border " + i);
            _word = UIBuilder.Text(_panel, "", Palette.EntityText, true, "Word");
            _word.Scale = 2;
            _word.Align = TextAlign.Center;
            _lines = UIBuilder.Text(_panel, "", Palette.EntityText, true, "Lines");
            _lines.Align = TextAlign.Center;
            _lines.Wrap = true;
            _meter = UIBuilder.Rect("Meter", _panel);
            var track = UIBuilder.Solid(_meter, new Color32(0x3A, 0x3E, 0x3C, 0xFF), "Track");
            track.rectTransform.Stretch();
            _you = UIBuilder.Solid(_meter, Palette.Highlight, "You");
            _them = UIBuilder.Solid(_meter, Palette.Red, "Them");
            // Phase J: the line past which letting go keeps the file, standing 3 px proud above and below the bar.
            // Phase Q4 (A8): the amber line is edged in dark so it reads on both fills (2.4:1 on the red alone).
            _keepEdge = UIBuilder.Solid(_meter, Palette.Dark, "Keep Edge");
            _keep = UIBuilder.Solid(_meter, Palette.Amber, "Keep Line");
            _youText = UIBuilder.Text(_panel, "", Palette.Highlight, true, "You Label");
            _themText = UIBuilder.Text(_panel, "", Palette.Red, true, "Them Label");
            _themText.Align = TextAlign.Right;
            var layer = _g.Layers.Effects;
            _small = new TugArrow(layer, 1, "Tug Arrow");
            _big = new TugArrow(layer, 2, "Tug Big Arrow");
            _bigReady = new TugArrow(layer, 3, "Tug Ready Arrow");
            for (int i = 0; i < 4; i++)
            {
                _ring[i] = UIBuilder.Solid(layer, Palette.GreenOnDark, "Latch Ring " + i);
                _ring[i].rectTransform.anchorMin = _ring[i].rectTransform.anchorMax = Vector2.zero;
                _ring[i].rectTransform.pivot = Vector2.zero;
                _ring[i].enabled = false;
            }
        }

        string T(string key, string fallback) => _g.Content != null ? _g.Content.Text(key, fallback) : fallback;
        string F(string key, params object[] args) => _g.Content != null ? _g.Content.Format(key, args) : key;

        ConflictSystem C => _g.Conflict;
        /// <summary>Phase P: the words of the model in play (the speed model's tug.*, the reel's haul.*).</summary>
        TugModel Model => C.CurrentSettings.model;
        TugVariant Variant => C.Reel != null ? C.Reel.Variant : C.IsAssisted ? TugVariant.Hold : TugVariant.Fight;
        string Key(string part) => TugText.Key(Model, Variant, part);

        void OnStarted(DragPayload p)
        {
            if (p != null) _first = _g.Tips.Claim("tug");
            if (_notice != null) { StopCoroutine(_notice); _notice = null; }
            _refusedShown = false;
            _resultUntil = -1f;
            _latchAt = -10f;
            _wasAhead = false;
            _small.Hide();
            ShowFight(FightState());
            _panel.gameObject.SetActive(true);
            GameLog.Info(LogChannel.Entity, "Tug HUD shown");
            Place(C.ObjectPosition, true);
        }

        FightText FightState()
        {
            var c = C;
            return c.InReady ? FightText.Ready : c.InRegrip ? FightText.Regrip : c.PlayerKeepsOnRelease ? FightText.Ahead : FightText.Pull;
        }

        bool Surging => C.Reel != null && C.Reel.Surging;
        bool BlinkOn => (_g.Fx != null && _g.Fx.ReduceFlashing) || Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.5f;

        /// <summary>The 2x word for the fight's state: PULL (the speed model names the arrow's way), ALMOST IN, GRAB IT!, HOLD.</summary>
        string Word(FightText state)
        {
            if (state == FightText.Regrip) return T("haul.word.regrip", "GRAB IT!");
            if (state == FightText.Ahead) return Model == TugModel.Speed ? T("haul.word.kept", "YOURS") : T("haul.word.ahead", "ALMOST IN");
            if (Variant != TugVariant.Fight) return T("haul.word.hold", "HOLD");
            return Model == TugModel.Speed ? F("tug.word", C.ArrowDirection) : T("haul.word", "PULL");
        }

        void ShowFight(FightText state)
        {
            _shown = state;
            _shownSurge = Surging;
            _shownBlink = BlinkOn;
            // Phase K: the speed model's lines name the arrow's direction ("HOLD AND DRAG DOWN-LEFT UNTIL THE BAR IS YOURS.").
            string way = C.ArrowDirection;
            string word = Word(state);
            switch (state)
            {
                case FightText.Ready:
                    SetText(word, Palette.EntityText, F(Key("ready"), way), Palette.Amber, false);
                    break;
                case FightText.Regrip:
                    SetText(word, _shownBlink ? Palette.Red : Palette.EntityFill, F(Key("regrip"), way), Palette.EntityText, true);
                    break;
                case FightText.Ahead:
                    SetText(word, Palette.GreenOnDark, F(Key("ahead"), way), Palette.EntityText, true);
                    break;
                default:
                    string part = _first && Variant == TugVariant.Fight ? "label.first" : "label";
                    SetText(word, _shownSurge ? Palette.Red : Palette.EntityText, F(Key(part), way), Palette.EntityText, true);
                    break;
            }
        }

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
            var c = C;
            _first = false;
            _shown = FightText.None;
            _resultSeconds = ResultSeconds;
            _resultUntil = Time.unscaledTime + _resultSeconds;
            _bigReady.Hide();
            string name = p != null && !string.IsNullOrEmpty(p.Label) ? p.Label : "the file";
            if (outcome == TugOutcome.Released)
            {
                // Phase P: the finale's LetGo hold was let go early: nobody won, and the file lies where the pointer let go of it.
                _big.Hide();
                // The words live in Night 3's strings only (no fallback here: the demo build must not carry the finale's lines).
                SetText(T("haul.word.released", ""), Palette.EntityText, T("haul.letgo.released", ""), Palette.EntityText, false);
                _winner = null;
                _anchor = c.ObjectPosition;
                GameLog.Info(LogChannel.Entity, "Tug HUD: released");
                PostLater(T("os.name", "NEXUS OS"), F("notify.haul.letgo.released", name), "icon_info", "ui_select", true);
                return;
            }
            bool won = outcome == TugOutcome.PlayerWins;
            bool letGo = !won && c.LastLostByRelease;
            // Phase I: a lost fight says why. Phase K: from what the pointer really did, with the direction by name, and the bar stays up where
            // it ended. Phase P, the reel: a win says where the file is (in the bin, torn loose, or kept where it was let go).
            bool kept = won && Model == TugModel.Reel && c.LastKeptOnRelease;
            string reason = !won ? ReasonKey(c.LastLossReason) : Model == TugModel.Speed || c.LastWonIntoBin ? "won" : kept ? "kept" : "won.tear";
            string word = !won ? T("haul.word.lost", "017 HAS IT") : c.LastWonIntoBin ? T("haul.word.won", "IN THE BIN") : T("haul.word.kept", "YOURS");
            string lines = won ? F(Key(reason)) : F(Key("lost." + reason), c.LastArrowDirection, c.LastPlayerDirection);
            SetText(word, won ? Palette.GreenOnDark : Palette.Red, lines, Palette.EntityText, true);
            // Phase Q4 (A3): about 16 characters a second (the usual 65 characters stay about 4 s, not 3.2).
            _resultSeconds = Mathf.Max(ResultSeconds, ResultSecondsPerChar * lines.Length);
            _resultUntil = Time.unscaledTime + _resultSeconds;
            SetMeter(won && !kept ? 1f : c.LastFinalLead, false);
            // The arrows stay where the fight left them, dimmed (the speed model's small arrow comes back on the file).
            if (_big.Shown) _big.Draw(_big.From, _big.Direction, 14f, 0.35f, false);
            if (Model == TugModel.Speed) _small.Draw(c.ObjectPosition, c.PullDirection, 24f, 0.45f, false);
            _winner = won ? _g.Player : _g.EntityAgent;
            if (c.LastWonIntoBin)
            {
                // Phase P: the file is in the bin and Confirm Shred is opening under the pointer: the result stays by the bin.
                _winner = null;
                _anchor = _g.Desktop.DisposalIcon.Hit.Center;
            }
            GameLog.Info(LogChannel.Entity, "Tug HUD: " + (won ? "kept (" + reason + ")" : "taken (" + reason + ")"));
            // Let go over the bin mid-fight: the bin did not ignore the drop, the other session still held the file.
            if (letGo && !_refusedShown && OverDisposal(c.LastEndPlayerPosition))
            {
                _refusedShown = true;
                PostLater(T("app.disposal", "Disposal"), F(Key("refused"), name), "icon_error", "sys_error");
                GameLog.Info(LogChannel.OS, "Disposal refused " + name + ": still held by session 017");
                return;
            }
            // Phase J: the result is also a notice, for a player who was looking somewhere else when the fight ended.
            string key = TugText.NoticeKey(Model, reason == "pulled" ? "" : reason);
            PostLater(T("os.name", "NEXUS OS"), F(key, name, c.LastArrowDirection, c.LastPlayerDirection), won ? "icon_info" : "icon_error", won ? "ui_select" : "sys_warning", true);
        }

        /// <summary>Phase P (R1 item 5): the result's notice comes 0.6 s after the result, once the dim is going (notices wait during a fight).</summary>
        void PostLater(string title, string body, string icon, string sound, bool sameAsPanel = false)
        {
            if (_notice != null) StopCoroutine(_notice);
            _notice = StartCoroutine(Post(title, body, icon, sound, sameAsPanel));
        }

        IEnumerator Post(string title, string body, string icon, string sound, bool sameAsPanel)
        {
            yield return new WaitForSecondsRealtime(NoticeDelay);
            _notice = null;
            // Phase Q4 (A3): a result the panel beside the player's pointer already says is kept in Recent notices only (it is not said twice);
            // a player who looked away (the pointer is far from the panel) gets the notice. Session 017 is the one who acted.
            bool watching = sameAsPanel && _panel != null && _panel.gameObject.activeSelf
                            && Vector2.Distance(_g.Player.Position, _panel.WorldRect().center) < WatchRadius;
            if (watching) _g.Notifications.Record(title, body, Core.Game.NoticeKind.Entity);
            else _g.Notifications.Show(title, body, icon, null, sound, false, null, Core.Game.NoticeKind.Entity);
        }

        /// <summary>
        /// A line beside a point with no fight going on: a file she took while the player was not holding it. It uses the
        /// same panel and time as the result of a fight.
        /// </summary>
        public void ShowMessage(string text, Vector2 at)
        {
            if (C != null && C.IsFighting) return;
            SetText("", Palette.Red, text, Palette.Red, false);
            _resultUntil = Time.unscaledTime + ResultSeconds;
            _winner = null;
            _anchor = at;
            _panel.gameObject.SetActive(true);
            _small.Hide();
            _big.Hide();
            _bigReady.Hide();
            Place(at, true);
            GameLog.Info(LogChannel.Entity, "Tug HUD: snatched (not holding)");
        }

        bool OverDisposal(Vector2 at)
        {
            var bin = _g.Desktop != null ? _g.Desktop.DisposalIcon : null;
            return bin != null && bin.Hit != null && bin.Hit.WorldRect.Contains(at);
        }

        /// <summary>One 2x word, the 1x lines under it and (optionally) the meter row; the panel is as wide as its widest part.</summary>
        void SetText(string word, Color32 wordColor, string lines, Color32 linesColor, bool meter)
        {
            _word.text = word ?? "";
            _word.color = wordColor;
            _lines.text = lines ?? "";
            _lines.color = linesColor;
            // Phase Q4 (A5): the reason lines follow the Reading text size (the panel grows to at most 424 px; the word is already 2x).
            float f = Game.DisplaySettings.ReadingFactor;
            _lines.Factor = f;
            int maxWidth = f > 1f ? Mathf.Min(424, Mathf.RoundToInt(MaxWidth * f)) : MaxWidth;
            var wordSize = _word.text.Length > 0 ? PixelFont.Measure(_word.text, 0, true, 2) : Vector2Int.zero;
            int linesW = _lines.text.Length > 0 ? PixelFont.Measure(_lines.text, 0, true, f).x : 0;
            int width = Mathf.Clamp(Mathf.Max(wordSize.x, Mathf.Max(linesW, meter ? MeterW + TagW * 2 + 8 : 0)) + Pad * 2, MinWidth, maxWidth);
            int inner = width - Pad * 2;
            int linesH = _lines.text.Length > 0 ? PixelFont.Measure(_lines.text, inner, true, f).y : 0;
            int y = Pad;
            if (wordSize.y > 0)
            {
                _word.rectTransform.At(Pad, y, inner, wordSize.y + 2);
                y += wordSize.y + 4;
            }
            _lines.rectTransform.At(Pad, y, inner, linesH + 2);
            y += linesH;
            _meter.gameObject.SetActive(meter);
            _youText.gameObject.SetActive(meter);
            _themText.gameObject.SetActive(meter);
            if (meter)
            {
                y += 6;
                _youText.text = T("tug.you", "YOU");
                _themText.text = T("tug.them", "017");
                float x = (width - MeterW) * 0.5f;
                _youText.rectTransform.At(x - TagW - 4, y - 2, TagW, 12);
                _themText.rectTransform.At(x + MeterW + 4, y - 2, TagW, 12);
                _meter.At(x, y, MeterW, MeterH);
                _keep.rectTransform.At(Mathf.Round(C.KeepLead * MeterW) - 1, -3, 2, MeterH + 6);
                _keepEdge.rectTransform.At(Mathf.Round(C.KeepLead * MeterW) - 2, -4, 4, MeterH + 8);
                y += MeterH;
            }
            int h = y + Pad;
            _panel.sizeDelta = new Vector2(width, h);
            Border(width, h);
        }

        void Border(float w, float h)
        {
            _border[0].rectTransform.At(0, 0, w, 1);
            _border[1].rectTransform.At(0, h - 1, w, 1);
            _border[2].rectTransform.At(0, 0, 1, h);
            _border[3].rectTransform.At(w - 1, 0, 1, h);
        }

        void SetMeter(float lead, bool latch)
        {
            float you = Mathf.Round(Mathf.Clamp01(lead) * MeterW);
            _you.rectTransform.At(0, 0, you, MeterH);
            _them.rectTransform.At(you, 0, MeterW - you, MeterH);
            // The latch: for 120 ms past the keep line the YOU fill flashes green.
            _you.color = latch ? (Color)Palette.GreenOnDark : (Color)Palette.Highlight;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Where the panel may not go: the track's bounding box (the reel) or the file (the speed model), plus a margin.</summary>
        Rect TrackBox()
        {
            var reel = C.Reel;
            Vector2 file = C.ObjectPosition;
            Rect r = new Rect(file.x - 16f, file.y - 16f, 32f, 32f);
            if (reel != null && C.IsFighting)
            {
                Vector2 o = reel.Origin.ToUnity(), axis = reel.Axis.ToUnity();
                Vector2 a = o - axis * reel.HerLine, b = o + axis * reel.Finish;
                r = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 4f, Mathf.Min(a.y, b.y) - 4f, Mathf.Max(a.x, b.x) + 4f, Mathf.Max(a.y, b.y) + 4f);
            }
            return new Rect(r.x - Margin, r.y - Margin, r.width + Margin * 2f, r.height + Margin * 2f);
        }

        int _side = -1;

        /// <summary>
        /// The panel keeps its side until something comes under it: a pointer, the arrow or the track. The first side tried is the one away
        /// from the bin; in a corner where every side covers something, her pointer, then the track, may go under it, never the player's pointer.
        /// </summary>
        void Place(Vector2 obj, bool fresh)
        {
            _anchor = obj;
            _avoid = C.IsFighting ? TrackBox() : new Rect(obj.x - 28f, obj.y - 28f, 56f, 56f);
            if (fresh) _side = -1;
            if (_side < 0 || Covers(Candidate(_side), 2))
            {
                int best = -1;
                for (int level = 2; level >= 0 && best < 0; level--)
                    foreach (int s in SideOrder())
                        if (!Covers(Candidate(s), level)) { best = s; break; }
                _side = best >= 0 ? best : _side >= 0 ? _side : SideOrder()[0];
            }
            var r = Candidate(_side);
            _panel.anchoredPosition = new Vector2(Mathf.Round(r.x), Mathf.Round(r.y));
        }

        readonly int[] _order = new int[4];

        /// <summary>Sides (0 above, 1 below, 2 left, 3 right) from the one facing away from the bin to the one facing it.</summary>
        int[] SideOrder()
        {
            Vector2 way = C.PullDirection.sqrMagnitude > 0.25f ? C.PullDirection.normalized : Vector2.down;
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++) _order[i] = i;
            System.Array.Sort(_order, (a, b) => Vector2.Dot(dirs[a], way).CompareTo(Vector2.Dot(dirs[b], way)));
            return _order;
        }

        Rect Candidate(int side)
        {
            float w = _panel.sizeDelta.x, h = _panel.sizeDelta.y;
            Rect box = _avoid;
            float x, y;
            switch (side)
            {
                case 0: x = _anchor.x - w * 0.5f; y = box.yMax; break;
                case 1: x = _anchor.x - w * 0.5f; y = box.yMin - h; break;
                case 2: x = box.xMin - w; y = _anchor.y - h * 0.5f; break;
                default: x = box.xMax; y = _anchor.y - h * 0.5f; break;
            }
            x = Mathf.Clamp(x, 2f, ScreenRig.Width - w - 2f);
            y = Mathf.Clamp(y, WindowManager.TaskbarHeight + 2f, ScreenRig.Height - h - 2f);
            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// What would be covered: the player's pointer and the arrows always; at <paramref name="level"/> 1 also her pointer, at 2 also the
        /// track (a fight) or the file (a result).
        /// </summary>
        bool Covers(Rect r, int level)
        {
            var grow = new Rect(r.x - 10f, r.y - 14f, r.width + 20f, r.height + 24f);
            if (grow.Contains(_g.Player.Position) || _big.Overlaps(grow) || _bigReady.Overlaps(grow)) return true;
            if (level >= 1 && _g.EntityAgent.Visible && grow.Contains(_g.EntityAgent.Position)) return true;
            return level >= 2 && r.Overlaps(_avoid);
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (_g == null || _panel == null) return;
            var c = C;
            Latch();
            if (c != null && c.IsFighting)
            {
                if (!_meter.gameObject.activeSelf && _shown == FightText.None) OnStarted(null);
                var state = FightState();
                if (state != _shown || (state == FightText.Pull && Surging != _shownSurge) || (state == FightText.Regrip && BlinkOn != _shownBlink)) ShowFight(state);
                bool ahead = c.PlayerKeepsOnRelease;
                if (ahead && !_wasAhead) _latchAt = Time.unscaledTime;
                _wasAhead = ahead;
                SetMeter(c.PlayerLead, Time.unscaledTime - _latchAt < LatchFlash);
                Arrows(state);
                Place(c.ObjectPosition, false);
                return;
            }
            if (_resultUntil > 0f && Time.unscaledTime < _resultUntil)
            {
                // The result follows the file: it is in the winner's hand now. The arrows and the bar stay as the fight ended.
                Place(_winner != null ? _winner.Position : _anchor, false);
                return;
            }
            _small.Hide();
            _big.Hide();
            _bigReady.Hide();
            if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
            _resultUntil = -1f;
        }

        /// <summary>
        /// GET READY: the 108 px arrow at the pointer, solid, toward the bin (the speed model: its arrow). The fight: the 72 px arrow with its
        /// travelling band, hidden in the re-grip window and after GET READY in the finale's LetGo hold.
        /// </summary>
        void Arrows(FightText state)
        {
            Vector2 at = _g.Player.Position, way = C.PullDirection;
            _small.Hide();
            if (state == FightText.Ready)
            {
                _big.Hide();
                _bigReady.Draw(at, way, 14f, 1f, false);
                return;
            }
            _bigReady.Hide();
            if (state == FightText.Regrip || Variant == TugVariant.LetGo) _big.Hide();
            else _big.Draw(at, way, 14f, 1f, true);
        }

        /// <summary>The latch's 1 px green ring around the file for 200 ms.</summary>
        void Latch()
        {
            bool on = C != null && C.IsFighting && Time.unscaledTime - _latchAt < LatchRing;
            if (!on)
            {
                if (_ring[0].enabled) foreach (var r in _ring) r.enabled = false;
                return;
            }
            Vector2 c = C.ObjectPosition;
            float x = Mathf.Round(c.x - 17f), y = Mathf.Round(c.y - 17f);
            SetRing(0, x, y, 34f, 1f);
            SetRing(1, x, y + 33f, 34f, 1f);
            SetRing(2, x, y, 1f, 34f);
            SetRing(3, x + 33f, y, 1f, 34f);
        }

        void SetRing(int i, float x, float y, float w, float h)
        {
            var rt = _ring[i].rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            _ring[i].enabled = true;
        }
    }
}
