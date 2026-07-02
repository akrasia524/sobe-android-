using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OsuBeatmapEditor.Game.Graphics
{
    /// <summary>
    /// Pulls a small palette of dominant, combo-friendly colours out of an image (a beatmap background), for the
    /// "auto colours from background" option. Downscales the image, runs k-means over the pixels to find
    /// <c>count</c> representative colours, gently boosts their saturation/value so they read as combo colours,
    /// and orders them by hue so the combo cycle looks pleasant.
    /// </summary>
    public static class ImagePaletteExtractor
    {
        // Small working size: enough colour information, cheap to cluster.
        private const int sample_size = 100;
        private const int kmeans_iterations = 12;

        // Combo colours want to be reasonably vivid and bright, never muddy or near-black.
        private const float min_saturation = 0.35f;
        private const float min_value = 0.55f;

        /// <summary>
        /// Extracts <paramref name="count"/> combo colours from the image at <paramref name="path"/>, or an empty
        /// list if it can't be read. <paramref name="count"/> is clamped to 2..8.
        /// </summary>
        public static List<Colour4> Extract(string path, int count)
        {
            count = Math.Clamp(count, 2, 8);

            try
            {
                using var image = Image.Load<Rgba32>(path);
                image.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(sample_size, sample_size), Mode = ResizeMode.Max }));

                var pixels = new List<(float r, float g, float b)>();
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            var p = row[x];
                            if (p.A < 128)
                                continue;
                            pixels.Add((p.R / 255f, p.G / 255f, p.B / 255f));
                        }
                    }
                });

                if (pixels.Count == 0)
                    return new List<Colour4>();

                var centroids = kMeans(pixels, count);

                return centroids
                    .Select(c => vivid(c))
                    .OrderBy(hue)
                    .Select(c => new Colour4(c.r, c.g, c.b, 1f))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ImagePaletteExtractor: could not read image");
                return new List<Colour4>();
            }
        }

        /// <summary>Standard Lloyd's k-means over RGB, seeded deterministically so a given image gives a stable palette.</summary>
        private static List<(float r, float g, float b)> kMeans(List<(float r, float g, float b)> pixels, int k)
        {
            var rng = new Random(12345);

            // k-means++ style seeding: first centroid random, each next one biased toward far-away pixels.
            var centroids = new List<(float r, float g, float b)> { pixels[rng.Next(pixels.Count)] };
            while (centroids.Count < k)
            {
                double total = 0;
                var dist = new double[pixels.Count];
                for (int i = 0; i < pixels.Count; i++)
                {
                    double best = double.MaxValue;
                    foreach (var c in centroids)
                        best = Math.Min(best, distSq(pixels[i], c));
                    dist[i] = best;
                    total += best;
                }

                if (total <= 0)
                {
                    centroids.Add(pixels[rng.Next(pixels.Count)]);
                    continue;
                }

                double target = rng.NextDouble() * total;
                double acc = 0;
                int chosen = pixels.Count - 1;
                for (int i = 0; i < pixels.Count; i++)
                {
                    acc += dist[i];
                    if (acc >= target)
                    {
                        chosen = i;
                        break;
                    }
                }

                centroids.Add(pixels[chosen]);
            }

            var sumR = new float[k];
            var sumG = new float[k];
            var sumB = new float[k];
            var counts = new int[k];

            for (int iter = 0; iter < kmeans_iterations; iter++)
            {
                Array.Clear(sumR);
                Array.Clear(sumG);
                Array.Clear(sumB);
                Array.Clear(counts);

                foreach (var px in pixels)
                {
                    int nearest = 0;
                    double best = double.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        double d = distSq(px, centroids[c]);
                        if (d < best)
                        {
                            best = d;
                            nearest = c;
                        }
                    }

                    sumR[nearest] += px.r;
                    sumG[nearest] += px.g;
                    sumB[nearest] += px.b;
                    counts[nearest]++;
                }

                for (int c = 0; c < k; c++)
                {
                    if (counts[c] == 0)
                        continue; // empty cluster: leave its centroid where it was
                    centroids[c] = (sumR[c] / counts[c], sumG[c] / counts[c], sumB[c] / counts[c]);
                }
            }

            return centroids;
        }

        private static double distSq((float r, float g, float b) a, (float r, float g, float b) b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            return dr * dr + dg * dg + db * db;
        }

        /// <summary>Nudges a colour toward combo-colour vibrancy: floors its saturation and value.</summary>
        private static (float r, float g, float b) vivid((float r, float g, float b) c)
        {
            var (h, s, v) = toHsv(c);
            return fromHsv(h, Math.Max(s, min_saturation), Math.Max(v, min_value));
        }

        private static float hue((float r, float g, float b) c) => toHsv(c).h;

        private static (float h, float s, float v) toHsv((float r, float g, float b) c)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b));
            float min = Math.Min(c.r, Math.Min(c.g, c.b));
            float delta = max - min;

            float h = 0;
            if (delta > 1e-5f)
            {
                if (max == c.r)
                    h = (c.g - c.b) / delta % 6f;
                else if (max == c.g)
                    h = (c.b - c.r) / delta + 2f;
                else
                    h = (c.r - c.g) / delta + 4f;

                h *= 60f;
                if (h < 0)
                    h += 360f;
            }

            float s = max <= 1e-5f ? 0 : delta / max;
            return (h, s, max);
        }

        private static (float r, float g, float b) fromHsv(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs(h / 60f % 2 - 1));
            float m = v - c;

            (float r, float g, float b) rgb = (h / 60f) switch
            {
                < 1 => (c, x, 0),
                < 2 => (x, c, 0),
                < 3 => (0, c, x),
                < 4 => (0, x, c),
                < 5 => (x, 0, c),
                _ => (c, 0, x),
            };

            return (rgb.r + m, rgb.g + m, rgb.b + m);
        }
    }
}
