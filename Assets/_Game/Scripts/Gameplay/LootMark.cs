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
        // Rule v2 (2026-11-26, design "v2" section). A field left at 0 switches its rule off, so facts built by hand stay valid.
        /// <summary>Least food a rival must carry to be a candidate for the mark.</summary>
        public float MinCargo;
        /// <summary>
        /// The last-resort floor (Maestro, 2026-11-27): when no rival carries <see cref="MinCargo"/>, the draw takes any rival who carries at least this
        /// much (the other filters stay), so the Thief is never left without a mark while someone carries something. 0 switches the fallback off.
        /// </summary>
        public float FallbackMinCargo;
        /// <summary>
        /// The stalled draw (Maestro, 2026-11-27): when a draw has been allowed and found no rival with food for this many seconds (everyone at 0, nobody
        /// foraging), the mark lands on a random rival, food or not, and stays on him while his beak is empty until someone worth marking carries food.
        /// 0 switches it off.
        /// </summary>
        public float StallSeconds;
        /// <summary>Metres from his own base centre at which a rival weighs in full; closer he weighs less (<see cref="LootPickRules.HomeFactor"/>).</summary>
        public float HomeRadius;
        /// <summary>The weight factor of a rival at or inside his base's wall (0..1).</summary>
        public float HomeMinWeight;
        /// <summary>A marked bird carrying less than this (but more than 0) is "light": the mark leaves him unless Steal is ready and nobody closer is worth it.</summary>
        public float KeepCargo;
        /// <summary>A light marked bird gives way to a candidate no farther from the Thief than him plus this many metres.</summary>
        public float SwitchSlackMeters;
        /// <summary>No mark is drawn before the match has run this many seconds.</summary>
        public float OpeningSeconds;
        /// <summary>No mark is drawn within this many seconds of an unpaid end.</summary>
        public float RedrawGapSeconds;

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

    /// <summary>Why a live mark ends unpaid (<see cref="LootRules.Keep"/>; <see cref="Timeout"/> is <see cref="LootRules.Advance"/>'s). Append only.</summary>
    public enum LootEnd : byte { None, Gone, Home, Empty, Light, Timeout }

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
    /// the living rivals that pass <see cref="LootRules.Eligible"/>, runs <see cref="Filter"/> (the last marked rival is out unless he is the only
    /// rival in the match), weighs each with <see cref="Weight(float, float, in LootWeightTable, float)"/> (the home factor included) and picks with <see cref="Pick"/>.
    /// </summary>
    public static class LootPickRules
    {
        /// <summary>
        /// The last marked rival leaves the list unless he is the only rival in the match (the Death Mark's rule, shared).
        /// <paramref name="rivalsInMatch"/> counts every rival of the match, carriers or not, so a lone carrier is never marked twice running.
        /// </summary>
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

        /// <summary><see cref="Weight(float, float, in LootWeightTable)"/> times the candidate's <see cref="HomeFactor"/>.</summary>
        public static float Weight(float banked, float carried, in LootWeightTable t, float homeFactor) =>
            Weight(banked, carried, t) * (homeFactor > 0f ? homeFactor : 0f);

        /// <summary>
        /// How much a rival's distance from his own base cuts his weight (rule v2, soft): <paramref name="minWeight"/> at or inside the wall
        /// (<paramref name="wallRadius"/>), 1 from <paramref name="homeRadius"/> out, linear between. A <paramref name="homeRadius"/> not above the wall's
        /// switches the rule off (1).
        /// </summary>
        public static float HomeFactor(float distToOwnBase, float wallRadius, float homeRadius, float minWeight)
        {
            if (!(homeRadius > wallRadius)) return 1f;
            float floor = minWeight < 0f ? 0f : minWeight > 1f ? 1f : minWeight;
            float t = (distToOwnBase - wallRadius) / (homeRadius - wallRadius);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return floor + (1f - floor) * t;
        }

        /// <summary>
        /// A light marked bird gives way (rule v2, 4d): among the candidates no farther from the Thief than the marked bird plus
        /// <paramref name="slack"/> metres, one proportional pick by <paramref name="weights"/> (uniform when all those weights are 0). Returns the index
        /// into the two lists, or -1 when no candidate is that close. <paramref name="distances"/> and <paramref name="weights"/> run parallel.
        /// </summary>
        public static int SwitchTarget(IReadOnlyList<float> distances, IReadOnlyList<float> weights, float markedDistance, float slack, float roll)
        {
            float limit = markedDistance + (slack > 0f ? slack : 0f);
            int count = distances.Count;
            float sum = 0f;
            int close = 0, lastClose = -1;
            for (int i = 0; i < count; i++)
            {
                if (distances[i] > limit) continue;
                close++;
                lastClose = i;
                sum += weights[i] > 0f ? weights[i] : 0f;
            }
            if (close == 0) return -1;
            float r = roll < 0f ? 0f : roll;
            if (!(sum > 0f))
            {
                int nth = (int)(r * close);
                if (nth >= close) nth = close - 1;
                for (int i = 0; i < count; i++)
                    if (distances[i] <= limit && nth-- == 0) return i;
                return lastClose;
            }
            float target = r * sum, run = 0f;
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                if (distances[i] > limit || !(weights[i] > 0f)) continue;
                run += weights[i];
                last = i;
                if (run > target) return i;
            }
            return last;
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

        /// <summary>
        /// May this rival be a candidate for the mark (rule v2, section 2: hard filters; the last victim is <see cref="LootPickRules.Filter"/>'s)? He is
        /// not removed, not an ally of the Thief's corner, not a decoy, carries at least <paramref name="minCargo"/> food (and some: a steal needs food),
        /// is not steal-proof (shell), not hidden, and not inside his own base's wall (no ability crosses it, so the mark would end the next tick).
        /// </summary>
        /// <param name="needsFood">False for the stalled draw: any rival that passes the other filters, empty beak included.</param>
        public static bool Eligible(bool removed, bool ally, bool decoy, bool insideOwnBase, float cargo, float minCargo, bool stealProof, bool hidden,
            bool needsFood = true) =>
            !removed && !ally && !decoy && !insideOwnBase && !stealProof && !hidden
            && (!needsFood || (StealRules.Stealable(cargo) > 1e-4f && cargo >= minCargo));

        /// <summary>
        /// A new mark may be drawn (rule v2, section 1): Steal is within the lead of ready (<see cref="DrawDue"/>), the match has run
        /// <paramref name="openingSeconds"/>, <paramref name="redrawGap"/> seconds have passed since the last unpaid end (<c>+Infinity</c> when there was none),
        /// the Thief has free room (a full beak draws nothing), and no marked steal is in flight.
        /// </summary>
        public static bool MayDraw(float stealCooldownLeft, float lead, float matchTime, float openingSeconds, float sinceUnpaidEnd, float redrawGap,
            float freeRoom, bool markedStealPending) =>
            DrawDue(stealCooldownLeft, lead) && matchTime + 1e-4f >= openingSeconds && sinceUnpaidEnd + 1e-4f >= redrawGap
            && freeRoom > 1e-4f && !markedStealPending;

        /// <summary>The marked bird carries some food, but less than <paramref name="keepCargo"/>.</summary>
        public static bool IsLight(float cargo, float keepCargo) => cargo > 1e-4f && cargo < keepCargo;

        /// <summary>
        /// Does a live mark end unpaid for a reason other than its age (rule v2, 4a to 4d, in this order)? <see cref="LootEnd.Gone"/>: removed.
        /// <see cref="LootEnd.Home"/>: he crossed into his own base's wall (unstealable; until then the mark follows him home).
        /// <see cref="LootEnd.Empty"/>: nothing to take. <see cref="LootEnd.Light"/>: little to take (<see cref="IsLight"/>) and either Steal is on
        /// cooldown, or it is ready and <paramref name="closerCandidate"/> says a candidate no farther than he is (plus the slack) exists: the caller
        /// then moves the mark there, or ends it when none is. A light bird with Steal ready and nobody closer keeps the mark. Age is
        /// <see cref="Advance"/>'s, not this function's.
        /// </summary>
        /// <param name="speculative">The mark came from the stalled draw and its bird has not carried anything yet: an empty beak does not end it.</param>
        public static LootEnd Keep(bool gone, bool insideOwnBase, float cargo, float keepCargo, bool stealReady, bool closerCandidate, bool speculative = false)
        {
            if (gone) return LootEnd.Gone;
            if (insideOwnBase) return LootEnd.Home;
            if (!(cargo > 1e-4f)) return speculative ? LootEnd.None : LootEnd.Empty;
            if (IsLight(cargo, keepCargo)) return stealReady && !closerCandidate ? LootEnd.None : LootEnd.Light;
            return LootEnd.None;
        }

        /// <summary>When the next mark is drawn: <paramref name="lead"/> seconds before Steal is ready, at once when that has already passed.</summary>
        public static float DrawAt(float now, float readyAt, float lead) => Math.Max(now, readyAt - Math.Max(0f, lead));

        /// <summary>
        /// The next mark is due: Steal is <paramref name="lead"/> seconds or less from ready (or ready). The same moment <see cref="DrawAt"/> names, read from
        /// the cooldown still left instead of a stored time, so the game follows the Steal slot's own timer as it runs (the simulator stores the time).
        /// </summary>
        public static bool DrawDue(float stealCooldownLeft, float lead) => stealCooldownLeft <= Math.Max(0f, lead) + 1e-4f;

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
