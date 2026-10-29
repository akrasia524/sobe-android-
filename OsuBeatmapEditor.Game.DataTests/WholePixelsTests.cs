using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;
using osuTK;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class WholePixelsTests
    {
        [Test]
        public void RoundsToWholePixels()
        {
            var r = WholePixels.Round(new Vector2(203.6f, 100.2f));
            Assert.That(r.X, Is.EqualTo(204f));
            Assert.That(r.Y, Is.EqualTo(100f));
        }

        [Test]
        public void MatchesTheSavedPosition()
        {
            // The save does (int)Math.Round(x); the preview must land on exactly the same pixel, including at .5.
            foreach (float v in new[] { 0.5f, 1.5f, 2.5f, 203.5f, 99.49f, 99.51f, -0.4f })
                Assert.That(WholePixels.Round(new Vector2(v, v)).X, Is.EqualTo((float)(int)System.Math.Round(v)));
        }

        [Test]
        public void WholeValuesAreUnchanged()
        {
            Assert.That(WholePixels.Round(new Vector2(256, 192)), Is.EqualTo(new Vector2(256, 192)));
        }
    }
}
