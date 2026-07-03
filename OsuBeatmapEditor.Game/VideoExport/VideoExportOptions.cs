using System;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// One export request: the chosen <see cref="VideoExportPreset"/> (resolution / fps / bitrate), the time range
    /// to render, and the scene-content toggles that decide *what* gets drawn - as opposed to the preset, which only
    /// decides *how* the frames are encoded.
    ///
    /// The renderer reads <see cref="ShowCursor"/> to include or omit the auto cursor drawable in the offscreen
    /// gameplay scene; the encoder reads <see cref="VideoExportPreset.ComputeVideoKbps"/> with
    /// <see cref="DurationSeconds"/> for the size-budget presets.
    /// </summary>
    public sealed record VideoExportOptions
    {
        /// <summary>Output resolution / fps / bitrate strategy.</summary>
        public required VideoExportPreset Preset { get; init; }

        /// <summary>Start of the rendered clip, in map time (ms).</summary>
        public required double StartTime { get; init; }

        /// <summary>End of the rendered clip, in map time (ms). Must be greater than <see cref="StartTime"/>.</summary>
        public required double EndTime { get; init; }

        /// <summary>
        /// Whether the auto cursor (and its trail) is drawn into the video. Off renders a clean "hit objects only"
        /// clip. Default on.
        /// </summary>
        public bool ShowCursor { get; init; } = true;

        /// <summary>
        /// Whether the map's hitsounds are baked into the audio (mixed over the song). Off = song only. Default on.
        /// </summary>
        public bool IncludeHitsounds { get; init; } = true;

        /// <summary>Render as HardRock (flipped, harder CS/AR). Mutually exclusive with <see cref="Easy"/>.</summary>
        public bool HardRock { get; init; }

        /// <summary>Render as Easy (bigger circles, slower approach). Mutually exclusive with <see cref="HardRock"/>.</summary>
        public bool Easy { get; init; }

        /// <summary>Render as Hidden (objects fade out, no approach circles). Mixes with anything.</summary>
        public bool Hidden { get; init; }

        /// <summary>DoubleTime: 1.5x speed, pitch preserved. Mutually exclusive with <see cref="Nightcore"/>.</summary>
        public bool DoubleTime { get; init; }

        /// <summary>Nightcore: 1.5x speed with raised pitch. Mutually exclusive with <see cref="DoubleTime"/>.</summary>
        public bool Nightcore { get; init; }

        // Future scene toggles land here (key overlay, follow points, etc.) - kept separate from the preset on purpose.

        /// <summary>Map-time length in ms of the selected range (before any speed mod).</summary>
        public double RangeMs => Math.Max(0, EndTime - StartTime);

        /// <summary>Playback speed multiplier: 1.5x under DoubleTime/Nightcore, else 1.0.</summary>
        public double PlaybackRate => DoubleTime || Nightcore ? 1.5 : 1.0;

        /// <summary>Whether the song's pitch is raised with the speed-up (Nightcore only).</summary>
        public bool NightcorePitch => Nightcore;

        /// <summary>Length of the selected range in seconds (map time, before speed mod).</summary>
        public double DurationSeconds => RangeMs / 1000.0;

        /// <summary>Real length of the rendered video in seconds - the range compressed by the speed mod.</summary>
        public double OutputSeconds => DurationSeconds / PlaybackRate;
    }
}
