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
    /// Phone scale panel, modelled on osu!lazer's scale popover: a factor (drag bar or typed), an axis (both / X / Y) and an
    /// origin (selection centre / playfield centre). The selection is scaled LIVE while the panel is open; Apply keeps it as one undo step, Cancel (or dismissing) puts everything back.
    /// </summary>
    public partial class SelectionScaleOverlay : VisibilityContainer
    {
        /// <summary>Live preview: scale factor, axis (0 both, 1 X, 2 Y) and whether to pivot on the playfield centre.</summary>
        public Action<float, int, bool>? Preview;

        /// <summary>Keep the previewed scale.</summary>
        public Action<float, int, bool>? Confirmed;

        /// <summary>Closed without applying: undo the preview.</summary>
        public Action? Cancelled;

        private bool committing;
        private bool syncing;

        private readonly BindableFloat angle = new BindableFloat(1) { MinValue = 0.1f, MaxValue = 5, Precision = 0.01f };
        private readonly BindableFloat angleBar = new BindableFloat(1) { MinValue = 0.25f, MaxValue = 2, Precision = 0.01f };
        private bool aroundPlayfield;
        private int axis;

        private Container panel = null!;

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
                                        Text = "Scale selection",
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
                                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
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
                                                Text = "Scale factor",
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Colour = EditorTheme.Colours.TextMuted,
                                                Font = EditorTheme.Type.Body(),
                                            },
                                            new AngleBox(angle)
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
                                        Child = new KnobSliderBar<float> { RelativeSizeAxes = Axes.Both, Current = angleBar },
                                    },
                                    new SpriteText
                                    {
                                        Text = "Stretch:",
                                        Colour = EditorTheme.Colours.TextMuted,
                                        Font = EditorTheme.Type.Caption(),
                                    },
                                    new RotationPopover.SegmentedToggle("Both", "X", "Y")
                                    {
                                        Changed = i =>
                                        {
                                            axis = i;
                                            if (State.Value == Visibility.Visible)
                                                Preview?.Invoke(angle.Value, axis, aroundPlayfield);
                                        },
                                    },
                                    new SpriteText
                                    {
                                        Text = "Scale around:",
                                        Colour = EditorTheme.Colours.TextMuted,
                                        Font = EditorTheme.Type.Caption(),
                                    },
                                    new RotationPopover.SegmentedToggle("Selection", "Playfield")
                                    {
                                        Changed = i =>
                                        {
                                            aroundPlayfield = i == 1;
                                            if (State.Value == Visibility.Visible)
                                                Preview?.Invoke(angle.Value, axis, aroundPlayfield);
                                        },
                                    },
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
                                            new OsuButton("Cancel", OsuColour.Surface) { Size = new Vector2(96, 36), Action = () => Hide() },
                                            new OsuButton("Apply", OsuColour.Pink) { Size = new Vector2(108, 36), Action = apply },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            angle.BindValueChanged(a =>
            {
                if (State.Value == Visibility.Visible)
                    Preview?.Invoke(a.NewValue, axis, aroundPlayfield);

                if (syncing)
                    return;
                syncing = true;
                angleBar.Value = Math.Clamp(a.NewValue, angleBar.MinValue, angleBar.MaxValue);
                syncing = false;
            }, true);
            angleBar.BindValueChanged(a =>
            {
                if (syncing)
                    return;
                syncing = true;
                angle.Value = a.NewValue;
                syncing = false;
            });
        }

        /// <summary>Opens the panel at 1.00x on both axes, pivoting on the selection centre.</summary>
        public void ShowFresh()
        {
            committing = false;
            aroundPlayfield = false;
            axis = 0;
            angle.Value = 1;
            Show();
        }

        private void apply()
        {
            committing = true;
            Confirmed?.Invoke(angle.Value, axis, aroundPlayfield);
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

        /// <summary>Decimal scale box bound to the angle; commits on Enter / focus loss.</summary>
        private partial class AngleBox : BasicTextBox
        {
            private readonly BindableFloat bindable;

            public AngleBox(BindableFloat bindable)
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

            protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '.';

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
