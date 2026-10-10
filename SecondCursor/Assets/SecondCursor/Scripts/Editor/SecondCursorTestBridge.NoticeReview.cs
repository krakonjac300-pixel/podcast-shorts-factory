using System;
using System.Collections;
using System.Reflection;
using SecondCursor.Core.Game;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        // Uses a disposable notification layer, so the real shift's notices and choices are untouched.
        static void NoticeReviewCheck()
        {
            if (G == null || G.Notifications == null) throw new InvalidOperationException("Play a shift before noticereview.");
            var layer = new GameObject("Notice Review Test", typeof(RectTransform));
            layer.transform.SetParent(G.Notifications.transform.parent, false);
            try
            {
                var notices = Notifications.Create((RectTransform)layer.transform);
                var order = new Notifications.Toast[3];
                for (int i = 0; i < order.Length; i++)
                {
                    NoticeReviewRelease(notices);
                    order[i] = notices.Show("Order " + i, "A choice that must remain available.", "icon_workorders", null, null, true);
                }
                NoticeReviewAssert(NoticeReviewCount(notices, true) == 3, "Three existing order notices fill the visible slots.");

                var current = notices.Show("Camera Viewer", "Security opened CAM 03.", "icon_camera", null, null, false, null, urgent: true);
                NoticeReviewAssert(NoticeReviewShown(current), "A new urgent camera warning is visible immediately behind a full queue.");
                for (int i = 0; i < order.Length; i++)
                    NoticeReviewAssert(!NoticeReviewFlag(order[i], "Dismissed"), "Order " + i + " is deferred without dismissal.");
                NoticeReviewAssert(notices.History.Count == 4, "Preemption preserves the recent notice history.");

                for (int i = 0; i < 12; i++)
                    current = notices.Show("Camera Viewer", "Security reopened CAM 03, event " + i + ".", "icon_camera", null, null, false, null, urgent: true);
                NoticeReviewAssert(NoticeReviewCount(notices, false) == 4, "Twelve camera changes leave one live camera status and three orders.");
                NoticeReviewAssert(notices.History.Count == 16, "Camera changes are coalesced on screen, not erased from history.");
                notices.DismissChannel(NoticeRules.CameraChannel);
                NoticeReviewLayout(notices);
                NoticeReviewAssert(NoticeReviewCount(notices, false) == 3, "Ending rounds removes live and queued camera status only.");
                NoticeReviewAssert(notices.History.Count == 16, "Ending rounds retains the recent camera history.");

                bool stillRelevant = true;
                current = notices.Show("Camera Viewer", "Custodial is on this camera.", "icon_camera", null, null, false, () => stillRelevant, urgent: true);
                stillRelevant = false;
                NoticeReviewLayout(notices);
                NoticeReviewAssert(NoticeReviewFlag(current, "Dismissed"), "A camera change invalidates an obsolete warning.");

                notices.HeldByStory = true;
                current = notices.Show("SESSION 017", "Close CAM 03.", "icon_warning", null, null, true, null,
                    NoticeKind.Entity, "camera.capture", urgent: true);
                NoticeReviewAssert(!NoticeReviewShown(current), "Urgency respects a protected story view.");
                notices.HeldByStory = false;
                NoticeReviewRelease(notices);
                NoticeReviewLayout(notices);
                NoticeReviewAssert(NoticeReviewShown(current), "The survival warning appears as soon as the protected view ends.");
                NoticeReviewAssert(NoticeRules.Channel("icon_workorders", null) == null, "Independent work orders are never coalesced.");
                Say("Notice review passed: immediate warnings, preserved choices, camera coalescing, history, state expiry, and story hold.");
            }
            finally
            {
                layer.SetActive(false);
                UnityEngine.Object.Destroy(layer);
            }
        }

        const BindingFlags NoticeReviewFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        static bool NoticeReviewFlag(Notifications.Toast toast, string name) =>
            (bool)typeof(Notifications.Toast).GetField(name, NoticeReviewFlags).GetValue(toast);

        static bool NoticeReviewShown(Notifications.Toast toast) =>
            NoticeReviewFlag(toast, "Shown") && !NoticeReviewFlag(toast, "Dismissed");

        static int NoticeReviewCount(Notifications notices, bool shownOnly)
        {
            int count = 0;
            foreach (Notifications.Toast toast in (IList)typeof(Notifications).GetField("_toasts", NoticeReviewFlags).GetValue(notices))
                if (!NoticeReviewFlag(toast, "Dismissed") && (!shownOnly || NoticeReviewShown(toast))) count++;
            return count;
        }

        static void NoticeReviewRelease(Notifications notices)
        {
            typeof(Notifications).GetField("_nextShowAt", NoticeReviewFlags).SetValue(notices, -100f);
            typeof(Notifications).GetField("_nextRelease", NoticeReviewFlags).SetValue(notices, -100f);
        }

        static void NoticeReviewLayout(Notifications notices) =>
            typeof(Notifications).GetMethod("Layout", NoticeReviewFlags).Invoke(notices, new object[] { 0f });

        static void NoticeReviewAssert(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Notice review failed: " + message);
            Say("PASS: " + message);
        }
    }
}
