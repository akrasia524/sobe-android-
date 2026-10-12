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

        /// <summary>Picks the phone value on mobile, the desktop value otherwise.</summary>
        public static float Pick(float desktop, float mobile) => IsMobile ? mobile : desktop;

        /// <summary>Height of the main-menu top bar.</summary>
        public static float TopBarHeight => Pick(52, 80);

        /// <summary>Side length of a standalone top-bar icon button.</summary>
        public static float TopBarButton => Pick(40, 64);

        /// <summary>Side length of one segment in the grouped playback card.</summary>
        public static float PlaybackButton => Pick(34, 56);

        /// <summary>Height of the search / sort row.</summary>
        public static float SearchRowHeight => Pick(40, 60);

        /// <summary>Size of the main-menu "New Beatmap" button.</summary>
        public static osuTK.Vector2 NewBeatmapButton => IsMobile ? new osuTK.Vector2(300, 76) : new osuTK.Vector2(220, 56);
    }
}
