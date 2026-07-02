using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Annotations;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The Review "changes" list: every proposed object move, each with Accept / Reject and click-to-jump, plus
    /// Accept-all / Reject-all. This is where suggested edits are reviewed (like a diff/PR), instead of buttons
    /// floating over the playfield. Fed by the editor whenever the Review layer changes.
    /// </summary>
    public partial class ReviewChangesPanel : CompositeDrawable
    {
        public const float WIDTH = 150;

        public Action<Annotation>? OnAccept;
        public Action<Annotation>? OnReject;
        public Action<Annotation>? OnJump;
        public Action? OnAcceptAll;
        public Action? OnRejectAll;

        private SpriteText header = null!;
        private FillFlowContainer list = null!;
        private Container allRow = null!;
        private SpriteText emptyLabel = null!;

        public ReviewChangesPanel()
        {
            AutoSizeAxes = Axes.Y;
            Width = WIDTH;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(EditorTheme.Spacing.Sm),
                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                        Children = new Drawable[]
                        {
                            header = new SpriteText
                            {
                                Text = "CHANGES",
                                Colour = EditorTheme.Colours.Accent,
                                Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                            },
                            emptyLabel = new SpriteText
                            {
                                Text = "Drag objects to propose moves.",
                                Colour = EditorTheme.Colours.TextMuted,
                                Font = EditorTheme.Type.Caption(),
                            },
                            allRow = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Alpha = 0,
                                Child = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Full,
                                    Spacing = new Vector2(EditorTheme.Spacing.Xs, EditorTheme.Spacing.Xs),
                                    Children = new Drawable[]
                                    {
                                        new PillButton("Accept all", EditorTheme.Colours.Success, () => OnAcceptAll?.Invoke()),
                                        new PillButton("Reject all", EditorTheme.Colours.Control, () => OnRejectAll?.Invoke()),
                                    },
                                },
                            },
                            list = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 3),
                            },
                        },
                    },
                },
            };
        }

        /// <summary>Rebuilds the list from the current move suggestions (annotation + display label per change).</summary>
        public void SetChanges(IReadOnlyList<(Annotation annotation, string label)> changes)
        {
            header.Text = $"CHANGES ({changes.Count})";
            emptyLabel.Alpha = changes.Count == 0 ? 1 : 0;
            allRow.Alpha = changes.Count > 1 ? 1 : 0;

            list.Clear();
            foreach (var (a, label) in changes)
                list.Add(new ChangeRow(a, label, OnAccept, OnReject, OnJump));
        }

        /// <summary>One change row: colour dot + label (click to jump), plus Accept / Reject icon buttons.</summary>
        private partial class ChangeRow : Container
        {
            private readonly Annotation annotation;
            private readonly Action<Annotation>? onJump;
            private Box background = null!;

            public ChangeRow(Annotation annotation, string label, Action<Annotation>? onAccept, Action<Annotation>? onReject, Action<Annotation>? onJump)
            {
                this.annotation = annotation;
                this.onJump = onJump;

                RelativeSizeAxes = Axes.X;
                Height = 22;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;

                Color4 colour = parseColour(annotation.Color);

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 5 },
                        Size = new Vector2(7),
                        Colour = colour,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 17 },
                        Text = label,
                        Colour = EditorTheme.Colours.Text,
                        Font = FontUsage.Default.With(size: 11),
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = 4 },
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(2, 0),
                        Children = new Drawable[]
                        {
                            new IconAction(FontAwesome.Solid.Check, EditorTheme.Colours.Success, () => onAccept?.Invoke(annotation)),
                            new IconAction(FontAwesome.Solid.Times, EditorTheme.Colours.Error, () => onReject?.Invoke(annotation)),
                        },
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                onJump?.Invoke(annotation);
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
                => background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);

            private static Color4 parseColour(string hex)
            {
                try { var c = Colour4.FromHex(hex); return new Color4(c.R, c.G, c.B, 1f); }
                catch { return Color4.White; }
            }
        }

        /// <summary>A small square icon button (Accept check / Reject cross) inside a change row.</summary>
        private partial class IconAction : CompositeDrawable
        {
            private readonly Action action;

            public IconAction(IconUsage icon, Color4 colour, Action action)
            {
                this.action = action;
                Size = new Vector2(18);
                InternalChild = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(11),
                    Colour = colour,
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true; // don't let the row's jump fire too
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.2f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.ScaleTo(1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        /// <summary>A small text pill button (Accept all / Reject all).</summary>
        private partial class PillButton : CompositeDrawable
        {
            private readonly Action action;

            public PillButton(string label, Color4 colour, Action action)
            {
                this.action = action;
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 8, Vertical = 3 },
                        Text = label,
                        Colour = Color4.White,
                        Font = FontUsage.Default.With(size: 10, weight: "Bold"),
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.03f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.ScaleTo(1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }
    }
}
