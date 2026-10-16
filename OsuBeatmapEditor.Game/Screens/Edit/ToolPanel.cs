using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>The editor's composing tools (mirrors osu!lazer's left-hand toolbox).</summary>
    public enum EditorTool
    {
        Selection,
        Circle,
        Slider,
        Spinner,
    }

    /// <summary>
    /// A vertical toolbox on the left of the editor that shows - and lets the user pick - the active
    /// composing tool, highlighting the current one. Spinner is shown for completeness but not yet placeable.
    /// </summary>
    public partial class ToolPanel : CompositeDrawable
    {
        // Phone sizing (design units at the 1024x576 mobile target): big finger-sized rows. Desktop keeps the original compact rows.
        private static float rowWidth => MobileMetrics.IsMobile ? 176 : 124;
        private static float rowHeight => MobileMetrics.IsMobile ? 56 : EditorTheme.Sizing.RowHeight;
        private static float rowGap => MobileMetrics.IsMobile ? 8 : 6;
        private static float iconX => MobileMetrics.IsMobile ? 42 : 32;
        private static float iconSize => MobileMetrics.IsMobile ? 24 : 13;
        private static float labelX => MobileMetrics.IsMobile ? 78 : 54;
        private static float hoverNudge => 4;

        /// <summary>Left margin of the whole panel inside the editor (moves the column right on phones).</summary>
        public static float LeftMargin => MobileMetrics.IsMobile ? 120 : 12;

        /// <summary>Extra downward shift of the panel from vertical centre (phones only).</summary>
        public static float TopOffset => MobileMetrics.IsMobile ? 28 : 0;

        /// <summary>Raised when a tool row is clicked.</summary>
        public Action<EditorTool>? ToolSelected;

        private readonly Dictionary<EditorTool, ToolRow> rows = new Dictionary<EditorTool, ToolRow>();

        private static readonly (EditorTool tool, int key, string label, IconUsage icon)[] entries =
        {
            (EditorTool.Selection, 1, "Select", FontAwesome.Solid.MousePointer),
            (EditorTool.Circle, 2, "Circle", FontAwesome.Regular.Circle),
            (EditorTool.Slider, 3, "Slider", FontAwesome.Solid.BezierCurve),
            (EditorTool.Spinner, 4, "Spinner", FontAwesome.Solid.Asterisk),  // placeable: click to start, scrub forward, click to end
        };

        public ToolPanel()
        {
            AutoSizeAxes = Axes.Both;

            var flow = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, rowGap),
            };

            foreach (var (tool, key, label, icon) in entries)
            {
                var row = new ToolRow(key, label, icon, disabled: false, () => ToolSelected?.Invoke(tool));
                rows[tool] = row;
                flow.Add(row);
            }

            InternalChild = flow;
        }

        /// <summary>Highlights the row for the active tool.</summary>
        public void SetActive(EditorTool tool)
        {
            foreach (var (key, row) in rows)
                row.SetActive(key == tool);
        }

        private partial class ToolRow : ClickableContainer
        {
            private readonly bool disabled;
            private Box background = null!;
            private SpriteText label = null!;
            private SpriteIcon iconSprite = null!;
            private readonly string text;
            private readonly int key;
            private readonly IconUsage icon;
            private bool active;

            public ToolRow(int key, string label, IconUsage icon, bool disabled, Action onClick)
            {
                this.key = key;
                text = label;
                this.icon = icon;
                this.disabled = disabled;
                Action = onClick;

                Size = new Vector2(rowWidth, rowHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
                Alpha = disabled ? 0.55f : 1f;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = EditorTheme.Spacing.Lg,
                        Text = key.ToString(),
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Label(numeric: true),
                    },
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = iconX,
                        Size = new Vector2(iconSize),
                        Icon = icon,
                        Colour = disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.TextMuted,
                    },
                    this.label = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = labelX,
                        Text = text,
                        Colour = disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.Text,
                        Font = MobileMetrics.IsMobile ? EditorTheme.Type.Heading() : EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            public void SetActive(bool active)
            {
                this.active = active;
                // Active tool = the sanctioned solid-accent case (a persistent selected state, dark-on-accent).
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.FadeColour(active ? EditorTheme.Colours.Sunken : (disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.Text), EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                iconSprite.FadeColour(active ? EditorTheme.Colours.Sunken : (disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.TextMuted), EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                // The little numeral keeps a touch more contrast when active, but isn't part of the hover/press feedback.
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!disabled && !active)
                {
                    background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    label.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    iconSprite.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }

                // The icon + label slide in a touch on hover (active or not) for a bit of life; the row is masked,
                // so we nudge the contents rather than scaling the whole row.
                if (!disabled)
                {
                    iconSprite.MoveToX(iconX + hoverNudge, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    label.MoveToX(labelX + hoverNudge, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                }

                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                {
                    background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    label.FadeColour(disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    iconSprite.FadeColour(disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }

                iconSprite.MoveToX(iconX, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.MoveToX(labelX, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (!disabled)
                    iconSprite.ScaleTo(0.8f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                iconSprite.ScaleTo(1f, EditorTheme.Motion.Normal, Easing.OutBack);
                base.OnMouseUp(e);
            }
        }
    }
}
