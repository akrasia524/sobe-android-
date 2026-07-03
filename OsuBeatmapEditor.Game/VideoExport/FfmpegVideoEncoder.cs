using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>Optional audio to mux into the video: a slice of the map's song file.</summary>
    public sealed record AudioSource(string FilePath, double StartSeconds, double DurationSeconds, int Kbps = 128);

    /// <summary>Everything the encoder needs for one run. The renderer fills this from a <see cref="VideoExportOptions"/>.</summary>
    public sealed record FfmpegEncodeSettings
    {
        public required string FfmpegPath { get; init; }
        public required string OutputPath { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int Fps { get; init; }

        /// <summary>Drives the bitrate strategy (CRF vs size budget).</summary>
        public required VideoExportPreset Preset { get; init; }

        /// <summary>Clip length in seconds - used to derive the bitrate for size-budget presets.</summary>
        public required double DurationSeconds { get; init; }

        /// <summary>Song slice to mux in, or null for a silent video.</summary>
        public AudioSource? Audio { get; init; }

        /// <summary>Pre-baked hitsound track (.wav, clip-length, starts at 0) to mix over the song. Null = no hitsounds.</summary>
        public string? HitsoundWavPath { get; init; }

        /// <summary>Song speed multiplier (1.5 for DT/NC). The song is time-stretched to match the sped-up video.</summary>
        public double PlaybackRate { get; init; } = 1.0;

        /// <summary>Whether the sped-up song also rises in pitch (Nightcore). False = pitch preserved (DoubleTime).</summary>
        public bool NightcorePitch { get; init; }

        /// <summary>x264 speed/size trade-off preset. Slower = smaller/better at the same quality.</summary>
        public string X264Preset { get; init; } = "medium";
    }

    /// <summary>Outcome of an encode run.</summary>
    public sealed record EncodeResult(bool Success, string OutputPath, string ErrorOutput);

    /// <summary>
    /// Streams raw RGBA frames into an ffmpeg process over stdin and produces an H.264 .mp4. One frame is
    /// <c>Width * Height * 4</c> bytes, top row first (matching ImageSharp's layout). Feed every frame with
    /// <see cref="WriteFrame"/>, then call <see cref="CompleteAsync"/> to flush and finish.
    ///
    /// stderr is drained on a background task so a chatty ffmpeg can't dead-lock the stdin pipe; the tail is kept
    /// for error reporting. This class is single-producer: call <see cref="WriteFrame"/> from one thread only.
    /// </summary>
    public sealed class FfmpegVideoEncoder : IDisposable
    {
        private readonly Process process;
        private readonly Stream stdin;
        private readonly int frameBytes;
        private readonly string outputPath;
        private readonly Task<string> stderrTail;

        private bool broken;
        private bool completed;

        public FfmpegVideoEncoder(FfmpegEncodeSettings settings)
        {
            if (settings.Width % 2 != 0 || settings.Height % 2 != 0)
                throw new ArgumentException("H.264 requires even width and height.");

            frameBytes = settings.Width * settings.Height * 4;
            outputPath = settings.OutputPath;

            var info = new ProcessStartInfo(settings.FfmpegPath)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string arg in buildArguments(settings))
                info.ArgumentList.Add(arg);

            process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start ffmpeg at '{settings.FfmpegPath}'.");
            stdin = process.StandardInput.BaseStream;

            // Drain stderr continuously; keep only the tail so a long run doesn't hold megabytes of logs.
            stderrTail = Task.Run(async () =>
            {
                var tail = new Queue<string>();
                string? line;
                while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    tail.Enqueue(line);
                    while (tail.Count > 40)
                        tail.Dequeue();
                }
                return string.Join('\n', tail);
            });
        }

        /// <summary>Whether ffmpeg is still accepting frames. Goes false if the process dies mid-stream.</summary>
        public bool IsAlive => !broken && !completed;

        /// <summary>
        /// Writes one RGBA frame. The span length must be exactly <c>Width * Height * 4</c>. If ffmpeg has already
        /// died (broken pipe), the frame is silently dropped and <see cref="IsAlive"/> becomes false - the real error
        /// surfaces from <see cref="CompleteAsync"/>.
        /// </summary>
        public void WriteFrame(ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length != frameBytes)
                throw new ArgumentException($"Frame must be {frameBytes} bytes ({frameBytes / 4} pixels), got {rgba.Length}.");

            if (!IsAlive)
                return;

            try
            {
                stdin.Write(rgba);
            }
            catch (IOException)
            {
                // Broken pipe: ffmpeg exited early. Stop writing; CompleteAsync will report why.
                broken = true;
            }
        }

        /// <summary>Closes the input stream, waits for ffmpeg to finish, and reports success plus any error tail.</summary>
        public async Task<EncodeResult> CompleteAsync()
        {
            completed = true;

            try
            {
                stdin.Flush();
                stdin.Close();
            }
            catch (IOException)
            {
                // Already broken; the exit code / stderr below tells the story.
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            string errors = await stderrTail.ConfigureAwait(false);

            bool success = process.ExitCode == 0 && File.Exists(outputPath);
            return new EncodeResult(success, outputPath, errors);
        }

        private static IEnumerable<string> buildArguments(FfmpegEncodeSettings s)
        {
            var args = new List<string>
            {
                "-y",
                "-hide_banner",
                "-nostats",
                "-loglevel", "error",

                // Raw RGBA frames arrive on stdin.
                "-f", "rawvideo",
                "-pix_fmt", "rgba",
                "-video_size", $"{s.Width}x{s.Height}",
                "-framerate", s.Fps.ToString(CultureInfo.InvariantCulture),
                "-i", "pipe:0",
            };

            // Audio inputs (after the video pipe = input 0). Track each one's input index for the mapping below.
            int nextInput = 1;
            int songIndex = -1;
            int hitsoundIndex = -1;

            if (s.Audio != null)
            {
                // Input-seek the song slice before decoding: fast and accurate enough for a clip.
                args.Add("-ss");
                args.Add(s.Audio.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                args.Add("-t");
                args.Add(s.Audio.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                args.Add("-i");
                args.Add(s.Audio.FilePath);
                songIndex = nextInput++;
            }

            if (s.HitsoundWavPath != null)
            {
                args.Add("-i");
                args.Add(s.HitsoundWavPath);
                hitsoundIndex = nextInput++;
            }

            args.Add("-map");
            args.Add("0:v:0");

            bool hasAudio = songIndex >= 0 || hitsoundIndex >= 0;

            // DT/NC time-stretch the song to match the sped-up video. Hitsounds are baked already re-timed, so only
            // the song gets the filter.
            string? songFilter = buildSongRateFilter(s.PlaybackRate, s.NightcorePitch);

            if (songIndex >= 0 && hitsoundIndex >= 0)
            {
                // Mix the (optionally sped-up) song and the baked hitsounds. normalize=0 keeps each at full level.
                string songLabel = songFilter != null ? "[sf]" : $"[{songIndex}:a]";
                string graph = songFilter != null ? $"[{songIndex}:a]{songFilter}[sf];" : string.Empty;
                graph += $"{songLabel}[{hitsoundIndex}:a]amix=inputs=2:normalize=0:duration=first[aout]";

                args.Add("-filter_complex");
                args.Add(graph);
                args.Add("-map");
                args.Add("[aout]");
            }
            else if (songIndex >= 0 && songFilter != null)
            {
                args.Add("-filter_complex");
                args.Add($"[{songIndex}:a]{songFilter}[aout]");
                args.Add("-map");
                args.Add("[aout]");
            }
            else if (songIndex >= 0)
            {
                args.Add("-map");
                args.Add($"{songIndex}:a:0");
            }
            else if (hitsoundIndex >= 0)
            {
                args.Add("-map");
                args.Add($"{hitsoundIndex}:a:0");
            }

            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add(s.X264Preset);
            args.Add("-pix_fmt");
            args.Add("yuv420p"); // widest player/Discord compatibility

            if (s.Preset.SizeMode == VideoSizeMode.SizeBudget)
            {
                int kbps = s.Preset.ComputeVideoKbps(s.DurationSeconds);
                // Near-CBR so the file reliably lands under the target size.
                args.Add("-b:v");
                args.Add($"{kbps}k");
                args.Add("-maxrate");
                args.Add($"{kbps}k");
                args.Add("-bufsize");
                args.Add($"{kbps * 2}k");
            }
            else
            {
                args.Add("-crf");
                args.Add(s.Preset.Crf.ToString(CultureInfo.InvariantCulture));
            }

            if (hasAudio)
            {
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add($"{(s.Audio?.Kbps ?? 192)}k");
            }

            args.Add("-r");
            args.Add(s.Fps.ToString(CultureInfo.InvariantCulture));
            args.Add("-movflags");
            args.Add("+faststart");
            args.Add("-shortest");
            args.Add(s.OutputPath);

            return args;
        }

        /// <summary>
        /// The ffmpeg audio filter that speeds the song up to <paramref name="rate"/>. DoubleTime keeps the pitch
        /// (<c>atempo</c>); Nightcore raises it by resampling (<c>asetrate</c>, normalised to 48 kHz first so it's
        /// independent of the source sample rate). Null when there's no speed mod.
        /// </summary>
        private static string? buildSongRateFilter(double rate, bool nightcorePitch)
        {
            if (Math.Abs(rate - 1.0) < 0.001)
                return null;

            if (nightcorePitch)
            {
                int stretched = (int)Math.Round(48000 * rate);
                return $"aresample=48000,asetrate={stretched},aresample=48000";
            }

            return $"atempo={rate.ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    try { stdin.Close(); } catch { /* ignore */ }
                    if (!process.WaitForExit(2000))
                        process.Kill(true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
