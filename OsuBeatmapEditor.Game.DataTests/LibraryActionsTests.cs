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
    public class LibraryActionsTests
    {
        private const string file_name = "Test Song [Insane].osu";

        private string root = null!;
        private string staging = null!;
        private string fallback = null!;
        private LibraryStore store = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-act-" + Guid.NewGuid().ToString("N"));
            staging = Path.Combine(root, "tmp-staging");
            fallback = Path.Combine(root, "exports-out");
            store = new LibraryStore(Path.Combine(root, "lib"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private string importSet(string osu = null!, bool withExtras = true)
        {
            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            {
                using (var s = archive.CreateEntry(file_name).Open())
                    s.Write(new UTF8Encoding(false).GetBytes(osu ?? Fixtures.Full));
                if (withExtras)
                {
                    using (var s = archive.CreateEntry("audio.mp3").Open()) s.Write(new byte[] { 1, 2, 3 });
                    using (var s = archive.CreateEntry("bg.jpg").Open()) s.Write(new byte[] { 9, 9 });
                    using (var s = archive.CreateEntry("sub/hit.wav").Open()) s.Write(new byte[] { 7 });
                }
            }

            zip.Position = 0;
            return store.ImportOsz(zip, allowDuplicate: true).Set.Id;
        }

        // ---------------------------------------------------------------- delete / restore / purge

        [Test]
        public void DeleteMovesToTrashAndKeepsEveryFileOnDisk()
        {
            string id = importSet();

            var r = LibraryActions.Delete(store, id);

            Assert.That(r.Success, Is.True);
            Assert.That(store.Sets, Is.Empty);
            Assert.That(store.Trash.Select(s => s.Id), Is.EqualTo(new[] { id }));
            Assert.That(File.Exists(Path.Combine(store.FilesDirectory(id), file_name)), Is.True, "nothing is removed by a delete");
            Assert.That(File.Exists(Path.Combine(store.FilesDirectory(id), "audio.mp3")), Is.True);
        }

        [Test]
        public void RestoreBringsASetBackWithItsFiles()
        {
            string id = importSet();
            LibraryActions.Delete(store, id);

            Assert.That(LibraryActions.Restore(store, id).Success, Is.True);

            Assert.That(store.Sets.Select(s => s.Id), Is.EqualTo(new[] { id }));
            Assert.That(store.Trash, Is.Empty);
            Assert.That(new LibraryStore(Path.Combine(root, "lib")).Sets.Single().IsDeleted, Is.False, "persisted across a reopen");
        }

        [Test]
        public void TrashSurvivesReopeningTheLibrary()
        {
            string id = importSet();
            LibraryActions.Delete(store, id);

            var reopened = new LibraryStore(Path.Combine(root, "lib"));

            Assert.That(reopened.Sets, Is.Empty);
            Assert.That(reopened.Trash.Single().Id, Is.EqualTo(id));
        }

        [Test]
        public void PurgeIsRefusedForASetThatIsNotInTheTrash()
        {
            string id = importSet();

            var r = LibraryActions.Purge(store, id);

            Assert.That(r.Success, Is.False);
            Assert.That(store.Sets.Single().Id, Is.EqualTo(id));
            Assert.That(Directory.Exists(store.FilesDirectory(id)), Is.True);
        }

        [Test]
        public void PurgeRemovesATrashedSetForGoodAndLeavesOtherSetsAlone()
        {
            string keep = importSet();
            string gone = importSet(Fixtures.Full.Replace("Test Song", "Other Song"));
            Assert.That(gone, Is.Not.EqualTo(keep));
            LibraryActions.Delete(store, gone);
            string goneDir = store.FilesDirectory(gone);

            Assert.That(LibraryActions.Purge(store, gone).Success, Is.True);

            Assert.That(Directory.Exists(goneDir), Is.False);
            Assert.That(store.Trash, Is.Empty);
            Assert.That(store.Sets.Single().Id, Is.EqualTo(keep));
            Assert.That(File.Exists(Path.Combine(store.FilesDirectory(keep), "audio.mp3")), Is.True);
            Assert.That(new LibraryStore(Path.Combine(root, "lib")).Trash, Is.Empty);
        }

        [Test]
        public void ActionsOnAnUnknownSetReportAFailureInsteadOfThrowing()
        {
            Assert.That(LibraryActions.Delete(store, "nope").Success, Is.False);
            Assert.That(LibraryActions.Restore(store, "nope").Success, Is.False);
            Assert.That(LibraryActions.Purge(store, "nope").Success, Is.False);
        }

        // ---------------------------------------------------------------- export

        [Test]
        public void ExportWritesAValidOszWithAllAssetsAndNoLibraryInternals()
        {
            string id = importSet();
            // The save path leaves a backup behind; it must never reach the .osz.
            store.SaveDifficulty(id, file_name, Fixtures.Full.Replace("Version:Insane", "Version:Insane2"));
            Assert.That(store.ListBackups(id, file_name), Is.Not.Empty);

            var r = LibraryActions.Export(store, id, staging, fallback);

            Assert.That(r.Success, Is.True, r.Message);
            string osz = Directory.GetFiles(fallback).Single();
            Assert.That(Path.GetExtension(osz), Is.EqualTo(".osz"));

            using var zip = ZipFile.OpenRead(osz);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "audio.mp3", "bg.jpg", "sub/hit.wav", file_name }.OrderBy(n => n, StringComparer.Ordinal).ToArray()));
            Assert.That(names, Has.None.Contain("set.json").And.None.Contain("backups").And.None.EndsWith(".bak"));

            using var reader = new StreamReader(zip.GetEntry(file_name)!.Open(), new UTF8Encoding(false));
            Assert.That(reader.ReadToEnd(), Does.Contain("Version:Insane2"), "exports the current (saved) file");
        }

        [Test]
        public void ExportedOszImportsBackIntoAnotherLibraryIdentically()
        {
            string id = importSet();
            LibraryActions.Export(store, id, staging, fallback);
            string osz = Directory.GetFiles(fallback).Single();

            var other = new LibraryStore(Path.Combine(root, "other"));
            using (var fs = File.OpenRead(osz))
                other.ImportOsz(fs);

            var set = other.Sets.Single();
            Assert.That(LibraryStore.ComputeContentHash(other.FilesDirectory(set.Id)), Is.EqualTo(LibraryStore.ComputeContentHash(store.FilesDirectory(id))));
        }

        [Test]
        public void ExportNeverOverwritesAnExistingFile()
        {
            string id = importSet();
            Directory.CreateDirectory(fallback);
            string name = LibraryActions.SafeFileName(LibraryActions.DisplayName(store.Find(id))) + ".osz";
            string unrelated = Path.Combine(fallback, name);
            File.WriteAllText(unrelated, "someone else's file");

            Assert.That(LibraryActions.Export(store, id, staging, fallback).Success, Is.True);
            Assert.That(LibraryActions.Export(store, id, staging, fallback).Success, Is.True);

            Assert.That(File.ReadAllText(unrelated), Is.EqualTo("someone else's file"));
            Assert.That(Directory.GetFiles(fallback), Has.Length.EqualTo(3));
        }

        [Test]
        public void ExportLeavesNoStagingFilesBehind()
        {
            string id = importSet();

            LibraryActions.Export(store, id, staging, fallback);
            LibraryActions.Export(store, id, staging, fallback, p => "Downloads/sobe");

            Assert.That(Directory.GetFiles(staging), Is.Empty);
        }

        [Test]
        public void ExportHandsTheFinishedFileToThePublisherAndReportsWhere()
        {
            string id = importSet();
            string? seen = null;
            long size = 0;

            var r = LibraryActions.Export(store, id, staging, fallback, p =>
            {
                seen = p;
                size = new FileInfo(p).Length;
                using var z = ZipFile.OpenRead(p); // valid archive at hand-over time
                return "Downloads/sobe";
            });

            Assert.That(r.Success, Is.True);
            Assert.That(r.Message, Does.Contain("Downloads/sobe"));
            Assert.That(Path.GetExtension(seen), Is.EqualTo(".osz"));
            Assert.That(size, Is.GreaterThan(0));
            Assert.That(Directory.Exists(fallback), Is.False, "nothing written to the fallback when a publisher took it");
        }

        [Test]
        public void AFailingPublisherIsReportedAndLeavesNothingBehind()
        {
            string id = importSet();

            var r = LibraryActions.Export(store, id, staging, fallback, _ => null);

            Assert.That(r.Success, Is.False);
            Assert.That(Directory.GetFiles(staging), Is.Empty);
        }

        [Test]
        public void ExportOfAMissingSetFails()
        {
            Assert.That(LibraryActions.Export(store, "nope", staging, fallback).Success, Is.False);
        }

        [Test]
        public void SafeFileNameRemovesPathAndReservedCharacters()
        {
            Assert.That(LibraryActions.SafeFileName("a/b\\c:d*e?f\"g<h>i|j"), Is.EqualTo("a_b_c_d_e_f_g_h_i_j"));
            Assert.That(LibraryActions.SafeFileName("   "), Is.EqualTo("beatmap"));
            Assert.That(LibraryActions.SafeFileName("テスト - 曲"), Is.EqualTo("テスト - 曲"));
            Assert.That(LibraryActions.SafeFileName(new string('x', 500)).Length, Is.LessThanOrEqualTo(120));
            Assert.That(LibraryActions.SafeFileName("../..").Contains('/'), Is.False);
        }
    }
}
