using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// "Lite interface" (phone performance mode): controls are drawn as just their icon / text - no filled shape, outline or rounded
    /// masking, and no press animation - so the editor needs far fewer draw commands. Read when a control is built, so a change
    /// applies the next time a map is opened.
    /// </summary>
    public static class LiteInterface
    {
        public static bool Enabled { get; set; }

        /// <summary>Strips a control's chrome: no rounded masking, no outline, and its filled background boxes are hidden.</summary>
        public static void Flatten(Container content)
        {
            content.Masking = false;
            content.BorderThickness = 0;
            foreach (var box in content.Children.OfType<Box>())
                box.Alpha = 0;
        }
    }
}
