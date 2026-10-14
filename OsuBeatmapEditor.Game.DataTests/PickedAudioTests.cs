using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class PickedAudioTests
    {
        private string dir = null!;

        [SetUp]
        public void SetUp() => dir = Path.Combine(Path.GetTempPath(), "sobe-pick-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        private string write(string name, byte[] bytes)
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name);
            File.WriteAllBytes(p, bytes);
            return p;
        }

        private static byte[] ogg() => Encoding.ASCII.GetBytes("OggS\0\u0002 padding padding");
        private static byte[] wav() => Encoding.ASCII.GetBytes("RIFF....WAVEfmt ");
        private static byte[] mp3() => new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0x21, 1, 2, 3 };

        [Test]
        public void ResolveFileNameKeepsAGoodAudioName()
        {
            Assert.That(PickedAudio.ResolveFileName("Song - Title.MP3", "audio/mpeg", null), Is.EqualTo("Song - Title.MP3"));
            Assert.That(PickedAudio.ResolveFileName("テスト.ogg", null, null), Is.EqualTo("テスト.ogg"));
        }

        [Test]
        public void ResolveFileNameStripsPathPartsAndTraversal()
        {
            Assert.That(PickedAudio.ResolveFileName("../../evil/song.mp3", null, null), Is.EqualTo("song.mp3"));
            Assert.That(PickedAudio.ResolveFileName(null, null, "primary:Music/Track.ogg"), Is.EqualTo("Track.ogg"));
            Assert.That(PickedAudio.ResolveFileName("a\\b\\c.wav", null, null), Is.EqualTo("c.wav"));
        }

        [Test]
        public void ResolveFileNameAddsAnExtensionFromTheMimeTypeWhenTheNameHasNone()
        {
            Assert.That(PickedAudio.ResolveFileName("1234", "audio/mpeg", null), Is.EqualTo("1234.mp3"));
            Assert.That(PickedAudio.ResolveFileName("track.bin", "application/ogg", null), Is.EqualTo("track.ogg"));
            Assert.That(PickedAudio.ResolveFileName(null, "audio/x-wav", "document%3A55"), Does.EndWith(".wav"));
        }

        [Test]
        public void ResolveFileNameNeverReturnsEmptyOrUnsafe()
        {
            Assert.That(PickedAudio.ResolveFileName(null, null, null), Is.Not.Empty);
            Assert.That(PickedAudio.ResolveFileName("   ", null, null), Is.Not.Empty);
            Assert.That(PickedAudio.ResolveFileName("a:b*c?.mp3", null, null), Does.Not.Contain("*").And.Not.Contain("?"));
        }

        [Test]
        public void ValidateAcceptsRealLookingFiles()
        {
            Assert.That(PickedAudio.Validate(write("a.mp3", mp3())), Is.Null);
            Assert.That(PickedAudio.Validate(write("a.ogg", ogg())), Is.Null);
            Assert.That(PickedAudio.Validate(write("a.wav", wav())), Is.Null);
        }

        [Test]
        public void ValidateRejectsWrongTypeMissingEmptyAndMislabelledFiles()
        {
            Assert.That(PickedAudio.Validate(write("a.m4a", mp3())), Does.Contain("not a supported"));
            Assert.That(PickedAudio.Validate(Path.Combine(dir, "nope.mp3")), Does.Contain("could not be found"));
            Assert.That(PickedAudio.Validate(write("e.mp3", Array.Empty<byte>())), Does.Contain("empty"));
            Assert.That(PickedAudio.Validate(write("fake.ogg", mp3())), Does.Contain("does not look like"));
            Assert.That(PickedAudio.Validate(write("fake.wav", ogg())), Does.Contain("does not look like"));
            Assert.That(PickedAudio.Validate(write("wavasmp3.mp3", wav())), Does.Contain("does not look like"));
            Assert.That(PickedAudio.Validate(write("m4a.mp3", Encoding.ASCII.GetBytes("\0\0\0\u0018ftypM4A "))), Does.Contain("does not look like"));
        }

        [Test]
        public void PickResultCancelIsNeitherAPathNorAnError()
        {
            Assert.That(new PickResult(null, null).Cancelled, Is.True);
            Assert.That(new PickResult("x", null).Cancelled, Is.False);
            Assert.That(new PickResult(null, "err").Cancelled, Is.False);
        }
    }
}
