using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using OsuBeatmapEditor.Game.VideoExport;

namespace OsuBeatmapEditor.Game.Tests.VideoExport
{
    /// <summary>
    /// Smoke test for the encoding half of the video exporter - no editor / GameHost involved. Generates a moving
    /// gradient, pipes it through <see cref="FfmpegVideoEncoder"/> and checks a playable .mp4 comes out. The file is
    /// left on disk so it can be opened manually (and dropped into Discord to confirm the format is accepted).
    ///
    /// Run with: <c>dotnet test --filter FfmpegVideoEncoderTests</c>. Skips itself if ffmpeg is not installed.
    /// </summary>
    [TestFixture]
    public class FfmpegVideoEncoderTests
    {
        [Test]
        public async Task EncodesSyntheticFramesToPlayableMp4()
        {
            string ffmpeg = FfmpegLocator.Resolve();
            if (!FfmpegLocator.IsAvailable(ffmpeg))
                Assert.Ignore("ffmpeg not found. Install it to run this test (macOS: `brew install ffmpeg`).");

            const int width = 1280;
            const int height = 720;
            const int fps = 30;
            const int seconds = 3;

            string outPath = Path.Combine(Path.GetTempPath(), $"sobe-video-smoketest-{Guid.NewGuid():N}.mp4");

            var settings = new FfmpegEncodeSettings
            {
                FfmpegPath = ffmpeg,
                OutputPath = outPath,
                Width = width,
                Height = height,
                Fps = fps,
                Preset = VideoExportPreset.Quality720p,
                DurationSeconds = seconds,
                // Audio omitted: this test only validates the video path.
            };

            byte[] frame = new byte[width * height * 4];
            int totalFrames = fps * seconds;

            EncodeResult result;
            using (var encoder = new FfmpegVideoEncoder(settings))
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    fillGradient(frame, width, height, i, totalFrames);
                    encoder.WriteFrame(frame);
                }

                result = await encoder.CompleteAsync();
            }

            Assert.That(result.Success, Is.True, $"ffmpeg failed:\n{result.ErrorOutput}");
            Assert.That(File.Exists(outPath), Is.True, "output file was not created");
            Assert.That(new FileInfo(outPath).Length, Is.GreaterThan(10_000), "output file is suspiciously small");

            TestContext.Out.WriteLine($"OK - wrote {new FileInfo(outPath).Length / 1024} KB to:\n{outPath}");
        }

        [Test]
        public void DiscordPresetBitrateFitsBudget()
        {
            // A 90-second clip under the 25 MB Discord budget must resolve to a positive, sane bitrate.
            int kbps = VideoExportPreset.DiscordFree.ComputeVideoKbps(90);
            Assert.That(kbps, Is.GreaterThan(200));

            // The whole file (video + audio) must fit under the hard limit with the built-in margin.
            double totalKbits = (kbps + VideoExportPreset.DiscordFree.AudioKbps) * 90.0;
            long estimatedBytes = (long)(totalKbits * 1000.0 / 8.0);
            Assert.That(estimatedBytes, Is.LessThan(VideoExportPreset.DiscordFree.TargetBytes));
        }

        /// <summary>Fills the buffer with an animated diagonal gradient so motion is visible in the output.</summary>
        private static void fillGradient(byte[] rgba, int width, int height, int frameIndex, int totalFrames)
        {
            float t = (float)frameIndex / totalFrames;
            int shift = (int)(t * 255);

            int idx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    rgba[idx++] = (byte)((x * 255 / width + shift) & 0xFF);       // R
                    rgba[idx++] = (byte)((y * 255 / height + shift) & 0xFF);      // G
                    rgba[idx++] = (byte)(((x + y) * 255 / (width + height)) & 0xFF); // B
                    rgba[idx++] = 255;                                            // A
                }
            }
        }
    }
}
