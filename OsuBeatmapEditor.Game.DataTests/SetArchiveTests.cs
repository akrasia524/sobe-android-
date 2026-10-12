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
    public class SetArchiveTests
    {
        private string root = null!;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "sobe-tests-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private string dest(string name = "set") => Path.Combine(root, "sets", name);

        private static MemoryStream zipOf(params (string name, byte[] data)[] entries)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in entries)
                {
                    using var s = zip.CreateEntry(name).Open();
                    s.Write(data, 0, data.Length);
                }
            }

            ms.Position = 0;
            return ms;
        }

        private static byte[] bytes(string text) => Encoding.UTF8.GetBytes(text);

        private static byte[] random(int length, int seed)
        {
            var b = new byte[length];
            new Random(seed).NextBytes(b);
            return b;
        }

        /// <summary>No folders other than the requested ones (and no ".importing-*" leftovers) exist next to the destination.</summary>
        private void assertNothingLeftBehind()
        {
            string sets = Path.Combine(root, "sets");
            Assert.That(Directory.Exists(sets) ? Directory.GetFileSystemEntries(sets) : Array.Empty<string>(), Is.Empty);
            Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories), Is.Empty, "no file may be written anywhere");
        }

        // ---- success paths -------------------------------------------------------------------------------------

        [Test]
        public void ExtractKeepsNamesCaseSubfoldersUnicodeAndExactBytes()
        {
            var audio = random(200_000, 1);
            var png = random(5_000, 2);
            using var zip = zipOf(
                ("Song [Insane].osu", bytes(Fixtures.Full)),
                ("Audio.MP3", audio),
                ("sb/star.png", png),
                ("テスト.jpg", random(100, 3)));

            var names = SetArchive.ExtractToDirectory(zip, dest());

            Assert.That(names, Is.EqualTo(new[] { "Audio.MP3", "Song [Insane].osu", "sb/star.png", "テスト.jpg" }));
            Assert.That(File.ReadAllBytes(Path.Combine(dest(), "Audio.MP3")), Is.EqualTo(audio));
            Assert.That(File.ReadAllBytes(Path.Combine(dest(), "sb", "star.png")), Is.EqualTo(png));
            Assert.That(File.ReadAllText(Path.Combine(dest(), "Song [Insane].osu")), Is.EqualTo(Fixtures.Full));
        }

        [Test]
        public void ImportExportImportGivesIdenticalFilesAndExportIsDeterministic()
        {
            using var zip = zipOf(("a.osu", bytes(Fixtures.Full)), ("audio.mp3", random(50_000, 4)), ("sb/x.png", random(900, 5)));
            SetArchive.ExtractToDirectory(zip, dest("one"));

            using var out1 = new MemoryStream();
            using var out2 = new MemoryStream();
            SetArchive.WriteArchive(dest("one"), out1);
            SetArchive.WriteArchive(dest("one"), out2);
            Assert.That(out1.ToArray(), Is.EqualTo(out2.ToArray()), "exporting the same folder twice must give identical bytes");

            out1.Position = 0;
            var names = SetArchive.ExtractToDirectory(out1, dest("two"));
            Assert.That(names, Is.EqualTo(new[] { "a.osu", "audio.mp3", "sb/x.png" }));

            foreach (string n in names)
                Assert.That(File.ReadAllBytes(Path.Combine(dest("two"), n)), Is.EqualTo(File.ReadAllBytes(Path.Combine(dest("one"), n))), n);
        }

        [Test]
        public void AnOsuOnlyArchiveIsAccepted()
        {
            using var zip = zipOf(("only.osu", bytes(Fixtures.Full)));
            Assert.That(SetArchive.ExtractToDirectory(zip, dest()), Is.EqualTo(new[] { "only.osu" }));
        }

        [Test]
        public void FolderEntriesAndOsJunkAreIgnored()
        {
            using var zip = zipOf(("a.osu", bytes("x")), ("__MACOSX/._a.osu", bytes("junk")), (".DS_Store", bytes("junk")), ("sb/", Array.Empty<byte>()), ("Thumbs.db", bytes("junk")));
            Assert.That(SetArchive.ExtractToDirectory(zip, dest()), Is.EqualTo(new[] { "a.osu" }));
        }

        [Test]
        public void ExportSkipsHiddenInProgressFolders()
        {
            using var zip = zipOf(("a.osu", bytes("x")));
            SetArchive.ExtractToDirectory(zip, dest());
            Directory.CreateDirectory(Path.Combine(dest(), ".backup"));
            File.WriteAllText(Path.Combine(dest(), ".backup", "old.osu"), "old");

            using var output = new MemoryStream();
            SetArchive.WriteArchive(dest(), output);
            output.Position = 0;
            using var read = new ZipArchive(output);
            Assert.That(read.Entries.Select(e => e.FullName), Is.EqualTo(new[] { "a.osu" }));
        }

        // ---- hostile / broken input ----------------------------------------------------------------------------

        [TestCase("../evil.txt")]
        [TestCase("a/../../evil.txt")]
        [TestCase("..\\evil.txt")]
        [TestCase("/abs/evil.txt")]
        [TestCase("C:/evil.txt")]
        [TestCase("C:\\evil.txt")]
        [TestCase("a//b.txt")]
        [TestCase("./a.txt")]
        public void UnsafePathsAreRejectedAndNothingIsWritten(string badName)
        {
            using var zip = zipOf(("good.osu", bytes("x")), (badName, bytes("evil")));

            var ex = Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest()));
            Assert.That(ex!.Message, Does.Contain("unsafe"));
            assertNothingLeftBehind();
        }

        [Test]
        public void TwoNamesDifferingOnlyByCaseAreRejected()
        {
            using var zip = zipOf(("a.osu", bytes("1")), ("A.OSU", bytes("2")));
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest()));
            assertNothingLeftBehind();
        }

        [Test]
        public void GarbageIsRejectedAsNotAnArchive()
        {
            using var zip = new MemoryStream(random(4096, 9));
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest()));
            assertNothingLeftBehind();
        }

        [Test]
        public void ATruncatedArchiveIsRejectedAndLeavesNothing()
        {
            using var good = zipOf(("a.osu", bytes(Fixtures.Full)), ("audio.mp3", random(300_000, 6)));
            var cut = good.ToArray().Take(150_000).ToArray();

            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(new MemoryStream(cut), dest()));
            assertNothingLeftBehind();
        }

        [Test]
        public void AnEmptyArchiveIsRejected()
        {
            using var zip = zipOf();
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest()));
            assertNothingLeftBehind();
        }

        [Test]
        public void TooManyEntriesAreRejected()
        {
            using var zip = zipOf(("a.osu", bytes("1")), ("b.osu", bytes("2")), ("c.osu", bytes("3")));
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest(), new ArchiveLimits(MaxEntries: 2)));
            assertNothingLeftBehind();
        }

        [Test]
        public void AHighlyCompressedBombIsStoppedByTheSizeCaps()
        {
            using var zip = zipOf(("bomb.bin", new byte[8 * 1024 * 1024])); // 8 MB of zeros compresses to a few KB
            Assert.That(zip.Length, Is.LessThan(100_000), "fixture sanity: it really is tiny on disk");

            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest(), new ArchiveLimits(MaxEntryBytes: 1024 * 1024)));
            assertNothingLeftBehind();
        }

        [Test]
        public void TheTotalSizeCapAppliesAcrossFiles()
        {
            using var zip = zipOf(("a.bin", new byte[600_000]), ("b.bin", new byte[600_000]));
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(zip, dest(), new ArchiveLimits(MaxTotalBytes: 1_000_000)));
            assertNothingLeftBehind();
        }

        [Test]
        public void AnExistingDestinationIsNeverOverwritten()
        {
            using var first = zipOf(("a.osu", bytes("original")));
            SetArchive.ExtractToDirectory(first, dest());

            using var second = zipOf(("a.osu", bytes("replacement")));
            Assert.Throws<ArchiveException>(() => SetArchive.ExtractToDirectory(second, dest()));

            Assert.That(File.ReadAllText(Path.Combine(dest(), "a.osu")), Is.EqualTo("original"));
            Assert.That(Directory.GetFileSystemEntries(Path.Combine(root, "sets")), Has.Length.EqualTo(1), "no temp folder left behind");
        }
    }
}
