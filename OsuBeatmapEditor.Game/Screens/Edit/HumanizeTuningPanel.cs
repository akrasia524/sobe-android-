using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A live tuning panel for the "Humanize" Auto cursor: a scrollable list of sliders, each bound straight to a
    /// <see cref="HumanizeTuning"/> field, so dragging one changes the AU cursor's motion in real time (the cursor
    /// reads the fields every frame). Opened from the AU chip's mini-menu. "Reset" restores the calibrated defaults.
    /// </summary>
    public partial class HumanizeTuningPanel : VisibilityContainer
    {
        private readonly Action onClose;
        private readonly Action onSave;
        private readonly List<(BindableFloat bindable, Func<float> get)> rows = new();
        private SpriteText savedFlash = null!;
        private BasicScrollContainer scroll = null!;

        private const float full_height = 580;
        private const float header_height = 40 + 1; // header + divider
        private const float bottom_screen_gap = 12;

        public HumanizeTuningPanel(Action onClose, Action onSave)
        {
            this.onClose = onClose;
            this.onSave = onSave;
            Width = 312;
            Height = full_height;
        }

        protected override void Update()
        {
            base.Update();

            // The panel used to be a fixed 580 tall; on a 576-tall phone screen (positioned below the top bar) its lower part ran
            // off-screen, so the scroll list's end was unreachable and it just sprang back. Fit it to the room that is actually there.
            if (Parent != null)
            {
                float available = Parent.DrawHeight - Margin.Top - bottom_screen_gap;
                float h = System.Math.Clamp(available, 200, full_height);
                if (System.Math.Abs(Height - h) > 0.5f)
                {
                    Height = h;
                    scroll.Height = h - header_height;
                }
            }
        }

        protected override void PopIn() => this.FadeIn(120, Easing.OutQuad);
        protected override void PopOut() => this.FadeOut(120, Easing.OutQuad);

        [BackgroundDependencyLoader]
        private void load()
        {
            var list = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                Padding = new MarginPadding(EditorTheme.Spacing.Md),
            };

            group(list, "FLOW (stream vs jump)");
            row(list, "Flow fast gap (ms)", 40, 200, () => HumanizeTuning.FlowFastMs, v => HumanizeTuning.FlowFastMs = v);
            row(list, "Flow slow gap (ms)", 60, 340, () => HumanizeTuning.FlowSlowMs, v => HumanizeTuning.FlowSlowMs = v);
            row(list, "Straighten zig-zag", 0, 1, () => HumanizeTuning.StreamStraightenAmount, v => HumanizeTuning.StreamStraightenAmount = v);
            row(list, "Straighten threshold (r)", 0, 2, () => HumanizeTuning.StreamStraightenThreshold, v => HumanizeTuning.StreamStraightenThreshold = v);
            row(list, "Straighten window (notes)", 1, 8, () => HumanizeTuning.StreamStraightenTaps, v => HumanizeTuning.StreamStraightenTaps = v);

            group(list, "REACTION (dwell after a pause)");
            row(list, "React short gap (ms)", 100, 500, () => HumanizeTuning.ReactionShortMs, v => HumanizeTuning.ReactionShortMs = v);
            row(list, "React long gap (ms)", 300, 1000, () => HumanizeTuning.ReactionLongMs, v => HumanizeTuning.ReactionLongMs = v);
            row(list, "Move time (ms)", 80, 500, () => HumanizeTuning.ReactionMoveMs, v => HumanizeTuning.ReactionMoveMs = v);

            group(list, "ARC (bow on jumps)");
            row(list, "Arc amount", 0, 0.6f, () => HumanizeTuning.ArcAmount, v => HumanizeTuning.ArcAmount = v);
            row(list, "Arc falloff (norm)", 30, 300, () => HumanizeTuning.ArcFalloffNorm, v => HumanizeTuning.ArcFalloffNorm = v);
            row(list, "Hand pivot X", -300, 300, () => HumanizeTuning.HandPivotX, v => HumanizeTuning.HandPivotX = v);
            row(list, "Hand pivot Y (below)", -200, 900, () => HumanizeTuning.HandPivotY, v => HumanizeTuning.HandPivotY = v);

            group(list, "AIM / TREMOR");
            row(list, "Aim error", 0, 1.5f, () => HumanizeTuning.AimErrorAmount, v => HumanizeTuning.AimErrorAmount = v);
            row(list, "Tremor amount", 0, 4, () => HumanizeTuning.TremorAmount, v => HumanizeTuning.TremorAmount = v);

            group(list, "OVERSHOOT");
            row(list, "Overshoot gain", 0, 0.4f, () => HumanizeTuning.OvershootGain, v => HumanizeTuning.OvershootGain = v);
            row(list, "Overshoot peak (norm)", 60, 400, () => HumanizeTuning.OvershootPeakNorm, v => HumanizeTuning.OvershootPeakNorm = v);

            group(list, "SLIDERS (lazy follow)");
            row(list, "Cut window", 0, 0.6f, () => HumanizeTuning.SliderCutWindow, v => HumanizeTuning.SliderCutWindow = v);
            row(list, "Laziness", 0, 1, () => HumanizeTuning.SliderLaziness, v => HumanizeTuning.SliderLaziness = v);
            row(list, "Edge fade (ms)", 0, 150, () => HumanizeTuning.SliderEdgeFadeMs, v => HumanizeTuning.SliderEdgeFadeMs = v);
            row(list, "Follow wander (r)", 0, 1, () => HumanizeTuning.SliderFollowWander, v => HumanizeTuning.SliderFollowWander = v);
            row(list, "Wander speed (Hz)", 0.2f, 4, () => HumanizeTuning.SliderWanderHz, v => HumanizeTuning.SliderWanderHz = v);
            row(list, "Kick slider max (ms)", 0, 200, () => HumanizeTuning.KickSliderMaxMs, v => HumanizeTuning.KickSliderMaxMs = v);
            row(list, "Flow release (momentum)", 0, 1, () => HumanizeTuning.SliderFlowRelease, v => HumanizeTuning.SliderFlowRelease = v);
            row(list, "Reverse laziness", 0, 1, () => HumanizeTuning.ReverseLaziness, v => HumanizeTuning.ReverseLaziness = v);
            row(list, "Release frac", 0, 0.3f, () => HumanizeTuning.SliderReleaseFrac, v => HumanizeTuning.SliderReleaseFrac = v);
            row(list, "Release max (ms)", 0, 100, () => HumanizeTuning.SliderReleaseMaxMs, v => HumanizeTuning.SliderReleaseMaxMs = v);
            row(list, "Anticipation (to next)", 0, 1, () => HumanizeTuning.SliderAnticipation, v => HumanizeTuning.SliderAnticipation = v);
            row(list, "Anticipation window (ms)", 0, 250, () => HumanizeTuning.SliderAnticipationMs, v => HumanizeTuning.SliderAnticipationMs = v);
            row(list, "Anticip. align off (dot)", -1, 1, () => HumanizeTuning.AnticipationAlignLo, v => HumanizeTuning.AnticipationAlignLo = v);
            row(list, "Anticip. align full (dot)", -1, 1, () => HumanizeTuning.AnticipationAlignHi, v => HumanizeTuning.AnticipationAlignHi = v);
            row(list, "Follow leash (r)", 0.5f, 2.4f, () => HumanizeTuning.FollowCircleSafety, v => HumanizeTuning.FollowCircleSafety = v);

            // Breathing room after the last slider so it isn't cramped against the panel's bottom edge.
            list.Add(new Container { RelativeSizeAxes = Axes.X, Height = 56 });

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.4f),
                    Radius = 12,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            header(),
                            new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorTheme.Colours.Sunken },
                            scroll = new BasicScrollContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = full_height - header_height, // panel height minus the header + divider; kept in sync in Update()
                                ScrollbarVisible = true,
                                Child = list,
                            },
                        },
                    },
                },
            };

            refresh();
        }

        private Drawable header() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Md },
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "HUMANIZE TUNING",
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Caption(),
                        },
                        savedFlash = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "saved",
                            Alpha = 0,
                            Colour = EditorTheme.Colours.Velocity,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new TextButton("Save", () => { onSave(); savedFlash.FadeIn(60).Then().FadeOut(900, Easing.InQuad); }),
                        new TextButton("Reset", () => { HumanizeTuning.ResetToDefaults(); refresh(); }),
                        new TextButton("X", onClose),
                    },
                },
            },
        };

        private static void group(FillFlowContainer list, string title) => list.Add(new SpriteText
        {
            Text = title,
            Colour = EditorTheme.Colours.Accent,
            Font = EditorTheme.Type.Caption(),
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm, Bottom = 2 },
        });

        private void row(FillFlowContainer list, string label, float min, float max, Func<float> get, Action<float> set)
        {
            var bindable = new BindableFloat(get())
            {
                MinValue = min,
                MaxValue = max,
                Precision = (max - min) / 250f,
            };

            var value = new SpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(numeric: true),
            };

            bindable.BindValueChanged(v =>
            {
                set(v.NewValue);
                value.Text = v.NewValue.ToString(max <= 3f ? "0.00" : "0");
            }, true);

            rows.Add((bindable, get));

            list.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Margin = new MarginPadding { Bottom = 3 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 14,
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = label,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Label(),
                            },
                            value,
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 9,
                        Child = new BasicSliderBar<float>
                        {
                            RelativeSizeAxes = Axes.Both,
                            Current = bindable,
                            BackgroundColour = EditorTheme.Colours.Sunken,
                            SelectionColour = EditorTheme.Colours.Selection,
                        },
                    },
                },
            });
        }

        /// <summary>Pulls every slider back in line with the current field values (after a Reset).</summary>
        private void refresh()
        {
            foreach (var (bindable, get) in rows)
                bindable.Value = get();
        }

        // Eat clicks/scrolls so interacting with the panel never falls through to the editor behind it.
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnScroll(ScrollEvent e) => true;
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        /// <summary>A small text button (Reset / close).</summary>
        private partial class TextButton : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Box bg;

            public TextButton(string text, Action onClick)
            {
                this.onClick = onClick;
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
                InternalChildren = new Drawable[]
                {
                    bg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                    new SpriteText
                    {
                        Text = text,
                        Margin = new MarginPadding { Horizontal = 8, Vertical = 4 },
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e) { bg.FadeColour(EditorTheme.Colours.Selection, 80); return true; }
            protected override void OnHoverLost(HoverLostEvent e) => bg.FadeColour(EditorTheme.Colours.Sunken, 80);

            protected override bool OnClick(ClickEvent e) { onClick(); return true; }
        }
    }
}
