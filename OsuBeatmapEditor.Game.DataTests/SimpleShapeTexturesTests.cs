using NUnit.Framework;
using OsuBeatmapEditor.Game.Screens.Edit;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class SimpleShapeTexturesTests
    {
        [Test]
        public void DiscIsSolidInsideAndEmptyOutside()
        {
            Assert.That(SimpleShapeTextures.Coverage(0, 50, 0), Is.EqualTo(1f));
            Assert.That(SimpleShapeTextures.Coverage(49, 50, 0), Is.EqualTo(1f));
            Assert.That(SimpleShapeTextures.Coverage(60, 50, 0), Is.EqualTo(0f));
        }

        [Test]
        public void RingIsEmptyInTheMiddleAndSolidOnTheBand()
        {
            // radius 100, band 10 wide: solid from 90 to 100.
            Assert.That(SimpleShapeTextures.Coverage(0, 100, 10), Is.EqualTo(0f));
            Assert.That(SimpleShapeTextures.Coverage(50, 100, 10), Is.EqualTo(0f));
            Assert.That(SimpleShapeTextures.Coverage(95, 100, 10), Is.EqualTo(1f));
            Assert.That(SimpleShapeTextures.Coverage(120, 100, 10), Is.EqualTo(0f));
        }

        [Test]
        public void EdgesAreSoftNotJagged()
        {
            float edge = SimpleShapeTextures.Coverage(50, 50, 0);
            Assert.That(edge, Is.GreaterThan(0f).And.LessThan(1f).Or.EqualTo(1f));
            Assert.That(SimpleShapeTextures.Coverage(50.4f, 50, 0), Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void ThicknessIsQuantisedAndBounded()
        {
            Assert.That(SimpleShapeTextures.Quantise(0.06f), Is.EqualTo(60));
            Assert.That(SimpleShapeTextures.Quantise(0.0601f), Is.EqualTo(60));
            Assert.That(SimpleShapeTextures.Quantise(0f), Is.EqualTo(4));
            Assert.That(SimpleShapeTextures.Quantise(5f), Is.EqualTo(500));
        }
    }
}
