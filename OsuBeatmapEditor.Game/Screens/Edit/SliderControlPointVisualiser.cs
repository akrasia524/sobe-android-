using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Draggable control-point editor for a selected slider, modelled on osu!lazer's
    /// <c>PathControlPointVisualiser</c>: a control polygon with a handle per control point (white = segment
    /// continuation, red = segment start / sharp corner, i.e. a non-null <see cref="SliderControlPoint.Type"/>).
    /// Dragging a handle reshapes (and resizes) the slider live; double-clicking a handle toggles its corner;
    /// right-clicking un-corners or deletes it; Ctrl+left-clicking the body inserts a control point. Edits commit
    /// through <see cref="IEditorActions"/>.
    /// </summary>
    public partial class SliderControlPointVisualiser : CompositeDrawable
    {
        /// <summary>Raised when an edge node is clicked: its node index (0 = head, <c>Slides</c> = tail).</summary>
        public Action<int>? PartNodeClicked;

        /// <summary>Raised when the slider body (not a handle or edge node) is single-clicked.</summary>
        public Action? BodyClicked;

        /// <summary>Snaps a dragged/inserted control point to the grid when grid snap is on (else returns it unchanged).</summary>
        public Func<Vector2, Vector2>? SnapToGrid;

        private readonly int sliderId;
        private readonly int tailNodeIndex;
        private readonly float diameter;
        private readonly IEditorActions actions;
        private readonly double pixelLength;

        private readonly List<SliderControlPoint> controlPoints;
        private readonly List<ControlPointPiece> pieces = new List<ControlPointPiece>();

        private Container lineLayer = null!;
        private Container pieceLayer = null!;
        private SmoothPath preview = null!;
        private Container? contextMenu;

        // Multi-selection of control points (lazer-style): kept static so it survives the visualiser being
        // recreated after an edit/part-selection, keyed by slider so switching sliders clears it.
        private static int selSlider = -1;
        private static readonly HashSet<int> selectedIndices = new HashSet<int>();

        public SliderControlPointVisualiser(HitObjectModel slider, float diameter, IEditorActions actions)
        {
            sliderId = slider.Id;
            tailNodeIndex = Math.Max(1, slider.Slides);
            this.diameter = diameter;
            this.actions = actions;
            controlPoints = slider.ControlPoints!.ToList();
            pixelLength = SliderGeometry.PathLength(slider.Path ?? Array.Empty<Vector2>());

            RelativeSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChildren = new Drawable[]
            {
                preview = new SmoothPath
                {
                    PathRadius = Math.Max(1.5f, diameter / 2f - 3f),
                    Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.35f),
                    Alpha = 0,
                },
                lineLayer = new Container { RelativeSizeAxes = Axes.Both },
                pieceLayer = new Container { RelativeSizeAxes = Axes.Both },
            };

            // Switching to a different slider drops the previous selection; otherwise heal stale indices (a
            // delete/insert may have shrunk the point list since the static selection was last set).
            if (selSlider != sliderId)
            {
                selSlider = sliderId;
                selectedIndices.Clear();
            }
            selectedIndices.RemoveWhere(i => i < 0 || i >= controlPoints.Count);

            rebuildPieces();
            rebuildPolygon();
            applySelectionVisual();
        }

        /// <summary>(Re)creates a handle per control point.</summary>
        private void rebuildPieces()
        {
            pieceLayer.Clear();
            pieces.Clear();

            for (int i = 0; i < controlPoints.Count; i++)
            {
                var piece = new ControlPointPiece(i, controlPoints[i].Type, diameter * 0.26f)
                {
                    Position = controlPoints[i].Position,
                    Moved = handleMoved,
                    ContextRequested = showContextMenu,
                    DragSelectRequested = ensureSelectedForDrag,
                    Clicked = pieceClicked,
                };
                pieces.Add(piece);
                pieceLayer.Add(piece);
            }
        }

        /// <summary>Reflects the current multi-selection on the handles (a ring on the selected ones).</summary>
        private void applySelectionVisual()
        {
            for (int i = 0; i < pieces.Count; i++)
                pieces[i].SetSelected(selectedIndices.Contains(i));
        }

        /// <summary>Redraws the thin control-polygon lines connecting consecutive control points.</summary>
        private void rebuildPolygon()
        {
            lineLayer.Clear();
            for (int i = 1; i < controlPoints.Count; i++)
                lineLayer.Add(polygonSegment(controlPoints[i - 1].Position, controlPoints[i].Position));
        }

        private Drawable polygonSegment(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return new Box
            {
                Position = a,
                Origin = Anchor.CentreLeft,
                Width = d.Length,
                Height = 1.5f,
                Rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X)),
                Colour = new Color4(1f, 1f, 1f, 0.35f),
            };
        }

        private void handleMoved(int index, Vector2 screenPosition, bool finished)
        {
            Vector2 osu = ToLocalSpace(screenPosition);
            if (SnapToGrid != null)
                osu = SnapToGrid(osu);
            osu = clamp(osu);

            // Group move: if the dragged handle is part of a multi-selection, translate the whole selection by
            // the same delta (lazer's DragInProgress). Otherwise just move the dragged handle.
            if (selectedIndices.Count > 1 && selectedIndices.Contains(index))
            {
                Vector2 delta = osu - controlPoints[index].Position;
                foreach (int i in selectedIndices)
                {
                    Vector2 p = clamp(controlPoints[i].Position + delta);
                    controlPoints[i] = controlPoints[i] with { X = p.X, Y = p.Y };
                    pieces[i].Position = p;
                }
            }
            else
            {
                controlPoints[index] = controlPoints[index] with { X = osu.X, Y = osu.Y };
                pieces[index].Position = osu;
            }

            rebuildPolygon();
            showPreview();

            if (finished)
            {
                actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
                actions.ClearSliderPreview();
            }
            else
            {
                // Show, in real time, how long the reshaped slider will occupy on the top timeline.
                actions.PreviewSliderResize(sliderId, controlPoints.ToArray());
            }
        }

        private static bool typesEqual(SliderPathType? a, SliderPathType? b)
        {
            if (a is null && b is null) return true;
            if (a is SliderPathType ta && b is SliderPathType tb) return ta.Type == tb.Type;
            return false;
        }

        /// <summary>The control points a menu/edit targets: the whole multi-selection if the acted-on handle is part of it, else just that handle.</summary>
        private List<int> targetsFor(int index) =>
            selectedIndices.Count > 1 && selectedIndices.Contains(index)
                ? selectedIndices.OrderBy(i => i).ToList()
                : new List<int> { index };

        /// <summary>Sets the segment curve type (or smooth = null) on the given control points and commits once.</summary>
        private void setSegmentType(IReadOnlyList<int> indices, SliderPathType? type)
        {
            closeContextMenu();

            bool changed = false;
            foreach (int i in indices)
            {
                // The head must always start a segment, so it can't be smooth.
                if (i <= 0 && type == null)
                    continue;
                if (typesEqual(controlPoints[i].Type, type))
                    continue;

                controlPoints[i] = controlPoints[i] with { Type = type };
                changed = true;
            }

            if (changed)
                actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
        }

        /// <summary>Deletes the given control points (keeping at least two), from the context menu, in one undo step.</summary>
        private void deleteAnchors(IReadOnlyList<int> indices)
        {
            closeContextMenu();

            var sorted = indices.Distinct().OrderByDescending(i => i).ToList();
            if (controlPoints.Count - sorted.Count < 2)
                return; // a slider needs at least two control points

            foreach (int i in sorted)
                controlPoints.RemoveAt(i);

            // The new head must keep a definite type.
            if (controlPoints[0].Type == null)
                controlPoints[0] = controlPoints[0] with { Type = SliderPathType.Bezier };

            selectedIndices.Clear();
            actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
        }

        /// <summary>
        /// Right-clicking a handle opens a small curve-type menu (Smooth / Linear / Perfect / Bezier + Delete),
        /// like osu!lazer's path context menu. If the handle is part of a multi-selection the menu acts on all of it.
        /// </summary>
        private void showContextMenu(int index, Vector2 atOsuPos)
        {
            closeContextMenu();

            var targets = targetsFor(index);
            bool multi = targets.Count > 1;
            string suffix = multi ? $" ({targets.Count})" : string.Empty;

            var items = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
            };

            // "Smooth" only when none of the targets is the head (the head must start a segment).
            if (!targets.Contains(0))
                items.Add(new MenuButton("Smooth" + suffix, OsuColour.Yellow, () => setSegmentType(targets, null)));
            items.Add(new MenuButton("Linear" + suffix, EditorTheme.Colours.Error, () => setSegmentType(targets, SliderPathType.Linear)));
            items.Add(new MenuButton("Perfect curve" + suffix, OsuColour.Purple, () => setSegmentType(targets, SliderPathType.PerfectCurve)));
            items.Add(new MenuButton("Bezier" + suffix, OsuColour.Pink, () => setSegmentType(targets, SliderPathType.Bezier)));
            if (controlPoints.Count - targets.Count >= 2)
                items.Add(new MenuButton("Delete" + suffix, EditorTheme.Colours.Error, () => deleteAnchors(targets), destructive: true));

            var panel = new Container
            {
                Position = atOsuPos + new Vector2(diameter * 0.3f, 0),
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4f,
                BorderThickness = 1f,
                BorderColour = EditorTheme.Colours.Border,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = new Color4(0, 0, 0, 0.4f),
                    Radius = 6f,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                    items,
                },
            };

            // A full-size catcher closes the menu on any click outside it.
            contextMenu = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new ClickCatcher { Action = closeContextMenu },
                    panel,
                },
            };
            AddInternal(contextMenu);
        }

        private void closeContextMenu()
        {
            contextMenu?.Expire();
            contextMenu = null;
        }

        /// <summary>
        /// A click on a handle (lazer's selection model): Ctrl/Shift+click toggles it in the multi-selection; a
        /// plain click selects just that handle, and on the head/tail also selects that edge node for the
        /// two-stage part selection (our hitsound feature).
        /// </summary>
        private void pieceClicked(int index, bool additive)
        {
            if (additive)
            {
                if (!selectedIndices.Add(index))
                    selectedIndices.Remove(index);
                applySelectionVisual();
                return;
            }

            selectedIndices.Clear();
            selectedIndices.Add(index);
            applySelectionVisual();

            // Phones: there is no right-click, and a long-press was a hassle - a single tap on a handle opens the
            // curve-type menu (Smooth / Linear / Perfect curve / Bezier / Delete).
            if (UI.MobileMetrics.IsMobile)
                showContextMenu(index, controlPoints[index].Position);

            if (index == 0)
                PartNodeClicked?.Invoke(0);
            else if (index == controlPoints.Count - 1)
                PartNodeClicked?.Invoke(tailNodeIndex);
            // Intermediate anchors aren't edge nodes; selecting one just highlights the handle.
        }

        /// <summary>When a drag starts on a handle that isn't already part of the selection, select just it.</summary>
        private void ensureSelectedForDrag(int index)
        {
            if (selectedIndices.Contains(index))
                return;

            selectedIndices.Clear();
            selectedIndices.Add(index);
            applySelectionVisual();
        }

        /// <summary>
        /// Click handling on the body (clicks on a handle go to the piece itself): Ctrl+left-click anywhere on
        /// the body inserts a control point on the nearest polygon segment (it takes priority, so it still works
        /// right on top of the head/tail circle); a plain click on the head/tail circle selects that edge node;
        /// a plain click on the body selects the body part.
        /// </summary>
        protected override bool OnClick(ClickEvent e)
        {
            Vector2 pos = clamp(ToLocalSpace(e.ScreenSpaceMousePosition));

            // Ctrl+left-click inserts a new anchor, checked first so it works even when hovering the head/tail
            // circle (where edge-node selection would otherwise consume the click).
            if (Shortcut.CommandPressed(e) && nearPath(pos, Math.Max(8f, diameter / 2f)))
            {
                int insertAt = nearestSegmentIndex(pos);
                // Hit-test with the raw click, but drop the inserted anchor on the grid when grid snap is on.
                controlPoints.Insert(insertAt, new SliderControlPoint(SnapToGrid?.Invoke(pos) ?? pos));
                actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
                return true;
            }

            // The head/tail circle is larger than its handle, so cover the circle area for edge-node selection.
            // The tail sits at the rendered path end (not the last control point, which a length trim can move).
            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);
            float nodeRadius = diameter / 2f;
            if (Vector2.Distance(pos, controlPoints[0].Position) <= nodeRadius)
            {
                PartNodeClicked?.Invoke(0);
                return true;
            }
            if (path.Count > 0 && Vector2.Distance(pos, path[^1]) <= nodeRadius)
            {
                PartNodeClicked?.Invoke(tailNodeIndex);
                return true;
            }

            if (!nearPath(pos, Math.Max(8f, diameter / 2f)))
            {
                closeAddPrompt();
                return false; // clicks away from the body fall through to the playfield
            }

            // Phones: a tap close to the slider's centre LINE offers a small round "+" to add an anchor there; a tap on
            // the wide body away from the line still selects the body (orange) so the slider can be moved comfortably.
            if (UI.MobileMetrics.IsMobile)
            {
                float lineRadius = Math.Max(9f, diameter * 0.22f);
                if (nearestOnPath(pos, out Vector2 onPath, out float lineDistance) && lineDistance <= lineRadius)
                {
                    showAddPrompt(onPath);
                    return true;
                }
            }

            closeAddPrompt();
            BodyClicked?.Invoke(); // selecting the body part keeps this slider selected
            return true;
        }

        private Container? addPrompt;

        /// <summary>The closest point on the rendered slider path to <paramref name="p"/>, and its distance.</summary>
        private bool nearestOnPath(Vector2 p, out Vector2 closest, out float distance)
        {
            closest = p;
            distance = float.MaxValue;

            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], ab = path[i] - a;
                float lengthSq = ab.LengthSquared;
                float t = lengthSq <= 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
                Vector2 q = a + ab * t;
                float d = Vector2.Distance(p, q);
                if (d < distance)
                {
                    distance = d;
                    closest = q;
                }
            }

            return distance < float.MaxValue;
        }

        /// <summary>Inserts a new anchor at <paramref name="at"/> (on the nearest polygon segment), grid-snapped when enabled.</summary>
        private void insertAnchorAt(Vector2 at)
        {
            int insertAt = nearestSegmentIndex(at);
            controlPoints.Insert(insertAt, new SliderControlPoint(SnapToGrid?.Invoke(at) ?? at));
            actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
        }

        /// <summary>
        /// Shows a small round white "+" next to the tapped spot on the slider line (and a tiny ring on the exact spot);
        /// tapping the "+" adds an anchor there. Tapping anywhere else dismisses it (or moves it, if on the line).
        /// </summary>
        private void showAddPrompt(Vector2 at)
        {
            closeAddPrompt();
            closeContextMenu();

            const float button_size = 34f;
            Vector2 buttonPos = new Vector2(
                Math.Clamp(at.X + button_size, button_size, ParsedBeatmap.PLAYFIELD_WIDTH - button_size),
                Math.Clamp(at.Y - button_size, button_size, ParsedBeatmap.PLAYFIELD_HEIGHT - button_size));

            addPrompt = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new ClickCatcher { Action = closeAddPrompt, PassThrough = true },
                    new CircularContainer
                    {
                        Origin = Anchor.Centre,
                        Position = at,
                        Size = new Vector2(10),
                        Masking = true,
                        BorderThickness = 2f,
                        BorderColour = Color4.White,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent },
                    },
                    new AddPointButton(() => insertAnchorAt(at))
                    {
                        Origin = Anchor.Centre,
                        Position = buttonPos,
                        Size = new Vector2(button_size),
                    },
                },
            };
            AddInternal(addPrompt);
        }

        private void closeAddPrompt()
        {
            addPrompt?.Expire();
            addPrompt = null;
        }

        private int nearestSegmentIndex(Vector2 p)
        {
            int best = controlPoints.Count;
            float bestDist = float.MaxValue;

            for (int i = 1; i < controlPoints.Count; i++)
            {
                float d = pointSegmentDistance(p, controlPoints[i - 1].Position, controlPoints[i].Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        private bool nearPath(Vector2 p, float radius)
        {
            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);
            for (int i = 1; i < path.Count; i++)
            {
                if (pointSegmentDistance(p, path[i - 1], path[i]) <= radius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Live preview of the reshaped slider body using the TICK-SNAPPED length (exactly what the commit will
        /// produce), so the mapper sees the real-time size snapping as they drag - not the raw control polygon.
        /// </summary>
        private void showPreview()
        {
            var path = actions.SnappedSliderPath(sliderId, controlPoints.ToArray());
            if (path.Count < 2)
            {
                preview.Alpha = 0;
                return;
            }

            preview.Alpha = 1;
            preview.Vertices = path;
            preview.Position = -preview.PositionInBoundingBox(Vector2.Zero);
        }

        private static float pointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.LengthSquared;
            float t = lengthSq <= 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector2 clamp(Vector2 p) => new Vector2(
            Math.Clamp(p.X, 0, ParsedBeatmap.PLAYFIELD_WIDTH),
            Math.Clamp(p.Y, 0, ParsedBeatmap.PLAYFIELD_HEIGHT));

        /// <summary>A single draggable control-point handle; reports moves, clicks, drags and context-menu requests upward.</summary>
        private partial class ControlPointPiece : CircularContainer, IHasTooltip
        {
            public Action<int, Vector2, bool>? Moved;
            public Action<int, Vector2>? ContextRequested;
            public Action<int>? DragSelectRequested;
            public Action<int, bool>? Clicked;

            private readonly int index;
            private readonly SliderPathType? segmentType;
            private readonly Box fill;

            public ControlPointPiece(int index, SliderPathType? segmentType, float size)
            {
                this.index = index;
                this.segmentType = segmentType;

                // A typeless point continues the previous segment smoothly; a typed one starts a new segment
                // (a corner). The fill colour names which kind of segment it begins, mirroring osu!lazer:
                // yellow = smooth, red = straight/corner, purple = circular arc, pink = curved (bezier).
                Color4 colour = colourFor(segmentType);

                Origin = Anchor.Centre;
                Size = new Vector2(size);
                Masking = true;
                BorderThickness = 2f;
                BorderColour = Color4.White;
                Child = fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colour,
                    Alpha = 0.9f,
                };
            }

            // Phones: a bigger invisible touch target around the small handle so a finger can hit it reliably.
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            {
                if (!UI.MobileMetrics.IsMobile)
                    return base.ReceivePositionalInputAt(screenSpacePos);

                float radius = Math.Max(DrawWidth / 2f, 18f);
                return Vector2.Distance(ToLocalSpace(screenSpacePos), DrawSize / 2f) <= radius;
            }

            /// <summary>Highlights this handle when it is part of the multi-selection (a thicker yellow ring).</summary>
            public void SetSelected(bool value)
            {
                BorderThickness = value ? 4f : 2f;
                BorderColour = value ? EditorTheme.Colours.Selection : Color4.White;
            }

            private static Color4 colourFor(SliderPathType? type)
            {
                if (type is not SliderPathType t)
                    return OsuColour.Yellow; // smooth continuation point

                return t.Type switch
                {
                    SliderSplineType.Linear => EditorTheme.Colours.Error,   // straight segment / sharp corner
                    SliderSplineType.PerfectCurve => OsuColour.Purple,       // circular arc
                    _ => OsuColour.Pink,                                      // bezier / other curves
                };
            }

            public LocalisableString TooltipText => segmentType is not SliderPathType t
                ? "Smooth point - the curve flows through it (right-click for curve type)"
                : t.Type switch
                {
                    SliderSplineType.Linear => "Corner: straight segment (right-click for curve type)",
                    SliderSplineType.PerfectCurve => "Corner: circular arc (right-click for curve type)",
                    _ => "Corner: curved (bezier) (right-click for curve type)",
                };

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                // Right-click opens the curve-type / delete context menu (like osu!lazer). Consuming it also keeps
                // the playfield from deleting the whole slider.
                if (e.Button == osuTK.Input.MouseButton.Right)
                {
                    ContextRequested?.Invoke(index, Position);
                    return true;
                }

                return base.OnMouseDown(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                DragSelectRequested?.Invoke(index);
                return true;
            }

            protected override void OnDrag(DragEvent e) => Moved?.Invoke(index, e.ScreenSpaceMousePosition, false);

            protected override void OnDragEnd(DragEndEvent e) => Moved?.Invoke(index, e.ScreenSpaceMousePosition, true);

            protected override bool OnClick(ClickEvent e)
            {
                Clicked?.Invoke(index, Shortcut.CommandPressed(e) || e.ShiftPressed);
                return true; // swallow so the playfield doesn't treat it as an empty-space click
            }

            protected override bool OnHover(HoverEvent e)
            {
                fill.Alpha = 1f;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                fill.Alpha = 0.85f;
                base.OnHoverLost(e);
            }
        }

        /// <summary>One row in the shift+right-click curve-type menu: a coloured dot + label, highlighting on hover.</summary>
        private partial class MenuButton : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Box background;

            public MenuButton(string label, Color4 dot, Action onClick, bool destructive = false)
            {
                this.onClick = onClick;

                AutoSizeAxes = Axes.Y;
                Width = 132;
                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay, Alpha = 0 },
                    new FillFlowContainer
                    {
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(7, 0),
                        Margin = new MarginPadding { Horizontal = 9, Vertical = 5 },
                        Children = new Drawable[]
                        {
                            new Circle
                            {
                                Size = new Vector2(8),
                                Colour = dot,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new SpriteText
                            {
                                Text = label,
                                Font = FontUsage.Default.With(size: 13),
                                Colour = destructive ? EditorTheme.Colours.Error : EditorTheme.Colours.Text,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.Alpha = 1f;
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => background.Alpha = 0f;

            // Consume the press so it never reaches the full-area ClickCatcher behind the menu - otherwise the
            // catcher would close (Expire) the menu on mouse-down, before this button's OnClick could ever fire.
            protected override bool OnMouseDown(MouseDownEvent e) => true;

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }

        /// <summary>Small round white "+" button (accent outline, like the other phone action buttons) that adds an anchor.</summary>
        private partial class AddPointButton : ClickableContainer
        {
            public AddPointButton(Action onClick)
            {
                Action = onClick;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                Child = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = 2.5f,
                    BorderColour = EditorTheme.Colours.Accent,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                        new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = FontAwesome.Solid.Plus,
                            Size = new Vector2(15),
                            Colour = Color4.White,
                        },
                    },
                };
            }

            // Consume the press so the full-area catcher behind never dismisses the prompt before this click resolves.
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }

        /// <summary>A transparent full-area layer behind the menu that closes it when clicked.</summary>
        private partial class ClickCatcher : Drawable
        {
            public Action? Action;

            /// <summary>When true the click closes the layer but is NOT consumed, so it still reaches the slider/handles underneath.</summary>
            public bool PassThrough;

            public ClickCatcher()
            {
                RelativeSizeAxes = Axes.Both;
            }

            // AlwaysPresent so it receives input even though it's fully transparent.
            public override bool IsPresent => true;

            protected override bool OnClick(ClickEvent e)
            {
                Action?.Invoke();
                return !PassThrough;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                // Only dismiss on a right-click press (so a fresh shift+right-click elsewhere doesn't stack menus).
                // A left press must NOT close here: doing so on every mouse-down expired the menu before a menu
                // button's click could resolve, which made the whole menu unclickable. Left-clicks outside the
                // panel still dismiss via OnClick above; the menu buttons consume their own press.
                if (e.Button == osuTK.Input.MouseButton.Right)
                {
                    Action?.Invoke();
                    return false;
                }

                return false;
            }
        }
    }
}
