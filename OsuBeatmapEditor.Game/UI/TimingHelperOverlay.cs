using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Small helper opened from the New Beatmap panel: plays the chosen audio so you can TAP along to the beat (the running
    /// average of your taps is the BPM) and pause on a downbeat to take the current time as the first beat's offset. "Use these"
    /// hands the BPM / offset back to the form. Nothing is saved until the beatmap is created.
    /// </summary>
    public partial class TimingHelperOverlay : VisibilityContainer
    {
        private const int max_taps = 12;
        private const double reset_gap_ms = 2000;

        /// <summary>Called with (bpm or null, offset ms or null) when the user taps "Use these".</summary>
        public Action<double?, double?>? Applied;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private Track? track;
        private ITrackStore? trackStore;

        private readonly List<double> tapTimes = new List<double>();
        private double? bpm;
        private double? offset;

        private SpriteText timeText = null!;
        private SpriteText bpmText = null!;
        private SpriteText offsetText = null!;
        private OsuButton playButton = null!;
        private Container panel = null!;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = osuTK.Graphics.Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(560, 330),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xl),
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                            Children = new Drawable[]
                            {
                                new SpriteText { Text = "Timing helper", Colour = EditorTheme.Colours.Accent, Font = EditorTheme.Type.Heading() },
                                new SpriteText
                                {
                                    Text = "Play the song and tap TAP on every beat. Pause on a downbeat and press 'Use this time as first beat'.",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                    Children = new Drawable[]
                                    {
                                        playButton = new OsuButton("Play", OsuColour.Surface) { Size = new Vector2(100, 40), Action = togglePlay },
                                        new OsuButton("-5 s", OsuColour.Surface) { Size = new Vector2(70, 40), Action = () => seekBy(-5000) },
                                        new OsuButton("+5 s", OsuColour.Surface) { Size = new Vector2(70, 40), Action = () => seekBy(5000) },
                                        new OsuButton("Restart", OsuColour.Surface) { Size = new Vector2(90, 40), Action = () => track?.Seek(0) },
                                        timeText = new SpriteText
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Colour = EditorTheme.Colours.Text,
                                            Font = EditorTheme.Type.Heading(),
                                            Text = "00:00.000",
                                        },
                                    },
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("TAP", OsuColour.Pink) { Size = new Vector2(180, 90), FontSize = 28, Action = tap },
                                        new FillFlowContainer
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                                            Children = new Drawable[]
                                            {
                                                bpmText = new SpriteText { Colour = EditorTheme.Colours.Text, Font = EditorTheme.Type.Heading() },
                                                offsetText = new SpriteText { Colour = EditorTheme.Colours.Text, Font = EditorTheme.Type.Heading() },
                                                new OsuButton("Use this time as first beat", OsuColour.Surface) { Size = new Vector2(250, 36), Action = takeOffset },
                                            },
                                        },
                                    },
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Reset taps", OsuColour.Surface) { Size = new Vector2(120, 40), Action = resetTaps },
                                        new OsuButton("Use these", OsuColour.Pink) { Size = new Vector2(140, 40), Action = apply },
                                        new OsuButton("Close", OsuColour.Surface) { Size = new Vector2(100, 40), Action = Hide },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            refreshReadouts();
        }

        /// <summary>Opens the helper for an audio file (an absolute path to the staged or chosen copy).</summary>
        public void ShowFor(string audioPath, double? currentBpm, double? currentOffset)
        {
            releaseTrack();
            resetTaps();
            offset = currentOffset;
            bpm = currentBpm;

            try
            {
                var storage = new NativeStorage(Path.GetDirectoryName(audioPath)!, host);
                trackStore = audio.GetTrackStore(new StorageBackedResourceStore(storage));
                track = trackStore.Get(Path.GetFileName(audioPath));
            }
            catch
            {
                track = null;
            }

            refreshReadouts();
            Show();
        }

        private void togglePlay()
        {
            if (track == null)
                return;

            if (track.IsRunning)
                track.Stop();
            else
            {
                if (track.HasCompleted)
                    track.Seek(0);
                track.Start();
            }
        }

        private void seekBy(double deltaMs)
        {
            if (track == null)
                return;

            track.Seek(Math.Clamp(track.CurrentTime + deltaMs, 0, Math.Max(0, track.Length - 1)));
        }

        private void tap()
        {
            double now = Time.Current;
            if (tapTimes.Count > 0 && now - tapTimes[^1] > reset_gap_ms)
                tapTimes.Clear();

            tapTimes.Add(now);
            if (tapTimes.Count > max_taps)
                tapTimes.RemoveAt(0);

            if (tapTimes.Count >= 2)
                bpm = Math.Round(60000.0 * (tapTimes.Count - 1) / (tapTimes[^1] - tapTimes[0]), 1);

            refreshReadouts();
        }

        private void resetTaps()
        {
            tapTimes.Clear();
            bpm = null;
            refreshReadouts();
        }

        private void takeOffset()
        {
            if (track == null)
                return;

            offset = Math.Round(track.CurrentTime);
            refreshReadouts();
        }

        private void apply()
        {
            Applied?.Invoke(bpm, offset);
            Hide();
        }

        private void refreshReadouts()
        {
            if (bpmText == null)
                return;

            bpmText.Text = bpm is double b ? $"BPM: {b.ToString("0.#", CultureInfo.InvariantCulture)}  ({tapTimes.Count} taps)" : "BPM: tap to measure";
            offsetText.Text = offset is double o ? $"First beat: {o.ToString("0", CultureInfo.InvariantCulture)} ms" : "First beat: not set";
        }

        protected override void Update()
        {
            base.Update();

            if (track == null || timeText == null)
                return;

            double t = Math.Max(0, track.CurrentTime);
            timeText.Text = $"{(int)(t / 60000):00}:{(int)(t / 1000) % 60:00}.{(int)t % 1000:000}";
            playButton.Text = track.IsRunning ? "Pause" : "Play";
        }

        private void releaseTrack()
        {
            track?.Stop();
            track?.Dispose();
            track = null;
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            releaseTrack();
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override void Dispose(bool isDisposing)
        {
            releaseTrack();
            base.Dispose(isDisposing);
        }
    }
}
