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
    public struct OracleChicken
    {
        public float MoveSpeed;      // world units / second
        public int   CargoCapacity;  // units carried before a forced return
        public float CollectionRate; // units / second while on a pile
        public float DepositRate;    // units / second while at base
    }

    /// <summary>Simulation output: Solo Clear Time, trip count, and whether the target was reachable.</summary>
    public struct OracleResult
    {
        public float SctSeconds;
        public int   Trips;
        public bool  ReachedTarget;
    }
}
