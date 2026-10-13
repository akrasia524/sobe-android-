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
    public class LibraryBeatmapSetsTests
    {
        private string root = null!;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "sobe-list-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static MemoryStream osz(string osuText)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            using (var s = zip.CreateEntry("map.osu").Open())
                s.Write(Encoding.UTF8.GetBytes(osuText));

            ms.Position = 0;
            return ms;
        }

        [Test]
        public void ImportedSetsAppearWithTheDataTheListShows()
        {
            var store = new LibraryStore(root);
            var imported = store.ImportOsz(osz(Fixtures.Full)).Set;

            var models = LibraryBeatmapSets.ToModels(store);

            Assert.That(models, Has.Count.EqualTo(1));
            var m = models[0];
            Assert.That(m.Title, Is.EqualTo("Test Song"));
            Assert.That(m.Artist, Is.EqualTo("Test Artist"));
            Assert.That(m.Author, Is.EqualTo("Mapper"));
            Assert.That(m.OnlineID, Is.EqualTo(456));
            Assert.That(m.Difficulties.Select(d => d.DifficultyName), Is.EqualTo(new[] { "Insane" }));
            Assert.That(m.Difficulties[0].RulesetShortName, Is.EqualTo("osu"));
            Assert.That(m.Difficulties[0].AudioFile, Is.EqualTo("audio.mp3"));
            Assert.That(m.Difficulties[0].PreviewTime, Is.EqualTo(12345));
            Assert.That(m.DateAdded, Is.EqualTo(imported.DateAdded));
            Assert.That(m.SearchText, Is.EqualTo("test artist test song mapper insane"));
        }

        [Test]
        public void TrashedSetsAreNotListed()
        {
            var store = new LibraryStore(root);
            store.Delete(store.ImportOsz(osz(Fixtures.Full)).Set.Id);

            Assert.That(LibraryBeatmapSets.ToModels(store), Is.Empty);
        }

        [Test]
        public void AnEmptyLibraryListsNothing() => Assert.That(LibraryBeatmapSets.ToModels(new LibraryStore(root)), Is.Empty);
    }
}
