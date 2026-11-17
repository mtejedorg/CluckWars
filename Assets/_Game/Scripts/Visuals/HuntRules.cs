using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.UI;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>What stops a live Death Mark's pounce right now, in the order the hunter's screen names it (declaration order).</summary>
    public enum HuntBlocker : byte
    {
        /// <summary>Nothing to name: arming, the pounce is legal, or only the hunter itself is stunned.</summary>
        None = 0,
        /// <summary>The target is shelled (steal-proof): nothing executes it.</summary>
        Shelled = 1,
        /// <summary>A base wall stands between hunter and target.</summary>
        Walled = 2,
        /// <summary>The target is beyond the kill range.</summary>
        TooFar = 3,
        /// <summary>The target is not stunned.</summary>
        NotStunned = 4,
    }

    /// <summary>What the hunter's screen edge shows (design section 10): a function of the hunt state only.</summary>
    public enum HuntScreenTint : byte
    {
        /// <summary>Nothing: no hunt, or the draw is still running.</summary>
        None = 0,
        /// <summary>Locked, not yet armed: a dark edge, the hunt is running.</summary>
        Arming = 1,
        /// <summary>Armed and the pounce is not legal: the red edge.</summary>
        Blocked = 2,
        /// <summary>The pounce is legal: the yellow edge.</summary>
        Pounce = 3,
    }

    /// <summary>The one corner badge the hunter's marker shows (bottom-right): egg, wall, star, or none.</summary>
    public enum MarkerBadge : byte
    {
        None = 0,
        Shell = 1,
        Lock = 2,
        Star = 3,
    }

    /// <summary>Where the hunter's marker is anchored this frame (K5r): exactly one place per frame.</summary>
    public enum MarkAnchor : byte
    {
        /// <summary>Over the target's head: the target is inside the floor circle round the hunter's own bird.</summary>
        OverHead = 0,
        /// <summary>A compact badge at the tip of the target's emphasised floor arrow: the target is outside the circle.</summary>
        OnArrow = 1,
        /// <summary>Outside the circle but the floor ring paints no arrow for it (pool full, dev edge arrows, no local bird): the
        /// marker keeps its K3 placement (over the head when in frame, else pinned to the edge).</summary>
        EdgeFallback = 2,
    }

    /// <summary>Why a hunter's mark ended, from the hunter's own last live frame.</summary>
    public enum MarkEndCause : byte
    {
        /// <summary>No word: not the hunter's mark, the round stopped, or the hunter is gone.</summary>
        None = 0,
        /// <summary>The pounce was legal on the last live frame: a landed Kill or a refunded one. No word.</summary>
        Pounced = 1,
        /// <summary>The window ran out or the target was lost.</summary>
        Missed = 2,
        /// <summary>The mark ended with the target behind its wall or in an egg.</summary>
        Safe = 3,
    }

    /// <summary>The facts of a hunter's last live frame that decide <see cref="HuntRules.EndCauseFor"/>.</summary>
    public struct HuntLastFrame
    {
        public MarkRole Role;
        public bool Armed;
        public bool CanKill;
        public bool Shelled;
        public bool Walled;
        public HuntBlocker Blocker;
        /// <summary>Seconds left in the mark's window on that frame.</summary>
        public float Remaining;
    }

    /// <summary>
    /// The hunter's side of the Death Mark as pure rules (spec 2026-11-17 H1-H4): which condition blocks the pounce, the count
    /// the marker and hex show, why a mark ended, and the marker and bracket colours. No Unity object, no networking: the
    /// inputs are what <see cref="MarkFeedback"/> gathered from replicated state.
    /// </summary>
    public static class HuntRules
    {
        /// <summary>The first condition that blocks the pounce, in the order egg, wall, too far, not stunned. None while the
        /// mark is arming or the pounce is legal, and when nothing on the list applies (only the hunter is stunned).</summary>
        public static HuntBlocker BlockerFor(bool armed, bool canKill, bool shelled, bool walled, float distance, float killRange, bool targetStunned)
        {
            if (!armed || canKill) return HuntBlocker.None;
            if (shelled) return HuntBlocker.Shelled;
            if (walled) return HuntBlocker.Walled;
            if (TooFar(distance, killRange)) return HuntBlocker.TooFar;
            if (!targetStunned) return HuntBlocker.NotStunned;
            return HuntBlocker.None;
        }

        /// <summary>The kill range's own comparison (<c>AssassinExecute.CanKillNow</c>: legal while distance &lt;= range).</summary>
        public static bool TooFar(float distance, float killRange) => distance > killRange;

        /// <summary>Tenths shown in the live second, rounded up so a live mark never reads 0.0.</summary>
        public static int LiveTenths(float remaining) => CasterStunCountdownRules.Tenths(remaining);

        /// <summary>The count on the marker and hex: the arm seconds (3, 2, 1) while arming, whole seconds (rounded up) while
        /// the live window has a second or more left, tenths ("0.8") below one second.</summary>
        public static string CountText(bool armed, int armSeconds, float remaining)
        {
            if (!armed) return armSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (remaining >= 1f) return MarkPresentationModel.ShownSeconds(remaining).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return CasterStunCountdownRules.Format(LiveTenths(remaining));
        }

        /// <summary>Below this many metres the chip shows one decimal (so 6.3 m reads red and 6.1 m does not), from here whole metres.</summary>
        public const float ChipDecimalBelowMetres = 10f;

        /// <summary>The metres chip's change key, the one place the number is decided (marker and Hunt hex both read it): tenths
        /// rounded UP under 10 m (a target a hair beyond the range never reads as the range), else whole metres plus 1000. Compare
        /// keys to write the text only when it changes.</summary>
        public static int MetresKey(float metres)
        {
            float m = Mathf.Max(0f, metres);
            if (m < ChipDecimalBelowMetres)
            {
                int tenths = Mathf.CeilToInt(m * 10f);
                if (tenths < Mathf.RoundToInt(ChipDecimalBelowMetres * 10f)) return tenths;
            }
            return 1000 + Mathf.RoundToInt(m);
        }

        /// <summary>The number of a <see cref="MetresKey"/>: "6.2", "24".</summary>
        public static string MetresText(int key) => key >= 1000
            ? (key - 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : (key / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + (key % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The metres number for a distance.</summary>
        public static string MetresText(float metres) => MetresText(MetresKey(metres));

        /// <summary>The whole chip ("6.2 m") for a key: the shared <c>mark.distance</c> string.</summary>
        public static string ChipText(int key) => UiText.Format(UiKeys.MarkDistance, ("m", MetresText(key)));

        /// <summary>The chip is red while the target is beyond the kill range (the shipped <c>KillRange</c>), the seat colour inside it.</summary>
        public static bool ChipRed(float distance, float killRange) => TooFar(distance, killRange);

        /// <summary>The smallest WCAG contrast ratio the metres chip's text keeps against its ink pill.</summary>
        public const float ChipMinContrast = 4.5f;

        /// <summary>The metres chip's text colour: cream on the red pill (the same treatment as the Hunt hex's far chip); within reach
        /// the seat colour on the ink pill, lightened toward cream just far enough to keep <see cref="ChipMinContrast"/> against the
        /// ink (the dark teal seat would not otherwise).</summary>
        public static Color ChipTextColour(bool red, Color seatColour)
        {
            Color cream = FeedbackTuning.MarkCream;
            if (red) return cream;
            Color ink = FeedbackTuning.MarkInk;
            if (Contrast(seatColour, ink) >= ChipMinContrast) return seatColour;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Contrast(Color.Lerp(seatColour, cream, mid), ink) >= ChipMinContrast) hi = mid; else lo = mid;
            }
            return Color.Lerp(seatColour, cream, hi);
        }

        /// <summary>WCAG contrast ratio (1..21) between two opaque colours.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static float Luminance(Color c) =>
            0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);

        private static float Linear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        /// <summary>The marker's one corner badge, priority egg, wall, not stunned. The egg and the wall show from the lock on,
        /// arming included (the mode says so before the blocker does); the star is a live-second reason only (the blocker is None
        /// until armed). Too far has no badge: the red metres chip is its glyph.</summary>
        public static MarkerBadge BadgeFor(MarkMarkerMode mode, HuntBlocker blocker) =>
            mode == MarkMarkerMode.Shelled || blocker == HuntBlocker.Shelled ? MarkerBadge.Shell
            : mode == MarkMarkerMode.Walled || blocker == HuntBlocker.Walled ? MarkerBadge.Lock
            : blocker == HuntBlocker.NotStunned ? MarkerBadge.Star
            : MarkerBadge.None;

        /// <summary>Why the mark ended, from the frozen last live frame. Anything not provably SAFE or a pounce is MISSED.</summary>
        public static MarkEndCause EndCauseFor(in HuntLastFrame last, bool matchRunningNow, bool hunterRemovedNow)
        {
            if (last.Role != MarkRole.Assassin || !matchRunningNow || hunterRemovedNow) return MarkEndCause.None;
            if (last.CanKill) return MarkEndCause.Pounced;
            if (last.Remaining > FeedbackTuning.MarkEndExpiryToleranceSeconds) return MarkEndCause.Missed;   // ended early
            return last.Shelled || last.Walled ? MarkEndCause.Safe : MarkEndCause.Missed;
        }

        /// <summary>Whether the end shows the hold, the word and the glyph (a Missed or Safe end).</summary>
        public static bool ShowsEndWord(MarkEndCause cause) => cause == MarkEndCause.Missed || cause == MarkEndCause.Safe;

        /// <summary>Whether the target counts as inside the floor circle: within <paramref name="ringRadius"/>, and once inside it stays
        /// inside until it is <see cref="FeedbackTuning.MarkCircleHysteresisMetres"/> beyond it, so the mark never flickers between
        /// the two places at the line.</summary>
        public static bool InsideCircle(float planarDistance, float ringRadius, bool wasInside) =>
            planarDistance <= ringRadius + (wasInside ? FeedbackTuning.MarkCircleHysteresisMetres : 0f);

        /// <summary>The marker's one anchor: inside the circle over the head, outside it on the target's floor arrow when one is
        /// painted, else the edge fallback. <paramref name="ringRadius"/> is <c>FloorRivalArrows.RingRadiusFor</c>, the distance
        /// is <c>FloorArrowRules.PlanarDistance</c> from the hunter's bird. Idempotent in <paramref name="wasInside"/>: feeding the
        /// result of <see cref="InsideCircle"/> back in returns the same answer.</summary>
        public static MarkAnchor AnchorFor(float planarDistance, float ringRadius, bool wasInside, bool arrowAvailable) =>
            InsideCircle(planarDistance, ringRadius, wasInside) ? MarkAnchor.OverHead
            : arrowAvailable ? MarkAnchor.OnArrow
            : MarkAnchor.EdgeFallback;

        /// <summary>The marker body's base scale for an anchor: the compact badge on the arrow, full size anywhere else.</summary>
        public static float AnchorBaseScale(MarkAnchor anchor) => anchor == MarkAnchor.OnArrow ? FeedbackTuning.MarkMarkerOnArrowScale : 1f;

        /// <summary>The pointer is drawn for every anchor but the arrow, in every marker mode: a function of the anchor only.</summary>
        public static bool PointerShown(MarkAnchor anchor) => anchor != MarkAnchor.OnArrow;

        /// <summary>The KILL pulse factor (1 outside KILL and under Reduced Motion).</summary>
        public static float KillPulseFactor(MarkMarkerMode mode, bool reducedMotion, float unscaledTime) =>
            mode == MarkMarkerMode.Kill && !reducedMotion
                ? 1f + FeedbackTuning.MarkMarkerKillPulse * 0.5f * (1f + Mathf.Sin(unscaledTime * FeedbackTuning.MarkMarkerKillPulseHz * 2f * Mathf.PI))
                : 1f;

        /// <summary>The marker body's final scale: the anchor's base scale times every animated factor (appear pop, live pop, KILL pulse),
        /// so no animation can undo the anchor's size.</summary>
        public static float MarkerScale(MarkAnchor anchor, float pop, float live, float pulse) => AnchorBaseScale(anchor) * pop * live * pulse;

        /// <summary>The hunter marker's colour by mode: yellow KILL, grey wall, cream egg, else the target's seat colour.</summary>
        public static Color MarkerColour(MarkMarkerMode mode, int targetCorner) =>
            mode == MarkMarkerMode.Kill ? FeedbackTuning.MarkKillColor
            : mode == MarkMarkerMode.Walled ? FeedbackTuning.MarkWalledColor
            : mode == MarkMarkerMode.Shelled ? FeedbackTuning.MarkCream
            : PlayerPalette.ForCorner(targetCorner);

        /// <summary>The ground brackets' colour: the seat colour while arming, yellow when the pounce is legal, red in the live
        /// second while it is not.</summary>
        public static Color BracketColour(bool armed, bool canKill, int corner) =>
            !armed ? PlayerPalette.ForCorner(corner)
            : canKill ? FeedbackTuning.MarkKillColor
            : FeedbackTuning.MarkHuntBlockedColor;

        /// <summary>The hunter's screen edge by hunt state: nothing until the draw has locked, dark while arming, then red or yellow.</summary>
        public static HuntScreenTint ScreenTintFor(bool locked, bool armed, bool canKill) =>
            !locked ? HuntScreenTint.None
            : !armed ? HuntScreenTint.Arming
            : canKill ? HuntScreenTint.Pounce
            : HuntScreenTint.Blocked;

        /// <summary>The edge glow's colour: the brackets' own red or yellow, so the edge and the brackets cannot disagree. Clear for
        /// the states that draw no glow (None, and Arming, which is the dark edge only).</summary>
        public static Color ScreenTintColour(HuntScreenTint tint, int targetCorner) =>
            tint == HuntScreenTint.Blocked ? BracketColour(true, false, targetCorner)
            : tint == HuntScreenTint.Pounce ? BracketColour(true, true, targetCorner)
            : Color.clear;
    }
}
