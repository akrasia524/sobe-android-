using System;
using System.IO;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Saves an edited difficulty of an app-owned library set: patches the ORIGINAL .osu text with <see cref="OsuFilePatcher"/>
    /// (only what changed is rewritten, everything else stays byte-for-byte), re-reads the result to check no hit object or
    /// timing point changed unexpectedly, then replaces the file through <see cref="LibraryStore.SaveDifficulty"/>, which keeps a
    /// rolling backup of the previous version. Any problem returns an error message and leaves the stored file untouched.
    /// </summary>
    public static class LibrarySaver
    {
        /// <summary>The patched text for a library difficulty (not saved), or null if the original cannot be read.</summary>
        public static string? BuildPatched(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, BeatmapSaver.Edits edits)
        {
            if (!set.IsLibrarySet || set.Library == null)
                return null;

            try
            {
                return OsuFilePatcher.Patch(set.Library.ReadFileText(set.LibrarySetId, difficulty.LibraryFile), parsed, edits);
            }
            catch (Exception ex) when (ex is LibraryException or IOException)
            {
                return null;
            }
        }

        /// <summary>Saves the edit. Returns null on success (including "nothing changed"), otherwise a message for the user.</summary>
        public static string? Save(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, BeatmapSaver.Edits edits, bool hasPendingBackground)
        {
            if (!set.IsLibrarySet || set.Library == null)
                return "This beatmap is not in the library.";

            if (hasPendingBackground)
                return "Changing the background is not supported for library maps yet. Nothing was saved.";

            try
            {
                string original = set.Library.ReadFileText(set.LibrarySetId, difficulty.LibraryFile);
                string patched = OsuFilePatcher.Patch(original, parsed, edits);

                if (OsuFilePatcher.Verify(patched, parsed) is { } problem)
                    return $"{problem} Nothing was saved.";

                // No real change: do not rewrite the file or spend a backup slot.
                if (patched == original)
                    return null;

                set.Library.SaveDifficulty(set.LibrarySetId, difficulty.LibraryFile, patched);

                // Keep the open editor's snapshot in step with what is stored (same idea as the lazer writer).
                var record = set.Library.Find(set.LibrarySetId);
                if (record?.Difficulties.Find(d => d.File == difficulty.LibraryFile) is { } stored)
                    difficulty.OsuFileHash = stored.Sha256;

                return null;
            }
            catch (LibraryException ex)
            {
                return ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "The file could not be written. Nothing was saved.";
            }
        }
    }
}
