using System;
using System.Collections;
using System.Reflection;
using SecondCursor.Apps;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        static T ReadabilityField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        // Tests real app controls against a disposable world, without running a timed story sequence.
        static IEnumerator ReadabilityCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("readabilitycheck requires savedir PATH first");
            var previous = G;
            GameBootstrap.Restart(2, "work");
            yield return WaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "readability test shift");
            var g = G;
            var originalReading = DisplaySettings.Size;
            try
            {
                DisplaySettings.SetSize(ReadingSize.Medium);
                var document = g.Apps.OpenFile("manifest_44", g.Player) as NotepadApp;
                ExperienceAssert(document != null, "manifest opens as a document");
                Canvas.ForceUpdateCanvases();
                document.Tick(0.016f);
                yield return null;
                document.Tick(0.016f);
                var scroll = ReadabilityField<ScrollArea>(document, "_scroll");
                ExperienceAssert(scroll.MaxOffset > 0f, "manifest test exercises overflowing readable text");
                ExperienceAssert(Mathf.Abs(scroll.Offset) < 0.1f, "Read easier opens the manifest at the beginning after layout");
                scroll.ScrollTo(scroll.MaxOffset / 2f);
                float readingPosition = scroll.Offset;
                var reopened = g.Apps.OpenFile("manifest_44", g.Player);
                ExperienceAssert(ReferenceEquals(document, reopened), "reopening text evidence reuses its existing window");
                ExperienceAssert(Mathf.Abs(scroll.Offset - readingPosition) < 0.1f, "reopening text evidence preserves reading position");
                DisplaySettings.SetSize(ReadingSize.Large);
                document.Tick(0.016f);
                ExperienceAssert(Mathf.Abs(scroll.Offset - readingPosition) < 0.1f, "changing document reading size does not jump to the end");

                var conversation = g.Apps.Launch(AppIds.Notepad, g.Player) as NotepadApp;
                conversation.ConversationMode = true;
                conversation.SetText(new string('\n', 70) + "latest conversation line");
                Canvas.ForceUpdateCanvases();
                conversation.Tick(0.016f);
                var conversationScroll = ReadabilityField<ScrollArea>(conversation, "_scroll");
                ExperienceAssert(conversationScroll.MaxOffset > 0f && Mathf.Abs(conversationScroll.Offset - conversationScroll.MaxOffset) < 0.1f,
                    "conversation layout still follows its newest line");
                conversation.Append("\nnew incoming line");
                conversation.Tick(0.016f);
                ExperienceAssert(Mathf.Abs(conversationScroll.Offset - conversationScroll.MaxOffset) < 0.1f,
                    "incoming conversation text remains visible");

                var data = g.Apps.OpenFile(ContentIds.FileCacheN2, g.Player) as DataViewerApp;
                ExperienceAssert(data != null && data.FileId == ContentIds.FileCacheN2, "temporary evidence opens in Data Viewer");
                var toggle = ReadabilityField<UiButton>(data, "_toggle");
                ExperienceAssert(toggle != null && toggle.Enabled && toggle.Label.text == "Text view", "ordinary temporary evidence offers a readable text button");
                toggle.Hit.SimulateClick(g.Player);
                var recovered = ReadabilityField<PixelText>(data, "_recovered");
                var textScroll = ReadabilityField<ScrollArea>(data, "_textScroll");
                ExperienceAssert(textScroll.gameObject.activeSelf && recovered.text.Contains("NEXUS TEMPORARY FILE"),
                    "text button displays the temporary file's actual evidence");
                ExperienceAssert(recovered.Factor == DisplaySettings.ReadingFactor && textScroll.Offset == 0f,
                    "temporary text respects reading size and starts at the beginning");
                ExperienceAssert(ReferenceEquals(data, g.Apps.OpenFile(ContentIds.FileCacheN2, g.Player)) && textScroll.gameObject.activeSelf,
                    "reopening data evidence reuses its window and keeps the selected text view");
                textScroll.ScrollTo(textScroll.MaxOffset / 2f);
                float evidencePosition = textScroll.Offset;
                g.Files.SetContent(ContentIds.FileCacheN2, g.Files.GetFile(ContentIds.FileCacheN2).Content + "\nUPDATED EVIDENCE");
                ExperienceAssert(ReferenceEquals(data, g.Apps.OpenFile(ContentIds.FileCacheN2, g.Player)), "changed data evidence still reuses its window");
                ExperienceAssert(recovered.text.Contains("UPDATED EVIDENCE") && Mathf.Abs(textScroll.Offset - evidencePosition) < 0.1f,
                    "reopened data updates readable evidence without resetting the reading position");
                toggle.Hit.SimulateClick(g.Player);
                var dump = ReadabilityField<PixelText>(data, "_dump");
                ExperienceAssert(dump.text.Contains("UPDATED") || dump.text.Contains("EVIDENCE"), "reopened data refreshes its hex and ASCII view too");

                g.Files.SetHidden(ContentIds.File214, false);
                g.Tasks.Activate(ContentIds.TaskE2Hide214);
                var files = g.Apps.OpenFolder(g.Files.FolderOf(ContentIds.File214), g.Player);
                ExperienceAssert(g.Tasks.IsActive(ContentIds.TaskE2Hide214) && files.CanArchive(ContentIds.File214),
                    "Ellen's active self-archive request supports the Archive menu");
                typeof(FilesApp).GetMethod("FileMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(files, new object[] { ContentIds.File214, g.Player });
                PopupMenu menu = null;
                foreach (var candidate in g.Layers.Popups.GetComponentsInChildren<PopupMenu>())
                    if (candidate.FindRow("Archive") != null) menu = candidate;
                ExperienceAssert(menu != null, "file context menu contains Archive");
                menu.FindRow("Archive").SimulateClick(g.Player);
                ExperienceAssert(g.Files.FolderOf(ContentIds.File214) == ContentIds.FolderArchive && g.Tasks.IsCompleted(ContentIds.TaskE2Hide214),
                    "Archive menu completes Ellen's actual file task");
                ExperienceAssert(!files.CanArchive(ContentIds.File214), "already archived file does not offer a redundant Archive action");
                g.Tasks.Activate(ContentIds.TaskN2Batch45);
                files.ArchiveRemainingBatch(ContentIds.Batch45A, g.Player);
                ExperienceAssert(g.Tasks.IsCompleted(ContentIds.TaskN2Batch45) && g.Files.FolderOf(ContentIds.Batch45A) == ContentIds.FolderArchive
                    && g.Files.FolderOf(ContentIds.Batch45B) == ContentIds.FolderArchive && g.Files.FolderOf(ContentIds.Batch45C) == ContentIds.FolderArchive,
                    "Archive remaining batch completes the later company batch");
                g.Tasks.Activate(ContentIds.TaskN2Batch46);
                string sharedFolder = g.Files.FolderOf(ContentIds.Batch46A);
                files.ArchiveRemainingBatch(ContentIds.Batch46A, g.Player);
                ExperienceAssert(g.Files.FolderOf(ContentIds.Batch46A) == sharedFolder && !g.Tasks.IsCompleted(ContentIds.TaskN2Batch46),
                    "bulk archive preserves Ellen's shared batch interaction");

                GameBootstrap.Restart(3, "work");
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "work", 30f, "editable document test shift");
                g = G;
                g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
                var config = g.Apps.OpenFile(ContentIds.FileSessionCfg, g.Player) as NotepadApp;
                ExperienceAssert(config != null && config.CanSave, "session.cfg opens as an editable document");
                string original = g.Files.GetFile(ContentIds.FileSessionCfg).Content;
                config.OnTyped("\n; readability regression edit", g.Player);
                string draft = config.Text;
                ExperienceAssert(config.HasUnsavedChanges && draft != original, "test creates an actual unsaved document edit");
                ExperienceAssert(ReferenceEquals(config, g.Apps.OpenFile(ContentIds.FileSessionCfg, g.Player)) && config.Text == draft && config.HasUnsavedChanges,
                    "reopening an editable document preserves its unsaved draft");
                ExperienceAssert(g.Files.GetFile(ContentIds.FileSessionCfg).Content == original, "reopening does not silently save the draft");
                Say("PASS readability integration checks complete");
            }
            finally
            {
                DisplaySettings.SetSize(originalReading);
                GameBootstrap.ToTitle();
            }
        }
    }
}
