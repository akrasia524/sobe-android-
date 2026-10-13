using System;
using System.Collections.Generic;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>Raised when a library operation cannot be completed safely. The message is suitable for showing to the user.</summary>
    public sealed class LibraryException : Exception
    {
        public LibraryException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>One difficulty (.osu file) of a set, as recorded in <c>set.json</c>.</summary>
    public sealed class DifficultyRecord
    {
        /// <summary>Relative path of the .osu inside the set's files folder (original case, '/' separators).</summary>
        public string File { get; set; } = string.Empty;

        /// <summary>The difficulty name ([Metadata] Version).</summary>
        public string Name { get; set; } = string.Empty;

        public string Audio { get; set; } = string.Empty;
        public string Background { get; set; } = string.Empty;
        public int PreviewTime { get; set; } = -1;

        /// <summary>Star rating; 0 until something computes it (not needed for storage).</summary>
        public double StarRating { get; set; }

        /// <summary>SHA-256 (hex) of the .osu bytes as currently stored.</summary>
        public string Sha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// The contents of a set's <c>set.json</c>. Beatmap files themselves live untouched in <c>files/</c>; this is only the
    /// library's own index data, so it can be rebuilt from the files if it is ever lost.
    /// </summary>
    public sealed class SetRecord
    {
        public const int CURRENT_VERSION = 1;

        public int Version { get; set; } = CURRENT_VERSION;
        public string Id { get; set; } = string.Empty;

        /// <summary>Hash of the files as first imported. Never changes, so re-importing the same download is recognised even after edits.</summary>
        public string ImportHash { get; set; } = string.Empty;

        /// <summary>"osz", "osu" or "new".</summary>
        public string Source { get; set; } = "osz";

        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public int OnlineSetId { get; set; } = -1;

        public DateTimeOffset DateAdded { get; set; }
        public DateTimeOffset DateModified { get; set; }

        /// <summary>Non-null while the set is in the trash.</summary>
        public DateTimeOffset? DeletedAt { get; set; }

        public List<DifficultyRecord> Difficulties { get; set; } = new List<DifficultyRecord>();

        public bool IsDeleted => DeletedAt != null;
    }

    public enum ImportStatus
    {
        Imported,

        /// <summary>The same files are already in the library; nothing was added.</summary>
        Duplicate,

        /// <summary>The same files were in the trash and have been brought back.</summary>
        RestoredFromTrash,
    }

    public readonly record struct ImportResult(ImportStatus Status, SetRecord Set);
}
