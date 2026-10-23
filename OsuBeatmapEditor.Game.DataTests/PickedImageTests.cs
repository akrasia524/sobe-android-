using System.IO;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Library;

namespace OsuBeatmapEditor.Game.DataTests
{
    public class PickedImageTests
    {
        private static readonly byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F' };
        private static readonly byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 };

        [Test]
        public void SniffsTheRealTypeFromTheBytes()
        {
            Assert.That(PickedImage.SniffExtension(jpeg, jpeg.Length), Is.EqualTo(".jpg"));
            Assert.That(PickedImage.SniffExtension(png, png.Length), Is.EqualTo(".png"));
            Assert.That(PickedImage.SniffExtension(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 8), Is.Null);
            Assert.That(PickedImage.SniffExtension(new byte[] { 0xFF, 0xD8 }, 2), Is.Null);
        }

        [Test]
        public void TheNameKeepsItsStemButTakesTheRealExtension()
        {
            Assert.That(PickedImage.FinalName("cover.PNG", null, ".jpg"), Is.EqualTo("cover.jpg"));
            Assert.That(PickedImage.FinalName("a/b\\c:bg.webp", null, ".png"), Is.EqualTo("bg.png"));
            Assert.That(PickedImage.FinalName(null, null, ".jpg"), Is.EqualTo("background.jpg"));
        }

        [Test]
        public void ValidateAcceptsRealImagesAndRefusesTheRest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pi-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "a.jpg");
                File.WriteAllBytes(a, jpeg);
                Assert.That(PickedImage.Validate(a), Is.Null);

                string b = Path.Combine(dir, "b.jpg");
                File.WriteAllBytes(b, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
                Assert.That(PickedImage.Validate(b), Does.Contain("not a .jpg or .png"));

                string c = Path.Combine(dir, "c.png");
                File.WriteAllBytes(c, new byte[0]);
                Assert.That(PickedImage.Validate(c), Does.Contain("empty"));

                Assert.That(PickedImage.Validate(Path.Combine(dir, "missing.png")), Does.Contain("could not be found"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
