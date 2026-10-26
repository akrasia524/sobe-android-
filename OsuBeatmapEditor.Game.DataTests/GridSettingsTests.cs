using NUnit.Framework;
using OsuBeatmapEditor.Game.Screens.Edit;
using osuTK;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class GridSettingsTests
    {
        private static readonly Vector2 centre = new Vector2(256, 192);

        [Test]
        public void SquareGridSnapsToNearestIntersection()
        {
            var snapped = GridSettings.Snap(new Vector2(263, 199), 16, 0, centre);
            Assert.That(snapped.X, Is.EqualTo(256).Within(0.001));
            Assert.That(snapped.Y, Is.EqualTo(192).Within(0.001));

            snapped = GridSettings.Snap(new Vector2(266, 185), 16, 0, centre);
            Assert.That(snapped.X, Is.EqualTo(272).Within(0.001));
            Assert.That(snapped.Y, Is.EqualTo(192).Within(0.001));
        }

        [Test]
        public void SpacingChangesTheStep()
        {
            var snapped = GridSettings.Snap(new Vector2(300, 150), 40, 0, centre);
            Assert.That(snapped.X, Is.EqualTo(296).Within(0.001)); // 256 + 1*40
            Assert.That(snapped.Y, Is.EqualTo(152).Within(0.001)); // 192 - 1*40
        }

        [Test]
        public void OriginShiftsTheGrid()
        {
            var snapped = GridSettings.Snap(new Vector2(100, 100), 16, 0, new Vector2(10, 5));
            Assert.That(snapped.X, Is.EqualTo(106).Within(0.001)); // 10 + 6*16
            Assert.That(snapped.Y, Is.EqualTo(101).Within(0.001)); // 5 + 6*16
        }

        [Test]
        public void RotatedGridIntersectionsSnapToThemselves()
        {
            // The grid point one step along its own x axis, for a grid rotated 30 degrees about the centre.
            float step = 20;
            double a = System.Math.PI / 6;
            var point = new Vector2(256 + (float)(step * System.Math.Cos(a)), 192 + (float)(step * System.Math.Sin(a)));

            var snapped = GridSettings.Snap(point + new Vector2(1.5f, -1f), step, 30, centre);
            Assert.That(snapped.X, Is.EqualTo(point.X).Within(0.01));
            Assert.That(snapped.Y, Is.EqualTo(point.Y).Within(0.01));
        }

        [Test]
        public void ResultStaysInsideThePlayfield()
        {
            var snapped = GridSettings.Snap(new Vector2(511, 383), 128, 0, centre);
            Assert.That(snapped.X, Is.InRange(0f, 512f));
            Assert.That(snapped.Y, Is.InRange(0f, 384f));
        }

        [Test]
        public void PresetCycleGoesTinyToLargeThenHidesThenRestarts()
        {
            var grid = new GridSettings();
            Assert.That(grid.Spacing.Value, Is.EqualTo(16f));

            grid.CyclePreset();
            Assert.That(grid.Spacing.Value, Is.EqualTo(32f));
            grid.CyclePreset();
            Assert.That(grid.Visible.Value, Is.False);
            grid.CyclePreset();
            Assert.That(grid.Visible.Value, Is.True);
            Assert.That(grid.Spacing.Value, Is.EqualTo(4f));
            grid.CyclePreset();
            Assert.That(grid.Spacing.Value, Is.EqualTo(8f));
        }

        [Test]
        public void ResetRestoresDefaults()
        {
            var grid = new GridSettings();
            grid.Spacing.Value = 50;
            grid.Rotation.Value = 12;
            grid.OriginX.Value = 10;
            grid.OriginY.Value = 20;
            grid.Reset();

            Assert.That(grid.Spacing.Value, Is.EqualTo(16f));
            Assert.That(grid.Rotation.Value, Is.EqualTo(0f));
            Assert.That(grid.Origin, Is.EqualTo(centre));
        }
    }
}
