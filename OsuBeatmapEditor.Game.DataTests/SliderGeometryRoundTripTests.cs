using System.Linq;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.DataTests
{
    /// <summary>Slider control points (including the END point and red anchors) must survive parse -> encode -> parse.</summary>
    [TestFixture]
    public class SliderGeometryRoundTripTests
    {
        // (index in Fixtures.Full's [HitObjects], expected curve field). These are the canonical forms we write.
        private static readonly object[] canonical_cases =
        {
            new object[] { 1, "B|200:100|300:200" },
            new object[] { 2, "B|100:50|100:50|150:100" }, // red anchor via duplicate point
            new object[] { 3, "P|120:20|200:80" },
            new object[] { 4, "L|100:0|100:100|0:100" },
            new object[] { 5, "C|20:5|30:10|40:5" },
            new object[] { 6, "B|150.5:250.25|200.75:200.5" }, // fractional coordinates
        };

        [TestCaseSource(nameof(canonical_cases))]
        public void CurveFieldReproducesTheOriginalCurveText(int index, string expectedCurve)
        {
            string raw = Fixtures.HitObjectLines(Fixtures.Full)[index];
            Assert.That(raw.Split(',')[5], Is.EqualTo(expectedCurve), "fixture sanity");

            var points = SliderGeometry.ParseControlPoints(raw);
            Assert.That(SliderGeometry.CurveField(points), Is.EqualTo(expectedCurve));
        }

        [TestCaseSource(nameof(canonical_cases))]
        public void ParseEncodeParseGivesIdenticalControlPoints(int index, string _)
        {
            string raw = Fixtures.HitObjectLines(Fixtures.Full)[index];
            var first = SliderGeometry.ParseControlPoints(raw);

            string rebuilt = HitObjectLineEditor.SetSliderCurve(raw, first, double.Parse(raw.Split(',')[7], System.Globalization.CultureInfo.InvariantCulture));
            var second = SliderGeometry.ParseControlPoints(rebuilt);

            Assert.That(second, Is.EqualTo(first));
        }

        [TestCaseSource(nameof(canonical_cases))]
        public void SetSliderCurveWithUnchangedPointsReturnsTheOriginalLine(int index, string _)
        {
            string raw = Fixtures.HitObjectLines(Fixtures.Full)[index];
            var points = SliderGeometry.ParseControlPoints(raw);
            double length = double.Parse(raw.Split(',')[7], System.Globalization.CultureInfo.InvariantCulture);

            // Slides, edge sounds/sets and the hitsample (incl. the custom filename) must not be touched either.
            Assert.That(HitObjectLineEditor.SetSliderCurve(raw, points, length), Is.EqualTo(raw));
        }

        [Test]
        public void TheEndAnchorIsKept()
        {
            // The old editor lost the last control point; here the parsed list must end exactly at the file's last coordinate.
            var points = SliderGeometry.ParseControlPoints(Fixtures.HitObjectLines(Fixtures.Full)[1]);
            Assert.That(points, Has.Count.EqualTo(3));
            Assert.That(points.Last().X, Is.EqualTo(300f));
            Assert.That(points.Last().Y, Is.EqualTo(200f));
        }

        [Test]
        public void TheRedAnchorDuplicateIsKeptAsATypedPoint()
        {
            var points = SliderGeometry.ParseControlPoints(Fixtures.HitObjectLines(Fixtures.Full)[2]);
            Assert.That(points.Count(p => p.IsSegmentStart), Is.EqualTo(2), "head + the red anchor");
            Assert.That(points.Last().X, Is.EqualTo(150f));
        }

        [Test]
        public void MixedSegmentTypesSurviveARoundTrip()
        {
            const string raw = "0,0,1000,2,0,L|10:10|20:20|B|30:30|40:40|50:50,1,100,0|0,0:0|0:0,0:0:0:0:";
            var first = SliderGeometry.ParseControlPoints(raw);
            Assert.That(first.Select(p => p.Type?.Type).Where(t => t != null).Distinct().Count(), Is.EqualTo(2), "fixture sanity: two spline types");

            var second = SliderGeometry.ParseControlPoints(HitObjectLineEditor.SetSliderCurve(raw, first, 100));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void ComputedPathIsTheSameAfterARoundTrip()
        {
            string raw = Fixtures.HitObjectLines(Fixtures.Full)[3];
            var first = SliderGeometry.ParseControlPoints(raw);
            var second = SliderGeometry.ParseControlPoints(HitObjectLineEditor.SetSliderCurve(raw, first, 200.000006));

            Assert.That(SliderGeometry.ComputePath(second, 200.000006), Is.EqualTo(SliderGeometry.ComputePath(first, 200.000006)));
        }

        [Test]
        public void ShiftPositionKeepsFractionsAndMovesEveryAnchor()
        {
            string raw = Fixtures.HitObjectLines(Fixtures.Full)[6];
            Assert.That(HitObjectLineEditor.ShiftPosition(raw, 10, -5),
                Is.EqualTo("110.5,195.25,5000,2,0,B|160.5:245.25|210.75:195.5,1,120,0|0,0:0|0:0,0:0:0:0:"));
        }
    }
}
