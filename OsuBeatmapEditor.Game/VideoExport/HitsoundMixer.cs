using System;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Sums decoded hitsound samples into a single interleaved-stereo float track, each placed at its event time and
    /// scaled by the event's volume - exactly mirroring the live editor, which sets <c>channel.Volume = volume</c> per
    /// hit. The result is written to a .wav that ffmpeg mixes with the song. The buffer has a one-second tail so a
    /// sample landing right at the clip end isn't truncated.
    /// </summary>
    public sealed class HitsoundMixer
    {
        private readonly float[] buffer;
        private readonly int sampleRate;
        private bool any;

        public HitsoundMixer(double durationSeconds, int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            int frames = (int)Math.Ceiling(Math.Max(0, durationSeconds) * sampleRate) + sampleRate;
            buffer = new float[frames * 2];
        }

        /// <summary>Whether any sample has actually been mixed in (so the caller can skip an empty track).</summary>
        public bool HasContent => any;

        /// <summary>
        /// Adds one decoded sample (interleaved stereo, same sample rate as the mixer) at <paramref name="timeSeconds"/>
        /// from the clip start, scaled by <paramref name="gain"/>. Out-of-range portions are clipped away silently.
        /// </summary>
        public void Add(float[] sampleStereo, double timeSeconds, float gain)
        {
            if (sampleStereo.Length == 0 || gain <= 0 || timeSeconds < 0)
                return;

            int destStart = (int)Math.Round(timeSeconds * sampleRate) * 2;
            if (destStart >= buffer.Length)
                return;

            int count = Math.Min(sampleStereo.Length, buffer.Length - destStart);
            for (int i = 0; i < count; i++)
                buffer[destStart + i] += sampleStereo[i] * gain;

            any = true;
        }

        public void WriteWav(string path) => WavWriter.Write(path, buffer, sampleRate);
    }
}
