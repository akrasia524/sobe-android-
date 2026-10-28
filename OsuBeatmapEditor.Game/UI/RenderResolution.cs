using System;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The "Lower render resolution" choices and the maths behind them. Pure (no UI, no Android), so it is unit tested.
    /// The editor is drawn into an off-screen picture of this many pixels and the phone stretches it to the screen, so the
    /// GPU has fewer pixels to fill. Nothing about touch or layout changes: only how sharp the picture is.
    /// </summary>
    public static class RenderResolution
    {
        /// <summary>osu!droid draws everything on a canvas 1280 pixels wide whatever the phone's screen is.</summary>
        public const float DroidWidth = 1280f;

        public const int Off = 0;
        public const int DroidLike = 1;
        public const int ThreeQuarters = 2;
        public const int Sixty = 3;
        public const int ModeCount = 4;

        private const float min_factor = 0.4f;

        public static string Label(int mode) => mode switch
        {
            DroidLike => "1280 px wide (like osu!droid)",
            ThreeQuarters => "75%",
            Sixty => "60%",
            _ => "Off (full resolution)",
        };

        public static int Next(int mode) => (Math.Clamp(mode, 0, ModeCount - 1) + 1) % ModeCount;

        /// <summary>
        /// The fraction of the screen's pixels to render at: 1 means "no lowering at all". Never below 0.4 and never above 1
        /// (a screen already narrower than 1280 pixels is left alone), and any unknown input falls back to 1.
        /// </summary>
        public static float Factor(int mode, float screenPixelWidth)
        {
            float factor = mode switch
            {
                DroidLike => screenPixelWidth > 0 ? DroidWidth / screenPixelWidth : 1f,
                ThreeQuarters => 0.75f,
                Sixty => 0.6f,
                _ => 1f,
            };

            if (float.IsNaN(factor) || float.IsInfinity(factor))
                return 1f;

            return Math.Clamp(factor, min_factor, 1f);
        }

        /// <summary>True when the factor is low enough to be worth an off-screen picture at all.</summary>
        public static bool IsLowered(float factor) => factor < 0.99f;
    }
}
