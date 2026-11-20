using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// Turns farming runs into the oracle report (spec 5.3, 5.4, 5.7, 8b): economy per kit, plus the Spoiler and Thief
    /// tables. Reports; never blocks. Reads every number from the assets (through <see cref="BalanceBook.Preview{T}"/> when
    /// <see cref="OracleInputs.Verdict"/> is set). Pure: no scene, no Fusion, no logging.
    /// </summary>
    public static class OracleRunner
    {
        public static readonly FarmRoute[] Routes =
            { FarmRoute.Nearest, FarmRoute.SidesFirst, FarmRoute.CentreFirst, FarmRoute.EasySideThenCentre };

        private const int Seats = OracleMapSO.SeatCount;

        public static OracleReport Run(OracleInputs inputs, IReadOnlyDictionary<string, float> offsets)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Map == null || inputs.Abilities == null || inputs.Classes == null || inputs.Match == null)
                throw new ArgumentException("OracleInputs needs Map, Abilities, Classes and Match.", nameof(inputs));

            using (var scope = new Previews(inputs))
            {
                var match = scope.Of(inputs.Match);
                var kits = OracleKits.All(inputs);
                var standard = OracleKits.StandardFarmer(inputs);
                var food = OracleKits.Food(inputs);

                var report = new OracleReport
                {
                    MapFingerprint = inputs.Map.SceneFingerprint,
                    BalanceHash = inputs.Verdict != null ? inputs.Verdict.Hash : BalanceBook.BaselineHash,
                    Food = food,
                    ClockSeconds = match.MatchDurationSeconds,
                    WinTarget = match.FoodTargetToWin,
                };

                report.Economy = Economy(inputs, kits, standard, food, FarmFoodEvent.None, offsets);
                report.EconomyGoldenPile = Economy(inputs, kits, standard, food, FarmFoodEvent.GoldenPile, offsets);
                report.EconomyRestock = Economy(inputs, kits, standard, food, FarmFoodEvent.Restock, offsets);

                var farmers = StandardFarmers(inputs, standard, food);
                report.StandardCapacity = standard.CargoCapacity;
                report.StandardMeanCargo = farmers.MeanCargo;
                report.StandardLeaderBanked = farmers.LeaderBanked;
                report.StandardFarmerBanked = farmers.FarmerBanked;
                report.StandardSeatBanked = farmers.SeatBanked;
                report.StandardPilesEmptyAt = farmers.PilesEmptyAt;

                BuildSpoiler(report, inputs, scope, match, farmers);
                BuildThief(report, inputs, scope, match, kits, farmers);
                return report;
            }
        }

        // ------------------------------------------------------------- economy

        private static List<KitLine> Economy(OracleInputs inputs, OracleKit[] kits, OracleKit standard, OracleFood food,
            FarmFoodEvent foodEvent, IReadOnlyDictionary<string, float> offsets)
        {
            var open = OracleKits.Rules(inputs, false, foodEvent);
            var stop = OracleKits.Rules(inputs, true, foodEvent);
            var lines = new List<KitLine>(kits.Length);
            foreach (var kit in kits)
            {
                var line = new KitLine { Label = kit.Label, Applicable = kit.Forages };
                if (kit.Forages) Fill(line, inputs.Map, kit, standard, food, open, stop, offsets);
                lines.Add(line);
            }
            return lines;
        }

        private static void Fill(KitLine line, OracleMapSO map, OracleKit kit, OracleKit standard, OracleFood food,
            FarmRules open, FarmRules stop, IReadOnlyDictionary<string, float> offsets)
        {
            int routes = Routes.Length;
            int cells = Seats * routes;
            var banked = new float[cells];
            var seconds = new float[cells];
            var reached = new bool[cells];

            for (int seat = 0; seat < Seats; seat++)
            {
                for (int r = 0; r < routes; r++)
                {
                    int cell = seat * routes + r;
                    var seats = OthersStandard(standard);
                    seats[seat] = new FarmSeat { Present = true, Kit = kit, Route = Routes[r] };
                    banked[cell] = FarmSim.Run(map, food, seats, open)[seat].Banked;
                    var toGoal = FarmSim.Run(map, food, seats, stop)[seat];
                    reached[cell] = toGoal.SecondsToTarget >= 0f;
                    seconds[cell] = GoalSeconds(toGoal, stop.WinTarget, stop.ClockSeconds);
                }
            }

            // Per route: the seat mean of seconds (a kit cannot choose its seat). A route on which some seat banks nothing has no number.
            // The best route is the fastest; a tie goes to the route more seats really finish on.
            int bestRoute = -1, bestReaching = 0;
            float bestSeconds = 0f, slowest = float.NegativeInfinity;
            for (int r = 0; r < routes; r++)
            {
                float sum = 0f;
                int n = 0;
                for (int seat = 0; seat < Seats; seat++)
                {
                    sum += seconds[seat * routes + r];
                    if (reached[seat * routes + r]) n++;
                }
                float mean = sum / Seats;
                if (float.IsNaN(mean)) continue;
                if (mean > slowest) slowest = mean;
                if (bestRoute < 0 || mean < bestSeconds || (mean == bestSeconds && n > bestReaching))
                {
                    bestRoute = r;
                    bestSeconds = mean;
                    bestReaching = n;
                }
            }

            if (bestRoute < 0)
            {
                // Banks nothing in some seat on every route: no number, like a kit that never forages.
                line.Applicable = false;
                return;
            }

            // Supporting food numbers, on the route that is fastest.
            float routeFood = 0f, seatMin = float.PositiveInfinity, seatMax = float.NegativeInfinity, allFood = 0f;
            for (int cell = 0; cell < cells; cell++) allFood += banked[cell];
            for (int seat = 0; seat < Seats; seat++)
            {
                float v = banked[seat * routes + bestRoute];
                routeFood += v;
                if (v < seatMin) seatMin = v;
                if (v > seatMax) seatMax = v;
            }

            line.BankedBySeatRoute = banked;
            line.SecondsBySeatRoute = seconds;
            line.ReachedBySeatRoute = reached;
            line.BestRoute = Routes[bestRoute];
            line.BestSeconds = bestSeconds;
            line.SeatsReachingGoal = bestReaching;
            line.RouteSpreadSeconds = slowest - bestSeconds;
            line.OffsetSeconds = offsets != null && offsets.TryGetValue(kit.Label, out var offset) ? offset : 0f;
            line.Result = line.BestSeconds - line.OffsetSeconds;
            line.BestBanked = routeFood / Seats;
            line.BestSeatMin = seatMin;
            line.BestSeatMax = seatMax;
            line.SeatSpread = seatMax - seatMin;
            line.MeanBanked = allFood / cells;
        }

        /// <summary>
        /// Seconds a seat needs to bank the goal. A seat that did: the time it did. One that did not: the clock plus what is still missing at the
        /// seat's own mean income, <c>clock + (target - banked) / (banked / clock)</c>, finite and the worse the less it banked. NaN when it banked nothing.
        /// </summary>
        public static float GoalSeconds(FarmSeatResult run, float target, float clock)
        {
            if (run.SecondsToTarget >= 0f) return run.SecondsToTarget;
            if (run.Banked <= 0f || clock <= 0f) return float.NaN;
            return clock + (target - run.Banked) / (run.Banked / clock);
        }

        private static FarmSeat[] OthersStandard(OracleKit standard)
        {
            var seats = new FarmSeat[Seats];
            for (int s = 0; s < Seats; s++) seats[s] = new FarmSeat { Present = true, Kit = standard, Route = FarmRoute.Nearest };
            return seats;
        }

        // --------------------------------------------------- standard farmers

        private struct FarmerFacts
        {
            public float MeanCargo;
            public float LeaderBanked;
            public float[] FarmerBanked;
            public float[] SeatBanked;
            public float PilesEmptyAt;
        }

        /// <summary>
        /// Four standard farmers, goal off, no event. Mean cargo = food per trip (banked / trips), the load a farmer is
        /// carrying home; the leader is the best banker.
        /// </summary>
        private static FarmerFacts StandardFarmers(OracleInputs inputs, OracleKit standard, OracleFood food)
        {
            var results = FarmSim.Run(inputs.Map, food, OthersStandard(standard), OracleKits.Rules(inputs, false, FarmFoodEvent.None));
            float sum = 0f, leader = 0f;
            int counted = 0;
            foreach (var r in results)
            {
                if (r.Banked > leader) leader = r.Banked;
                if (r.Trips <= 0) continue;
                sum += Mathf.Min(r.Banked / r.Trips, standard.CargoCapacity);
                counted++;
            }
            // The Spoiler takes the fourth seat, so the three farmers it hunts are seats 0 to 2 of this run.
            var hunted = new float[Seats - 1];
            for (int i = 0; i < hunted.Length; i++) hunted[i] = results[i].Banked;
            var all = new float[Seats];
            for (int i = 0; i < all.Length; i++) all[i] = results[i].Banked;
            return new FarmerFacts
            {
                MeanCargo = counted > 0 ? sum / counted : 0f, LeaderBanked = leader, FarmerBanked = hunted,
                SeatBanked = all, PilesEmptyAt = results[0].PilesEmptyAt,
            };
        }

        // ------------------------------------------------------- Sneaky tables

        private static void BuildSpoiler(OracleReport report, OracleInputs inputs, Previews scope, MatchConfigSO match, FarmerFacts farmers)
        {
            var passive = scope.Of(FindPassive<SpoilerPassiveSO>(inputs.Abilities));
            var mark = scope.Of(FindAbility(inputs.Abilities, AbilityJob.DeathMark)).OracleFacts;
            var t = new SpoilerTableInputs
            {
                ExecuteBounty = match.ExecuteBounty,
                PassiveBonus = passive.MatchEndBonusFood(null),
                ClockSeconds = match.MatchDurationSeconds,
                WinTarget = match.FoodTargetToWin,
                RemovalSeconds = mark.MarkRemovalSeconds,
                MeanCargo = farmers.MeanCargo,
                Capacity = report.StandardCapacity,
                FarmerBanked = farmers.FarmerBanked,
                MaxKills = SneakyTables.MaxSpoilerKills(mark.MarkRoundStartCooldown, mark.MarkWindowSeconds, mark.MarkSuccessCooldown, match.MatchDurationSeconds),
            };
            report.SpoilerBountyPerKill = t.ExecuteBounty;
            report.SpoilerPassiveBonus = t.PassiveBonus;
            report.SpoilerRemovalSeconds = t.RemovalSeconds;
            report.Spoiler = SneakyTables.Spoiler(t);
        }

        private static void BuildThief(OracleReport report, OracleInputs inputs, Previews scope, MatchConfigSO match,
            OracleKit[] kits, FarmerFacts farmers)
        {
            string label = ChickenSubclass.Assassin_Thief.ToString();
            OracleKit kit = default;
            bool found = false;
            foreach (var k in kits)
                if (k.Label == label) { kit = k; found = true; break; }
            if (!found) throw new InvalidOperationException($"The oracle has no {label} kit: the Thief table cannot be built.");

            var passive = scope.Of(FindPassive<ThiefPassiveSO>(inputs.Abilities));
            var steal = scope.Of(FindSteal(inputs.Abilities));

            var t = new ThiefTableInputs
            {
                StealAmount = steal.OracleFacts.StealAmount,
                StealMultiplier = passive.ModifyStealAmount(1f, null),
                StealCooldown = passive.ModifyCooldown(steal.Cooldown, steal, null),
                CargoCapacity = kit.CargoCapacity,
                MoveSpeed = kit.MoveSpeed,
                DepositRate = kit.DepositRate,
                MeanBaseToContestedLeg = MeanBaseToContestedLeg(inputs.Map, kit.Jumps),
                ClockSeconds = match.MatchDurationSeconds,
                WinTarget = match.FoodTargetToWin,
                MeanCargo = farmers.MeanCargo,
                Capacity = report.StandardCapacity,
            };
            report.Thief = SneakyTables.Thief(t);
        }

        private static float MeanBaseToContestedLeg(OracleMapSO map, bool jumps)
        {
            float sum = 0f;
            for (int seat = 0; seat < Seats; seat++)
            {
                int b = OracleMapSO.BaseIndex(seat);
                sum += map.Leg(b, OracleMapSO.ContestedIndex(seat), jumps);
                sum += map.Leg(b, OracleMapSO.ContestedIndex((seat + Seats - 1) % Seats), jumps);
            }
            return sum / (Seats * 2);
        }

        // -------------------------------------------------------------- lookups

        private static T FindPassive<T>(AbilityRegistrySO abilities) where T : PassiveAbilitySO
        {
            foreach (var p in abilities.Passives)
                if (p is T found) return found;
            throw new InvalidOperationException($"The ability registry holds no {typeof(T).Name}: the oracle cannot build its Sneaky tables.");
        }

        /// <summary>The ability of type T in the registry, else in a passive's fixed kit (Death Mark is pre-equip only and may not be registered).</summary>
        private static AbilityBaseSO FindAbility(AbilityRegistrySO abilities, AbilityJob job)
        {
            if (abilities.All != null)
                foreach (var a in abilities.All)
                    if (a != null && a.Job == job) return a;
            foreach (var p in abilities.Passives)
            {
                if (p == null || p.Kit == null) continue;
                foreach (var a in p.Kit)
                    if (a != null && a.Job == job) return a;
            }
            throw new InvalidOperationException($"No {job} asset is reachable from the ability registry.");
        }

        private static AbilityBaseSO FindSteal(AbilityRegistrySO abilities)
        {
            if (abilities.All != null)
                foreach (var a in abilities.All)
                    if (a != null && a.Job == AbilityJob.Steal)
                        return a.OracleFacts.StealAmount > 0f
                            ? a
                            : throw new InvalidOperationException($"{a.name} has the Steal job but declares no steal amount in its OracleFacts: the oracle cannot read it.");
            throw new InvalidOperationException("The ability registry holds no ability with the Steal job.");
        }

        /// <summary>Owns the <see cref="BalanceBook.Preview{T}"/> copies of one run and destroys them on dispose.</summary>
        private sealed class Previews : IDisposable
        {
            private readonly OracleInputs _inputs;
            private readonly List<ScriptableObject> _copies = new List<ScriptableObject>();

            public Previews(OracleInputs inputs) { _inputs = inputs; }

            public T Of<T>(T asset) where T : ScriptableObject
            {
                if (asset == null || _inputs.Book == null || _inputs.Verdict == null || _inputs.Verdict.IsEmpty) return asset;
                var copy = _inputs.Book.Preview(asset, _inputs.Verdict);
                _copies.Add(copy);
                return copy;
            }

            public void Dispose()
            {
                foreach (var copy in _copies)
                {
                    if (copy == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
                    else UnityEngine.Object.DestroyImmediate(copy);
                }
                _copies.Clear();
            }
        }
    }
}
