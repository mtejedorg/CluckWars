using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Champion's per-enemy trophies as numbers (design 2026-11-25, redesign): each landed Stun, Shove or bite is a trophy on that rival, and each
    /// trophy inflates the Champion's Steal on that rival by <see cref="Steal"/> food. A <see cref="Steal"/> of 0 adds nothing (the shipped value);
    /// <see cref="IsChampion"/> is true whatever the numbers, so a report can find the kit.
    /// </summary>
    public struct TrophyFacts
    {
        public bool IsChampion;
        /// <summary>Food one trophy adds to the Steal on the rival it counts on.</summary>
        public float Steal;
        /// <summary>Most trophies held on one rival.</summary>
        public int Cap;
        /// <summary>Seconds without a gain on a rival after which its count goes to 0, all at once.</summary>
        public float Seconds;

        public static TrophyFacts None => default;
    }

    /// <summary>The pure trophy rules, shared by the match simulator and, later, the game.</summary>
    public static class TrophyRules
    {
        /// <summary>One more trophy, stopping at the cap (a cap of 0 or less earns none).</summary>
        public static int Add(int held, int cap) => held < cap ? held + 1 : Mathf.Max(held, 0);

        /// <summary>A Steal's amount on a rival holding <paramref name="count"/> trophies: the nominal amount times the thief's factor plus the trophies' food.</summary>
        public static float StealAmount(float stealAmount, float stealFactor, float perTrophy, int count) =>
            stealAmount * stealFactor + perTrophy * Mathf.Max(count, 0);

        /// <summary>True once <paramref name="now"/> is past <paramref name="until"/>.</summary>
        public static bool Decayed(float now, float until) => now > until;
    }
}
