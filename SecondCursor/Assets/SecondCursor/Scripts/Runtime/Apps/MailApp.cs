using SecondCursor.Core.Content;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Company mail client: message list on top (unread in bold), reading pane below. Phase H: a taller reading pane, a
    /// "More below" marker while the message goes on under the fold, and it opens clear of the notices' column.
    /// </summary>
    public sealed class MailApp : App
    {
        ListView _list;
        ScrollArea _reader;
        PixelText _header;
        PixelText _body;
        PixelText _status;
        RectTransform _more;
        int _revision = -1;
        string _showing;

        public override string AppId => AppIds.Mail;
        protected override bool AvoidsNotices => true;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            var win = CreateWindow(G.Content.Text("app.mail"), "icon_mail", 150, 16, 560, 460, WindowFlags.Standard, zoomFrom);
            var client = win.Client;

            _list = new ListView(client, "Inbox", new[] { 150, 250, 130 }, new[] { "From", "Subject", "Received" }, true);
            _list.Root.TopStrip(2, 118, 2, 2);
            _list.RowSelected += (row, a) => Show((string)row.Tag, a);

            var readerFrame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Reader");
            readerFrame.rectTransform.Stretch(2, 124, 2, 20);
            _reader = ScrollArea.Create(readerFrame.rectTransform, "Reader Scroll");
            ((RectTransform)_reader.transform).Stretch(2, 2, 2, 2);
            _header = UIBuilder.Text(_reader.Content, "", Palette.Text);
            _header.Wrap = true;
            _body = UIBuilder.Text(_reader.Content, "", Palette.Text);
            _body.Wrap = true;

            // The message goes on below the fold: say so at the bottom of the pane until it is scrolled to the end.
            _more = UIBuilder.Rect("More Below", readerFrame.rectTransform).BottomRight(20, 3, 96, 14);
            var moreFace = _more.gameObject.AddComponent<BevelGraphic>();
            moreFace.Style = BevelStyle.Window;
            moreFace.Fill = Palette.Tooltip;
            moreFace.raycastTarget = false;
            var arrow = UIBuilder.Icon(_more, "glyph_arrow_down", 1);
            arrow.rectTransform.anchoredPosition = new Vector2(4f, -3f);
            var moreText = UIBuilder.Text(_more, G.Content.Text("mail.more", "More below"), Palette.Text, true);
            moreText.rectTransform.Stretch(16, 1, 2, 1);
            moreText.VAlign = TextVAlign.Middle;
            _more.gameObject.SetActive(false);

            var status = UIBuilder.Bevel(client, BevelStyle.StatusField, "Status");
            status.rectTransform.BottomStrip(0, 18, 2, 2);
            _status = UIBuilder.Text(status.rectTransform, "", Palette.Text);
            _status.rectTransform.Stretch(4, 0, 4, 0);
            _status.VAlign = TextVAlign.Middle;

            Refresh();
            // Open straight onto the newest unread message.
            string newest = null;
            foreach (var id in G.Mail.Inbox) if (!G.Mail.IsRead(id)) newest = id;
            if (newest != null) _list.SelectWhere(r => (string)r.Tag == newest, by);
        }

        void Refresh()
        {
            _revision = G.Mail.Revision;
            string selected = _list.Selected?.Tag as string;
            _list.Clear();
            var inbox = G.Mail.Inbox;
            for (int i = inbox.Count - 1; i >= 0; i--)
            {
                var mail = G.Content.Email(inbox[i]);
                if (mail == null) continue;
                bool unread = !G.Mail.IsRead(mail.id);
                string from = string.IsNullOrEmpty(mail.from) ? "(no sender)" : ShortFrom(mail.from);
                var row = _list.AddRow(unread ? "icon_mail_unread" : "icon_mail", mail.id, "mail:" + mail.id,
                    from, string.IsNullOrEmpty(mail.subject) ? "(no subject)" : mail.subject, mail.date);
                _list.SetBold(row, unread);
            }
            if (selected != null) _list.SelectWhere(r => (string)r.Tag == selected, null);
            _status.text = inbox.Count + " message(s), " + G.Mail.UnreadCount + " unread";
        }

        static string ShortFrom(string from)
        {
            int lt = from.IndexOf('<');
            return lt > 0 ? from.Substring(0, lt).Trim() : from;
        }

        void Show(string id, CursorAgent by)
        {
            var mail = G.Content.Email(id);
            if (mail == null) return;
            bool changed = _showing != id;
            _showing = id;
            _header.text = "From:    " + (string.IsNullOrEmpty(mail.from) ? "" : mail.from) + "\nTo:      " + mail.to +
                           "\nSubject: " + mail.subject + "\nDate:    " + mail.date;
            _body.text = mail.body;
            Layout();
            if (changed) _reader.ScrollTo(0);
            G.Mail.MarkRead(id, by);
        }

        /// <summary>Header and body sized for the reading pane at the Reading text scale.</summary>
        void Layout()
        {
            int scale = Game.DisplaySettings.ReadingScale;
            _header.Scale = scale;
            _body.Scale = scale;
            Canvas.ForceUpdateCanvases();
            int width = Mathf.Max(100, Mathf.FloorToInt(_reader.Viewport.rect.width) - 8);
            var hs = PixelFont.Measure(_header.text, width, false, scale);
            var bs = PixelFont.Measure(_body.text, width, false, scale);
            _header.rectTransform.At(4, 4, width, hs.y + 2);
            _body.rectTransform.At(4, 4 + hs.y + 14, width, bs.y + 4);
            _reader.ContentHeight = hs.y + bs.y + 30;
        }

        /// <summary>Selects and shows one message (the Work Queue's new-mail line opens it this way).</summary>
        public void ShowMail(string id, CursorAgent by)
        {
            if (string.IsNullOrEmpty(id) || !G.Mail.Has(id)) return;
            if (_revision != G.Mail.Revision) Refresh();
            _list.SelectWhere(r => (string)r.Tag == id, by);
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Mail.Revision) Refresh();
            if (_showing != null && _body.Scale != Game.DisplaySettings.ReadingScale) Layout();
            bool more = _showing != null && _reader.MaxOffset > 2f && _reader.Offset < _reader.MaxOffset - 2f;
            if (_more != null && _more.gameObject.activeSelf != more) _more.gameObject.SetActive(more);
        }
    }
}
