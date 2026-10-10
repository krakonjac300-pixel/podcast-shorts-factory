using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        // Requires an isolated save, and changes the actual app and settings controls rather than a parallel mock layout.
        static IEnumerator UiReviewFixCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("uireviewfixcheck requires savedir PATH first");
            var previous = G;
            GameBootstrap.Restart(2, "work");
            yield return RegressionWaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "UI review regression shift");
            var g = G;
            var originalSize = DisplaySettings.Size;
            var originalClick = AccessSettings.ClickSpeed;
            try
            {
                DisplaySettings.SetSize(ReadingSize.Normal);
                var mail = g.Apps.Launch(AppIds.Mail, g.Player) as MailApp;
                var orders = g.Apps.Launch(AppIds.WorkOrders, g.Player) as WorkOrdersApp;
                var staff = g.Apps.Launch(AppIds.Staff, g.Player) as StaffApp;
                var queue = g.Apps.Launch(AppIds.WorkQueue, g.Player) as WorkQueueApp;
                Canvas.ForceUpdateCanvases();
                yield return null;
                var inbox = ReadabilityField<ListView>(mail, "_list");
                var orderList = ReadabilityField<ListView>(orders, "_list");
                ExperienceAssert(inbox.Rows.Count > 0 && orderList.Rows.Count > 0, "UI regression has actual messages and orders");
                inbox.Select(0, g.Player);
                object selectedMail = inbox.Selected?.Tag;
                float oldOrderWidth = orders.Window.Size.x;
                float oldQueueWidth = queue.Window.Size.x;
                DisplaySettings.SetSize(ReadingSize.Large);
                yield return null;
                yield return null;
                mail.Tick(0.016f);
                orders.Tick(0.016f);
                staff.Tick(0.016f);
                queue.Tick(0.016f);
                Canvas.ForceUpdateCanvases();
                yield return null;
                ExperienceAssert(inbox.Rows[0].Columns[0].Factor == 2f && orderList.Rows[0].Columns[0].Factor == 2f,
                    "existing Inbox and order rows use Large text");
                ExperienceAssert(inbox.RowHeight >= PixelFont.GlyphHeight * 2 + 4,
                    "Large text also enlarges the list row hit area");
                ExperienceAssert(Equals(selectedMail, inbox.Selected?.Tag), "list reflow preserves the selected message");
                ExperienceAssert(orders.Window.Size.x > oldOrderWidth && queue.Window.Size.x > oldQueueWidth,
                    "changing text size widens existing work windows without rebuilding the shift");
                var approve = ReadabilityField<UiButton>(orders, "_approve");
                var reject = ReadabilityField<UiButton>(orders, "_reject");
                ExperienceAssert(approve.Label.Factor == 2f && reject.Label.Factor == 2f,
                    "Approve and Reject use Large text in their enlarged actions");
                ExperienceAssert(!approve.Hit.WorldRect.Overlaps(reject.Hit.WorldRect), "order actions remain separate hit targets");
                var header = ReadabilityField<PixelText>(mail, "_header");
                var body = ReadabilityField<PixelText>(mail, "_body");
                ExperienceAssert(header.Factor <= 1.5f && body.Factor == 2f,
                    "compact Mail headers leave space for the Large message body");
                var taskScroll = ReadabilityField<ScrollArea>(queue, "_taskScroll");
                ExperienceAssert(taskScroll != null && taskScroll.ContentHeight >= 0f,
                    "the task list remains scrollable when Large rows need more space");
                var taskHeader = ReadabilityField<PixelText>(queue, "_header");
                ExperienceAssert(taskHeader.Factor == 2f, "Work Queue scan text follows the chosen reading size");
                var popup = PopupMenu.Show(g.Layers.Popups, new Vector2(100f, 450f),
                    new[] { MenuItem.Of("Archive remaining batch", a => { }) });
                Canvas.ForceUpdateCanvases();
                var popupText = popup.GetComponentInChildren<PixelText>();
                ExperienceAssert(popupText.Factor == 2f && PixelFont.MeasureLine(popupText.text, popupText.Bold, 2f) <= popupText.rectTransform.rect.width,
                    "Large context-menu actions have enough width for their labels");
                popup.Close();
                bool hadLogoff = g.Flags.Has(Core.Story.Flags.LogoffItem);
                g.Flags.Set(Core.Story.Flags.LogoffItem);
                g.Taskbar.StartMenu.Open(g.Player);
                var nexus = ReadabilityField<PopupMenu>(g.Taskbar.StartMenu, "_menu");
                Canvas.ForceUpdateCanvases();
                var nexusBounds = nexus.Rect.WorldRect();
                bool logoffVisible = false;
                foreach (var item in nexus.GetComponentsInChildren<Interactable>())
                {
                    if (!item.elementId.StartsWith("start:", StringComparison.Ordinal)) continue;
                    var bounds = item.WorldRect;
                    ExperienceAssert(bounds.xMin >= nexusBounds.xMin && bounds.xMax <= nexusBounds.xMax
                        && bounds.yMin >= nexusBounds.yMin && bounds.yMax <= nexusBounds.yMax,
                        "Large Nexus menu contains action " + item.elementId);
                    if (item.elementId == "start:logoff") logoffVisible = true;
                }
                ExperienceAssert(logoffVisible && nexusBounds.yMax <= ScreenRig.Height && nexusBounds.yMin >= OS.WindowManager.TaskbarHeight,
                    "Large Nexus menu includes Log Off and fits above the taskbar");
                g.Taskbar.StartMenu.Close();
                if (!hadLogoff) g.Flags.Clear(Core.Story.Flags.LogoffItem);
                staff.ShowById(ContentIds.Employee000, g.Player);
                staff.Window.SetSize(new Vector2(ScreenRig.Width / 2f, ScreenRig.Height - OS.WindowManager.TaskbarHeight));
                orders.Window.SetSize(new Vector2(ScreenRig.Width / 2f, ScreenRig.Height - OS.WindowManager.TaskbarHeight));
                Canvas.ForceUpdateCanvases();
                staff.Tick(0.016f);
                orders.Tick(0.016f);
                yield return null;
                var staffStatus = ReadabilityField<PixelText>(staff, "_status");
                var notes = ReadabilityField<ReadingPane>(staff, "_notes");
                ExperienceAssert(staffStatus.rectTransform.WorldRect().height > 0f && notes.Scroll.ViewportHeight >= 40f,
                    "Large personnel keeps status and scrollable notes available in a half-width window");
                var fields = ReadabilityField<ReadingPane>(staff, "_fieldsPane");
                ExperienceAssert(fields.Scroll.ViewportHeight > 0f, "narrow personnel fields remain scrollable");
                var owner = ReadabilityField<UiButton>(orders, "_owner");
                ExperienceAssert(!owner.Hit.WorldRect.Overlaps(approve.Hit.WorldRect) && !owner.Hit.WorldRect.Overlaps(reject.Hit.WorldRect),
                    "narrow work windows stack owner and decision actions without overlap");

                PauseMenu.Current.OpenMenu();
                yield return null;
                var panel = ReadabilityField<RectTransform>(PauseMenu.Current, "_panel");
                var settingsButton = panel.GetComponentsInChildren<UiButton>();
                bool motorHelpVisible = false;
                foreach (var text in panel.GetComponentsInChildren<PixelText>())
                    if (text.text.Contains("drag for 0.5 sec") && text.text.Contains("click to drop")) motorHelpVisible = true;
                ExperienceAssert(motorHelpVisible, "settings explain Hold and Click lock beside their controls");
                foreach (var button in settingsButton)
                    if (button.Label != null && button.Hit.elementId != "pause:voldown" && button.Hit.elementId != "pause:volup")
                        ExperienceAssert(button.Label.Factor == 2f, "Large settings label: " + button.Hit.elementId);
                ExperienceAssert(panel.Find("Pause Box").GetComponent<RectTransform>().rect.height <= ScreenRig.Height,
                    "Large settings including motor instructions fit on the desktop");
                foreach (var button in settingsButton)
                    if (button.Hit.elementId == "pause:resume") { button.Press(g.Player); break; }
                AccessSettings.SetClickSpeed(ClickSpeed.Single);
                ExperienceAssert(ClickRules.Instructions("Double-click Workstation. A slower double-click is optional.")
                    == "Click Workstation. A slower double-click is optional.",
                    "Single click instructions change the open gesture and preserve the option's name");
                ExperienceAssert(ClickRules.Instructions("OPEN: Double-click an icon or file to open it.")
                    == "OPEN: Click an icon or file to open it.", "manual section headings keep the chosen open gesture");
                ExperienceAssert(ClickRules.Instructions("CAMERAS: double-click Camera Viewer to open it again.")
                    == "CAMERAS: click Camera Viewer to open it again.", "lowercase camera instructions keep the chosen open gesture");
                ExperienceAssert(ClickRules.Instructions("then double-click b7_door_log.txt.") == "then click b7_door_log.txt.",
                    "named file opening instructions use the enabled gesture");
                ExperienceAssert(ClickRules.Instructions("then press A twice on b7_door_log.txt.") == "then press A once on b7_door_log.txt.",
                    "mid-sentence gamepad instructions use the enabled gesture");
                var help = g.Apps.Launch(AppIds.Help, g.Player) as HelpApp;
                ReadabilityField<UiButton>(help, "_mode").Press(g.Player);
                var helpText = ReadabilityField<PixelText>(help, "_text");
                ExperienceAssert(helpText.text.Contains("OPEN: Click an icon") && !helpText.text.Contains("Double-click Workstation"),
                    "the live manual uses the enabled Single click gesture");
                Say("PASS UI review regression checks complete");
            }
            finally
            {
                if (PauseMenu.IsPaused)
                {
                    var panel = ReadabilityField<RectTransform>(PauseMenu.Current, "_panel");
                    foreach (var button in panel.GetComponentsInChildren<UiButton>())
                        if (button.Hit.elementId == "pause:resume") { button.Press(g.Player); break; }
                }
                AccessSettings.SetClickSpeed(originalClick);
                DisplaySettings.SetSize(originalSize);
                GameBootstrap.ToTitle();
            }
        }
    }
}
