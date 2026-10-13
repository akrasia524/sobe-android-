using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class SetFileResolverTests
    {
        private string root = null!;
        private string folder = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-res-" + Guid.NewGuid().ToString("N"));
            folder = Path.Combine(root, "files");
            Directory.CreateDirectory(Path.Combine(folder, "sb"));
            File.WriteAllText(Path.Combine(folder, "Map [Hard].osu"), Fixtures.Full);
            File.WriteAllBytes(Path.Combine(folder, "Audio.MP3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "sb", "Star.png"), new byte[] { 2 });
            File.WriteAllText(Path.Combine(root, "secret.txt"), "outside the set folder");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private BeatmapSetModel librarySet() => new BeatmapSetModel { LibraryFilesDirectory = folder };
        private static BeatmapDifficultyModel diff(string file) => new BeatmapDifficultyModel { LibraryFile = file };

        [Test]
        public void ResolvesTheDifficultyFileOfALibrarySet() =>
            Assert.That(SetFileResolver.ResolveOsu(librarySet(), diff("Map [Hard].osu")), Is.EqualTo(Path.Combine(Path.GetFullPath(folder), "Map [Hard].osu")));

        [TestCase("Audio.MP3")]
        [TestCase("audio.mp3")]
        [TestCase("AUDIO.mp3")]
        public void ReferencedNamesMatchCaseInsensitively(string name) =>
            Assert.That(SetFileResolver.ResolveNamed(librarySet(), name), Is.EqualTo(Path.Combine(Path.GetFullPath(folder), "Audio.MP3")));

        [TestCase("sb/star.png")]
        [TestCase("SB/Star.png")]
        [TestCase("sb\\Star.png")]
        public void SubfoldersAndBackslashesWork(string name)
        {
            // "SB/" differs from "sb/" in case: only the file name part is matched case-insensitively, so that one is expected to be null on a case-sensitive file system.
            string? result = SetFileResolver.ResolveNamed(librarySet(), name);
            if (name.StartsWith("SB/", StringComparison.Ordinal))
                Assert.That(result, Is.Null.Or.EqualTo(Path.Combine(Path.GetFullPath(folder), "sb", "Star.png")));
            else
                Assert.That(result, Is.EqualTo(Path.Combine(Path.GetFullPath(folder), "sb", "Star.png")));
        }

        [TestCase("../secret.txt")]
        [TestCase("..\\secret.txt")]
        [TestCase("sb/../../secret.txt")]
        [TestCase("/etc/passwd")]
        [TestCase("C:\\Windows\\win.ini")]
        [TestCase("")]
        public void NamesThatEscapeTheSetFolderAreRefused(string name)
        {
            Assert.That(SetFileResolver.ResolveNamed(librarySet(), name), Is.Null);
            Assert.That(SetFileResolver.ResolveOsu(librarySet(), diff(name)), Is.Null);
        }

        [Test]
        public void MissingFilesResolveToNull()
        {
            Assert.That(SetFileResolver.ResolveNamed(librarySet(), "nope.mp3"), Is.Null);
            Assert.That(SetFileResolver.ResolveNamed(librarySet(), "sb/nope.png"), Is.Null);
            Assert.That(SetFileResolver.ResolveNamed(librarySet(), "missing-folder/a.png"), Is.Null);
        }

        [Test]
        public void LazerSetsStillResolveThroughTheHashStore()
        {
            const string hash = "abcdef0123456789";
            string stored = Path.Combine(root, "files", "a", "ab", hash);
            Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
            File.WriteAllBytes(stored, new byte[] { 3 });

            var set = new BeatmapSetModel
            {
                DataDirectory = root,
                Files = new System.Collections.Generic.Dictionary<string, string> { ["audio.mp3"] = hash },
            };

            Assert.That(set.IsLibrarySet, Is.False);
            Assert.That(SetFileResolver.ResolveNamed(set, "Audio.MP3"), Is.EqualTo(stored));
            Assert.That(SetFileResolver.ResolveOsu(set, new BeatmapDifficultyModel { OsuFileHash = hash }), Is.EqualTo(stored));
            Assert.That(SetFileResolver.ResolveNamed(set, "other.mp3"), Is.Null);
            Assert.That(SetFileResolver.ResolveNamed(new BeatmapSetModel(), "audio.mp3"), Is.Null);
        }

        [Test]
        public void TheLibraryListingCarriesTheFolderAndFileNames()
        {
            var store = new LibraryStore(Path.Combine(root, "lib"));
            var zip = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                using var s = archive.CreateEntry("Song [Insane].osu").Open();
                s.Write(System.Text.Encoding.UTF8.GetBytes(Fixtures.Full));
            }

            zip.Position = 0;
            store.ImportOsz(zip);

            var model = LibraryBeatmapSets.ToModels(store).Single();
            Assert.That(model.IsLibrarySet, Is.True);
            string? path = SetFileResolver.ResolveOsu(model, model.Difficulties[0]);
            Assert.That(path, Is.Not.Null);
            Assert.That(File.ReadAllText(path!), Is.EqualTo(Fixtures.Full));
        }
    }
}
