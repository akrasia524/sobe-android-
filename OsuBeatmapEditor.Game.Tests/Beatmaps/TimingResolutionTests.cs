using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Tests.Beatmaps
{
    [TestFixture]
    public class TimingResolutionTests
    {
        [Test]
        public void SliderOnCoincidentRedGreenUsesGreenVelocity()
        {
            // A red (BPM) line and a green (SV 1.5) line share the exact same time, with a slider sitting on them.
            // The green's velocity must win regardless of the order the two lines appear, otherwise the slider is
            // parsed 1.5x too long. Regression for "Dale, Zelda, Dale" at 10787ms. The green is listed FIRST here so
            // the test exercises the red-before-green tie-break directly, not the (unstable) sort's luck.
            string osu = string.Join("\n",
                "osu file format v14",
                "",
                "[Difficulty]",
                "SliderMultiplier:2",
                "",
                "[TimingPoints]",
                "1000,-66.6666666666667,4,2,0,65,0,0",
                "1000,368.098159509202,4,2,0,65,1,0",
                "",
                "[HitObjects]",
                "0,0,1000,2,0,L|100:0,1,300");

            var beatmap = OsuFileDecoder.Decode(new StringReader(osu));
            var slider = beatmap.HitObjects.Single(h => h.Kind == HitObjectKind.Slider);

            // 300 px * 368.098 beatLength / (mult 2 * 100 * sv 1.5) = 368.098 ms. With sv=1 (the bug) it would be 552 ms.
            Assert.That(slider.Duration, Is.EqualTo(368.098).Within(1.0));
        }
    }
}
