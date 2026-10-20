using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>Raised when an archive cannot be imported safely. The message is suitable for showing to the user.</summary>
    public sealed class ArchiveException : Exception
    {
        public ArchiveException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>Caps that stop a hostile or broken archive from filling the phone's storage or memory.</summary>
    public sealed record ArchiveLimits(int MaxEntries = 5000, long MaxEntryBytes = 512L * 1024 * 1024, long MaxTotalBytes = 1024L * 1024 * 1024);

    /// <summary>
    /// Reads and writes <c>.osz</c> archives for the app-owned library. Pure file I/O: no Realm, no osu!lazer, no UI.
    /// Import is all-or-nothing (extract into a temporary sibling folder, then rename), rejects path-traversal names
    /// (zip-slip), and never holds a whole file in memory. Files are stored exactly as they appear in the archive.
    /// </summary>
    public static class SetArchive
    {
        public enum NameVerdict
        {
            Ok,

            /// <summary>A folder entry or OS junk file: ignored, not an error.</summary>
            Skip,

            /// <summary>A name that could escape the target folder or is otherwise malformed: the whole archive is refused.</summary>
            Unsafe,
        }

        /// <summary>
        /// Validates an archive entry name and converts it to a clean relative path with '/' separators.
        /// </summary>
        public static NameVerdict CheckName(string rawName, out string name)
        {
            name = string.Empty;

            if (string.IsNullOrEmpty(rawName))
                return NameVerdict.Unsafe;

            string n = rawName.Replace('\\', '/');

            if (n.EndsWith('/'))
                return NameVerdict.Skip; // directory entry

            if (n.Any(c => c < 0x20 || c == 0x7f))
                return NameVerdict.Unsafe;

            if (n.StartsWith('/') || (n.Length >= 2 && n[1] == ':'))
                return NameVerdict.Unsafe; // absolute path or drive letter

            string[] parts = n.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.TrimEnd().Length != p.Length || p.TrimEnd('.').Length == 0))
                return NameVerdict.Unsafe;

            string last = parts[^1];
            if (parts[0] == "__MACOSX" || last == ".DS_Store" || last.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase))
                return NameVerdict.Skip;

            name = string.Join('/', parts);
            return NameVerdict.Ok;
        }

        /// <summary>
        /// Extracts <paramref name="zip"/> into the NEW folder <paramref name="destDir"/> and returns the relative file
        /// names written (sorted). Either the whole folder appears or nothing does; a failure leaves no partial files.
        /// </summary>
        /// <exception cref="ArchiveException">The archive is corrupt, unsafe, empty, too large, or the destination exists.</exception>
        public static IReadOnlyList<string> ExtractToDirectory(Stream zip, string destDir, ArchiveLimits? limits = null)
        {
            limits ??= new ArchiveLimits();
            destDir = Path.GetFullPath(destDir);

            if (Directory.Exists(destDir) || File.Exists(destDir))
                throw new ArchiveException("This beatmap is already in the library.");

            string parent = Path.GetDirectoryName(destDir)!;
            Directory.CreateDirectory(parent);
            string temp = Path.Combine(parent, "." + Path.GetFileName(destDir) + ".importing-" + Guid.NewGuid().ToString("N"));

            try
            {
                var written = extract(zip, temp, limits);
                Directory.Move(temp, destDir);
                return written;
            }
            catch
            {
                tryDelete(temp);
                throw;
            }
        }

        private static List<string> extract(Stream zip, string tempRoot, ArchiveLimits limits)
        {
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                throw new ArchiveException("This file is not a valid .osz archive.", ex);
            }

            using (archive)
            {
                var entries = new List<(ZipArchiveEntry entry, string name)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (archive.Entries.Count > limits.MaxEntries)
                        throw new ArchiveException("This archive has too many files.");

                    foreach (var entry in archive.Entries)
                    {
                        switch (CheckName(entry.FullName, out string name))
                        {
                            case NameVerdict.Skip:
                                continue;

                            case NameVerdict.Unsafe:
                                throw new ArchiveException("This archive contains an unsafe file path and was not imported.");
                        }

                        if (!seen.Add(name))
                            throw new ArchiveException("This archive contains two files with the same name.");

                        if (entry.Length > limits.MaxEntryBytes)
                            throw new ArchiveException("This archive contains a file that is too large.");

                        entries.Add((entry, name));
                    }

                    if (entries.Count == 0)
                        throw new ArchiveException("This archive is empty.");

                    entries = stripSingleRootFolder(entries);

                    Directory.CreateDirectory(tempRoot);
                    string root = Path.GetFullPath(tempRoot) + Path.DirectorySeparatorChar;
                    long total = 0;

                    foreach (var (entry, name) in entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(tempRoot, name));
                        if (!target.StartsWith(root, StringComparison.Ordinal))
                            throw new ArchiveException("This archive contains an unsafe file path and was not imported.");

                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                        // Count the bytes actually produced: the size in the header can lie (zip bomb).
                        using var input = entry.Open();
                        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920);
                        var buffer = new byte[81920];
                        long entryBytes = 0;
                        int read;

                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            entryBytes += read;
                            total += read;

                            if (entryBytes > limits.MaxEntryBytes || total > limits.MaxTotalBytes)
                                throw new ArchiveException("This archive is too large to import.");

                            output.Write(buffer, 0, read);
                        }
                    }

                    return entries.Select(e => e.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                }
                catch (ArchiveException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                {
                    throw new ArchiveException("This archive is damaged and could not be read.", ex);
                }
            }
        }

        /// <summary>
        /// A map folder zipped as a whole ("[id] Artist - Title/map.osu", "[id] Artist - Title/audio.mp3", ...) has every file
        /// under ONE top-level folder. The set's files must sit at the root, so that wrapper folder is removed. Archives
        /// with any file at the root, or with several top-level folders, are left exactly as they are.
        /// </summary>
        internal static List<(ZipArchiveEntry entry, string name)> stripSingleRootFolder(List<(ZipArchiveEntry entry, string name)> entries)
        {
            string? root = null;

            foreach (var (_, name) in entries)
            {
                int slash = name.IndexOf('/');
                if (slash <= 0)
                    return entries; // a file at the archive root: nothing to strip

                string first = name.Substring(0, slash);
                if (root == null)
                    root = first;
                else if (!string.Equals(root, first, StringComparison.Ordinal))
                    return entries; // more than one top-level folder
            }

            if (root == null)
                return entries;

            return entries.Select(e => (e.entry, e.name.Substring(root.Length + 1))).ToList();
        }

        private static readonly HashSet<string> already_compressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".ogg", ".jpg", ".jpeg", ".png", ".mp4", ".avi", ".flv", ".m4v", ".webm", ".wav_",
        };

        /// <summary>
        /// Writes every file under <paramref name="sourceDir"/> into an <c>.osz</c> (zip) stream, exactly as stored:
        /// same names (case and sub-folders), same bytes. Output is deterministic (sorted, fixed timestamps).
        /// </summary>
        public static void WriteArchive(string sourceDir, Stream output)
        {
            string root = Path.GetFullPath(sourceDir);
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                                 .Select(p => (path: p, name: Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/')))
                                 .Where(f => !f.name.StartsWith('.') && !f.name.Contains("/."))
                                 .OrderBy(f => f.name, StringComparer.Ordinal)
                                 .ToList();

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            foreach (var (path, name) in files)
            {
                var level = already_compressed.Contains(Path.GetExtension(name)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var entry = archive.CreateEntry(name, level);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                using var entryStream = entry.Open();
                input.CopyTo(entryStream);
            }
        }

        private static void tryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // Best effort: a leftover ".importing-*" folder is invisible to the library and swept on next start.
            }
        }
    }
}
