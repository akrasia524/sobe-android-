using System;
using System.IO;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Finds a beatmap set's files on disk, for both kinds of set the UI can show: app-owned library sets (a plain folder of
    /// files under their real names) and osu!lazer sets (a content-addressed store keyed by hash). The editor asks this
    /// instead of knowing which kind it has. Names inside a .osu are untrusted, so nothing outside the set folder is returned.
    /// </summary>
    public static class SetFileResolver
    {
        /// <summary>The path of a difficulty's .osu file, or null if it cannot be found.</summary>
        public static string? ResolveOsu(BeatmapSetModel set, BeatmapDifficultyModel difficulty) =>
            set.IsLibrarySet
                ? ResolveInFolder(set.LibraryFilesDirectory, difficulty.LibraryFile)
                : LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);

        /// <summary>The path of a file a .osu refers to by name (audio, background, hitsound sample...), or null.</summary>
        public static string? ResolveNamed(BeatmapSetModel set, string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            if (set.IsLibrarySet)
                return ResolveInFolder(set.LibraryFilesDirectory, filename);

            if (string.IsNullOrEmpty(set.DataDirectory)
                || !set.Files.TryGetValue(filename.ToLowerInvariant(), out string? hash)
                || string.IsNullOrEmpty(hash))
                return null;

            return LazerFileStore.ResolvePath(set.DataDirectory, hash);
        }

        /// <summary>
        /// Resolves <paramref name="name"/> inside <paramref name="folder"/>. osu! treats file names case-insensitively
        /// (and Android's file system does not), so an exact match is tried first, then a case-insensitive one.
        /// </summary>
        public static string? ResolveInFolder(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
                return null;

            if (SetArchive.CheckName(name, out string clean) != SetArchive.NameVerdict.Ok)
                return null;

            try
            {
                string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
                string exact = Path.GetFullPath(Path.Combine(root, clean));

                if (!exact.StartsWith(root, StringComparison.Ordinal))
                    return null;

                if (File.Exists(exact))
                    return exact;

                string? directory = Path.GetDirectoryName(exact);
                if (directory == null || !Directory.Exists(directory))
                    return null;

                string wanted = Path.GetFileName(exact);
                foreach (string candidate in Directory.EnumerateFiles(directory))
                {
                    if (string.Equals(Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }

                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
