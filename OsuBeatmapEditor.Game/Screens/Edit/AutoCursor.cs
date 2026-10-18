using System;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A non-interactive "Auto" cursor that follows the beatmap exactly like osu!lazer's Auto mod: it rests on
    /// each hit object as it is hit, traces sliders along their path, spins on spinners, and glides between
    /// objects across the gaps. Purely a visual preview - it never touches the saved map. Lives inside the
    /// playfield's osu!pixel coordinate space (a child of the play area), so positions are plain osu!pixels.
    /// Cursor colour and trail length are configurable. <see cref="PositionSource"/> supplies the position per
    /// frame (null hides the cursor - disabled, or no objects).
    /// </summary>
    public partial class AutoCursor : CompositeDrawable
    {
        /// <summary>The cursor's current osu!pixel position, or null to hide it.</summary>
        public Func<Vector2?>? PositionSource;

        private const float cursor_size = 22f; // osu!pixels
        private const int max_trail = 120;
        private const float ms_per_unit = 28f; // trail lifetime per "Trail" unit (max 120 -> ~3.4 s)

        private readonly CircularContainer head;
        private readonly Box headFill;
        private readonly TrailDrawable trail;

        private Color4 cursorColour = new Color4(1f, 0.86f, 0.2f, 1f); // yellow default
        private int trailLength = 10;

        public AutoCursor()
        {
            // Fill the play area exactly (512x384) so child positions are osu!pixels with no offset.
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                trail = new TrailDrawable { RelativeSizeAxes = Axes.Both },
                head = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(cursor_size),
                    Masking = true,
                    BorderThickness = 2.5f,
                    BorderColour = Color4.White,
                    Alpha = 0,
                    Child = headFill = new Box { RelativeSizeAxes = Axes.Both },
                },
            };

            applyColour();
        }

        /// <summary>Sets the cursor + trail colour.</summary>
        public void SetColour(Color4 colour)
        {
            cursorColour = colour;
            applyColour();
        }

        /// <summary>Sets how long the trail is (0 = none).</summary>
        public void SetTrailLength(int length)
        {
            trailLength = Math.Clamp(length, 0, max_trail);
            trail.LifetimeMs = trailLength * ms_per_unit;
        }

        /// <summary>Sets the trail thickness multiplier (1 = default).</summary>
        public void SetTrailWidth(float width) => trail.WidthScale = Math.Clamp(width, 0.2f, 4f);

        private void applyColour()
        {
            headFill.Colour = cursorColour;
            trail.TrailColour = cursorColour;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            trail.LifetimeMs = trailLength * ms_per_unit;
        }

        protected override void Update()
        {
            base.Update();

            Vector2? pos = PositionSource?.Invoke();

            if (pos == null)
            {
                head.Alpha = 0;
                trail.Clear();
                return;
            }

            head.Alpha = 1;
            head.Position = pos.Value;
            trail.Add(pos.Value);
        }

        /// <summary>
        /// The whole trail as ONE drawable: a fixed-size ring of timestamped points (interpolated at an even
        /// spacing so fast movement stays a smooth line) drawn as a single batch of textured quads with a soft
        /// dot texture. No child drawables, no masking, no per-point layout - cost is a few hundred quads.
        /// Fading is based on wall-clock time, so it is independent of the editor/audio clock and seeking.
        /// </summary>
        private partial class TrailDrawable : Drawable, ITexturedShaderDrawable
        {
            public const int Capacity = 640;
            private const float spacing = 2.5f;       // osu!pixels between trail dots
            private const float teleport = 260f;      // a jump this large (seek / next far object) breaks the trail
            private const float dot_size = 15f;       // osu!pixels at width 1

            public IShader TextureShader { get; private set; } = null!;
            public Texture? DotTexture { get; private set; }

            public float LifetimeMs;
            public float WidthScale = 1f;
            public Color4 TrailColour = Color4.White;

            // Ring buffer (oldest at `start`).
            public readonly float[] Xs = new float[Capacity];
            public readonly float[] Ys = new float[Capacity];
            public readonly double[] Times = new double[Capacity];
            public int Start;
            public int Count;
            public double Now;

            private readonly Stopwatch clock = Stopwatch.StartNew();
            private bool hasLast;
            private Vector2 last;
            private double lastTime;
            private int version;

            public int Version => version;

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders, IRenderer renderer)
            {
                TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);

                // Soft round dot, generated once (no asset to ship).
                const int size = 32;
                var image = new Image<Rgba32>(size, size);
                float c = (size - 1) / 2f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size / 2f);
                        float a = d >= 1f ? 0f : d < 0.45f ? 1f : 1f - (d - 0.45f) / 0.55f;
                        byte v = (byte)Math.Clamp(a * 255f, 0f, 255f);
                        image[x, y] = new Rgba32(255, 255, 255, v);
                    }
                }

                DotTexture = renderer.CreateTexture(size, size);
                DotTexture.SetData(new TextureUpload(image));
            }

            public void Clear()
            {
                if (Count == 0 && !hasLast)
                    return;
                Count = 0;
                Start = 0;
                hasLast = false;
                Invalidate(Invalidation.DrawNode);
            }

            public void Add(Vector2 p)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                Now = now;

                // Drop expired points from the old end.
                while (Count > 0 && now - Times[Start] > LifetimeMs)
                {
                    Start = (Start + 1) % Capacity;
                    Count--;
                }

                if (LifetimeMs <= 0)
                {
                    hasLast = false;
                    Count = 0;
                    return;
                }

                if (hasLast)
                {
                    Vector2 delta = p - last;
                    float dist = delta.Length;

                    if (dist > teleport)
                    {
                        Count = 0;
                        Start = 0;
                        push(p, now);
                    }
                    else if (dist >= spacing)
                    {
                        int steps = (int)(dist / spacing);
                        for (int i = 1; i <= steps; i++)
                        {
                            float t = i / (float)steps;
                            push(last + delta * t, lastTime + (now - lastTime) * t);
                        }
                    }
                    else
                        return; // not moved enough yet; keep `last` so slow movement still accumulates

                    // Advance `last` only by what we pushed, so tiny moves are not lost.
                }
                else
                    push(p, now);

                last = p;
                lastTime = now;
                hasLast = true;
                version++;
                Invalidate(Invalidation.DrawNode);
            }

            private void push(Vector2 p, double time)
            {
                if (Count == Capacity)
                {
                    Start = (Start + 1) % Capacity;
                    Count--;
                }

                int i = (Start + Count) % Capacity;
                Xs[i] = p.X;
                Ys[i] = p.Y;
                Times[i] = time;
                Count++;
            }

            protected override void Update()
            {
                base.Update();

                // Keep fading even when the cursor has stopped moving.
                if (Count > 0)
                {
                    Now = clock.Elapsed.TotalMilliseconds;
                    while (Count > 0 && Now - Times[Start] > LifetimeMs)
                    {
                        Start = (Start + 1) % Capacity;
                        Count--;
                    }

                    Invalidate(Invalidation.DrawNode);
                }
            }

            protected override DrawNode CreateDrawNode() => new TrailDrawNode(this);

            private class TrailDrawNode : TexturedShaderDrawNode
            {
                private readonly TrailDrawable source;
                private readonly float[] xs = new float[Capacity];
                private readonly float[] ys = new float[Capacity];
                private readonly double[] times = new double[Capacity];
                private int count;
                private double now;
                private float lifetime;
                private float width;
                private Color4 colour;
                private Texture? texture;

                public TrailDrawNode(TrailDrawable source)
                    : base(source)
                {
                    this.source = source;
                }

                public override void ApplyState()
                {
                    base.ApplyState();

                    count = source.Count;
                    now = source.Now;
                    lifetime = source.LifetimeMs;
                    width = source.WidthScale;
                    colour = source.TrailColour;
                    texture = source.DotTexture;

                    for (int n = 0; n < count; n++)
                    {
                        int i = (source.Start + n) % Capacity;
                        xs[n] = source.Xs[i];
                        ys[n] = source.Ys[i];
                        times[n] = source.Times[i];
                    }
                }

                // Row-vector transform, as the framework's own matrices use.
                private static Vector2 xf(Matrix3 m, Vector2 v) =>
                    new Vector2(v.X * m.Row0.X + v.Y * m.Row1.X + m.Row2.X, v.X * m.Row0.Y + v.Y * m.Row1.Y + m.Row2.Y);

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (count == 0 || texture == null || lifetime <= 0)
                        return;

                    BindTextureShader(renderer);

                    var m = DrawInfo.Matrix;

                    for (int n = 0; n < count; n++)
                    {
                        float life = 1f - (float)((now - times[n]) / lifetime);
                        if (life <= 0f)
                            continue;

                        // Newest dots are fullest; both opacity and size taper towards the tail.
                        float half = dot_size * width * (0.3f + 0.7f * life) * 0.5f;
                        float alpha = life * life * 0.75f;
                        float x = xs[n], y = ys[n];

                        var quad = new Quad(
                            xf(m, new Vector2(x - half, y - half)),
                            xf(m, new Vector2(x + half, y - half)),
                            xf(m, new Vector2(x - half, y + half)),
                            xf(m, new Vector2(x + half, y + half)));

                        renderer.DrawQuad(texture, quad, ColourInfo.SingleColour(new Color4(colour.R, colour.G, colour.B, alpha)));
                    }

                    UnbindTextureShader(renderer);
                }
            }
        }
    }
}
