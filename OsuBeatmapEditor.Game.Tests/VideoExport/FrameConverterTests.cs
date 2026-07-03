using System;
using NUnit.Framework;
using OsuBeatmapEditor.Game.VideoExport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuBeatmapEditor.Game.Tests.VideoExport
{
    [TestFixture]
    public class FrameConverterTests
    {
        [Test]
        public void CopiesSameSizeFrameVerbatim()
        {
            using var image = new Image<Rgba32>(4, 2);
            image[0, 0] = new Rgba32(10, 20, 30, 255);
            image[3, 1] = new Rgba32(40, 50, 60, 255);

            byte[] dest = new byte[4 * 2 * 4];
            FrameConverter.CopyResized(image, 4, 2, dest);

            // Top-left pixel first (top row first, RGBA order).
            Assert.That(dest[0], Is.EqualTo(10));
            Assert.That(dest[1], Is.EqualTo(20));
            Assert.That(dest[2], Is.EqualTo(30));
            Assert.That(dest[3], Is.EqualTo(255));

            // Bottom-right pixel is the last four bytes.
            Assert.That(dest[^4], Is.EqualTo(40));
            Assert.That(dest[^3], Is.EqualTo(50));
            Assert.That(dest[^2], Is.EqualTo(60));
        }

        [Test]
        public void ResizesToTargetResolution()
        {
            using var image = new Image<Rgba32>(100, 60);
            byte[] dest = new byte[20 * 10 * 4];

            Assert.DoesNotThrow(() => FrameConverter.CopyResized(image, 20, 10, dest));
        }

        [Test]
        public void RejectsWrongSizedBuffer()
        {
            using var image = new Image<Rgba32>(4, 4);
            byte[] tooSmall = new byte[10];

            Assert.Throws<ArgumentException>(() => FrameConverter.CopyResized(image, 4, 4, tooSmall));
        }
    }
}
