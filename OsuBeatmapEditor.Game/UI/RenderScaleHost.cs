using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Holds everything that is visible. With the "lower render resolution" setting off it is a plain pass-through (the children
    /// are drawn exactly as before, with zero extra cost). With it on, the children are drawn into an off-screen picture at a
    /// fraction of the screen's pixels and that picture is stretched back over the screen.
    /// This is done entirely inside the framework: it never touches the Android surface (an earlier attempt that did shrank the
    /// picture into a corner), and touch input is unaffected because layout stays the same.
    /// </summary>
    public partial class RenderScaleHost : CompositeDrawable
    {
        private readonly Container inner;
        private BufferedContainer? buffer;

        /// <summary>Which <see cref="RenderResolution"/> mode to use. Safe to change at any time, from any thread.</summary>
        public int Mode
        {
            get => mode;
            set => mode = value;
        }

        private volatile int mode;
        private int appliedMode = -1;
        private float appliedFactor = -1;

        [Resolved]
        private GameHost host { get; set; } = null!;

        public RenderScaleHost(params Drawable[] children)
        {
            RelativeSizeAxes = Axes.Both;
            inner = new Container { RelativeSizeAxes = Axes.Both, Children = children };
            AddInternal(inner);
        }

        protected override void Update()
        {
            base.Update();

            // Re-check every frame: cheap, and it follows a screen-size change too.
            float width = host.Window?.ClientSize.Width ?? 0;
            float factor = RenderResolution.Factor(mode, width);

            if (mode == appliedMode && Math.Abs(factor - appliedFactor) < 0.001f)
                return;

            apply(factor);
            appliedMode = mode;
            appliedFactor = factor;
        }

        private void apply(float factor)
        {
            if (!RenderResolution.IsLowered(factor))
            {
                if (buffer != null)
                {
                    // Back to the plain path: take the content out of the off-screen picture and drop the picture.
                    buffer.Remove(inner, false);
                    RemoveInternal(buffer, true);
                    buffer = null;
                    AddInternal(inner);
                }

                return;
            }

            if (buffer == null)
            {
                RemoveInternal(inner, false);
                buffer = new BufferedContainer { RelativeSizeAxes = Axes.Both };
                buffer.Add(inner);
                AddInternal(buffer);
            }

            buffer.FrameBufferScale = new Vector2(factor);
        }
    }
}
