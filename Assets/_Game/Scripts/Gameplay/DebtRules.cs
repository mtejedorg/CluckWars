using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Enforcer's per-enemy debts as numbers (design 2026-11-25, redesign): each landed Stun, Shove or bite is a debt on that rival, and each
    /// debt inflates the Enforcer's Steal on that rival by <see cref="Steal"/> food. A <see cref="Steal"/> of 0 adds nothing (the shipped value);
    /// <see cref="IsEnforcer"/> is true whatever the numbers, so a report can find the kit.
    /// </summary>
    public struct DebtFacts
    {
        public bool IsEnforcer;
        /// <summary>Food one debt adds to the Steal on the rival it counts on.</summary>
        public float Steal;
        /// <summary>Most debts held on one rival.</summary>
        public int Cap;
        /// <summary>Seconds without a gain on a rival after which its count goes to 0, all at once.</summary>
        public float Seconds;

        public static DebtFacts None => default;
    }

    /// <summary>The pure debt rules, shared by the match simulator and, later, the game.</summary>
    public static class DebtRules
    {
        /// <summary>One more debt, stopping at the cap (a cap of 0 or less earns none).</summary>
        public static int Add(int held, int cap) => held < cap ? held + 1 : Mathf.Max(held, 0);

        /// <summary>A Steal's amount on a rival holding <paramref name="count"/> debts: the nominal amount times the thief's factor plus the debts' food.</summary>
        public static float StealAmount(float stealAmount, float stealFactor, float perDebt, int count) =>
            stealAmount * stealFactor + perDebt * Mathf.Max(count, 0);

        /// <summary>True once <paramref name="now"/> is past <paramref name="until"/>.</summary>
        public static bool Decayed(float now, float until) => now > until;
    }
}
