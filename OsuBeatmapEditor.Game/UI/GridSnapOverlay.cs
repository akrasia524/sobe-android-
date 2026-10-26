using System;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Screens.Edit;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Phone grid-snap panel, modelled on osu!lazer's grid tool: spacing (4-128, with the tiny/small/medium/large presets),
    /// rotation (-45..45 degrees) and the grid origin X / Y. Everything is live (the grid redraws and snapping follows at
    /// once); there is nothing to apply or undo because it is only a view/snapping aid.
    /// </summary>
    public partial class GridSnapOverlay : VisibilityContainer
    {
        private readonly GridSettings grid;
        private Container panel = null!;

        public GridSnapOverlay(GridSettings grid)
        {
            this.grid = grid;
        }

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = panel = new DraggablePanel
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-EditorTheme.Spacing.Xxl, 96),
                Width = 300,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = new osuTK.Graphics.Color4(0f, 0f, 0f, 0.5f),
                    Radius = 12f,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new DragHandle(d => panel.Position += d)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 34,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                                    new SpriteText
                                    {
                                        Text = "Grid snap",
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Lg },
                                        Colour = EditorTheme.Colours.Accent,
                                        Font = EditorTheme.Type.Body(),
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding(EditorTheme.Spacing.Lg),
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                                Children = new Drawable[]
                                {
                                    row("Spacing", grid.Spacing),
                                    presetRow(),
                                    row("Rotation (deg)", grid.Rotation),
                                    row("Origin X", grid.OriginX),
                                    row("Origin Y", grid.OriginY),
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Horizontal,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                        Children = new Drawable[]
                                        {
                                            new OsuButton("Reset", OsuColour.Surface) { Size = new Vector2(96, 36), Action = grid.Reset },
                                            new OsuButton("Close", OsuColour.Pink) { Size = new Vector2(108, 36), Action = () => Hide() },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>A labelled number box plus a drag bar bound to one grid setting.</summary>
        private Drawable row(string label, BindableFloat value) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = label,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Body(),
                        },
                        new NumberBox(value)
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Size = new Vector2(84, 28),
                        },
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Child = new KnobSliderBar<float> { RelativeSizeAxes = Axes.Both, Current = value },
                },
            },
        };

        /// <summary>The tiny / small / medium / large spacing shortcuts (lazer's Ctrl+1..4).</summary>
        private Drawable presetRow()
        {
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                Margin = new MarginPadding { Bottom = EditorTheme.Spacing.Xs },
            };

            foreach (float preset in GridSettings.Presets)
            {
                float value = preset;
                flow.Add(new OsuButton(value.ToString("0", CultureInfo.InvariantCulture), OsuColour.Surface)
                {
                    Size = new Vector2(60, 32),
                    Action = () =>
                    {
                        grid.Spacing.Value = value;
                        grid.Visible.Value = true;
                    },
                });
            }

            return flow;
        }

        protected override void PopIn() => this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

        protected override void PopOut() => this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

        private partial class DraggablePanel : Container
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
            protected override bool OnScroll(ScrollEvent e) => true;
        }

        /// <summary>Whole-number box bound to a grid setting; accepts a leading minus; commits on Enter / focus loss.</summary>
        private partial class NumberBox : BasicTextBox
        {
            private readonly BindableFloat bindable;

            public NumberBox(BindableFloat bindable)
            {
                this.bindable = bindable;
                CommitOnFocusLost = true;
                LengthLimit = 6;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                updateText();
                bindable.BindValueChanged(_ => updateText());
            }

            protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '.' || character == '-';

            private void updateText() => Text = bindable.Value.ToString("0.##", CultureInfo.InvariantCulture);

            protected override void Commit()
            {
                if (float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    bindable.Value = Math.Clamp(v, bindable.MinValue, bindable.MaxValue);

                updateText();
                base.Commit();
            }
        }

        private partial class DragHandle : Container
        {
            private readonly Action<Vector2> onDrag;

            public DragHandle(Action<Vector2> onDrag)
            {
                this.onDrag = onDrag;
            }

            protected override bool OnDragStart(DragStartEvent e) => true;
            protected override void OnDrag(DragEvent e) => onDrag(e.Delta);
        }
    }
}
