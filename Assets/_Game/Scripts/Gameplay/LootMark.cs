using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Thief's loot mark as numbers (design 2026-11-23, BL-217), read once from the Thief's passive so the match simulator and,
    /// later, the game use the same values. A <see cref="Bounty"/> of 0 switches the mark off: the shipped Thief has none.
    /// </summary>
    public struct LootMarkFacts
    {
        /// <summary>The passive is the Thief's. True even with the mark off, so a report can find the kit whatever its numbers.</summary>
        public bool IsThief;
        /// <summary>New food a marked steal adds to the Thief's bounty bag on top of what it takes. 0 = no loot mark at all.</summary>
        public float Bounty;
        /// <summary>Seconds after which a mark moves, checked only while Steal is on cooldown.</summary>
        public float Seconds;
        /// <summary>How many seconds before Steal comes off cooldown the next mark appears.</summary>
        public float LeadSeconds;
        /// <summary>The marked take goes to the bounty bag (true) or, as a probe, to the beak under its free room (false).</summary>
        public bool TakeToBag;
        public LootWeightTable Weights;
        /// <summary>The Thief's own number: <see cref="LootWeightTable.OverThreshold"/> is this fraction of the win target.</summary>
        public float OverFraction;

        public bool Active => IsThief && Bounty > 0f;

        public static LootMarkFacts None => default;
    }

    /// <summary>The lottery's weights (design section 5): what a rival's food adds to its chance of carrying the Thief's mark.</summary>
    public struct LootWeightTable
    {
        public float Base;
        public float Banked;
        public float Carried;
        /// <summary>Banked plus carried food above this earns <see cref="OverBonus"/> (Maestro: "mas de 25", banked plus carried).</summary>
        public float OverThreshold;
        public float OverBonus;
    }

    /// <summary>Where the Thief's mark stands in its life cycle (design section 4).</summary>
    public enum LootState : byte { Off, Waiting, Live, Overdue }

    /// <summary>
    /// Who the Thief's loot mark lands on, pure, every roll injected, so the game and the simulator call the same rule. The caller lists
    /// the living rivals, runs <see cref="Filter"/> (the last marked rival is out unless he is the only one), weighs each with
    /// <see cref="Weight"/> and picks with <see cref="Pick"/>.
    /// </summary>
    public static class LootPickRules
    {
        /// <summary>The last marked rival leaves the list unless he is the only rival in the match (the Death Mark's rule, shared).</summary>
        public static void Filter<T>(List<T> candidates, bool hasLast, T last, int rivalsInMatch) where T : struct, IEquatable<T> =>
            MarkPickRules.Filter(candidates, hasLast, last, rivalsInMatch);

        /// <summary>
        /// A candidate's weight: the base, plus banked food times its weight, plus carried food (beak and bounty bag) times its weight, plus
        /// the over bonus when banked plus carried is above the threshold.
        /// </summary>
        public static float Weight(float banked, float carried, in LootWeightTable t)
        {
            float b = banked > 0f ? banked : 0f;
            float c = carried > 0f ? carried : 0f;
            float w = t.Base + t.Banked * b + t.Carried * c;
            if (b + c > t.OverThreshold) w += t.OverBonus;
            return w > 0f ? w : 0f;
        }

        /// <summary>
        /// A proportional pick: the chance of candidate i is weights[i] over their sum. Walks the candidates in the caller's order and returns
        /// the first whose running sum passes <c>roll * sum</c>. -1 with no candidate; when every weight is 0 a uniform pick by the same roll.
        /// </summary>
        public static int Pick(IReadOnlyList<float> weights, float roll)
        {
            int count = weights.Count;
            if (count <= 0) return -1;
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += weights[i] > 0f ? weights[i] : 0f;
            if (!(sum > 0f)) return MarkPickRules.Pick(count, roll);
            float target = (roll < 0f ? 0f : roll) * sum;
            float run = 0f;
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                float w = weights[i] > 0f ? weights[i] : 0f;
                if (w <= 0f) continue;
                run += w;
                last = i;
                if (run > target) return i;
            }
            return last;   // a roll at the very top of the range that rounding pushed past the sum
        }
    }

    /// <summary>The loot mark's other pure rules (design section 13): when a take holds, when the next mark appears, when a mark moves, what it takes.</summary>
    public static class LootRules
    {
        /// <summary>A Steal cast takes the mark: the marked bird is inside the cast, no base wall lies between and he is not steal-proof.</summary>
        public static bool CanTake(bool inRange, bool walled, bool stealProof) => inRange && !walled && !stealProof;

        /// <summary>When the next mark is drawn: <paramref name="lead"/> seconds before Steal is ready, at once when that has already passed.</summary>
        public static float DrawAt(float now, float readyAt, float lead) => Math.Max(now, readyAt - Math.Max(0f, lead));

        /// <summary>
        /// A mark that is <paramref name="seconds"/> old moves only while Steal is on cooldown; with Steal ready it stays (the Overdue state).
        /// </summary>
        public static bool TimedOut(float age, float seconds, bool stealReady) => age >= seconds && !stealReady;

        /// <summary>
        /// What the marked bird loses: the nominal steal times the Thief's factor, held to what the bird carries (beak plus bag). The Thief's
        /// free room does not limit it: the take goes to the bag. 0 for an empty bird (the bounty still pays).
        /// </summary>
        public static float MarkedTake(float nominal, float factor, float victimCargo, float victimBag) =>
            StealMath.Clamp(nominal * factor, float.MaxValue, StealRules.Stealable(victimCargo, victimBag));
    }
}
