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
    public class LibraryResourceStoreTests
    {
        private const string file_name = "Test Song [Insane].osu";

        private string root = null!;
        private LibraryStore store = null!;
        private string setId = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-res-" + Guid.NewGuid().ToString("N"));
            store = new LibraryStore(root);

            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            {
                using (var s = archive.CreateEntry(file_name).Open()) s.Write(new UTF8Encoding(false).GetBytes(Fixtures.Full));
                using (var s = archive.CreateEntry("audio.mp3").Open()) s.Write(new byte[] { 1, 2, 3, 4 });
                using (var s = archive.CreateEntry("BG.JPG").Open()) s.Write(new byte[] { 9, 8, 7 });
                using (var s = archive.CreateEntry("sub/pic.png").Open()) s.Write(new byte[] { 5 });
            }

            zip.Position = 0;
            setId = store.ImportOsz(zip).Set.Id;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static string listStyle(string key) => $"{key[..1]}/{key[..2]}/{key}"; // what the song list asks for

        [Test]
        public void SetModelsMapBackgroundAndAudioToStoreKeysWithoutTouchingTheDisk()
        {
            var set = LibraryBeatmapSets.ToModels(store).Single();

            Assert.That(set.Files.ContainsKey("audio.mp3"), Is.True, "audio is listed under its lower-case name");
            Assert.That(set.Files.ContainsKey("bg.jpg"), Is.True, "the .osu references \"bg.jpg\"; keys are lower-case");
            Assert.That(set.Files["audio.mp3"], Is.EqualTo(LibraryResourceStore.KeyFor(setId, "audio.mp3")));
            Assert.That(set.Files["audio.mp3"].Length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ReadsFilesThroughTheListStylePath()
        {
            var res = new LibraryResourceStore(store);
            var set = LibraryBeatmapSets.ToModels(store).Single();

            Assert.That(res.Get(listStyle(set.Files["audio.mp3"])), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            using var stream = res.GetStream(listStyle(set.Files["bg.jpg"]))!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }), "case-insensitive match: BG.JPG on disk, bg.jpg requested");
            Assert.That(res.GetAsync(listStyle(set.Files["audio.mp3"])).Result, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void AlsoAcceptsTheBareKeyAndSubfolders()
        {
            var res = new LibraryResourceStore(store);

            Assert.That(res.Get(LibraryResourceStore.KeyFor(setId, "audio.mp3")), Is.Not.Null);
            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, "sub/pic.png"))), Is.EqualTo(new byte[] { 5 }));
        }

        [Test]
        public void UnknownMissingAndMalformedNamesReturnNothing()
        {
            var res = new LibraryResourceStore(store);

            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, "nope.mp3"))), Is.Null);
            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor("unknown-set", "audio.mp3"))), Is.Null);
            Assert.That(res.GetStream("garbage"), Is.Null);
            Assert.That(res.Get(""), Is.Null);
            Assert.That(res.Get(listStyle(setId + "|")), Is.Null);
            Assert.That(res.Get("|audio.mp3"), Is.Null);
        }

        [Test]
        public void CannotEscapeTheSetFolder()
        {
            string outside = Path.Combine(root, "secret.txt");
            File.WriteAllText(outside, "secret");
            var res = new LibraryResourceStore(store);

            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, "../../../secret.txt"))), Is.Null);
            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, "..\\..\\..\\secret.txt"))), Is.Null);
            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, outside))), Is.Null);
            Assert.That(res.Get(listStyle(LibraryResourceStore.KeyFor(setId, "../set.json"))), Is.Null);
        }

        [Test]
        public void FilesOfTrashedSetsAreNotServed()
        {
            var res = new LibraryResourceStore(store);
            string key = listStyle(LibraryResourceStore.KeyFor(setId, "audio.mp3"));
            Assert.That(res.Get(key), Is.Not.Null);

            store.Delete(setId);
            Assert.That(res.Get(key), Is.Null);

            store.Restore(setId);
            Assert.That(res.Get(key), Is.Not.Null);
        }

        [Test]
        public void TryParseSplitsKeysAndRejectsJunk()
        {
            Assert.That(LibraryResourceStore.TryParse("a/ab/abcd|dir/file.ogg", out string id, out string file), Is.True);
            Assert.That((id, file), Is.EqualTo(("abcd", "dir/file.ogg")));
            Assert.That(LibraryResourceStore.TryParse("abcd|f.mp3", out id, out file), Is.True);
            Assert.That((id, file), Is.EqualTo(("abcd", "f.mp3")));
            Assert.That(LibraryResourceStore.TryParse("nobar", out _, out _), Is.False);
            Assert.That(LibraryResourceStore.TryParse("abcd|", out _, out _), Is.False);
            Assert.That(LibraryResourceStore.TryParse(null!, out _, out _), Is.False);
        }
    }
}
