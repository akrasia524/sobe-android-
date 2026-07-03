using System;
using System.Collections.Generic;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// How a preset decides the video bitrate / file size.
    /// </summary>
    public enum VideoSizeMode
    {
        /// <summary>Constant quality (ffmpeg CRF). File size varies with the content; used by the quality presets.</summary>
        Quality,

        /// <summary>
        /// Fit the whole clip under a fixed file-size budget. The video bitrate is derived from the clip duration
        /// (see <see cref="VideoExportPreset.ComputeVideoKbps"/>) and encoded in two passes so the result reliably
        /// lands under <see cref="VideoExportPreset.TargetBytes"/>. This is what the Discord presets use.
        /// </summary>
        SizeBudget,
    }

    /// <summary>
    /// A named set of output settings for the offline video renderer: target resolution, frame rate and the
    /// strategy used to pick the bitrate. Presets are the user-facing knob; the renderer and the ffmpeg encoder
    /// only ever read these values, never hard-code them.
    ///
    /// Two families:
    /// <list type="bullet">
    /// <item><b>Quality</b> presets (720p/1080p) encode at a constant CRF - file size follows the content.</item>
    /// <item><b>Size-budget</b> presets (Discord) target a fixed <see cref="TargetBytes"/> and compute the video
    /// bitrate from the clip length so the file always fits the upload limit.</item>
    /// </list>
    /// </summary>
    public sealed record VideoExportPreset
    {
        /// <summary>Short label shown in the preset picker, e.g. "Discord (25 MB)".</summary>
        public required string Name { get; init; }

        /// <summary>One-line explanation shown under the name in the picker.</summary>
        public required string Description { get; init; }

        /// <summary>Output width in pixels (16:9 gameplay frame). Must be even for H.264.</summary>
        public required int Width { get; init; }

        /// <summary>Output height in pixels. Must be even for H.264.</summary>
        public required int Height { get; init; }

        /// <summary>Frames rendered (and encoded) per second of video.</summary>
        public required int Fps { get; init; }

        /// <summary>Whether the bitrate comes from a quality target or a file-size budget.</summary>
        public required VideoSizeMode SizeMode { get; init; }

        /// <summary>Audio bitrate in kbps (AAC). Reserved out of the budget before the video gets its share.</summary>
        public int AudioKbps { get; init; } = 128;

        /// <summary>
        /// <see cref="VideoSizeMode.Quality"/> only: x264 CRF (0 = lossless, ~18 = visually lossless, 23 = default).
        /// Lower is better quality and a bigger file.
        /// </summary>
        public int Crf { get; init; } = 20;

        /// <summary>
        /// <see cref="VideoSizeMode.SizeBudget"/> only: the file-size ceiling in bytes the whole clip must fit under.
        /// </summary>
        public long TargetBytes { get; init; }

        /// <summary>
        /// For a size-budget preset, the video bitrate (kbps) needed to fit <paramref name="durationSeconds"/> of clip
        /// under <see cref="TargetBytes"/>, after reserving the audio track and a small muxing-overhead margin.
        /// Clamped to a sane floor so extremely long clips still produce a (low-quality) playable file.
        /// </summary>
        public int ComputeVideoKbps(double durationSeconds)
        {
            if (SizeMode != VideoSizeMode.SizeBudget)
                throw new InvalidOperationException("Video bitrate is only computed for size-budget presets.");

            if (durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));

            // Leave ~3% headroom for container overhead so we land *under*, never over, the hard limit.
            const double container_margin = 0.97;

            double budgetKbits = TargetBytes * 8.0 / 1000.0 * container_margin;
            double audioKbits = AudioKbps * durationSeconds;
            double videoKbps = (budgetKbits - audioKbits) / durationSeconds;

            return (int)Math.Max(200, Math.Floor(videoKbps));
        }

        /// <summary>
        /// Rough estimate of the output file size in bytes for a clip of <paramref name="durationSeconds"/>. Accurate
        /// for size-budget presets (near-CBR, so ≈ the target); a ballpark for quality presets, whose size depends on
        /// the content.
        /// </summary>
        public long EstimateBytes(double durationSeconds)
        {
            if (durationSeconds <= 0)
                return 0;

            int videoKbps = SizeMode == VideoSizeMode.SizeBudget
                ? ComputeVideoKbps(durationSeconds)
                : nominalQualityVideoKbps();

            double totalKbits = (videoKbps + AudioKbps) * durationSeconds;
            return (long)(totalKbits * 1000.0 / 8.0);
        }

        // Ballpark H.264 bitrate for this resolution/fps (~0.09 bits per pixel). Content-dependent, hence "estimate".
        private int nominalQualityVideoKbps() => (int)(Width * (long)Height * Fps * 0.09 / 1000.0);

        // Common Discord upload ceilings (decimal MB, matching how Discord states them). Targets sit a touch under
        // each hard limit so container overhead can't push the file over.
        private const long discord_free_target = 9_800_000;     // 10 MB free tier
        private const long discord_nitro_basic = 49_000_000;    // 50 MB Nitro Basic
        private const long discord_nitro = 490_000_000;         // 500 MB Nitro

        /// <summary>The preset selected by default when the export panel opens.</summary>
        public static VideoExportPreset Default => DiscordFree;

        /// <summary>Guarantees the clip fits Discord's free 10 MB upload limit at 720p30.</summary>
        public static readonly VideoExportPreset DiscordFree = new()
        {
            Name = "Discord (10 MB)",
            Description = "Fits Discord's free upload limit - 720p, auto bitrate",
            Width = 1280,
            Height = 720,
            Fps = 30,
            SizeMode = VideoSizeMode.SizeBudget,
            TargetBytes = discord_free_target,
            AudioKbps = 128,
        };

        /// <summary>Fits Discord Nitro Basic (50 MB) at 1080p30.</summary>
        public static readonly VideoExportPreset DiscordNitroBasic = new()
        {
            Name = "Discord Nitro Basic (50 MB)",
            Description = "Fits the 50 MB Nitro Basic limit - 1080p, auto bitrate",
            Width = 1920,
            Height = 1080,
            Fps = 30,
            SizeMode = VideoSizeMode.SizeBudget,
            TargetBytes = discord_nitro_basic,
            AudioKbps = 160,
        };

        /// <summary>Fits Discord Nitro (500 MB) at 1080p60 - effectively high quality.</summary>
        public static readonly VideoExportPreset DiscordNitro = new()
        {
            Name = "Discord Nitro (500 MB)",
            Description = "Fits the 500 MB Nitro limit - 1080p60, high quality",
            Width = 1920,
            Height = 1080,
            Fps = 60,
            SizeMode = VideoSizeMode.SizeBudget,
            TargetBytes = discord_nitro,
            AudioKbps = 192,
        };

        /// <summary>Constant-quality 720p30 - no size cap, good for local keeps.</summary>
        public static readonly VideoExportPreset Quality720p = new()
        {
            Name = "720p 30fps",
            Description = "Constant quality, no size limit",
            Width = 1280,
            Height = 720,
            Fps = 30,
            SizeMode = VideoSizeMode.Quality,
            Crf = 21,
        };

        /// <summary>Constant-quality 1080p60 - no size cap, best local quality.</summary>
        public static readonly VideoExportPreset Quality1080p60 = new()
        {
            Name = "1080p 60fps",
            Description = "Constant quality, no size limit",
            Width = 1920,
            Height = 1080,
            Fps = 60,
            SizeMode = VideoSizeMode.Quality,
            Crf = 20,
        };

        /// <summary>The built-in presets, in the order they appear in the picker. Custom presets come later.</summary>
        public static IReadOnlyList<VideoExportPreset> Defaults { get; } = new[]
        {
            DiscordFree,
            DiscordNitroBasic,
            DiscordNitro,
            Quality720p,
            Quality1080p60,
        };
    }
}
