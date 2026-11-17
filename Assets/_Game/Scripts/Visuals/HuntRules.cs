using CluckWars.Input;
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
    }
}
