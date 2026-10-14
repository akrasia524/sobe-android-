using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps.Difficulty;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class LibraryStarRatingTests
    {
        private const string file_name = "Test Song [Insane].osu";

        private string root = null!;
        private LibraryStore store = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-stars-" + Guid.NewGuid().ToString("N"));
            store = new LibraryStore(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private string import(string osu)
        {
            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            {
                using var s = archive.CreateEntry(file_name).Open();
                s.Write(new UTF8Encoding(false).GetBytes(osu));
            }

            zip.Position = 0;
            return store.ImportOsz(zip, allowDuplicate: true).Set.Id;
        }

        private static double expected(string osu) => StarRatingCalculator.Calculate(Fixtures.Decode(osu));

        [Test]
        public void ImportStoresTheCalculatedStarRatingAndTheListShowsIt()
        {
            string id = import(Fixtures.Full);

            double stars = store.Find(id)!.Difficulties.Single().StarRating;

            Assert.That(expected(Fixtures.Full), Is.GreaterThan(0), "the fixture is rateable");
            Assert.That(stars, Is.EqualTo(expected(Fixtures.Full)).Within(0.0001));
            Assert.That(LibraryBeatmapSets.ToModels(store).Single().Difficulties.Single().StarRating, Is.EqualTo(stars));
            Assert.That(new LibraryStore(root).Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(stars).Within(0.0001), "persisted");
        }

        [Test]
        public void AMapWithoutEnoughObjectsGetsZeroInsteadOfFailing()
        {
            string empty = Fixtures.Full.Substring(0, Fixtures.Full.IndexOf("[HitObjects]", StringComparison.Ordinal)) + "[HitObjects]\n";

            string id = import(empty);

            Assert.That(store.Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(0));
        }

        [Test]
        public void SavingRecalculatesTheStarRating()
        {
            string id = import(Fixtures.Full);
            double before = store.Find(id)!.Difficulties.Single().StarRating;

            // Make the map much harder: pack many circles close together in time.
            string harder;
            harder = Fixtures.Full.Replace("[CustomSection]", string.Concat(Enumerable.Range(0, 80).Select(i => $"{(i * 97) % 500},{(i * 53) % 380},{20000 + i * 90},1,0,0:0:0:0:\n")) + "\n[CustomSection]");

            store.SaveDifficulty(id, file_name, harder);

            double after = store.Find(id)!.Difficulties.Single().StarRating;
            Assert.That(after, Is.EqualTo(expected(harder)).Within(0.0001));
            Assert.That(after, Is.Not.EqualTo(before).Within(0.01));
        }

        [Test]
        public void BackfillRatesSetsThatHaveNoStoredRatingAndPersistsIt()
        {
            string id = import(Fixtures.Full);
            double real = store.Find(id)!.Difficulties.Single().StarRating;

            // Simulate a set imported before ratings were stored.
            string json = Path.Combine(root, "sets", id, "set.json");
            File.WriteAllText(json, Regex.Replace(File.ReadAllText(json), "\"StarRating\":\\s*[-0-9.eE]+", "\"StarRating\": 0"));
            var old = new LibraryStore(root);
            Assert.That(old.Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(0));

            Assert.That(old.BackfillStarRatings(), Is.EqualTo(1));
            Assert.That(old.Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(real).Within(0.0001));
            Assert.That(new LibraryStore(root).Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(real).Within(0.0001), "written to set.json");

            Assert.That(old.BackfillStarRatings(), Is.EqualTo(0), "nothing left to do");
        }

        [Test]
        public void BackfillLeavesTheFilesAndOtherFieldsUntouched()
        {
            string id = import(Fixtures.Full);
            string json = Path.Combine(root, "sets", id, "set.json");
            File.WriteAllText(json, Regex.Replace(File.ReadAllText(json), "\"StarRating\":\\s*[-0-9.eE]+", "\"StarRating\": 0"));
            var old = new LibraryStore(root);
            string hashBefore = LibraryStore.ComputeContentHash(old.FilesDirectory(id));
            var recBefore = old.Find(id)!;
            string sha = recBefore.Difficulties.Single().Sha256;

            old.BackfillStarRatings();

            Assert.That(LibraryStore.ComputeContentHash(old.FilesDirectory(id)), Is.EqualTo(hashBefore));
            Assert.That(old.Find(id)!.Difficulties.Single().Sha256, Is.EqualTo(sha));
            Assert.That(old.ListBackups(id, file_name), Is.Empty);
        }

        [Test]
        public void RestoringABackupRecalculatesTheRating()
        {
            string id = import(Fixtures.Full);
            double original = store.Find(id)!.Difficulties.Single().StarRating;
            string harder = Fixtures.Full.Replace("[CustomSection]", string.Concat(Enumerable.Range(0, 80).Select(i => $"{(i * 97) % 500},{(i * 53) % 380},{20000 + i * 90},1,0,0:0:0:0:\n")) + "\n[CustomSection]");
            store.SaveDifficulty(id, file_name, harder);

            store.RestoreBackup(id, file_name, store.ListBackups(id, file_name).Last());

            Assert.That(store.Find(id)!.Difficulties.Single().StarRating, Is.EqualTo(original).Within(0.0001));
        }
    }
}
