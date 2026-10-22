using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Small draggable panel (same style as the slider-to-stream one) for setting how many REVERSES a slider has: type any
    /// number (0 = a plain one-way slider, 1 = out and back, 2 = out, back and out again...). The slider keeps its length and
    /// speed, so each extra reverse makes it last one more span. Applied with the Apply button.
    /// </summary>
    public partial class SliderReverseOverlay : VisibilityContainer
    {
        /// <summary>Fired when the mapper applies the new reverse count.</summary>
        public Action<int>? Confirmed;

        /// <summary>Fired continuously while the panel is open as the count changes, so the slider can be shown live.</summary>
        public Action<int>? Preview;

        /// <summary>Fired when the panel closes WITHOUT applying (Cancel / dismissed), so the live preview is undone.</summary>
        public Action? Cancelled;

        private bool committing;

        // Typed freely (digits only, up to 4 of them).
        private readonly BindableInt reverses = new BindableInt(1) { MinValue = 0, MaxValue = 9999 };

        // The drag bar spans a comfortable 0-16; the number box can go far past it (typed), like the stream panel.
        private readonly BindableInt reversesBar = new BindableInt(1) { MinValue = 0, MaxValue = 16 };
        private bool syncing;

        private Container panel = null!;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            // Spans the screen so the panel can be dragged anywhere, but captures no input itself.
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
                                        Text = "Reverse slider",
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
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Text = "Reverses",
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Colour = EditorTheme.Colours.TextMuted,
                                                Font = EditorTheme.Type.Body(),
                                            },
                                            new ReverseCountBox(reverses)
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
                                        Child = new KnobSliderBar<int>
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Current = reversesBar,
                                        },
                                    },
                                    new SpriteText
                                    {
                                        Text = "0 = one way, 1 = out and back, 2 = out, back, out...",
                                        Colour = EditorTheme.Colours.TextMuted,
                                        Font = EditorTheme.Type.Caption(),
                                    },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Horizontal,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Md },
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

            // Two-way sync between the true count and the bar proxy; typing beyond the bar's range just works.
            reverses.BindValueChanged(r =>
            {
                if (State.Value == Visibility.Visible)
                    Preview?.Invoke(r.NewValue);

                if (syncing)
                    return;
                syncing = true;
                reversesBar.Value = Math.Clamp(r.NewValue, reversesBar.MinValue, reversesBar.MaxValue);
                syncing = false;
            }, true);
            reversesBar.BindValueChanged(r =>
            {
                if (syncing)
                    return;
                syncing = true;
                reverses.Value = r.NewValue;
                syncing = false;
            });
        }

        /// <summary>Opens the panel seeded with the slider's current reverse count.</summary>
        public void Show(int currentReverses)
        {
            committing = false;
            reverses.Value = Math.Clamp(currentReverses, reverses.MinValue, reverses.MaxValue);
            Show();
        }

        private void apply()
        {
            committing = true;
            Confirmed?.Invoke(reverses.Value);
            Hide();
        }

        protected override void PopIn() => this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

            // Closed without Apply -> the editor drops the live preview and restores the slider.
            if (!committing)
                Cancelled?.Invoke();
            committing = false;
        }

        /// <summary>A floating panel that blocks input within its own bounds (so taps don't leak to the playfield).</summary>
        private partial class DraggablePanel : Container
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
            protected override bool OnScroll(ScrollEvent e) => true;
        }

        /// <summary>Digits-only number box bound to the reverse count; commits on Enter / focus loss.</summary>
        private partial class ReverseCountBox : BasicTextBox
        {
            private readonly BindableInt bindable;

            public ReverseCountBox(BindableInt bindable)
            {
                this.bindable = bindable;
                CommitOnFocusLost = true;
                LengthLimit = 4;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                updateText();
                bindable.BindValueChanged(_ => updateText());
            }

            protected override bool CanAddCharacter(char character) => char.IsDigit(character);

            private void updateText() => Text = bindable.Value.ToString();

            protected override void Commit()
            {
                if (int.TryParse(Text, out int value))
                    bindable.Value = Math.Clamp(value, bindable.MinValue, bindable.MaxValue);

                updateText();
                base.Commit();
            }
        }

        /// <summary>A header strip that drags its owning panel around by reporting pointer deltas.</summary>
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
