using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Pre-drawn white discs and rings used by the "Simple renderer". A masked rounded container makes the GPU switch shaders
    /// and split its batch for every circle on screen; a plain sprite with one of these shared textures does not, so many
    /// objects can be drawn together. Each texture is made once per renderer and reused by every object.
    /// </summary>
    public static class SimpleShapeTextures
    {
        private const int disc_size = 128;
        private const int ring_size = 256;

        private static readonly object sync = new object();
        private static IRenderer? owner;
        private static Texture? disc;
        private static readonly Dictionary<int, Texture> rings = new Dictionary<int, Texture>();

        /// <summary>Ring thickness is stored in 1/1000ths of the diameter; this keeps the cache small and the shapes stable.</summary>
        public static int Quantise(float thicknessFraction) => Math.Clamp((int)Math.Round(thicknessFraction * 1000f), 4, 500);

        /// <summary>A solid white disc filling its box (anti-aliased edge).</summary>
        public static Texture Disc(IRenderer renderer)
        {
            lock (sync)
            {
                reset(renderer);
                return disc ??= create(renderer, disc_size, 0f);
            }
        }

        /// <summary>
        /// A white ring whose edge sits on the box's edge, <paramref name="thicknessFraction"/> of the box wide, inside it
        /// (the same shape as a masked circle's border).
        /// </summary>
        public static Texture Ring(IRenderer renderer, float thicknessFraction)
        {
            int key = Quantise(thicknessFraction);

            lock (sync)
            {
                reset(renderer);

                if (!rings.TryGetValue(key, out var texture))
                    rings[key] = texture = create(renderer, ring_size, key / 1000f);

                return texture;
            }
        }

        // A different renderer (e.g. the graphics context was recreated) invalidates everything we made.
        private static void reset(IRenderer renderer)
        {
            if (ReferenceEquals(owner, renderer))
                return;

            owner = renderer;
            disc = null;
            rings.Clear();
        }

        /// <summary>Alpha (0..1) of a disc/ring pixel: pure maths, so it is unit tested.</summary>
        public static float Coverage(float distanceFromCentre, float radius, float thickness)
        {
            // One pixel of soft edge so the shape stays smooth when scaled.
            float outer = Math.Clamp(radius - distanceFromCentre + 0.5f, 0f, 1f);

            if (thickness <= 0f)
                return outer;

            float innerRadius = radius - thickness;
            float inner = Math.Clamp(distanceFromCentre - innerRadius + 0.5f, 0f, 1f);
            return Math.Min(outer, inner);
        }

        private static Texture create(IRenderer renderer, int size, float thicknessFraction)
        {
            var image = new Image<Rgba32>(size, size);
            float centre = (size - 1) / 2f;
            float radius = size / 2f;
            float thickness = thicknessFraction * size;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = MathF.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    byte a = (byte)Math.Clamp(Coverage(d, radius, thickness) * 255f, 0f, 255f);
                    image[x, y] = new Rgba32(255, 255, 255, a);
                }
            }

            var texture = renderer.CreateTexture(size, size);
            texture.SetData(new TextureUpload(image));
            return texture;
        }
    }
}
