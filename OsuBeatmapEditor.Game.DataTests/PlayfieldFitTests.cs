using NUnit.Framework;
using OsuBeatmapEditor.Game.Screens.Edit;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class PlayfieldFitTests
    {
        [Test]
        public void WideAreaIsBoundByHeightAndKeepsAspect()
        {
            // Phone-like: very wide, short compose area.
            float s = PlayfieldFit.Scale(1289, 454, 512, 384, PlayfieldFit.EDITOR_FIT);
            Assert.That(384 * s, Is.EqualTo(454).Within(0.001)); // border reaches the bars exactly
            Assert.That(512 * s, Is.LessThanOrEqualTo(1289));
            Assert.That((512 * s) / (384 * s), Is.EqualTo(512f / 384f).Within(1e-5)); // 4:3 preserved
        }

        [Test]
        public void NarrowAreaIsBoundByWidth()
        {
            float s = PlayfieldFit.Scale(400, 900, 512, 384, 1f);
            Assert.That(512 * s, Is.EqualTo(400).Within(0.001));
            Assert.That(384 * s, Is.LessThanOrEqualTo(900));
        }

        [Test]
        public void CircleDiameterScalesWithField()
        {
            // A CS-4 circle is 54.4 osu!px wide; relative to the 512 field width it must stay the same ratio at any fit.
            const float d = 54.4f;
            float small = PlayfieldFit.Scale(1000, 400, 512, 384, 0.97f);
            float big = PlayfieldFit.Scale(1000, 400, 512, 384, 1f);
            Assert.That(d * small / (512 * small), Is.EqualTo(d * big / (512 * big)).Within(1e-5));
        }

        [Test]
        public void DegenerateInputsGiveZero()
        {
            Assert.That(PlayfieldFit.Scale(0, 100, 512, 384, 1f), Is.EqualTo(0));
            Assert.That(PlayfieldFit.Scale(100, -1, 512, 384, 1f), Is.EqualTo(0));
        }
    }
}
