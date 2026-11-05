using CluckWars.Abilities;

namespace CluckWars.Balance
{
    /// <summary>
    /// A specialization whose farming run is <b>allowed</b> to miss its class's SCT target,
    /// with the number it lands on instead and the reason that is acceptable.
    /// </summary>
    public struct SpecSctTarget
    {
        public ChickenSubclass Subclass;
        public float           Seconds;
        public int             Trips;

        /// <summary>
        /// Cargo capacity (class stat through the passive's hook) the entry was measured at. The
        /// Oracle is not monotonic in cargo (Warrior caps 16, 17 and 18 all give 38.77 s / 3), so
        /// the seconds alone would let a different bonus pass silently; the test pins this too.
        /// </summary>
        public int             CargoCapacity;

        public string          Reason;
    }

    /// <summary>
    /// Declared exceptions to the SCT axiom for class specializations (decision A8, 2026-10-21).
    /// </summary>
    /// <remarks>
    /// <see cref="SctTargets"/> governs a <i>naked</i> chicken. A passive that changes cargo
    /// capacity, deposit rate or the Peck cooldown changes the farming run too, and before
    /// this table that change sat outside every test (+6 Bully cargo made a Warrior farm in
    /// 30.9 s / 2 trips and nothing noticed). <c>BalanceOracleTests</c> now runs the Oracle
    /// for every shipped passive through its real hooks: a passive with no entry here must
    /// land within <see cref="SctTargets.ShippedAssetToleranceSeconds"/> of its class target;
    /// a passive WITH an entry must land within the same band of the entry instead. So a
    /// passive that drifts goes red either way, and an entry that no longer describes its
    /// passive goes red too (remove it).
    /// <para>
    /// The numbers are what the real Oracle produces on the shipped map (all four seats agree);
    /// they are not goals to solve stats against. Changing one needs Maestro's approval, the
    /// same as any balance number.
    /// </para>
    /// </remarks>
    public static class SpecSctTargets
    {
        public static readonly SpecSctTarget[] All =
        {
            new SpecSctTarget
            {
                Subclass = ChickenSubclass.Fatty_Hoarder, Seconds = 25.82f, Trips = 1, CargoCapacity = 40,
                Reason = "Designed: cargo floor 40 = the win target, so a perfect run is one trip " +
                         "(Fatty signature fantasy). 4.2 s faster than the naked Fatty by intent.",
            },
            // Warrior_Bully's entry (38.77 s / 3 at cap 16, its old +2 cargo) was removed 2026-11-05: the reworked
            // Bully (Maestro) changes no cargo, deposit or Peck cadence, so it farms as the naked Warrior again.
        };

        /// <summary>The declared entry for <paramref name="subclass"/>, if there is one.</summary>
        public static bool TryGet(ChickenSubclass subclass, out SpecSctTarget target)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Subclass != subclass) continue;
                target = All[i];
                return true;
            }
            target = default;
            return false;
        }

        /// <summary>
        /// Same rule as <see cref="SctTargets.Within"/>: target reached, trip count exact,
        /// seconds within <paramref name="toleranceSeconds"/>.
        /// </summary>
        public static bool Within(OracleResult result, SpecSctTarget target, float toleranceSeconds) =>
            SctTargets.Within(result,
                new SctTarget { ClassName = target.Subclass.ToString(), Seconds = target.Seconds, Trips = target.Trips },
                toleranceSeconds);
    }
}
