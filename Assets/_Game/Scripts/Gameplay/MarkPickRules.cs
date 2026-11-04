using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Who a Death Mark lands on, pure, with every roll injected. The caller lists the living, non-decoy rivals; <see cref="Filter"/>
    /// takes the last mark out of them (unless it is the only rival in the match; there is no time release); each survivor scores
    /// <c>roll * w * w * a</c>, where <c>w</c> is how far it is from its own base (<see cref="HomeFraction"/>) and <c>a</c> how far
    /// it is from the Assassin (<see cref="AssassinFraction"/>), and the highest score wins. A bird at home scores 0 and a bird
    /// far from home is favoured; a bird right next to the Assassin is disfavoured but never excluded (<see cref="NearFloor"/>),
    /// so anyone can still be picked and hugging the Assassin is no hiding place.
    /// </summary>
    public static class MarkPickRules
    {
        /// <summary>The Assassin factor of a rival standing on top of the Assassin: a point-blank mark stays possible (a quarter
        /// of the weight of a far one), so nobody is unmarkable by hugging him.</summary>
        public const float NearFloor = 0.25f;

        /// <summary>The share of the base span (<c>PlayerBase.BaseSpan</c>) at which the Assassin factor saturates to 1: a rival
        /// this far from the Assassin or farther weighs as much as any other.</summary>
        public const float NearSpanFraction = 1f / 3f;

        /// <summary>Removes the last mark from <paramref name="candidates"/> unless it is the only rival in the match. This is the
        /// "recent" factor of the score (0 for the last mark, 1 for everyone else), applied by removal.</summary>
        /// <param name="rivalsInMatch">Every real rival, including one out of play for the moment.</param>
        public static void Filter<T>(List<T> candidates, bool hasLast, T last, int rivalsInMatch) where T : struct, IEquatable<T>
        {
            if (!hasLast || rivalsInMatch <= 1) return;
            for (int i = candidates.Count - 1; i >= 0; i--)
                if (candidates[i].Equals(last)) candidates.RemoveAt(i);
        }

        /// <summary>How far a chicken is from its own base, 0 (on it) to 1 (a base span away or more).
        /// <paramref name="baseSpan"/> not above 0 (no base data) gives 1, so every candidate weighs the same and the pick is uniform.</summary>
        public static float HomeFraction(float distanceToOwnBase, float baseSpan) =>
            baseSpan > 0f ? Clamp01(distanceToOwnBase / baseSpan) : 1f;

        /// <summary>How far a candidate is from the Assassin, as a factor: <see cref="NearFloor"/> on top of him, rising linearly
        /// to 1 at <see cref="NearSpanFraction"/> of <paramref name="baseSpan"/> and beyond. <paramref name="baseSpan"/> not above 0
        /// (no base data) gives 1, so the pick stays uniform.</summary>
        public static float AssassinFraction(float distanceToAssassin, float baseSpan)
        {
            if (!(baseSpan > 0f)) return 1f;
            float n = Clamp01(distanceToAssassin / (baseSpan * NearSpanFraction));
            return NearFloor + (1f - NearFloor) * n;
        }

        /// <summary>The score of one filtered candidate: its own roll in [0, 1) times <paramref name="homeFraction"/> squared
        /// times <paramref name="assassinFraction"/>.</summary>
        public static float Score(float roll, float homeFraction, float assassinFraction) =>
            roll * homeFraction * homeFraction * assassinFraction;

        /// <summary>
        /// The index of the highest <see cref="Score"/> over the filtered candidates (first wins a tie); -1 when there are none.
        /// When every score is 0 (everyone at home, or every roll 0) a uniform pick by <paramref name="fallbackRoll"/> in [0, 1),
        /// so a mark always lands when a candidate exists.
        /// </summary>
        /// <param name="homeFractions">One <see cref="HomeFraction"/> per candidate.</param>
        /// <param name="assassinFractions">One <see cref="AssassinFraction"/> per candidate.</param>
        /// <param name="rolls">One roll per candidate, from the authority's RNG.</param>
        public static int PickWeighted(IReadOnlyList<float> homeFractions, IReadOnlyList<float> assassinFractions,
            IReadOnlyList<float> rolls, float fallbackRoll)
        {
            int count = homeFractions.Count;
            if (count <= 0) return -1;
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < count; i++)
            {
                float score = Score(rolls[i], homeFractions[i], assassinFractions[i]);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : Pick(count, fallbackRoll);
        }

        /// <summary>An index into the filtered candidates for a roll in [0, 1); -1 when there is nobody.</summary>
        public static int Pick(int count, float roll)
        {
            if (count <= 0) return -1;
            int i = (int)(roll * count);
            return i < 0 ? 0 : i >= count ? count - 1 : i;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
