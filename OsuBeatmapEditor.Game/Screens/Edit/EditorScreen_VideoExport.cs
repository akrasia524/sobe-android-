using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.IO.Stores;
using osu.Framework.Timing;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.VideoExport;
using OsuBeatmapEditor.Resources;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Offline video export (see the <c>VideoExport</c> folder for the engine). Renders a chosen time range of the map
    /// to an .mp4 by driving a manual clock frame-by-frame over a clean gameplay view and screenshotting the backbuffer
    /// (the only CPU readback osu!framework exposes). Deterministic: no dropped frames, always smooth at the target fps.
    ///
    /// The pieces here are the framework glue behind <see cref="VideoCaptureDriver"/>'s injected capture delegate; the
    /// encoder / preset / frame-conversion logic lives in the (framework-agnostic, unit-tested) VideoExport engine.
    /// </summary>
    public partial class EditorScreen
    {
        // --- capture state (read on the update thread; see the guard in Update() and VisualTime) ---
        private bool capturing;
        private double captureTimeMs;
        private ManualClock? captureSource;
        private FramedClock? captureClock;

        private bool videoExportInProgress;

        // Saved editor state, restored when capture ends.
        private MarginPadding savedComposePadding;
        private bool savedAutoPlay;
        private float savedToastsAlpha;
        private bool savedHardRock;
        private bool savedEasy;
        private bool savedHidden;

        /// <summary>Applies the current capture time to the manual clock each update frame (called from Update()).</summary>
        private void tickCaptureClock()
        {
            if (captureSource == null || captureClock == null)
                return;

            captureSource.CurrentTime = captureTimeMs;
            captureClock.ProcessFrame();
        }

        private VideoExportPanel videoExportPanel = null!;

        /// <summary>Adds the export modal to the HUD layer (so it's hidden with the chrome during a render). Called from load().</summary>
        private void setupVideoExportPanel()
        {
            hudLayer.Add(videoExportPanel = new VideoExportPanel
            {
                CurrentTime = () => VisualTime,
                TrackLength = track?.Length ?? 0,
                OnRender = StartVideoExport,
                ResolveFolder = videoExportFolder,
                OnBrowseFolder = browseVideoFolder,
                OnRevealFolder = revealVideoFolder,
            });
        }

        /// <summary>The destination folder for renders: the user's chosen folder, or the app's default storage folder.</summary>
        private string videoExportFolder()
        {
            string configured = settings.VideoExportFolder.Value;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return host.Storage.GetFullPath("video-exports");
        }

        /// <summary>Opens the OS folder picker; persists the choice and refreshes the panel.</summary>
        private void browseVideoFolder()
        {
            Task.Run(async () =>
            {
                string? picked = await NativeFolderPicker.PickAsync("Choose the video export folder").ConfigureAwait(false);
                if (picked == null)
                    return;

                Schedule(() =>
                {
                    settings.VideoExportFolder.Value = picked;
                    videoExportPanel.RefreshFolder();
                    toasts?.Push($"Videos will save to {Path.GetFileName(picked.TrimEnd('/', '\\'))}", EditorTheme.Colours.Success);
                });
            });
        }

        /// <summary>Reveals the current destination folder in the OS file browser (creating it first if needed).</summary>
        private void revealVideoFolder()
        {
            string dir = videoExportFolder();
            try
            {
                Directory.CreateDirectory(dir);
                host.PresentFileExternally(dir);
            }
            catch
            {
                toasts?.Push("Couldn't open the export folder", EditorTheme.Colours.Error);
            }
        }

        /// <summary>Opens the video export modal (Export menu entry).</summary>
        private void openVideoExportPanel()
        {
            videoExportPanel.TrackLength = track?.Length ?? 0;
            videoExportPanel.Show();
        }

        /// <summary>
        /// Kicks off a video export of the given range/preset. Runs asynchronously; toasts on completion and reveals
        /// the file. Safe to call from the UI thread.
        /// </summary>
        public void StartVideoExport(VideoExportOptions options)
        {
            if (videoExportInProgress)
            {
                toasts?.Push("A video export is already running", EditorTheme.Colours.Warning);
                return;
            }

            if (options.DurationSeconds <= 0)
            {
                toasts?.Push("Nothing to render - the clip is empty", EditorTheme.Colours.Warning);
                return;
            }

            _ = runVideoExportAsync(options);
        }

        private async Task runVideoExportAsync(VideoExportOptions options)
        {
            videoExportInProgress = true;
            var loading = toasts?.PushLoading("Rendering video...", FontAwesome.Solid.Film);

            string ffmpegPath = FfmpegLocator.Resolve();
            string outputDir = videoExportFolder();
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir,
                $"{sanitizeFileName(set.Artist)} - {sanitizeFileName(set.Title)} [{sanitizeFileName(difficulty.DifficultyName)}] {DateTime.Now:yyyyMMdd-HHmmss}.mp4");

            string? error = null;
            string? hitsoundWav = null;
            EncodeResult? result = null;

            try
            {
                await scheduleAsync(() => beginCapture(options)).ConfigureAwait(false);

                AudioSource? audio = audioFilePath != null && File.Exists(audioFilePath)
                    ? new AudioSource(audioFilePath, options.StartTime / 1000.0, options.DurationSeconds, options.Preset.AudioKbps)
                    : null;

                // Bake the map's hitsounds into a wav that ffmpeg mixes over the song (best-effort; song-only on failure).
                if (options.IncludeHitsounds)
                    hitsoundWav = await bakeHitsoundsAsync(options, ffmpegPath).ConfigureAwait(false);

                var encodeSettings = new FfmpegEncodeSettings
                {
                    FfmpegPath = ffmpegPath,
                    OutputPath = outputPath,
                    Width = options.Preset.Width,
                    Height = options.Preset.Height,
                    Fps = options.Preset.Fps,
                    Preset = options.Preset,
                    // Bitrate is budgeted over the real video length, which the speed mod shortens.
                    DurationSeconds = options.OutputSeconds,
                    Audio = audio,
                    HitsoundWavPath = hitsoundWav,
                    PlaybackRate = options.PlaybackRate,
                    NightcorePitch = options.NightcorePitch,
                };

                using var encoder = new FfmpegVideoEncoder(encodeSettings);
                var driver = new VideoCaptureDriver();
                result = await driver.RunAsync(options, encoder, captureFrameAtTime, cancellationToken: CancellationToken.None)
                                     .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                await scheduleAsync(endCapture).ConfigureAwait(false);
                videoExportInProgress = false;

                if (hitsoundWav != null)
                {
                    try { File.Delete(hitsoundWav); } catch { /* temp cleanup */ }
                }
            }

            await scheduleAsync(() =>
            {
                loading?.Dismiss();

                if (error == null && result is { Success: true })
                {
                    toasts?.Push($"Rendered {Path.GetFileName(outputPath)}", EditorTheme.Colours.Success);
                    host.PresentFileExternally(outputPath);
                }
                else
                {
                    string detail = error ?? tailLine(result?.ErrorOutput) ?? "unknown error";
                    toasts?.Push($"Video render failed: {detail}", EditorTheme.Colours.Error);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// The capture delegate handed to <see cref="VideoCaptureDriver"/>: parks the scene at <paramref name="time"/>,
        /// lets one update+draw settle, then grabs the backbuffer. All of this is marshalled onto the update thread.
        /// </summary>
        private async Task<Image<Rgba32>> captureFrameAtTime(double time)
        {
            await scheduleAsync(() => captureTimeMs = time).ConfigureAwait(false);
            // One more scheduled hop guarantees a full update+draw ran at the new time before we read the backbuffer.
            await scheduleAsync(() => { }).ConfigureAwait(false);
            return await requestScreenshot().ConfigureAwait(false);
        }

        private void beginCapture(VideoExportOptions options)
        {
            track?.Stop();

            captureSource = new ManualClock { CurrentTime = options.StartTime };
            captureClock = new FramedClock(captureSource);
            captureClock.ProcessFrame();
            captureTimeMs = options.StartTime;

            playfield.SetClock(captureClock);

            // Apply the requested mods through the editor's own bindables (which recompute CS/AR, flip, fade and
            // rebuild the objects), so the render matches the live mod preview. Restored in endCapture.
            savedHardRock = hardRockMod.Value;
            savedEasy = easyMod.Value;
            savedHidden = hiddenMod.Value;
            hardRockMod.Value = options.HardRock;
            easyMod.Value = options.Easy;
            hiddenMod.Value = options.Hidden;

            playfield.SetPresentationMode(true);

            savedAutoPlay = playfield.AutoPlayEnabled;
            playfield.SetAutoPlay(options.ShowCursor);

            savedComposePadding = composeContainer.Padding;
            composeContainer.Padding = new MarginPadding(0); // playfield fills the window for a clean gameplay frame
            hudLayer.Alpha = 0;

            // The toast overlay lives at the game root (not inside hudLayer), so it would otherwise be captured into
            // the video - e.g. the "Rendering video..." toast itself. Hide it for the duration; the scrubbing gameplay
            // on the window is feedback enough, and the result toast shows once it's restored.
            if (toasts != null)
            {
                savedToastsAlpha = toasts.Alpha;
                toasts.Alpha = 0;
            }

            capturing = true;
        }

        private void endCapture()
        {
            capturing = false;

            composeContainer.Padding = savedComposePadding;
            hudLayer.Alpha = hudHidden ? 0 : 1;
            if (toasts != null)
                toasts.Alpha = savedToastsAlpha;
            playfield.SetPresentationMode(false);
            playfield.SetAutoPlay(savedAutoPlay);

            // Restore the editor's mod preview state.
            hardRockMod.Value = savedHardRock;
            easyMod.Value = savedEasy;
            hiddenMod.Value = savedHidden;

            // Hand the playfield back to the live clock it was using before the export.
            if (visualClock != null)
                playfield.SetClock(visualClock);
            else if (audioClock != null)
                playfield.SetClock(audioClock);

            captureSource = null;
            captureClock = null;
        }

        /// <summary>Runs <paramref name="action"/> on the update thread and completes when it has run.</summary>
        private Task scheduleAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            Schedule(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }

        /// <summary>Requests a backbuffer screenshot from the update thread and returns the captured image.</summary>
        private Task<Image<Rgba32>> requestScreenshot()
        {
            var tcs = new TaskCompletionSource<Task<Image<Rgba32>>>();
            Schedule(() => tcs.SetResult(host.TakeScreenshotAsync()));
            return tcs.Task.Unwrap();
        }

        private static string sanitizeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return string.IsNullOrWhiteSpace(value) ? "untitled" : value;
        }

        /// <summary>The last non-empty line of ffmpeg's error tail, for a compact toast.</summary>
        private static string? tailLine(string? errorOutput)
        {
            if (string.IsNullOrWhiteSpace(errorOutput))
                return null;

            string[] lines = errorOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > 0 ? lines[^1] : null;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Hitsound baking: mixes the map's hitsounds for the clip into a wav that ffmpeg overlays on the song. The
        // sample resolution mirrors HitsoundPlayer ({bank}-{name}{index}, beatmap samples before the default skin);
        // the user skin's own hitsounds are not baked yet.
        // ---------------------------------------------------------------------------------------------------------

        private static readonly string[] sample_extensions = { "", ".wav", ".mp3", ".ogg" };

        /// <summary>Snapshots the in-range hitsound events on the update thread, then bakes them off-thread.</summary>
        private async Task<string?> bakeHitsoundsAsync(VideoExportOptions options, string ffmpegPath)
        {
            var events = new List<SampleEvent>();
            await scheduleAsync(() =>
            {
                foreach (var e in sampleEvents)
                    if (e.Time >= options.StartTime && e.Time <= options.EndTime)
                        events.Add(e);
            }).ConfigureAwait(false);

            if (events.Count == 0)
                return null;

            try
            {
                return bakeHitsoundsWav(events, options.StartTime, options.OutputSeconds, options.PlaybackRate, ffmpegPath);
            }
            catch
            {
                return null; // best-effort: a bake failure just falls back to song-only audio
            }
        }

        private string? bakeHitsoundsWav(List<SampleEvent> events, double startMs, double outputSeconds, double rate, string ffmpegPath)
        {
            bool ignoreBeatmap = settings.IgnoreBeatmapHitsounds.Value;

            IResourceStore<byte[]>? beatmapBytes = null;
            if (set.Files.Count > 0 && !string.IsNullOrEmpty(set.DataDirectory))
            {
                try { beatmapBytes = new BeatmapSampleStore(set.Files, set.DataDirectory); }
                catch { beatmapBytes = null; }
            }

            IResourceStore<byte[]> skinBytes = new NamespacedResourceStore<byte[]>(
                new DllResourceStore(OsuBeatmapEditorResources.ResourceAssembly), "Samples");

            // The active imported skin's own hitsounds (at its folder root), consulted between the beatmap and the
            // bundled default skin - exactly like HitsoundPlayer's live resolution.
            IResourceStore<byte[]>? userSkinBytes = skinManager?.Current.Value?.Resources;

            var pcmCache = new Dictionary<string, float[]?>();
            var mixer = new HitsoundMixer(outputSeconds);

            foreach (var e in events)
            {
                // Compress each hit's time by the speed mod so it lands on the sped-up timeline (samples keep pitch).
                double timeSeconds = (e.Time - startMs) / 1000.0 / rate;
                if (timeSeconds < 0)
                    continue;

                foreach (var look in eventSampleLookups(e, ignoreBeatmap))
                {
                    float[]? pcm = pcmFor(look, ffmpegPath, pcmCache, beatmapBytes, userSkinBytes, skinBytes, ignoreBeatmap);
                    if (pcm != null)
                        mixer.Add(pcm, timeSeconds, e.Volume);
                }
            }

            if (!mixer.HasContent)
                return null;

            string wav = Path.Combine(Path.GetTempPath(), $"sobe-hitsounds-{Guid.NewGuid():N}.wav");
            mixer.WriteWav(wav);
            return wav;
        }

        /// <summary>The samples one event sounds, mirroring <c>HitsoundPlayer.Play</c> / <c>PlaySliderTick</c>.</summary>
        private static IEnumerable<(string key, SampleBank bank, string name, int index, string custom)> eventSampleLookups(SampleEvent e, bool ignoreBeatmap)
        {
            if (e.Kind == SampleEventKind.SliderTick)
            {
                yield return (keyFor(e.Normal, "slidertick", e.Index), e.Normal, "slidertick", e.Index, "");
                yield break;
            }

            // A custom sample filename replaces the normal hitsound (unless beatmap samples are ignored).
            if (!ignoreBeatmap && !string.IsNullOrEmpty(e.Filename))
                yield return ("file:" + e.Filename, SampleBank.Normal, "", 0, e.Filename);
            else
                yield return (keyFor(e.Normal, "hitnormal", e.Index), e.Normal, "hitnormal", e.Index, "");

            if ((e.HitSound & 0b0010) != 0) yield return (keyFor(e.Addition, "hitwhistle", e.Index), e.Addition, "hitwhistle", e.Index, "");
            if ((e.HitSound & 0b0100) != 0) yield return (keyFor(e.Addition, "hitfinish", e.Index), e.Addition, "hitfinish", e.Index, "");
            if ((e.HitSound & 0b1000) != 0) yield return (keyFor(e.Addition, "hitclap", e.Index), e.Addition, "hitclap", e.Index, "");
        }

        private static float[]? pcmFor((string key, SampleBank bank, string name, int index, string custom) look,
                                       string ffmpegPath, Dictionary<string, float[]?> cache,
                                       IResourceStore<byte[]>? beatmapBytes, IResourceStore<byte[]>? userSkinBytes,
                                       IResourceStore<byte[]> skinBytes, bool ignoreBeatmap)
        {
            if (cache.TryGetValue(look.key, out float[]? cached))
                return cached;

            byte[]? bytes = look.custom.Length > 0
                ? getSampleBytes(look.custom, beatmapBytes, userSkinBytes, skinBytes, ignoreBeatmap)
                : resolveBankBytes(look.bank, look.name, look.index, beatmapBytes, userSkinBytes, skinBytes, ignoreBeatmap);

            float[]? pcm = bytes != null ? AudioDecoder.Decode(ffmpegPath, bytes) : null;
            cache[look.key] = pcm;
            return pcm;
        }

        /// <summary>Resolves <c>{bank}-{name}{index}</c> (suffix only for index ≥ 2, then the un-suffixed name).</summary>
        private static byte[]? resolveBankBytes(SampleBank bank, string name, int index,
                                                IResourceStore<byte[]>? beatmap, IResourceStore<byte[]>? userSkin,
                                                IResourceStore<byte[]> skin, bool ignoreBeatmap)
        {
            string b = bankName(bank);

            if (index >= 2)
            {
                byte[]? suffixed = getSampleBytes($"{b}-{name}{index}", beatmap, userSkin, skin, ignoreBeatmap);
                if (suffixed != null)
                    return suffixed;
            }

            return getSampleBytes($"{b}-{name}", beatmap, userSkin, skin, ignoreBeatmap);
        }

        /// <summary>
        /// Beatmap samples first (root), then the active imported skin (its sounds sit at the folder root), then the
        /// bundled default skin (under <c>Gameplay/</c>) - the same order HitsoundPlayer uses live.
        /// </summary>
        private static byte[]? getSampleBytes(string baseName, IResourceStore<byte[]>? beatmap,
                                              IResourceStore<byte[]>? userSkin, IResourceStore<byte[]> skin, bool ignoreBeatmap)
        {
            if (!ignoreBeatmap)
            {
                byte[]? own = tryStore(beatmap, baseName);
                if (own != null)
                    return own;
            }

            byte[]? fromSkin = tryStore(userSkin, baseName);
            if (fromSkin != null)
                return fromSkin;

            return tryStore(skin, "Gameplay/" + baseName);
        }

        private static byte[]? tryStore(IResourceStore<byte[]>? store, string keyBase)
        {
            if (store == null)
                return null;

            foreach (string ext in sample_extensions)
            {
                try
                {
                    byte[]? bytes = store.Get(keyBase + ext);
                    if (bytes is { Length: > 0 })
                        return bytes;
                }
                catch
                {
                    // missing resource for this extension - try the next
                }
            }

            return null;
        }

        private static string keyFor(SampleBank bank, string name, int index) => $"{bankName(bank)}-{name}-{index}";

        private static string bankName(SampleBank bank) => bank switch
        {
            SampleBank.Soft => "soft",
            SampleBank.Drum => "drum",
            _ => "normal",
        };
    }
}
