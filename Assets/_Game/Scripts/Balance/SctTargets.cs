namespace CluckWars.Balance
{
    /// <summary>One class's Solo Clear Time goalpost (spec §1).</summary>
    public struct SctTarget
    {
        public string ClassName;
        public float  Seconds;
        public int    Trips;
    }

    /// <summary>
    /// The SCT axiom's goalposts and the tolerance check. Change these four numbers to
    /// re-solve the whole game (spec §1 / §7.1). The Oracle asserts each class's naked
    /// stats land Within() its target.
    /// </summary>
    public static class SctTargets
    {
        /// <summary>
        /// The GDD <b>design band</b> (§2.1): how close a design must land to its SCT target.
        /// Used by <see cref="Within"/> by default and by the pure-oracle unit tests.
        /// </summary>
        public const float DefaultToleranceSeconds = 1.5f;

        /// <summary>
        /// The <b>shipped-asset drift guard</b>: the tolerance for the test that runs the real
        /// <c>.asset</c> values on the real map from all four seats. The shipped numbers are
        /// solved to land within ~0.05 s of target, so the wide design band only hides drift:
        /// at ±1.5 s a +5 % <c>_baseCornerDistance</c> (piles move with the base) or collect
        /// reach dropping to 0 stayed green. Must stay strictly below
        /// <see cref="DefaultToleranceSeconds"/>. If the shipped assets drift past it, re-solve
        /// the stats; do not widen this.
        /// </summary>
        public const float ShippedAssetToleranceSeconds = 0.5f;

        public static readonly SctTarget[] All =
        {
            new SctTarget { ClassName = "Speedy",   Seconds = 30f, Trips = 4 },
            new SctTarget { ClassName = "Fatty",    Seconds = 30f, Trips = 2 },
            new SctTarget { ClassName = "Warrior",  Seconds = 35f, Trips = 3 },
        };

        /// <summary>
        /// Classes the SCT axiom does <b>not</b> govern, and why.
        /// </summary>
        /// <remarks>
        /// The axiom is "a naked chicken, alone on the full map, farming greedily, banks the
        /// win target". The Assassin cannot forage at all
        /// (<c>PreEquippedLoadout.ClassMayForage</c> is false for it; a test pins that this list and that rule agree) — so there is no farming run to measure and its old 40 s / 4-trip
        /// target described something that can no longer happen. It is governed instead by
        /// the Predation axiom (see the 2026-08-13 Peck/four-slot spec §7.2): given rivals
        /// carrying C food every T seconds, an Assassin banks the win target in X, where each
        /// execute yields C plus a flat execute bounty.
        /// <para>
        /// This is a list rather than a bool on <see cref="SctTarget"/> so that a class is
        /// either governed or explicitly excused — never silently absent from both, which is
        /// how a class ends up with no balance guard at all.
        /// </para>
        /// </remarks>
        public static readonly string[] Exempt = { "Assassin" };

        /// <summary>True when <paramref name="className"/> is excused from the SCT axiom.</summary>
        public static bool IsExempt(string className)
        {
            for (int i = 0; i < Exempt.Length; i++)
                if (Exempt[i] == className) return true;
            return false;
        }

        /// <summary>True iff the result reached the target, matched the trip count exactly,
        /// and landed within <paramref name="toleranceSeconds"/> of the target time.</summary>
        public static bool Within(OracleResult result, SctTarget target,
            float toleranceSeconds = DefaultToleranceSeconds)
        {
            if (!result.ReachedTarget) return false;
            if (result.Trips != target.Trips) return false;
            return UnityEngine.Mathf.Abs(result.SctSeconds - target.Seconds) <= toleranceSeconds;
        }
    }
}
