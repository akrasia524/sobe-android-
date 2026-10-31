using System.Collections.Generic;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Screens.Edit;
using osuTK;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class SliderRibbonTests
    {
        [Test]
        public void StraightLineHasOneRibbonWithSideOffsetsOfTheRadius()
        {
            var r = SliderRibbon.Build(new[] { new Vector2(0, 0), new Vector2(50, 0), new Vector2(100, 0) }, 10f);

            Assert.That(r.Valid, Is.True);
            Assert.That(r.Ribbons, Has.Count.EqualTo(1));
            Assert.That(r.Ribbons[0], Has.Count.EqualTo(3));
            Assert.That(r.Joints, Is.Empty);

            foreach (var s in r.Ribbons[0])
            {
                Assert.That(s.Offset.X, Is.EqualTo(0f).Within(0.001));
                Assert.That(System.Math.Abs(s.Offset.Y), Is.EqualTo(10f).Within(0.001));
            }
        }

        [Test]
        public void GentleBendIsMitredIntoOneRibbon()
        {
            // 90 degrees: mitre length is sqrt(2) radii, inside the limit.
            var r = SliderRibbon.Build(new[] { new Vector2(0, 0), new Vector2(50, 0), new Vector2(50, 50) }, 10f);

            Assert.That(r.Ribbons, Has.Count.EqualTo(1));
            Assert.That(r.Joints, Is.Empty);
            Assert.That(r.Ribbons[0][1].Offset.Length, Is.EqualTo(10f * System.MathF.Sqrt(2f)).Within(0.01));
        }

        [Test]
        public void SharpCornerSplitsTheRibbonAndGetsAJoint()
        {
            // A near-reversal: far too sharp for a mitre.
            var r = SliderRibbon.Build(new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(0, 10) }, 10f);

            Assert.That(r.Ribbons, Has.Count.EqualTo(2));
            Assert.That(r.Joints, Has.Count.EqualTo(1));
            Assert.That(r.Joints[0], Is.EqualTo(new Vector2(100, 0)));
            // Each half ends/starts square, so its offset is exactly one radius.
            Assert.That(r.Ribbons[0][^1].Offset.Length, Is.EqualTo(10f).Within(0.001));
            Assert.That(r.Ribbons[1][0].Offset.Length, Is.EqualTo(10f).Within(0.001));
        }

        [Test]
        public void CapsPointAwayFromThePath()
        {
            var r = SliderRibbon.Build(new[] { new Vector2(0, 0), new Vector2(100, 0) }, 10f);

            Assert.That(r.StartDirection.X, Is.EqualTo(-1f).Within(0.001));
            Assert.That(r.EndDirection.X, Is.EqualTo(1f).Within(0.001));
        }

        [Test]
        public void DegeneratePathsAreInvalidNotCrashes()
        {
            Assert.That(SliderRibbon.Build(new[] { new Vector2(5, 5) }, 10f).Valid, Is.False);
            Assert.That(SliderRibbon.Build(new Vector2[0], 10f).Valid, Is.False);
            Assert.That(SliderRibbon.Build(new[] { new Vector2(0, 0), new Vector2(10, 0) }, 0f).Valid, Is.False);
        }

        [Test]
        public void SimplifyDropsPointsOnTheLineAndKeepsCorners()
        {
            var pts = new List<Vector2> { new(0, 0), new(10, 0.05f), new(20, 0), new(30, 0), new(30, 40) };
            var simplified = SliderRibbon.Simplify(pts, 0.25f);

            Assert.That(simplified, Is.EqualTo(new[] { new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 40) }));
        }

        [Test]
        public void SimplifyRemovesDuplicatesAndKeepsEndpoints()
        {
            var simplified = SliderRibbon.Simplify(new[] { new Vector2(1, 1), new Vector2(1, 1), new Vector2(9, 9) }, 0.25f);

            Assert.That(simplified, Is.EqualTo(new[] { new Vector2(1, 1), new Vector2(9, 9) }));
        }

        [Test]
        public void ArcStartsAndEndsAtTheRequestedAngles()
        {
            var arc = SliderRibbon.Arc(new Vector2(10, 10), 5f, 0f, System.MathF.PI, 4);

            Assert.That(arc, Has.Length.EqualTo(5));
            Assert.That(arc[0].X, Is.EqualTo(15f).Within(0.001));
            Assert.That(arc[4].X, Is.EqualTo(5f).Within(0.001));
        }
    }
}
