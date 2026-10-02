using System;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase R (sixth blind playtest: "the loss is told in a toast that fades; I never knew who was playing or who won"): a persistent strip at the
    /// top of the screen while a contest runs ("TUG-OF-WAR: [you] YOU VS [017] SESSION 017", "RACE TO THE BUTTON: ..."), and when the contest ends
    /// a full-width WON or LOST card with one sentence about what happened and what it means, about 2.5 s, with a short sting from the existing
    /// sounds. It belongs to the tug HUD (<see cref="TugHud.Banner"/>) and describes contests; nothing here changes one. The strip checks its own
    /// contest is still running every frame, so a dialog closed by a story reset never leaves it up.
    /// </summary>
    public sealed class ContestBanner : MonoBehaviour
    {
        public const int StripHeight = 15, CardGap = 4, CardMinHeight = 44;
        const int Pad = 6, ItemGap = 5, IconWidth = 6, IconHeight = 8, CardInner = 880;
        const float FadeOut = 0.3f;
        static readonly Color32 StripFill = new Color32(0x0B, 0x0E, 0x0D, 0xF0);
        static readonly Color32 WonFill = new Color32(0x16, 0x3A, 0x20, 0xF4), WonEdge = new Color32(0x4F, 0xB0, 0x5E, 0xFF);
        static readonly Color32 LostFill = new Color32(0x44, 0x14, 0x10, 0xF4), LostEdge = new Color32(0xD0, 0x4A, 0x3A, 0xFF);

        GameServices _g;
        RectTransform _strip, _card;
        PixelText _lead, _you, _vs, _rival, _word, _line;
        Image _youIcon, _rivalIcon, _cardFill, _cardTop, _cardBottom;
        CanvasGroup _cardGroup;
        Func<bool> _alive;
        float _cardUntil = -1f;
        int _cardHeight;

        /// <summary>The strip is up.</summary>
        public bool StripShown => _strip != null && _strip.gameObject.activeSelf;
        /// <summary>The card is up.</summary>
        public bool CardShown => _card != null && _card.gameObject.activeSelf;

        /// <summary>Pixels from the top of the screen that the strip and the card cover now (the tug panel stays under them).</summary>
        public int Occupied => (StripShown ? StripHeight : 0) + (CardShown ? CardGap + _cardHeight : 0);

        public static ContestBanner Create(GameServices g)
        {
            var root = UIBuilder.Rect("Contest Banner", g.Layers.Effects);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.pivot = Vector2.zero;
            var b = root.gameObject.AddComponent<ContestBanner>();
            b._g = g;
            b.Build(root);
            return b;
        }

        void Build(RectTransform root)
        {
            _strip = UIBuilder.Rect("Strip", root);
            _strip.TopStrip(0, StripHeight);
            var fill = UIBuilder.Solid(_strip, StripFill, "Fill");
            fill.rectTransform.Stretch();
            var edge = UIBuilder.Solid(_strip, Palette.EntityOutline, "Edge");
            edge.rectTransform.BottomStrip(0, 1);
            _lead = UIBuilder.Text(_strip, "", Palette.EntityText, true, "Lead");
            _you = UIBuilder.Text(_strip, "", Palette.Highlight, true, "You");
            _vs = UIBuilder.Text(_strip, "", Palette.TextDisabled, true, "Vs");
            _rival = UIBuilder.Text(_strip, "", Palette.EntityOutline, true, "Rival");
            _youIcon = ActorSprites.TinyIcon(_strip, NoticeKind.Plain);
            _rivalIcon = ActorSprites.TinyIcon(_strip, NoticeKind.Entity);
            _strip.gameObject.SetActive(false);

            _card = UIBuilder.Rect("Card", root);
            _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
            _cardGroup.blocksRaycasts = false;
            _cardGroup.interactable = false;
            _cardFill = UIBuilder.Solid(_card, WonFill, "Fill");
            _cardFill.rectTransform.Stretch();
            _cardTop = UIBuilder.Solid(_card, WonEdge, "Top");
            _cardTop.rectTransform.TopStrip(0, 2);
            _cardBottom = UIBuilder.Solid(_card, WonEdge, "Bottom");
            _cardBottom.rectTransform.BottomStrip(0, 2);
            _word = UIBuilder.Text(_card, "", Palette.BiosBright, true, "Word");
            _word.Scale = 2;
            _word.Align = TextAlign.Center;
            _line = UIBuilder.Text(_card, "", Palette.EntityText, true, "Sentence");
            _line.Align = TextAlign.Center;
            _line.Wrap = true;
            _card.gameObject.SetActive(false);
        }

        string T(string key, string fallback) => _g.Content != null ? _g.Content.Text(key, fallback) : fallback;

        // ------------------------------------------------------------------ the strip

        /// <summary>
        /// The strip says "<paramref name="leadKey"/> YOU VS (rival's name)" with both pointers in their colours, until <paramref name="alive"/>
        /// returns false (the contest is over) or <see cref="EndStrip"/> is called. A rival with no name in the content (a Night 2 and 3 string
        /// on a night without it) shows no strip.
        /// </summary>
        public void BeginStrip(string leadKey, NoticeKind rival, Func<bool> alive)
        {
            string name = T(rival == NoticeKind.Gary ? "pointer.tag.gary" : "pointer.tag.entity", rival == NoticeKind.Gary ? "" : "SESSION 017");
            string lead = T(leadKey, "");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(lead)) return;
            _alive = alive;
            _lead.text = lead;
            _you.text = T("contest.you", "YOU");
            _vs.text = T("contest.vs", "VS");
            _rival.text = name;
            _rivalIcon.sprite = ActorSprites.For(ActorSprites.Tiny, rival);
            _rival.color = rival == NoticeKind.Gary ? Palette.GaryOutline : Palette.EntityOutline;
            LayOutStrip();
            _strip.gameObject.SetActive(true);
            // A new contest takes the screen: the last card goes.
            HideCard();
            GameLog.Info(LogChannel.Entity, "Contest strip: " + lead + " " + _you.text + " " + _vs.text + " " + name);
        }

        public void EndStrip()
        {
            _alive = null;
            if (_strip != null && _strip.gameObject.activeSelf) _strip.gameObject.SetActive(false);
        }

        void LayOutStrip()
        {
            int wLead = PixelFont.MeasureLine(_lead.text, true), wYou = PixelFont.MeasureLine(_you.text, true);
            int wVs = PixelFont.MeasureLine(_vs.text, true), wRival = PixelFont.MeasureLine(_rival.text, true);
            int total = wLead + ItemGap + IconWidth + 3 + wYou + ItemGap + wVs + ItemGap + IconWidth + 3 + wRival;
            int x = (ScreenRig.Width - total) / 2, ty = 3, iy = 3;
            Put(_lead, ref x, wLead, ty);
            Icon(_youIcon, ref x, iy);
            Put(_you, ref x, wYou, ty);
            Put(_vs, ref x, wVs, ty);
            Icon(_rivalIcon, ref x, iy);
            Put(_rival, ref x, wRival, ty, false);
        }

        static void Put(PixelText t, ref int x, int width, int y, bool gap = true)
        {
            t.rectTransform.At(x, y, width + 2, 10);
            x += width + (gap ? ItemGap : 0);
        }

        static void Icon(Image icon, ref int x, int y)
        {
            icon.rectTransform.anchoredPosition = new Vector2(x, -y);
            x += IconWidth + 3;
        }

        // ------------------------------------------------------------------ the card

        /// <summary>The full-width card: YOU WON or YOU LOST, one sentence under it, and a short sting.</summary>
        public void ShowCard(bool won, string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return;
            float f = DisplaySettings.ReadingFactor;
            _line.Factor = f;
            _line.text = sentence;
            _word.text = T(won ? "contest.card.won" : "contest.card.lost", won ? "YOU WON" : "YOU LOST");
            _word.color = won ? Palette.GreenOnDark : new Color32(0xFF, 0x8A, 0x78, 0xFF);
            _cardFill.color = won ? WonFill : LostFill;
            _cardTop.color = _cardBottom.color = won ? WonEdge : LostEdge;
            int lineH = PixelFont.Measure(sentence, CardInner, true, f).y;
            _cardHeight = Mathf.Max(CardMinHeight, 8 + 16 + 5 + lineH + 8);
            int top = StripShown ? StripHeight + CardGap : CardGap;
            _card.TopStrip(top, _cardHeight);
            _word.rectTransform.At(0, 8, ScreenRig.Width, 16);
            _line.rectTransform.At((ScreenRig.Width - CardInner) / 2, 8 + 16 + 5, CardInner, lineH + 2);
            _cardGroup.alpha = 1f;
            _card.gameObject.SetActive(true);
            _cardUntil = Time.unscaledTime + ContestCopy.CardSeconds(sentence.Length);
            // The sting reuses the OS's own sounds: the task-done chime for a win, the error tone for a loss.
            _g.Audio?.Play(won ? "notify_task" : "sys_error", 0.8f);
            GameLog.Info(LogChannel.Entity, "Contest card: " + (won ? "WON" : "LOST") + ": " + sentence);
        }

        public void HideCard()
        {
            _cardUntil = -1f;
            if (_card != null && _card.gameObject.activeSelf) _card.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_alive != null && _strip.gameObject.activeSelf && !_alive()) EndStrip();
            if (!_card.gameObject.activeSelf) return;
            float left = _cardUntil - Time.unscaledTime;
            if (left <= 0f) HideCard();
            else _cardGroup.alpha = Mathf.Clamp01(left / FadeOut);
        }
    }
}
