using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A compact text-label button in the same style as <see cref="IconBarButton"/> (rounded neutral surface,
    /// lightens on hover, shrinks on press). Used for editor actions that have no good icon (+BPM, +SV, Stream).
    /// </summary>
    public partial class LabelBarButton : ClickableContainer, IHasTooltip
    {
        private readonly string label;
        private readonly string tooltip;
        private Box background = null!;
        private SpriteText text = null!;
        private Container content = null!;

        public LabelBarButton(string label, string tooltip, Action onClick, Vector2 size)
        {
            this.label = label;
            this.tooltip = tooltip;
            Action = onClick;
            Size = size;
        }

        public LocalisableString TooltipText => tooltip;

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
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    text = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = label,
                        Font = EditorTheme.Type.BodyStrong(),
                        Colour = EditorTheme.Colours.TextMuted,
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            text.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            text.FadeColour(EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
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
