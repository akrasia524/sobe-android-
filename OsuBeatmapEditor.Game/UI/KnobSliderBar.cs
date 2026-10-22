using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuColour = osuTK.Graphics.Color4;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Shared drag bar: slim track, fill, and a fixed-size round knob exactly at the current value. Touch friendly: the
    /// whole (tall) control is the hit area, pressing jumps the value to the finger straight away, and dragging never
    /// gets rejected for wobbling vertically (the stock bar did, which felt "stuck").
    /// </summary>
    public partial class KnobSliderBar<T> : SliderBar<T>
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        public const float KnobSize = 24;
        private const float track_height = 8;

        private readonly Box fill;
        private readonly Container knob;
        private float normalized;

        public KnobSliderBar()
        {
            RangePadding = KnobSize / 2;
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = track_height,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Masking = true,
                    CornerRadius = track_height / 2,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                        fill = new Box { RelativeSizeAxes = Axes.Y, Colour = EditorTheme.Colours.Accent },
                    },
                },
                knob = new Container
                {
                    Size = new Vector2(KnobSize),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    Masking = true,
                    CornerRadius = KnobSize / 2,
                    BorderThickness = 3,
                    BorderColour = osuColour.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent },
                },
            };
        }

        protected override void UpdateValue(float value) => normalized = value;

        protected override void Update()
        {
            base.Update();
            float x = RangePadding + Math.Clamp(normalized, 0, 1) * UsableWidth;
            knob.X = x;
            fill.Width = x;
        }

        private void setFrom(MouseButtonEvent e)
        {
            if (CurrentNumber.Disabled || UsableWidth <= 0) return;
            float n = (ToLocalSpace(e.ScreenSpaceMousePosition).X - RangePadding) / UsableWidth;
            CurrentNumber.SetProportional(Math.Clamp(n, 0f, 1f));
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            setFrom(e);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            setFrom(e);
            return true;
        }

        protected override void OnDrag(DragEvent e) => setFrom(e);

        protected override bool OnClick(ClickEvent e) => true;
    }
}
