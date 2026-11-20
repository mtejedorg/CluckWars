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
        /// <summary>The best standard farmer's banked food in a four-standard-farmer run.</summary>
        public float LeaderBanked;
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
        public static int MaxSpoilerKills(MarkKillAbilitySO mark, float clockSeconds)
        {
            if (mark == null) throw new ArgumentNullException(nameof(mark));
            float window = Math.Max(mark.WindowSeconds, 0.1f);
            float press = mark.RoundStartCooldownSeconds;
            int kills = 0;
            while (press + window < clockSeconds)
            {
                kills++;
                press += window + mark.SuccessCooldownSeconds;
            }
            return kills;
        }

        /// <summary>Rows for kills 0..<see cref="SpoilerTableInputs.MaxKills"/> at each cargo level, level by level.</summary>
        public static List<SpoilerRow> Spoiler(SpoilerTableInputs t)
        {
            var rows = new List<SpoilerRow>();
            float incomePerSecond = t.ClockSeconds > 0f ? t.LeaderBanked / t.ClockSeconds : 0f;
            foreach (float cargo in CargoLevels(t.MeanCargo, t.Capacity))
            {
                for (int kills = 0; kills <= t.MaxKills; kills++)
                {
                    float final = kills * t.ExecuteBounty + kills * cargo;
                    // Kills spread evenly over the three farmers, the leader first: it takes kills 1, 4, 7 ...
                    int onLeader = (kills + 2) / 3;
                    float leader = Math.Max(0f, t.LeaderBanked - onLeader * (cargo + incomePerSecond * t.RemovalSeconds));
                    float withPassive = final + t.PassiveBonus;
                    rows.Add(new SpoilerRow
                    {
                        Kills = kills,
                        CargoPerKill = cargo,
                        Final = final,
                        FinalWithPassive = withPassive,
                        LeaderFinal = leader,
                        Wins = final > leader,
                        // The passive pays only when the clock runs out with nobody at the goal.
                        WinsWithPassive = withPassive > leader && leader < t.WinTarget,
                    });
                }
            }
            return rows;
        }

        /// <summary>Rows for steals 1..N (as many as the clock allows at the steal cooldown) at each cargo level, level by level.</summary>
        public static List<ThiefRow> Thief(ThiefTableInputs t)
        {
            var rows = new List<ThiefRow>();
            float cooldown = Math.Max(t.StealCooldown, 0.1f);
            int maxSteals = Math.Max(1, (int)Math.Floor(t.ClockSeconds / cooldown));
            float speed = Math.Max(t.MoveSpeed, 0.01f);
            float deposit = Math.Max(t.DepositRate, 0.01f);
            float capacity = Math.Max(t.CargoCapacity, 1f);
            float perSteal = t.StealAmount * t.StealMultiplier;
            float walkHome = t.MeanBaseToContestedLeg / speed;

            foreach (float cargo in CargoLevels(t.MeanCargo, t.Capacity))
            {
                float each = Math.Min(perSteal, cargo);
                for (int steals = 1; steals <= maxSteals; steals++)
                {
                    float banked = steals * each;
                    int trips = (int)Math.Ceiling(banked / capacity);
                    float seconds = steals * cooldown + trips * walkHome + banked / deposit;
                    rows.Add(new ThiefRow
                    {
                        Steals = steals,
                        VictimCargo = cargo,
                        Banked = banked,
                        SecondsNeeded = seconds,
                        FitsTheClock = seconds <= t.ClockSeconds,
                    });
                }
            }
            return rows;
        }
    }
}
