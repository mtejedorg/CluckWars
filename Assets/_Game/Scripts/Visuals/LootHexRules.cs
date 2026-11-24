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
        /// <summary>A mark is live, or comes within the passive's lead time: the dot is solid gold.</summary>
        Live = 2,
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
    /// Steal cooldown, then turns solid gold the lead time before Steal is ready: the instant <see cref="LootRules.DrawDue"/> says the next mark is drawn, so
    /// the hex, the disc and the arrow light together. The lead is the passive's (<c>LootMarkFacts.LeadSeconds</c>), never a literal.
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
            if (markUp || LootRules.DrawDue(cooldownRemaining, leadSeconds))
                return new LootHexView { Dot = LootDotState.Live, Fill01 = 1f, Pulse = markUp && markedInReach };
            return new LootHexView { Dot = LootDotState.Coming, Fill01 = Mathf.Clamp01(cooldownProgress01) };
        }
    }
}
