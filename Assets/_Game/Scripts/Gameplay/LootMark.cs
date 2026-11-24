using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Thief's loot mark as numbers (design 2026-11-23, BL-217; ordinary cargo since Maestro's third note of 2026-11-24), read once from the Thief's passive so the match simulator and,
    /// later, the game use the same values. A <see cref="Bounty"/> of 0 switches the mark off: the shipped Thief has none.
    /// </summary>
    public struct LootMarkFacts
    {
        /// <summary>The passive is the Thief's. True even with the mark off, so a report can find the kit whatever its numbers.</summary>
        public bool IsThief;
        /// <summary>New food a marked steal adds to the Thief's cargo on top of what it takes, as far as it fits. 0 = no loot mark at all.</summary>
        public float Bounty;
        /// <summary>Seconds after which a mark moves, checked only while Steal is on cooldown.</summary>
        public float Seconds;
        /// <summary>How many seconds before Steal comes off cooldown the next mark appears.</summary>
        public float LeadSeconds;
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

    /// <summary>What a live mark does on a tick (<see cref="LootRules.Advance"/>).</summary>
    public enum LootStep : byte
    {
        /// <summary>Nothing changes.</summary>
        Stay,
        /// <summary>The mark is old and Steal is ready: it stays on its bird, now <see cref="LootState.Overdue"/>.</summary>
        GoOverdue,
        /// <summary>The mark is old and Steal is on cooldown, for whatever reason: it ends unpaid and the next one is scheduled.</summary>
        Move,
    }

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
        /// A candidate's weight: the base, plus banked food times its weight, plus carried food times its weight, plus
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
        /// One tick of a live mark (design section 4, with Maestro's correction of 2026-11-24). Young (below <paramref name="seconds"/>): it stays,
        /// whatever Steal does. Old with Steal ready: it stays on its bird as <see cref="LootState.Overdue"/>. Old with Steal on cooldown, an
        /// Overdue mark included and whatever spent the cooldown (a steal that hit someone else, a cast on the marked bird that was refused):
        /// it moves, unpaid. A mark that is not <see cref="LootState.Live"/> or <see cref="LootState.Overdue"/> never steps.
        /// </summary>
        public static LootStep Advance(LootState state, float age, float seconds, bool stealReady)
        {
            if (state != LootState.Live && state != LootState.Overdue) return LootStep.Stay;
            if (TimedOut(age, seconds, stealReady)) return LootStep.Move;
            return state == LootState.Live && age >= seconds ? LootStep.GoOverdue : LootStep.Stay;
        }

        /// <summary>
        /// What a marked steal puts into the Thief's cargo (Maestro, 2026-11-24, third note: no capacity-free bag, everything is ordinary cargo).
        /// The stolen food comes first and is an ordinary steal's take: the nominal steal times the Thief's factor, held to the Thief's free room and
        /// to what the bird carries. The new-food bounty fills whatever room is left; what does not fit is lost, never held back. The bird loses
        /// only what the Thief gains from it. <paramref name="wanted"/> is what the same cast would have gained with no room limit, so
        /// <c>wanted - stolen - bountyKept</c> is what the Thief's full beak cost him.
        /// </summary>
        public static void MarkedGain(float nominal, float factor, float freeRoom, float victimCargo, float bounty,
            out float stolen, out float bountyKept, out float wanted)
        {
            float room = freeRoom > 0f ? freeRoom : 0f;
            float available = StealRules.Stealable(victimCargo);
            stolen = StealMath.Clamp(nominal * factor, room, available);
            float bonus = bounty > 0f ? bounty : 0f;
            bountyKept = Math.Min(bonus, Math.Max(0f, room - stolen));
            wanted = StealMath.Clamp(nominal * factor, float.MaxValue, available) + bonus;
        }
    }
}
