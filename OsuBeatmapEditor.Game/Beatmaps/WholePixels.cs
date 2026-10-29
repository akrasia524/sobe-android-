using System;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Hit-object positions are saved as whole osu!pixels, but snapping (onto a slider tail, a grid point, a distance
    /// spacing) works with fractional ones. If the preview showed the fractional spot and the save rounded it, the object
    /// visibly jumped up to a pixel on release. Rounding the preview the same way the save does removes the jump.
    /// Uses the same rounding as the save (<see cref="Math.Round(double)"/>), so the two can never disagree.
    /// </summary>
    public static class WholePixels
    {
        public static Vector2 Round(Vector2 position) => new Vector2((float)Math.Round(position.X), (float)Math.Round(position.Y));
    }
}
