using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Solo Clear Time simulator. Runs the greedy "farm nearest pile, return when full"
    /// policy the SCT axiom describes and returns seconds + trip count. Pure, deterministic,
    /// no Fusion — spec §7.1. Travel is straight-line (walls are the PlayMode layer's job, §7.2).
    /// </summary>
    public static class BalanceOracle
    {
        // Guards against a policy bug spinning forever; far above any real trip count.
        private const int MaxIterations = 10_000;
        private const float Epsilon = 1e-4f;

        public static OracleResult Simulate(OracleMap map, OracleChicken chicken, float winTarget)
        {
            // Mutable copy of pile food so the input map is not modified.
            float[] remaining = new float[map.Piles.Length];
            for (int i = 0; i < map.Piles.Length; i++) remaining[i] = map.Piles[i].Food;

            Vector2 pos = map.BasePos;
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
                    time += Vector2.Distance(pos, map.BasePos) / chicken.MoveSpeed;
                    pos = map.BasePos;

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
                    // ---- walk to the nearest pile that still has food, collect ----
                    int nearest = -1;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < remaining.Length; i++)
                    {
                        if (remaining[i] <= Epsilon) continue;
                        float d = Vector2.Distance(pos, map.Piles[i].Pos);
                        if (d < bestDist) { bestDist = d; nearest = i; }
                    }

                    time += bestDist / chicken.MoveSpeed;
                    pos = map.Piles[nearest].Pos;

                    float space = capacity - carrying;
                    float take = Mathf.Min(space, remaining[nearest]);

                    // Discrete, not continuous: food comes out of a pile one Peck press at a
                    // time. A press that only partly fills (the pile ran dry, or the chicken
                    // ran out of room) still costs a full cooldown, so the count is a CEILING.
                    // The epsilon stops an exact multiple — take 6, amount 3 — from rounding
                    // up to 3 presses on a float that landed a hair above 2.0.
                    if (chicken.PeckAmount <= 0f || chicken.PeckCooldown < 0f)
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
