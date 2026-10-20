using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
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
    /// Phone action button for "New combo" (the Q shortcut): a ring with "NEW" above and "COMBO" below plus a couple of
    /// little sparks. The ring and sparks take the colour the next combo would get (so they follow the combo colours set
    /// in Song Setup), and the button lights up while new combo is on (armed for the next object, or set on the selection).
    /// </summary>
    public partial class NewComboButton : ClickableContainer, IHasTooltip
    {
        private readonly Func<Color4> tint;
        private readonly Func<bool> isOn;

        private Box fill = null!;
        private Container content = null!;
        private CircularContainer ring = null!;
        private Box ringCentre = null!;
        private SpriteText newText = null!;
        private SpriteText comboText = null!;

        private Color4 lastTint = Color4.Transparent;
        private bool lastOn;
        private double lastPoll = double.MinValue;

        public NewComboButton(Action onClick, Func<Color4> tint, Func<bool> isOn)
        {
            Action = onClick;
            this.tint = tint;
            this.isOn = isOn;
        }

        public LocalisableString TooltipText => "New combo - toggle it on the selected objects, or for the next object you place";

        [BackgroundDependencyLoader]
        private void load()
        {
            FontUsage font = FontUsage.Default.With(size: 13, weight: "Bold");

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                BorderThickness = 3,
                BorderColour = EditorTheme.Colours.Accent,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0 },
                    newText = new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 7,
                        Text = "NEW",
                        Font = font,
                        Spacing = new Vector2(2.5f, 0),
                        Colour = Color4.White,
                    },
                    comboText = new SpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -7,
                        Text = "COMBO",
                        Font = font,
                        Spacing = new Vector2(2.5f, 0),
                        Colour = Color4.White,
                    },
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(30),
                        Masking = true,
                        BorderThickness = 4,
                        BorderColour = Color4.White,
                        Child = ringCentre = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.12f, 0.12f, 0.13f, 1f) },
                    },
                    // Sparks: a small plus at the upper-left, a tiny dot above the ring, and a four-point star at the lower-right.
                    sparkPlus(new Vector2(-24, -11), 8),
                    sparkDot(new Vector2(-12, -19), 3),
                    sparkStar(new Vector2(22, 17), 9),
                },
            };

            refresh(force: true);
        }

        private Drawable sparkPlus(Vector2 offset, float size) => new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Position = offset,
            Size = new Vector2(size),
            Name = "spark",
            Children = new Drawable[]
            {
                new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size, 2), Colour = Color4.White },
                new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(2, size), Colour = Color4.White },
            },
        };

        private Drawable sparkDot(Vector2 offset, float size) => new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Position = offset,
            Size = new Vector2(size),
            Name = "spark",
            Colour = Color4.White,
        };

        // A four-point star: a plus of tapered-looking thin bars plus a rotated diamond core.
        private Drawable sparkStar(Vector2 offset, float size) => new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Position = offset,
            Size = new Vector2(size),
            Name = "spark",
            Children = new Drawable[]
            {
                new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size, 2), Colour = Color4.White },
                new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(2, size), Colour = Color4.White },
                new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size * 0.55f), Rotation = 45, Colour = Color4.White },
            },
        };

        protected override void Update()
        {
            base.Update();

            // Poll a few times a second (cheap): the combo colours / on-state can change from Song Setup or the selection.
            if (Time.Current - lastPoll > 150)
            {
                lastPoll = Time.Current;
                refresh(force: false);
            }
        }

        private void refresh(bool force)
        {
            Color4 colour = tint();
            bool on = isOn();
            if (!force && colour == lastTint && on == lastOn)
                return;

            lastTint = colour;
            lastOn = on;

            ring.BorderColour = colour;
            fill.Colour = colour;
            fill.Alpha = on ? 0.3f : 0f;
            ringCentre.Colour = on ? colour : new Color4(0.12f, 0.12f, 0.13f, 1f);

            // Sparks (and the NEW / COMBO text edge) pick up the combo colour; the text stays white for legibility.
            foreach (var child in content.Children)
            {
                if (child.Name != "spark")
                    continue;

                if (child is Container c)
                    foreach (var part in c.Children)
                        part.Colour = colour;
                else
                    child.Colour = colour;
            }
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.92f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Show the new state straight away rather than waiting for the next poll.
            bool handled = base.OnClick(e);
            lastPoll = double.MinValue;
            return handled;
        }
    }
}
