using System.Collections.Generic;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// <see cref="FarmSim"/> on small hand-made maps (two or three places that matter, every other leg 1000 m) and,
    /// where noted, on the baked map. Expected times are computed here from the legs, speeds and cooldowns that the
    /// test sets; nothing is copied from a game asset.
    /// </summary>
    public class FarmSimTests
    {
        private const float Tick = 0.01f;
        private const float Tol = 0.1f;
        private const float Far = 1000f;

        private static readonly int B0 = OracleMapSO.BaseIndex(0);
        private static readonly int B1 = OracleMapSO.BaseIndex(1);
        private static readonly int P0 = OracleMapSO.PersonalIndex(0);

        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        // ---- helpers ----

        private OracleMapSO Synth(float reach = 2f)
        {
            var map = ScriptableObject.CreateInstance<OracleMapSO>();
            _made.Add(map);
            map.Places = new OraclePlace[OracleMapSO.PlaceCount];
            for (int i = 0; i < OracleMapSO.PlaceCount; i++)
            {
                var kind = i == 0 ? OraclePlaceKind.Centre : i <= 4 ? OraclePlaceKind.Personal : i <= 8 ? OraclePlaceKind.Contested : OraclePlaceKind.Base;
                map.Places[i] = new OraclePlace { Kind = kind, Seat = i == 0 ? -1 : (i - 1) % 4 };
            }
            int n = OracleMapSO.PlaceCount;
            map.WalkLegs = new float[n * n];
            map.JumpLegs = new float[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    map.WalkLegs[i * n + j] = map.JumpLegs[i * n + j] = i == j ? 0f : Far;
            map.CollectReach = reach;
            return map;
        }

        private static void SetLeg(OracleMapSO map, int a, int b, float walk, float jump = -1f)
        {
            if (jump < 0f) jump = walk;
            int n = OracleMapSO.PlaceCount;
            map.WalkLegs[a * n + b] = map.WalkLegs[b * n + a] = walk;
            map.JumpLegs[a * n + b] = map.JumpLegs[b * n + a] = jump;
        }

        private static OracleKit Kit(float speed = 6f, int capacity = 6, float amount = 2f, float cooldown = 0.5f, float cast = 0.5f, float deposit = 3f) =>
            new OracleKit
            {
                Label = "test", Forages = true, MoveSpeed = speed, CargoCapacity = capacity, PeckAmount = amount,
                PeckCooldown = cooldown, PeckCastSeconds = cast, DepositRate = deposit,
            };

        private static FarmSeat[] Seat0(OracleKit kit, FarmRoute route = FarmRoute.Nearest)
        {
            var seats = new FarmSeat[4];
            seats[0] = new FarmSeat { Present = true, Kit = kit, Route = route };
            return seats;
        }

        private static FarmRules Rules(float target, float clock, bool stop) =>
            new FarmRules { WinTarget = target, ClockSeconds = clock, StopAtTarget = stop, TickSeconds = Tick, PileSlowFactor = 1f };

        private static OracleFood PersonalOnly(float food) => new OracleFood { Personal = food };

        /// <summary>Seat 0 alone: base to its personal pile is <paramref name="leg"/> metres, one trip fills the kit from a pile that holds exactly that.</summary>
        private OracleMapSO PersonalMap(float leg, float jumpLeg = -1f)
        {
            var map = Synth();
            SetLeg(map, B0, P0, leg, jumpLeg);
            return map;
        }

        // ---- the economy of a run ----

        [Test]
        public void SingleLoad_OneTrip()
        {
            const float leg = 12f, food = 6f;
            var kit = Kit(capacity: 6, amount: 2f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, 60f, true))[0];

            int pecks = Mathf.CeilToInt(food / kit.PeckAmount);
            float expected = leg / kit.MoveSpeed + pecks * kit.PeckCooldown + leg / kit.MoveSpeed + food / kit.DepositRate;
            Assert.AreEqual(food, r.Banked, 1e-3f);
            Assert.AreEqual(1, r.Trips);
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
        }

        [Test]
        public void Capacity_ForcesTwoTrips()
        {
            const float leg = 12f, food = 8f;
            var kit = Kit(capacity: 4, amount: 2f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, 120f, true))[0];

            int pecksPerTrip = Mathf.CeilToInt(kit.CargoCapacity / kit.PeckAmount);
            float trip = leg / kit.MoveSpeed + pecksPerTrip * kit.PeckCooldown + leg / kit.MoveSpeed + kit.CargoCapacity / kit.DepositRate;
            Assert.AreEqual(2, r.Trips);
            Assert.AreEqual(food, r.Banked, 1e-3f);
            Assert.AreEqual(2f * trip, r.SecondsToTarget, Tol);
        }

        [Test]
        public void PartialPeck_StillCostsAFullCycle()
        {
            const float leg = 12f, food = 7f;
            var kit = Kit(capacity: 7, amount: 3f); // 3 + 3 + 1: the last press is partial
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, 60f, true))[0];

            int pecks = Mathf.CeilToInt(food / kit.PeckAmount);
            Assert.AreEqual(3, pecks);
            float expected = leg / kit.MoveSpeed + pecks * kit.PeckCooldown + leg / kit.MoveSpeed + food / kit.DepositRate;
            Assert.AreEqual(food, r.Banked, 1e-3f);
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
        }

        [Test]
        public void PeckCast_DelaysTheFood()
        {
            const float leg = 6f, food = 3f;
            var map = PersonalMap(leg);
            var slowCast = Kit(capacity: 3, amount: 1f, cooldown: 0.4f, cast: 0.5f);
            var fastCast = Kit(capacity: 3, amount: 1f, cooldown: 0.4f, cast: 0.4f);

            float withCast = FarmSim.Run(map, PersonalOnly(food), Seat0(slowCast), Rules(food, 60f, true))[0].SecondsToTarget;
            float withoutCast = FarmSim.Run(map, PersonalOnly(food), Seat0(fastCast), Rules(food, 60f, true))[0].SecondsToTarget;

            float travelAndDeposit = 2f * leg / slowCast.MoveSpeed + food / slowCast.DepositRate;
            Assert.AreEqual(travelAndDeposit + 3f * Mathf.Max(slowCast.PeckCooldown, slowCast.PeckCastSeconds), withCast, Tol, "Three pecks cycle at the cast length when it exceeds the cooldown (1.5 s, not 1.2 s).");
            Assert.AreEqual(travelAndDeposit + 3f * fastCast.PeckCooldown, withoutCast, Tol);
            Assert.AreEqual(3f * (slowCast.PeckCastSeconds - slowCast.PeckCooldown), withCast - withoutCast, Tol);
        }

        [Test]
        public void ZeroCast_FoodComesAtThePress_SoOnlyTheCyclesBetweenPecksCost()
        {
            const float leg = 12f, food = 6f;
            var kit = Kit(capacity: 6, amount: 2f, cooldown: 0.5f, cast: 0f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, 60f, true))[0];

            int pecks = Mathf.CeilToInt(food / kit.PeckAmount);
            float expected = leg / kit.MoveSpeed + (pecks - 1) * kit.PeckCooldown + leg / kit.MoveSpeed + food / kit.DepositRate;
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
        }

        [Test]
        public void ARootedChicken_DoesNotWalkAwayBeforeItsFoodLands()
        {
            const float leg = 6f, food = 2f;
            var kit = Kit(capacity: 2, amount: 2f, cooldown: 0.4f, cast: 2f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, 60f, true))[0];

            Assert.AreEqual(2f * leg / kit.MoveSpeed + kit.PeckCastSeconds + food / kit.DepositRate, r.SecondsToTarget, Tol);
        }

        [Test]
        public void WinCrossesMidDeposit_CountsOnlyNeeded()
        {
            const float leg = 12f, food = 10f, target = 6f;
            var kit = Kit(capacity: 10, amount: 4f, deposit: 2f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(target, 60f, true))[0];

            // Two pecks carry 8, which is more than the 6 still needed; the win lands after 6 of the 8 are banked.
            float expected = leg / kit.MoveSpeed + 2f * kit.PeckCooldown + leg / kit.MoveSpeed + target / kit.DepositRate;
            Assert.AreEqual(target, r.Banked, 1e-3f, "Only the food needed for the win is counted.");
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
        }

        [Test]
        public void StopAtTargetFalse_KeepsBanking()
        {
            const float leg = 12f, food = 8f, target = 6f;
            var kit = Kit(capacity: 10, amount: 4f, deposit: 2f);
            var map = PersonalMap(leg);

            var r = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(target, 60f, false))[0];

            float expected = leg / kit.MoveSpeed + 2f * kit.PeckCooldown + leg / kit.MoveSpeed + target / kit.DepositRate;
            Assert.AreEqual(food, r.Banked, 1e-3f, "The whole load is banked past the target.");
            Assert.AreEqual(expected, r.SecondsToTarget, Tol, "The win time is still the moment the target was crossed.");
        }

        [Test]
        public void EmptiedPile_Redirects()
        {
            // Seat 0 walks 10 m to contested pile 5; seat 1 is 1 m from it and empties it with one peck while seat 0 is on the way.
            // Seat 0 must turn to pile 6 instead of standing at the empty one.
            const float seat0Leg = 10f, seat1Leg = 1f, toSecond = 14f, between = 3f, amount = 4f;
            var map = Synth();
            int pile5 = OracleMapSO.ContestedIndex(0), pile6 = OracleMapSO.ContestedIndex(1);
            SetLeg(map, B0, pile5, seat0Leg);
            SetLeg(map, B1, pile5, seat1Leg);
            SetLeg(map, B0, pile6, toSecond);
            SetLeg(map, pile5, pile6, between);

            var kit = Kit(speed: 5f, capacity: 4, amount: amount, cooldown: 0.5f, cast: 0f, deposit: 4f);
            var seats = new FarmSeat[4];
            seats[0] = new FarmSeat { Present = true, Kit = kit };
            seats[1] = new FarmSeat { Present = true, Kit = kit };
            var food = new OracleFood { Contested = amount };

            var r = FarmSim.Run(map, food, seats, Rules(amount, 60f, true));

            float emptiedAt = seat1Leg / kit.MoveSpeed;
            float walkedBy = kit.MoveSpeed * emptiedAt;
            float redirect = Mathf.Min(walkedBy + toSecond, (seat0Leg - walkedBy) + between);
            float expected = emptiedAt + redirect / kit.MoveSpeed + toSecond / kit.MoveSpeed + amount / kit.DepositRate;
            Assert.AreEqual(amount, r[0].Banked, 1e-3f, "Seat 0 must reach another pile instead of waiting at the empty one.");
            Assert.AreEqual(expected, r[0].SecondsToTarget, Tol);
        }

        [Test]
        public void PeckCast_OnAPileAnotherFarmerEmptied_EndsAtOnce_AndTheChickenIsFreed()
        {
            // Both seats reach pile 5 together. Seat 1 (instant cast) empties it; seat 0 is mid a long cast on it. The game cuts that
            // cast: seat 0 is freed at once and turns to pile 6, instead of standing rooted until the cast would have ended.
            const float leg = 1f, between = 3f, toSecond = 2f, amount = 4f, slowCast = 3f;
            var map = Synth();
            int pile5 = OracleMapSO.ContestedIndex(0), pile6 = OracleMapSO.ContestedIndex(1);
            SetLeg(map, B0, pile5, leg);
            SetLeg(map, B1, pile5, leg);
            SetLeg(map, pile5, pile6, between);
            SetLeg(map, B0, pile6, toSecond);

            var seats = new FarmSeat[4];
            seats[0] = new FarmSeat { Present = true, Kit = Kit(speed: 5f, capacity: 4, amount: amount, cooldown: 0.5f, cast: slowCast, deposit: 4f) };
            seats[1] = new FarmSeat { Present = true, Kit = Kit(speed: 5f, capacity: 4, amount: amount, cooldown: 0.5f, cast: 0f, deposit: 4f) };
            var r = FarmSim.Run(map, new OracleFood { Contested = amount }, seats, Rules(amount, 60f, true));

            // Seat 0 is freed at leg/speed and walks to pile 6 while its cooldown (the cast length here) is still running, so the
            // second cast starts exactly one cycle after the first. Uncut it could not even start walking before that cycle ended.
            const float speed = 5f;
            float cycle = slowCast;
            float expected = leg / speed + cycle + slowCast + toSecond / speed + amount / 4f;
            float uncut = leg / speed + cycle + between / speed + slowCast + toSecond / speed + amount / 4f;
            Assert.AreEqual(amount, r[0].Banked, 1e-3f, "Seat 0 must end up with the food of the second pile.");
            Assert.AreEqual(expected, r[0].SecondsToTarget, Tol, "The cast on the emptied pile must end when the pile does.");
            Assert.Less(r[0].SecondsToTarget, uncut - 0.5f);
        }

        [Test]
        public void IdenticalKits_SymmetricSeats_BankTheSame()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            var kit = Kit(speed: 8f, capacity: 10, amount: 3f, cooldown: 0.8f, cast: 0.8f, deposit: 5f);
            var seats = new FarmSeat[4];
            for (int s = 0; s < 4; s++) seats[s] = new FarmSeat { Present = true, Kit = kit, Route = FarmRoute.Nearest };
            var rules = new FarmRules { WinTarget = 40f, ClockSeconds = 45f, StopAtTarget = false, PileSlowFactor = 1f };

            var r = FarmSim.Run(map, new OracleFood { Personal = 5f, Contested = 10f, Centre = 20f }, seats, rules);

            float min = Mathf.Min(Mathf.Min(r[0].Banked, r[1].Banked), Mathf.Min(r[2].Banked, r[3].Banked));
            float max = Mathf.Max(Mathf.Max(r[0].Banked, r[1].Banked), Mathf.Max(r[2].Banked, r[3].Banked));
            Assert.Greater(max, 0f, "The run must bank something for the comparison to mean anything.");
            Assert.LessOrEqual(max - min, kit.PeckAmount + 1e-3f, $"Banked per seat: {r[0].Banked}, {r[1].Banked}, {r[2].Banked}, {r[3].Banked}.");
        }

        [Test]
        public void Jumper_UsesJumpLegs()
        {
            const float walkLeg = 20f, jumpLeg = 10f, food = 6f;
            var map = PersonalMap(walkLeg, jumpLeg);
            var walker = Kit();
            var jumper = Kit();
            jumper.Jumps = true;

            var w = FarmSim.Run(map, PersonalOnly(food), Seat0(walker), Rules(food, 60f, true))[0];
            var j = FarmSim.Run(map, PersonalOnly(food), Seat0(jumper), Rules(food, 60f, true))[0];

            float fixedPart = 3f * walker.PeckCooldown + food / walker.DepositRate;
            Assert.AreEqual(2f * walkLeg / walker.MoveSpeed + fixedPart, w.SecondsToTarget, Tol);
            Assert.AreEqual(2f * jumpLeg / jumper.MoveSpeed + fixedPart, j.SecondsToTarget, Tol);
        }

        [Test]
        public void Burst_ShortensTravelOnly()
        {
            const float leg = 12f, food = 6f, multiplier = 2f;
            var map = PersonalMap(leg);
            var plain = Kit();
            var burst = Kit();
            burst.BurstMultiplier = multiplier;
            burst.BurstSeconds = 100f; // covers the whole run, so any effect on pecking or depositing would show
            burst.BurstCooldown = 0f;

            var a = FarmSim.Run(map, PersonalOnly(food), Seat0(plain), Rules(food, 60f, true))[0];
            var b = FarmSim.Run(map, PersonalOnly(food), Seat0(burst), Rules(food, 60f, true))[0];

            float fixedPart = 3f * plain.PeckCooldown + food / plain.DepositRate;
            Assert.AreEqual(2f * leg / plain.MoveSpeed + fixedPart, a.SecondsToTarget, Tol);
            Assert.AreEqual(2f * leg / (plain.MoveSpeed * multiplier) + fixedPart, b.SecondsToTarget, Tol);
        }

        [Test]
        public void Restock_AddsFoodAtTheEventTime()
        {
            const float leg = 12f, left = 10f, clock = 40f, restock = 6f;
            var map = PersonalMap(leg);
            var kit = Kit(capacity: 6, amount: 6f, cooldown: 1f, cast: 0f);
            var rules = Rules(restock, clock, true);
            rules.Event = FarmFoodEvent.Restock;
            rules.EventSecondsLeft = left;
            rules.RestockFood = restock;

            var r = FarmSim.Run(map, default, Seat0(kit), rules)[0];
            var none = FarmSim.Run(map, default, Seat0(kit), Rules(restock, clock, true))[0];

            float expected = (clock - left) + leg / kit.MoveSpeed + leg / kit.MoveSpeed + restock / kit.DepositRate;
            Assert.AreEqual(restock, r.Banked, 1e-3f);
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
            Assert.AreEqual(0f, none.Banked, "Without the event there is no food to bank.");
            Assert.AreEqual(-1f, none.SecondsToTarget);
        }

        [Test]
        public void GoldenPile_AddsFoodToTheCentre()
        {
            const float leg = 12f, left = 10f, clock = 40f, golden = 6f;
            var map = Synth();
            SetLeg(map, B0, OracleMapSO.CentreIndex, leg);
            var kit = Kit(capacity: 6, amount: 6f, cooldown: 1f, cast: 0f);
            var rules = Rules(golden, clock, true);
            rules.Event = FarmFoodEvent.GoldenPile;
            rules.EventSecondsLeft = left;
            rules.GoldenPileFood = golden;

            var r = FarmSim.Run(map, default, Seat0(kit), rules)[0];

            float expected = (clock - left) + 2f * leg / kit.MoveSpeed + golden / kit.DepositRate;
            Assert.AreEqual(golden, r.Banked, 1e-3f);
            Assert.AreEqual(expected, r.SecondsToTarget, Tol);
        }

        [Test]
        public void PileSlow_SlowsOnlyTheReachAtANonEmptyPile()
        {
            const float leg = 10f, food = 6f, factor = 0.5f, reach = 2f;
            var map = Synth(reach);
            SetLeg(map, B0, P0, leg);
            var kit = Kit(speed: 5f);
            var immune = Kit(speed: 5f);
            immune.IgnoresPileSlow = true;
            var rules = Rules(food, 60f, true);
            rules.PileSlowFactor = factor;

            var slowed = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), rules)[0];
            var free = FarmSim.Run(map, PersonalOnly(food), Seat0(immune), rules)[0];

            // The way there ends at a non-empty pile; the way back starts from a pile this chicken just emptied.
            float fixedPart = 3f * kit.PeckCooldown + food / kit.DepositRate + leg / kit.MoveSpeed;
            float thereSlowed = (leg - reach) / kit.MoveSpeed + reach / (kit.MoveSpeed * factor);
            Assert.AreEqual(fixedPart + thereSlowed, slowed.SecondsToTarget, Tol);
            Assert.AreEqual(fixedPart + leg / kit.MoveSpeed, free.SecondsToTarget, Tol);
        }

        [Test]
        public void NotForaging_BanksNothingAndSaysSo()
        {
            const float leg = 12f;
            var map = PersonalMap(leg);
            var kit = Kit();
            kit.Forages = false;

            var r = FarmSim.Run(map, PersonalOnly(6f), Seat0(kit), Rules(6f, 60f, true))[0];

            Assert.AreEqual(0f, r.Banked);
            Assert.AreEqual(0, r.Trips);
            Assert.AreEqual(-1f, r.SecondsToTarget);
        }

        [Test]
        public void Deterministic_SameInputsSameOutputs()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            var seats = new FarmSeat[4];
            var routes = new[] { FarmRoute.Nearest, FarmRoute.SidesFirst, FarmRoute.CentreFirst, FarmRoute.EasySideThenCentre };
            for (int s = 0; s < 4; s++)
            {
                var kit = Kit(speed: 7f + s, capacity: 8 + s, amount: 2f + s * 0.5f);
                kit.Jumps = s == 2;
                seats[s] = new FarmSeat { Present = true, Kit = kit, Route = routes[s] };
            }
            var food = new OracleFood { Personal = 5f, Contested = 10f, Centre = 20f };
            var rules = new FarmRules { WinTarget = 40f, ClockSeconds = 45f, PileSlowFactor = 0.8f };

            var a = FarmSim.Run(map, food, seats, rules);
            var b = FarmSim.Run(map, food, seats, rules);

            for (int s = 0; s < 4; s++)
            {
                Assert.AreEqual(a[s].Banked, b[s].Banked, $"seat {s} banked");
                Assert.AreEqual(a[s].SecondsToTarget, b[s].SecondsToTarget, $"seat {s} seconds");
                Assert.AreEqual(a[s].Trips, b[s].Trips, $"seat {s} trips");
            }
        }

        // ---- ties and the clock ----

        private static OracleMapSO BakedMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return map;
        }

        private static OracleFood MapFood(OracleMapSO map)
        {
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            return new OracleFood { Personal = match.PersonalPileFood, Contested = match.ContestedPileFood, Centre = match.CentrePileFood };
        }

        [Test]
        public void TieBreak_IsRelativeToTheSeat()
        {
            var map = BakedMap();
            var kit = Kit(speed: 7f, capacity: 8, amount: 2f);
            var rules = new FarmRules { WinTarget = 40f, ClockSeconds = 45f, PileSlowFactor = 1f };
            var routes = new[] { FarmRoute.Nearest, FarmRoute.SidesFirst, FarmRoute.CentreFirst, FarmRoute.EasySideThenCentre };

            foreach (var route in routes)
            {
                var banked = new float[4];
                for (int tested = 0; tested < 4; tested++)
                {
                    var seats = new FarmSeat[4];
                    for (int s = 0; s < 4; s++)
                        seats[s] = new FarmSeat { Present = true, Kit = Kit(speed: 6f, capacity: 6, amount: 2f), Route = FarmRoute.Nearest };
                    seats[tested] = new FarmSeat { Present = true, Kit = kit, Route = route };
                    banked[tested] = FarmSim.Run(map, MapFood(map), seats, rules)[tested].Banked;
                }

                float min = Mathf.Min(banked), max = Mathf.Max(banked);
                float mean = (banked[0] + banked[1] + banked[2] + banked[3]) / 4f;
                int odd = 0;
                for (int s = 1; s < 4; s++)
                    if (Mathf.Abs(banked[s] - mean) > Mathf.Abs(banked[odd] - mean)) odd = s;
                Assert.LessOrEqual(max - min, kit.PeckAmount,
                    $"route {route}: banked by seat = [{string.Join(", ", banked)}]; seat {odd} differs from the rest");
            }
        }

        [Test]
        public void CarriedFood_IsBankedBeforeTheClockEnds()
        {
            var map = BakedMap();
            var food = MapFood(map);
            float total = food.Centre + 4f * food.Personal + 4f * food.Contested;
            var kit = Kit(speed: 7f, capacity: Mathf.CeilToInt(total) + 1, amount: 2f);
            // A clock far too short to take everything: the chicken is still collecting when it has to turn home.
            float clock = 0.5f * total / (kit.PeckAmount / kit.PeckCooldown);
            var rules = new FarmRules { WinTarget = total, ClockSeconds = clock, PileSlowFactor = 1f };

            var r = FarmSim.Run(map, food, Seat0(kit), rules)[0];

            Assert.Greater(r.Banked, 0f, "the chicken banked nothing");
            Assert.AreEqual(0f, r.Carried, 1e-3f, $"it ended the clock carrying food it never banked (banked {r.Banked})");
        }

        [Test]
        public void DoesNotLeaveHome_WhenARoundTripNoLongerFits()
        {
            const float leg = 12f, food = 6f;
            var kit = Kit(speed: 6f, capacity: 6, amount: 2f);
            var map = PersonalMap(leg);
            float roundTrip = 2f * leg / kit.MoveSpeed;
            float cycle = Mathf.Max(kit.PeckCooldown, kit.PeckCastSeconds);

            var tooLate = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, roundTrip + cycle - 0.5f, false))[0];
            Assert.AreEqual(0f, tooLate.Carried, 1e-3f, "it walked out although the round trip plus a peck cycle did not fit the clock");
            Assert.AreEqual(0f, tooLate.Banked, 1e-3f);

            float fits = roundTrip + cycle + kit.PeckAmount / kit.DepositRate + 1f;
            var enough = FarmSim.Run(map, PersonalOnly(food), Seat0(kit), Rules(food, fits, false))[0];
            Assert.Greater(enough.Banked, 0f, "with time for the trip it should bank");
        }
    }
}
