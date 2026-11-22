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
    /// The farm-only match: four standard farmers on the shipped map must bank what part 1's <see cref="FarmSim"/> says (a comparison of
    /// two models, not of a design number), and the simulator must be deterministic and obey its own clock, target and reaction delay.
    /// </summary>
    public sealed class MatchSimFarmTests
    {
        private const float Half = 20f;

        public static IEnumerable<FarmRoute> Routes => OracleRunner.Routes;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        // ---- the shipped map ----

        private static OracleInputs ShippedInputs()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return new OracleInputs
            {
                Map = map,
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
        }

        // The win target is lifted in both models so that neither ends early: the comparison is about farming, not about who gets there.
        private static FarmRules NeverEndingRules(OracleInputs inputs)
        {
            var rules = OracleKits.Rules(inputs, false, FarmFoodEvent.None);
            rules.WinTarget = 1e6f;
            return rules;
        }

        private static MatchSetup ShippedSetup(OracleInputs inputs, OracleKit kit, FarmRoute route, float reactionDelay, bool thinkEveryTick)
        {
            var rules = NeverEndingRules(inputs);
            float half = OracleMapBaker.ArenaHalf();
            var mk = new MatchKit { Label = kit.Label, Class = ChickenClass.Warrior, Farm = kit };
            var param = new PolicyParams[4];
            for (int s = 0; s < 4; s++)
            {
                param[s] = PolicyParams.Default;
                param[s].ReactionDelay = reactionDelay;
                param[s].Route = route;
            }
            return new MatchSetup
            {
                Map = inputs.Map,
                Nav = OracleNav.For(inputs.Map, half),
                Food = OracleKits.Food(inputs),
                Rules = new MatchRules { Farm = rules, ArenaHalf = half, ThinkSeconds = thinkEveryTick ? rules.TickSeconds : 0f },
                Seats = new[] { mk, mk, mk, mk },
                Policies = new[] { FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory, FarmOnlyPolicy.Factory },
                Params = param,
            };
        }

        [TestCaseSource(nameof(Routes))]
        public void FourStandardFarmers_BankWithinOneFoodOfFarmSim(FarmRoute route)
        {
            var inputs = ShippedInputs();
            var kit = OracleKits.StandardFarmer(inputs);
            var food = OracleKits.Food(inputs);

            var seats = new FarmSeat[4];
            for (int s = 0; s < 4; s++) seats[s] = new FarmSeat { Present = true, Kit = kit, Route = route };
            var farm = FarmSim.Run(inputs.Map, food, seats, NeverEndingRules(inputs));

            var match = MatchSim.Run(ShippedSetup(inputs, kit, route, 0f, true), 1UL);

            // Four identical kits on a four-fold symmetric map: any difference between two seats is tie luck, not economy. Two seats that land
            // a peck on the same pile on the same tick alternate by the tick's rotation, so shifting the tick order by one swaps their food
            // (checked: it turns 37/35 into 35/37 and back). The seats are therefore compared by rank, which is what the equivalence means.
            var expected = new float[4];
            var actual = new float[4];
            for (int s = 0; s < 4; s++) { expected[s] = farm[s].Banked; actual[s] = match.Banked[s]; }
            System.Array.Sort(expected);
            System.Array.Sort(actual);
            string detail = $"{route}: FarmSim by seat [{string.Join(", ", System.Array.ConvertAll(farm, f => f.Banked.ToString("0.00")))}], MatchSim by seat [{string.Join(", ", System.Array.ConvertAll(match.Banked, f => f.ToString("0.00")))}]";
            for (int rank = 0; rank < 4; rank++)
                Assert.LessOrEqual(Mathf.Abs(actual[rank] - expected[rank]), 1f, $"rank {rank}. {detail}");
            Assert.LessOrEqual(Mathf.Abs(Sum(actual) - Sum(expected)), 1f, $"total banked. {detail}");
        }

        private static float Sum(float[] values)
        {
            float total = 0f;
            foreach (var v in values) total += v;
            return total;
        }

        [TestCaseSource(nameof(Routes))]
        public void FarmOnly_NeverHasARejectedIntent(FarmRoute route)
        {
            var inputs = ShippedInputs();
            var kit = OracleKits.StandardFarmer(inputs);
            var result = MatchSim.Run(ShippedSetup(inputs, kit, route, 0.3f, false), 7UL);
            Assert.AreEqual(0, result.RejectedIntents, route.ToString());
        }

        // ---- synthetic maps ----

        private static MatchKit Kit() => MatchTestKit.Farmer("test", speed: 6f, capacity: 6, peckAmount: 2f, peckCooldown: 0.5f, castSeconds: 0.4f, depositRate: 4f);

        private MatchSetup FourFarmers(float reactionDelay)
        {
            var map = MatchTestKit.OpenMap(Half);
            var k = Kit();
            var setup = MatchTestKit.Setup(map, new[] { k, k, k, k }, FarmOnlyPolicy.Factory, Half);
            for (int s = 0; s < 4; s++) setup.Params[s].ReactionDelay = reactionDelay;
            setup.RecordEvents = true;
            return setup;
        }

        [Test]
        public void SameSeed_SameResult()
        {
            var setup = FourFarmers(0.3f);
            var a = MatchSim.Run(setup, 99UL);
            var b = MatchSim.Run(setup, 99UL);

            CollectionAssert.AreEqual(a.Banked, b.Banked);
            Assert.AreEqual(a.Winner, b.Winner);
            Assert.AreEqual(a.EndSeconds, b.EndSeconds);
            Assert.Greater(a.Events.Count, 0, "the match must have produced events for this comparison to mean anything");
            Assert.AreEqual(a.Events.Count, b.Events.Count);
            for (int i = 0; i < a.Events.Count; i++)
            {
                var x = a.Events[i];
                var y = b.Events[i];
                Assert.AreEqual((x.Time, x.Kind, x.Seat, x.Other, x.Amount, x.Cause), (y.Time, y.Kind, y.Seat, y.Other, y.Amount, y.Cause), $"event {i}");
            }
        }

        [Test]
        public void ReachingTheTarget_EndsTheMatch()
        {
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Setup(map, new[] { Kit(), null, null, null }, FarmOnlyPolicy.Factory, Half);
            setup.Rules.Farm.WinTarget = 5f;

            var result = MatchSim.Run(setup, 3UL);

            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(0, result.Winner);
            Assert.Less(result.EndSeconds, setup.Rules.Farm.ClockSeconds);
            Assert.GreaterOrEqual(result.Banked[0], setup.Rules.Farm.WinTarget - 1e-3f);
        }

        [Test]
        public void TheClock_EndsAMatchNobodyWins_AndPicksTheMostBanked()
        {
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Setup(map, new[] { Kit(), Kit(), null, null }, FarmOnlyPolicy.Factory, Half);
            setup.Rules.Farm.WinTarget = 1e6f;

            var result = MatchSim.Run(setup, 3UL);

            Assert.IsFalse(result.ReachedTarget);
            Assert.AreEqual(setup.Rules.Farm.ClockSeconds, result.EndSeconds);
            int best = result.Banked[0] >= result.Banked[1] ? 0 : 1;
            if (Mathf.Abs(result.Banked[0] - result.Banked[1]) > 1e-3f) Assert.AreEqual(best, result.Winner);
        }

        // A policy that sends its chicken to a pile and records where it stands each time it is asked.
        private sealed class Probe : IMatchPolicy
        {
            public readonly List<(float time, Vector2 pos)> Samples = new List<(float, Vector2)>();
            private readonly int _self;
            public Probe(int self) { _self = self; }

            public MatchIntent Think(MatchView view, ref MatchRng rng)
            {
                Samples.Add((view.Time, view.Seats[_self].Pos));
                return new MatchIntent { Goal = view.Map.Places[OracleMapSO.PersonalIndex(0)].Pos, PeckPile = OracleMapSO.PersonalIndex(0), CastSlot = -1, CastTarget = -1 };
            }
        }

        [Test]
        public void ReactionDelay_DelaysTheFirstMove()
        {
            const float delay = 0.5f;
            var map = MatchTestKit.OpenMap(Half);
            var probe = new Probe(0);
            var setup = MatchTestKit.Setup(map, new[] { Kit(), null, null, null }, (seat, p) => probe, Half);
            setup.Params[0].ReactionDelay = delay;
            setup.Rules.Farm.ClockSeconds = 3f;

            MatchSim.Run(setup, 1UL);

            Vector2 spawn = probe.Samples[0].pos;
            Assert.LessOrEqual(Vector2.Distance(spawn, map.Places[OracleMapSO.BaseIndex(0)].Pos), map.BaseRadius + 1e-3f, "a chicken spawns inside its own base zone");
            float tick = setup.Rules.Farm.TickSeconds;
            Assert.Greater(probe.Samples.Count, 10);
            foreach (var (time, pos) in probe.Samples)
            {
                // A think at time t is seen by the chicken at t + delay; a sample taken before that still shows the chicken at its spawn.
                if (time <= delay) Assert.AreEqual(spawn, pos, $"the chicken moved at t={time:0.000}, before the first intent could take effect");
            }
            var last = probe.Samples[probe.Samples.Count - 1];
            Assert.Greater(Vector2.Distance(spawn, last.pos), 1f, "after the delay the chicken must be on its way");

            // Control: no delay, and the chicken has moved one tick after the first think.
            var instant = new Probe(0);
            var setup0 = MatchTestKit.Setup(map, new[] { Kit(), null, null, null }, (seat, p) => instant, Half);
            setup0.Rules.Farm.ClockSeconds = 3f;
            MatchSim.Run(setup0, 1UL);
            Assert.AreNotEqual(spawn, instant.Samples[2].pos, $"without a delay the chicken should have moved by tick 2 (tick {tick:0.000} s)");
        }
    }
}
