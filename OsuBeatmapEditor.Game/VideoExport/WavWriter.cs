using System;
using System.IO;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Writes an interleaved-stereo float buffer to a 16-bit PCM .wav file. Used for the baked hitsound track that
    /// ffmpeg then mixes with the song. Samples are clamped to [-1, 1] before quantising.
    /// </summary>
    public static class WavWriter
    {
        public static void Write(string path, float[] interleavedStereo, int sampleRate)
        {
            const int channels = 2;
            const int bitsPerSample = 16;

            int frameCount = interleavedStereo.Length; // total samples across both channels
            int dataBytes = frameCount * (bitsPerSample / 8);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = channels * (bitsPerSample / 8);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(stream);

            // RIFF header
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + dataBytes);
            w.Write("WAVE"u8.ToArray());

            // fmt chunk
            w.Write("fmt "u8.ToArray());
            w.Write(16);                 // PCM chunk size
            w.Write((short)1);           // PCM format
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write(blockAlign);
            w.Write((short)bitsPerSample);

            // data chunk
            w.Write("data"u8.ToArray());
            w.Write(dataBytes);

            foreach (float sample in interleavedStereo)
            {
                float clamped = Math.Clamp(sample, -1f, 1f);
                w.Write((short)Math.Round(clamped * short.MaxValue));
            }
        }
    }
}
