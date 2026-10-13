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
    public class LibrarySaverTests
    {
        private const string file_name = "Test Song [Insane].osu";

        private string root = null!;
        private LibraryStore store = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "sobe-save-" + Guid.NewGuid().ToString("N"));
            store = new LibraryStore(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private (BeatmapSetModel set, BeatmapDifficultyModel diff, ParsedBeatmap parsed, BeatmapSaver.Edits edits) open(string osuText)
        {
            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            {
                using (var s = archive.CreateEntry(file_name).Open())
                    s.Write(new UTF8Encoding(false).GetBytes(osuText));
                using (var s = archive.CreateEntry("audio.mp3").Open())
                    s.Write(new byte[] { 1, 2, 3 });
            }

            zip.Position = 0;
            store.ImportOsz(zip);

            var set = LibraryBeatmapSets.ToModels(store).Single();
            var diff = set.Difficulties.Single();
            var parsed = Fixtures.Decode(store.ReadFileText(set.LibrarySetId, file_name));
            return (set, diff, parsed, Fixtures.EditsFor(parsed));
        }

        private string stored(BeatmapSetModel set) => store.ReadFileText(set.LibrarySetId, file_name);

        [Test]
        public void ASaveWithNoRealEditChangesNothingAndSpendsNoBackup()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);

            Assert.That(LibrarySaver.Save(set, diff, parsed, edits, false), Is.Null);

            Assert.That(stored(set), Is.EqualTo(Fixtures.Full));
            Assert.That(store.ListBackups(set.LibrarySetId, file_name), Is.Empty);
        }

        [Test]
        public void ARealEditIsSavedBackedUpAndEverythingElseIsPreserved()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);

            edits.Title = "Edited Title";
            parsed.Bookmarks.Add(4000);
            var h = parsed.HitObjects[1];
            parsed.HitObjects[1] = h with { RawLine = HitObjectLineEditor.ShiftPosition(h.RawLine, 8, -4) };
            int t = parsed.TimingPointModels.FindIndex(x => x.Time == 5000);
            parsed.TimingPointModels[t] = parsed.TimingPointModels[t] with { Volume = 90 };

            Assert.That(LibrarySaver.Save(set, diff, parsed, edits, false), Is.Null);

            string saved = stored(set);
            string expected = Fixtures.Full
                .Replace("Title:Test Song", "Title:Edited Title")
                .Replace("Bookmarks: 1000,2000,3000", "Bookmarks: 1000,2000,3000,4000")
                .Replace("100,100,1500,6,2,B|200:100|300:200,", "108,96,1500,6,2,B|208:96|308:196,")
                .Replace("5000,-50,4,2,1,70,0,1", "5000,-50,4,2,1,90,0,1");
            Assert.That(saved, Is.EqualTo(expected), "exactly the four edits, nothing else (video, storyboard, unknown keys, long precision, hitsamples, [CustomSection])");

            var backups = store.ListBackups(set.LibrarySetId, file_name);
            Assert.That(backups, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(root, "sets", set.LibrarySetId, "backups", file_name, backups[0])), Is.EqualTo(Fixtures.Full));

            Assert.That(diff.OsuFileHash, Is.EqualTo(store.Find(set.LibrarySetId)!.Difficulties[0].Sha256), "snapshot follows the stored file");
        }

        [Test]
        public void TheSavedEditSurvivesReopeningTheLibrary()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);
            edits.Version = "Renamed";
            Assert.That(LibrarySaver.Save(set, diff, parsed, edits, false), Is.Null);

            var reopened = new LibraryStore(root);
            Assert.That(Fixtures.Decode(reopened.ReadFileText(set.LibrarySetId, file_name)).Version, Is.EqualTo("Renamed"));
            Assert.That(LibraryBeatmapSets.ToModels(reopened).Single().Difficulties[0].DifficultyName, Is.EqualTo("Renamed"));
        }

        [Test]
        public void ARevertingSecondSaveRestoresTheOriginalTextAndKeepsBothBackups()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);
            edits.Version = "Renamed";
            LibrarySaver.Save(set, diff, parsed, edits, false);

            var parsed2 = Fixtures.Decode(stored(set));
            var edits2 = Fixtures.EditsFor(parsed2);
            edits2.Version = "Insane";
            Assert.That(LibrarySaver.Save(set, diff, parsed2, edits2, false), Is.Null);

            Assert.That(stored(set), Is.EqualTo(Fixtures.Full), "a reversible edit: setting the value back gives back the exact original file");
            Assert.That(store.ListBackups(set.LibrarySetId, file_name), Has.Count.EqualTo(2));
        }

        [Test]
        public void AByteOrderMarkAndCrlfLineEndingsSurviveAnEdit()
        {
            string original = "﻿" + Fixtures.Full.Replace("\n", "\r\n");
            var (set, diff, parsed, edits) = open(original);

            edits.Tags = "changed tags";
            Assert.That(LibrarySaver.Save(set, diff, parsed, edits, false), Is.Null);

            Assert.That(stored(set), Is.EqualTo(original.Replace("Tags:tag1 tag2", "Tags:changed tags")));
            Assert.That(File.ReadAllBytes(Path.Combine(store.FilesDirectory(set.LibrarySetId), file_name)).Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Test]
        public void ADroppedBackgroundIsRefusedClearlyAndNothingChanges()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);
            edits.Title = "Would be saved";

            string? error = LibrarySaver.Save(set, diff, parsed, edits, hasPendingBackground: true);

            Assert.That(error, Does.Contain("background").And.Contain("Nothing was saved"));
            Assert.That(stored(set), Is.EqualTo(Fixtures.Full));
            Assert.That(store.ListBackups(set.LibrarySetId, file_name), Is.Empty);
        }

        [Test]
        public void ASaveThatWouldCorruptAHitObjectIsRefusedByTheCheck()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);
            parsed.HitObjects[0] = parsed.HitObjects[0] with { RawLine = "this is not a hit object" };

            string? error = LibrarySaver.Save(set, diff, parsed, edits, false);

            Assert.That(error, Is.Not.Null.And.Contain("Nothing was saved"));
            Assert.That(stored(set), Is.EqualTo(Fixtures.Full));
            Assert.That(store.ListBackups(set.LibrarySetId, file_name), Is.Empty);
        }

        [Test]
        public void ASetThatIsNotInTheLibraryIsRefused()
        {
            var (_, diff, parsed, edits) = open(Fixtures.Full);
            Assert.That(LibrarySaver.Save(new BeatmapSetModel(), diff, parsed, edits, false), Is.EqualTo("This beatmap is not in the library."));
        }

        [Test]
        public void TheSharedPatchedTextBuilderServesLibrarySetsToo()
        {
            var (set, diff, parsed, edits) = open(Fixtures.Full);
            edits.Source = "Other Game";

            string? patched = BeatmapSaver.BuildPatchedOsu(set, diff, parsed, edits);

            Assert.That(patched, Is.EqualTo(Fixtures.Full.Replace("Source:Game", "Source:Other Game")));
            Assert.That(stored(set), Is.EqualTo(Fixtures.Full), "building the text does not save it");
        }
    }
}
