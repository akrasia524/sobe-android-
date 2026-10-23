using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class LibraryCreatorTests
    {
        private string root = null!;
        private string lib = null!;
        private LibraryStore store = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-create-" + Guid.NewGuid().ToString("N"));
            lib = Path.Combine(root, "library");
            Directory.CreateDirectory(root);
            store = new LibraryStore(lib);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private string audio(string name = "My Song.mp3", byte[]? bytes = null)
        {
            string p = Path.Combine(root, name);
            File.WriteAllBytes(p, bytes ?? new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0x21, 9, 9, 9 });
            return p;
        }

        private static NewBeatmapRequest request(string audioPath) => new NewBeatmapRequest
        {
            Artist = "テスト Artist",
            Title = "My Song",
            Creator = "me",
            DifficultyName = "Normal",
            Bpm = 140,
            AudioPath = audioPath,
        };

        private string[] libraryTemp() => new[] { "pending-audio", "create-temp" }
            .Select(n => Path.Combine(lib, n)).Where(Directory.Exists).SelectMany(d => Directory.GetFileSystemEntries(d, "*", SearchOption.AllDirectories)).ToArray();

        // ---------------------------------------------------------------- staging

        [Test]
        public void StageAudioCopiesTheFileIntoTheLibraryAndKeepsTheOriginal()
        {
            string src = audio();

            var (path, error) = LibraryCreator.StageAudio(lib, src);

            Assert.That(error, Is.Null);
            Assert.That(path, Does.StartWith(Path.Combine(lib, "pending-audio")));
            Assert.That(File.ReadAllBytes(path!), Is.EqualTo(File.ReadAllBytes(src)));
            Assert.That(File.Exists(src), Is.True);
            Assert.That(Path.GetFileName(path), Is.EqualTo("My Song.mp3"));
        }

        [Test]
        public void StageAudioRejectsABadFileAndLeavesNothingBehind()
        {
            var (path, error) = LibraryCreator.StageAudio(lib, audio("notes.txt", new byte[] { 1, 2, 3 }));

            Assert.That(path, Is.Null);
            Assert.That(error, Does.Contain("not a supported"));
            Assert.That(libraryTemp(), Is.Empty);
        }

        [Test]
        public void DiscardStagedRemovesOnlyStagedFiles()
        {
            string src = audio();
            string staged = LibraryCreator.StageAudio(lib, src).Path!;

            LibraryCreator.DiscardStaged(lib, src); // outside the pending area: ignored
            Assert.That(File.Exists(src), Is.True);

            LibraryCreator.DiscardStaged(lib, staged);
            Assert.That(File.Exists(staged), Is.False);
            Assert.That(libraryTemp(), Is.Empty);

            LibraryCreator.DiscardStaged(lib, null); // no-op
            LibraryCreator.DiscardStaged(lib, Path.Combine(lib, "pending-audio", "x.mp3")); // directly in pending: ignored, no throw
        }

        [Test]
        public void SweepLeftoversClearsUnfinishedPicksAndExports()
        {
            LibraryCreator.StageAudio(lib, audio());
            Directory.CreateDirectory(Path.Combine(lib, "export-temp"));
            File.WriteAllText(Path.Combine(lib, "export-temp", "x.osz"), "x");

            LibraryCreator.SweepLeftovers(lib);

            Assert.That(Directory.Exists(Path.Combine(lib, "pending-audio")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(lib, "export-temp")), Is.False);
            Assert.That(store.Sets, Is.Empty);
        }

        // ---------------------------------------------------------------- creating

        [Test]
        public void CreateAddsACompleteSetWithTheAudioAndAReadableMap()
        {
            string staged = LibraryCreator.StageAudio(lib, audio()).Path!;

            var outcome = LibraryCreator.Create(store, request(staged));

            Assert.That(outcome.Success, Is.True, outcome.Message);
            var set = store.Sets.Single();
            Assert.That(set.Difficulties, Has.Count.EqualTo(1));

            string files = store.FilesDirectory(set.Id);
            Assert.That(File.ReadAllBytes(Path.Combine(files, "audio.mp3")), Is.EqualTo(File.ReadAllBytes(staged)));

            var parsed = Fixtures.Decode(store.ReadFileText(set.Id, set.Difficulties[0].File));
            Assert.That(parsed.Artist, Is.EqualTo("テスト Artist"));
            Assert.That(parsed.Title, Is.EqualTo("My Song"));
            Assert.That(parsed.Version, Is.EqualTo("Normal"));
            Assert.That(store.ReadFileText(set.Id, set.Difficulties[0].File), Does.Contain("AudioFilename: audio.mp3").Or.Contain("AudioFilename:audio.mp3"));
            Assert.That(Directory.Exists(Path.Combine(lib, "create-temp")) ? Directory.GetFiles(Path.Combine(lib, "create-temp")) : Array.Empty<string>(), Is.Empty, "no temp archive left behind");
            LibraryCreator.DiscardStaged(lib, staged); // the screen does this once the set exists
            Assert.That(libraryTemp(), Is.Empty);
        }

        [Test]
        public void CreateWithABackgroundStoresTheImageAndPointsTheMapAtIt()
        {
            string staged = LibraryCreator.StageAudio(lib, audio()).Path!;
            string img = Path.Combine(root, "cover.jpg");
            File.WriteAllBytes(img, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 1, 2, 3 });

            var req = request(staged);
            req.BackgroundPath = img;
            var outcome = LibraryCreator.Create(store, req);

            Assert.That(outcome.Success, Is.True, outcome.Message);
            var set = store.Sets.Single();
            Assert.That(File.ReadAllBytes(Path.Combine(store.FilesDirectory(set.Id), "cover.jpg")), Is.EqualTo(File.ReadAllBytes(img)));
            var parsed = Fixtures.Decode(store.ReadFileText(set.Id, set.Difficulties[0].File));
            Assert.That(parsed.BackgroundFilename, Is.EqualTo("cover.jpg"));
        }

        [Test]
        public void CreateWithoutABackgroundStillWorksAndHasNone()
        {
            string staged = LibraryCreator.StageAudio(lib, audio()).Path!;

            Assert.That(LibraryCreator.Create(store, request(staged)).Success, Is.True);
            var set = store.Sets.Single();
            Assert.That(Fixtures.Decode(store.ReadFileText(set.Id, set.Difficulties[0].File)).BackgroundFilename, Is.Empty);
        }

        [Test]
        public void ABadBackgroundIsRefusedAndNothingIsCreated()
        {
            string staged = LibraryCreator.StageAudio(lib, audio()).Path!;
            string img = Path.Combine(root, "notes.png");
            File.WriteAllBytes(img, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            var req = request(staged);
            req.BackgroundPath = img;
            var outcome = LibraryCreator.Create(store, req);

            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Message, Does.Contain("Background"));
            Assert.That(store.Sets, Is.Empty);
        }

        [Test]
        public void CreateSurvivesAReopenAndTwoIdenticalCreatesAreBothKept()
        {
            string a = audio();
            Assert.That(LibraryCreator.Create(store, request(a)).Success, Is.True);
            Assert.That(LibraryCreator.Create(store, request(a)).Success, Is.True);

            Assert.That(new LibraryStore(lib).Sets, Has.Count.EqualTo(2));
        }

        [Test]
        public void CreateWithMissingAudioFailsAndAddsNothing()
        {
            var outcome = LibraryCreator.Create(store, request(Path.Combine(root, "gone.mp3")));

            Assert.That(outcome.Success, Is.False);
            Assert.That(store.Sets, Is.Empty);
            Assert.That(libraryTemp(), Is.Empty);
        }

        [Test]
        public void CreateWithAnUnusableAudioFileFailsAndAddsNothing()
        {
            var outcome = LibraryCreator.Create(store, request(audio("fake.ogg", new byte[] { 1, 2, 3, 4, 5 })));

            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Message, Does.Contain("does not look like"));
            Assert.That(store.Sets, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(lib, "sets")) ? Directory.GetDirectories(Path.Combine(lib, "sets")) : Array.Empty<string>(), Is.Empty);
        }

        [Test]
        public void CreateWithIncompleteMetadataFailsAndAddsNothing()
        {
            var req = request(audio());
            req.Title = "";

            var outcome = LibraryCreator.Create(store, req);

            Assert.That(outcome.Success, Is.False);
            Assert.That(store.Sets, Is.Empty);
        }

        [Test]
        public void CreateFailingInsideTheStoreLeavesNoPartialSet()
        {
            // Force the import to fail: a file in the way of the temp folder name makes the archive step throw an IOException.
            File.WriteAllText(Path.Combine(lib, "create-temp"), "blocker");

            var outcome = LibraryCreator.Create(store, request(audio()));

            Assert.That(outcome.Success, Is.False);
            Assert.That(store.Sets, Is.Empty);
            Assert.That(Directory.GetDirectories(Path.Combine(lib, "sets")), Is.Empty);
        }
    }
}
