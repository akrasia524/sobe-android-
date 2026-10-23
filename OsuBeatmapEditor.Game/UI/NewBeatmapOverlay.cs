using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Library;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Modal dialog for creating a new (empty) beatmap: title, artist, creator, difficulty name,
    /// BPM and an audio file. On confirm it builds a standard .osz and hands it to osu!lazer.
    /// </summary>
    public partial class NewBeatmapOverlay : VisibilityContainer
    {
        /// <summary>Allowed audio extensions for the source track (lower-case, with leading dot).</summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".ogg", ".wav" };

        /// <summary>Invoked after a beatmap has been successfully built and sent to osu!lazer.</summary>
        public Action<NewBeatmapRequest>? Created;

        private FormField artistField = null!;
        private FormField titleField = null!;
        private FormField creatorField = null!;
        private FormField difficultyField = null!;
        private FormField bpmField = null!;
        private BasicFileSelector? fileSelector;

        /// <summary>
        /// When set (Android), creating a beatmap goes here instead of osu!lazer's realm: returns an error message, or null on
        /// success. It runs off the UI thread.
        /// </summary>
        public Func<NewBeatmapRequest, string?>? CreateSet { get; set; }

        /// <summary>Called with a staged audio path when it is no longer needed (cancelled, replaced or created).</summary>
        public Action<string>? DiscardAudio { get; set; }

        /// <summary>Copies a picked audio file into app storage; returns the new path or an error. Null = use the picked path as is.</summary>
        public Func<string, (string? Path, string? Error)>? StageAudio { get; set; }

        // Optional background image (Android picker): a copy in the app cache, used only if the user chose one.
        private string? backgroundPath;
        private SpriteText backgroundChipText = null!;
        private Box backgroundChipBackground = null!;
        private OsuButton removeBackgroundButton = null!;

        private string? stagedAudio;
        private bool creating;
        private Box audioChipBackground = null!;
        private SpriteText audioChipText = null!;
        private SpriteText statusText = null!;
        private OsuButton createButton = null!;

        private Container panel = null!;
        private string? audioPath;

        // Set when seeding from an existing set ("Create new Set"): reuse its audio filename and timing.
        private string? seededAudioFileName;
        private System.Collections.Generic.IReadOnlyList<string>? seededTimingLines;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = osuTK.Graphics.Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(EditorTheme.Sizing.OverlayWidth, EditorTheme.Sizing.OverlayHeight),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Raised,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Child = new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),                 // header
                                    new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Xl),
                                    new Dimension(),                                       // body (fills)
                                    new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Lg),
                                    new Dimension(GridSizeMode.AutoSize),                 // footer
                                },
                                Content = new[]
                                {
                                    new Drawable?[] { buildHeader() },
                                    new Drawable?[] { null },
                                    new Drawable?[] { buildBody() },
                                    new Drawable?[] { null },
                                    new Drawable?[] { buildFooter() },
                                },
                            },
                        },
                    },
                },
            };

            createButton.Enabled.Value = false;

            artistField.Current.ValueChanged += _ => validate();
            titleField.Current.ValueChanged += _ => validate();
            bpmField.Current.ValueChanged += _ => validate();

            fileSelector?.CurrentFile.BindValueChanged(e =>
            {
                if (e.NewValue == null)
                    return;

                // Picking a file overrides any seeded audio; its extension now derives from the chosen path.
                setAudio(e.NewValue.FullName);
            });
        }

        private Drawable buildHeader() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "New Beatmap",
                    Colour = EditorTheme.Colours.Accent,
                    Font = EditorTheme.Type.Display(),
                },
                new SpriteText
                {
                    Text = "Fill in the metadata and choose an audio track to start from.",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
            },
        };

        private Drawable buildBody() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 320),
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Xxl),
                new Dimension(),
            },
            Content = new[]
            {
                new Drawable?[]
                {
                    // Left column: metadata fields.
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                        Children = new Drawable[]
                        {
                            artistField = new FormField("Artist"),
                            titleField = new FormField("Title"),
                            creatorField = new FormField("Creator"),
                            difficultyField = new FormField("Difficulty name", "Normal"),
                            bpmField = new FormField("BPM", "120"),
                        },
                    },
                    null,
                    // Right column: audio track selection.
                    buildAudioColumn(),
                },
            },
        };

        private Drawable buildAudioColumn() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = new[]
            {
                new Dimension(GridSizeMode.AutoSize),                 // heading
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Sm),
                new Dimension(GridSizeMode.AutoSize),                 // selected-file chip
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Sm),
                new Dimension(),                                       // file browser (fills)
            },
            Content = new[]
            {
                new Drawable?[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Xxs),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "Audio track",
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Heading(),
                            },
                            new SpriteText
                            {
                                Text = PlatformPicker.PickAudio != null ? "mp3 / ogg / wav" : "Drag a file onto the window, or browse below  (mp3 / ogg / wav)",
                                Colour = EditorTheme.Colours.TextFaint,
                                Font = EditorTheme.Type.Caption(),
                            },
                        },
                    },
                },
                new Drawable?[] { null },
                new Drawable?[] { buildAudioChip() },
                new Drawable?[] { null },
                new Drawable?[] { PlatformPicker.PickAudio != null ? buildPickerPanel() : buildDesktopBrowser() },
            },
        };

        /// <summary>Desktop: the in-game file browser (unchanged).</summary>
        private Drawable buildDesktopBrowser() =>
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = EditorTheme.Radius.Md,
                        BorderThickness = EditorTheme.Sizing.BorderThickness,
                        BorderColour = EditorTheme.Colours.Border,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = EditorTheme.Colours.Sunken,
                            },
                            fileSelector = new BasicFileSelector(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                AudioExtensions)
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                        },
                    };

        /// <summary>Android: one big button that opens the chooser (system picker plus installed file managers).</summary>
        private Drawable buildPickerPanel() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
            Children = new Drawable[]
            {
                new OsuButton("Choose audio file", OsuColour.Pink)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = MobileMetrics.Pick(48, 72),
                    Action = onChooseAudio,
                },
                new SpriteText
                {
                    Text = "Opens Android's chooser. You can use the built-in picker or a file manager app.",
                    Colour = EditorTheme.Colours.TextFaint,
                    Font = EditorTheme.Type.Caption(),
                },
                buildBackgroundSection(),
            },
        };

        /// <summary>Optional background image: a chip showing the choice, a Choose button and a Remove button. Skipping it is fine.</summary>
        private Drawable buildBackgroundSection() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "Background image (optional)",
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Heading(),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = EditorTheme.Sizing.RowHeight,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Sm,
                    Children = new Drawable[]
                    {
                        backgroundChipBackground = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                        backgroundChipText = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Md },
                            Text = "No background - you can add one later in Song Setup",
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.BodyStrong(),
                            Truncate = true,
                            RelativeSizeAxes = Axes.X,
                        },
                    },
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                    Children = new Drawable[]
                    {
                        new OsuButton("Choose background image", OsuColour.Surface) { Size = new Vector2(250, 40), Action = onChooseBackground },
                        removeBackgroundButton = new OsuButton("Remove", OsuColour.Surface) { Size = new Vector2(100, 40), Action = () => clearBackground(), Alpha = 0 },
                    },
                },
            },
        };

        private void onChooseBackground()
        {
            var pick = PlatformPicker.PickImage;
            if (pick == null || creating)
                return;

            pick(result => Schedule(() => onBackgroundPicked(result)));
        }

        private void onBackgroundPicked(PickResult result)
        {
            if (result.Cancelled)
                return; // backed out: keep whatever was chosen before

            if (result.Path == null)
            {
                showStatus(result.Error ?? "Could not open that file.", true);
                return;
            }

            string? problem = PickedImage.Validate(result.Path);
            if (problem != null)
            {
                tryDelete(result.Path);
                showStatus(problem, true);
                return;
            }

            if (!IsLoaded || State.Value == Visibility.Hidden)
            {
                tryDelete(result.Path); // the dialog was dismissed meanwhile: don't keep an orphaned copy
                return;
            }

            discardBackground();
            backgroundPath = result.Path;
            backgroundChipText.Text = Path.GetFileName(result.Path);
            backgroundChipText.Colour = EditorTheme.Colours.Text;
            backgroundChipBackground.FadeColour(EditorTheme.Colours.AccentSoft, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            removeBackgroundButton.Alpha = 1;
            showStatus("Background selected: " + Path.GetFileName(result.Path), false);
        }

        /// <summary>Forgets the chosen background (and deletes its cached copy). The map is then created without one.</summary>
        private void clearBackground()
        {
            discardBackground();
            if (backgroundChipText == null)
                return;

            backgroundChipText.Text = "No background - you can add one later in Song Setup";
            backgroundChipText.Colour = EditorTheme.Colours.TextMuted;
            backgroundChipBackground.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            removeBackgroundButton.Alpha = 0;
        }

        private void discardBackground()
        {
            if (backgroundPath == null)
                return;

            tryDelete(backgroundPath);
            try
            {
                string? dir = Path.GetDirectoryName(backgroundPath);
                if (dir != null && Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty) == "image-pick")
                    Directory.Delete(dir, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            backgroundPath = null;
        }

        private void onChooseAudio()
        {
            var pick = PlatformPicker.PickAudio;
            if (pick == null || creating)
                return;

            pick(result => Schedule(() => onAudioPicked(result)));
        }

        private void onAudioPicked(PickResult result)
        {
            if (result.Cancelled)
                return; // the user backed out of the chooser: keep whatever was chosen before

            if (result.Path == null)
            {
                showStatus(result.Error ?? "Could not open that file.", true);
                return;
            }

            string picked = result.Path;
            showStatus("Reading audio file...", false);

            // Copying (up to hundreds of MB) and reading tags must not block the UI thread.
            Task.Run(() =>
            {
                string path = picked;
                string? error = null;

                if (StageAudio != null)
                {
                    var staged = StageAudio(picked);
                    tryDelete(picked); // the picker's temporary copy is no longer needed either way
                    if (staged.Path == null)
                        error = staged.Error ?? "Could not use that file.";
                    else
                        path = staged.Path;
                }
                else
                {
                    error = PickedAudio.Validate(path);
                }

                var tags = error == null ? AudioTags.Read(path) : new AudioTagInfo(null, null);
                Schedule(() => applyPickedAudio(path, error, tags));
            });
        }

        private void applyPickedAudio(string path, string? error, AudioTagInfo tags)
        {
            if (error != null)
            {
                showStatus(error, true);
                return;
            }

            if (!IsLoaded || State.Value == Visibility.Hidden)
            {
                // The dialog was dismissed while the file was being read: don't keep an orphaned copy.
                if (StageAudio != null)
                    DiscardAudio?.Invoke(path);
                return;
            }

            discardStaged();
            if (StageAudio != null)
                stagedAudio = path;

            setAudio(path);
            showStatus("Audio selected: " + Path.GetFileName(path), false);
            fillMetadata(path, tags);
        }

        // What we last typed into Artist / Title ourselves: a new pick may replace those, never text the user wrote.
        private string? autoArtist, autoTitle;

        private void setAuto(FormField field, ref string? remembered, string? value)
        {
            string current = field.Current.Value;
            if (value == null || (current.Length > 0 && current != remembered))
                return;

            field.Current.Value = value;
            remembered = value;
        }

        /// <summary>
        /// Artist / Title from the file's own tags when it has any (the file name is often "Title - Vocaloid" or similar, so it is
        /// not trusted first). Only a tag-less file falls back to the "Artist - Title" file-name convention.
        /// </summary>
        private void fillMetadata(string path, AudioTagInfo tags)
        {
            if (tags.HasAny)
            {
                setAuto(artistField, ref autoArtist, tags.Artist);
                setAuto(titleField, ref autoTitle, tags.Title);
                return;
            }

            string stem = Path.GetFileNameWithoutExtension(path);
            int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                setAuto(artistField, ref autoArtist, stem[..dash].Trim());
                setAuto(titleField, ref autoTitle, stem[(dash + 3)..].Trim());
            }
            else
            {
                setAuto(titleField, ref autoTitle, stem.Trim());
            }
        }

        private void showStatus(string text, bool error)
        {
            statusText.Colour = error ? EditorTheme.Colours.Error : EditorTheme.Colours.TextMuted;
            statusText.Text = text;
        }

        /// <summary>Android: creates the set in the app library off the UI thread; nothing is added if it fails.</summary>
        private void createInLibrary(NewBeatmapRequest request)
        {
            var create = CreateSet!;
            creating = true;
            createButton.Enabled.Value = false;
            showStatus("Creating...", false);

            Task.Run(() => create(request)).ContinueWith(t => Schedule(() =>
            {
                creating = false;
                string? error = t.IsFaulted ? t.Exception?.GetBaseException().Message ?? "unknown error" : t.Result;

                if (error != null)
                {
                    showStatus("Failed: " + error, true);
                    validate(); // the form and the chosen audio stay as they were so the user can retry
                    return;
                }

                showStatus("Beatmap created.", false);
                clearStagedAudio();
                clearBackground();
                Created?.Invoke(request);
                Scheduler.AddDelayed(Hide, 600);
            }));
        }

        /// <summary>Back to a blank form: metadata, chosen audio, seeded data and any message. Used whenever the dialog closes or opens.</summary>
        private void resetForm()
        {
            discardStaged();

            artistField.Current.Value = string.Empty;
            titleField.Current.Value = string.Empty;
            creatorField.Current.Value = string.Empty;
            difficultyField.Current.Value = "Normal";
            bpmField.Current.Value = "120";

            audioPath = null;
            seededAudioFileName = null;
            seededTimingLines = null;
            autoArtist = autoTitle = null;

            clearBackground();
            setAudioLabel("No audio selected");
            statusText.Text = string.Empty;
            validate();
        }

        /// <summary>Drops the staged audio copy and forgets the chosen file (the set already has its own copy, or was cancelled).</summary>
        private void clearStagedAudio()
        {
            if (stagedAudio == null)
                return;

            discardStaged();
            audioPath = null;
            setAudioLabel("No audio selected");
            validate();
        }

        private void discardStaged()
        {
            if (stagedAudio != null)
                DiscardAudio?.Invoke(stagedAudio);
            stagedAudio = null;
        }

        private static void tryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        private Drawable buildAudioChip() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = EditorTheme.Sizing.RowHeight,
            Masking = true,
            CornerRadius = EditorTheme.Radius.Sm,
            Children = new Drawable[]
            {
                audioChipBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = EditorTheme.Colours.Control,
                },
                audioChipText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Md },
                    Text = "No audio selected",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.BodyStrong(),
                    Truncate = true,
                    RelativeSizeAxes = Axes.X,
                },
            },
        };

        private Drawable buildFooter() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                statusText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                    Children = new Drawable[]
                    {
                        new OsuButton("Cancel", OsuColour.Surface)
                        {
                            Size = new Vector2(120, 44),
                            Action = () => Hide(),
                        },
                        createButton = new OsuButton("Create", OsuColour.Pink)
                        {
                            Size = new Vector2(150, 44),
                            Action = onCreate,
                        },
                    },
                },
            },
        };

        private NewBeatmapRequest buildRequest() => new NewBeatmapRequest
        {
            Artist = artistField.Current.Value.Trim(),
            Title = titleField.Current.Value.Trim(),
            Creator = creatorField.Current.Value.Trim(),
            DifficultyName = string.IsNullOrWhiteSpace(difficultyField.Current.Value) ? "Normal" : difficultyField.Current.Value.Trim(),
            Bpm = double.TryParse(bpmField.Current.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) ? bpm : 0,
            AudioPath = audioPath ?? string.Empty,
            AudioFileName = seededAudioFileName,
            TimingPointLines = seededTimingLines,
            BackgroundPath = backgroundPath,
        };

        /// <summary>Opens the dialog for a brand-new map (clears any seeded audio/timing from a prior "new set").</summary>
        public void ShowForNewBeatmap()
        {
            resetForm();
            Show();
        }

        /// <summary>
        /// Opens the dialog for a brand-new map with an audio file already chosen (e.g. dropped onto the
        /// window). Artist/Title are guessed from the filename when they fit the "Artist - Title" convention.
        /// </summary>
        public void ShowForDroppedAudio(string path)
        {
            resetForm();
            setAudio(path);

            string stem = Path.GetFileNameWithoutExtension(path);
            int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                if (artistField.Current.Value.Length == 0)
                    artistField.Current.Value = stem[..dash].Trim();
                if (titleField.Current.Value.Length == 0)
                    titleField.Current.Value = stem[(dash + 3)..].Trim();
            }
            else if (titleField.Current.Value.Length == 0)
            {
                titleField.Current.Value = stem.Trim();
            }

            Show();
        }

        /// <summary>
        /// Opens the dialog pre-filled from an existing set ("Create new Set"): metadata, plus its audio file
        /// and timing points carried over. The user can still adjust any field or pick a different audio file.
        /// </summary>
        public void ShowSeeded(string artist, string title, string creator, string audioStorePath,
                               string audioFileName, System.Collections.Generic.IReadOnlyList<string> timingLines, double bpm)
        {
            resetForm();
            artistField.Current.Value = artist;
            titleField.Current.Value = title;
            creatorField.Current.Value = creator;
            difficultyField.Current.Value = "Normal";
            if (bpm > 0)
                bpmField.Current.Value = bpm.ToString("0.###", CultureInfo.InvariantCulture);

            audioPath = audioStorePath;
            seededAudioFileName = audioFileName;
            seededTimingLines = timingLines;
            setAudioLabel($"Reusing audio: {audioFileName}");

            validate();
            Show();
        }

        /// <summary>Records a freshly chosen source audio path (overriding any seeded audio) and updates the UI.</summary>
        private void setAudio(string path)
        {
            audioPath = path;
            seededAudioFileName = null;
            setAudioLabel(Path.GetFileName(path));
            validate();
        }

        private void setAudioLabel(string text)
        {
            bool hasAudio = audioPath != null;
            audioChipText.Text = text;
            audioChipText.Colour = hasAudio ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted;
            audioChipBackground.FadeColour(hasAudio ? EditorTheme.Colours.AccentSoft : EditorTheme.Colours.Control,
                EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        private void validate() => createButton.Enabled.Value = buildRequest().IsValid;

        private void onCreate()
        {
            if (creating)
                return;

            var request = buildRequest();
            if (!request.IsValid)
                return;

            if (CreateSet != null)
            {
                createInLibrary(request);
                return;
            }

            // Write the new set straight into osu!lazer's realm + file store (no importer, no launch).
            string? error = BeatmapRealmCreator.CreateSet(request);

            statusText.Colour = error == null ? EditorTheme.Colours.Success : EditorTheme.Colours.Error;
            statusText.Text = error == null ? "Beatmap created." : $"Failed: {error}";

            if (error == null)
            {
                Created?.Invoke(request);
                Scheduler.AddDelayed(Hide, 1200);
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.MoveToY(0, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            if (!creating)
                resetForm(); // cancelled or finished: nothing typed here is kept for the next time

            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.MoveToY(20, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Click outside the panel (children of the panel consume their own clicks) dismisses.
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

        /// <summary>A vertical label + text box pair used for each form field.</summary>
        private partial class FormField : FillFlowContainer
        {
            private readonly string label;
            private readonly string placeholder;
            private BasicTextBox textBox = null!;

            public Bindable<string> Current => textBox.Current;

            public FormField(string label, string placeholder = "")
            {
                this.label = label;
                this.placeholder = placeholder;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Label(),
                    },
                    textBox = new BasicTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = EditorTheme.Sizing.InputHeight,
                        PlaceholderText = placeholder,
                        Text = placeholder,
                    },
                };
            }
        }

        /// <summary>Container that swallows mouse input so clicks inside the panel don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
