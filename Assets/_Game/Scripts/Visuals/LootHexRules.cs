using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>What the Steal hex's loot dot shows.</summary>
    public enum LootDotState : byte
    {
        /// <summary>Not a Thief, or no round running: nothing.</summary>
        Off = 0,
        /// <summary>The next mark is still coming: a pale, empty bag dot that fills with the Steal cooldown.</summary>
        Coming = 1,
        /// <summary>A mark is up on a rival: the dot is solid gold.</summary>
        Live = 2,
        /// <summary>The next mark is due (Steal is ready or within the passive's lead time) but none is up yet: nobody is eligible, or the opening seconds or the redraw
        /// gap still hold (rule v2, 2026-11-26). A hollow gold ring: the promise is not kept yet. Appended after <see cref="Live"/>; no ordinal moved.</summary>
        Searching = 3,
    }

    /// <summary>The Steal hex's loot dot for one frame.</summary>
    public struct LootHexView
    {
        public LootDotState Dot;
        /// <summary>How much of the Steal cooldown has passed, 0..1 (1 while <see cref="LootDotState.Live"/>).</summary>
        public float Fill01;
        /// <summary>The marked rival is inside Steal's reach: the dot pulses ("fire now").</summary>
        public bool Pulse;
    }

    /// <summary>
    /// The Steal hex while a loot mark is on or coming (design 2026-11-23 section 11, "mark is coming"): pure rules, no Unity object, fed by
    /// <c>LootFeedback</c> and drawn by the HUD (<c>TouchControlsController</c>, touch arc and desktop / pad strip alike). The dot is empty and fills with the
    /// Steal cooldown, then turns into a hollow gold ring the lead time before Steal is ready (<see cref="LootRules.DrawDue"/>: the next mark is being drawn) and
    /// solid gold only once a mark is up on a rival, so a draw that finds nobody (rule v2) never shows a promise it does not keep. The lead is the passive's
    /// (<c>LootMarkFacts.LeadSeconds</c>), never a literal.
    /// </summary>
    public static class LootHexRules
    {
        /// <summary>USS class on the hex while it carries the loot dot.</summary>
        public const string LootClass = "cw-hex--loot";

        /// <param name="isThiefActive">The local chicken's passive carries a loot mark.</param>
        /// <param name="state">The replicated mark state.</param>
        /// <param name="cooldownRemaining">Seconds left on the Steal slot.</param>
        /// <param name="cooldownProgress01">The Steal slot's cooldown progress, 0..1 (1 when ready).</param>
        /// <param name="leadSeconds">The passive's lead: how long before Steal is ready the next mark is drawn.</param>
        /// <param name="markedInReach">The marked rival is within Steal's reach.</param>
        public static LootHexView ViewFor(bool isThiefActive, LootState state, float cooldownRemaining, float cooldownProgress01, float leadSeconds, bool markedInReach)
        {
            if (!isThiefActive || state == LootState.Off) return default;
            bool markUp = state == LootState.Live || state == LootState.Overdue;
            if (markUp) return new LootHexView { Dot = LootDotState.Live, Fill01 = 1f, Pulse = markedInReach };
            if (LootRules.DrawDue(cooldownRemaining, leadSeconds)) return new LootHexView { Dot = LootDotState.Searching, Fill01 = 1f };
            return new LootHexView { Dot = LootDotState.Coming, Fill01 = Mathf.Clamp01(cooldownProgress01) };
        }
    }
}
