using System;
using System.IO;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>The user-facing result of importing one file into the library.</summary>
    public readonly record struct ImportOutcome(bool Success, string Message, SetRecord? Set = null, ImportStatus? Status = null)
    {
        public static ImportOutcome Fail(string message) => new ImportOutcome(false, message);
    }

    /// <summary>
    /// Turns "the user picked / dropped this file" into a <see cref="LibraryStore"/> import and a message fit for a toast.
    /// Never throws for bad input: every failure becomes <see cref="ImportOutcome.Fail"/>. Pure file I/O, unit-tested.
    /// </summary>
    public static class LibraryImporter
    {
        /// <summary>A single .osu is tiny; anything bigger is not a beatmap and is refused before being read into memory.</summary>
        public const long MAX_OSU_BYTES = 20L * 1024 * 1024;

        public static ImportOutcome ImportFile(LibraryStore store, string path)
        {
            try
            {
                if (!File.Exists(path))
                    return ImportOutcome.Fail("The file could not be found.");

                ImportResult result;

                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".osz":
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                            result = store.ImportOsz(stream);
                        break;

                    case ".osu":
                        if (new FileInfo(path).Length > MAX_OSU_BYTES)
                            return ImportOutcome.Fail("That file is too large to be a beatmap.");

                        result = store.ImportOsu(Path.GetFileName(path), File.ReadAllBytes(path));
                        break;

                    default:
                        return ImportOutcome.Fail("Unsupported file type: choose an .osz or .osu file.");
                }

                return new ImportOutcome(true, describe(result), result.Set, result.Status);
            }
            catch (Exception ex) when (ex is ArchiveException or LibraryException)
            {
                return ImportOutcome.Fail(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ImportOutcome.Fail("The file could not be read.");
            }
            catch (Exception)
            {
                return ImportOutcome.Fail("The import failed unexpectedly. Nothing was added to your library.");
            }
        }

        private static string describe(ImportResult result)
        {
            string name = string.IsNullOrWhiteSpace(result.Set.Artist) ? result.Set.Title : $"{result.Set.Artist} - {result.Set.Title}";

            return result.Status switch
            {
                ImportStatus.Duplicate => $"{name} is already in your library",
                ImportStatus.RestoredFromTrash => $"Restored {name} from the trash",
                _ => $"Imported {name}",
            };
        }
    }
}
