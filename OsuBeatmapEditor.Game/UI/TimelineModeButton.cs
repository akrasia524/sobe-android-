using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Phone-only toggle for the top timeline's touch mode: "Select" (drag selects / moves objects, like desktop) or
    /// "Scroll" (drag on empty space scrolls the timeline so objects can be placed precisely; tapping an object selects it and dragging it moves it). Shows the current mode in bold text
    /// with a matching coloured outline (orange = Select, purple = Scroll). Tapping switches the mode.
    /// </summary>
    public partial class TimelineModeButton : ClickableContainer, IHasTooltip
    {
        private static readonly Color4 select_colour = new Color4(0xFF, 0x80, 0x00, 0xFF); // #FF8000
        private static readonly Color4 scroll_colour = new Color4(0x92, 0x67, 0xBE, 0xFF); // #9267BE

        private readonly BindableBool scrollMode;
        private Container content = null!;
        private SpriteText text = null!;

        public TimelineModeButton(BindableBool scrollMode)
        {
            this.scrollMode = scrollMode;
            Action = () => this.scrollMode.Value = !this.scrollMode.Value;
        }

        public LocalisableString TooltipText => "Top timeline mode: Scroll (drag empty space to scroll; tap an object to select it, drag it to move) / Select (drag to rubber-band select and move objects)";

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                BorderThickness = 3,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    text = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                    },
                },
            };

            scrollMode.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            var colour = scrollMode.Value ? scroll_colour : select_colour;
            text.Text = scrollMode.Value ? "Scroll" : "Select";
            text.Colour = colour;
            content.BorderColour = colour;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.92f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }
    }
}
