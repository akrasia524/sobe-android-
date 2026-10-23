using System;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Phone polygon generator panel - the same four settings as osu!lazer's Generate > Polygon popover (distance snap,
    /// offset angle, repeats, vertices). The ring of circles is shown LIVE while the panel is open; Create keeps it
    /// as one undo step, Cancel (or dismissing) removes it again.
    /// </summary>
    public partial class PolygonOverlay : VisibilityContainer
    {
        /// <summary>Live preview: distance snap, offset angle (deg), repeats, vertices. Returns false if the polygon doesn't fit the playfield.</summary>
        public Func<double, int, int, int, bool>? Preview;

        /// <summary>Create pressed (and the polygon fits).</summary>
        public Action? Confirmed;

        /// <summary>Closed without creating: remove the preview.</summary>
        public Action? Cancelled;

        private bool committing;
        private bool fits = true;

        private readonly BindableDouble distance = new BindableDouble(1) { MinValue = 0.1, MaxValue = 6, Precision = 0.1 };
        private readonly BindableInt offset = new BindableInt(0) { MinValue = 0, MaxValue = 180, Precision = 1 };
        private readonly BindableInt repeats = new BindableInt(1) { MinValue = 1, MaxValue = 10, Precision = 1 };
        private readonly BindableInt vertices = new BindableInt(3) { MinValue = 3, MaxValue = 32, Precision = 1 };

        private Container panel = null!;
        private SpriteText warning = null!;
        private OsuButton createButton = null!;

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
                                        Text = "Polygon",
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
                                    row("Distance snap", v => $"{v:0.0}x", distance, new KnobSliderBar<double> { RelativeSizeAxes = Axes.Both, Current = distance }),
                                    row("Offset angle", v => $"{v}°", offset, new KnobSliderBar<int> { RelativeSizeAxes = Axes.Both, Current = offset }),
                                    row("Repeats", v => $"{v}", repeats, new KnobSliderBar<int> { RelativeSizeAxes = Axes.Both, Current = repeats }),
                                    row("Vertices", v => $"{v}", vertices, new KnobSliderBar<int> { RelativeSizeAxes = Axes.Both, Current = vertices }),
                                    warning = new SpriteText
                                    {
                                        Text = "Doesn't fit the playfield - lower the distance snap.",
                                        Colour = EditorTheme.Colours.Error,
                                        Font = EditorTheme.Type.Caption(),
                                        Alpha = 0,
                                    },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Horizontal,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                        Children = new Drawable[]
                                        {
                                            new OsuButton("Cancel", OsuColour.Surface) { Size = new Vector2(96, 36), Action = () => Hide() },
                                            createButton = new OsuButton("Create", OsuColour.Pink) { Size = new Vector2(108, 36), Action = create },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        private static Drawable row<T>(string label, Func<T, string> format, Bindable<T> bindable, Drawable bar) where T : struct, IComparable<T>
        {
            SpriteText value;
            var container = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 22,
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
                                    value = new SpriteText
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Colour = EditorTheme.Colours.Text,
                                        Font = EditorTheme.Type.Body(),
                                    },
                                },
                            },
                            new Container { RelativeSizeAxes = Axes.X, Height = 30, Child = bar },
                        },
                    },
                },
            };
            bindable.BindValueChanged(v => value.Text = format(v.NewValue), true);
            return container;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            distance.BindValueChanged(_ => update());
            offset.BindValueChanged(_ => update());
            repeats.BindValueChanged(_ => update());
            vertices.BindValueChanged(_ => update());
        }

        private void update()
        {
            if (State.Value != Visibility.Visible)
                return;

            fits = Preview?.Invoke(distance.Value, offset.Value, repeats.Value, vertices.Value) ?? true;
            warning.Alpha = fits ? 0 : 1;
            createButton.Alpha = fits ? 1 : 0.4f;
        }

        /// <summary>Opens the panel (lazer defaults: offset 0, 1 repeat, 3 vertices) seeded with the editor's distance spacing.</summary>
        public void ShowFresh(double currentDistanceSpacing)
        {
            committing = false;
            fits = true;
            distance.Value = Math.Clamp(Math.Round(currentDistanceSpacing, 1), distance.MinValue, distance.MaxValue);
            offset.Value = 0;
            repeats.Value = 1;
            vertices.Value = 3;
            Show();
            update();
        }

        private void create()
        {
            if (!fits)
                return;

            committing = true;
            Confirmed?.Invoke();
            Hide();
        }

        protected override void PopIn() => this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

            if (!committing)
                Cancelled?.Invoke();
            committing = false;
        }

        private partial class DraggablePanel : Container
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
            protected override bool OnScroll(ScrollEvent e) => true;
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
