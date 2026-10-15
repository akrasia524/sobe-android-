using osu.Framework;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Central place for phone-vs-desktop sizing so touch devices get finger-sized controls while the desktop
    /// layout stays exactly as designed. Values are in the game's logical units, not pixels or dp.
    /// </summary>
    public static class MobileMetrics
    {
        /// <summary>True on Android / iOS.</summary>
        public static bool IsMobile => RuntimeInfo.IsMobile;

        /// <summary>
        /// The layout size the whole UI is designed against; the game scales everything so this much fits the screen. A smaller
        /// phone value makes every control physically larger (on a ~70 mm-tall phone screen the desktop 768-unit height gives
        /// ~3.6 mm for a 40-unit button, far below a comfortable ~7 mm touch target). Tune this single value after looking at
        /// screenshots: smaller = bigger controls but less room for panels.
        /// </summary>
        public static osuTK.Vector2 TargetDrawSize => IsMobile ? new osuTK.Vector2(1024, 576) : new osuTK.Vector2(1366, 768);

        /// <summary>Picks the phone value on mobile, the desktop value otherwise.</summary>
        public static float Pick(float desktop, float mobile) => IsMobile ? mobile * density_compensation : desktop;

        /// <summary>
        /// The phone values below were tuned (and phone-tested) when the layout was 1366x768; they are kept at the same physical size
        /// by this factor (old scale / new scale) so that only the code that never had phone sizing grows with <see cref="TargetDrawSize"/>.
        /// </summary>
        private const float density_compensation = 0.75f;

        /// <summary>Height of the main-menu top bar.</summary>
        public static float TopBarHeight => Pick(52, 80);

        /// <summary>Side length of a standalone top-bar icon button.</summary>
        public static float TopBarButton => Pick(40, 64);

        /// <summary>Side length of one segment in the grouped playback card.</summary>
        public static float PlaybackButton => Pick(34, 56);

        /// <summary>Height of the search / sort row.</summary>
        public static float SearchRowHeight => Pick(40, 60);

        /// <summary>Size of the main-menu "New Beatmap" button.</summary>
        public static osuTK.Vector2 NewBeatmapButton => IsMobile ? new osuTK.Vector2(300, 76) * density_compensation : new osuTK.Vector2(220, 56);
    }
}
