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
    public class LibraryImporterTests
    {
        private string root = null!;
        private LibraryStore store = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-imp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "incoming"));
            store = new LibraryStore(Path.Combine(root, "library"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private string incoming(string name) => Path.Combine(root, "incoming", name);

        private string writeOsz(string name, string osuText)
        {
            using var zip = ZipFile.Open(incoming(name), ZipArchiveMode.Create);
            using (var s = zip.CreateEntry("map.osu").Open())
                s.Write(Encoding.UTF8.GetBytes(osuText));
            using (var s = zip.CreateEntry("audio.mp3").Open())
                s.Write(new byte[] { 1, 2, 3 });
            return incoming(name);
        }

        [Test]
        public void ImportsAnOszAndNamesItForTheToast()
        {
            var outcome = LibraryImporter.ImportFile(store, writeOsz("a.osz", Fixtures.Full));

            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Message, Is.EqualTo("Imported Test Artist - Test Song"));
            Assert.That(outcome.Status, Is.EqualTo(ImportStatus.Imported));
            Assert.That(store.Sets.Select(s => s.Id), Is.EqualTo(new[] { outcome.Set!.Id }));
            Assert.That(File.Exists(Path.Combine(store.FilesDirectory(outcome.Set.Id), "audio.mp3")));
        }

        [Test]
        public void ImportingTheSameFileTwiceIsASuccessThatSaysSoAndAddsNothing()
        {
            string path = writeOsz("a.osz", Fixtures.Full);
            LibraryImporter.ImportFile(store, path);

            var again = LibraryImporter.ImportFile(store, path);

            Assert.That(again.Success, Is.True);
            Assert.That(again.Status, Is.EqualTo(ImportStatus.Duplicate));
            Assert.That(again.Message, Is.EqualTo("Test Artist - Test Song is already in your library"));
            Assert.That(store.Sets, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReImportingATrashedSetSaysItWasRestored()
        {
            string path = writeOsz("a.osz", Fixtures.Full);
            store.Delete(LibraryImporter.ImportFile(store, path).Set!.Id);

            var again = LibraryImporter.ImportFile(store, path);

            Assert.That(again.Message, Is.EqualTo("Restored Test Artist - Test Song from the trash"));
            Assert.That(store.Sets, Has.Count.EqualTo(1));
        }

        [Test]
        public void ImportsABareOsuFile()
        {
            string path = incoming("single.osu");
            File.WriteAllText(path, Fixtures.Full);

            var outcome = LibraryImporter.ImportFile(store, path);

            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Set!.Difficulties.Select(d => d.File), Is.EqualTo(new[] { "single.osu" }));
        }

        [Test]
        public void BadInputBecomesAFailureMessageAndAddsNothing()
        {
            File.WriteAllText(incoming("notes.txt"), "hi");
            File.WriteAllBytes(incoming("corrupt.osz"), new byte[] { 1, 2, 3, 4, 5, 6 });
            File.WriteAllText(incoming("taiko.osu"), Fixtures.Full.Replace("Mode: 0", "Mode: 1"));
            using (var f = File.Create(incoming("huge.osu")))
                f.SetLength(LibraryImporter.MAX_OSU_BYTES + 1);

            foreach (string path in new[] { incoming("missing.osz"), incoming("notes.txt"), incoming("corrupt.osz"), incoming("taiko.osu"), incoming("huge.osu") })
            {
                var outcome = LibraryImporter.ImportFile(store, path);
                Assert.That(outcome.Success, Is.False, path);
                Assert.That(outcome.Message, Is.Not.Empty, path);
                Assert.That(outcome.Set, Is.Null, path);
            }

            Assert.That(LibraryImporter.ImportFile(store, incoming("notes.txt")).Message, Does.Contain(".osz or .osu"));
            Assert.That(LibraryImporter.ImportFile(store, incoming("taiko.osu")).Message, Does.Contain("osu!standard"));
            Assert.That(store.Sets, Is.Empty);
            Assert.That(Directory.GetDirectories(Path.Combine(root, "library", "sets")), Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(root, "library", "staging")) ? Directory.GetFileSystemEntries(Path.Combine(root, "library", "staging")) : Array.Empty<string>(), Is.Empty);
        }
    }
}
