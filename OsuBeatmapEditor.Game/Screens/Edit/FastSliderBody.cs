using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The "faster slider body". Draws the slider tube as plain vertex-coloured quads straight to the screen (a white rim, then the
    /// body gradient, with round caps), instead of the framework's path, which renders every slider into its own off-screen
    /// picture first. It uses the same colours as <see cref="SliderBodyPath"/>, so it should look the same. One known difference:
    /// where a slider crosses over itself the two layers blend twice, so the overlap is slightly stronger.
    /// </summary>
    public partial class FastSliderBody : Drawable, ITexturedShaderDrawable
    {
        // Fraction of the radius at the five cross-section levels, outer edge to centre (the rim/body step shares one radius).
        private readonly float[] levelRadius;
        private readonly Color4[] levelColour;

        private readonly SliderRibbon.Result ribbon;
        private readonly float radius;
        private readonly Vector2 origin;

        // The three drawn bands as (outer level, inner level): fade-in edge, white rim, body gradient.
        private static readonly (int, int)[] band_levels = { (0, 1), (1, 2), (3, 4) };

        private const int cap_steps = 8;
        private const int joint_steps = 16;

        public IShader TextureShader { get; private set; } = null!;
        public Texture? WhiteTexture { get; private set; }

        public FastSliderBody(IReadOnlyList<Vector2> path, float radius, SliderBodyPath style)
        {
            this.radius = radius;

            // Close enough to be invisible (a quarter of an osu!pixel), far fewer quads to draw.
            var points = SliderRibbon.Simplify(path, 0.25f);
            ribbon = SliderRibbon.Build(points, radius);

            float border = Math.Clamp(style.BorderPortion, 0.02f, 0.9f);
            levelRadius = new[] { 1f, 0.98f, 1f - border, 1f - border, 0f };
            levelColour = new[]
            {
                withAlpha(style.Sample(0f), 0f),
                style.Sample(0.02f),
                style.Sample(border),
                style.Sample(Math.Min(1f, border + 0.0001f)),
                style.Sample(1f),
            };

            if (ribbon.Valid)
            {
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);
                float reach = radius * 2f;

                foreach (var p in points)
                {
                    min = Vector2.ComponentMin(min, p - new Vector2(reach));
                    max = Vector2.ComponentMax(max, p + new Vector2(reach));
                }

                origin = min;
                Position = min;
                Size = max - min;
            }
        }

        private static Color4 withAlpha(Color4 c, float alphaScale) => new Color4(c.R, c.G, c.B, c.A * alphaScale);

        [BackgroundDependencyLoader]
        private void load(ShaderManager shaders, IRenderer renderer)
        {
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            WhiteTexture = renderer.WhitePixel;
        }

        protected override DrawNode CreateDrawNode() => new FastSliderDrawNode(this);

        private class FastSliderDrawNode : TexturedShaderDrawNode
        {
            private readonly FastSliderBody source;
            private SliderRibbon.Result ribbon = null!;
            private Texture? texture;
            private Color4 parentColour;

            public FastSliderDrawNode(FastSliderBody source)
                : base(source)
            {
                this.source = source;
            }

            public override void ApplyState()
            {
                base.ApplyState();

                ribbon = source.ribbon;
                texture = source.WhiteTexture;

                // Fold the parent's fade (the object's fade in/out) into every vertex.
                parentColour = DrawColourInfo.Colour.TryExtractSingleColour(out var single) ? (Color4)single : (Color4)DrawColourInfo.Colour.AverageColour;
            }

            // Row-vector transform, as the framework's own matrices use.
            private static Vector2 xf(Matrix3 m, Vector2 v) =>
                new Vector2(v.X * m.Row0.X + v.Y * m.Row1.X + m.Row2.X, v.X * m.Row0.Y + v.Y * m.Row1.Y + m.Row2.Y);

            private Color4 tint(Color4 c) => new Color4(c.R * parentColour.R, c.G * parentColour.G, c.B * parentColour.B, c.A * parentColour.A);

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (!ribbon.Valid || texture == null || parentColour.A <= 0.001f)
                    return;

                BindTextureShader(renderer);

                var m = DrawInfo.Matrix;
                var o = source.origin;
                var radiusFraction = source.levelRadius;
                var colours = source.levelColour;

                var bands = band_levels;

                foreach (var strip in ribbon.Ribbons)
                {
                    for (int i = 0; i + 1 < strip.Count; i++)
                    {
                        var a = strip[i];
                        var b = strip[i + 1];

                        foreach (var (outer, inner) in bands)
                        {
                            for (int side = -1; side <= 1; side += 2)
                            {
                                Vector2 a0 = a.Centre + a.Offset * (side * radiusFraction[outer]) - o;
                                Vector2 b0 = b.Centre + b.Offset * (side * radiusFraction[outer]) - o;
                                Vector2 a1 = a.Centre + a.Offset * (side * radiusFraction[inner]) - o;
                                Vector2 b1 = b.Centre + b.Offset * (side * radiusFraction[inner]) - o;

                                drawBand(renderer, m, a0, b0, a1, b1, colours[outer], colours[inner]);
                            }
                        }
                    }
                }

                // Round caps at both ends, and a round disc at every corner too sharp to mitre.
                cap(renderer, m, o, ribbon.StartPoint, ribbon.StartDirection);
                cap(renderer, m, o, ribbon.EndPoint, ribbon.EndDirection);

                foreach (var joint in ribbon.Joints)
                    fan(renderer, m, o, joint, 0f, MathF.PI * 2f, joint_steps);

                UnbindTextureShader(renderer);
            }

            private void cap(IRenderer renderer, Matrix3 m, Vector2 o, Vector2 centre, Vector2 direction)
            {
                float angle = MathF.Atan2(direction.Y, direction.X);
                fan(renderer, m, o, centre, angle - MathF.PI / 2f, MathF.PI, cap_steps);
            }

            /// <summary>A pie of concentric bands (rim, body) around a centre, spanning the given arc.</summary>
            private void fan(IRenderer renderer, Matrix3 m, Vector2 o, Vector2 centre, float startAngle, float sweep, int steps)
            {
                var radiusFraction = source.levelRadius;
                var colours = source.levelColour;
                float radius = source.radius;
                var bands = band_levels;

                foreach (var (outer, inner) in bands)
                {
                    var ringOuter = SliderRibbon.Arc(centre - o, radius * radiusFraction[outer], startAngle, sweep, steps);
                    var ringInner = SliderRibbon.Arc(centre - o, radius * radiusFraction[inner], startAngle, sweep, steps);

                    for (int k = 0; k < steps; k++)
                        drawBand(renderer, m, ringOuter[k], ringOuter[k + 1], ringInner[k], ringInner[k + 1], colours[outer], colours[inner]);
                }
            }

            private void drawBand(IRenderer renderer, Matrix3 m, Vector2 topLeft, Vector2 topRight, Vector2 bottomLeft, Vector2 bottomRight, Color4 outerColour, Color4 innerColour)
            {
                var quad = new Quad(xf(m, topLeft), xf(m, topRight), xf(m, bottomLeft), xf(m, bottomRight));
                var outer = tint(outerColour);
                var inner = tint(innerColour);

                var colour = new ColourInfo
                {
                    TopLeft = outer,
                    TopRight = outer,
                    BottomLeft = inner,
                    BottomRight = inner,
                    HasSingleColour = false,
                };

                renderer.DrawQuad(texture!, quad, colour);
            }
        }
    }
}
