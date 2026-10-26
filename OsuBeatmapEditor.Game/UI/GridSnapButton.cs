using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Phone "Grid snap" toggle (osu!lazer's grid-snap button): a 3x3 grid icon in a bordered tile the size of the New Combo button.
    /// Off = muted grid icon; on = bright icon over a tinted tile. Tapping runs the same action as the desktop shortcut.
    /// </summary>
    public partial class GridSnapButton : ClickableContainer, IHasTooltip
    {
        private const float icon_size = 40;
        private const float line = 4.5f;

        private readonly Func<bool> isOn;
        private Container content = null!;
        private Box tint = null!;
        private Container icon = null!;
        private bool lastOn;
        private double lastPoll = double.MinValue;

        public GridSnapButton(Action onClick, Func<bool> isOn)
        {
            Action = onClick;
            this.isOn = isOn;
        }

        public LocalisableString TooltipText => "Grid snap - snap placing and moving objects to the grid";

        [BackgroundDependencyLoader]
        private void load()
        {
            icon = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(icon_size),
                Children = new Drawable[]
                {
                    // Outer rounded frame.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 2.5f,
                        BorderThickness = line,
                        BorderColour = Color4.White,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
                    // Two vertical and two horizontal dividers at the thirds -> 3x3 cells.
                    divider(vertical: true, 1f / 3f),
                    divider(vertical: true, 2f / 3f),
                    divider(vertical: false, 1f / 3f),
                    divider(vertical: false, 2f / 3f),
                },
            };

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
                    tint = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent, Alpha = 0 },
                    icon,
                },
            };

            if (LiteInterface.Enabled)
                LiteInterface.Flatten(content);

            refresh(force: true);
        }

        private static Drawable divider(bool vertical, float at) => new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = vertical ? Anchor.TopCentre : Anchor.CentreLeft,
            RelativeSizeAxes = vertical ? Axes.Y : Axes.X,
            RelativePositionAxes = vertical ? Axes.X : Axes.Y,
            X = vertical ? at : 0,
            Y = vertical ? 0 : at,
            Width = vertical ? line : 1,
            Height = vertical ? 1 : line,
            Colour = Color4.White,
        };

        protected override void Update()
        {
            base.Update();

            // The on-state can also change from the keyboard shortcut or elsewhere, so poll a few times a second (cheap).
            if (Time.Current - lastPoll > 150)
            {
                lastPoll = Time.Current;
                refresh(force: false);
            }
        }

        private void refresh(bool force)
        {
            bool on = isOn();
            if (!force && on == lastOn)
                return;

            lastOn = on;
            tint.Alpha = on ? 0.3f : 0f;
            icon.Colour = on ? Color4.White : EditorTheme.Colours.TextMuted;
        }

        protected override bool OnClick(ClickEvent e)
        {
            bool handled = base.OnClick(e);
            lastPoll = double.MinValue; // show the new state straight away
            return handled;
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
