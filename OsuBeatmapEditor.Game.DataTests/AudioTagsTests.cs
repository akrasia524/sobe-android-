using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class AudioTagsTests
    {
        private const string jp_title = "溺死して死ね";
        private const string jp_artist = "ついなちゃん";

        private string dir = null!;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "sobe-tags-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private string file(string name, byte[] bytes)
        {
            string p = Path.Combine(dir, name);
            File.WriteAllBytes(p, bytes);
            return p;
        }

        // ---------------------------------------------------------------- builders

        private static byte[] syncsafe(int v) => new[] { (byte)(v >> 21 & 0x7F), (byte)(v >> 14 & 0x7F), (byte)(v >> 7 & 0x7F), (byte)(v & 0x7F) };
        private static byte[] be(int v, int n) => Enumerable.Range(0, n).Select(i => (byte)(v >> 8 * (n - 1 - i))).ToArray();

        private static byte[] textUtf16(string s) => new byte[] { 1, 0xFF, 0xFE }.Concat(new UnicodeEncoding(false, false).GetBytes(s)).ToArray();
        private static byte[] textUtf8(string s) => new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes(s)).ToArray();

        private static byte[] frame(int major, string id, byte[] data, byte flag2 = 0, byte[]? rawSizeOverride = null)
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(id));
            if (major == 2)
                ms.Write(be(data.Length, 3));
            else
            {
                ms.Write(rawSizeOverride ?? (major == 4 ? syncsafe(data.Length) : be(data.Length, 4)));
                ms.WriteByte(0);
                ms.WriteByte(flag2);
            }

            ms.Write(data);
            return ms.ToArray();
        }

        private static byte[] id3(int major, byte flags, params byte[][] frames)
        {
            byte[] body = frames.SelectMany(f => f).Concat(new byte[20]).ToArray(); // + padding
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("ID3"));
            ms.WriteByte((byte)major);
            ms.WriteByte(0);
            ms.WriteByte(flags);
            ms.Write(syncsafe(body.Length));
            ms.Write(body);
            ms.Write(new byte[] { 0xFF, 0xFB, 0x90, 0x00, 1, 2, 3, 4 }); // some audio
            return ms.ToArray();
        }

        private static byte[] oggPage(byte type, int seq, byte[][] packetPieces, bool lastPacketContinues = false)
        {
            // each element is one complete packet (or, with lastPacketContinues, the final one is left open)
            var segs = new List<byte>();
            var data = new MemoryStream();
            for (int i = 0; i < packetPieces.Length; i++)
            {
                byte[] p = packetPieces[i];
                int rem = p.Length;
                bool open = lastPacketContinues && i == packetPieces.Length - 1;
                while (rem >= 255)
                {
                    segs.Add(255);
                    rem -= 255;
                }

                if (!open)
                    segs.Add((byte)rem);
                else if (rem > 0)
                    throw new InvalidOperationException("open packets must be multiples of 255 bytes");

                data.Write(p);
            }

            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("OggS"));
            ms.WriteByte(0);
            ms.WriteByte(type);
            ms.Write(new byte[8]);
            ms.Write(BitConverter.GetBytes(1));
            ms.Write(BitConverter.GetBytes(seq));
            ms.Write(new byte[4]);
            ms.WriteByte((byte)segs.Count);
            ms.Write(segs.ToArray());
            ms.Write(data.ToArray());
            return ms.ToArray();
        }

        private static byte[] vorbisComments(string magic, params string[] comments)
        {
            var ms = new MemoryStream();
            ms.Write(magic == "vorbis" ? new byte[] { 3 }.Concat(Encoding.ASCII.GetBytes("vorbis")).ToArray() : Encoding.ASCII.GetBytes("OpusTags"));
            byte[] vendor = Encoding.UTF8.GetBytes("test");
            ms.Write(BitConverter.GetBytes(vendor.Length));
            ms.Write(vendor);
            ms.Write(BitConverter.GetBytes(comments.Length));
            foreach (string c in comments)
            {
                byte[] b = Encoding.UTF8.GetBytes(c);
                ms.Write(BitConverter.GetBytes(b.Length));
                ms.Write(b);
            }

            if (magic == "vorbis")
                ms.WriteByte(1); // framing bit
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- ID3v2

        [Test]
        public void ReadsUtf16JapaneseTagsFromId3v23()
        {
            string p = file("a.mp3", id3(3, 0, frame(3, "TIT2", textUtf16(jp_title)), frame(3, "TPE1", textUtf16(jp_artist))));

            var tags = AudioTags.Read(p);

            Assert.That(tags.Title, Is.EqualTo(jp_title));
            Assert.That(tags.Artist, Is.EqualTo(jp_artist));
        }

        [Test]
        public void ReadsUtf8TagsFromId3v24WithSyncsafeFrameSizes()
        {
            string p = file("a.mp3", id3(4, 0, frame(4, "TPE1", textUtf8("Reol")), frame(4, "TIT2", textUtf8("Streaming Heart"))));

            var tags = AudioTags.Read(p);

            Assert.That((tags.Title, tags.Artist), Is.EqualTo(("Streaming Heart", "Reol")));
        }

        [Test]
        public void ReadsId3v22ThreeLetterFrames()
        {
            string p = file("a.mp3", id3(2, 0, frame(2, "TT2", new byte[] { 0 }.Concat(Encoding.Latin1.GetBytes("Old Title")).ToArray()),
                frame(2, "TP1", new byte[] { 0 }.Concat(Encoding.Latin1.GetBytes("Old Artist")).ToArray())));

            var tags = AudioTags.Read(p);

            Assert.That((tags.Title, tags.Artist), Is.EqualTo(("Old Title", "Old Artist")));
        }

        [Test]
        public void TakesTheFirstOfSeveralNulSeparatedValuesAndIgnoresUnrelatedFrames()
        {
            byte[] two = new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes("Main\0Second")).ToArray();
            string p = file("a.mp3", id3(4, 0, frame(4, "TALB", textUtf8("Album")), frame(4, "TPE1", two)));

            var tags = AudioTags.Read(p);

            Assert.That(tags.Artist, Is.EqualTo("Main"));
            Assert.That(tags.Title, Is.Null);
        }

        [Test]
        public void FallsBackToId3v1WhenThereIsNoV2Tag()
        {
            byte[] v1 = new byte[128];
            Encoding.ASCII.GetBytes("TAG").CopyTo(v1, 0);
            Encoding.ASCII.GetBytes("Plain Title").CopyTo(v1, 3);
            Encoding.ASCII.GetBytes("Plain Artist").CopyTo(v1, 33);
            string p = file("a.mp3", new byte[300].Concat(v1).ToArray());

            var tags = AudioTags.Read(p);

            Assert.That((tags.Title, tags.Artist), Is.EqualTo(("Plain Title", "Plain Artist")));
        }

        [Test]
        public void ReturnsNothingForUntaggedGarbageEmptyAndTruncatedFiles()
        {
            Assert.That(AudioTags.Read(file("a.mp3", new byte[] { 0xFF, 0xFB, 1, 2, 3 })).HasAny, Is.False);
            Assert.That(AudioTags.Read(file("b.mp3", Array.Empty<byte>())).HasAny, Is.False);
            Assert.That(AudioTags.Read(Path.Combine(dir, "missing.mp3")).HasAny, Is.False);

            byte[] good = id3(3, 0, frame(3, "TIT2", textUtf16(jp_title)));
            for (int cut = 1; cut < good.Length; cut += 7)
                Assert.DoesNotThrow(() => AudioTags.Read(file("t.mp3", good.Take(cut).ToArray())));

            // a frame claiming to be larger than the tag must not crash
            byte[] lying = id3(3, 0, frame(3, "TIT2", textUtf16("x"), rawSizeOverride: be(0x7FFFFFF0, 4)));
            Assert.DoesNotThrow(() => AudioTags.Read(file("l.mp3", lying)));
        }

        // ---------------------------------------------------------------- Ogg

        [Test]
        public void ReadsVorbisComments()
        {
            byte[] ident = new byte[] { 1 }.Concat(Encoding.ASCII.GetBytes("vorbis")).Concat(new byte[20]).ToArray();
            string p = file("a.ogg", oggPage(2, 0, new[] { ident }).Concat(oggPage(0, 1, new[] { vorbisComments("vorbis", "title=" + jp_title, "ARTIST=" + jp_artist, "ALBUM=x") })).ToArray());

            var tags = AudioTags.Read(p);

            Assert.That((tags.Title, tags.Artist), Is.EqualTo((jp_title, jp_artist)));
        }

        [Test]
        public void ReadsOpusTags()
        {
            byte[] head = Encoding.ASCII.GetBytes("OpusHead").Concat(new byte[11]).ToArray();
            string p = file("a.ogg", oggPage(2, 0, new[] { head }).Concat(oggPage(0, 1, new[] { vorbisComments("opus", "TITLE=Opus Song", "ARTIST=Opus Artist") })).ToArray());

            Assert.That((AudioTags.Read(p).Title, AudioTags.Read(p).Artist), Is.EqualTo(("Opus Song", "Opus Artist")));
        }

        [Test]
        public void ReadsVorbisCommentsThatSpanSeveralPagesBecauseOfBigCoverArt()
        {
            string art = "METADATA_BLOCK_PICTURE=" + new string('A', 2000);
            byte[] comments = vorbisComments("vorbis", "TITLE=Big Tag", "ARTIST=Cover Artist", art);
            byte[] ident = new byte[] { 1 }.Concat(Encoding.ASCII.GetBytes("vorbis")).Concat(new byte[20]).ToArray();

            // Split the comment packet over two pages: first page carries a multiple of 255 bytes (packet left open).
            int firstLen = 255 * 4;
            byte[] page1 = oggPage(2, 0, new[] { ident, comments.Take(firstLen).ToArray() }, lastPacketContinues: true);
            byte[] page2 = oggPage(1, 1, new[] { comments.Skip(firstLen).ToArray() });

            string p = file("a.ogg", page1.Concat(page2).ToArray());

            var tags = AudioTags.Read(p);
            Assert.That((tags.Title, tags.Artist), Is.EqualTo(("Big Tag", "Cover Artist")));
        }

        [Test]
        public void MalformedOggNeverThrows()
        {
            byte[] good = oggPage(2, 0, new[] { new byte[] { 1, 2, 3 } }).Concat(oggPage(0, 1, new[] { vorbisComments("vorbis", "TITLE=T") })).ToArray();
            for (int cut = 1; cut < good.Length; cut += 5)
                Assert.DoesNotThrow(() => AudioTags.Read(file("m.ogg", good.Take(cut).ToArray())));
        }
    }
}
