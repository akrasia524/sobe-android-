using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Graphics.Cursor;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A compact square icon button for the editor's top-left bar (e.g. the settings gear), styled to match the
    /// neutral text buttons next to it: a rounded surface that lightens on hover. Shows a tooltip for its label.
    /// </summary>
    public partial class IconBarButton : ClickableContainer, IHasTooltip
    {
        /// <summary>Draws a pink (accent) outline like the play/pause button (phone action buttons).</summary>
        public bool AccentBorder { get; init; }

        /// <summary>Overrides the outline colour used when <see cref="AccentBorder"/> is on.</summary>
        public osuTK.Graphics.Color4? BorderOverride { get; init; }

        /// <summary>Draws the button as a circle instead of a rounded square.</summary>
        public bool Round { get; init; }

        private readonly IconUsage icon;
        private readonly string tooltip;
        private Box background = null!;
        private SpriteIcon iconSprite = null!;
        private Container content = null!;

        /// <summary>Glyph size override (default 13); set together with a larger <see cref="Drawable.Size"/> for touch-sized buttons.</summary>
        public float IconSize { get; init; } = 13;

        public IconBarButton(IconUsage icon, string tooltip, Action onClick)
        {
            this.icon = icon;
            this.tooltip = tooltip;
            Action = onClick;
            Size = new Vector2(24);
        }

        public LocalisableString TooltipText => tooltip;

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            // The content lives in an inner wrapper so the hover/press scale can grow past the button's layout box
            // (the outer container isn't masked) without nudging its neighbours.
            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                BorderThickness = AccentBorder ? 3 : 0,
                BorderColour = BorderOverride ?? EditorTheme.Colours.Accent,
                CornerRadius = Round ? Size.X / 2 : 5,
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = icon,
                        Size = new Vector2(IconSize),
                        Colour = EditorTheme.Colours.TextMuted,
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            iconSprite.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            content.ScaleTo(1.1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            iconSprite.FadeColour(EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            content.ScaleTo(1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.88f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(IsHovered ? 1.1f : 1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }
    }
}
