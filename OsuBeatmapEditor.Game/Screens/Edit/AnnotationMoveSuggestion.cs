using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Annotations;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Renders a Review "move" suggestion for the mapper: a hollow ghost ring at each referenced object's proposed
    /// position (its current position + the suggested delta) with an arrow from where it is now, in the author's
    /// colour, plus Accept / Discard buttons. Accept applies the move to the real map; Discard drops the suggestion.
    /// Fades in as the playhead nears the suggestion's time, like a note. Positioned in osu!pixel coordinates.
    /// </summary>
    public partial class AnnotationMoveSuggestion : CompositeDrawable
    {
        private readonly Annotation annotation;
        private readonly Func<double> timeSource;

        public AnnotationMoveSuggestion(Annotation annotation, Func<int, Vector2?> headResolver, float diameter, Func<double> timeSource)
        {
            this.annotation = annotation;
            this.timeSource = timeSource;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            Color4 colour = colourFromHex(annotation.Color);
            Vector2 delta = new Vector2(annotation.DX ?? 0, annotation.DY ?? 0);

            var children = new List<Drawable>();

            foreach (int id in annotation.Objects ?? new List<int>())
            {
                if (headResolver(id) is not Vector2 head)
                    continue; // object gone (edited/deleted since the suggestion was authored)

                // The object now renders at its proposed spot (head + delta); mark where it WAS with a faint ghost + arrow.
                children.Add(arrow(head, head + delta, colour));
                children.Add(ghostRing(head, colour, diameter));
            }

            InternalChildren = children.ToArray();
        }

        // Purely visual - never blocks input (Accept/Reject live in the changes panel now).
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => false;

        protected override void Update()
        {
            base.Update();

            // Fade by time window around the suggestion's anchor time, like a note.
            double now = timeSource();
            double half = Math.Max(1, annotation.WindowMs * 0.5);
            double d = Math.Abs(now - annotation.Time);
            Alpha = d <= half ? 1f : d <= annotation.WindowMs ? (float)(1 - (d - half) / half) : 0f;
        }

        /// <summary>A hollow object-sized ring at a proposed position.</summary>
        private static Drawable ghostRing(Vector2 pos, Color4 colour, float diameter) => new CircularContainer
        {
            Position = pos,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter),
            Masking = true,
            BorderThickness = diameter * 0.09f,
            BorderColour = colour,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(colour.R, colour.G, colour.B, 0.12f) },
        };

        /// <summary>An arrow (line + head) from an object's current position to its proposed one.</summary>
        private static Drawable arrow(Vector2 from, Vector2 to, Color4 colour)
        {
            Vector2 d = to - from;
            float length = d.Length;
            float angle = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X));
            var lineColour = new Color4(colour.R, colour.G, colour.B, 0.7f);

            var container = new Container { RelativeSizeAxes = Axes.Both };
            container.Add(new Box
            {
                Position = from,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(length, 2f),
                Rotation = angle,
                Colour = lineColour,
            });

            // A small arrow head at the target end (two short strokes).
            if (length > 8f)
            {
                Vector2 dir = d / length;
                Vector2 back = to - dir * 9f;
                Vector2 perp = new Vector2(-dir.Y, dir.X) * 5f;
                container.Add(headStroke(to, back + perp, lineColour));
                container.Add(headStroke(to, back - perp, lineColour));
            }

            return container;
        }

        private static Drawable headStroke(Vector2 from, Vector2 to, Color4 colour)
        {
            Vector2 d = to - from;
            return new Box
            {
                Position = from,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(d.Length, 2f),
                Rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X)),
                Colour = colour,
            };
        }

        private static Color4 colourFromHex(string hex)
        {
            try
            {
                var c = Colour4.FromHex(hex);
                return new Color4(c.R, c.G, c.B, 1f);
            }
            catch
            {
                return Color4.White;
            }
        }
    }
}
