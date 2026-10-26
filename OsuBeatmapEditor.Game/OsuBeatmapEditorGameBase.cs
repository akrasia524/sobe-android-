using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Stores;
using osuTK;
using OsuBeatmapEditor.Resources;

namespace OsuBeatmapEditor.Game
{
    public partial class OsuBeatmapEditorGameBase : osu.Framework.Game
    {
        // Anything in this class is shared between the test browser and the game implementation.
        // It allows for caching global dependencies that should be accessible to tests, or changing
        // the screen scaling for all components including the test browser and framework overlays.

        protected override Container<Drawable> Content { get; }

        /// <summary>
        /// Cycles the framework's own performance overlay (off -> frame times -> frame times + threads/GC -> off). It shows how
        /// long each frame spends in Update (CPU logic), Draw (GPU submission) and Input, plus garbage-collection activity - the
        /// numbers needed to tell what makes the editor slow. Wired to a tap on the FPS counter.
        /// </summary>
        public static System.Action? CyclePerformanceOverlay { get; private set; }

        /// <summary>Closes the performance overlay if it is open (cycles it round to "off").</summary>
        public static System.Action? HidePerformanceOverlay { get; private set; }

        // Where the overlay is in its three-step cycle (0 = off, 1 = frame times, 2 = frame times + threads/GC).
        private static int overlayStep;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            CyclePerformanceOverlay = () =>
            {
                CycleFrameStatistics();
                overlayStep = (overlayStep + 1) % 3;
            };
            HidePerformanceOverlay = () =>
            {
                while (overlayStep != 0)
                {
                    CycleFrameStatistics();
                    overlayStep = (overlayStep + 1) % 3;
                }
            };
        }

        protected OsuBeatmapEditorGameBase()
        {
            // Ensure game and tests scale with window size and screen DPI.
            base.Content.Add(Content = new DrawSizePreservingFillContainer
            {
                // You may want to change TargetDrawSize to your "default" resolution, which will decide how things scale and position when using absolute coordinates.
                TargetDrawSize = UI.MobileMetrics.TargetDrawSize
            });
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Resources.AddStore(new DllResourceStore(typeof(OsuBeatmapEditorResources).Assembly));

            // Fallback fonts (Noto, same set osu! uses) so non-Latin metadata (CJK, Hangul) draws instead of "???".
            // Display only: the stored text is never changed by the font.
            foreach (var name in new[] { "Noto-Basic", "Noto-Bopomofo", "Noto-CJK-Basic", "Noto-CJK-Compatibility", "Noto-Hangul" })
                AddFont(Resources, $@"Fonts/Noto/{name}");
        }
    }
}
