using System;
using System.IO;
using System.Threading;
using SecondCursor.Core.Util;
using Xunit;

namespace SecondCursor.Tests
{
    public class SaveReliabilityTests
    {
        [Fact]
        public void ABriefSharingLockDoesNotLoseTheLatestCheckpoint()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "save_review_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var writer = new BackgroundFileWriter();
            string path = Path.Combine(folder, "progress.json");
            try
            {
                File.WriteAllText(path, "old checkpoint");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    writer.Enqueue(path, "new checkpoint");
                    Assert.True(SpinWait.SpinUntil(() => File.Exists(path + ".tmp"), 2000));
                    Thread.Sleep(60);
                }
                Assert.True(writer.Flush());
                Assert.Equal("new checkpoint", File.ReadAllText(path));
                Assert.Equal("old checkpoint", File.ReadAllText(path + ".bak"));
                Assert.Null(writer.TakeMessages());
            }
            finally
            {
                writer.Flush();
                Directory.Delete(folder, true);
            }
        }
    }
}
