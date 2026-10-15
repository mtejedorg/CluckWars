using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Solo Clear Time simulator. Runs the greedy "farm nearest pile, return when full"
    /// policy the SCT axiom describes and returns seconds + trip count. Pure, deterministic,
    /// no Fusion — spec §7.1. Travel is straight-line (walls are the PlayMode layer's job, §7.2)
    /// but reach-aware: each leg is the centre distance minus the radius the chicken does not
    /// have to walk at either end (see <see cref="OracleMap"/>).
    /// </summary>
    public static class BalanceOracle
    {
        // Guards against a policy bug spinning forever; far above any real trip count.
        private const int MaxIterations = 10_000;
        private const float Epsilon = 1e-4f;

        private const int AtBase = -1;

        /// <summary>Walkable length of a leg between two places; <c>-1</c> is the base, otherwise a pile index.</summary>
        private static float Leg(OracleMap map, int from, int to)
        {
            Vector2 a = from == AtBase ? map.BasePos : map.Piles[from].Pos;
            Vector2 b = to == AtBase ? map.BasePos : map.Piles[to].Pos;
            float centreDistance = Vector2.Distance(a, b);
            return Mathf.Max(0f, centreDistance - Radius(map, from, b - a) - Radius(map, to, a - b));
        }

        /// <summary>Radius around a place that need not be walked: base deposit radius, or a pile's oval toward <paramref name="toward"/> plus peck reach.</summary>
        private static float Radius(OracleMap map, int place, Vector2 toward)
        {
            if (place == AtBase) return map.BaseRadius;
            var pile = map.Piles[place];
            return PileFootprintShape.RadiusToward(toward, pile.Yaw, pile.Footprint) + map.CollectReach;
        }

        public static OracleResult Simulate(OracleMap map, OracleChicken chicken, float winTarget)
        {
            // Mutable copy of pile food so the input map is not modified.
            float[] remaining = new float[map.Piles.Length];
            for (int i = 0; i < map.Piles.Length; i++) remaining[i] = map.Piles[i].Food;

            int at = AtBase;
            float carrying = 0f;
            float banked = 0f;
            float time = 0f;
            int trips = 0;
            int capacity = chicken.CargoCapacity;

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                bool anyFood = false;
                for (int i = 0; i < remaining.Length; i++)
                    if (remaining[i] > Epsilon) { anyFood = true; break; }

                bool full = carrying >= capacity - Epsilon || carrying >= (winTarget - banked) - Epsilon;

                // Decide: deposit, or collect.
                if (full || (!anyFood && carrying > Epsilon))
                {
                    // ---- return to base and deposit ----
                    time += Leg(map, at, AtBase) / chicken.MoveSpeed;
                    at = AtBase;

                    float needed = winTarget - banked;
                    if (carrying >= needed - Epsilon)
                    {
                        // Win crosses mid-deposit — only count the time to bank `needed`.
                        time += needed / chicken.DepositRate;
                        banked = winTarget;
                        trips++;
                        return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = true };
                    }

                    time += carrying / chicken.DepositRate;
                    banked += carrying;
                    carrying = 0f;
                    trips++;
                    continue;
                }

                if (anyFood)
                {
                    // ---- walk to the nearest pile (centre-to-centre) that still has food, collect ----
                    int nearest = -1;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < remaining.Length; i++)
                    {
                        if (remaining[i] <= Epsilon) continue;
                        float d = Vector2.Distance(at == AtBase ? map.BasePos : map.Piles[at].Pos, map.Piles[i].Pos);
                        if (d < bestDist) { bestDist = d; nearest = i; }
                    }

                    time += Leg(map, at, nearest) / chicken.MoveSpeed;
                    at = nearest;

                    float space = capacity - carrying;
                    float take = Mathf.Min(space, remaining[nearest]);

                    // Discrete, not continuous: food comes out of a pile one Peck press at a
                    // time. A press that only partly fills (the pile ran dry, or the chicken
                    // ran out of room) still costs a full cooldown, so the count is a CEILING.
                    // The epsilon stops an exact multiple — take 6, amount 3 — from rounding
                    // up to 3 presses on a float that landed a hair above 2.0.
                    // Both halves reject zero: a zero cooldown would make collection free
                    // (pecks * 0), i.e. infinite foraging rate, which is no more valid than
                    // a zero amount that takes no food.
                    if (chicken.PeckAmount <= 0f || chicken.PeckCooldown <= 0f)
                        return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = false };

                    int pecks = Mathf.Max(1, Mathf.CeilToInt(take / chicken.PeckAmount - Epsilon));
                    time += pecks * chicken.PeckCooldown;

                    carrying += take;
                    remaining[nearest] -= take;
                    continue;
                }

                // No food left and nothing to deposit — target is unreachable.
                return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = false };
            }

            // Iteration guard tripped — treat as unreachable rather than hang.
            return new OracleResult { SctSeconds = time, Trips = trips, ReachedTarget = false };
        }
    }
}
