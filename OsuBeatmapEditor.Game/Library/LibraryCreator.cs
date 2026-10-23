using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Creates brand-new beatmaps inside the app-owned library. A new map is built as an .osz and imported through
    /// <see cref="LibraryStore"/>, so it appears completely or not at all.
    /// </summary>
    public static class LibraryCreator
    {
        private const string pending_dir = "pending-audio";
        private const string temp_dir = "create-temp";

        /// <summary>Removes leftovers of picks / creations that never finished. Call once at start-up.</summary>
        public static void SweepLeftovers(string libraryRoot)
        {
            foreach (string name in new[] { pending_dir, temp_dir, "export-temp" })
            {
                try
                {
                    string dir = Path.Combine(libraryRoot, name);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// Checks a picked audio file and copies it into the library's pending area, so it no longer depends on the picker's
        /// temporary copy. Returns the new path, or an error (and leaves nothing behind).
        /// </summary>
        public static (string? Path, string? Error) StageAudio(string libraryRoot, string sourcePath)
        {
            string? problem = PickedAudio.Validate(sourcePath);
            if (problem != null)
                return (null, problem);

            string? dir = null;

            try
            {
                dir = Path.Combine(libraryRoot, pending_dir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);

                string target = Path.Combine(dir, LibraryActions.SafeFileName(Path.GetFileName(sourcePath)));
                string part = target + ".part";
                File.Copy(sourcePath, part, false);
                File.Move(part, target);
                return (target, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                TryDeleteDir(dir);
                return (null, "Could not copy the audio file: " + e.Message);
            }
        }

        /// <summary>Deletes a staged audio file (only ever inside the library's pending area).</summary>
        public static void DiscardStaged(string libraryRoot, string? stagedPath)
        {
            if (string.IsNullOrEmpty(stagedPath))
                return;

            string pending = Path.GetFullPath(Path.Combine(libraryRoot, pending_dir)) + Path.DirectorySeparatorChar;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(stagedPath));
            if (dir != null && (dir + Path.DirectorySeparatorChar).StartsWith(pending, StringComparison.Ordinal) && dir.Length > pending.Length - 1)
                TryDeleteDir(dir);
        }

        /// <summary>
        /// Builds and imports a new set from <paramref name="request"/>. On any failure nothing is added to the library.
        /// </summary>
        public static ActionOutcome Create(LibraryStore store, NewBeatmapRequest request)
        {
            if (!request.IsValid)
                return new ActionOutcome(false, "Fill in the artist, title and BPM and choose an audio file first.");

            // A reused source audio (AudioFileName set) was validated when it was first added; only fresh picks are checked here.
            string? audioProblem = string.IsNullOrWhiteSpace(request.AudioFileName) ? PickedAudio.Validate(request.AudioPath) : null;
            if (audioProblem != null)
                return new ActionOutcome(false, audioProblem);

            // The background is optional, but a chosen one must be a real image (checked before anything is built).
            if (!string.IsNullOrWhiteSpace(request.BackgroundPath))
            {
                string? imageProblem = PickedImage.Validate(request.BackgroundPath);
                if (imageProblem != null)
                    return new ActionOutcome(false, "Background: " + imageProblem);
            }

            string tempDir = Path.Combine(store.RootDirectory, temp_dir);
            string tempOsz = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".osz");

            try
            {
                Directory.CreateDirectory(tempDir);

                string audioName = BeatmapArchiveWriter.ResolveAudioFilename(request);
                string osuName = BeatmapArchiveWriter.OsuFilename(request);
                string? backgroundName = BeatmapArchiveWriter.ResolveBackgroundFilename(request, audioName);

                using (var archive = ZipFile.Open(tempOsz, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry(osuName, CompressionLevel.NoCompression);
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        writer.Write(BeatmapArchiveWriter.BuildOsuText(request, audioName, uninheritedTimingOnly: false, backgroundName));

                    archive.CreateEntryFromFile(request.AudioPath, audioName, CompressionLevel.NoCompression);

                    if (backgroundName != null)
                        archive.CreateEntryFromFile(request.BackgroundPath!, backgroundName, CompressionLevel.NoCompression);
                }

                using (var fs = File.OpenRead(tempOsz))
                    store.ImportOsz(fs, allowDuplicate: true);

                return new ActionOutcome(true, $"Created {request.Artist} - {request.Title}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or LibraryException or ArchiveException or InvalidDataException)
            {
                return new ActionOutcome(false, "Could not create the beatmap: " + e.Message);
            }
            finally
            {
                try { File.Delete(tempOsz); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        private static void TryDeleteDir(string? dir)
        {
            if (dir == null)
                return;

            try { Directory.Delete(dir, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
