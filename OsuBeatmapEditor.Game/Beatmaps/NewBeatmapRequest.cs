using System.Collections.Generic;
using System.IO;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// User-supplied data for creating a brand new (empty) beatmap.
    /// </summary>
    public class NewBeatmapRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string DifficultyName { get; set; } = "Normal";
        public double Bpm { get; set; } = 120;

        /// <summary>Time (ms) of the first beat - where the first (red) timing point sits. 0 by default.</summary>
        public double Offset { get; set; }

        /// <summary>Beats per bar (the time signature's top number, e.g. 4 for 4/4). Clamped to 1-16 when written.</summary>
        public int Meter { get; set; } = 4;

        /// <summary>Absolute path to the source audio file chosen by the user.</summary>
        public string AudioPath { get; set; } = string.Empty;

        /// <summary>
        /// Desired stored audio filename (with extension), e.g. when reusing another set's audio whose
        /// on-disk path is content-addressed (no extension). When null the name is derived from
        /// <see cref="AudioPath"/>'s extension.
        /// </summary>
        public string? AudioFileName { get; set; }

        /// <summary>
        /// Raw [TimingPoints] lines to emit verbatim (e.g. carried over from a source set). When null/empty
        /// a single uninherited point is generated from <see cref="Bpm"/>.
        /// </summary>
        public IReadOnlyList<string>? TimingPointLines { get; set; }

        /// <summary>Optional: a .jpg/.png to use as the map's background (null = no background).</summary>
        public string? BackgroundPath { get; set; }

        /// <summary>Whether all required fields are present and valid.</summary>
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Title)
            && !string.IsNullOrWhiteSpace(Artist)
            && Bpm > 0
            && !string.IsNullOrWhiteSpace(AudioPath)
            && File.Exists(AudioPath);
    }
}
