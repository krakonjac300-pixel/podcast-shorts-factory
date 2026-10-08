using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    public class CameraExposureTests
    {
        [Fact]
        public void ReadingWindowEndsInCaptureOnlyAfterNineSecondsOfWatching()
        {
            var exposure = new CameraExposure();
            exposure.Tick(true, 8.9f);
            Assert.False(exposure.Finished);
            exposure.Tick(true, 0.2f);
            Assert.True(exposure.Captured);
            Assert.False(exposure.Escaped);
        }

        [Fact]
        public void ClosingAtTheLastMomentPreventsCapture()
        {
            var exposure = new CameraExposure();
            exposure.Tick(true, 8.9f);
            exposure.Tick(false, 2f);
            Assert.True(exposure.Escaped);
            exposure.Tick(true, 100f);
            Assert.False(exposure.Captured);
        }

        [Fact]
        public void BrieflySwitchingDoesNotEraseTheThreatOrCountAsEscape()
        {
            var exposure = new CameraExposure();
            exposure.Tick(true, 5f);
            exposure.Tick(false, 1f);
            exposure.Tick(true, 4f);
            Assert.True(exposure.Captured);
            Assert.Equal(0f, exposure.AwaySeconds);
        }

        [Fact]
        public void PausedFramesDoNotAdvanceTheThreat()
        {
            var exposure = new CameraExposure();
            exposure.Tick(true, 0f);
            exposure.Tick(true, -1f);
            exposure.Tick(true, float.NaN);
            Assert.False(exposure.Finished);
            Assert.Equal(0f, exposure.WatchedSeconds);
        }
    }
}
