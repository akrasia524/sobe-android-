using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Edits the beatmap's own combo colours (its <c>[Colours]</c>), shown in Song Setup. These are the
    /// map's colours - distinct from the editor's palette in Settings - and are saved to the .osu. Add/remove
    /// rows; each swatch opens the HSV picker. Rebuilds itself when the colour list changes.
    /// </summary>
    public partial class MapColoursEditor : CompositeDrawable
    {
        [Resolved]
        private EditableBeatmap beatmap { get; set; } = null!;

        private FillFlowContainer flow = null!;

        // Sensible starting colours for "add" (osu! stable defaults), cycled by current count.
        private static readonly Colour4[] add_defaults =
        {
            new Colour4(255, 192, 0, 255),
            new Colour4(0, 202, 0, 255),
            new Colour4(18, 124, 255, 255),
            new Colour4(242, 24, 57, 255),
        };

        public MapColoursEditor()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
            };

            beatmap.MapColours.BindCollectionChanged((_, _) => rebuild(), true);
            // A background can be dragged in while Song Setup is open, so react to it appearing/disappearing.
            beatmap.HasBackground.BindValueChanged(_ => rebuild());
        }

        /// <summary>Extracts <paramref name="count"/> combo colours from the background image and applies them as the map palette.</summary>
        private void applyAutoPalette(int count)
        {
            var palette = beatmap.ExtractBackgroundColours?.Invoke(count);
            if (palette is { Count: > 0 })
                beatmap.SetMapColours(palette);
        }

        private void rebuild()
        {
            flow.Clear();

            // "Auto colours from background": a count picker (2-6) that fills the palette from the background image.
            if (beatmap.HasBackground.Value && beatmap.ExtractBackgroundColours != null)
                flow.Add(new AutoPaletteRow(applyAutoPalette));

            if (beatmap.MapColours.Count == 0)
            {
                flow.Add(new SpriteText
                {
                    Text = "No custom colours - the default skin colours are used.",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                });
            }

            for (int i = 0; i < beatmap.MapColours.Count; i++)
            {
                var colour = beatmap.MapColours[i];
                flow.Add(new ColourRow(i + 1, colour, () => beatmap.RemoveMapColour(colour)));
            }

            flow.Add(new AddButton(() =>
                beatmap.AddMapColour(add_defaults[beatmap.MapColours.Count % add_defaults.Length])));
        }

        /// <summary>"Auto colours from background" row: a label plus 2-6 count buttons that fill the palette from the image.</summary>
        private partial class AutoPaletteRow : FillFlowContainer
        {
            private readonly System.Action<int> apply;

            public AutoPaletteRow(System.Action<int> apply)
            {
                this.apply = apply;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var buttons = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                };

                for (int n = 2; n <= 6; n++)
                {
                    int count = n;
                    buttons.Add(new CountButton(count, () => apply(count)));
                }

                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Auto colours from background",
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Body(),
                    },
                    buttons,
                };
            }
        }

        /// <summary>A small square number button (2-6) that requests that many auto-extracted colours.</summary>
        private partial class CountButton : ClickableContainer
        {
            private readonly int count;
            private Box background = null!;

            public CountButton(int count, System.Action action)
            {
                this.count = count;
                Action = action;
                Size = new Vector2(EditorTheme.Sizing.InputHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = count.ToString(),
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Accent, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }
        }

        /// <summary>One combo-colour row: index label, swatch (opens picker) and a remove button.</summary>
        private partial class ColourRow : Container
        {
            public ColourRow(int number, Bindable<Colour4> colour, System.Action remove)
            {
                RelativeSizeAxes = Axes.X;
                Height = EditorTheme.Sizing.InputHeight;

                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = $"Combo {number}",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Body(),
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
                            new ColourSwatch(colour) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            new RemoveButton(remove) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        },
                    },
                };
            }
        }

        /// <summary>Small square "x" button that removes its colour row.</summary>
        private partial class RemoveButton : ClickableContainer
        {
            private Box background = null!;

            public RemoveButton(System.Action action)
            {
                Action = action;
                Size = new Vector2(EditorTheme.Sizing.InputHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "x",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Error, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }
        }

        /// <summary>"+ Add colour" button row.</summary>
        private partial class AddButton : ClickableContainer
        {
            private Box background = null!;

            public AddButton(System.Action action)
            {
                Action = action;
                RelativeSizeAxes = Axes.X;
                Height = EditorTheme.Sizing.InputHeight;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "+ Add colour",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }
        }
    }
}
