using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small rounded "pill" tucked top-left of the play area (just below the timeline) that names the current
    /// editor mode - Review, Modding, or the suggested-map preview. Hidden (faded out) in normal editing. Replaces
    /// the transient toasts that used to announce mode changes.
    /// </summary>
    public partial class ModePill : CompositeDrawable
    {
        private Box background = null!;
        private SpriteText label = null!;

        public ModePill()
        {
            AutoSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new CircularContainer
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    label = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Padding = new MarginPadding { Horizontal = 12, Vertical = 4 },
                        Colour = Color4.White,
                        Font = FontUsage.Default.With(size: 12, weight: "Bold"),
                    },
                },
            };
        }

        /// <summary>Sets the pill's text + colour, fading it in. Null/empty text fades it out.</summary>
        public void SetMode(string? text, Color4 colour)
        {
            if (string.IsNullOrEmpty(text))
            {
                this.FadeOut(150, Easing.OutQuint);
                return;
            }

            label.Text = text.ToUpperInvariant();
            background.FadeColour(colour, 150, Easing.OutQuint);
            this.FadeIn(150, Easing.OutQuint);
        }
    }
}
