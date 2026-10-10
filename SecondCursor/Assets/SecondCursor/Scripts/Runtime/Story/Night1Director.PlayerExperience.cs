using System.Collections;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.OS;

namespace SecondCursor.Story
{
    public sealed partial class Night1Director
    {
        const string CameraDeclined = "m.n1.camera_declined";

        IEnumerator LedgerAcknowledgement()
        {
            const string shown = "n1.ledger_acknowledged";
            if (_g.Flags.Has(shown)) yield break;
            yield return Wait(3f);
            _g.Flags.Set(shown);
            _g.Notifications.Show("Session 017", "That isn't when I left.", "icon_info",
                a => (_g.Apps.Launch(AppIds.Mail, a) as Apps.MailApp)?.ShowMail("mail_ledger_receipt", a),
                "ui_select", false, null, NoticeKind.Entity);
            SaveCurrentProgress();
        }

        void DeliverFirstShiftMail(string id, bool notify = true)
        {
            var g = _g;
            var mail = g.Content.Email(id);
            if (mail == null) return;
            string stamp = "n1.mail_minute." + id;
            int stored = g.Flags.Get(stamp);
            if (stored == 0)
            {
                stored = g.Clock.TotalMinutes + 1;
                g.Flags.SetCounter(stamp, stored);
            }
            mail.date = "Wed 11/18/98 " + GameClock.Format12(stored - 1);
            g.Mail.Deliver(id, notify);
        }

        IEnumerator QuickStartWithComfort()
        {
            var c = _g.Content;
            while (true)
            {
                string easier = c.Text("quickstart.comfort");
                string options = c.Text("title.settings");
                var quick = Dialogs.Message(_g, c.Text("quickstart.title"), c.Text("quickstart.body"),
                    "icon_info", new[] { "Begin", easier, options }, null);
                yield return ClockStillWhile(quick, 120f);
                if (quick.Result == easier)
                {
                    if (DisplaySettings.Size == ReadingSize.Normal) DisplaySettings.SetSize(ReadingSize.Medium);
                    _g.Fx.Crt = CrtLevel.Low;
                    SaveSystem.SaveSettings(_g);
                }
                else if (quick.Result == options)
                {
                    PauseMenu.Current?.OpenSettings();
                    while (PauseMenu.IsPaused) yield return null;
                }
                else yield break;
            }
        }

        // Rebuilding a checkpoint restores the actual decision, including a mistake, without counting it twice.
        void RestoreFirstShiftOrder(string id)
        {
            var g = _g;
            string decision = NightSetup.RestoreOrder(g, 1, id);
            if (decision != "approve" && decision != "reject") return;
            DeliverFirstShiftMail("mail_" + id + "_" + decision, false);
        }

        IEnumerator CameraConsent()
        {
            var c = _g.Content;
            string open = c.Text("camera.consent.open");
            var choice = Dialogs.Message(_g, c.Text("camera.consent.title"), c.Text("camera.consent.body"),
                "icon_camera", new[] { open, c.Text("camera.consent.closed") }, null, 1);
            // A refusal has no countdown. Closing this prompt also leaves the feed closed.
            bool frozen = _g.Clock.Frozen;
            _g.Clock.Frozen = true;
            try { while (choice.IsOpen) yield return null; }
            finally { _g.Clock.Frozen = frozen; }
            if (choice.Result != open || choice.AnsweredBy == null || !choice.AnsweredBy.IsPlayer)
            {
                _g.Flags.Set(CameraDeclined);
                _g.Apps.FindById(AppIds.Camera)?.Window.Close(null, true);
            }
        }
    }
}
