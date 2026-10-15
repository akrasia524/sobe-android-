using System;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Pure maths for fitting the 512x384 osu! play area into the editor's compose area. One uniform scale
    /// (never separate x/y), so the field keeps its 4:3 shape and every osu!-pixel quantity - including circle
    /// diameter derived from CS - scales with it, i.e. objects stay at the osu!-correct size relative to the field.
    /// </summary>
    public static class PlayfieldFit
    {
        /// <summary>Editor fit: the field's outer border touches the bars above/below it (no extra margin).</summary>
        public const float EDITOR_FIT = 1.0f;

        /// <summary>Largest uniform scale that fits <paramref name="fieldWidth"/> x <paramref name="fieldHeight"/> into the area.</summary>
        public static float Scale(float areaWidth, float areaHeight, float fieldWidth, float fieldHeight, float fitFactor)
        {
            if (areaWidth <= 0 || areaHeight <= 0 || fieldWidth <= 0 || fieldHeight <= 0)
                return 0;

            return Math.Min(areaWidth / fieldWidth, areaHeight / fieldHeight) * fitFactor;
        }
    }
}
