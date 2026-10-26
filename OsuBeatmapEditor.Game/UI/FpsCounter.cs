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

        [osu.Framework.Allocation.Resolved]
        private OsuBeatmapEditor.Game.Screens.Edit.EditorSettings settings { get; set; } = null!;

        [osu.Framework.Allocation.Resolved]
        private osu.Framework.Platform.GameHost host { get; set; } = null!;
        private double averageFrameTime;
        private double sinceTextUpdate;

        public osu.Framework.Localisation.LocalisableString TooltipText => "Frames per second (the performance details on tap can be turned on in Settings > Performance)";

        public FpsCounter()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
            // Does nothing unless "Tap FPS counter for performance details" is on in Settings > Performance.
            Action = () =>
            {
                if (settings.PerfDetailsOnTap.Value)
                    OsuBeatmapEditorGameBase.CyclePerformanceOverlay?.Invoke();
            };
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
                // In multi-threaded mode the update thread can race ahead (100+ Hz) while the screen still only redraws at the
                // draw thread's pace, so show the DRAW rate - the frames you actually see - plus the update rate.
                double draw = host.DrawThread.Clock.FramesPerSecond;
                double update = 1000.0 / averageFrameTime;
                text.Text = draw > 0 && draw < update * 0.9
                    ? $"{draw:0} FPS (upd {update:0})"
                    : $"{update:0} FPS";
            }
        }
    }
}
