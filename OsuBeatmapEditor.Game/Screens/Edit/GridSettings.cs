using System;
using osu.Framework.Bindables;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The editor's snapping grid, modelled on osu!lazer's grid tool: a square grid with a spacing (4-128 osu!px), a rotation
    /// (-45..45 degrees) and an origin point (default the playfield centre). Pure maths + bindables, so it can be tested.
    /// </summary>
    public class GridSettings
    {
        public const float DefaultSpacing = 16f;
        public const float PlayfieldWidth = 512f;
        public const float PlayfieldHeight = 384f;

        /// <summary>The grid's spacing presets (tiny, small, medium, large - lazer's Ctrl+1..4), used by the G cycle.</summary>
        public static readonly float[] Presets = { 4f, 8f, 16f, 32f };

        public readonly BindableFloat Spacing = new BindableFloat(DefaultSpacing) { MinValue = 4f, MaxValue = 128f, Precision = 1f };
        public readonly BindableFloat Rotation = new BindableFloat(0f) { MinValue = -45f, MaxValue = 45f, Precision = 1f };
        public readonly BindableFloat OriginX = new BindableFloat(PlayfieldWidth / 2f) { MinValue = 0f, MaxValue = PlayfieldWidth, Precision = 1f };
        public readonly BindableFloat OriginY = new BindableFloat(PlayfieldHeight / 2f) { MinValue = 0f, MaxValue = PlayfieldHeight, Precision = 1f };

        /// <summary>Whether the grid lines are drawn (G cycles through the presets and then hides them).</summary>
        public readonly BindableBool Visible = new BindableBool(true);

        public Vector2 Origin => new Vector2(OriginX.Value, OriginY.Value);

        public void Reset()
        {
            Spacing.SetDefault();
            Rotation.SetDefault();
            OriginX.SetDefault();
            OriginY.SetDefault();
        }

        /// <summary>The nearest grid intersection to <paramref name="position"/>, clamped to the playfield.</summary>
        public Vector2 Snap(Vector2 position)
            => Snap(position, Spacing.Value, Rotation.Value, Origin);

        public static Vector2 Snap(Vector2 position, float spacing, float rotationDegrees, Vector2 origin)
        {
            if (spacing <= 0)
                return position;

            double a = rotationDegrees * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);

            // Into the grid's own axes (undo the rotation about the origin), round to the nearest cell corner, and back out.
            double dx = position.X - origin.X, dy = position.Y - origin.Y;
            double gx = dx * cos + dy * sin;
            double gy = -dx * sin + dy * cos;

            // The nearest grid intersection that lies INSIDE the playfield. (Clamping the nearest intersection instead leaves a
            // stretch near the edges where the result sits still while the cursor keeps moving - an invisible wall.)
            int ix0 = (int)Math.Floor(gx / spacing), iy0 = (int)Math.Floor(gy / spacing);
            bool found = false;
            double bestDistance = double.MaxValue, bestX = 0, bestY = 0;

            for (int ix = ix0 - 1; ix <= ix0 + 2; ix++)
            {
                for (int iy = iy0 - 1; iy <= iy0 + 2; iy++)
                {
                    double cx = ix * (double)spacing, cy = iy * (double)spacing;
                    double x = origin.X + cx * cos - cy * sin;
                    double y = origin.Y + cx * sin + cy * cos;

                    if (x < -0.001 || x > PlayfieldWidth + 0.001 || y < -0.001 || y > PlayfieldHeight + 0.001)
                        continue;

                    double d = (x - position.X) * (x - position.X) + (y - position.Y) * (y - position.Y);
                    if (d < bestDistance)
                    {
                        found = true;
                        bestDistance = d;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            if (!found)
            {
                // No intersection near here is inside the playfield (a huge spacing off in a corner): fall back to the clamped nearest one.
                double cx = Math.Round(gx / spacing) * spacing, cy = Math.Round(gy / spacing) * spacing;
                bestX = origin.X + cx * cos - cy * sin;
                bestY = origin.Y + cx * sin + cy * cos;
            }

            return new Vector2(
                Math.Clamp((float)bestX, 0f, PlayfieldWidth),
                Math.Clamp((float)bestY, 0f, PlayfieldHeight));
        }

        /// <summary>The next spacing in the G cycle: the next preset above the current spacing, then "hidden", then the first preset again.</summary>
        public void CyclePreset()
        {
            if (!Visible.Value)
            {
                Visible.Value = true;
                Spacing.Value = Presets[0];
                return;
            }

            foreach (float preset in Presets)
            {
                if (preset > Spacing.Value + 0.001f)
                {
                    Spacing.Value = preset;
                    return;
                }
            }

            Visible.Value = false;
        }
    }
}
