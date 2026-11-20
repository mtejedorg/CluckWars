using System;
using System.Collections.Generic;
using CluckWars.Abilities;

namespace CluckWars.Balance
{
    /// <summary>Everything the Spoiler table reads, already resolved from the (possibly previewed) assets.</summary>
    public struct SpoilerTableInputs
    {
        public float ExecuteBounty;
        public float PassiveBonus;
        public float ClockSeconds;
        public float WinTarget;
        public float RemovalSeconds;
        /// <summary>The standard farmers' mean food per trip, and their carrying capacity: the two non-zero cargo levels.</summary>
        public float MeanCargo;
        public float Capacity;
        /// <summary>The three standard farmers' banked food (goal off, no event): the chickens the Spoiler hunts.</summary>
        public float[] FarmerBanked;
        public int MaxKills;
    }

    /// <summary>Everything the Thief table reads, already resolved from the (possibly previewed) assets.</summary>
    public struct ThiefTableInputs
    {
        public float StealAmount;
        public float StealMultiplier;
        public float StealCooldown;
        public float CargoCapacity;
        public float MoveSpeed;
        public float DepositRate;
        /// <summary>Mean walk from a base to the two contested piles next to it, in metres.</summary>
        public float MeanBaseToContestedLeg;
        public float ClockSeconds;
        public float WinTarget;
        public float MeanCargo;
        public float Capacity;
    }

    /// <summary>
    /// The arithmetic tables of the two Sneaky kits (spec 5.3 and 8b): both kits never forage, so their number is not a
    /// farming run but a count of kills or steals against what the standard farmers carry and bank. Pure.
    /// </summary>
    public static class SneakyTables
    {
        /// <summary>The victim cargo levels the tables use: nothing, the farmers' mean load, a full load.</summary>
        public static float[] CargoLevels(float meanCargo, float capacity) => new[] { 0f, meanCargo, capacity };

        /// <summary>
        /// Most executions one round allows: the first press when the round-start cooldown ends, each kill lands at the end
        /// of the window, the next press comes one success cooldown later, and a kill counts only if it lands before the clock ends.
        /// </summary>
        public static int MaxSpoilerKills(float roundStartCooldown, float windowSeconds, float successCooldown, float clockSeconds)
        {
            float window = Math.Max(windowSeconds, 0.1f);
            float press = roundStartCooldown;
            int kills = 0;
            while (press + window < clockSeconds)
            {
                kills++;
                press += window + successCooldown;
            }
            return kills;
        }

        /// <summary>
        /// Rows for kills 0..<see cref="SpoilerTableInputs.MaxKills"/> at each cargo level, level by level. Kills spread over
        /// the farmers best first (kill 1 on the best, 2 on the second, 3 on the third, 4 on the best again); each victim
        /// loses the cargo plus its own income for the removal seconds. <c>LeaderFinal</c> is the best farmer LEFT after those
        /// losses, which is what the Spoiler has to beat.
        /// </summary>
        public static List<SpoilerRow> Spoiler(SpoilerTableInputs t)
        {
            if (t.FarmerBanked == null || t.FarmerBanked.Length == 0)
                throw new ArgumentException("The Spoiler table needs the farmers' banked food.", nameof(t));
            var rows = new List<SpoilerRow>();
            int farmers = t.FarmerBanked.Length;
            var order = new int[farmers];
            for (int i = 0; i < farmers; i++) order[i] = i;
            var banks = t.FarmerBanked;
            Array.Sort(order, (x, y) => banks[y].CompareTo(banks[x]));

            foreach (float cargo in CargoLevels(t.MeanCargo, t.Capacity))
            {
                for (int kills = 0; kills <= t.MaxKills; kills++)
                {
                    float final = kills * t.ExecuteBounty + kills * cargo;
                    float bestLeft = 0f;
                    for (int rank = 0; rank < farmers; rank++)
                    {
                        // Kills that land on the farmer of this rank: kill numbers rank+1, rank+1+farmers, ...
                        int hits = kills > rank ? (kills - rank - 1) / farmers + 1 : 0;
                        float banked = banks[order[rank]];
                        float income = t.ClockSeconds > 0f ? banked / t.ClockSeconds : 0f;
                        float left = Math.Max(0f, banked - hits * (cargo + income * t.RemovalSeconds));
                        if (left > bestLeft) bestLeft = left;
                    }
                    float withPassive = final + t.PassiveBonus;
                    rows.Add(new SpoilerRow
                    {
                        Kills = kills,
                        CargoPerKill = cargo,
                        Final = final,
                        FinalWithPassive = withPassive,
                        LeaderFinal = bestLeft,
                        Wins = final > bestLeft,
                        // The passive pays only when the clock runs out with nobody at the goal.
                        WinsWithPassive = withPassive > bestLeft && bestLeft < t.WinTarget,
                    });
                }
            }
            return rows;
        }

        /// <summary>
        /// Rows for steals 1..N (as many as the clock allows at the steal cooldown) at each cargo level, level by level.
        /// The game caps a steal request at the thief's free cargo room (<c>FoodTransferRules.Request</c>); the bounty bag
        /// takes only overflow that arrives unasked and is not counted against capacity. So a steal takes
        /// min(amount, victim cargo, free room), and the thief goes home to bank when it cannot take a full steal. Every trip
        /// is a round trip between a contested pile and its base; the load still held at the end is banked on one last trip.
        /// </summary>
        public static List<ThiefRow> Thief(ThiefTableInputs t)
        {
            var rows = new List<ThiefRow>();
            float cooldown = Math.Max(t.StealCooldown, 0.1f);
            int maxSteals = Math.Max(1, (int)Math.Floor(t.ClockSeconds / cooldown));
            float speed = Math.Max(t.MoveSpeed, 0.01f);
            float deposit = Math.Max(t.DepositRate, 0.01f);
            float capacity = Math.Max(t.CargoCapacity, 1f);
            float perSteal = t.StealAmount * t.StealMultiplier;
            float roundTrip = 2f * t.MeanBaseToContestedLeg / speed;

            foreach (float cargo in CargoLevels(t.MeanCargo, t.Capacity))
            {
                float full = Math.Min(perSteal, cargo);
                float held = 0f, banked = 0f;
                int trips = 0;
                for (int steals = 1; steals <= maxSteals; steals++)
                {
                    if (held > 0f && capacity - held < full - 1e-4f)
                    {
                        banked += held;
                        held = 0f;
                        trips++;
                    }
                    held += Math.Min(full, capacity - held);

                    float total = banked + held;
                    int allTrips = trips + (held > 0f ? 1 : 0);
                    float seconds = steals * cooldown + allTrips * roundTrip + total / deposit;
                    rows.Add(new ThiefRow
                    {
                        Steals = steals,
                        VictimCargo = cargo,
                        Banked = total,
                        Trips = allTrips,
                        SecondsNeeded = seconds,
                        FitsTheClock = seconds <= t.ClockSeconds,
                    });
                }
            }
            return rows;
        }
    }
}
