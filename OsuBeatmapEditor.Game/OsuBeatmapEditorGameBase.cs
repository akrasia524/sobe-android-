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
