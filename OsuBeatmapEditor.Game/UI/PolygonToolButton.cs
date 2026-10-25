using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The phone "Polygon" generator button: a special tool, so it looks different from the other action buttons - a
    /// pink-to-violet gradient border (a bit thicker), five hollow circles in a ring in the middle and a small gear
    /// in the top-right corner.
    /// </summary>
    public partial class PolygonToolButton : ClickableContainer, IHasTooltip
    {
        private const float border = 5;
        private Container content = null!;

        public PolygonToolButton(Action onClick)
        {
            Action = onClick;
        }

        public LocalisableString TooltipText => "Polygon generator - create a ring of circles (like osu!lazer's Generate > Polygon)";

        [BackgroundDependencyLoader]
        private void load()
        {
            if (LiteInterface.Enabled)
            {
                // Lite: just an icon (no gradient border, no five masked rings).
                Child = content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Child = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.DrawPolygon,
                        Size = new Vector2(34),
                        Colour = new Color4(0xC4, 0x78, 0xD0, 0xFF),
                    },
                };
                return;
            }

            var pink = new Color4(0xFF, 0x80, 0x9B, 0xFF);
            var mid = new Color4(0xC4, 0x78, 0xD0, 0xFF);
            var violet = new Color4(0x8F, 0x70, 0xFF, 0xFF);

            var rings = new Container { RelativeSizeAxes = Axes.Both };
            const float ring_size = 17;
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                rings.Add(new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(ring_size),
                    Position = new Vector2((float)Math.Cos(a) * 17.5f, (float)Math.Sin(a) * 17.5f + 2),
                    Masking = true,
                    CornerRadius = ring_size / 2,
                    BorderThickness = 2.2f,
                    BorderColour = Color4.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                });
            }

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = 9,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new ColourInfo { TopLeft = pink, TopRight = mid, BottomLeft = mid, BottomRight = violet },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(border),
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 4,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                                rings,
                                new SpriteIcon
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Icon = FontAwesome.Solid.Cog,
                                    Size = new Vector2(13),
                                    Margin = new MarginPadding(3),
                                    Colour = new Color4(0xF2, 0xF2, 0xF2, 0xFF),
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (LiteInterface.Enabled)
                return base.OnMouseDown(e);

            content.ScaleTo(0.92f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (LiteInterface.Enabled)
            {
                base.OnMouseUp(e);
                return;
            }

            content.ScaleTo(1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }
    }
}
