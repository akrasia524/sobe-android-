using System;
using System.Diagnostics;
using System.IO;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Decodes an audio file's bytes (wav/mp3/ogg - whatever a hitsound sample is packed as) into interleaved-stereo
    /// float PCM at a fixed sample rate, via ffmpeg. The bytes are written to a temp file and ffmpeg streams raw
    /// <c>f32le</c> to stdout; this sidesteps the two-way stdin/stdout pipe dance for small sample files.
    /// </summary>
    public static class AudioDecoder
    {
        public static float[]? Decode(string ffmpegPath, byte[] input, int sampleRate = 44100)
        {
            if (input.Length == 0)
                return null;

            string tempIn = Path.Combine(Path.GetTempPath(), $"sobe-sample-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(tempIn, input);

                var info = new ProcessStartInfo(ffmpegPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (string arg in new[]
                {
                    "-v", "error",
                    "-i", tempIn,
                    "-f", "f32le",
                    "-acodec", "pcm_f32le",
                    "-ac", "2",
                    "-ar", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "pipe:1",
                })
                    info.ArgumentList.Add(arg);

                using var process = Process.Start(info);
                if (process == null)
                    return null;

                using var outStream = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(outStream);
                process.StandardError.ReadToEnd(); // drain so ffmpeg can't block on a full stderr pipe
                process.WaitForExit();

                if (process.ExitCode != 0)
                    return null;

                byte[] raw = outStream.ToArray();
                var floats = new float[raw.Length / sizeof(float)];
                Buffer.BlockCopy(raw, 0, floats, 0, floats.Length * sizeof(float));
                return floats;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(tempIn); } catch { /* best effort */ }
            }
        }
    }
}
