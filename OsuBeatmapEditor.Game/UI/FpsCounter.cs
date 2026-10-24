using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A small, unobtrusive frames-per-second readout. The rate is exponentially smoothed so the value
    /// stays readable rather than flickering every frame.
    /// </summary>
    public partial class FpsCounter : ClickableContainer, osu.Framework.Graphics.Cursor.IHasTooltip
    {
        private SpriteText text = null!;
        private double averageFrameTime;
        private double sinceTextUpdate;

        public osu.Framework.Localisation.LocalisableString TooltipText => "Tap to show / cycle the performance details (frame times)";

        public FpsCounter()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
            Action = () => OsuBeatmapEditorGameBase.CyclePerformanceOverlay?.Invoke();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.6f },
                text = new SpriteText
                {
                    Margin = new MarginPadding { Horizontal = 8, Vertical = 4 },
                    Colour = OsuColour.TextMuted,
                    Font = FontUsage.Default.With(size: 14, weight: "Bold", fixedWidth: true),
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            double elapsed = Clock.ElapsedFrameTime;
            if (elapsed <= 0)
                return;

            // Exponential moving average over frame time, then invert to a rate.
            averageFrameTime = averageFrameTime == 0 ? elapsed : averageFrameTime * 0.9 + elapsed * 0.1;

            // The text only needs refreshing a few times a second (a new string every frame is pointless garbage).
            sinceTextUpdate += elapsed;
            if (sinceTextUpdate >= 250)
            {
                sinceTextUpdate = 0;
                text.Text = $"{1000.0 / averageFrameTime:0} FPS";
            }
        }
    }
}
