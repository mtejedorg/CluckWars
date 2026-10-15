using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>A pile the model can farm: world position, remaining food and (optionally) its oval footprint.</summary>
    public struct OraclePile
    {
        public Vector2 Pos;
        public float Food;

        /// <summary>Oval size in metres (X = first/long axis, Y = second), as <c>FoodPile</c> authors it. Zero = a point (centre-to-centre travel).</summary>
        public Vector2 Footprint;

        /// <summary>Yaw of the oval in degrees (<c>PileFootprintShape</c> convention).</summary>
        public float Yaw;
    }

    /// <summary>The full map the Oracle simulates: one base + the piles to farm.</summary>
    /// <remarks>
    /// <b>Reach-aware travel.</b> A chicken does not walk to a pile's centre: it stops at the
    /// rim and pecks from within <see cref="CollectReach"/> of it, and deposits from inside the
    /// base's <see cref="BaseRadius"/>. Each trip leg is therefore
    /// <c>max(0, centreDistance - r_from - r_to)</c>, where a pile's radius is its oval's radius
    /// toward the other end plus <see cref="CollectReach"/>. Both new fields default to 0, which
    /// (with zero footprints) is the plain centre-to-centre model.
    /// </remarks>
    public struct OracleMap
    {
        public Vector2 BasePos;
        public OraclePile[] Piles;

        /// <summary>Deposit radius around <see cref="BasePos"/> (<c>PlayerBase._depositRadius</c>).</summary>
        public float BaseRadius;

        /// <summary>How far past a pile's rim a chicken can still peck (<c>FoodPile._collectReach</c>).</summary>
        public float CollectReach;
    }

    /// <summary>The naked stats the SCT axiom is solved against (no abilities, no passives).</summary>
    /// <remarks>
    /// Collection is <b>discrete</b>, not a rate. It used to be a single
    /// <c>CollectionRate</c> in units/second, which was correct while food drained
    /// automatically for as long as you stood near a pile. Food is now taken by pressing
    /// Peck, so the cost of filling up is <c>ceil(food / PeckAmount) * PeckCooldown</c> — and
    /// the ceiling matters: topping up 1 food from a nearly-empty pile still costs a whole
    /// cooldown.
    /// <para>
    /// Because of that rounding waste, the nominal rate <c>PeckAmount / PeckCooldown</c> lands
    /// roughly 20–30% <i>above</i> the old <c>CollectionRate</c> it replaces for the same SCT.
    /// That is expected, not a buff — do not "fix" it by matching the two numbers.
    /// </para>
    /// </remarks>
    public struct OracleChicken
    {
        public float MoveSpeed;     // world units / second
        public int   CargoCapacity; // units carried before a forced return
        public float PeckAmount;    // food gained per Peck press
        public float PeckCooldown;  // seconds between Peck presses
        public float DepositRate;   // units / second while at base
    }

    /// <summary>Simulation output: Solo Clear Time, trip count, and whether the target was reachable.</summary>
    public struct OracleResult
    {
        public float SctSeconds;
        public int   Trips;
        public bool  ReachedTarget;
    }
}
