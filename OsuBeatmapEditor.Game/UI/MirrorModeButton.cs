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
    /// Phone-only round toggle for what the two flip buttons do: "Mirror" (purple, the default: reflect the selection
    /// across the PLAYFIELD centre) or "Flip" (orange: flip it in place around its own centre). Shows the current mode
    /// in bold text with a matching coloured outline. Tapping switches the mode.
    /// </summary>
    public partial class MirrorModeButton : ClickableContainer, IHasTooltip
    {
        private static readonly Color4 flip_colour = new Color4(0xFF, 0x80, 0x00, 0xFF); // #FF8000
        private static readonly Color4 mirror_colour = new Color4(0x92, 0x67, 0xBE, 0xFF); // #9267BE

        private readonly BindableBool mirrorMode;
        private Container content = null!;
        private SpriteText text = null!;

        public MirrorModeButton(BindableBool mirrorMode)
        {
            this.mirrorMode = mirrorMode;
            Action = () => this.mirrorMode.Value = !this.mirrorMode.Value;
        }

        public LocalisableString TooltipText => "Mirror (across the playfield centre) / Flip (around the selection's own centre) - what the two arrow buttons do";

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = Size.X / 2,
                BorderThickness = 3,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    text = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = FontUsage.Default.With(size: 17, weight: "Bold"),
                    },
                },
            };

            mirrorMode.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            var colour = mirrorMode.Value ? mirror_colour : flip_colour;
            text.Text = mirrorMode.Value ? "Mirror" : "Flip";
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
