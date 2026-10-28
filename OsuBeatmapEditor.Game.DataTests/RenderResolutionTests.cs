using NUnit.Framework;
using OsuBeatmapEditor.Game.UI;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class RenderResolutionTests
    {
        [Test]
        public void OffIsFullResolution()
        {
            Assert.That(RenderResolution.Factor(RenderResolution.Off, 1612), Is.EqualTo(1f));
            Assert.That(RenderResolution.IsLowered(RenderResolution.Factor(RenderResolution.Off, 1612)), Is.False);
        }

        [Test]
        public void DroidLikeScalesToTheFixedWidth()
        {
            // The Infinix Hot 50 is 1612 px wide: 1280 / 1612.
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 1612), Is.EqualTo(1280f / 1612f).Within(0.0001));
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 2560), Is.EqualTo(0.5f).Within(0.0001));
        }

        [Test]
        public void DroidLikeNeverEnlargesASmallScreen()
        {
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 1000), Is.EqualTo(1f));
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 1280), Is.EqualTo(1f));
        }

        [Test]
        public void DroidLikeNeverGoesBelowTheFloor()
        {
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 100000), Is.EqualTo(0.4f).Within(0.0001));
        }

        [Test]
        public void UnknownWidthOrModeFallsBackToFullResolution()
        {
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, 0), Is.EqualTo(1f));
            Assert.That(RenderResolution.Factor(RenderResolution.DroidLike, float.NaN), Is.EqualTo(1f));
            Assert.That(RenderResolution.Factor(99, 1612), Is.EqualTo(1f));
            Assert.That(RenderResolution.Factor(-3, 1612), Is.EqualTo(1f));
        }

        [Test]
        public void FixedPercentagesAreIndependentOfTheScreen()
        {
            Assert.That(RenderResolution.Factor(RenderResolution.ThreeQuarters, 1612), Is.EqualTo(0.75f));
            Assert.That(RenderResolution.Factor(RenderResolution.Sixty, 720), Is.EqualTo(0.6f));
        }

        [Test]
        public void NextCyclesThroughEveryModeAndWraps()
        {
            int mode = RenderResolution.Off;
            var seen = new System.Collections.Generic.List<int>();
            for (int i = 0; i < RenderResolution.ModeCount; i++)
            {
                seen.Add(mode);
                mode = RenderResolution.Next(mode);
            }

            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(mode, Is.EqualTo(RenderResolution.Off));
            Assert.That(RenderResolution.Next(99), Is.EqualTo(RenderResolution.Off));
        }

        [Test]
        public void EveryModeHasALabel()
        {
            for (int i = 0; i < RenderResolution.ModeCount; i++)
                Assert.That(RenderResolution.Label(i), Is.Not.Empty);
        }
    }
}
