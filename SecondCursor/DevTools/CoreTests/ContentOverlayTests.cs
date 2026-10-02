using System;
using System.Linq;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Night overlays on top of the base content (expansion spec 2.1).</summary>
    public class ContentOverlayTests
    {
        static FileData File(string id, string folder = "intake", string content = "", bool removed = false) =>
            new FileData { id = id, name = id + ".dat", folder = folder, content = content, removed = removed };

        [Fact]
        public void FilesAreReplacedAddedAndRemovedById()
        {
            var b = new FileSystemData
            {
                folders = new[] { new FolderData { id = "intake", name = "Intake" }, new FolderData { id = "old", name = "Old" } },
                files = new[] { File("a", content: "A"), File("b", content: "B"), File("c", content: "C") },
            };
            var o = new FileSystemData
            {
                folders = new[] { new FolderData { id = "old", removed = true }, new FolderData { id = "restricted", name = "Restricted", locked = true, code = "0217" } },
                files = new[] { File("b", content: "B2"), File("c", removed: true), File("d", content: "D") },
            };
            var m = ContentOverlay.Apply(b, o);
            Assert.Equal(new[] { "a", "b", "d" }, m.files.Select(f => f.id).ToArray());
            Assert.Equal("B2", m.files[1].content);
            Assert.Equal(new[] { "intake", "restricted" }, m.folders.Select(f => f.id).ToArray());
            Assert.Equal("0217", m.folders[1].code);
            // The inputs are untouched.
            Assert.Equal(3, b.files.Length);
            Assert.Equal("B", b.files[1].content);
            Assert.Equal(3, o.files.Length);
        }

        [Fact]
        public void EmailsCanBeRemoved()
        {
            var b = new EmailsData { emails = new[] { new EmailData { id = "m1", subject = "one" }, new EmailData { id = "m2", subject = "two" } } };
            var o = new EmailsData { emails = new[] { new EmailData { id = "m1", removed = true }, new EmailData { id = "m3", subject = "three" } } };
            var m = ContentOverlay.Apply(b, o);
            Assert.Equal(new[] { "m2", "m3" }, m.emails.Select(e => e.id).ToArray());
        }

        [Fact]
        public void EmployeesOrdersAndTasksReplaceOrAdd()
        {
            var employees = ContentOverlay.Apply(
                new EmployeesData { employees = new[] { new EmployeeData { id = "017", status = "ACTIVE" }, new EmployeeData { id = "118" } } },
                new EmployeesData { employees = new[] { new EmployeeData { id = "017", status = "RETAINED" }, new EmployeeData { id = "209" } } });
            Assert.Equal(new[] { "017", "118", "209" }, employees.employees.Select(e => e.id).ToArray());
            Assert.Equal("RETAINED", employees.employees[0].status);

            var orders = ContentOverlay.Apply(
                new WorkOrdersData { orders = new[] { new WorkOrderData { id = "wo_1", correct = "approve" } } },
                new WorkOrdersData { orders = new[] { new WorkOrderData { id = "wo_1", correct = "reject" }, new WorkOrderData { id = "wo_2", rule = "shelf" } } });
            Assert.Equal("reject", orders.orders[0].correct);
            Assert.Equal("shelf", orders.orders[1].rule);

            var tasks = ContentOverlay.Apply(
                new TasksData { tasks = new[] { new TaskData { id = "t1", title = "One" } } },
                new TasksData { tasks = new[] { new TaskData { id = "e1", author = "entity", timeout = 75f, deadline = "3:00 AM" } } });
            Assert.Equal(new[] { "t1", "e1" }, tasks.tasks.Select(t => t.id).ToArray());
            Assert.Equal("entity", tasks.tasks[1].author);
        }

        [Fact]
        public void StringsMergeByKey()
        {
            var m = ContentOverlay.Apply(
                new StringTableData { entries = new[] { new StringEntry { key = "a", value = "1" }, new StringEntry { key = "b", value = "2" } } },
                new StringTableData { entries = new[] { new StringEntry { key = "b", value = "two" }, new StringEntry { key = "c", value = "3" } } });
            Assert.Equal("1,two,3", string.Join(",", m.entries.Select(e => e.value)));
        }

        [Fact]
        public void StoryArraysReplaceOnlyWhenNotEmpty()
        {
            var b = new StoryData
            {
                biosLines = new[] { "BIOS 1" }, splashTagline = "base", endingLines = new[] { "END" }, anomalyNotes = new[] { "note" },
                cameras = new[] { new CameraData { id = "cam01", label = "CAM 01" }, new CameraData { id = "cam03", label = "CAM 03" } },
            };
            var o = new StoryData
            {
                biosLines = new[] { "BIOS 2", "copy" }, splashTagline = "", endingLines = Array.Empty<string>(),
                cameras = new[] { new CameraData { id = "cam03", label = "CAM 03 B" }, new CameraData { id = "cam00", label = "CAM 00", hidden = true } },
            };
            var m = ContentOverlay.Apply(b, o);
            Assert.Equal(new[] { "BIOS 2", "copy" }, m.biosLines);
            Assert.Equal("base", m.splashTagline);
            Assert.Equal(new[] { "END" }, m.endingLines);
            Assert.Equal(new[] { "note" }, m.anomalyNotes);
            Assert.Equal(new[] { "cam01", "cam03", "cam00" }, m.cameras.Select(c => c.id).ToArray());
            Assert.Equal("CAM 03 B", m.cameras[1].label);
            Assert.True(m.cameras[2].hidden);
        }

        [Fact]
        public void DialogueMergesExchangesAndLineSets()
        {
            var b = new DialogueData
            {
                exchanges = new[] { new ExchangeData { id = "ex_stop", entityLines = new[] { "STOP" } } },
                panicLines = new[] { "NO" }, cameraLines = new[] { "LOOK" }, recordLines = new[] { "YOU" },
                lineSets = new[] { new LineSetData { id = "end", lines = new[] { "BYE" } } },
            };
            var o = new DialogueData
            {
                exchanges = new[] { new ExchangeData { id = "ex2_back", voice = "", entityLines = new[] { "BACK" } } },
                panicLines = new[] { "CLOSE IT" },
                lineSets = new[] { new LineSetData { id = "end", lines = new[] { "GOODBYE" } }, new LineSetData { id = "g2", voice = "gary", lines = new[] { "night casey" } } },
            };
            var m = ContentOverlay.Apply(b, o);
            Assert.Equal(new[] { "ex_stop", "ex2_back" }, m.exchanges.Select(x => x.id).ToArray());
            Assert.Equal(new[] { "CLOSE IT" }, m.panicLines);
            Assert.Equal(new[] { "LOOK" }, m.cameraLines);
            Assert.Equal(new[] { "YOU" }, m.recordLines);
            var db = new ContentDatabase(null, null, null, null, null, null, null, m);
            Assert.Equal(new[] { "GOODBYE" }, db.Lines("end"));
            Assert.Equal("gary", db.LineSet("g2").voice);
            Assert.Empty(db.Lines("missing"));
        }

        [Fact]
        public void NullOverlaysLeaveTheBaseAlone()
        {
            var b = new EmailsData { emails = new[] { new EmailData { id = "m1" } } };
            Assert.Same(b, ContentOverlay.Apply(b, (EmailsData)null));
            var fromNothing = ContentOverlay.Apply((EmailsData)null, b);
            Assert.Single(fromNothing.emails);
        }

        [Fact]
        public void OverlaysApplyCumulatively()
        {
            // Night 3 loads base + night2 + night3: a later night overrides an earlier one.
            var basePack = new ContentPack { FileSystem = new FileSystemData { files = new[] { File("a", content: "base") } } };
            var night2 = new ContentPack { FileSystem = new FileSystemData { files = new[] { File("a", content: "n2"), File("b", content: "n2") } } };
            var night3 = new ContentPack
            {
                FileSystem = new FileSystemData { files = new[] { File("b", removed: true), File("c", content: "n3") } },
                Emails = new EmailsData { emails = new[] { new EmailData { id = "mail_n3", subject = "3" } } },
            };
            Assert.True(new ContentPack().IsEmpty);
            var n3 = basePack.Overlay(night2).Overlay(night3);
            Assert.Equal(new[] { "a", "c" }, n3.FileSystem.files.Select(f => f.id).ToArray());
            Assert.Equal("n2", n3.FileSystem.files[0].content);
            Assert.Single(n3.Emails.emails);
            var n2 = basePack.Overlay(night2);
            Assert.Equal(new[] { "a", "b" }, n2.FileSystem.files.Select(f => f.id).ToArray());
            Assert.Null(n2.Emails);
            Assert.Equal("base", basePack.FileSystem.files[0].content);
        }

        [Fact]
        public void RemovedEntriesNeverReachTheFileSystem()
        {
            var fs = new VirtualFileSystem(new FileSystemData
            {
                folders = new[] { new FolderData { id = "intake", name = "Intake" }, new FolderData { id = "gone", name = "Gone", removed = true } },
                files = new[] { File("a"), File("b", removed: true) },
            });
            Assert.NotNull(fs.GetFile("a"));
            Assert.Null(fs.GetFile("b"));
            Assert.Null(fs.GetFolder("gone"));
        }

        [Fact]
        public void ValidationReportsBadLineSetsAndEmployeeTargets()
        {
            var dialogue = new DialogueData
            {
                lineSets = new[] { new LineSetData { id = "x", lines = new[] { "A" } }, new LineSetData { id = "x", lines = new[] { "B" } } },
            };
            var tasks = new TasksData { tasks = new[] { new TaskData { id = "t_view", type = "ViewEmployee", targets = new[] { "999" } } } };
            var db = new ContentDatabase(null, null, null, null, null, null, tasks, dialogue);
            Assert.Contains(db.Problems, p => p.Contains("Duplicate line set id 'x'"));
            Assert.Contains(db.Problems, p => p.Contains("unknown employee '999'"));
            Assert.Equal(new[] { "A" }, db.Lines("x"));
        }
    }
}
