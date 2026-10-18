using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

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

        // osu!-style "long" trail: time-based (each piece lives ~trailLength*22 ms regardless of frame rate), pieces are interpolated
        // along the path at a fixed spacing so fast movement leaves a continuous smooth line instead of gaps, and each piece fades
        // out (and shrinks a little) over its life. Pieces are pooled drawables.
        private const int max_pieces = 420;
        private const float piece_spacing = 4f;           // osu!pixels between interpolated pieces
        private const double ms_per_trail_unit = 22;       // trail setting (0-120) -> lifetime
        private const float teleport_distance = 260f;      // a one-frame jump this big (a seek) doesn't draw a streak

        private readonly Container trailContainer;
        private readonly CircularContainer head;
        private readonly Box headFill;

        private readonly List<CircularContainer> trailParts = new List<CircularContainer>();
        private readonly List<Box> trailFills = new List<Box>();

        // Live trail pieces, oldest first (position + the clock time they were laid down).
        private readonly List<(Vector2 pos, double born)> pieces = new List<(Vector2, double)>();
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private Vector2? lastPos;
        private double lastTime;

        private Color4 cursorColour = new Color4(1f, 0.86f, 0.2f, 1f); // yellow default
        private int trailLength = 10;
        private float trailWidth = 1f;

        public AutoCursor()
        {
            // Fill the play area exactly (512x384) so child positions are osu!pixels with no offset.
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                trailContainer = new Container { RelativeSizeAxes = Axes.Both },
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

            for (int i = 0; i < max_pieces; i++)
            {
                var fill = new Box { RelativeSizeAxes = Axes.Both };
                var part = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(cursor_size * 0.8f),
                    Masking = true,
                    Alpha = 0,
                    Child = fill,
                };
                trailParts.Add(part);
                trailFills.Add(fill);
                trailContainer.Add(part);
            }

            applyColour();
        }

        /// <summary>Sets the cursor + trail colour.</summary>
        public void SetColour(Color4 colour)
        {
            cursorColour = colour;
            applyColour();
        }

        /// <summary>Sets how many trailing segments follow the cursor (0 = none).</summary>
        public void SetTrailLength(int length) => trailLength = Math.Clamp(length, 0, max_trail);

        /// <summary>Sets the trail thickness multiplier (1 = default).</summary>
        public void SetTrailWidth(float width) => trailWidth = Math.Clamp(width, 0.2f, 4f);

        private void applyColour()
        {
            headFill.Colour = cursorColour;
            foreach (var fill in trailFills)
                fill.Colour = cursorColour;
        }

        protected override void Update()
        {
            base.Update();

            Vector2? pos = PositionSource?.Invoke();
            // Wall-clock time (not the editor/audio clock, which pauses and jumps on seeks): the trail fades in real time like osu!'s.
            double now = clock.Elapsed.TotalMilliseconds;

            if (pos == null)
            {
                head.Alpha = 0;
                pieces.Clear();
                lastPos = null;
                foreach (var part in trailParts)
                    part.Alpha = 0;
                return;
            }

            head.Alpha = 1;
            head.Position = pos.Value;

            double lifetime = trailLength * ms_per_trail_unit;

            // Drop expired pieces (oldest first).
            int expired = 0;
            while (expired < pieces.Count && now - pieces[expired].born > lifetime)
                expired++;
            if (expired > 0)
                pieces.RemoveRange(0, expired);

            if (lifetime <= 0)
            {
                pieces.Clear();
                lastPos = pos.Value;
                lastTime = now;
            }
            else if (lastPos is Vector2 from)
            {
                // lastPos/lastTime = the last point a piece was laid at; lay evenly-spaced pieces from there to the cursor.
                Vector2 delta = pos.Value - from;
                float dist = delta.Length;

                if (dist > teleport_distance)
                {
                    // A seek / jump: don't draw a streak across the playfield, start a fresh trail.
                    pieces.Clear();
                    lastPos = pos.Value;
                    lastTime = now;
                }
                else if (dist >= piece_spacing)
                {
                    int steps = (int)(dist / piece_spacing);
                    double span = now - lastTime;
                    for (int k = 1; k <= steps; k++)
                    {
                        float f = k * piece_spacing / dist;
                        pieces.Add((from + delta * f, lastTime + span * f));
                    }

                    float used = steps * piece_spacing / dist;
                    lastPos = from + delta * used;
                    lastTime += span * used;
                }
                else if (dist < 0.05f)
                {
                    // Standing still: keep the reference time fresh so the next movement is timestamped from "now".
                    lastTime = now;
                }
                // else: moving slowly - keep accumulating until a full spacing is covered.
            }
            else
            {
                lastPos = pos.Value;
                lastTime = now;
            }

            if (pieces.Count > max_pieces)
                pieces.RemoveRange(0, pieces.Count - max_pieces);

            // Render oldest -> newest; fade by age.
            int shown = Math.Min(pieces.Count, trailParts.Count);
            int i = 0;
            for (; i < shown; i++)
            {
                var (p, born) = pieces[pieces.Count - shown + i];
                float age = lifetime > 0 ? (float)Math.Clamp((now - born) / lifetime, 0, 1) : 1f; // 0 new .. 1 old
                float life = 1f - age;
                var part = trailParts[i];
                part.Position = p;
                part.Alpha = life * life * 0.62f;
                part.Scale = new Vector2((0.55f + 0.45f * life) * trailWidth);
            }

            for (; i < trailParts.Count; i++)
                trailParts[i].Alpha = 0;
        }
    }
}
