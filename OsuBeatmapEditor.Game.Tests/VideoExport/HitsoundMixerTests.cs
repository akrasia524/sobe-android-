using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using OsuBeatmapEditor.Game.VideoExport;

namespace OsuBeatmapEditor.Game.Tests.VideoExport
{
    [TestFixture]
    public class HitsoundMixerTests
    {
        [Test]
        public void WavWriterProducesWellFormedHeader()
        {
            float[] samples = new float[44100 * 2]; // 1s stereo of silence
            string path = Path.Combine(Path.GetTempPath(), $"sobe-wav-{Guid.NewGuid():N}.wav");

            try
            {
                WavWriter.Write(path, samples, 44100);

                byte[] bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length, Is.EqualTo(44 + samples.Length * 2), "header (44 bytes) + 16-bit samples");
                Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
                Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("WAVE"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void DecodesAndMixesASampleIntoTheTrack()
        {
            string ffmpeg = FfmpegLocator.Resolve();
            if (!FfmpegLocator.IsAvailable(ffmpeg))
                Assert.Ignore("ffmpeg not found.");

            // Make a short tone as a stand-in hitsound sample, then decode it.
            byte[] sampleBytes = makeToneWav(ffmpeg, 0.1);
            float[]? pcm = AudioDecoder.Decode(ffmpeg, sampleBytes);

            Assert.That(pcm, Is.Not.Null);
            Assert.That(pcm!.Length, Is.EqualTo(4410 * 2).Within(200), "~0.1s of 44.1k stereo");
            Assert.That(Array.Exists(pcm, s => Math.Abs(s) > 0.01f), Is.True, "tone should be audible");

            // Mix it in at 0.5s of a 2s track and confirm energy lands there, not at the start.
            var mixer = new HitsoundMixer(2.0);
            mixer.Add(pcm, 0.5, 1f);
            Assert.That(mixer.HasContent, Is.True);

            string wav = Path.Combine(Path.GetTempPath(), $"sobe-mix-{Guid.NewGuid():N}.wav");
            try
            {
                mixer.WriteWav(wav);
                float[]? back = AudioDecoder.Decode(ffmpeg, File.ReadAllBytes(wav));
                Assert.That(back, Is.Not.Null);

                Assert.That(energyAround(back!, 0.0), Is.LessThan(0.001f), "start should be silent");
                Assert.That(energyAround(back!, 0.5), Is.GreaterThan(0.01f), "energy at 0.5s");
            }
            finally
            {
                File.Delete(wav);
            }
        }

        // Average absolute amplitude in a 20ms window around the given second.
        private static float energyAround(float[] stereo, double seconds)
        {
            int center = (int)(seconds * 44100) * 2;
            int half = (int)(0.01 * 44100) * 2;
            int start = Math.Max(0, center - half);
            int end = Math.Min(stereo.Length, center + half);

            float sum = 0;
            for (int i = start; i < end; i++)
                sum += Math.Abs(stereo[i]);
            return end > start ? sum / (end - start) : 0;
        }

        private static byte[] makeToneWav(string ffmpeg, double seconds)
        {
            string path = Path.Combine(Path.GetTempPath(), $"sobe-tone-{Guid.NewGuid():N}.wav");
            var info = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (string a in new[]
            {
                "-v", "error", "-y",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "-ac", "2", "-ar", "44100",
                path,
            })
                info.ArgumentList.Add(a);

            using (var p = Process.Start(info)!)
            {
                p.StandardError.ReadToEnd();
                p.WaitForExit();
            }

            byte[] bytes = File.ReadAllBytes(path);
            File.Delete(path);
            return bytes;
        }
    }
}
