using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Turns a captured screenshot into the raw RGBA byte frame the encoder expects: resized to the target
    /// resolution (only when needed) and copied top-row-first into a caller-owned, reused buffer so the capture
    /// loop allocates nothing per frame.
    /// </summary>
    public static class FrameConverter
    {
        /// <summary>
        /// Resizes <paramref name="source"/> to <paramref name="width"/>x<paramref name="height"/> if it isn't already,
        /// then copies its RGBA bytes into <paramref name="dest"/> (which must be exactly <c>width*height*4</c> bytes).
        /// </summary>
        public static void CopyResized(Image<Rgba32> source, int width, int height, byte[] dest)
        {
            int expected = width * height * 4;
            if (dest.Length != expected)
                throw new ArgumentException($"Destination buffer must be {expected} bytes, got {dest.Length}.", nameof(dest));

            if (source.Width == width && source.Height == height)
            {
                source.CopyPixelDataTo(dest);
                return;
            }

            // "Cover" fit: centre-crop the source to the target aspect ratio, then resize. This fills the whole frame
            // without distortion (the alternative - a plain stretch resize - squashes the playfield when the window
            // and preset aspect ratios differ, e.g. a 16:10 window into a 16:9 preset).
            Rectangle crop = coverCrop(source.Width, source.Height, width, height);

            using Image<Rgba32> processed = source.Clone(ctx => ctx.Crop(crop).Resize(width, height));
            processed.CopyPixelDataTo(dest);
        }

        /// <summary>The largest centred rectangle of <paramref name="srcW"/>x<paramref name="srcH"/> matching the target aspect.</summary>
        private static Rectangle coverCrop(int srcW, int srcH, int targetW, int targetH)
        {
            double targetAspect = (double)targetW / targetH;
            double srcAspect = (double)srcW / srcH;

            if (srcAspect > targetAspect)
            {
                // Source too wide: keep full height, trim the sides.
                int cropW = (int)Math.Round(srcH * targetAspect);
                return new Rectangle((srcW - cropW) / 2, 0, cropW, srcH);
            }

            // Source too tall (or equal): keep full width, trim top/bottom.
            int cropH = (int)Math.Round(srcW / targetAspect);
            return new Rectangle(0, (srcH - cropH) / 2, srcW, cropH);
        }
    }
}
