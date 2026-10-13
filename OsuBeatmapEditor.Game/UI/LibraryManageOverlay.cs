using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Library;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The phone's library manager: export or trash the selected set, and restore or permanently delete sets in the
    /// trash. All the real work is in <see cref="LibraryActions"/>; this only draws it. Permanent deletion always
    /// goes through a confirmation.
    /// </summary>
    public partial class LibraryManageOverlay : VisibilityContainer
    {
        private Container panel = null!;
        private SpriteText selectedText = null!;
        private SpriteText trashHeading = null!;
        private FillFlowContainer trashList = null!;

        /// <summary>The library to manage.</summary>
        public LibraryStore? Store { get; set; }

        /// <summary>Id of the set currently selected in the song list, or null.</summary>
        public Func<string?>? GetSelectedSetId { get; set; }

        /// <summary>Shows a toast (message, isError).</summary>
        public Action<string, bool>? Notify { get; set; }

        /// <summary>Asks the screen's confirm dialog: title, message, button label, action on confirm.</summary>
        public Action<string, string, string, Action>? Confirm { get; set; }

        /// <summary>Runs after the library changed so the song list reloads.</summary>
        public Action? LibraryChanged { get; set; }

        /// <summary>Runs the export (off the UI thread) and reports back on it; supplied by the screen.</summary>
        public Action<string, Action<ActionOutcome>>? ExportRequested { get; set; }

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;
            float row = MobileMetrics.Pick(44, 60);

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(640, 560),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl) { Bottom = EditorTheme.Spacing.Xxl + row + EditorTheme.Spacing.Lg },
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                            Children = new Drawable[]
                            {
                                new SpriteText { Text = "Library", Colour = EditorTheme.Colours.Accent, Font = EditorTheme.Type.Title() },
                                selectedText = new SpriteText { Colour = EditorTheme.Colours.Text, Font = EditorTheme.Type.Body(), Truncate = true, MaxWidth = 580 },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Export .osz", OsuColour.Pink) { Size = new Vector2(240, row), Action = exportSelected },
                                        new OsuButton("Move to trash", EditorTheme.Colours.Error) { Size = new Vector2(240, row), Action = trashSelected },
                                    },
                                },
                                trashHeading = new SpriteText { Colour = EditorTheme.Colours.TextMuted, Font = EditorTheme.Type.Heading() },
                                new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 230,
                                    ScrollbarVisible = false,
                                    Child = trashList = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                                    },
                                },
                            },
                        },
                        new OsuButton("Close", OsuColour.Surface)
                        {
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight,
                            Margin = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Size = new Vector2(160, row),
                            Action = () => Hide(),
                        },
                    },
                },
            };
        }

        protected override void PopIn()
        {
            Refresh();
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
        }

        protected override void PopOut() => this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

        /// <summary>Rebuilds the selected-set line and the trash list from the store.</summary>
        public void Refresh()
        {
            if (Store == null || selectedText == null)
                return;

            var selected = GetSelectedSetId?.Invoke() is string id ? Store.Find(id) : null;
            selectedText.Text = selected != null && !selected.IsDeleted
                ? "Selected: " + LibraryActions.DisplayName(selected)
                : "Pick a beatmap in the list first to export or trash it.";

            var trash = Store.Trash;
            trashHeading.Text = trash.Count == 0 ? "Trash is empty" : $"Trash ({trash.Count})";
            trashList.Clear();

            float row = MobileMetrics.Pick(44, 60);
            foreach (var set in trash)
            {
                string setId = set.Id;
                string name = LibraryActions.DisplayName(set);

                trashList.Add(new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = row,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = name,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Body(),
                            Truncate = true,
                            MaxWidth = 250,
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                            Children = new Drawable[]
                            {
                                new OsuButton("Restore", OsuColour.Surface) { Size = new Vector2(120, row - 6), Action = () => restore(setId) },
                                new OsuButton("Delete forever", EditorTheme.Colours.Error) { Size = new Vector2(170, row - 6), Action = () => askPurge(setId, name) },
                            },
                        },
                    },
                });
            }
        }

        private void exportSelected()
        {
            string? id = GetSelectedSetId?.Invoke();
            if (id == null || Store?.Find(id) is not { IsDeleted: false })
            {
                Notify?.Invoke("Select a beatmap in the list first", true);
                return;
            }

            Notify?.Invoke("Exporting...", false);
            ExportRequested?.Invoke(id, outcome => Notify?.Invoke(outcome.Message, !outcome.Success));
        }

        private void trashSelected()
        {
            string? id = GetSelectedSetId?.Invoke();
            if (id == null || Store?.Find(id) is not { IsDeleted: false })
            {
                Notify?.Invoke("Select a beatmap in the list first", true);
                return;
            }

            apply(LibraryActions.Delete(Store, id));
        }

        private void restore(string id)
        {
            if (Store != null)
                apply(LibraryActions.Restore(Store, id));
        }

        private void askPurge(string id, string name)
        {
            Confirm?.Invoke("Delete forever",
                $"Permanently delete \"{name}\"? This cannot be undone.",
                "Delete forever",
                () =>
                {
                    if (Store != null)
                        apply(LibraryActions.Purge(Store, id));
                });
        }

        private void apply(ActionOutcome outcome)
        {
            Notify?.Invoke(outcome.Message, !outcome.Success);
            Refresh();
            if (outcome.Success)
                LibraryChanged?.Invoke();
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
