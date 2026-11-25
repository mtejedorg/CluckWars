using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The pure decisions of the Warrior kits' feedback (BL-227, design 2026-11-25 section 16 slice G9): how full a draining bar is, how many debt pips
    /// are drawn and filled, which food a plain cargo pop leaves to the kit's own pop. <see cref="WarriorFeedback"/> reads the replicated state and asks
    /// these; the HUD only draws the answers. No state, no allocation.
    /// </summary>
    public static class WarriorFeedbackRules
    {
        /// <summary>A draining bar's fill, 0 to 1: <paramref name="left"/> of <paramref name="total"/> seconds; 0 when the total is 0 or less.</summary>
        public static float Fill01(float left, float total) => total > 0f ? Mathf.Clamp01(left / total) : 0f;

        /// <summary>
        /// The fresh marker's fill: full while the rival is mid Peck cast (the window has not started draining), then the share of
        /// <paramref name="freshSeconds"/> left after the Peck landed food.
        /// </summary>
        public static float FreshFill01(bool casting, float secondsSinceFed, float freshSeconds) =>
            casting ? 1f : Fill01(freshSeconds - secondsSinceFed, freshSeconds);

        /// <summary>Debt pips drawn on a rival: the cap (so the player reads the ceiling), never more than the <paramref name="maxSlots"/> the HUD has.</summary>
        public static int PipSlots(int cap, int maxSlots) => Mathf.Clamp(cap, 0, Mathf.Max(0, maxSlots));

        /// <summary>Pips that are filled: the held count inside the drawn slots.</summary>
        public static int PipsFilled(int count, int slots) => Mathf.Clamp(count, 0, Mathf.Max(0, slots));

        /// <summary>Is a food amount worth a pop of its own (it rounds to at least 1).</summary>
        public static bool PopShown(float amount) => Mathf.RoundToInt(amount) > 0;

        /// <summary>
        /// What is left of a cargo gain after the kit's own pop already named <paramref name="popped"/> of it (the same split the Thief's loot uses): the plain
        /// <c>+n</c> pop shows the rest, and nothing at all when the kit's pop named it all.
        /// </summary>
        public static float GainAfterPopped(float delta, float popped) => Mathf.Max(0f, delta - Mathf.Max(0f, popped));

        /// <summary>The Steal hex's number: the amount the next Steal would take, whole food. 0 means nothing to show.</summary>
        public static int HexAmount(float amount) => Mathf.Max(0, Mathf.RoundToInt(amount));

        /// <summary>
        /// What the Steal would take from a carrier: <paramref name="asked"/> (the debt-inflated amount), no more than the victim holds
        /// (<paramref name="victimStealable"/>) and no more than fits in the Enforcer's beak (<paramref name="ownFreeSpace"/>), as the cargo clamps the ask.
        /// </summary>
        public static float ClampedSteal(float asked, float victimStealable, float ownFreeSpace) =>
            Mathf.Max(0f, Mathf.Min(asked, Mathf.Min(victimStealable, ownFreeSpace)));

        /// <summary>A pop event and the cargo change it names land in the same Render or the next one; later than that, the pending amount is stale.</summary>
        public static bool PendingFresh(int frameNow, int framePending, int slackFrames) => frameNow - framePending <= slackFrames;
    }
}
