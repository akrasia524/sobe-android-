using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Every tunable number for the "Humanize" Auto cursor, in one place, so the feel can be dialled in by hand.
    /// Edit a value (or use the in-editor live panel) and watch the AU cursor (toggle Humanize in the AU chip's mini-menu).
    ///
    /// Conventions:
    ///  - Distances are osu! NORMALISED units: distance * 50 / circleRadius, so one circle DIAMETER = 100, radius = 50.
    ///    (e.g. 80 ≈ 0.8 of a diameter; 200 ≈ a 2-diameter jump.) This matches osu!'s own aim metric.
    ///  - Times are milliseconds.
    ///  - A "Lo/Hi" pair is a smoothstep ramp. The comment says which side is the "full" (=1) side.
    ///  - Amounts are fractions (of the jump distance, or of the circle radius) unless noted.
    ///
    /// Defaults were calibrated from real osu!lazer replays (the OsuBeatmapEditor.ReplayAnalysis tool).
    /// </summary>
    public static class HumanizeTuning
    {
        // ─── FLOW (stream sweep vs settle-on-each-note) ────────────────────────────────────────────────────
        // Each object gets a flow factor φ from its surrounding note-to-note time gaps: short, fast gaps on both
        // sides → φ≈1 (the cursor sweeps THROUGH it like a stream); a long gap on either side → φ≈0 (it eases in
        // and settles on that note, like an isolated hit). φ scales the spline's velocity at the object, and the
        // SAME φ·velocity is shared by the gap before and after, so the path is always C1 (it can never teleport).
        public static float FlowFastMs = 110f;   // gaps THIS short on both sides = full flow-through (a stream)
        public static float FlowSlowMs = 220f;   // a gap THIS long either side = settle on the note (a jump)
        // Stream "path of least effort": a small note-to-note zig-zag in a stream isn't traced — the cursor goes
        // nearly straight through it, following it only once its amplitude passes a threshold. A binomial smooth
        // (¼,½,¼) removes just the ALTERNATING component (so smooth curved streams are kept, only zig-zags flatten);
        // a soft-threshold on the removed part straightens up to the threshold and follows the excess. Streams only.
        public static float StreamStraightenAmount = 0.55f;    // 0 = trace every note, 1 = fully collapse the stream onto its mean line (user-tuned)
        public static float StreamStraightenThreshold = 1.6f;  // deviation half-amplitude (× radius) below which it's straightened; above, followed (user-tuned)
        // How many notes on EACH side the stream-straightening averages over. A wide window makes the cursor cut a
        // smooth line THROUGH a whole run instead of only cancelling the highest-frequency note-to-note zig-zag, so a
        // dense stream stops looking "snapped" note by note. The window auto-stops at a jump (flow breaks the chain).
        public static float StreamStraightenTaps = 4f;

        // ─── REACTION (dwell after a pause, then move late) ────────────────────────────────────────────────
        // After a pause the player doesn't drift slowly toward the next note — they hold position and only move once
        // they've reacted to the upcoming circle, arriving right on time. So on a long gap the cursor DWELLS near the
        // previous spot, then makes the whole move over the last ReactionMoveMs (arriving settled). Short gaps (streams,
        // close jumps) keep flowing with no delay. React ramps in between ReactionShortMs (none) and ReactionLongMs (full).
        public static float ReactionShortMs = 260f;   // gaps THIS short or less: no reaction delay (flow straight through)
        public static float ReactionLongMs = 560f;    // gaps THIS long or more: full reaction delay (dwell, then move late)
        public static float ReactionMoveMs = 260f;    // how long the actual move to the next circle takes; the rest of the gap is dwell

        // ─── ARC (lateral bow on jumps — the main "human vs robot" trait) ───────────────────────────────────
        // Replay analysis: real cursors bow OFF the straight line between two objects by 6-21% of the jump (more on
        // shorter jumps). arcFrac = ArcAmount × ArcFalloffNorm / (ArcFalloffNorm + dNorm) fits the measured curve.
        // Only applied where flow is low (jumps); the Hermite spline already curves streams from their note layout.
        public static float ArcAmount = 0.35f;       // bow as a fraction of the jump as dNorm→0 (then decays with distance)
        public static float ArcFalloffNorm = 90f;    // normalised distance at which the bow fraction halves
        // Handedness: arcs bow away from a fixed wrist pivot (offset from the playfield centre), so the side is
        // consistent and never flips. Replay-calibrated to a pivot below + slightly off-centre. osu! y is DOWN,
        // so a positive Y is BELOW the play area; flip HandPivotX's sign to swap the handedness (right vs left).
        public static float HandPivotX = -40f;       // pivot offset from centre on X (+ = right)
        public static float HandPivotY = 350f;       // pivot offset from centre on Y (+ = below the play area)

        // ─── AIM / TREMOR (the only high-frequency "noise") ────────────────────────────────────────────────
        public static float AimErrorAmount = 0.14f;  // lands off-centre: × radius × uniform(0..1) (user-tuned tighter than the ~0.34 r replay median)
        public static float TremorAmount = 1f;        // multiplier on the faint ~0.13px hand wobble (replay shake is tiny)

        // ─── OVERSHOOT (a medium jump flicks just past, then settles — replay peak ~100-160 normalised) ──────
        public static float OvershootGain = 0.046f;    // peak overshoot as a fraction of the jump (0 = none) (user-tuned)
        public static float OvershootPeakNorm = 130f;  // jump size (normalised) where overshoot peaks; fades above & below

        // ─── SLIDERS (lazy follow — cut curvature smoothly, never stall) ────────────────────────────────────
        // Real cursors don't trace the path exactly: on a curve they "omit" it, cutting the corner into an arc for
        // comfort. Modelled as a CENTRED moving average of the ball path (smooth, always tracking the ball — no
        // stop-and-wait or lag). The window decides how much curvature is cut; straights stay on the ball.
        public static float SliderCutWindow = 0.4f;      // averaging half-window as a fraction of the slider duration (bigger = more corner cut) (user-tuned)
        public static float SliderLaziness = 0.76f;       // overall blend: 0 = trace the path exactly, 1 = full cut (user-tuned)
        public static float SliderEdgeFadeMs = 72f;       // keep the head/tail on-path over this many ms (those are actually hit) (user-tuned)
        // Organic wander: a real hand doesn't sit dead-on the ball — it loosely snakes a little to each side of the
        // path. Without this, straight sliders trace a perfect (robotic) line and curves become a clean averaged arc.
        // A slow, low-frequency lateral drift (× radius) breaks that up. Bounded by FollowCircleSafety, so it can never
        // wander far enough to drop a tick. 0 = dead-on the lazy path.
        public static float SliderFollowWander = 0.2f;     // 2D drift amplitude as a fraction of the radius
        public static float SliderWanderHz = 0.85f;        // how fast the wander oscillates (cycles per slider-second)
        // A "kick slider" (1/4, 1/8, ...) is so short it's played like a circle — the player just taps the head and
        // moves on instead of tracing it. Sliders at or under this duration release at the head (stream like circles).
        public static float KickSliderMaxMs = 80f;
        // Momentum: how much a fast-flowing rhythm makes the player release a slider early (skip the rest and flow on).
        // 0 = always follow to the end; 1 = a full-flow burst releases right at the head. In between = a brief "amago".
        public static float SliderFlowRelease = 0.8f;
        // Reverse (repeat) sliders: the player often parks near the MIDDLE rather than chasing the ball back and forth
        // (the ball keeps returning within reach). 0 = follow the ball normally, 1 = sit on the path midpoint.
        public static float ReverseLaziness = 0.6f;
        public static float SliderReleaseFrac = 0.012f;  // let go this fraction of the slider early (replay median ~1%)..
        public static float SliderReleaseMaxMs = 8f;     // ..capped at this many ms
        // Anticipation ("path of least effort" leaving a slider): approaching the release the player already drifts the
        // cursor toward the NEXT object instead of tracing the tail then jumping. Blend the follow target toward the
        // next head over the last AnticipationMs, up to AnticipationAmount. Clamped by FollowCircleSafety so ticks are
        // never lost — the cursor may lead toward the exit but stays inside the follow circle of the actual ball.
        public static float SliderAnticipation = 0.35f;    // 0 = trace the tail fully, 1 = pull the follow this fraction of the way toward the next head by release
        public static float SliderAnticipationMs = 110f;   // how long before release the drift-toward-next begins
        // Anticipation only makes sense when the NEXT note is "downstream" of the slider's exit flow (you drift toward
        // where you're already heading). If it's back the way the slider came, don't pre-empt — follow to the end then
        // turn. Gate = smoothstep(dot(exitDir, toNextDir), Lo, Hi): ≤Lo (opposite) none, ≥Hi (ahead) full anticipation.
        public static float AnticipationAlignLo = -0.15f;  // alignment (dot) at/below which anticipation is fully off
        public static float AnticipationAlignHi = 0.55f;   // alignment (dot) at/above which anticipation is fully on
        // Safety leash on the lazy CURVATURE CUT only (not the exit anticipation): the corner-cutting follow may never
        // stray further than this many object-radii from the real ball, so a sharp curve / reverse can't drop a tick.
        // osu!'s follow circle is ~2.4r; stay a bit inside it. The exit toward the next note rides on top, unleashed.
        public static float FollowCircleSafety = 1.9f;

        // ─── defaults snapshot (for the live panel's "Reset") ──────────────────────────────────────────────
        // Captured once, before any tweak, via reflection over the public static float fields above.
        private static readonly Dictionary<string, float> defaults =
            typeof(HumanizeTuning).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(float))
                .ToDictionary(f => f.Name, f => (float)f.GetValue(null)!);

        /// <summary>Restores every value to the calibrated default it had at startup.</summary>
        public static void ResetToDefaults()
        {
            foreach (var f in typeof(HumanizeTuning).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(float) && defaults.TryGetValue(f.Name, out float d))
                    f.SetValue(null, d);
        }
    }
}
