using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>A pile the model can farm: world position and remaining food.</summary>
    public struct OraclePile
    {
        public Vector2 Pos;
        public float Food;
    }

    /// <summary>The full map the Oracle simulates: one base + the piles to farm.</summary>
    public struct OracleMap
    {
        public Vector2 BasePos;
        public OraclePile[] Piles;
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
