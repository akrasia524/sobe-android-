using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.VideoExport;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Centred modal for exporting a clip of the map to video: pick the in/out points (from the playhead), a preset
    /// (resolution/fps/size-budget) and whether to show the auto cursor, then render. Lives inside the editor's HUD
    /// layer so it's hidden along with the rest of the chrome while a render is actually running.
    /// </summary>
    public partial class VideoExportPanel : VisibilityContainer
    {
        /// <summary>Current playhead time (ms), used to set the in/out points.</summary>
        public Func<double>? CurrentTime;

        /// <summary>Track length (ms); the out point can't go past it.</summary>
        public double TrackLength;

        /// <summary>Invoked with the chosen options when the user clicks Render.</summary>
        public Action<VideoExportOptions>? OnRender;

        /// <summary>Returns the current destination folder to display.</summary>
        public Func<string>? ResolveFolder;

        /// <summary>Opens the OS folder picker (the editor updates the setting, then calls <see cref="RefreshFolder"/>).</summary>
        public Action? OnBrowseFolder;

        /// <summary>Reveals the current destination folder in the OS file browser.</summary>
        public Action? OnRevealFolder;

        private double inTime;
        private double outTime;
        private VideoExportPreset preset = VideoExportPreset.Default;
        private bool showCursor = true;
        private bool includeHitsounds = true;
        private bool modHardRock;
        private bool modEasy;
        private bool modHidden;
        private bool modDoubleTime;
        private bool modNightcore;

        private SpriteText inValue = null!;
        private SpriteText outValue = null!;
        private SpriteText durationValue = null!;
        private SpriteText folderValue = null!;
        private SpriteText summary = null!;
        private ModChip hrChip = null!;
        private ModChip ezChip = null!;
        private ModChip hdChip = null!;
        private ModChip dtChip = null!;
        private ModChip ncChip = null!;
        private ToggleRow cursorToggle = null!;
        private TextButton renderButton = null!;
        private readonly List<PresetRow> presetRows = new List<PresetRow>();

        protected override bool StartHidden => true;

        public VideoExportPanel()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var presetFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            };

            foreach (var p in VideoExportPreset.Defaults)
            {
                var row = new PresetRow(p, () => selectPreset(p));
                presetRows.Add(row);
                presetFlow.Add(row);
            }

            InternalChildren = new Drawable[]
            {
                new ClickCatcher { Action = Hide },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 420,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    BorderThickness = 1f,
                    BorderColour = EditorTheme.Colours.Border,
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = new Color4(0, 0, 0, 0.5f),
                        Radius = 16f,
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xl),
                            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                            Children = new Drawable[]
                            {
                                header(),
                                rangeHeader(),
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                                    Children = new Drawable[]
                                    {
                                        rangeButton("Set in", out inValue, setIn),
                                        rangeButton("Set out", out outValue, setOut),
                                    },
                                },
                                sectionLabel("Mods"),
                                modsRow(),
                                sectionLabel("Preset"),
                                presetFlow,
                                cursorToggle = new ToggleRow("Show cursor", showCursor, v => showCursor = v),
                                new ToggleRow("Include hitsounds", includeHitsounds, v => includeHitsounds = v),
                                sectionLabel("Output folder"),
                                folderRow(),
                                summary = new SpriteText
                                {
                                    Font = EditorTheme.Type.Caption(),
                                    Colour = EditorTheme.Colours.TextMuted,
                                },
                                renderButton = new TextButton("Render video", EditorTheme.Colours.Accent, doRender)
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 38,
                                },
                            },
                        },
                    },
                },
            };

            refreshPresetSelection();
            refreshMods();
        }

        private Drawable header() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 24,
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "Export video",
                    Font = EditorTheme.Type.Title(),
                    Colour = EditorTheme.Colours.Text,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                new CloseButton(Hide)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                },
            },
        };

        private static SpriteText sectionLabel(string text) => new SpriteText
        {
            Text = text.ToUpperInvariant(),
            Font = EditorTheme.Type.Caption(),
            Colour = EditorTheme.Colours.TextFaint,
        };

        // "RANGE" on the left, the live clip duration on the right.
        private Drawable rangeHeader()
        {
            durationValue = new SpriteText
            {
                Text = "0:00.000",
                Font = EditorTheme.Type.Label(numeric: true),
                Colour = EditorTheme.Colours.Accent,
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 14,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "RANGE",
                        Font = EditorTheme.Type.Caption(),
                        Colour = EditorTheme.Colours.TextFaint,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    durationValue,
                },
            };
        }

        private Drawable modsRow() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
            Children = new Drawable[]
            {
                hrChip = new ModChip("HR", EditorTheme.Colours.Error, () => toggleMod(Mod.HardRock)),
                ezChip = new ModChip("EZ", EditorTheme.Colours.Velocity, () => toggleMod(Mod.Easy)),
                hdChip = new ModChip("HD", EditorTheme.Colours.Selection, () => toggleMod(Mod.Hidden)),
                dtChip = new ModChip("DT", EditorTheme.Colours.Accent, () => toggleMod(Mod.DoubleTime)),
                ncChip = new ModChip("NC", EditorTheme.Colours.Info, () => toggleMod(Mod.Nightcore)),
            },
        };

        private enum Mod { HardRock, Easy, Hidden, DoubleTime, Nightcore }

        private void toggleMod(Mod mod)
        {
            switch (mod)
            {
                case Mod.HardRock:
                    modHardRock = !modHardRock;
                    if (modHardRock) modEasy = false; // HR and EZ are mutually exclusive (osu!'s rule)
                    break;

                case Mod.Easy:
                    modEasy = !modEasy;
                    if (modEasy) modHardRock = false;
                    break;

                case Mod.Hidden:
                    modHidden = !modHidden;
                    break;

                case Mod.DoubleTime:
                    modDoubleTime = !modDoubleTime;
                    if (modDoubleTime) modNightcore = false; // DT and NC are mutually exclusive
                    break;

                case Mod.Nightcore:
                    modNightcore = !modNightcore;
                    if (modNightcore) modDoubleTime = false;
                    break;
            }

            refreshMods();
            refreshTimes(); // the speed mod changes the video's length + estimated size
        }

        private void refreshMods()
        {
            hrChip.SetActive(modHardRock);
            ezChip.SetActive(modEasy);
            hdChip.SetActive(modHidden);
            dtChip.SetActive(modDoubleTime);
            ncChip.SetActive(modNightcore);
        }

        private Drawable folderRow()
        {
            folderValue = new SpriteText
            {
                Text = "-",
                Font = EditorTheme.Type.Body(),
                Colour = EditorTheme.Colours.TextMuted,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 28,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Right = 170 },
                        Masking = true,
                        Child = folderValue,
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
                            new TextButton("Browse...", EditorTheme.Colours.Control, () => OnBrowseFolder?.Invoke()) { Width = 96, Height = 28 },
                            new TextButton("Open", EditorTheme.Colours.Control, () => OnRevealFolder?.Invoke()) { Width = 60, Height = 28 },
                        },
                    },
                },
            };
        }

        /// <summary>Re-reads the destination folder into the label (after opening the panel or picking a new folder).</summary>
        public void RefreshFolder()
        {
            folderValue.Text = shortenPath(ResolveFolder?.Invoke() ?? string.Empty);
        }

        private static string shortenPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "-";

            string[] parts = path.TrimEnd('/', '\\').Split('/', '\\');
            return parts.Length <= 2 ? path : $".../{parts[^2]}/{parts[^1]}";
        }

        // Builds a "Set in/out" button with a value label underneath; hands the label back via out-param so the panel
        // can update the time text later.
        private Drawable rangeButton(string caption, out SpriteText valueLabel, Action onClick)
        {
            valueLabel = new SpriteText
            {
                Text = "--:--.---",
                Font = EditorTheme.Type.BodyStrong(numeric: true),
                Colour = EditorTheme.Colours.Text,
            };

            return new Container
            {
                Width = 186,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                        Children = new Drawable[]
                        {
                            new TextButton(caption, EditorTheme.Colours.Control, onClick)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 30,
                            },
                            valueLabel,
                        },
                    },
                },
            };
        }

        private void setIn()
        {
            inTime = CurrentTime?.Invoke() ?? inTime;
            if (outTime <= inTime)
                outTime = Math.Min(inTime + 5000, TrackLength > 0 ? TrackLength : inTime + 5000);
            refreshTimes();
        }

        private void setOut()
        {
            double t = CurrentTime?.Invoke() ?? outTime;
            outTime = Math.Max(t, inTime + 1);
            refreshTimes();
        }

        private void selectPreset(VideoExportPreset p)
        {
            preset = p;
            refreshPresetSelection();
            refreshTimes();
        }

        private void refreshPresetSelection()
        {
            foreach (var row in presetRows)
                row.SetSelected(row.Preset == preset);
        }

        private void refreshTimes()
        {
            inValue.Text = formatTime(inTime);
            outValue.Text = formatTime(outTime);

            double durationSeconds = Math.Max(0, (outTime - inTime) / 1000.0);
            double rate = modDoubleTime || modNightcore ? 1.5 : 1.0;
            double outputSeconds = durationSeconds / rate;
            bool valid = outTime > inTime;

            // Live duration of the selected range (map time) next to the range header.
            durationValue.Text = formatTime(Math.Max(0, outTime - inTime));

            // The size estimate is over the real video length, which a speed mod shortens.
            summary.Text = valid ? summaryText(outputSeconds) : "Set an out point after the in point";
            summary.Colour = valid ? EditorTheme.Colours.TextMuted : EditorTheme.Colours.Warning;

            renderButton.SetEnabled(valid);
        }

        // Leads with the estimated file size (what the user asked to preview), then the resolution/fps.
        private string summaryText(double durationSeconds)
        {
            string size = $"{preset.EstimateBytes(durationSeconds) / 1_000_000.0:0.#} MB";
            string spec = $"{preset.Width}x{preset.Height} {preset.Fps}fps";
            string tail = preset.SizeMode == VideoSizeMode.SizeBudget ? "max" : "approx";
            return $"~{size} {tail}  -  {spec}";
        }

        private void doRender()
        {
            if (outTime <= inTime)
                return;

            OnRender?.Invoke(new VideoExportOptions
            {
                Preset = preset,
                StartTime = inTime,
                EndTime = outTime,
                ShowCursor = showCursor,
                IncludeHitsounds = includeHitsounds,
                HardRock = modHardRock,
                Easy = modEasy,
                Hidden = modHidden,
                DoubleTime = modDoubleTime,
                Nightcore = modNightcore,
            });

            Hide();
        }

        protected override void PopIn()
        {
            // Default the range around the current playhead each time the panel opens.
            inTime = CurrentTime?.Invoke() ?? 0;
            outTime = Math.Min(inTime + 5000, TrackLength > 0 ? TrackLength : inTime + 5000);
            refreshTimes();
            RefreshFolder();

            this.FadeIn(120, Easing.OutQuint);
        }

        protected override void PopOut() => this.FadeOut(100, Easing.OutQuint);

        private static string formatTime(double ms)
        {
            var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        /// <summary>A selectable preset entry: name + spec, highlighted when it's the active preset.</summary>
        private partial class PresetRow : CompositeDrawable
        {
            public readonly VideoExportPreset Preset;

            private readonly Action onClick;
            private readonly Box background;
            private readonly Container outline;
            private bool selected;

            public PresetRow(VideoExportPreset preset, Action onClick)
            {
                Preset = preset;
                this.onClick = onClick;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                InternalChild = outline = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Md,
                    BorderThickness = 1f,
                    BorderColour = EditorTheme.Colours.Border,
                    Children = new Drawable[]
                    {
                        background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control, Alpha = 0.6f },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 1),
                            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Md },
                            Children = new Drawable[]
                            {
                                new SpriteText { Text = preset.Name, Font = EditorTheme.Type.Label(), Colour = EditorTheme.Colours.Text },
                                new SpriteText { Text = preset.Description, Font = EditorTheme.Type.Caption(), Colour = EditorTheme.Colours.TextMuted },
                            },
                        },
                    },
                };
            }

            public void SetSelected(bool value)
            {
                selected = value;
                outline.BorderColour = value ? EditorTheme.Colours.Accent : EditorTheme.Colours.Border;
                background.Colour = value ? EditorTheme.Colours.AccentSoft : EditorTheme.Colours.Control;
                background.Alpha = value ? 1f : 0.6f;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.Alpha = 1f;
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                    background.Alpha = 0.6f;
            }

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }

        /// <summary>A labelled on/off switch row.</summary>
        private partial class ToggleRow : CompositeDrawable
        {
            private readonly Action<bool> onChanged;
            private readonly Box knob;
            private readonly Box track;
            private bool value;

            public ToggleRow(string label, bool initial, Action<bool> onChanged)
            {
                this.onChanged = onChanged;
                value = initial;

                RelativeSizeAxes = Axes.X;
                Height = 22;

                InternalChildren = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = EditorTheme.Type.Body(),
                        Colour = EditorTheme.Colours.Text,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Size = new Vector2(38, 20),
                        Masking = true,
                        CornerRadius = 10,
                        Children = new Drawable[]
                        {
                            track = new Box { RelativeSizeAxes = Axes.Both },
                            knob = new Box
                            {
                                Size = new Vector2(14),
                                Position = new Vector2(3, 3),
                                Colour = EditorTheme.Colours.Text,
                            },
                        },
                    },
                };

                updateVisual();
            }

            private void updateVisual()
            {
                track.Colour = value ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control;
                knob.MoveToX(value ? 21 : 3, 60, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                value = !value;
                updateVisual();
                onChanged(value);
                return true;
            }
        }

        /// <summary>A mod toggle chip: outlined in the mod's colour when off, filled when on.</summary>
        private partial class ModChip : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Color4 colour;
            private readonly Box background;
            private readonly SpriteText label;
            private bool active;

            public ModChip(string text, Color4 colour, Action onClick)
            {
                this.onClick = onClick;
                this.colour = colour;

                Size = new Vector2(50, 30);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
                BorderThickness = 1.5f;
                BorderColour = colour;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0 },
                    label = new SpriteText
                    {
                        Text = text,
                        Font = EditorTheme.Type.Label(),
                        Colour = colour,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                };
            }

            public void SetActive(bool value)
            {
                active = value;
                background.FadeTo(value ? 1f : 0f, 80);
                label.FadeColour(value ? EditorTheme.Colours.Text : colour, 80);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!active)
                    background.FadeTo(0.25f, 80);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                    background.FadeTo(0f, 80);
            }

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }

        /// <summary>A filled text button with hover/press feedback and an enabled state.</summary>
        private partial class TextButton : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Color4 baseColour;
            private readonly Box background;
            private readonly SpriteText text;
            private bool enabled = true;

            public TextButton(string label, Color4 colour, Action onClick)
            {
                this.onClick = onClick;
                baseColour = colour;

                AutoSizeAxes = Axes.None;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                    text = new SpriteText
                    {
                        Text = label,
                        Font = EditorTheme.Type.Label(),
                        Colour = EditorTheme.Colours.Text,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                };
            }

            public void SetEnabled(bool value)
            {
                enabled = value;
                this.FadeTo(value ? 1f : 0.4f, 80);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (enabled)
                    background.FadeColour(baseColour == EditorTheme.Colours.Accent ? EditorTheme.Colours.AccentHover : EditorTheme.Colours.ControlHover, 80);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => background.FadeColour(baseColour, 80);

            protected override bool OnClick(ClickEvent e)
            {
                if (enabled)
                    onClick();
                return true;
            }
        }

        /// <summary>Small "x" that closes the panel.</summary>
        private partial class CloseButton : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly SpriteIcon icon;

            public CloseButton(Action onClick)
            {
                this.onClick = onClick;
                Size = new Vector2(22);

                InternalChild = icon = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.Times,
                    Size = new Vector2(14),
                    Colour = EditorTheme.Colours.TextMuted,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                icon.FadeColour(EditorTheme.Colours.Text, 80);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => icon.FadeColour(EditorTheme.Colours.TextMuted, 80);

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }

        /// <summary>Transparent full-area backdrop that closes the modal on an outside click.</summary>
        private partial class ClickCatcher : Drawable
        {
            public Action? Action;

            public ClickCatcher() => RelativeSizeAxes = Axes.Both;

            public override bool IsPresent => true;

            protected override bool OnClick(ClickEvent e)
            {
                Action?.Invoke();
                return true;
            }
        }
    }
}
