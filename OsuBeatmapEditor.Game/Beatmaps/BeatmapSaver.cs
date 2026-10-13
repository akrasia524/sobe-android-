using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Framework.Graphics;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Saves edits to an existing beatmap by text-patching the original .osu (so nothing else is lost),
    /// packaging it with its audio/background into a .osz, and handing it to osu!lazer to import.
    /// </summary>
    public static class BeatmapSaver
    {
        /// <summary>Holds the edited metadata/difficulty values to write back.</summary>
        public class Edits
        {
            public string Title = "", TitleUnicode = "", Artist = "", ArtistUnicode = "", Creator = "", Version = "", Source = "", Tags = "";
            public float Hp, Cs, Ar, Od, StackLeniency = 0.7f;
            public float SliderMultiplier = 1.4f, SliderTickRate = 1f;

            /// <summary>The computed star rating to persist to the realm (so the carousel/lazer reflect it). -1 = leave as-is.</summary>
            public double StarRating = -1;

            /// <summary>The map's own combo colours ([Colours] ComboN), in order. Empty = no [Colours] section.</summary>
            public List<Colour4> ComboColours = new List<Colour4>();

            /// <summary>The background image filename to write into [Events] (empty = leave the original untouched).</summary>
            public string BackgroundFilename = "";
        }

        /// <summary>
        /// Produces the patched .osu text for an edited difficulty (re-emitting [HitObjects],
        /// [TimingPoints] and [Editor] Bookmarks, and replacing [General]/[Metadata]/[Difficulty] lines
        /// while preserving everything else verbatim), or <c>null</c> if the original .osu can't be resolved.
        /// Shared by the .osz exporter (<see cref="Save"/>) and the in-place realm writer
        /// (<see cref="BeatmapRealmWriter"/>).
        /// </summary>
        public static string? BuildPatchedOsu(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, Edits edits)
        {
            if (set.IsLibrarySet)
                return Library.LibrarySaver.BuildPatched(set, difficulty, parsed, edits);

            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);
            if (osuPath == null)
                return null;

            // Patch the ORIGINAL text: only what actually changed is rewritten (see OsuFilePatcher).
            return OsuFilePatcher.Patch(File.ReadAllText(osuPath), parsed, edits);
        }

        public static bool Save(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, Edits edits,
            string? newBackgroundPath = null, string? newBackgroundName = null)
        {
            string? patched = BuildPatchedOsu(set, difficulty, parsed, edits);
            if (patched == null)
                return false;

            // Which stored file is the difficulty we edited (so we replace its content, keep the rest).
            string? editedFile = null;
            foreach (var (name, hash) in set.Files)
            {
                if (string.Equals(hash, difficulty.OsuFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    editedFile = name;
                    break;
                }
            }

            string exportDir = Path.Combine(Path.GetTempPath(), "osu-editor-exports");
            Directory.CreateDirectory(exportDir);

            string oszPath = Path.Combine(exportDir, sanitise($"{edits.Artist} - {edits.Title} ({edits.Creator}).osz"));
            if (File.Exists(oszPath))
                File.Delete(oszPath);

            using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Create))
            {
                // Repackage the WHOLE set: every original file verbatim, except the edited difficulty,
                // whose content we replace with the patched version. This keeps the other difficulties.
                foreach (var (name, hash) in set.Files)
                {
                    if (string.Equals(name, editedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                        writer.Write(patched);
                    }
                    else
                    {
                        string? path = LazerFileStore.ResolvePath(set.DataDirectory, hash);
                        if (path != null)
                            archive.CreateEntryFromFile(path, name, CompressionLevel.NoCompression);
                    }
                }

                // Fallback: if the edited difficulty wasn't in the file list, add it under a derived name.
                if (editedFile == null)
                {
                    string osuName = sanitise($"{edits.Artist} - {edits.Title} ({edits.Creator}) [{edits.Version}].osu");
                    var entry = archive.CreateEntry(osuName, CompressionLevel.NoCompression);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(patched);
                }

                // A dragged-in background that isn't already among the set's files: add it under its target name.
                if (!string.IsNullOrEmpty(newBackgroundPath) && !string.IsNullOrEmpty(newBackgroundName) && File.Exists(newBackgroundPath)
                    && !set.Files.Keys.Any(n => string.Equals(n, newBackgroundName, StringComparison.OrdinalIgnoreCase)))
                {
                    archive.CreateEntryFromFile(newBackgroundPath, newBackgroundName, CompressionLevel.NoCompression);
                }
            }

            return LazerImporter.Import(oszPath);
        }

        private static string sanitise(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "beatmap.osz" : cleaned;
        }
    }
}
