using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Timing;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The osu! Standard play area (512x384 osu!pixels). Fills its parent and keeps the play area
    /// centred and uniformly scaled to fit, rendering each hit object in osu!pixel coordinates.
    /// Hit-object visibility tracks <see cref="TimeSource"/> (the audio position) every frame.
    /// </summary>
    public partial class Playfield : Container
    {
        /// <summary>Supplies the current playback time in milliseconds (typically the audio track).</summary>
        public Func<double>? TimeSource;

        /// <summary>Supplies the beat-snapped current time (for the placement preview's combo number).</summary>
        public Func<double>? SnappedTimeSource;

        /// <summary>Resolves where a circle placement at a cursor position would actually land (distance/magnetic snap).</summary>
        public Func<Vector2, Vector2>? PlacementSnap;

        /// <summary>Snaps an osu!-space position to the grid when grid snap is on (else returns it unchanged). Applied to
        /// slider anchors while drawing and to control-point handles while editing, so the whole slider follows the grid.</summary>
        public Func<Vector2, Vector2>? GridSnap;

        /// <summary>Resolves the Shift-held spacing guide (snapped position + distance lines) for a cursor position.</summary>
        public Func<Vector2, SpacingGuide>? SpacingGuideResolver;

        /// <summary>Spacing (osu!pixels) between slider ticks for a given slider, honouring tempo/SV/tick-rate; 0 = none.</summary>
        public Func<HitObjectModel, double>? SliderTickDistance;


        [Resolved]
        private EditorSelection selection { get; set; } = null!;

        [Resolved]
        private NodeSelection nodeSelection { get; set; } = null!;

        [Resolved]
        private IEditorActions actions { get; set; } = null!;

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private EditableBeatmap editable { get; set; } = null!;

        // Active skin (cached at the game root; absent under the test browser). Lets the placement preview match a
        // committed slider's skin-aware body colours.
        [Resolved(CanBeNull = true)]
        private Skinning.SkinManager? skinManager { get; set; }

        private bool moving;
        private Vector2 moveStart;

        // Review-mode "suggest a move": dragging objects in Review/Select previews a ghost at the new position and,
        // on release, raises OnReviewCreateMoveSuggestion with the delta (the editor turns it into a shareable
        // suggestion). It never touches the real objects.
        private bool reviewMoving;
        private Vector2 reviewMoveStart;
        private Container? reviewMovePreview;

        /// <summary>Raised when a Review/Select object drag finishes: the (dx, dy) osu!pixel move to suggest for the selection.</summary>
        public Action<Vector2>? OnReviewCreateMoveSuggestion;

        // Rubber-band box-select state.
        private bool boxSelecting;
        private Vector2 boxStart;
        private Vector2 boxCurrent;
        private Box? selectionBox;
        private HashSet<int> dragBaseline = new HashSet<int>();
        // Objects picked by the box so far. Box selection accumulates: while the box is held during playback,
        // objects that fade in inside it are added and never removed as they fade out again.
        private HashSet<int> boxSelected = new HashSet<int>();

        // Grid sizes (osu!pixels) cycled by the G key, matching osu!lazer's editor; 0 = grid off.
        private static readonly float[] grid_sizes = { 4f, 8f, 16f, 32f, 0f };
        private int gridSizeIndex = 2; // default to 16px, like lazer

        private readonly Container playArea;
        private Container gridContainer = null!;
        private Container playfieldBorder = null!;
        private readonly HitObjectLifetimeContainer followPointContainer;
        private readonly HitObjectLifetimeContainer hitObjectContainer;
        private readonly HitObjectLifetimeContainer angleContainer;
        private readonly Container selectionLayer;
        private readonly Container overlayLayer;
        private readonly AnnotationLayer annotationLayer;
        private readonly Container annotationHighlightLayer;
        private readonly Container modifiedHighlightLayer;
        // Persistent per-object highlight groups (so their alpha can be eased in sync with the note's fade rather
        // than rebuilt on/off); rebuilt only when the set of highlighted objects or their colours changes.
        private readonly Dictionary<int, Container> annotationHighlightGroups = new Dictionary<int, Container>();
        private string annotationHighlightSig = "";

        private bool reviewMode;

        /// <summary>When true, the playfield is in Review mode: object editing is suppressed and clicks drop notes.
        /// Also tints the snapping grid reddish so it's obvious you're reviewing.</summary>
        public bool ReviewMode
        {
            get => reviewMode;
            set
            {
                if (reviewMode == value)
                    return;

                reviewMode = value;
                buildGrid(); // recolour the grid (reddish while reviewing)
                playfieldBorder.BorderColour = value ? new Color4(0.82f, 0.31f, 0.36f, 1f) : OsuColour.TextMuted;
            }
        }

        /// <summary>The active Review tool (Select / Note / Line), driving what a playfield click does in Review mode.</summary>
        public ReviewTool ReviewTool;

        /// <summary>Raised when a click in Review mode should create a note at the given osu!pixel position.</summary>
        public Action<Vector2>? OnReviewCreateNote;

        /// <summary>Supplies the modder's current colour, for the suggestion ghost/arrow preview.</summary>
        public Func<Color4>? ReviewLineColour;

        /// <summary>The modding-annotation overlay (notes), driven by the editor's Review layer.</summary>
        public AnnotationLayer Annotations => annotationLayer;
        private CircularContainer placementPreview = null!;
        private Box placementFill = null!;
        private SpriteText placementNumber = null!;

        // Solid distance lines + gap labels shown under the ghost while placing with Shift held (spacing guide).
        private Container spacingGuideLayer = null!;

        // Live drawable + model per object id, so edits sync incrementally instead of rebuilding everything.
        private readonly List<DrawableHitObject> objects = new List<DrawableHitObject>();
        private readonly Dictionary<int, DrawableHitObject> drawableMap = new Dictionary<int, DrawableHitObject>();
        private readonly Dictionary<int, HitObjectModel> modelMap = new Dictionary<int, HitObjectModel>();
        private float lastDiameter = -1;
        private double lastPreempt = -1;

        private IReadOnlyList<HitObjectModel> currentHitObjects = Array.Empty<HitObjectModel>();
        private float currentDiameter = 40f;

        // Mod-preview state: HardRock flips the play area vertically (about its centre, like osu!'s HR), Hidden
        // makes objects fade out before they're hit. Set by the editor; purely visual, never saved.
        private bool modHardRock;
        private bool modHidden;
        // Set by SetMods (HardRock) so the next SetHitObjects rebuilds every drawable exactly once.
        private bool pendingFullRebuild;

        // Auto-mod preview: a cursor that plays the map. Purely visual, like the HR/HD previews.
        private AutoCursor autoCursor = null!;
        private bool modAuto;

        // Optional K1/K2 "tapping" overlay shown alongside the Auto cursor (a setting under the AU chip).
        private KeyOverlay keyOverlay = null!;
        private bool modKeyOverlay;
        // "Humanise" the Auto cursor: arc between objects, overshoot + correct, jitter and aim slightly off-centre.
        private bool modHumanize;
        // The object index the key overlay is currently lighting (so a new object bumps the count once).
        private int lastKeyObjectIndex = -1;

        // Each live follow-point connection plus the endpoints/ids it was built from, so a position drag can
        // recreate just the connections touching the selection instead of rebuilding the whole map (which lags).
        private sealed class FollowPointConnection
        {
            public int FromId, ToId;
            public Vector2 BaseStart, BaseEnd;
            public double StartTime, EndTime;
            public DrawableFollowPoints Drawable = null!;
        }

        private readonly List<FollowPointConnection> followPoints = new List<FollowPointConnection>();

        /// <summary>Whether the circle-placement tool is armed (a ghost circle follows the cursor).</summary>
        public bool PlacementActive { get; private set; }

        /// <summary>Whether the slider-placement tool is armed (drag from head to tail to create a linear slider).</summary>
        public bool SliderPlacementActive { get; private set; }

        /// <summary>Whether the spinner-placement tool is armed (click to start, scrub forward, click/right-click to end).</summary>
        public bool SpinnerPlacementActive { get; private set; }

        // Spinner build state: a left-click starts it at the current time; the end follows the playhead as the
        // user scrubs forward; a left- or right-click commits it. Mirrors osu!lazer's SpinnerPlacementBlueprint.
        private bool buildingSpinner;
        private CircularContainer? spinnerGhost;

        /// <summary>Whether the next placed object will start a new combo (toggled with Q).</summary>
        public bool NewComboArmed { get; set; }

        // Slider-build state: anchors committed by successive clicks (double-click = sharp corner),
        // right-click finishes (the cursor becomes the tail), Esc cancels.
        private bool buildingSlider;
        // The playhead time when the head was placed. Locked in so scrubbing the timeline while adding anchors
        // doesn't move/re-snap the slider (its start is already established).
        private double sliderBuildStartTime;
        private readonly List<SliderControlPoint> sliderAnchors = new List<SliderControlPoint>();
        private double lastAnchorClickTime = double.MinValue;
        // The live placement preview renders the *actual* slider body (same SliderBodyPath as a committed
        // slider) plus a ghost tail circle, so you see the finished slider while drawing it (lazer-style).
        private SliderBodyPath? sliderPreview;
        private CircularContainer? sliderTailPreview;
        // Node markers shown on each anchor (and the cursor) while a slider is being drawn.
        private Container? sliderNodes;

        // Live control-point editor for the currently-selected single slider, plus its base position (the
        // slider's stack offset) so a move preview can add the drag offset on top without losing the stacking.
        private SliderControlPointVisualiser? controlPoints;
        private Vector2 controlPointsBase;

        // Transform box (rotate/scale/flip), shown while Shift is held with a selection.
        private SelectionBox? transformBox;
        private bool transforming;

        /// <summary>True while a slider is being traced (head placed, awaiting more anchors / finish).</summary>
        public bool BuildingSlider => buildingSlider;

        public Playfield()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
            // The Auto "tapping" overlay floats at the right edge of the play area (screen space), like lazer.
            keyOverlay = new KeyOverlay
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                // Phones: sits in the open space just left of the modifier column (beside AU), a little below the vertical centre.
                Margin = UI.MobileMetrics.IsMobile ? new MarginPadding { Right = 77, Top = 68 } : new MarginPadding { Right = 12 },
                Alpha = 0,
            },
            playArea = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH, ParsedBeatmap.PLAYFIELD_HEIGHT),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.BackgroundDark,
                        Alpha = 0.6f,
                    },
                    // Snapping grid (toggled / resized with G), behind everything else.
                    gridContainer = new Container { RelativeSizeAxes = Axes.Both },
                    playfieldBorder = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = 2,
                        BorderColour = OsuColour.TextMuted,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Transparent,
                        },
                    },
                    // Follow points sit beneath the hit objects. Both are lifetime-managed so only on-screen
                    // objects are realised/updated/drawn (like lazer's HitObjectContainer).
                    followPointContainer = new HitObjectLifetimeContainer { RelativeSizeAxes = Axes.Both },
                    hitObjectContainer = new HitObjectLifetimeContainer { RelativeSizeAxes = Axes.Both },
                    // Review-mode highlights: outlines (in the note author's colour) of the objects a shown note refers to.
                    annotationHighlightLayer = new Container { RelativeSizeAxes = Axes.Both },
                    // Red outlines around objects changed by a suggestion (shown in the "Suggested changes" preview).
                    modifiedHighlightLayer = new Container { RelativeSizeAxes = Axes.Both },
                    // Persistent yellow selection outlines (always visible, independent of object fade).
                    selectionLayer = new Container { RelativeSizeAxes = Axes.Both },
                    // Review-mode angle indicators (opening angle between objects), above the objects. Lifetime-managed
                    // like the objects/follow points so only on-screen angles are realised.
                    angleContainer = new HitObjectLifetimeContainer { RelativeSizeAxes = Axes.Both },
                    // Placement preview + rubber-band box. The spacing-guide lines sit under the ghost circle.
                    overlayLayer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            spacingGuideLayer = new Container { RelativeSizeAxes = Axes.Both },
                            buildPlacementPreview(),
                        },
                    },
                    // Review-mode modding annotations (notes), above the objects/selection but below the AU cursor.
                    annotationLayer = new AnnotationLayer { TimeSource = () => TimeSource?.Invoke() ?? 0 },
                    // Auto-mod preview cursor, on top of everything (osu!pixel space = the play area itself).
                    autoCursor = new AutoCursor { PositionSource = autoCursorPosition },
                },
            },
            };

            buildGrid();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            selection.Changed += updateSelection;
            nodeSelection.Changed += updateSelection;

            // Review notes drive the highlight outlines of the objects they reference.
            annotationLayer.OnHighlightsChanged = SetAnnotationHighlights;

            // Rebuild all objects live when the relevant appearance settings change.
            settings.ObjectBackgroundOpacity.ValueChanged += _ => rebuildAppearance();
            settings.ObjectBorderThickness.ValueChanged += _ => rebuildAppearance();
            settings.SliderTickSize.ValueChanged += _ => rebuildAppearance();
            settings.ObjectFadeOut.ValueChanged += _ => rebuildAppearance();
            // Combo palette changes (editor palette, map colours, or the use-map-colours toggle) all funnel
            // through EditableBeatmap.ColoursChanged - rebuild so objects pick up the new colours live.
            editable.ColoursChanged += rebuildAppearance;
        }

        /// <summary>
        /// Forces every hit-object drawable to be recreated. Used when a global value that the incremental
        /// sync can't see in the per-object models changes (e.g. slider tick rate / velocity affect the ticks).
        /// </summary>
        public void RebuildObjects() => rebuildAppearance();

        /// <summary>When set, objects are tinted by who placed them (authorship mode) instead of by combo colour.</summary>
        public Func<HitObjectModel, Color4?>? AuthorColourProvider;

        /// <summary>Sets (or clears) the per-object author-colour provider and repaints every object once.</summary>
        public void SetAuthorColouring(Func<HitObjectModel, Color4?>? provider)
        {
            AuthorColourProvider = provider;
            rebuildAppearance();
        }

        /// <summary>
        /// Sets the active mod-preview state. HardRock changes circle size (diameter) + AR (preempt) and flips
        /// the field, so it needs every drawable rebuilt - it just flags the rebuild and lets the caller's
        /// following <c>SetHitObjects</c> do it once (avoids a double rebuild). Hidden only changes the fade, so
        /// it's applied in place on the existing drawables (no recreation) to keep the toggle snappy.
        /// </summary>
        public void SetMods(bool hardRock, bool hidden)
        {
            bool hrChanged = modHardRock != hardRock;
            bool hdChanged = modHidden != hidden;
            if (!hrChanged && !hdChanged)
                return;

            modHardRock = hardRock;
            modHidden = hidden;

            // HardRock alters diameter/preempt → the next SetHitObjects must rebuild everything.
            if (hrChanged)
                pendingFullRebuild = true;

            // Hidden is a pure fade change: retarget the existing drawables instead of recreating them.
            if (hdChanged && !hrChanged)
            {
                foreach (var d in objects)
                    d.SetHidden(hidden);
            }
        }

        /// <summary>Toggles the Auto-mod preview cursor (visual only).</summary>
        public void SetAutoPlay(bool enabled) => modAuto = enabled;

        /// <summary>Whether the Auto-mod preview cursor is currently on (so the video exporter can save/restore it).</summary>
        public bool AutoPlayEnabled => modAuto;

        // Saved alphas of the editor-chrome layers while presentation mode hides them, so exiting restores exactly.
        private readonly System.Collections.Generic.Dictionary<Drawable, float> presentationSavedAlpha = new System.Collections.Generic.Dictionary<Drawable, float>();

        // While true (video export), the play area shrinks to osu!'s gameplay 0.8 fit so edge objects aren't clipped.
        private bool presentationMode;
        private const float presentation_scale = 0.8f;

        // In presentation mode objects fade out fast (like being hit in real gameplay) instead of the editor's
        // slower, authoring-friendly fade. Circles/ends vanish shortly after their hit time.
        private const double presentation_fade_out = 240;

        /// <summary>Object fade-out duration: a quick gameplay-like fade while rendering a video, else the editor setting.</summary>
        private double effectiveFadeOut => presentationMode ? presentation_fade_out : settings.ObjectFadeOut.Value;

        /// <summary>
        /// Hides every editor-only layer (grid, border, selection/annotation highlights, placement previews, angle
        /// indicators) so only the background + hit objects + Auto cursor remain - a clean gameplay view for the video
        /// exporter. Alphas are saved on entry and restored on exit, so it round-trips regardless of prior state.
        /// </summary>
        public void SetPresentationMode(bool on)
        {
            presentationMode = on;

            Drawable[] chrome =
            {
                gridContainer, playfieldBorder, selectionLayer, overlayLayer,
                annotationLayer, annotationHighlightLayer, modifiedHighlightLayer, angleContainer,
            };

            if (on)
            {
                presentationSavedAlpha.Clear();
                foreach (var d in chrome)
                {
                    presentationSavedAlpha[d] = d.Alpha;
                    d.Alpha = 0;
                }
            }
            else
            {
                foreach (var d in chrome)
                {
                    if (presentationSavedAlpha.TryGetValue(d, out float saved))
                        d.Alpha = saved;
                }
                presentationSavedAlpha.Clear();
            }

            // Rebuild so the objects pick up the presentation fade-out (fast, gameplay-like) or revert to the editor's.
            rebuildAppearance();
        }

        /// <summary>Sets the Auto cursor colour.</summary>
        public void SetAutoColour(Color4 colour) => autoCursor.SetColour(colour);

        /// <summary>Sets the Auto cursor trail length (number of trailing segments).</summary>
        public void SetAutoTrailLength(int length) => autoCursor.SetTrailLength(length);

        /// <summary>Sets the Auto cursor trail thickness multiplier.</summary>
        public void SetAutoTrailWidth(float width) => autoCursor.SetTrailWidth(width);

        /// <summary>Toggles the "humanised" Auto cursor (arcs, overshoot, jitter, aim error). Visual only.</summary>
        public void SetHumanize(bool enabled) => modHumanize = enabled;

        /// <summary>Toggles the K1/K2 "tapping" overlay shown alongside the Auto cursor (visual only).</summary>
        public void SetKeyOverlay(bool enabled)
        {
            modKeyOverlay = enabled;
            if (!enabled)
            {
                lastKeyObjectIndex = -1;
                keyOverlay.Reset();
            }
        }

        /// <summary>How long after a circle's start the key stays lit, so a tap is visible (sliders/spinners use their own duration).</summary>
        private const double key_hold_ms = 80;

        /// <summary>
        /// Drives the Auto key overlay from the current playback time: finds the object being "tapped" (the last one
        /// whose hit window covers now), alternates K1/K2 per object like a real player, and bumps the count on each
        /// new object. Only visible while both Auto and the overlay setting are on.
        /// </summary>
        private void updateKeyOverlay()
        {
            bool show = modAuto && modKeyOverlay && currentHitObjects.Count > 0;
            keyOverlay.Alpha = show ? 1 : 0;

            if (!show)
            {
                if (lastKeyObjectIndex != -1)
                {
                    lastKeyObjectIndex = -1;
                    keyOverlay.SetHeld(-1);
                }
                return;
            }

            double time = TimeSource?.Invoke() ?? 0;
            var objs = currentHitObjects;

            // Last object whose start is at/before now (binary search; the list is time-sorted).
            int lo = 0, hi = objs.Count - 1, prev = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (objs[mid].StartTime <= time)
                {
                    prev = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }

            // Active only while now is within the object's hit window (its duration, or a short window for circles).
            int active = -1;
            if (prev >= 0)
            {
                double end = objs[prev].StartTime + Math.Max(objs[prev].Duration, key_hold_ms);
                if (time <= end)
                    active = prev;
            }

            if (active != lastKeyObjectIndex)
            {
                lastKeyObjectIndex = active;
                if (active >= 0)
                    keyOverlay.Press(active & 1); // alternate K1/K2 per object
            }

            keyOverlay.SetHeld(active >= 0 ? active & 1 : -1);
        }

        /// <summary>
        /// The osu!pixel position of the Auto cursor at the current playback time, or null when the preview is
        /// off / there are no objects. Ported from the behaviour of osu!lazer's Auto generator: rest on circles,
        /// trace sliders along the path (bouncing across repeats), spin on spinners, glide between objects.
        /// </summary>
        private Vector2? autoCursorPosition()
        {
            if (!modAuto || currentHitObjects.Count == 0)
                return null;

            double time = TimeSource?.Invoke() ?? 0;
            var objs = currentHitObjects;

            // Last object whose start is at/before the current time (binary search; list is time-sorted).
            int lo = 0, hi = objs.Count - 1, prev = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (objs[mid].StartTime <= time)
                {
                    prev = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }

            Vector2 pos = autoBasePosition(prev, time, objs);

            // Humanise: a faint, continuous hand-tremor on top of whatever base position we landed on (resting on an
            // object, following a slider, or gliding through a gap). The path/aim humanisation is folded into the base
            // position itself; this only adds the unsteady-hand wobble.
            if (modHumanize)
                pos += humanJitter(time) * HumanizeTuning.TremorAmount;

            return pos;
        }

        /// <summary>The exact (un-jittered) Auto position for the current time, given the index of the last started object.</summary>
        private Vector2 autoBasePosition(int prev, double time, IReadOnlyList<HitObjectModel> objs)
        {
            // Before the first object: wait on its start position.
            if (prev < 0)
                return autoStartPos(objs[0]);

            var cur = objs[prev];

            // When humanising, the player lets go of a slider early — how early depends on momentum (the flow of the
            // surrounding rhythm). curEnd is that release time; the gap to the next object starts from it. A kick in a
            // fast stream releases at the head (flows like a circle); a slider in a fast burst releases partway (a
            // brief "amago" then off to the next); an isolated/slow slider is followed almost to the end.
            double curEnd = modHumanize ? sliderReleaseTime(objs, prev) : autoEndTime(cur);

            // Inside the current object (a long slider / spinner): follow it. Leaving a slider toward a next note, the
            // follow already anticipates — drifting toward that note before the tail (path of least effort), but only
            // when that note is downstream of the exit flow (see sliderAnticipation).
            var (followAnticipate, anticipateStrength) = sliderAnticipation(objs, prev, curEnd);
            if (time <= curEnd)
            {
                // A circle — or a kick slider, which is played AS a circle at its head (sliderExit is false, so both
                // surrounding gaps take their endpoint from nodeClean) — is a single instant here. Return the SAME
                // spline knot those gaps use (nodeClean, which straightens a stream note), not the raw head, or this one
                // frame flicks to the un-straightened position and back = a teleport at every such note in a stream.
                // Only a real, followed slider (sliderExit) or a spinner falls through to the actual follow.
                if (modHumanize && (cur.Kind == HitObjectKind.Circle || isKickSlider(cur)))
                    return nodeClean(objs, prev) + aimError(cur);
                return autoFollowPos(cur, time, followAnticipate, curEnd, anticipateStrength);
            }

            // After the last object: rest where it ended.
            if (prev + 1 >= objs.Count)
                return autoEndPos(cur);

            // In the gap between two objects: glide from one to the next.
            var next = objs[prev + 1];
            double from = curEnd;
            double to = next.StartTime;
            if (to <= from)
                return autoStartPos(next);

            // Leave WITH slider momentum only if the cursor actually followed the slider for a bit; if it released right
            // at the head (a kick / very high momentum) there's no follow to carry out, so flow on like a circle.
            bool sliderExit = modHumanize && cur.Kind == HitObjectKind.Slider && (curEnd - cur.StartTime) > 8.0;
            // The exit point is the anticipated follow at release (already leaning toward next), matching the follow
            // branch above at time == curEnd so the handoff into the gap is continuous.
            Vector2 a = sliderExit ? autoFollowPos(cur, curEnd, followAnticipate, curEnd, anticipateStrength) : autoEndPos(cur);
            Vector2 b = autoStartPos(next);
            double f = Math.Clamp((time - from) / (to - from), 0, 1);

            // Humanised gap: flow through tight spacing (streams / close jumps) on a curve, overshoot + correct
            // only on very large jumps. Otherwise the perfect Auto easing (smoothstep) along a straight line.
            if (modHumanize)
            {
                // Carry the cursor's ACTUAL exit velocity into the gap (measured off the follow over the last few ms,
                // including any anticipation drift) so there's no velocity jump — the slider→gap handoff is C1, no tug.
                const double h = 12.0;
                Vector2 exitBack = sliderExit ? autoFollowPos(cur, Math.Max(cur.StartTime, curEnd - h), followAnticipate, curEnd, anticipateStrength) : a;
                Vector2 exitVel = sliderExit ? (a - exitBack) / (float)Math.Min(h, curEnd - cur.StartTime + 1e-3) : Vector2.Zero;
                return humanGapPosition(objs, prev, sliderExit, a, b, exitVel, f, to - from);
            }

            f = f * f * (3 - 2 * f); // smoothstep
            return Vector2.Lerp(a, b, (float)f);
        }

        /// <summary>
        /// When humanising, the time the player lets go of a slider — driven by momentum (the flow of the surrounding
        /// rhythm). A slow/isolated slider is followed to a tiny lead before its end. As the context speeds up (or the
        /// slider is a very short kick), the player commits less and releases earlier: a kick releases right at the
        /// head (so it streams like a circle, no dwell), a longer slider in a fast burst releases partway (a brief
        /// "amago" of following, then off to the next). Returns the object's natural end for non-sliders.
        /// </summary>
        private double sliderReleaseTime(IReadOnlyList<HitObjectModel> objs, int prev)
        {
            var o = objs[prev];
            if (o.Kind != HitObjectKind.Slider)
                return autoEndTime(o);

            double dur = Math.Max(0, o.Duration);
            double normalRelease = autoEndTime(o) - Math.Min(dur * HumanizeTuning.SliderReleaseFrac, HumanizeTuning.SliderReleaseMaxMs);
            if (prev + 1 >= objs.Count)
                return normalRelease; // nothing to flow to: follow it (almost) to the end

            // rel = how much of the slider to skip (release early). A very short kick forces rel→1 (release at the head);
            // otherwise it scales with the flow of the rhythm here (fast burst => release earlier => brief amago).
            float kickness = 1f - smoothstep((float)dur, HumanizeTuning.KickSliderMaxMs, HumanizeTuning.KickSliderMaxMs * 1.5f);
            float flowRel = flowFactor(objs, prev) * HumanizeTuning.SliderFlowRelease;
            float rel = Math.Clamp(Math.Max(kickness, flowRel), 0f, 1f);
            return o.StartTime + (1f - rel) * (normalRelease - o.StartTime);
        }

        /// <summary>
        /// The "path of least effort" lead when leaving a slider: the head to drift the follow toward (the next object)
        /// and how strongly. Anticipation only fires when the next note is DOWNSTREAM of the slider's exit flow — the
        /// player already heads where they're going. If it's back the way the slider came, the strength gates to 0 and
        /// the cursor follows to the end, then turns. Returns (null, 0) when there's nothing to anticipate.
        /// </summary>
        private (Vector2? head, float strength) sliderAnticipation(IReadOnlyList<HitObjectModel> objs, int prev, double curEnd)
        {
            var cur = objs[prev];
            if (!modHumanize || cur.Kind != HitObjectKind.Slider || prev + 1 >= objs.Count || HumanizeTuning.SliderAnticipation <= 0.001f)
                return (null, 0f);

            Vector2 head = autoStartPos(objs[prev + 1]);

            // Pure slider exit direction (the geometric tangent at release, no anticipation) = the "flow" of the slider.
            const double h = 12.0;
            Vector2 exit = autoFollowPos(cur, curEnd);
            Vector2 exitPrev = autoFollowPos(cur, Math.Max(cur.StartTime, curEnd - h));
            Vector2 exitDir = safeNormalize(exit - exitPrev);
            Vector2 toNext = safeNormalize(head - exit);

            float align = Vector2.Dot(exitDir, toNext); // 1 = next is straight ahead of the exit, -1 = straight behind
            float gate = smoothstep(align, HumanizeTuning.AnticipationAlignLo, HumanizeTuning.AnticipationAlignHi);
            float strength = HumanizeTuning.SliderAnticipation * gate;
            return strength > 0.001f ? (head, strength) : (null, 0f);
        }

        /// <summary>
        /// A "human" glide from exit point <paramref name="a"/> (time tA) to the next object's head <paramref name="b"/>
        /// (time tB) over linear progress <paramref name="f"/>. This is one segment of a cubic Hermite spline laid
        /// through the hit objects in TIME: each object's velocity is its Catmull-Rom tangent scaled by a flow factor
        /// φ (<see cref="flowFactor"/>). Because the same φ·velocity is shared by the gaps before and after an object,
        /// the path is C1 (position AND velocity continuous) everywhere — it cannot teleport. That single φ also sets
        /// the feel with no special cases: a stream (short, regular gaps) keeps φ≈1 and sweeps THROUGH each note; an
        /// isolated note has φ≈0, which collapses the Hermite to a smoothstep that eases in and settles on the target.
        /// Stacks fall out for free (a near-zero-length gap barely moves) and rotational patterns arc naturally from
        /// the tangents (no turn / figure-of-eight logic). A subtle, gated overshoot is the only added flourish.
        /// </summary>
        private Vector2 humanGapPosition(IReadOnlyList<HitObjectModel> objs, int prev, bool sliderExit, Vector2 a, Vector2 b, Vector2 sliderExitVel, double f, double gapMs)
        {
            // Stream "path of least effort": pull each endpoint onto the straightened (zig-zag-free) note centre while
            // keeping its aim-error offset, so the cursor cuts nearly straight through a small stream zig-zag instead of
            // tracing it. nodeClean is identity on jumps, so those endpoints are unchanged. (Leaving a slider keeps its
            // exit point.) The tangents below use nodeClean too, so the whole stream path stays C1.
            if (!sliderExit)
                a = nodeClean(objs, prev) + aimError(objs[prev]);
            b = nodeClean(objs, prev + 1) + aimError(objs[prev + 1]);

            float dt = (float)Math.Max(1, gapMs);
            float s = (float)Math.Clamp(f, 0, 1);
            float chord = (b - a).Length;

            // Reaction delay: after a PAUSE a real player doesn't drift the whole gap — they hold position and only move
            // once they've reacted to the upcoming circle, then cover the distance late and arrive on time. So on a long
            // gap remap progress to DWELL near A, then make the move over the last ReactionMoveMs (smoothstep, arriving
            // settled). Short gaps (streams / close jumps) get react≈0 and flow through unchanged. Still a pure function
            // of time and continuous in f, so C1 / no-teleport holds. vA/vB are already small on these low-flow gaps (the
            // slider-exit cap shrinks with gap length), so freezing the start doesn't fight any real exit momentum.
            float react = smoothstep((float)gapMs, HumanizeTuning.ReactionShortMs, HumanizeTuning.ReactionLongMs);
            if (react > 0.001f)
            {
                float moveFrac = Math.Clamp((float)(HumanizeTuning.ReactionMoveMs / Math.Max(1.0, gapMs)), 0.05f, 1f);
                float dwellEnd = 1f - moveFrac;
                float moved = s <= dwellEnd ? 0f : smoothstep((s - dwellEnd) / moveFrac, 0f, 1f);
                s = s + react * (moved - s); // lerp(s, moved, react)
            }

            // Velocity (osu!px/ms) at each end of the gap. vB is the next head's Catmull-Rom tangent scaled by its flow
            // factor; vA is the previous object's — except leaving a slider, where we carry the cursor's ACTUAL exit
            // velocity (measured off the follow itself, sliderExitVel) so the gap continues with no velocity jump — the
            // handoff is C1 and there's no tug. Capped so a very fast slider into a long gap can't fling the Hermite
            // past the next note (a bounded magnitude discontinuity only in that pathological case).
            Vector2 vB = knotVel(objs, prev + 1);
            Vector2 vA;
            if (sliderExit)
            {
                float cap = slider_exit_speed_cap * chord / dt;
                float sp = sliderExitVel.Length;
                vA = sp > cap && sp > 1e-4f ? sliderExitVel * (cap / sp) : sliderExitVel;
            }
            else
                vA = knotVel(objs, prev);

            // Cubic Hermite through A,B with those velocities (Hermite tangents = velocity × Δt). With small tangents
            // this is exactly a smoothstep (ease-in/out, the cursor settles); with full tangents it sweeps straight through.
            float s2 = s * s, s3 = s2 * s;
            float h00 = 2f * s3 - 3f * s2 + 1f;
            float h10 = s3 - 2f * s2 + s;
            float h01 = -2f * s3 + 3f * s2;
            float h11 = s3 - s2;
            Vector2 pos = a * h00 + (vA * dt) * h10 + b * h01 + (vB * dt) * h11;

            // --- Human character on top of the clean C1 spline. Both terms below are zero in VALUE and SLOPE at s=0
            // and s=1, so they never disturb the shared knot velocity — the no-teleport (C1) guarantee still holds. ---
            float radius = currentDiameter * 0.5f;
            float scaling = radius > 0.01f ? 50f / radius : 1f;   // osu!'s normalised_radius = 50 (diameter = 100)
            float chordLen = chord;
            float dNorm = chordLen * scaling;
            float gapFlow = shortness(gapMs);                     // 1 = a fast (stream) gap, 0 = a slow (jump) gap

            // Lateral arc — the biggest "human vs robot" tell. Replay analysis (real lazer cursors) shows a bow of
            // 6-21% of the jump OFF the straight A→B line, larger on shorter jumps: arcFrac = ArcAmount·F/(F+dNorm).
            // The Hermite already curves streams (high flow), so the explicit bow is weighted onto the jump gaps.
            if (gapFlow < 0.999f && dNorm > 1f && chordLen > 0.001f)
            {
                float arcFrac = HumanizeTuning.ArcAmount * HumanizeTuning.ArcFalloffNorm
                                / (HumanizeTuning.ArcFalloffNorm + dNorm);
                Vector2 chordDir = (b - a) / chordLen;
                Vector2 perp = new Vector2(-chordDir.Y, chordDir.X);
                // Handedness: a real player's wrist pivots from a fixed point (below the play area), so every arc bows
                // AWAY from that pivot on a CONSISTENT side — it never flips between jumps the way an alternating or
                // outside-of-corner rule does. The side is a SMOOTH field (a dot product, not a hard branch), so it
                // eases through zero along the pivot axis instead of snapping sides. Replay-calibrated: horizontal
                // jumps bow up, vertical jumps bow slightly to one side => a pivot below + slightly off-centre.
                Vector2 mid = (a + b) * 0.5f;
                Vector2 pivot = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH * 0.5f + HumanizeTuning.HandPivotX,
                                            ParsedBeatmap.PLAYFIELD_HEIGHT * 0.5f + HumanizeTuning.HandPivotY);
                float bowSign = Vector2.Dot(perp, safeNormalize(mid - pivot)); // smooth [-1,1]; + = the side away from the pivot
                float shape = 16f * s2 * (1f - s) * (1f - s);                  // C1 bump: 0 value+slope at both ends, peak at s = 0.5
                pos += perp * (arcFrac * chordLen * (1f - gapFlow) * shape * bowSign);
            }

            // Subtle overshoot: a MEDIUM jump (replay peak ~100-160 normalised) flicks a little past, then settles back
            // exactly on it; near-zero on streams (gated by flow) and on huge jumps (those are landed precisely).
            float over = smoothstep(dNorm, HumanizeTuning.OvershootPeakNorm * 0.5f, HumanizeTuning.OvershootPeakNorm)
                         * (1f - smoothstep(dNorm, HumanizeTuning.OvershootPeakNorm, HumanizeTuning.OvershootPeakNorm * 2.5f))
                         * (1f - gapFlow);
            if (over > 0.001f && HumanizeTuning.OvershootGain > 0.001f && chordLen > 0.001f)
            {
                Vector2 travel = (b - a) / chordLen;
                // s³(1-s)²: zero VALUE and zero SLOPE at both ends (fully C1, no landing kick); peaks just past the target.
                float bump = 28.94f * s3 * (1f - s) * (1f - s);
                pos += travel * (HumanizeTuning.OvershootGain * chordLen * over * bump);
            }

            return pos;
        }

        /// <summary>Upper bound on the cursor's slider-exit speed, as a multiple of the straight-line speed to the next
        /// note, so a fast slider into a long gap can't fling the Hermite past the target. Below the cap the real exit
        /// velocity is carried through unchanged (C1 handoff, no tug).</summary>
        private const float slider_exit_speed_cap = 1.8f;

        /// <summary>An object's head position WITHOUT aim error, clamped so callers may index past the ends. Used for the
        /// tangent finite-differences so a note's random aim offset can't make a stream weave (the gap endpoints A/B
        /// still carry the aim error, so the cursor lands where the player aimed).</summary>
        private Vector2 cleanHead(IReadOnlyList<HitObjectModel> objs, int k)
        {
            var o = objs[Math.Clamp(k, 0, objs.Count - 1)];
            return autoStartPos(o) - aimError(o);
        }

        /// <summary>
        /// The note's effective centre for the spline, with a small stream zig-zag straightened out (the "path of
        /// least effort"). A binomial ¼,½,¼ smooth removes only the ALTERNATING note-to-note component, so a smoothly
        /// curved stream is preserved while a zig-zag flattens; a soft-threshold then straightens that removed part up
        /// to <see cref="HumanizeTuning.StreamStraightenThreshold"/> radii and keeps the excess (big zig-zags still get
        /// followed). Only acts inside streams (scaled by the note's flow φ); on jumps it returns the raw centre.
        /// </summary>
        private Vector2 nodeClean(IReadOnlyList<HitObjectModel> objs, int k)
        {
            Vector2 center = cleanHead(objs, k);
            float fl = flowFactor(objs, k);
            if (fl < 0.01f || HumanizeTuning.StreamStraightenAmount < 0.001f)
                return center;

            // A φ-continuity-weighted moving average over up to StreamStraightenTaps notes on each side. Each neighbour's
            // weight is the running product of the gap-shortness along the chain to it (× a Gaussian taper), so the
            // window naturally spans a whole stream and CUTS OFF at a jump (a slow gap zeroes the chain). Averaging over
            // many notes — not just the 3-tap alternating component — collapses the stream onto its smooth mean line, so
            // the cursor slides through it instead of visibly hitting every note; a gentle curve is preserved (low freq).
            int taps = Math.Max(1, (int)MathF.Round(HumanizeTuning.StreamStraightenTaps));
            Vector2 sum = center;
            float wsum = 1f;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                float chain = 1f;
                for (int i = 1; i <= taps; i++)
                {
                    int a = k + dir * (i - 1), b = k + dir * i;      // consecutive pair along the run
                    chain *= shortness(gapBetween(objs, a, b));      // continuity of the stream out to this note
                    if (chain < 0.02f)
                        break;                                        // a jump broke the chain: stop the window here
                    float taper = MathF.Exp(-(i * i) / (float)(2 * taps * taps));
                    float w = chain * taper;
                    sum += cleanHead(objs, b) * w;
                    wsum += w;
                }
            }
            Vector2 smoothed = sum / wsum;

            Vector2 removed = center - smoothed;          // what the straightening would remove; small on a smooth run
            float thr = HumanizeTuning.StreamStraightenThreshold * currentDiameter * 0.5f;
            float len = removed.Length;
            float keep = len > thr ? (len - thr) / len : 0f; // straighten up to the threshold, follow a large deliberate deviation
            Vector2 target = smoothed + removed * keep;
            return Vector2.Lerp(center, target, fl * HumanizeTuning.StreamStraightenAmount);
        }

        /// <summary>Time gap (ms) between two objects by index, clamped to the ends; <see cref="double.PositiveInfinity"/>
        /// past an end so the flow chain treats the map boundary as a jump.</summary>
        private static double gapBetween(IReadOnlyList<HitObjectModel> objs, int i, int j)
        {
            if (i < 0 || j < 0 || i >= objs.Count || j >= objs.Count)
                return double.PositiveInfinity;
            return Math.Abs(objs[j].StartTime - objs[i].StartTime);
        }

        /// <summary>Catmull-Rom tangent (osu!px/ms) at object <paramref name="k"/> — the time-correct secant through its
        /// neighbours — scaled by its flow factor φ. This is the spline's velocity at the object; sharing it across the
        /// gaps on either side is what keeps the whole path C1 (no teleports). The magnitude is capped at 1.5× the slower
        /// of the two neighbouring average speeds so a wildly non-uniform spacing (e.g. a stack right next to a jump)
        /// can't make the Hermite loop out of the short side — and because the cap is a property of the knot alone, the
        /// shared velocity (and therefore C1) is preserved.</summary>
        private Vector2 knotVel(IReadOnlyList<HitObjectModel> objs, int k)
        {
            int n = objs.Count;
            int kp = Math.Clamp(k - 1, 0, n - 1);
            int kn = Math.Clamp(k + 1, 0, n - 1);
            float span = (float)(objs[kn].StartTime - objs[kp].StartTime);
            if (span < 1f)
                return Vector2.Zero;

            Vector2 v = (nodeClean(objs, kn) - nodeClean(objs, kp)) / span * flowFactor(objs, k);

            float before = k - 1 >= 0 ? segmentSpeed(objs, k - 1, k) : float.MaxValue;
            float after = k + 1 < n ? segmentSpeed(objs, k, k + 1) : float.MaxValue;
            float cap = 1.5f * Math.Min(before, after);
            float speed = v.Length;
            if (speed > cap && speed > 1e-4f)
                v *= cap / speed;

            return v;
        }

        /// <summary>Average cursor speed (osu!px/ms) over the segment from object <paramref name="i"/> to <paramref name="j"/>.</summary>
        private float segmentSpeed(IReadOnlyList<HitObjectModel> objs, int i, int j)
        {
            float dt = (float)Math.Max(1, objs[j].StartTime - objs[i].StartTime);
            return (cleanHead(objs, j) - cleanHead(objs, i)).Length / dt;
        }

        /// <summary>
        /// φ ∈ [0,1]: how much the cursor flows THROUGH object <paramref name="k"/> (short, fast gaps on both sides →
        /// φ≈1, a stream) versus settles ON it (a long gap on either side, or the first/last object → φ≈0, a jump).
        /// The one knob that tells a stream from an isolated note, with everything else falling out of the spline.
        /// </summary>
        private static float flowFactor(IReadOnlyList<HitObjectModel> objs, int k)
        {
            int n = objs.Count;
            double before = k - 1 >= 0 ? objs[k].StartTime - objs[k - 1].StartTime : double.PositiveInfinity;
            double after = k + 1 < n ? objs[k + 1].StartTime - objs[k].StartTime : double.PositiveInfinity;
            return shortness(before) * shortness(after);
        }

        /// <summary>1 for a fast note-to-note gap (≤ FlowFastMs), ramping to 0 for a slow one (≥ FlowSlowMs).</summary>
        private static float shortness(double gapMs)
            => 1f - smoothstep((float)Math.Min(gapMs, 1e6), HumanizeTuning.FlowFastMs, HumanizeTuning.FlowSlowMs);

        private static Vector2 safeNormalize(Vector2 v)
        {
            float l = v.Length;
            return l > 0.0001f ? v / l : Vector2.Zero;
        }

        /// <summary>Smoothstep ramp from 0 (at <paramref name="edge0"/>) to 1 (at <paramref name="edge1"/>).</summary>
        private static float smoothstep(float x, float edge0, float edge1)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A faint multi-frequency wobble (osu!px) — the barely-unsteady hand. Replay analysis shows real
        /// streams shake only ~0.27px (median), so this is kept very small. ~0.13px amplitude.</summary>
        private static Vector2 humanJitter(double time)
        {
            double t = time / 1000.0;
            float x = (float)(Math.Sin(t * 9.0) * 0.085 + Math.Sin(t * 19.0) * 0.045);
            float y = (float)(Math.Cos(t * 7.5) * 0.085 + Math.Sin(t * 21.0) * 0.045);
            return new Vector2(x, y);
        }

        /// <summary>
        /// A stable per-object aim error (osu!px) for circles when humanising: the cursor lands a little off the
        /// centre instead of dead-on. Derived from the object's id so it's identical every pass. Zero otherwise.
        /// </summary>
        private Vector2 aimError(HitObjectModel o)
        {
            if (!modHumanize || o.Kind != HitObjectKind.Circle)
                return Vector2.Zero;

            uint h = (uint)o.Id * 2654435761u + 40503u;
            float ang = (h & 0xFFFF) / 65535f * (float)(Math.PI * 2);
            float mag = ((h >> 16) & 0xFFFF) / 65535f;
            // Replay analysis: real aim error is a median ~0.33 of the radius (factor 0.6 x uniform mag ≈ that median).
            float radius = currentDiameter * 0.5f * HumanizeTuning.AimErrorAmount * mag;
            return new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * radius;
        }

        /// <summary>Spin speed (rad/ms) and radius (osu!px) the Auto cursor uses on spinners.</summary>
        private const double auto_spin_speed = 0.025;
        private const float auto_spin_radius = 55f;

        /// <summary>The spinning cursor position at <paramref name="time"/> on a spinner (centre + a rotating offset).
        /// The single source of truth so a spinner's aim endpoints (start/end pos) match what the follow actually does
        /// at those instants — otherwise the gap would aim at the bare centre while the follow sits 55px out, teleporting
        /// at each spinner boundary.</summary>
        private static Vector2 spinnerPosAt(HitObjectModel o, double time)
        {
            var centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f);
            double ang = (time - o.StartTime) * auto_spin_speed;
            return centre + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * auto_spin_radius;
        }

        private Vector2 autoStartPos(HitObjectModel o)
            => o.Kind == HitObjectKind.Spinner
                ? spinnerPosAt(o, o.StartTime) // where the spin actually begins (centre + radius on +X), not the bare centre
                : new Vector2(o.X, o.Y) + DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter) + aimError(o);

        private Vector2 autoEndPos(HitObjectModel o)
        {
            Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
            switch (o.Kind)
            {
                case HitObjectKind.Slider when isKickSlider(o):
                    return new Vector2(o.X, o.Y) + stack; // kick slider treated as a circle: ends on its head

                case HitObjectKind.Slider when o.Path is { Count: > 0 } path:
                    // Even slide count ends back at the head; odd ends at the tail.
                    return (o.Slides % 2 == 0 ? path[0] : path[^1]) + stack;

                case HitObjectKind.Spinner:
                    return spinnerPosAt(o, autoEndTime(o)); // where the spin actually ends, matching the follow (no teleport)

                default:
                    return new Vector2(o.X, o.Y) + stack + aimError(o);
            }
        }

        private double autoEndTime(HitObjectModel o) => o.StartTime + Math.Max(0, o.Duration);

        /// <summary>Keeps <paramref name="want"/> within the follow circle (FollowCircleSafety × object radius) of the
        /// real ball position <paramref name="ball"/>, so laziness/anticipation can never drift far enough to drop a
        /// slider tick. Returns the point on the leash if the request is outside it, else the request unchanged.</summary>
        private Vector2 clampToFollowCircle(Vector2 want, Vector2 ball)
        {
            float maxOff = currentDiameter * 0.5f * HumanizeTuning.FollowCircleSafety;
            Vector2 d = want - ball;
            float l = d.Length;
            return l > maxOff && l > 1e-4f ? ball + d * (maxOff / l) : want;
        }

        /// <param name="o">The object being followed.</param>
        /// <param name="time">The current playback time (ms).</param>
        /// <param name="releaseTime">The time the slider is released (ms); anticipation ramps over the window before it.</param>
        /// <param name="anticipateTo">When set (humanising, leaving a slider toward a next object), the follow target
        /// drifts toward this point over the last <see cref="HumanizeTuning.SliderAnticipationMs"/> before
        /// <paramref name="releaseTime"/> — the "path of least effort" where the player already heads to the next note
        /// before the tail. The drift is leashed to the follow circle so a tick is never missed.</param>
        /// <param name="anticipateStrength">How strongly to drift toward <paramref name="anticipateTo"/> (0 = not at
        /// all). Already includes the directional gate — only leads when the next note is downstream of the exit flow.</param>
        private Vector2 autoFollowPos(HitObjectModel o, double time, Vector2? anticipateTo = null, double releaseTime = double.MaxValue, float anticipateStrength = 0f)
        {
            Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);

            switch (o.Kind)
            {
                case HitObjectKind.Slider when o.Path is { Count: > 0 }:
                {
                    double dur = Math.Max(1, o.Duration);
                    double elapsed = Math.Clamp(time - o.StartTime, 0, dur);
                    Vector2 exact = sliderBallAt(o, elapsed); // perfect on-path trace (incl. stack + repeats bounce)

                    if (!modHumanize)
                        return exact;

                    // Lazy follow as a CENTRED moving average of the ball path: the cursor smoothly cuts curvature
                    // (a sharp "^" or a repeat reversal rounds into an arc — "omitting" the exact path for comfort)
                    // while always tracking the ball's progress. Unlike a follow-circle it never stalls or lags
                    // waiting for the ball to escape; on a straight slider the centred average IS the ball (no drift).
                    double win = dur * HumanizeTuning.SliderCutWindow;
                    Vector2 avg = Vector2.Zero;
                    float wsum = 0;
                    const int taps = 6;
                    for (int k = -taps; k <= taps; k++)
                    {
                        double tt = Math.Clamp(elapsed + (double)k / taps * win, 0, dur);
                        float w = taps + 1 - Math.Abs(k); // triangular weights, peak at the centre
                        avg += sliderBallAt(o, tt) * w;
                        wsum += w;
                    }
                    avg /= wsum;

                    // Reverse (repeat) slider: the player doesn't chase the ball back and forth — they park near the
                    // middle, since the ball keeps returning within reach. Pull the lazy target toward the path midpoint.
                    if (o.Slides >= 2)
                        avg = Vector2.Lerp(avg, SliderGeometry.PointAtFraction(o.Path!, 0.5) + stack, HumanizeTuning.ReverseLaziness);

                    // Keep the very head and tail on-path (those are actually hit); cut freely through the middle and
                    // across repeats. Fades the cut over the first/last SliderEdgeFadeMs only (not at every repeat).
                    float edge = (float)Math.Clamp(Math.Min(elapsed, dur - elapsed) / Math.Max(1.0, HumanizeTuning.SliderEdgeFadeMs), 0, 1);
                    Vector2 lazy = Vector2.Lerp(exact, avg, HumanizeTuning.SliderLaziness * edge);

                    // Organic wander: a loose hand snakes a little to each side of the path instead of tracing it dead-on
                    // (so a straight slider isn't a perfect line and a curve isn't a clean averaged arc). A slow, low-freq
                    // lateral drift off the local ball tangent; faded at the head/tail (same edge) so those stay on-path.
                    if (HumanizeTuning.SliderFollowWander > 0.001f && edge > 0.001f)
                        lazy += sliderWander(o, elapsed, dur) * edge;

                    // Safety leash — but ONLY on the lazy curvature-cut, not on the exit. The leash exists so cutting a
                    // sharp corner / reverse can't drift far enough to drop a tick; that's a property of the follow, so
                    // clamp it to the real ball HERE, before anticipation. Anticipation (below) is the intentional exit
                    // toward the next note and must NOT be leashed — otherwise the cursor pins to the follow-circle edge
                    // "trying to leave but held back" until the gap yanks it free.
                    lazy = clampToFollowCircle(lazy, exact);

                    // Anticipation: approaching the release the cursor already drifts toward the next object instead of
                    // tracing the tail then jumping (least effort). Strength is gated by the caller so it only leads when
                    // the next note is downstream of the exit flow. Rides on top of the leashed follow, unclamped.
                    // The ramp is EASE-IN (u², not smoothstep): velocity grows monotonically toward release and hands
                    // off to the gap, instead of a smoothstep's mid-window peak-then-settle that reads as a lurch/sudden
                    // acceleration out of the slider. So leaving is one smooth speed-up into the gap.
                    if (anticipateTo is Vector2 nextHead && anticipateStrength > 0.001f && releaseTime < double.MaxValue)
                    {
                        // Clamp the window to the slider's OWN start: on a slider shorter than SliderAnticipationMs the
                        // window would otherwise reach before the head, making the follow start with a non-zero drift at
                        // StartTime — but the gap feeding INTO this slider aims at autoStartPos with NO drift, so the two
                        // wouldn't meet at the head → a teleport. Anchoring u=0 at StartTime keeps that boundary exact.
                        double winStart = Math.Max(o.StartTime, releaseTime - HumanizeTuning.SliderAnticipationMs);
                        double winLen = Math.Max(1.0, releaseTime - winStart);
                        float u = Math.Clamp((float)((time - winStart) / winLen), 0f, 1f);
                        lazy = Vector2.Lerp(lazy, nextHead, u * u * anticipateStrength);
                    }

                    return lazy;
                }

                case HitObjectKind.Spinner:
                    return spinnerPosAt(o, time);

                default:
                    return new Vector2(o.X, o.Y) + stack + aimError(o);
            }
        }

        /// <summary>A "kick slider" (1/4, 1/8, …) is so short the player treats it as a circle — tap the head and move
        /// on rather than trace it. True only while humanising, for sliders at/under <see cref="HumanizeTuning.KickSliderMaxMs"/>.</summary>
        private bool isKickSlider(HitObjectModel o)
            => modHumanize && o.Kind == HitObjectKind.Slider && o.Duration <= HumanizeTuning.KickSliderMaxMs;

        /// <summary>The exact on-path slider-ball position at <paramref name="elapsed"/> ms into the slider
        /// (includes the stack offset and the bounce on each repeat).</summary>
        private Vector2 sliderBallAt(HitObjectModel o, double elapsed)
        {
            var path = o.Path!;
            double dur = Math.Max(1, o.Duration);
            double spanDur = dur / Math.Max(1, o.Slides);
            double e = Math.Clamp(elapsed, 0, dur);
            int span = spanDur > 0 ? Math.Min(o.Slides - 1, (int)(e / spanDur)) : 0;
            double within = spanDur > 0 ? (e - span * spanDur) / spanDur : 0;
            double frac = span % 2 == 0 ? within : 1 - within; // bounce back on each repeat
            return SliderGeometry.PointAtFraction(path, Math.Clamp(frac, 0, 1))
                   + DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
        }

        /// <summary>
        /// A slow, low-frequency 2D drift (osu!px) so the humanised slider follow reads as a loose hand rather than a
        /// perfect trace — a straight slider stops being a dead-straight line and a curve stops being a clean averaged
        /// arc. Deliberately an ABSOLUTE-space drift (not perpendicular to the ball tangent): a tangent-relative wander
        /// flips sign at a reverse's turn-around and made the cursor jitter wildly there. This is smooth everywhere.
        /// Stable per object; the caller fades it at the head/tail and the follow-circle leash bounds it, so it can
        /// never drift far enough to drop a tick.
        /// </summary>
        private Vector2 sliderWander(HitObjectModel o, double elapsed, double dur)
        {
            double phase = (o.Id % 97) * 0.646; // a stable per-object phase so two sliders don't wander in lockstep
            double w = 2 * Math.PI * HumanizeTuning.SliderWanderHz * (elapsed / 1000.0);
            float x = (float)(Math.Sin(w + phase) * 0.7 + Math.Sin(w * 1.9 + phase * 1.3) * 0.3);
            float y = (float)(Math.Cos(w * 1.15 + phase * 0.7) * 0.7 + Math.Sin(w * 2.3 + phase) * 0.3);
            return new Vector2(x, y) * (currentDiameter * 0.5f * HumanizeTuning.SliderFollowWander);
        }

        /// <summary>Rebuilds every hit-object drawable so appearance-setting changes take effect immediately.</summary>
        private void rebuildAppearance()
        {
            if (currentHitObjects.Count == 0)
                return;

            hitObjectContainer.Clear();
            drawableMap.Clear();
            modelMap.Clear();
            SetHitObjects(currentHitObjects, currentDiameter, lastPreempt);
        }

        /// <summary>The cursor's position in osu!pixels, if it is currently over the play area.</summary>
        public bool TryGetCursorOsuPosition(out Vector2 osuPosition)
        {
            osuPosition = Vector2.Zero;

            var input = GetContainingInputManager();
            if (input == null)
                return false;

            Vector2 local = playArea.ToLocalSpace(input.CurrentState.Mouse.Position);
            if (!insidePlayfield(local))
                return false;

            osuPosition = local;
            return true;
        }

        /// <summary>
        /// Whether the object with the given id is currently on-screen: the playback time lies within its
        /// approach+active window (StartTime - preempt .. end). Time-based (not the drawable's interpolated
        /// alpha) so it stays consistent with callers that snap against the same <see cref="TimeSource"/>.
        /// </summary>
        public bool IsObjectVisible(int id)
        {
            foreach (var o in currentHitObjects)
            {
                if (o.Id == id)
                    return IsObjectVisible(o);
            }

            return false;
        }

        /// <summary>
        /// Whether the object is currently within the drawn window, computed directly from the model (no lookup).
        /// Prefer this overload in hot paths that already hold the object - the id overload scans to find it.
        /// </summary>
        public bool IsObjectVisible(HitObjectModel o)
        {
            double t = TimeSource?.Invoke() ?? 0;

            // Match what's actually drawn (and what selection treats as hittable): the approach window
            // plus the configurable fade-out tail, so a passed object stays snappable while it lingers.
            double end = o.StartTime + (o.Kind is HitObjectKind.Slider or HitObjectKind.Spinner ? o.Duration : 0);
            return t >= o.StartTime - lastPreempt && t <= end + effectiveFadeOut;
        }

        /// <summary>
        /// The best currently-visible object to pick under an osu!pixel position, or null. Picking is tiered so
        /// the obvious target wins: (1) an already-selected object under the cursor keeps priority - so you can
        /// grab and move it even when another object sits on top; (2) an object whose head/tail circle is under
        /// the cursor beats a mere slider body - so a note tucked beneath a slider body is still selectable;
        /// (3) otherwise a plain body hit (slider path / spinner).
        /// </summary>
        private DrawableHitObject? hittableAt(Vector2 osuPosition)
        {
            DrawableHitObject? selectedHit = null;
            DrawableHitObject? endpointHit = null;
            DrawableHitObject? bodyHit = null;

            for (int i = objects.Count - 1; i >= 0; i--)
            {
                var o = objects[i];
                if (!o.IsHittable || !o.BodyContains(osuPosition))
                    continue;

                if (selectedHit == null && selection.Contains(o.Id))
                    selectedHit = o;
                if (endpointHit == null && o.EndpointContains(osuPosition))
                    endpointHit = o;
                bodyHit ??= o;
            }

            return selectedHit ?? endpointHit ?? bodyHit;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // While tracing a slider, right-click finishes it (committing the placed anchors).
            if (e.Button == MouseButton.Right && buildingSlider)
            {
                finishSlider();
                return true;
            }

            // While placing a spinner, right-click commits it (ending at the current playhead time).
            if (e.Button == MouseButton.Right && buildingSpinner)
            {
                finishSpinner();
                return true;
            }

            // A placement tool is armed but nothing has been started yet (we're still just previewing) -
            // right-click toggles "new combo" for the next placed object (the phantom preview reflects it).
            if (e.Button == MouseButton.Right && (PlacementActive || SliderPlacementActive || SpinnerPlacementActive))
            {
                NewComboArmed = !NewComboArmed;
                return true;
            }

            // Right-click quick-delete (M2), like lazer: remove the hovered object or the whole selection.
            if (e.Button == MouseButton.Right)
            {
                var o = hittableAt(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
                if (o != null)
                {
                    if (selection.Contains(o.Id))
                        actions.DeleteSelected();
                    else
                        actions.DeleteObject(o.Id);
                }

                return true;
            }

            return true; // receive the left press for click/drag
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Review mode: clicking an object toggles it into the selection (so it can be attached to a note's
            // timestamp). On empty space the Note tool drops a note referencing the selection; the Select tool just
            // clears it (Line is handled via drag, later). Clicks on a note are consumed by the note itself.
            if (ReviewMode)
            {
                Vector2 reviewPos = playArea.ToLocalSpace(e.ScreenSpaceMousePosition);
                var hitObj = hittableAt(reviewPos);
                if (hitObj != null)
                {
                    selection.Toggle(hitObj.Id);
                    nodeSelection.Clear();
                }
                else if (ReviewTool == ReviewTool.Note && insidePlayfield(reviewPos))
                {
                    OnReviewCreateNote?.Invoke(reviewPos);
                }
                else
                {
                    selection.Clear();
                }
                return true;
            }

            // Slider tool: each left-click drops an anchor; a quick double-click turns the last one into a
            // sharp corner (red anchor), matching osu!lazer.
            if (SliderPlacementActive)
            {
                double now = Time.Current;
                bool doubleClick = buildingSlider && now - lastAnchorClickTime < 250;
                lastAnchorClickTime = now;

                if (doubleClick && sliderAnchors.Count > 1)
                    sliderAnchors[^1] = sliderAnchors[^1] with { Type = sliderAnchors[^1].IsSegmentStart ? (SliderPathType?)null : SliderPathType.Bezier };
                else
                    addSliderAnchor(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
                return true;
            }

            // Spinner tool: the first left-click starts the spinner at the current time; a second left-click
            // commits it (the end having followed the playhead). Right-click also commits (handled above).
            if (SpinnerPlacementActive)
            {
                if (!buildingSpinner)
                {
                    buildingSpinner = true;
                    setSpinnerGhostActive(true);
                    actions.BeginSpinnerPlacement();
                }
                else
                {
                    finishSpinner();
                }

                return true;
            }

            // With the placement tool armed, a left-click drops a circle at the cursor instead of selecting.
            if (PlacementActive)
            {
                Vector2 placePos = playArea.ToLocalSpace(e.ScreenSpaceMousePosition);
                if (insidePlayfield(placePos))
                    actions.PlaceCircle(placePos);
                return true;
            }

            // Selecting on the playfield, like lazer's composer: click an object to select it (CTRL toggles),
            // click empty space to clear. Only currently-visible objects are hittable.
            var o = hittableAt(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));

            if (o != null)
            {
                if (Shortcut.CommandPressed(e))
                {
                    selection.Toggle(o.Id);
                    nodeSelection.Clear();
                }
                else
                {
                    // Two-stage selection: the first click selects the whole object. Selecting an individual
                    // slider part (head / tail / body) is only possible once the slider is the sole selection,
                    // and is handled by the SliderControlPointVisualiser that appears in that state.
                    selection.SetSingle(o.Id);
                    nodeSelection.Clear();
                }
            }
            else if (!Shortcut.CommandPressed(e))
            {
                selection.Clear();
                nodeSelection.Clear();
            }

            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            // Review mode: the Select tool drags objects to propose a move (a suggestion, not a real move); other
            // tools don't drag (notes drag themselves).
            if (ReviewMode)
            {
                if (ReviewTool == ReviewTool.Select)
                {
                    Vector2 reviewStart = playArea.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
                    var grabbed = hittableAt(reviewStart);
                    if (grabbed != null)
                    {
                        if (!selection.Contains(grabbed.Id))
                            selection.SetSingle(grabbed.Id);

                        reviewMoving = true;
                        reviewMoveStart = reviewStart;
                        updateReviewMovePreview(Vector2.Zero);
                        return true;
                    }
                }

                return false;
            }

            // Slider tool: consume drags so they don't box-select; the anchor is added on release.
            if (SliderPlacementActive)
                return true;

            // Spinner tool: a drag shouldn't box-select; placement is click-based.
            if (SpinnerPlacementActive)
                return true;

            if (PlacementActive)
                return false;

            Vector2 start = playArea.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
            var o = hittableAt(start);

            if (o != null)
            {
                // Dragging an object moves the selection.
                if (!selection.Contains(o.Id))
                    selection.SetSingle(o.Id);

                moving = true;
                moveStart = start;
                // Pass the grabbed object's position so grid snap (if on) lands that object on the grid.
                actions.BeginMove(o.Position);
                return true;
            }

            // Dragging empty space rubber-band selects, like lazer's composer.
            boxSelecting = true;
            boxStart = start;
            dragBaseline = Shortcut.CommandPressed(e) ? new HashSet<int>(selection.Selected) : new HashSet<int>();
            boxSelected = new HashSet<int>(dragBaseline);

            overlayLayer.Add(selectionBox = new Box
            {
                Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.15f),
            });

            updateBoxSelection(start);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            Vector2 pos = playArea.ToLocalSpace(e.ScreenSpaceMousePosition);

            if (SliderPlacementActive)
                return; // preview tracks the cursor in Update(); the anchor commits on release
            if (reviewMoving)
                updateReviewMovePreview(pos - reviewMoveStart);
            else if (moving)
                actions.MoveSelectionPosition(pos - moveStart);
            else if (boxSelecting)
                updateBoxSelection(pos);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (SliderPlacementActive)
            {
                addSliderAnchor(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
            }
            else if (reviewMoving)
            {
                reviewMoving = false;
                Vector2 delta = playArea.ToLocalSpace(e.ScreenSpaceMousePosition) - reviewMoveStart;
                reviewMovePreview?.Expire();
                reviewMovePreview = null;

                // Ignore a tiny drag (a mis-click); otherwise raise the suggestion for the current selection.
                if (delta.LengthSquared >= 9f)
                    OnReviewCreateMoveSuggestion?.Invoke(delta);
            }
            else if (moving)
            {
                moving = false;
                actions.EndMove();
            }
            else if (boxSelecting)
            {
                boxSelecting = false;
                selectionBox?.Expire();
                selectionBox = null;
            }
        }

        /// <summary>Live ghost preview while dragging a Review "suggest move": hollow rings at the proposed head
        /// positions of every selected object, plus arrows from their current positions, in the review colour.</summary>
        private void updateReviewMovePreview(Vector2 delta)
        {
            reviewMovePreview?.Expire();
            reviewMovePreview = new Container { RelativeSizeAxes = Axes.Both };
            overlayLayer.Add(reviewMovePreview);

            Color4 colour = ReviewLineColour?.Invoke() ?? Color4.White;

            foreach (var o in currentHitObjects)
            {
                if (o.Kind == HitObjectKind.Spinner || !selection.Contains(o.Id))
                    continue;

                Vector2 head = startPosition(o) + DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
                Vector2 to = head + delta;
                if (delta.LengthSquared >= 1f)
                    reviewMovePreview.Add(guideLine(head, to, new Color4(colour.R, colour.G, colour.B, 0.6f), 2f));
                reviewMovePreview.Add(suggestionRing(to, colour));
            }
        }

        /// <summary>A hollow ring (object-sized) marking a suggested position, in the review colour.</summary>
        private CircularContainer suggestionRing(Vector2 pos, Color4 colour) => new CircularContainer
        {
            Position = pos,
            Origin = Anchor.Centre,
            Size = new Vector2(currentDiameter),
            Masking = true,
            BorderThickness = currentDiameter * 0.09f,
            BorderColour = colour,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(colour.R, colour.G, colour.B, 0.12f) },
        };

        /// <summary>Commits a slider anchor at the given (clamped) position, starting the trace on the first click.</summary>
        private void addSliderAnchor(Vector2 osuPosition)
        {
            // The head (the first anchor) snaps exactly like a placed circle (distance + magnetic snap); the later
            // anchors are placed freely, except grid snap - which applies to every anchor so the whole slider grids.
            bool isHead = !buildingSlider;
            Vector2 snapped = isHead && PlacementSnap != null
                ? PlacementSnap(osuPosition)
                : GridSnap?.Invoke(osuPosition) ?? osuPosition;
            Vector2 p = clampToPlayfield(snapped);

            if (!buildingSlider)
            {
                buildingSlider = true;
                sliderBuildStartTime = snappedNow();
                actions.BeginSliderPlacement();
                sliderAnchors.Clear();

                // The body uses the same SliderBodyPath + appearance settings as a committed slider, so the
                // preview is the finished slider rather than a flat trace.
                Color4 combo = previewCombo().colour;
                sliderPreview = new SliderBodyPath
                {
                    PathRadius = currentDiameter / 2f,
                    BodyOpacity = settings.ObjectBackgroundOpacity.Value,
                    BorderPortion = Math.Clamp(2f * settings.ObjectBorderThickness.Value, 0.02f, 0.9f),
                };
                // Same skin-aware body colours a committed slider gets, so the preview doesn't recolour on commit.
                sliderPreview.ApplySkinAppearance(skinManager?.Current.Value, combo);
                overlayLayer.Add(sliderPreview);
                // A ghost tail ring (matching the head ghost) sits at the slider's finalized end.
                overlayLayer.Add(sliderTailPreview = ghostRing(combo));
                // Node markers sit on top of the trace so the anchors stay visible as the slider is drawn.
                overlayLayer.Add(sliderNodes = new Container { RelativeSizeAxes = Axes.Both });
            }

            // Ignore a duplicate anchor right on top of the previous one.
            if (sliderAnchors.Count > 0 && (sliderAnchors[^1].Position - p).LengthSquared < 1f)
                return;

            sliderAnchors.Add(new SliderControlPoint(p));
        }

        /// <summary>Finishes the slider, adding the cursor as the tail, then committing it (needs a head + tail).</summary>
        private void finishSlider()
        {
            if (!buildingSlider)
                return;

            // Where the user right-clicked becomes the final anchor (unless it lands on the previous one).
            if (TryGetCursorOsuPosition(out var cursor))
            {
                Vector2 p = clampToPlayfield(cursor);
                if (sliderAnchors.Count == 0 || (sliderAnchors[^1].Position - p).LengthSquared >= 1f)
                    sliderAnchors.Add(new SliderControlPoint(p));
            }

            var anchors = new List<SliderControlPoint>(sliderAnchors);

            // Commit BEFORE tearing down the build state: PlaceSlider reads the locked-in start time, which
            // cancelSliderBuild clears via EndSliderPlacement.
            if (anchors.Count >= 2)
                actions.PlaceSlider(anchors);

            cancelSliderBuild();
        }

        /// <summary>Discards the in-progress slider trace and its preview.</summary>
        public void CancelSliderBuild() => cancelSliderBuild();

        private void cancelSliderBuild()
        {
            buildingSlider = false;
            sliderAnchors.Clear();
            sliderPreview?.Expire();
            sliderPreview = null;
            sliderTailPreview?.Expire();
            sliderTailPreview = null;
            sliderNodes?.Expire();
            sliderNodes = null;
            clearPreviewFollowPoints();
            actions.ClearSliderPreview();
            actions.EndSliderPlacement();
        }

        private static Vector2 clampToPlayfield(Vector2 p) => new Vector2(
            Math.Clamp(p.X, 0, ParsedBeatmap.PLAYFIELD_WIDTH),
            Math.Clamp(p.Y, 0, ParsedBeatmap.PLAYFIELD_HEIGHT));

        private void updateBoxSelection(Vector2 current)
        {
            boxCurrent = current;

            Vector2 min = Vector2.ComponentMin(boxStart, current);
            Vector2 max = Vector2.ComponentMax(boxStart, current);

            if (selectionBox != null)
            {
                selectionBox.Position = min;
                selectionBox.Size = max - min;
            }

            // Accumulate: add every currently-visible object whose head lies inside the box, and never remove.
            // Re-run each frame (from Update) so that while the box is held during playback, objects that fade
            // in inside it get picked up - and ones that fade out stay selected.
            foreach (var o in objects)
            {
                if (!o.IsHittable)
                    continue;

                Vector2 head = o.HeadPosition;
                if (head.X >= min.X && head.X <= max.X && head.Y >= min.Y && head.Y <= max.Y)
                    boxSelected.Add(o.Id);
            }

            selection.SetRange(boxSelected);
        }

        /// <summary>Advances to the next grid size (G key), wrapping back to the start.</summary>
        public void CycleGridSize()
        {
            gridSizeIndex = (gridSizeIndex + 1) % grid_sizes.Length;
            buildGrid();
        }

        /// <summary>Current grid spacing in osu!pixels, or 0 when the grid is off. Used by grid snapping.</summary>
        public float CurrentGridSize => grid_sizes[gridSizeIndex];

        /// <summary>Turns the grid on at the default spacing if it's currently off (so grid snap has something to snap to).</summary>
        public void EnsureGridVisible()
        {
            if (grid_sizes[gridSizeIndex] == 0)
            {
                gridSizeIndex = 2; // 16px, the default
                buildGrid();
            }
        }

        private void buildGrid()
        {
            gridContainer.Clear();

            float size = grid_sizes[gridSizeIndex];
            if (size <= 0)
                return;

            float centreX = ParsedBeatmap.PLAYFIELD_WIDTH / 2f;
            float centreY = ParsedBeatmap.PLAYFIELD_HEIGHT / 2f;

            // Vertical lines, stepping out from the playfield centre in both directions.
            for (float x = centreX; x <= ParsedBeatmap.PLAYFIELD_WIDTH; x += size)
            {
                gridContainer.Add(gridLine(x, vertical: true, centre: x == centreX, gridColour(x == centreX)));
                if (x != centreX)
                    gridContainer.Add(gridLine(centreX - (x - centreX), vertical: true, centre: false, gridColour(false)));
            }

            // Horizontal lines.
            for (float y = centreY; y <= ParsedBeatmap.PLAYFIELD_HEIGHT; y += size)
            {
                gridContainer.Add(gridLine(y, vertical: false, centre: y == centreY, gridColour(y == centreY)));
                if (y != centreY)
                    gridContainer.Add(gridLine(centreY - (y - centreY), vertical: false, centre: false, gridColour(false)));
            }
        }

        /// <summary>The grid-line colour: neutral white normally, reddish while in Review mode. Centre lines are stronger.</summary>
        private Color4 gridColour(bool centre) => reviewMode
            ? new Color4(1f, 0.36f, 0.33f, centre ? 0.36f : 0.18f)
            : new Color4(1f, 1f, 1f, centre ? 0.22f : 0.1f);

        private static Drawable gridLine(float position, bool vertical, bool centre, Color4 colour) => new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = vertical ? Anchor.TopCentre : Anchor.CentreLeft,
            RelativeSizeAxes = vertical ? Axes.Y : Axes.X,
            Width = vertical ? (centre ? 2f : 1f) : 1f,
            Height = vertical ? 1f : (centre ? 2f : 1f),
            X = vertical ? position : 0,
            Y = vertical ? 0 : position,
            Colour = colour,
        };

        /// <summary>Arms or disarms the circle-placement tool, clearing the selection when arming.</summary>
        public void SetPlacementActive(bool active)
        {
            PlacementActive = active;
            NewComboArmed = false;

            if (active)
            {
                SliderPlacementActive = false;
                SetSpinnerPlacementActive(false);
                cancelSliderBuild();
                selection.Clear();
            }
            else
            {
                placementPreview.Alpha = 0;
            }
        }

        /// <summary>Arms or disarms the spinner-placement tool, clearing the selection when arming.</summary>
        public void SetSpinnerPlacementActive(bool active)
        {
            if (SpinnerPlacementActive == active)
                return;

            SpinnerPlacementActive = active;
            NewComboArmed = false;
            cancelSpinnerBuild();

            if (active)
            {
                PlacementActive = false;
                SliderPlacementActive = false;
                cancelSliderBuild();
                selection.Clear();
                setSpinnerGhostActive(false); // visible-but-dim ghost shows the spinner area until placement starts
                ensureSpinnerGhost();
            }
            else
            {
                placementPreview.Alpha = 0;
                spinnerGhost?.Expire();
                spinnerGhost = null;
            }
        }

        /// <summary>Discards the in-progress spinner placement.</summary>
        public void CancelSpinnerBuild() => cancelSpinnerBuild();

        private void cancelSpinnerBuild()
        {
            if (!buildingSpinner)
                return;

            buildingSpinner = false;
            actions.CancelSpinnerPlacement();
            setSpinnerGhostActive(false);
        }

        private void finishSpinner()
        {
            if (!buildingSpinner)
                return;

            buildingSpinner = false;
            actions.FinishSpinnerPlacement();
            setSpinnerGhostActive(false); // stays armed (dim ghost) for the next spinner
        }

        /// <summary>Creates the centred spinner ghost ring if the tool is armed and it isn't already shown.</summary>
        private void ensureSpinnerGhost()
        {
            if (spinnerGhost != null)
                return;

            const float radius = 130f; // matches DrawableHitObject's spinner disc
            overlayLayer.Add(spinnerGhost = new CircularContainer
            {
                Position = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f),
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Masking = true,
                BorderThickness = 4,
                BorderColour = OsuColour.Pink,
                Alpha = 0.4f,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
            });
        }

        private void setSpinnerGhostActive(bool building)
        {
            ensureSpinnerGhost();
            spinnerGhost?.FadeTo(building ? 0.85f : 0.4f, 150, Easing.OutQuint);
        }

        /// <summary>Arms or disarms the slider-placement tool, clearing the selection when arming.</summary>
        public void SetSliderPlacementActive(bool active)
        {
            SliderPlacementActive = active;
            NewComboArmed = false;
            cancelSliderBuild();

            if (active)
            {
                PlacementActive = false;
                SetSpinnerPlacementActive(false);
                selection.Clear();
            }
            else
            {
                placementPreview.Alpha = 0;
            }
        }

        private static bool insidePlayfield(Vector2 osuPosition) =>
            osuPosition.X >= 0 && osuPosition.Y >= 0
            && osuPosition.X <= ParsedBeatmap.PLAYFIELD_WIDTH && osuPosition.Y <= ParsedBeatmap.PLAYFIELD_HEIGHT;

        /// <summary>
        /// Syncs the displayed hit objects to <paramref name="hitObjects"/>, creating/removing/replacing only
        /// the ones that actually changed (by id + value), so a single edit doesn't rebuild the whole map.
        /// A circle-size (diameter) or AR (preempt) change rebuilds everything, since those affect all objects.
        /// </summary>
        public void SetHitObjects(IReadOnlyList<HitObjectModel> hitObjects, float circleDiameter, double preempt)
        {
            currentHitObjects = hitObjects;
            currentDiameter = circleDiameter;

            bool fullRebuild = pendingFullRebuild || circleDiameter != lastDiameter || preempt != lastPreempt;
            pendingFullRebuild = false;
            lastDiameter = circleDiameter;
            lastPreempt = preempt;

            if (fullRebuild)
            {
                hitObjectContainer.Clear();
                drawableMap.Clear();
                modelMap.Clear();
            }

            var present = new HashSet<int>();

            foreach (var o in hitObjects)
            {
                present.Add(o.Id);

                // Unchanged object: keep its existing drawable (record-struct value equality, paths by ref).
                if (modelMap.TryGetValue(o.Id, out var old) && old.Equals(o))
                    continue;

                if (drawableMap.TryGetValue(o.Id, out var existing))
                    hitObjectContainer.Remove(existing);

                double tick = o.Kind == HitObjectKind.Slider ? SliderTickDistance?.Invoke(o) ?? 0 : 0;
                Color4? author = AuthorColourProvider?.Invoke(o);
                var drawable = new DrawableHitObject(o, circleDiameter, preempt, effectiveFadeOut, tick, modHidden, modHardRock, author);
                drawableMap[o.Id] = drawable;
                modelMap[o.Id] = o;
                hitObjectContainer.Add(drawable);
            }

            // Remove drawables whose objects are gone.
            foreach (int id in drawableMap.Keys.Where(id => !present.Contains(id)).ToList())
            {
                hitObjectContainer.Remove(drawableMap[id]);
                drawableMap.Remove(id);
                modelMap.Remove(id);
            }

            // Rebuild the flat list used for hit-testing/selection in time order.
            objects.Clear();
            foreach (var o in hitObjects)
            {
                if (drawableMap.TryGetValue(o.Id, out var d))
                    objects.Add(d);
            }

            buildFollowPoints(hitObjects);
            if (ShowAngles)
                buildAngles(hitObjects);
            updateSelection();
        }

        /// <summary>Sets the clock the hit objects animate against (the editor's interpolated audio clock).</summary>
        public void SetClock(IFrameBasedClock clock)
        {
            hitObjectContainer.Clock = clock;
            hitObjectContainer.ProcessCustomClock = false;
            followPointContainer.Clock = clock;
            followPointContainer.ProcessCustomClock = false;
            angleContainer.Clock = clock;
            angleContainer.ProcessCustomClock = false;
        }

        /// <summary>
        /// Live preview of a position move: translates the selected objects' drawables and their selection
        /// outlines by an osu!pixel offset without rebuilding. Committed (and reset) on drag release.
        /// </summary>
        public void PreviewPositionOffset(Vector2 osuOffset, IReadOnlyDictionary<int, int>? stackHeights = null)
        {
            foreach (var o in objects)
            {
                // Use the live-recomputed stack offset when supplied so the selection visibly stacks mid-drag.
                Vector2 stack = stackHeights != null && stackHeights.TryGetValue(o.Id, out int sh)
                    ? DrawableHitObject.StackOffsetFor(sh, currentDiameter)
                    : o.StackOffset;

                o.Position = selection.Contains(o.Id) ? stack + osuOffset : stack;
            }

            selectionLayer.Position = osuOffset;

            // Keep the slider's control-point handles glued to the body as it is dragged (on top of the
            // slider's stack offset, which is the visualiser's resting position).
            if (controlPoints != null)
                controlPoints.Position = controlPointsBase + osuOffset;

            // Keep the follow-point chain glued to the moving objects (only the touched connections update).
            previewFollowPoints(osuOffset);
        }

        /// <summary>
        /// Flashes the selection outlines red to signal a transform was refused - e.g. a rotation that would have
        /// pushed a slider out of bounds (which previously got "fixed" by clamping/resizing it, compounding into
        /// runaway growth). A quick visual "no" instead of silently distorting the slider.
        /// </summary>
        public void FlashSelectionBlocked() =>
            selectionLayer.FlashColour(new Color4(1f, 0.15f, 0.15f, 1f), 450, Easing.OutQuint);

        /// <summary>
        /// Rebuilds the always-visible yellow selection outlines so selected objects stay outlined even
        /// once they fade out of the approach window - matching osu!lazer's editor.
        /// </summary>
        private void updateSelection()
        {
            if (selection == null)
                return;

            selectionLayer.Clear();
            selectionLayer.Position = Vector2.Zero;

            foreach (var o in currentHitObjects)
            {
                if (!selection.Contains(o.Id))
                    continue;

                Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);

                // Body part selected: highlight the whole path beneath the endpoint rings.
                if (o.Kind == HitObjectKind.Slider && nodeSelection.IsBodySelected(o.Id) && o.Path is { Count: > 1 })
                    selectionLayer.Add(bodyHighlight(o.Path, stack));

                bool headNode = nodeSelection.Selected is { } hn && hn.ObjectId == o.Id && hn.NodeIndex == 0;
                selectionLayer.Add(selectionRing(startPosition(o) + stack, headNode ? EditorTheme.Colours.Error : OsuColour.Yellow));

                if (o.Kind == HitObjectKind.Slider)
                {
                    bool tailNode = nodeSelection.Selected is { } tn && tn.ObjectId == o.Id && tn.NodeIndex == Math.Max(1, o.Slides);
                    selectionLayer.Add(selectionRing(endPosition(o) + stack, tailNode ? EditorTheme.Colours.Error : OsuColour.Yellow));
                }
            }

            updateControlPointEditor();
        }

        /// <summary>
        /// Shows a draggable control-point editor when exactly one slider is selected (and no placement tool
        /// is armed), so its anchors can be moved or toggled red - mirroring osu!lazer's slider selection.
        /// </summary>
        private void updateControlPointEditor()
        {
            controlPoints?.Expire();
            controlPoints = null;

            // No slider-anchor editing while reviewing (selection there is only for attaching a note timestamp).
            if (PlacementActive || SliderPlacementActive || ReviewMode || selection.Selected.Count != 1)
                return;

            int id = selection.Selected.First();
            foreach (var o in currentHitObjects)
            {
                if (o.Id == id && o.Kind == HitObjectKind.Slider && o.ControlPoints is { Count: >= 2 })
                {
                    int sliderId = o.Id;
                    controlPointsBase = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
                    overlayLayer.Add(controlPoints = new SliderControlPointVisualiser(o, currentDiameter, actions)
                    {
                        // Shift the whole editor by the slider's stack offset so its handles/polygon line up with
                        // the stacked body (the control points themselves stay in unstacked coordinates, which is
                        // what gets edited - ToLocalSpace then maps clicks/drags back through this offset).
                        Position = controlPointsBase,
                        // Two-stage part selection: with the slider already the sole selection, clicking a
                        // head/tail node or the body selects just that part.
                        PartNodeClicked = node => nodeSelection.Select(sliderId, node),
                        BodyClicked = () => nodeSelection.SelectBody(sliderId),
                        // Editing a control-point handle snaps it to the grid too, when grid snap is on.
                        SnapToGrid = GridSnap,
                    });
                    return;
                }
            }
        }

        /// <summary>A translucent yellow overlay tracing the slider's body, shown when its body part is selected.</summary>
        private Drawable bodyHighlight(IReadOnlyList<Vector2> path, Vector2 stack)
        {
            var highlight = new SmoothPath
            {
                PathRadius = currentDiameter / 2f,
                Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.3f),
            };
            highlight.Vertices = path;
            highlight.Position = stack - highlight.PositionInBoundingBox(Vector2.Zero);
            return highlight;
        }

        private Drawable selectionRing(Vector2 position, Color4 colour) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(currentDiameter * 1.15f),
            Masking = true,
            BorderThickness = Math.Max(2.5f, currentDiameter * 0.06f),
            BorderColour = colour,
            // A very soft glow so the selection reads as "lit" without a hard halo.
            EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
            {
                Type = osu.Framework.Graphics.Effects.EdgeEffectType.Glow,
                Colour = new Color4(colour.R, colour.G, colour.B, 0.22f),
                Radius = Math.Max(5f, currentDiameter * 0.13f),
            },
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
        };

        /// <summary>
        /// Draws Review-mode highlight outlines (in each note author's colour) around the hit objects a shown note
        /// refers to - the same ring/body treatment as selection, so it reads as "these objects are what I mean".
        /// Called every frame: the object/colour set only rebuilds when it changes, while each group's alpha is set
        /// to the note's current fade so the highlight eases in/out together with the note instead of snapping.
        /// </summary>
        public void SetAnnotationHighlights(Dictionary<int, (Colour4 colour, float alpha)> highlights)
        {
            // Signature of which objects + colours are highlighted (order-independent): rebuild only when it changes.
            string sig = "";
            if (highlights.Count > 0)
            {
                var parts = new System.Collections.Generic.List<string>(highlights.Count);
                foreach (var (id, hl) in highlights)
                    parts.Add($"{id}:{hl.colour.ToHex()}");
                parts.Sort();
                sig = string.Join("|", parts);
            }

            if (sig != annotationHighlightSig)
            {
                annotationHighlightSig = sig;
                annotationHighlightLayer.Clear();
                annotationHighlightGroups.Clear();

                foreach (var o in currentHitObjects)
                {
                    if (!highlights.TryGetValue(o.Id, out var hl))
                        continue;

                    Color4 c = new Color4(hl.colour.R, hl.colour.G, hl.colour.B, 1f);
                    Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
                    var group = new Container { RelativeSizeAxes = Axes.Both };

                    if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 1 })
                    {
                        var body = new SmoothPath
                        {
                            PathRadius = currentDiameter / 2f,
                            Colour = new Color4(c.R, c.G, c.B, 0.25f),
                        };
                        body.Vertices = o.Path;
                        body.Position = stack - body.PositionInBoundingBox(Vector2.Zero);
                        group.Add(body);
                    }

                    group.Add(selectionRing(startPosition(o) + stack, c));
                    if (o.Kind == HitObjectKind.Slider)
                        group.Add(selectionRing(endPosition(o) + stack, c));

                    annotationHighlightGroups[o.Id] = group;
                    annotationHighlightLayer.Add(group);
                }
            }

            // Drive each group's alpha from its note's current fade (no rebuild) so they fade in sync.
            foreach (var (id, group) in annotationHighlightGroups)
                group.Alpha = highlights.TryGetValue(id, out var hl) ? hl.alpha : 0f;
        }

        /// <summary>Draws a soft red selection ring around each object changed by a suggestion (the "Suggested changes" view).</summary>
        public void SetModifiedHighlight(IReadOnlyCollection<int> ids)
        {
            modifiedHighlightLayer.Clear();
            if (ids.Count == 0)
                return;

            var red = new Color4(1f, 0.36f, 0.36f, 1f);
            foreach (var o in currentHitObjects)
            {
                if (!ids.Contains(o.Id) || o.Kind == HitObjectKind.Spinner)
                    continue;

                Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
                modifiedHighlightLayer.Add(selectionRing(startPosition(o) + stack, red));
                if (o.Kind == HitObjectKind.Slider)
                    modifiedHighlightLayer.Add(selectionRing(endPosition(o) + stack, red));
            }
        }

        /// <summary>
        /// Connects consecutive objects within the same combo (osu! draws no follow point across a new
        /// combo, nor to/from spinners) with a <see cref="DrawableFollowPoints"/> chain.
        /// </summary>
        private void buildFollowPoints(IReadOnlyList<HitObjectModel> hitObjects)
        {
            // Each connection's geometry depends only on its two neighbours, so reuse the ones whose endpoints
            // and times are unchanged and recreate only those that actually moved. A full rebuild every sync
            // would recreate every connection's drawables on each frame of a rotate/scale/move drag, which lags.
            var existing = new Dictionary<(int, int), FollowPointConnection>();
            foreach (var c in followPoints)
                existing[(c.FromId, c.ToId)] = c;

            var rebuilt = new List<FollowPointConnection>();
            var kept = new HashSet<FollowPointConnection>();

            for (int i = 0; i < hitObjects.Count - 1; i++)
            {
                var current = hitObjects[i];
                var next = hitObjects[i + 1];

                // New combo (ComboNumber resets to 1) breaks the chain; spinners never connect.
                if (next.ComboNumber == 1 || current.Kind == HitObjectKind.Spinner || next.Kind == HitObjectKind.Spinner)
                    continue;

                Vector2 start = endPosition(current);
                Vector2 end = startPosition(next);
                double startTime = endTime(current);
                double endTimeMs = next.StartTime;

                // Reuse the existing connection if everything that determines its geometry is unchanged.
                if (existing.TryGetValue((current.Id, next.Id), out var reuse)
                    && reuse.BaseStart == start && reuse.BaseEnd == end
                    && reuse.StartTime == startTime && reuse.EndTime == endTimeMs)
                {
                    kept.Add(reuse);
                    rebuilt.Add(reuse);
                    continue;
                }

                var connection = new FollowPointConnection
                {
                    FromId = current.Id,
                    ToId = next.Id,
                    BaseStart = start,
                    BaseEnd = end,
                    StartTime = startTime,
                    EndTime = endTimeMs,
                };
                connection.Drawable = new DrawableFollowPoints(start, end, startTime, endTimeMs);
                followPointContainer.Add(connection.Drawable);
                rebuilt.Add(connection);
            }

            // Drop connections that no longer exist (or were replaced).
            foreach (var c in followPoints)
                if (!kept.Contains(c))
                    followPointContainer.Remove(c.Drawable);

            followPoints.Clear();
            followPoints.AddRange(rebuilt);
        }

        /// <summary>
        /// Live preview of a position drag for the follow points: recreates only the connections that touch a
        /// selected (dragged) object, offsetting that endpoint by <paramref name="osuOffset"/>. This keeps the
        /// chain glued to the moving objects without rebuilding the whole map's connections each frame (which lags).
        /// A committed move (or drag cancel) calls <see cref="buildFollowPoints"/> again to restore the full set.
        /// </summary>
        private void previewFollowPoints(Vector2 osuOffset)
        {
            foreach (var c in followPoints)
            {
                bool fromSelected = selection.Contains(c.FromId);
                bool toSelected = selection.Contains(c.ToId);
                if (!fromSelected && !toSelected)
                    continue;

                Vector2 start = c.BaseStart + (fromSelected ? osuOffset : Vector2.Zero);
                Vector2 end = c.BaseEnd + (toSelected ? osuOffset : Vector2.Zero);

                followPointContainer.Remove(c.Drawable);
                c.Drawable = new DrawableFollowPoints(start, end, c.StartTime, c.EndTime);
                followPointContainer.Add(c.Drawable);
            }
        }

        // Live follow-point connections from/to the object currently being placed (the ghost preview). Rebuilt
        // each frame in updatePlacementPreview, cleared when no placement tool is active. Lets the mapper see the
        // flow into (and out of) the pending object in real time, exactly like osu!lazer (which inserts the
        // pending object into the beatmap during placement, so its follow points update naturally).
        private readonly List<Drawable> previewFollowDrawables = new List<Drawable>();

        private void clearPreviewFollowPoints()
        {
            if (previewFollowDrawables.Count == 0)
                return;

            foreach (var d in previewFollowDrawables)
                followPointContainer.Remove(d);
            previewFollowDrawables.Clear();
        }

        /// <summary>Whether an object's raw line carries the new-combo type bit (bit 2), i.e. it starts its own combo.</summary>
        private static bool startsNewCombo(HitObjectModel o)
        {
            string[] parts = o.RawLine.Split(',');
            return parts.Length > 3 && int.TryParse(parts[3], out int type) && (type & 4) != 0;
        }

        /// <summary>
        /// Rebuilds the follow points touching the pending placement object: one from the previous in-combo
        /// object into the pending start, and one from the pending end out to the next in-combo object. Spinners
        /// and new-combo boundaries break the chain (as in <see cref="buildFollowPoints"/>).
        /// </summary>
        private void updatePreviewFollowPoints(Vector2 startPos, Vector2 endPos, double startTime, double endTimeMs, bool isNewCombo)
        {
            clearPreviewFollowPoints();

            // Immediate neighbours by time (currentHitObjects is time-sorted). Objects sharing the exact start
            // time are ignored - placing would replace them (removeObjectsAt).
            HitObjectModel? prev = null, next = null;
            foreach (var o in currentHitObjects)
            {
                if (o.StartTime < startTime) prev = o;
                else if (o.StartTime > startTime && next == null) next = o;
            }

            // Incoming: previous -> pending (unless the pending object starts a new combo, or the previous is a spinner).
            if (!isNewCombo && prev is HitObjectModel p && p.Kind != HitObjectKind.Spinner)
                addPreviewFollow(endPosition(p), startPos, endTime(p), startTime);

            // Outgoing: pending -> next (unless the next object starts its own new combo, or is a spinner).
            if (next is HitObjectModel n && n.Kind != HitObjectKind.Spinner && !startsNewCombo(n))
                addPreviewFollow(endPos, startPosition(n), endTimeMs, n.StartTime);
        }

        private void addPreviewFollow(Vector2 start, Vector2 end, double startTime, double endTimeMs)
        {
            var d = new DrawableFollowPoints(start, end, startTime, endTimeMs);
            followPointContainer.Add(d);
            previewFollowDrawables.Add(d);
        }

        /// <summary>When true, the Review-mode opening-angle indicators are drawn over the objects.</summary>
        public bool ShowAngles { get; private set; }

        // Angles across a gap larger than this (ms) are skipped - a break/section jump has no meaningful angle.
        private const double angle_max_gap_ms = 2000;

        /// <summary>Toggles the angle indicators, (re)building them for the current objects or clearing them.</summary>
        public void SetShowAngles(bool on)
        {
            if (ShowAngles == on)
                return;

            ShowAngles = on;
            if (on)
                buildAngles(currentHitObjects);
            else
                angleContainer.Clear();
        }

        /// <summary>
        /// Builds an angle indicator on each interior object: the opening angle between the path coming in from the
        /// previous object (its exit → this head) and going out to the next (this exit → the next head), matching
        /// how follow points treat slider heads/tails. Skips spinners and cross-break gaps.
        /// </summary>
        private void buildAngles(IReadOnlyList<HitObjectModel> hitObjects)
        {
            angleContainer.Clear();

            for (int i = 1; i < hitObjects.Count - 1; i++)
            {
                var prev = hitObjects[i - 1];
                var cur = hitObjects[i];
                var next = hitObjects[i + 1];

                if (prev.Kind == HitObjectKind.Spinner || cur.Kind == HitObjectKind.Spinner || next.Kind == HitObjectKind.Spinner)
                    continue;

                if (cur.StartTime - endTime(prev) > angle_max_gap_ms || next.StartTime - endTime(cur) > angle_max_gap_ms)
                    continue;

                Vector2 vertex = startPosition(cur);
                Vector2 inbound = endPosition(prev) - vertex;               // back toward where the cursor came from
                Vector2 outbound = startPosition(next) - endPosition(cur);  // onward toward the next object

                if (inbound.LengthSquared < 1f || outbound.LengthSquared < 1f)
                    continue; // stacked / coincident: no meaningful angle

                Vector2 dirIn = Vector2.Normalize(inbound);
                Vector2 dirOut = Vector2.Normalize(outbound);

                float dot = Math.Clamp(Vector2.Dot(dirIn, dirOut), -1f, 1f);
                float angleDeg = MathHelper.RadiansToDegrees(MathF.Acos(dot));

                double lifeStart = cur.StartTime - lastPreempt;
                double lifeEnd = endTime(cur) + effectiveFadeOut;
                angleContainer.Add(new DrawableAngle(vertex, dirIn, dirOut, angleDeg, lifeStart, lifeEnd));
            }
        }

        private static Vector2 startPosition(HitObjectModel o) =>
            o.Path is { Count: > 0 } path ? path[0] : new Vector2(o.X, o.Y);

        private static Vector2 endPosition(HitObjectModel o)
        {
            // A slider ends at its tail on odd span counts, back at its head on even counts.
            if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } path)
                return o.Slides % 2 == 1 ? path[^1] : path[0];

            return new Vector2(o.X, o.Y);
        }

        private static double endTime(HitObjectModel o) =>
            o.StartTime + (o.Kind == HitObjectKind.Slider ? o.Duration : 0);

        protected override void Update()
        {
            base.Update();

            // Scale the play area exactly like osu!lazer's editor: it fits the 512x384 (4:3) playfield into
            // the full available area (DrawableOsuEditorRuleset overrides the adjustment container to
            // Size = Vector2.One, dropping gameplay's 0.8 shrink). The smaller of width/height binds, keeping
            // the 4:3 aspect; this is a fixed scale independent of circle size.
            // Full fit (PlayfieldFit.EDITOR_FIT): the field's border reaches the top/bottom timeline bars.
            // In presentation mode (video export) we DON'T want the editor's near-full fit: reinstate gameplay's
            // 0.8 shrink so objects and their approach circles near the playfield edges aren't clipped by the frame.
            float fitFactor = presentationMode ? presentation_scale : PlayfieldFit.EDITOR_FIT;
            float scale = PlayfieldFit.Scale(DrawWidth, DrawHeight,
                ParsedBeatmap.PLAYFIELD_WIDTH, ParsedBeatmap.PLAYFIELD_HEIGHT, fitFactor);

            // HardRock flips the play area vertically about its centre (y' = 384 - y), exactly like osu!'s HR.
            // Flipping the whole container keeps input (ToLocalSpace), overlays and selection consistent for
            // free; the upside-down combo numbers are counter-flipped inside the drawables that draw text.
            if (scale > 0)
                playArea.Scale = new Vector2(scale, modHardRock ? -scale : scale);

            // Hit objects + follow points animate themselves via scheduled transforms against the audio clock
            // (set in SetClock); the lifetime container only updates/draws the ones currently on screen.
            updatePlacementPreview();
            updateTransformBox();
            updateKeyOverlay();

            // While a rubber-band box is held, keep picking up objects that fade in inside it during playback,
            // even when the cursor is stationary (so the selection grows as the song plays).
            if (boxSelecting)
                updateBoxSelection(boxCurrent);
        }

        /// <summary>
        /// Shows the rotate/scale/flip selection box while Shift is held with a movable selection (and no
        /// placement tool armed), keeping it sized to the selection's osu!pixel bounds.
        /// </summary>
        private void updateTransformBox()
        {
            var input = GetContainingInputManager();
            bool shift = input?.CurrentState.Keyboard.ShiftPressed ?? false;
            bool eligible = transforming
                            || (shift && !PlacementActive && !SliderPlacementActive && !buildingSlider && actions.SelectionBounds() != null);

            if (eligible && transformBox == null)
            {
                transformBox = new SelectionBox
                {
                    ScreenToOsu = playArea.ToLocalSpace,
                    TransformBegin = () => { transforming = true; actions.BeginSelectionTransform(); },
                    TransformEnd = () => { transforming = false; actions.EndSelectionTransform(); },
                    Rotate = deg => actions.RotateSelection(deg),
                    Resize = (d, a) => actions.ScaleSelection(d, a),
                    Flip = h => actions.FlipSelection(h),
                };
                overlayLayer.Add(transformBox);
            }
            else if (!eligible && transformBox != null)
            {
                transformBox.Expire();
                transformBox = null;
            }

            if (transformBox != null && actions.SelectionBounds() is RectangleF b)
            {
                // Inflate by the circle radius so the box surrounds the visible objects, not just their centres.
                float r = currentDiameter / 2f;
                transformBox.Position = new Vector2(b.X - r, b.Y - r);
                transformBox.Size = new Vector2(b.Width + 2 * r, b.Height + 2 * r);
            }
        }

        /// <summary>A circle preview shaped like a real hit circle (combo colour, white rim, combo number).</summary>
        private Drawable buildPlacementPreview()
        {
            return placementPreview = new CircularContainer
            {
                Origin = Anchor.Centre,
                Masking = true,
                BorderThickness = currentDiameter * 0.08f,
                BorderColour = Color4.White,
                Alpha = 0,
                Children = new Drawable[]
                {
                    placementFill = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Pink, Alpha = 0.9f },
                    placementNumber = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Color4.White,
                        Text = "1",
                    },
                },
            };
        }

        /// <summary>Tracks the ghost circle (and slider trace) to the cursor while a placement tool is armed.</summary>
        private void updatePlacementPreview()
        {
            if ((!PlacementActive && !SliderPlacementActive) || !TryGetCursorOsuPosition(out var pos))
            {
                placementPreview.Alpha = 0;
                if (!SliderPlacementActive)
                {
                    sliderPreview?.Hide();
                    if (sliderTailPreview != null) sliderTailPreview.Alpha = 0;
                }
                clearPreviewFollowPoints();
                spacingGuideLayer.Clear();
                return;
            }

            var (colour, number) = previewCombo();

            // Ghost head circle: at the slider's (already-snapped) head while tracing, otherwise at the snapped
            // cursor. The slider head snaps exactly like a placed circle - both the circle and slider tools do.
            Vector2 ghost = buildingSlider && sliderAnchors.Count > 0
                ? sliderAnchors[0].Position
                : (PlacementActive || SliderPlacementActive) && PlacementSnap != null ? PlacementSnap(pos) : pos;

            placementPreview.Size = new Vector2(currentDiameter);
            placementPreview.BorderThickness = currentDiameter * 0.08f;
            placementPreview.Position = ghost;
            placementPreview.Alpha = 0.85f;
            placementFill.Colour = colour;
            placementNumber.Text = number.ToString();
            placementNumber.Font = FontUsage.Default.With(size: currentDiameter * 0.5f, weight: "Bold");
            // Counter-flip the ghost's number so it stays upright while HardRock flips the play area.
            placementNumber.Scale = new Vector2(1, modHardRock ? -1 : 1);

            // Live follow points for the pending object. While a slider body is being traced this is driven by
            // updateSliderTrace (which knows the tail position/time); otherwise the pending object is a single
            // point (a circle, or a slider head not yet committed), so it connects like a circle.
            if (!buildingSlider)
            {
                double startTime = snappedNow();
                updatePreviewFollowPoints(ghost, ghost, startTime, startTime, number == 1);
                updateSpacingGuide(pos);
            }
            else
            {
                spacingGuideLayer.Clear();
            }

            if (SliderPlacementActive)
                updateSliderTrace(pos);
        }

        /// <summary>
        /// Rebuilds the spacing-guide overlay for the current cursor: a solid line + gap label from each nearby
        /// anchor to where the object would land, with the snapped line highlighted. Cleared when the guide is
        /// inactive (Shift not held / nothing nearby).
        /// </summary>
        private void updateSpacingGuide(Vector2 cursor)
        {
            spacingGuideLayer.Clear();

            var guide = SpacingGuideResolver?.Invoke(cursor) ?? SpacingGuide.Inactive;
            if (!guide.Active)
                return;

            // Blanket wrap rings (behind the lines): the live suggestion near the cursor is bright pink; existing
            // near-perfect blankets are faint.
            foreach (var ring in guide.Rings)
            {
                spacingGuideLayer.Add(new CircularContainer
                {
                    Position = ring.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(ring.Radius * 2),
                    Masking = true,
                    BorderThickness = ring.Active ? 3f : 2f,
                    BorderColour = EditorTheme.Colours.Accent.Opacity(ring.Active ? 0.85f : 0.35f),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                });
            }

            foreach (var seg in guide.Segments)
            {
                if (Vector2.DistanceSquared(seg.From, seg.To) < 1f)
                    continue; // skip degenerate (zero-length) lines

                // Reference lines (existing spacings) are a calm blue; the placement line is brighter, turning
                // yellow when it snaps onto a matching gap.
                Color4 colour = !seg.Placement ? EditorTheme.Colours.Bookmark
                    : seg.Highlighted ? EditorTheme.Colours.Selection
                    : EditorTheme.Colours.Text;

                float alpha = seg.Placement ? 0.95f : 0.55f;
                float thickness = seg.Placement ? (seg.Highlighted ? 3f : 2f) : 2f;

                spacingGuideLayer.Add(guideLine(seg.From, seg.To, colour.Opacity(alpha), thickness));

                spacingGuideLayer.Add(new SpriteText
                {
                    Position = (seg.From + seg.To) / 2f,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Text = $"{seg.Gap:0}",
                    Colour = colour,
                    // Counter-flip so the label reads upright while HardRock flips the play area.
                    Scale = new Vector2(1, modHardRock ? -1 : 1),
                    Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                });
            }
        }

        /// <summary>A solid straight line (osu!pixel space) between two points, as a thin rotated box.</summary>
        private static Drawable guideLine(Vector2 from, Vector2 to, Color4 colour, float thickness)
        {
            Vector2 d = to - from;
            float length = d.Length;
            float angle = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X));

            return new Box
            {
                Position = from,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(length, thickness),
                Rotation = angle,
                Colour = colour,
            };
        }

        /// <summary>Redraws the live slider preview (the finished body + tail) through the committed anchors plus the current cursor.</summary>
        private void updateSliderTrace(Vector2 cursor)
        {
            if (sliderPreview == null || !buildingSlider)
                return;

            // Grid-snap the moving tail so the preview shows where the next anchor will actually land.
            cursor = GridSnap?.Invoke(cursor) ?? cursor;

            var pts = new List<SliderControlPoint>(sliderAnchors) { new SliderControlPoint(clampToPlayfield(cursor)) };
            updateSliderNodes(pts);

            // Render exactly the slider that PlaceSlider will commit: same type-inference + tick snap, so the
            // preview shows the finalized (trimmed) body rather than the raw drawn polyline.
            var path = actions.PlacementSliderPath(pts);

            if (path.Count < 2)
            {
                sliderPreview.Hide();
                if (sliderTailPreview != null) sliderTailPreview.Alpha = 0;
                clearPreviewFollowPoints();
                actions.ClearSliderPreview();
                return;
            }

            sliderPreview.Show();
            sliderPreview.Vertices = path;
            sliderPreview.Position = -sliderPreview.PositionInBoundingBox(Vector2.Zero);

            if (sliderTailPreview != null)
            {
                sliderTailPreview.Alpha = 0.85f;
                sliderTailPreview.Position = path[^1];
            }

            // Live follow points: previous object -> slider head, and slider tail -> next object. The start time
            // is the locked-in build time (not the live playhead), so scrubbing doesn't shift them.
            double startTime = sliderBuildStartTime;
            double duration = actions.PlacementSliderDuration(pts);
            updatePreviewFollowPoints(sliderAnchors[0].Position, path[^1], startTime, startTime + duration, previewCombo().number == 1);

            // Show, in real time, how long the slider will occupy on the top timeline.
            actions.PreviewSliderPlacement(pts);
        }

        /// <summary>A translucent combo-coloured ring matching the head placement ghost, used for the slider tail preview.</summary>
        private CircularContainer ghostRing(Color4 colour) => new CircularContainer
        {
            Origin = Anchor.Centre,
            Size = new Vector2(currentDiameter),
            Masking = true,
            BorderThickness = currentDiameter * 0.08f,
            BorderColour = Color4.White,
            Alpha = 0,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
                Alpha = 0.35f,
            },
        };

        /// <summary>Redraws the control polygon (connection lines) plus a small handle on each committed anchor (and the cursor) of the slider being drawn.</summary>
        private void updateSliderNodes(IReadOnlyList<SliderControlPoint> pts)
        {
            if (sliderNodes == null)
                return;

            sliderNodes.Clear();

            // The thin control-polygon lines connecting consecutive anchors, drawn first so the handles sit on
            // top (mirrors the post-placement SliderControlPointVisualiser polygon).
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1].Position;
                Vector2 d = pts[i].Position - a;
                sliderNodes.Add(new Box
                {
                    Position = a,
                    Origin = Anchor.CentreLeft,
                    Width = d.Length,
                    Height = 1.5f,
                    Rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X)),
                    Colour = new Color4(1f, 1f, 1f, 0.35f),
                });
            }

            float size = Math.Max(6f, currentDiameter * 0.26f);
            for (int i = 0; i < pts.Count; i++)
            {
                bool red = pts[i].IsSegmentStart && i > 0;
                sliderNodes.Add(new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Position = pts[i].Position,
                    Size = new Vector2(size),
                    Masking = true,
                    BorderThickness = 2f,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = red ? OsuColour.Pink : Color4.White,
                        Alpha = red ? 0.95f : 0.85f,
                    },
                });
            }
        }

        /// <summary>The beat-snapped current time (the time a placed object would receive).</summary>
        private double snappedNow() => SnappedTimeSource?.Invoke() ?? TimeSource?.Invoke() ?? 0;

        /// <summary>The combo colour and number a circle placed at the current time would receive.</summary>
        private (Color4 colour, int number) previewCombo()
        {
            // Count only objects the playhead has actually reached on screen. The snapped placement time can round FORWARD
            // past the next object, and the playfield draws by the (audio-offset) visual clock, so using the snapped time
            // alone made the ghost read one number too high (e.g. 3) on top of the real object that is still 2.
            double time = SnappedTimeSource?.Invoke() ?? TimeSource?.Invoke() ?? 0;
            if (TimeSource != null)
                time = Math.Min(time, TimeSource());

            int prevNumber = 0;
            int prevIndex = 0;
            bool anyBefore = false;

            foreach (var o in currentHitObjects)
            {
                // Strictly before: an object sitting exactly at the (snapped) time is the one the ghost overlaps, so it must
                // not be counted - otherwise the ghost shows the NEXT number (3) on top of that object's own number (2).
                if (o.StartTime < time - 0.5)
                {
                    prevNumber = o.ComboNumber;
                    prevIndex = o.ComboIndex;
                    anyBefore = true;
                }
                else
                {
                    break;
                }
            }

            // A new combo (Q) or the very first object restarts the count on the next colour.
            if (NewComboArmed || !anyBefore)
                return (comboColour(anyBefore ? prevIndex + 1 : 0), 1);

            return (comboColour(prevIndex), prevNumber + 1);
        }

        /// <summary>The configurable combo colour for an index, as an osuTK colour.</summary>
        private Color4 comboColour(int index)
        {
            var c = editable.ComboColourFor(index);
            return new Color4(c.R, c.G, c.B, c.A);
        }
    }
}
