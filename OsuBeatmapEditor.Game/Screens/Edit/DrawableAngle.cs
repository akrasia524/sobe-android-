using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The Review-mode "angle" indicator drawn at a hit object: the opening angle between the path coming in from
    /// the previous object and the path going out to the next one (honouring slider heads/tails, like follow
    /// points). A radial-gradient wedge (bright at the object, fading to transparent outward) fills the angle,
    /// with two rays and the degree value, all coloured by how sharp the angle is - red for a tight fold-back
    /// through green for a flowy straight line. Positioned in osu!pixel coordinates.
    /// </summary>
    public partial class DrawableAngle : CompositeDrawable
    {
        private const float ray_length = 44f;
        private const float ray_thickness = 2.5f;
        private const float wedge_radius = 36f;
        private const int wedge_layers = 6;
        private const float wedge_layer_alpha = 0.12f;
        private const double fade = 120;

        // Keep transforms after they complete so seeking the editor re-evaluates the fade correctly.
        public override bool RemoveCompletedTransforms => false;
        public override bool RemoveWhenNotAlive => false;

        private readonly double lifetimeStart;
        private readonly double lifetimeEnd;

        public DrawableAngle(Vector2 vertex, Vector2 dirIn, Vector2 dirOut, float angleDeg, double lifetimeStart, double lifetimeEnd)
        {
            this.lifetimeStart = lifetimeStart;
            this.lifetimeEnd = lifetimeEnd;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            Color4 colour = angleColour(angleDeg);
            Color4 clear = new Color4(colour.R, colour.G, colour.B, 0f);

            var children = new List<Drawable>();

            // The wedge spans the shorter (interior) way from the in-ray to the out-ray. CircularProgress sweeps
            // clockwise from the top, so start at whichever ray is counter-clockwise-most and sweep |delta|.
            float a1 = MathF.Atan2(dirIn.Y, dirIn.X);
            float a2 = MathF.Atan2(dirOut.Y, dirOut.X);
            float delta = wrapPi(a2 - a1);
            float startDeg = MathHelper.RadiansToDegrees(delta >= 0 ? a1 : a2);
            float progress = Math.Abs(delta) / MathF.Tau;

            // Radial gradient: concentric filled sectors, largest (faintest by overlap) at the bottom. A pixel near
            // the vertex is covered by every layer (bright); one near the rim by only the largest (faint) - so the
            // colour fades from the object outward to transparent.
            for (int k = wedge_layers; k >= 1; k--)
            {
                float r = wedge_radius * k / wedge_layers;
                children.Add(new CircularProgress
                {
                    Origin = Anchor.Centre,
                    Position = vertex,
                    Size = new Vector2(r * 2f, r * 2f),
                    InnerRadius = 1f,
                    Progress = progress,
                    Rotation = startDeg + 90f,
                    Colour = new Color4(colour.R, colour.G, colour.B, wedge_layer_alpha),
                });
            }

            // Rays fading from the object colour at the vertex to transparent at their tips.
            children.Add(ray(vertex, dirIn, colour, clear));
            children.Add(ray(vertex, dirOut, colour, clear));

            // Bisector points into the wedge; near-straight angles have a ~zero bisector, so fall back to a normal.
            Vector2 bis = dirIn + dirOut;
            if (bis.LengthSquared < 1e-4f)
                bis = new Vector2(-dirIn.Y, dirIn.X);
            bis = Vector2.Normalize(bis);

            children.Add(new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = vertex + bis * 25f,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 3f,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0f, 0f, 0f, 0.55f) },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 4f, Vertical = 1f },
                        Text = Math.Round(angleDeg).ToString(),
                        Colour = colour,
                        Font = FontUsage.Default.With(size: 18, weight: "Bold"),
                    },
                },
            });

            InternalChildren = children.ToArray();

            LifetimeStart = lifetimeStart;
            LifetimeEnd = lifetimeEnd;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Fade in when the object appears and out as it leaves, so scrubbing/playback isn't a hard pop.
            using (BeginAbsoluteSequence(lifetimeStart))
                this.FadeIn(fade);
            using (BeginAbsoluteSequence(Math.Max(lifetimeStart + fade, lifetimeEnd - fade)))
                this.FadeOut(fade);
        }

        /// <summary>A ray (rotated box) from the vertex outward, fading from <paramref name="solid"/> to transparent.</summary>
        private static Drawable ray(Vector2 vertex, Vector2 dir, Color4 solid, Color4 clear)
        {
            float angle = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Y, dir.X));
            return new Box
            {
                Position = vertex,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(ray_length, ray_thickness),
                Rotation = angle,
                Colour = ColourInfo.GradientHorizontal(solid, clear),
            };
        }

        /// <summary>Wraps an angle (radians) to (-pi, pi] so the wedge always sweeps the shorter, interior way.</summary>
        private static float wrapPi(float a)
        {
            while (a <= -MathF.PI) a += MathF.Tau;
            while (a > MathF.PI) a -= MathF.Tau;
            return a;
        }

        /// <summary>Red (sharp fold-back) -> yellow (~square) -> green (straight/flowy), by angle in degrees.</summary>
        private static Color4 angleColour(float deg)
        {
            float t = Math.Clamp(deg / 180f, 0f, 1f);
            Color4 red = new Color4(0.95f, 0.27f, 0.27f, 1f);
            Color4 yellow = new Color4(1f, 0.82f, 0.2f, 1f);
            Color4 green = new Color4(0.36f, 0.85f, 0.42f, 1f);
            return t < 0.5f ? lerp(red, yellow, t * 2f) : lerp(yellow, green, (t - 0.5f) * 2f);
        }

        private static Color4 lerp(Color4 a, Color4 b, float t) => new Color4(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            1f);
    }
}
