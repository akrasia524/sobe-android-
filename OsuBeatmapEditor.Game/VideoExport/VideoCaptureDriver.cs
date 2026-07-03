using System;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Orchestrates the offline, deterministic render loop: for each frame it asks for the gameplay drawn at an
    /// exact map time, converts it to a raw RGBA frame and feeds it to the encoder. The clock advances by a fixed
    /// <c>1000/fps</c> per frame regardless of wall-clock time, so no frame is ever dropped and the output is always
    /// smooth at the target fps (this is the whole point of "Option B").
    ///
    /// All the framework/threading glue - driving the manual clock and reading back the backbuffer on the right
    /// threads - lives behind the injected <c>captureFrameAtTime</c> delegate, so this class stays framework-agnostic
    /// and testable. <see cref="FfmpegVideoEncoder"/> does the encoding; this class only sequences the frames.
    /// </summary>
    public sealed class VideoCaptureDriver
    {
        /// <summary>
        /// Renders the requested range to the encoder.
        /// </summary>
        /// <param name="options">The clip range, preset (resolution/fps) and scene toggles.</param>
        /// <param name="encoder">A started encoder configured for the same resolution/fps as <paramref name="options"/>.</param>
        /// <param name="captureFrameAtTime">
        /// Sets the gameplay clock to the given map time (ms) and returns the drawn frame. The returned image is
        /// disposed by this method. Implemented by the editor using the game host + a manual clock.
        /// </param>
        /// <param name="progress">Receives 0..1 progress after each frame.</param>
        /// <param name="cancellationToken">Cancels the render; the partial file is finalised/cleaned by the caller.</param>
        public async Task<EncodeResult> RunAsync(
            VideoExportOptions options,
            FfmpegVideoEncoder encoder,
            Func<double, Task<Image<Rgba32>>> captureFrameAtTime,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int width = options.Preset.Width;
            int height = options.Preset.Height;
            int fps = options.Preset.Fps;

            // Each video frame advances map time by rate/fps: under DoubleTime/Nightcore (rate 1.5) the clock steps
            // faster, so fewer frames cover the same range and the video plays back sped up. rate 1.0 = normal.
            double mapStepMs = options.PlaybackRate * 1000.0 / fps;
            int totalFrames = Math.Max(1, (int)Math.Ceiling(options.RangeMs / mapStepMs));

            byte[] frameBuffer = new byte[width * height * 4];

            for (int i = 0; i < totalFrames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double time = options.StartTime + i * mapStepMs;

                using (Image<Rgba32> frame = await captureFrameAtTime(time).ConfigureAwait(false))
                    FrameConverter.CopyResized(frame, width, height, frameBuffer);

                encoder.WriteFrame(frameBuffer);

                if (!encoder.IsAlive)
                    break; // ffmpeg died; CompleteAsync will surface the error

                progress?.Report((i + 1.0) / totalFrames);
            }

            return await encoder.CompleteAsync().ConfigureAwait(false);
        }
    }
}
