using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>Result of a user-facing library action: a message the UI can show, and whether it worked.</summary>
    public sealed record ActionOutcome(bool Success, string Message);

    /// <summary>
    /// The delete / restore / permanently-delete / export actions behind the library screen. UI-free so it can be tested.
    /// Delete only moves a set to the trash; only <see cref="Purge"/> removes data, and only for a set already in the trash.
    /// </summary>
    public static class LibraryActions
    {
        /// <summary>
        /// Set by the Android host: receives a finished .osz in app storage, copies it somewhere the user can reach
        /// (the Downloads folder) and returns a short description of where, or null on failure.
        /// </summary>
        public static Func<string, string?>? Publisher { get; set; }

        public static ActionOutcome Delete(LibraryStore store, string setId) => run(store, setId, s => store.Delete(s), n => $"Moved \"{n}\" to the trash");

        public static ActionOutcome Restore(LibraryStore store, string setId) => run(store, setId, s => store.Restore(s), n => $"Restored \"{n}\"");

        /// <summary>Permanently removes a set. Refused (nothing removed) unless it is already in the trash.</summary>
        public static ActionOutcome Purge(LibraryStore store, string setId) => run(store, setId, s => store.Purge(s), n => $"Permanently deleted \"{n}\"");

        private static ActionOutcome run(LibraryStore store, string setId, Action<string> action, Func<string, string> ok)
        {
            string name = DisplayName(store.Find(setId));
            try
            {
                action(setId);
                return new ActionOutcome(true, ok(name));
            }
            catch (LibraryException e)
            {
                return new ActionOutcome(false, e.Message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new ActionOutcome(false, "Could not change the library: " + e.Message);
            }
        }

        public static string DisplayName(SetRecord? set)
        {
            if (set == null)
                return "this set";
            string name = string.IsNullOrWhiteSpace(set.Artist) ? set.Title : $"{set.Artist} - {set.Title}";
            return string.IsNullOrWhiteSpace(name) ? "Untitled set" : name;
        }

        /// <summary>
        /// Exports a set as an .osz. The archive is built in <paramref name="stagingDirectory"/>, then handed to
        /// <see cref="Publisher"/> (Android: Downloads) or, without one, moved into <paramref name="fallbackDirectory"/>.
        /// An existing file is never overwritten: a numbered name is used instead.
        /// </summary>
        public static ActionOutcome Export(LibraryStore store, string setId, string stagingDirectory, string fallbackDirectory, Func<string, string?>? publisher = null)
        {
            var set = store.Find(setId);
            if (set == null)
                return new ActionOutcome(false, "That set is no longer in the library.");

            string? staged = null;

            try
            {
                Directory.CreateDirectory(stagingDirectory);
                staged = Path.Combine(stagingDirectory, SafeFileName(DisplayName(set)) + ".osz");
                staged = uniquePath(staged);

                string tmp = staged + ".part";
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
                    store.ExportOsz(setId, fs);
                File.Move(tmp, staged);

                publisher ??= Publisher;

                if (publisher != null)
                {
                    string? where = publisher(staged);
                    return where == null
                        ? new ActionOutcome(false, "Export failed: could not save to Downloads.")
                        : new ActionOutcome(true, "Exported to " + where);
                }

                Directory.CreateDirectory(fallbackDirectory);
                string dest = moveUnique(staged, fallbackDirectory);
                staged = null;
                return new ActionOutcome(true, "Exported to " + dest);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or LibraryException or ArchiveException)
            {
                return new ActionOutcome(false, "Export failed: " + e.Message);
            }
            finally
            {
                // The staged copy is only a handover file; never leave it (or a half-written .part) behind.
                if (staged != null)
                {
                    tryDelete(staged);
                    tryDelete(staged + ".part");
                }
            }
        }

        /// <summary>A file name that is valid on Android and desktop file systems.</summary>
        public static string SafeFileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsControl(c) || bad.Contains(c) ? '_' : c);

            string s = sb.ToString().Trim().TrimEnd('.');
            if (s.Length > 120)
                s = s.Substring(0, 120).TrimEnd();
            return s.Length == 0 ? "beatmap" : s;
        }

        private static string uniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        /// <summary>Moves without ever replacing: File.Move refuses to overwrite, so a race just picks the next name.</summary>
        private static string moveUnique(string source, string directory)
        {
            string target = uniquePath(Path.Combine(directory, Path.GetFileName(source)));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(source, target);
                    return target;
                }
                catch (IOException) when (File.Exists(target) && attempt < 50)
                {
                    target = uniquePath(target);
                }
            }
        }

        private static void tryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
