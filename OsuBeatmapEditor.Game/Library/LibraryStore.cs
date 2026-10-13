using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// The app-owned beatmap library: a plain folder tree this app fully controls (no Realm, no osu!lazer / osu!droid storage).
    /// <code>
    /// root/
    ///   sets/&lt;id&gt;/set.json         library index data for the set
    ///   sets/&lt;id&gt;/files/...        the beatmap files exactly as imported (original names, case, sub-folders)
    ///   sets/&lt;id&gt;/backups/...      rolling backups of replaced .osu files
    ///   staging/&lt;guid&gt;/...         in-progress imports; swept on start
    /// </code>
    /// Every change is staged and renamed into place, so a crash or full disk leaves either the old state or the new one.
    /// Pure file I/O so it is unit-tested without a device. Not yet connected to the UI.
    /// </summary>
    public sealed class LibraryStore
    {
        public const int DEFAULT_BACKUPS_KEPT = 10;

        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { WriteIndented = true };
        private static readonly UTF8Encoding utf8 = new UTF8Encoding(false);
        private static readonly Regex mode_regex = new Regex(@"^\s*Mode\s*:\s*(\d+)", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex set_id_regex = new Regex(@"^\s*BeatmapSetID\s*:\s*(-?\d+)", RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly object gate = new object();
        private readonly Dictionary<string, SetRecord> index = new Dictionary<string, SetRecord>();
        private readonly Func<DateTimeOffset> clock;
        private readonly int backupsKept;

        public string RootDirectory { get; }

        /// <summary>Sets whose set.json could not be read at start-up (set id folder + reason); their files are left alone.</summary>
        public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

        public LibraryStore(string rootDirectory, Func<DateTimeOffset>? clock = null, int backupsKept = DEFAULT_BACKUPS_KEPT)
        {
            RootDirectory = Path.GetFullPath(rootDirectory);
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
            this.backupsKept = Math.Max(1, backupsKept);

            Directory.CreateDirectory(setsDir);
            sweepStaging();
            load();
        }

        private string setsDir => Path.Combine(RootDirectory, "sets");
        private string stagingDir => Path.Combine(RootDirectory, "staging");
        private string setDir(string id) => Path.Combine(setsDir, id);

        /// <summary>The folder holding the set's beatmap files.</summary>
        public string FilesDirectory(string setId) => Path.Combine(setDir(setId), "files");

        /// <summary>Sets that are not in the trash, newest first.</summary>
        public IReadOnlyList<SetRecord> Sets
        {
            get { lock (gate) return index.Values.Where(s => !s.IsDeleted).OrderByDescending(s => s.DateAdded).ToList(); }
        }

        /// <summary>Sets in the trash, most recently deleted first.</summary>
        public IReadOnlyList<SetRecord> Trash
        {
            get { lock (gate) return index.Values.Where(s => s.IsDeleted).OrderByDescending(s => s.DeletedAt).ToList(); }
        }

        public SetRecord? Find(string id)
        {
            lock (gate) return index.TryGetValue(id, out var s) ? s : null;
        }

        // ------------------------------------------------------------------------------------------ start-up

        private void sweepStaging()
        {
            try
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
            catch
            {
                // Best effort: leftover staging data is invisible to the library.
            }
        }

        private void load()
        {
            var problems = new List<string>();

            foreach (string dir in Directory.EnumerateDirectories(setsDir))
            {
                string id = Path.GetFileName(dir);
                if (id.StartsWith('.'))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<SetRecord>(File.ReadAllText(Path.Combine(dir, "set.json"), utf8), json_options)
                                 ?? throw new InvalidDataException("empty");

                    if (record.Id != id)
                        throw new InvalidDataException("set.json does not belong to this folder");
                    if (record.Version > SetRecord.CURRENT_VERSION)
                        throw new InvalidDataException("written by a newer version of the app");

                    index[id] = record;
                    sweepSavingLeftovers(Path.Combine(dir, "files"));
                }
                catch (Exception ex)
                {
                    problems.Add($"{id}: {ex.Message}");
                }
            }

            Problems = problems;
        }

        /// <summary>Removes half-written ".saving-*" files a crash may have left next to a beatmap file.</summary>
        private static void sweepSavingLeftovers(string filesDir)
        {
            try
            {
                if (!Directory.Exists(filesDir))
                    return;

                foreach (string f in Directory.EnumerateFiles(filesDir, "*.saving-*", SearchOption.AllDirectories))
                    File.Delete(f);
            }
            catch
            {
                // Best effort.
            }
        }

        // ------------------------------------------------------------------------------------------ import

        /// <summary>Imports an .osz. Importing the same files again returns the existing set instead of adding a copy.</summary>
        /// <exception cref="ArchiveException">The archive is unsafe, damaged or has no osu!standard difficulty.</exception>
        public ImportResult ImportOsz(Stream zip, bool allowDuplicate = false, ArchiveLimits? limits = null)
        {
            lock (gate)
            {
                string stage = newStage();

                try
                {
                    SetArchive.ExtractToDirectory(zip, Path.Combine(stage, "files"), limits);
                    return finishImport(stage, "osz", allowDuplicate);
                }
                catch
                {
                    tryDeleteDirectory(stage);
                    throw;
                }
            }
        }

        /// <summary>Imports a single .osu file as a new set (for a bare file with no archive).</summary>
        public ImportResult ImportOsu(string fileName, byte[] bytes, bool allowDuplicate = false)
        {
            if (SetArchive.CheckName(fileName, out string name) != SetArchive.NameVerdict.Ok || !name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                throw new ArchiveException("That is not a valid .osu file name.");

            lock (gate)
            {
                string stage = newStage();

                try
                {
                    string target = Path.Combine(stage, "files", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                    return finishImport(stage, "osu", allowDuplicate);
                }
                catch
                {
                    tryDeleteDirectory(stage);
                    throw;
                }
            }
        }

        private string newStage()
        {
            string stage = Path.Combine(stagingDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            return stage;
        }

        private ImportResult finishImport(string stage, string source, bool allowDuplicate)
        {
            string filesDir = Path.Combine(stage, "files");
            var difficulties = readDifficulties(filesDir);

            if (difficulties.Count == 0)
                throw new ArchiveException("No osu!standard difficulties were found in this beatmap.");

            string hash = ComputeContentHash(filesDir);

            if (!allowDuplicate && index.Values.FirstOrDefault(s => s.ImportHash == hash) is { } existing)
            {
                tryDeleteDirectory(stage);

                if (!existing.IsDeleted)
                    return new ImportResult(ImportStatus.Duplicate, existing);

                setDeleted(existing, null);
                return new ImportResult(ImportStatus.RestoredFromTrash, existing);
            }

            var now = clock();
            var first = difficulties.OrderBy(d => d.record.File, StringComparer.Ordinal).First();

            var record = new SetRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ImportHash = hash,
                Source = source,
                Title = nonEmpty(first.parsed.Title, Path.GetFileNameWithoutExtension(first.record.File)),
                Artist = first.parsed.Artist,
                Creator = first.parsed.Creator,
                OnlineSetId = first.onlineSetId,
                DateAdded = now,
                DateModified = now,
                Difficulties = difficulties.Select(d => d.record).OrderBy(d => d.File, StringComparer.Ordinal).ToList(),
            };

            writeRecord(stage, record);
            Directory.CreateDirectory(setsDir);
            Directory.Move(stage, setDir(record.Id)); // the single atomic step that makes the set appear
            index[record.Id] = record;
            return new ImportResult(ImportStatus.Imported, record);
        }

        private static string nonEmpty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static List<(DifficultyRecord record, ParsedBeatmap parsed, int onlineSetId)> readDifficulties(string filesDir)
        {
            var result = new List<(DifficultyRecord, ParsedBeatmap, int)>();

            foreach (string path in Directory.EnumerateFiles(filesDir, "*.osu", SearchOption.AllDirectories))
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text = utf8.GetString(bytes);

                // The editor is osu!standard only; other modes' files stay in the set (and in exports) but are not listed.
                if (mode_regex.Match(text) is { Success: true } m && m.Groups[1].Value != "0")
                    continue;

                ParsedBeatmap parsed;
                try
                {
                    parsed = OsuFileDecoder.Decode(new StringReader(text));
                }
                catch
                {
                    continue;
                }

                result.Add((toRecord(Path.GetRelativePath(filesDir, path).Replace(Path.DirectorySeparatorChar, '/'), bytes, parsed), parsed, onlineSetId(text)));
            }

            return result;
        }

        private static int onlineSetId(string text) =>
            set_id_regex.Match(text) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int id) ? id : -1;

        private static DifficultyRecord toRecord(string file, byte[] bytes, ParsedBeatmap parsed) => new DifficultyRecord
        {
            File = file,
            Name = parsed.Version,
            Audio = parsed.AudioFilename,
            Background = parsed.BackgroundFilename,
            PreviewTime = parsed.PreviewTime,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };

        /// <summary>Hash over every file's name and content, independent of the order they were read in.</summary>
        public static string ComputeContentHash(string filesDir)
        {
            using var overall = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (string path in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories)
                                             .OrderBy(p => Path.GetRelativePath(filesDir, p).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal))
            {
                string name = Path.GetRelativePath(filesDir, path).Replace(Path.DirectorySeparatorChar, '/');
                using var stream = File.OpenRead(path);
                string hex = Convert.ToHexString(SHA256.HashData(stream));
                overall.AppendData(utf8.GetBytes(name + "\n" + hex + "\n"));
            }

            return Convert.ToHexString(overall.GetHashAndReset()).ToLowerInvariant();
        }

        // ------------------------------------------------------------------------------------------ export

        /// <summary>Writes the set's files as an .osz. Library bookkeeping (set.json, backups) is never included.</summary>
        public void ExportOsz(string setId, Stream output)
        {
            lock (gate)
            {
                requireSet(setId);
                SetArchive.WriteArchive(FilesDirectory(setId), output);
            }
        }

        // ------------------------------------------------------------------------------------------ reading / saving

        /// <summary>Reads a stored file as text without discarding a leading byte-order mark (so a save can keep it).</summary>
        public string ReadFileText(string setId, string fileName)
        {
            lock (gate)
            {
                requireSet(setId);
                return utf8.GetString(File.ReadAllBytes(resolve(setId, fileName)));
            }
        }

        /// <summary>
        /// Replaces a difficulty's .osu with <paramref name="newText"/>. The previous version is first copied into a rolling
        /// backup, the new text is written to a temporary file and renamed over the old one, so any failure leaves the
        /// previous file intact.
        /// </summary>
        public void SaveDifficulty(string setId, string fileName, string newText)
        {
            lock (gate)
            {
                var record = requireSet(setId);
                var diff = requireDifficulty(record, fileName);

                if (string.IsNullOrWhiteSpace(newText))
                    throw new LibraryException("Refusing to save an empty beatmap file.");

                ParsedBeatmap parsed;
                try
                {
                    parsed = OsuFileDecoder.Decode(new StringReader(newText));
                }
                catch (Exception ex)
                {
                    throw new LibraryException("The edited beatmap could not be read back, so it was not saved.", ex);
                }

                writeReplacing(setId, diff.File, utf8.GetBytes(newText));

                var updated = toRecord(diff.File, utf8.GetBytes(newText), parsed);
                diff.Name = updated.Name;
                diff.Audio = updated.Audio;
                diff.Background = updated.Background;
                diff.PreviewTime = updated.PreviewTime;
                diff.Sha256 = updated.Sha256;
                record.DateModified = clock();
                writeRecord(setDir(setId), record);
            }
        }

        /// <summary>Backups of a difficulty, oldest first (names are opaque; pass one to <see cref="RestoreBackup"/>).</summary>
        public IReadOnlyList<string> ListBackups(string setId, string fileName)
        {
            lock (gate)
            {
                var diff = requireDifficulty(requireSet(setId), fileName);
                string dir = backupDirectory(setId, diff.File);
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.bak").Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToList() : new List<string>();
            }
        }

        /// <summary>Puts a backup back as the current file. The version it replaces is backed up first, so this can be undone too.</summary>
        public void RestoreBackup(string setId, string fileName, string backupName)
        {
            lock (gate)
            {
                var record = requireSet(setId);
                var diff = requireDifficulty(record, fileName);

                if (backupName != Path.GetFileName(backupName) || !backupName.EndsWith(".bak", StringComparison.Ordinal))
                    throw new LibraryException("Unknown backup.");

                string backup = Path.Combine(backupDirectory(setId, diff.File), backupName);
                if (!File.Exists(backup))
                    throw new LibraryException("That backup no longer exists.");

                SaveDifficulty(setId, fileName, utf8.GetString(File.ReadAllBytes(backup)));
            }
        }

        private string backupDirectory(string setId, string file) => Path.Combine(setDir(setId), "backups", file.Replace('/', '~'));

        private void writeReplacing(string setId, string relativeFile, byte[] data)
        {
            string target = resolve(setId, relativeFile);
            string temp = target + ".saving-" + Guid.NewGuid().ToString("N");

            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }

                backUp(setId, relativeFile, target);
                File.Move(temp, target, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* best effort */ }
                throw;
            }
        }

        private void backUp(string setId, string relativeFile, string target)
        {
            if (!File.Exists(target))
                return;

            string dir = backupDirectory(setId, relativeFile);
            Directory.CreateDirectory(dir);

            var existing = Directory.GetFiles(dir, "*.bak").Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            int next = existing.Count == 0 ? 1 : int.Parse(Path.GetFileNameWithoutExtension(existing[^1])!) + 1;

            File.Copy(target, Path.Combine(dir, next.ToString("D6") + ".bak"));
            existing.Add(next.ToString("D6") + ".bak");

            // The ring: drop the oldest beyond the limit.
            foreach (string old in existing.Take(Math.Max(0, existing.Count - backupsKept)))
                File.Delete(Path.Combine(dir, old));
        }

        // ------------------------------------------------------------------------------------------ delete / restore

        /// <summary>Moves a set to the trash. Nothing is removed from disk, so it can be restored.</summary>
        public void Delete(string setId)
        {
            lock (gate)
            {
                var record = requireSet(setId);
                if (!record.IsDeleted)
                    setDeleted(record, clock());
            }
        }

        public void Restore(string setId)
        {
            lock (gate)
            {
                var record = requireSet(setId);
                if (record.IsDeleted)
                    setDeleted(record, null);
            }
        }

        /// <summary>Permanently removes a set that is already in the trash.</summary>
        public void Purge(string setId)
        {
            lock (gate)
            {
                var record = requireSet(setId);
                if (!record.IsDeleted)
                    throw new LibraryException("Only a set in the trash can be permanently deleted.");

                Directory.Delete(setDir(setId), true);
                index.Remove(setId);
            }
        }

        private void setDeleted(SetRecord record, DateTimeOffset? when)
        {
            var before = record.DeletedAt;
            record.DeletedAt = when;

            try
            {
                writeRecord(setDir(record.Id), record);
            }
            catch
            {
                record.DeletedAt = before; // keep memory consistent with disk
                throw;
            }
        }

        // ------------------------------------------------------------------------------------------ helpers

        private SetRecord requireSet(string setId) =>
            setId != null && index.TryGetValue(setId, out var record) ? record : throw new LibraryException("That beatmap is not in the library.");

        private static DifficultyRecord requireDifficulty(SetRecord record, string fileName) =>
            record.Difficulties.FirstOrDefault(d => d.File == fileName) ?? throw new LibraryException("That difficulty is not part of this beatmap.");

        /// <summary>Resolves a stored file to its path, refusing anything outside the set's files folder.</summary>
        private string resolve(string setId, string fileName)
        {
            if (SetArchive.CheckName(fileName, out string name) != SetArchive.NameVerdict.Ok)
                throw new LibraryException("Invalid file name.");

            string root = Path.GetFullPath(FilesDirectory(setId)) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, name));

            return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? full : throw new LibraryException("That file is not in the beatmap.");
        }

        private static void writeRecord(string directory, SetRecord record)
        {
            string path = Path.Combine(directory, "set.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, json_options), utf8);
            File.Move(temp, path, overwrite: true);
        }

        private static void tryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // Best effort; staging is swept on the next start.
            }
        }
    }
}
