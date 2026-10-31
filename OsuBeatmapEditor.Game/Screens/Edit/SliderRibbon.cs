using System;
using System.Collections.Generic;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Pure geometry for the "faster slider body": turns a slider's path into ribbons of cross-sections, so it can be drawn
    /// as plain coloured quads straight to the screen instead of through the framework's per-slider off-screen buffer.
    /// No drawing and no framework types beyond vectors, so it is unit tested.
    /// </summary>
    public static class SliderRibbon
    {
        /// <summary>One cross-section of the tube: the path point and the (mitred, radius-long) offset to its left edge.</summary>
        public readonly struct Section
        {
            public readonly Vector2 Centre;
            public readonly Vector2 Offset;

            public Section(Vector2 centre, Vector2 offset)
            {
                Centre = centre;
                Offset = offset;
            }
        }

        /// <summary>A path broken into continuous ribbons (a sharp corner starts a new one) plus the points that need a round joint.</summary>
        public sealed class Result
        {
            public readonly List<List<Section>> Ribbons = new List<List<Section>>();
            public readonly List<Vector2> Joints = new List<Vector2>();
            public Vector2 StartPoint;
            public Vector2 StartDirection;
            public Vector2 EndPoint;
            public Vector2 EndDirection;
            public bool Valid;
        }

        /// <summary>The longest allowed mitre (in radii). Corners sharper than this are split and joined with a round disc.</summary>
        public const float MiterLimit = 1.6f;

        /// <summary>Drops points that lie within <paramref name="tolerance"/> of the line between their neighbours (Douglas-Peucker).</summary>
        public static List<Vector2> Simplify(IReadOnlyList<Vector2> points, float tolerance)
        {
            var result = new List<Vector2>(points.Count);

            // Remove (near-)duplicates first: they have no direction.
            for (int i = 0; i < points.Count; i++)
            {
                if (result.Count == 0 || (points[i] - result[^1]).LengthSquared > 0.0001f)
                    result.Add(points[i]);
            }

            if (result.Count <= 2 || tolerance <= 0)
                return result;

            var keep = new bool[result.Count];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, result.Count - 1));

            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float worst = 0;
                int index = -1;

                for (int i = a + 1; i < b; i++)
                {
                    float d = distanceToSegment(result[i], result[a], result[b]);
                    if (d > worst)
                    {
                        worst = d;
                        index = i;
                    }
                }

                if (index >= 0 && worst > tolerance)
                {
                    keep[index] = true;
                    stack.Push((a, index));
                    stack.Push((index, b));
                }
            }

            var simplified = new List<Vector2>();
            for (int i = 0; i < result.Count; i++)
            {
                if (keep[i])
                    simplified.Add(result[i]);
            }

            return simplified;
        }

        private static float distanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;
            if (lengthSquared < 1e-8f)
                return (p - a).Length;

            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
            return (p - (a + ab * t)).Length;
        }

        /// <summary>Builds the ribbons for a path (already simplified) with the given tube radius.</summary>
        public static Result Build(IReadOnlyList<Vector2> points, float radius)
        {
            var result = new Result();
            if (points.Count < 2 || radius <= 0)
                return result;

            int n = points.Count;
            var dirs = new Vector2[n - 1];
            for (int i = 0; i < n - 1; i++)
                dirs[i] = Vector2.Normalize(points[i + 1] - points[i]);

            result.StartPoint = points[0];
            result.StartDirection = -dirs[0];
            result.EndPoint = points[n - 1];
            result.EndDirection = dirs[n - 2];
            result.Valid = true;

            var current = new List<Section>();
            current.Add(new Section(points[0], normal(dirs[0]) * radius));

            for (int i = 1; i < n - 1; i++)
            {
                Vector2 n0 = normal(dirs[i - 1]);
                Vector2 n1 = normal(dirs[i]);
                Vector2 sum = n0 + n1;

                // A straight reversal has no usable mitre.
                float miterDot = sum.LengthSquared < 1e-6f ? 0f : Vector2.Dot(Vector2.Normalize(sum), n0);
                float miterLength = miterDot > 1e-4f ? 1f / miterDot : float.PositiveInfinity;

                if (miterLength <= MiterLimit)
                {
                    current.Add(new Section(points[i], Vector2.Normalize(sum) * (miterLength * radius)));
                }
                else
                {
                    // Too sharp for a mitre: end this ribbon square, start the next one square, and cover the corner with a disc.
                    current.Add(new Section(points[i], n0 * radius));
                    result.Ribbons.Add(current);
                    current = new List<Section> { new Section(points[i], n1 * radius) };
                    result.Joints.Add(points[i]);
                }
            }

            current.Add(new Section(points[n - 1], normal(dirs[n - 2]) * radius));
            result.Ribbons.Add(current);
            return result;
        }

        /// <summary>The left-hand normal of a unit direction.</summary>
        private static Vector2 normal(Vector2 direction) => new Vector2(-direction.Y, direction.X);

        /// <summary>Points around a circle arc (for the round caps and joints), starting at <paramref name="startAngle"/>.</summary>
        public static Vector2[] Arc(Vector2 centre, float radius, float startAngle, float sweep, int steps)
        {
            var points = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = startAngle + sweep * i / steps;
                points[i] = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            }

            return points;
        }
    }
}
