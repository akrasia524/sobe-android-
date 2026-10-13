using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class LibraryStoreTests
    {
        private string root = null!;
        private DateTimeOffset now;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-lib-" + Guid.NewGuid().ToString("N"));
            now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private LibraryStore open(int backups = LibraryStore.DEFAULT_BACKUPS_KEPT) => new LibraryStore(root, () => now = now.AddMinutes(1), backups);

        private static byte[] bytes(string s) => Encoding.UTF8.GetBytes(s);

        private static MemoryStream osz(string osuText, string osuName = "Test Song [Insane].osu", byte[]? audio = null)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                void add(string name, byte[] data)
                {
                    using var s = zip.CreateEntry(name).Open();
                    s.Write(data, 0, data.Length);
                }

                add(osuName, bytes(osuText));
                add("audio.mp3", audio ?? new byte[] { 1, 2, 3, 4, 5 });
                add("sb/star.png", new byte[] { 9, 9, 9 });
            }

            ms.Position = 0;
            return ms;
        }

        private static string[] allFiles(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToArray() : Array.Empty<string>();

        private void assertStagingEmpty() => Assert.That(allFiles(Path.Combine(root, "staging")), Is.Empty, "no staging files may remain");

        // ---- import / persistence ------------------------------------------------------------------------------

        [Test]
        public void ImportRecordsMetadataAndPersistsAcrossRestarts()
        {
            var store = open();
            var result = store.ImportOsz(osz(Fixtures.Full));

            Assert.That(result.Status, Is.EqualTo(ImportStatus.Imported));
            var set = result.Set;
            Assert.That(set.Title, Is.EqualTo("Test Song"));
            Assert.That(set.Artist, Is.EqualTo("Test Artist"));
            Assert.That(set.Creator, Is.EqualTo("Mapper"));
            Assert.That(set.OnlineSetId, Is.EqualTo(456));
            Assert.That(set.Difficulties, Has.Count.EqualTo(1));
            Assert.That(set.Difficulties[0].File, Is.EqualTo("Test Song [Insane].osu"));
            Assert.That(set.Difficulties[0].Name, Is.EqualTo("Insane"));
            Assert.That(set.Difficulties[0].Audio, Is.EqualTo("audio.mp3"));
            Assert.That(set.Difficulties[0].Background, Is.EqualTo("bg.jpg"));
            Assert.That(set.Difficulties[0].PreviewTime, Is.EqualTo(12345));

            Assert.That(File.Exists(Path.Combine(root, "sets", set.Id, "set.json")));
            Assert.That(File.ReadAllText(Path.Combine(store.FilesDirectory(set.Id), "Test Song [Insane].osu")), Is.EqualTo(Fixtures.Full));
            assertStagingEmpty();

            var reopened = open();
            Assert.That(reopened.Sets.Select(s => s.Id), Is.EqualTo(new[] { set.Id }));
            Assert.That(reopened.Find(set.Id)!.Difficulties[0].Sha256, Is.EqualTo(set.Difficulties[0].Sha256));
        }

        [Test]
        public void ImportingTheSameFilesAgainReturnsTheExistingSet()
        {
            var store = open();
            var first = store.ImportOsz(osz(Fixtures.Full));
            var second = store.ImportOsz(osz(Fixtures.Full));

            Assert.That(second.Status, Is.EqualTo(ImportStatus.Duplicate));
            Assert.That(second.Set.Id, Is.EqualTo(first.Set.Id));
            Assert.That(store.Sets, Has.Count.EqualTo(1));
            Assert.That(Directory.GetDirectories(Path.Combine(root, "sets")), Has.Length.EqualTo(1));
            assertStagingEmpty();
        }

        [Test]
        public void DuplicateIsStillRecognisedAfterTheLibraryCopyWasEdited()
        {
            var store = open();
            var first = store.ImportOsz(osz(Fixtures.Full)).Set;
            store.SaveDifficulty(first.Id, first.Difficulties[0].File, Fixtures.Full.Replace("Title:Test Song", "Title:Edited"));

            Assert.That(store.ImportOsz(osz(Fixtures.Full)).Status, Is.EqualTo(ImportStatus.Duplicate));
        }

        [Test]
        public void AllowDuplicateCreatesASeparateSet()
        {
            var store = open();
            store.ImportOsz(osz(Fixtures.Full));
            var copy = store.ImportOsz(osz(Fixtures.Full), allowDuplicate: true);

            Assert.That(copy.Status, Is.EqualTo(ImportStatus.Imported));
            Assert.That(store.Sets, Has.Count.EqualTo(2));
        }

        [Test]
        public void DifferentFilesAreNotDuplicates()
        {
            var store = open();
            store.ImportOsz(osz(Fixtures.Full));
            Assert.That(store.ImportOsz(osz(Fixtures.Full, audio: new byte[] { 7, 7 })).Status, Is.EqualTo(ImportStatus.Imported));
        }

        [Test]
        public void ABareOsuFileCanBeImported()
        {
            var store = open();
            var result = store.ImportOsu("single.osu", bytes(Fixtures.Full));

            Assert.That(result.Set.Source, Is.EqualTo("osu"));
            Assert.That(result.Set.Difficulties.Select(d => d.File), Is.EqualTo(new[] { "single.osu" }));
            Assert.That(() => store.ImportOsu("../evil.osu", bytes(Fixtures.Full)), Throws.TypeOf<ArchiveException>());
            Assert.That(() => store.ImportOsu("notes.txt", bytes("x")), Throws.TypeOf<ArchiveException>());
        }

        // ---- failed imports ------------------------------------------------------------------------------------

        [Test]
        public void FailedImportsLeaveNoSetAndNoStagingFiles()
        {
            var store = open();

            Assert.That(() => store.ImportOsz(new MemoryStream(new byte[] { 1, 2, 3, 4 })), Throws.TypeOf<ArchiveException>(), "corrupt");

            var traversal = new MemoryStream();
            using (var zip = new ZipArchive(traversal, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var s = zip.CreateEntry("../evil.osu").Open();
                s.Write(bytes(Fixtures.Full));
            }

            traversal.Position = 0;
            Assert.That(() => store.ImportOsz(traversal), Throws.TypeOf<ArchiveException>(), "zip-slip");

            string taiko = Fixtures.Full.Replace("Mode: 0", "Mode: 1");
            Assert.That(() => store.ImportOsz(osz(taiko)), Throws.TypeOf<ArchiveException>().With.Message.Contain("osu!standard"), "no standard difficulty");

            Assert.That(store.Sets, Is.Empty);
            Assert.That(Directory.GetDirectories(Path.Combine(root, "sets")), Is.Empty);
            assertStagingEmpty();
        }

        [Test]
        public void LeftoverStagingFromACrashIsSweptOnStart()
        {
            Directory.CreateDirectory(Path.Combine(root, "staging", "dead"));
            File.WriteAllText(Path.Combine(root, "staging", "dead", "half.osu"), "x");

            open();
            assertStagingEmpty();
        }

        [Test]
        public void ACorruptSetJsonIsReportedAndDoesNotHideTheOtherSets()
        {
            var store = open();
            var good = store.ImportOsz(osz(Fixtures.Full)).Set;
            var bad = store.ImportOsz(osz(Fixtures.Full, audio: new byte[] { 5 })).Set;
            File.WriteAllText(Path.Combine(root, "sets", bad.Id, "set.json"), "{ not json");

            var reopened = open();
            Assert.That(reopened.Sets.Select(s => s.Id), Is.EqualTo(new[] { good.Id }));
            Assert.That(reopened.Problems, Has.Count.EqualTo(1));
            Assert.That(reopened.Problems[0], Does.StartWith(bad.Id));
            Assert.That(Directory.Exists(reopened.FilesDirectory(bad.Id)), "the files of the unreadable set are left alone");
        }

        // ---- export --------------------------------------------------------------------------------------------

        [Test]
        public void ExportThenImportIntoAFreshLibraryGivesIdenticalFilesWithoutBookkeeping()
        {
            var store = open();
            var set = store.ImportOsz(osz(Fixtures.Full)).Set;
            store.SaveDifficulty(set.Id, set.Difficulties[0].File, Fixtures.Full + "\n"); // creates a backup too

            using var exported = new MemoryStream();
            store.ExportOsz(set.Id, exported);
            exported.Position = 0;
            Assert.That(new ZipArchive(exported, ZipArchiveMode.Read, true).Entries.Select(e => e.FullName),
                Is.EqualTo(new[] { "audio.mp3", "sb/star.png", "Test Song [Insane].osu" }));

            exported.Position = 0;
            string otherRoot = root + "-other";
            try
            {
                var other = new LibraryStore(otherRoot);
                var copy = other.ImportOsz(exported).Set;

                foreach (string name in new[] { "audio.mp3", "sb/star.png", "Test Song [Insane].osu" })
                    Assert.That(File.ReadAllBytes(Path.Combine(other.FilesDirectory(copy.Id), name)),
                        Is.EqualTo(File.ReadAllBytes(Path.Combine(store.FilesDirectory(set.Id), name))), name);
            }
            finally
            {
                if (Directory.Exists(otherRoot))
                    Directory.Delete(otherRoot, true);
            }
        }

        // ---- soft delete ---------------------------------------------------------------------------------------

        [Test]
        public void DeleteMovesToTrashKeepsFilesAndSurvivesRestart()
        {
            var store = open();
            var set = store.ImportOsz(osz(Fixtures.Full)).Set;

            store.Delete(set.Id);
            Assert.That(store.Sets, Is.Empty);
            Assert.That(store.Trash.Select(s => s.Id), Is.EqualTo(new[] { set.Id }));
            Assert.That(File.Exists(Path.Combine(store.FilesDirectory(set.Id), "audio.mp3")), "files stay on disk");

            var reopened = open();
            Assert.That(reopened.Sets, Is.Empty);
            Assert.That(reopened.Trash, Has.Count.EqualTo(1));

            reopened.Restore(set.Id);
            Assert.That(reopened.Sets.Select(s => s.Id), Is.EqualTo(new[] { set.Id }));
            Assert.That(reopened.Trash, Is.Empty);
        }

        [Test]
        public void ReImportingATrashedSetRestoresItInsteadOfCopyingIt()
        {
            var store = open();
            var set = store.ImportOsz(osz(Fixtures.Full)).Set;
            store.Delete(set.Id);

            var again = store.ImportOsz(osz(Fixtures.Full));
            Assert.That(again.Status, Is.EqualTo(ImportStatus.RestoredFromTrash));
            Assert.That(again.Set.Id, Is.EqualTo(set.Id));
            Assert.That(store.Sets, Has.Count.EqualTo(1));
            Assert.That(store.Trash, Is.Empty);
        }

        [Test]
        public void OnlyTrashedSetsCanBePurgedAndPurgeRemovesEverything()
        {
            var store = open();
            var set = store.ImportOsz(osz(Fixtures.Full)).Set;

            Assert.That(() => store.Purge(set.Id), Throws.TypeOf<LibraryException>());
            Assert.That(Directory.Exists(Path.Combine(root, "sets", set.Id)), "a live set must survive a purge attempt");

            store.Delete(set.Id);
            store.Purge(set.Id);
            Assert.That(Directory.Exists(Path.Combine(root, "sets", set.Id)), Is.False);
            Assert.That(store.Trash, Is.Empty);
            Assert.That(open().Trash, Is.Empty);
        }

        [Test]
        public void UnknownSetIdsAreRefused()
        {
            var store = open();
            Assert.That(() => store.Delete("nope"), Throws.TypeOf<LibraryException>());
            Assert.That(() => store.Restore("nope"), Throws.TypeOf<LibraryException>());
            Assert.That(() => store.ExportOsz("nope", new MemoryStream()), Throws.TypeOf<LibraryException>());
        }

        // ---- saving + rolling backups --------------------------------------------------------------------------

        private (LibraryStore store, SetRecord set, string file) imported(int backups = LibraryStore.DEFAULT_BACKUPS_KEPT)
        {
            var store = open(backups);
            var set = store.ImportOsz(osz(Fixtures.Full)).Set;
            return (store, set, set.Difficulties[0].File);
        }

        [Test]
        public void SaveReplacesTheFileBacksUpTheOldOneAndUpdatesTheRecord()
        {
            var (store, set, file) = imported();
            DateTimeOffset before = store.Find(set.Id)!.DateModified;
            string edited = Fixtures.Full.Replace("Version:Insane", "Version:Extra");

            store.SaveDifficulty(set.Id, file, edited);

            Assert.That(store.ReadFileText(set.Id, file), Is.EqualTo(edited));
            var backups = store.ListBackups(set.Id, file);
            Assert.That(backups, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(root, "sets", set.Id, "backups", file, backups[0])), Is.EqualTo(Fixtures.Full));

            var record = store.Find(set.Id)!;
            Assert.That(record.Difficulties[0].Name, Is.EqualTo("Extra"));
            Assert.That(record.DateModified, Is.GreaterThan(before));
            Assert.That(open().Find(set.Id)!.Difficulties[0].Name, Is.EqualTo("Extra"), "persisted to set.json");
            Assert.That(allFiles(store.FilesDirectory(set.Id)).Where(f => f.Contains(".saving-")), Is.Empty);
        }

        [Test]
        public void TheBackupRingKeepsOnlyTheNewestAndDropsTheOldest()
        {
            var (store, set, file) = imported(backups: 3);

            for (int v = 1; v <= 6; v++)
                store.SaveDifficulty(set.Id, file, Fixtures.Full + $"// v{v}\n");

            var backups = store.ListBackups(set.Id, file);
            Assert.That(backups, Has.Count.EqualTo(3));

            // 6 saves made backups of: original, v1, v2, v3, v4, v5. The newest three are v3, v4, v5.
            string read(string b) => File.ReadAllText(Path.Combine(root, "sets", set.Id, "backups", file, b));
            Assert.That(read(backups[0]), Does.EndWith("// v3\n"));
            Assert.That(read(backups[1]), Does.EndWith("// v4\n"));
            Assert.That(read(backups[2]), Does.EndWith("// v5\n"));
            Assert.That(store.ReadFileText(set.Id, file), Does.EndWith("// v6\n"));
        }

        [Test]
        public void RestoringABackupBringsTheOldTextBackAndKeepsTheReplacedVersionRecoverable()
        {
            var (store, set, file) = imported();
            store.SaveDifficulty(set.Id, file, Fixtures.Full + "// v1\n");
            store.SaveDifficulty(set.Id, file, Fixtures.Full + "// v2\n");

            string oldest = store.ListBackups(set.Id, file)[0]; // the original import
            store.RestoreBackup(set.Id, file, oldest);

            Assert.That(store.ReadFileText(set.Id, file), Is.EqualTo(Fixtures.Full));
            var backups = store.ListBackups(set.Id, file);
            Assert.That(backups, Has.Count.EqualTo(3), "v2 (the version just replaced) was backed up as well");
            Assert.That(File.ReadAllText(Path.Combine(root, "sets", set.Id, "backups", file, backups[^1])), Does.EndWith("// v2\n"));
        }

        [Test]
        public void FailedSavesLeaveTheCurrentFileAndBackupsUntouched()
        {
            var (store, set, file) = imported();
            store.SaveDifficulty(set.Id, file, Fixtures.Full + "// good\n");
            string current = store.ReadFileText(set.Id, file);
            var backupsBefore = store.ListBackups(set.Id, file);
            var filesBefore = allFiles(store.FilesDirectory(set.Id));

            Assert.That(() => store.SaveDifficulty(set.Id, file, ""), Throws.TypeOf<LibraryException>(), "empty text");
            Assert.That(() => store.SaveDifficulty(set.Id, file, "   \n"), Throws.TypeOf<LibraryException>(), "blank text");
            Assert.That(() => store.SaveDifficulty(set.Id, "../x.osu", "text"), Throws.TypeOf<LibraryException>(), "path traversal");
            Assert.That(() => store.SaveDifficulty(set.Id, "audio.mp3", "text"), Throws.TypeOf<LibraryException>(), "not a difficulty");
            Assert.That(() => store.SaveDifficulty("nope", file, "text"), Throws.TypeOf<LibraryException>(), "unknown set");
            Assert.That(() => store.RestoreBackup(set.Id, file, "../../set.json"), Throws.TypeOf<LibraryException>(), "backup traversal");
            Assert.That(() => store.RestoreBackup(set.Id, file, "999999.bak"), Throws.TypeOf<LibraryException>(), "missing backup");

            Assert.That(store.ReadFileText(set.Id, file), Is.EqualTo(current));
            Assert.That(store.ListBackups(set.Id, file), Is.EqualTo(backupsBefore));
            Assert.That(allFiles(store.FilesDirectory(set.Id)), Is.EqualTo(filesBefore));
        }

        [Test]
        public void ALeftoverHalfWrittenSaveFileIsRemovedOnStartAndNeverExported()
        {
            var (store, set, file) = imported();
            string leftover = Path.Combine(store.FilesDirectory(set.Id), file + ".saving-deadbeef");
            File.WriteAllText(leftover, "half");

            var reopened = open();
            Assert.That(File.Exists(leftover), Is.False);
            Assert.That(reopened.Find(set.Id), Is.Not.Null);
        }

        [Test]
        public void AByteOrderMarkSurvivesASaveRoundTrip()
        {
            var (store, set, file) = imported();
            string withBom = "﻿" + Fixtures.Full;

            store.SaveDifficulty(set.Id, file, withBom);

            Assert.That(File.ReadAllBytes(Path.Combine(store.FilesDirectory(set.Id), file)).Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(store.ReadFileText(set.Id, file), Is.EqualTo(withBom), "reading must not strip the BOM or the next save would lose it");
        }
    }
}
