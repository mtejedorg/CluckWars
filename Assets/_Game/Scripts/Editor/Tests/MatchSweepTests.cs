using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The hypercube, the mixed population and the robustness rules of the match simulator's sweep. No rate is asserted against a target:
    /// the tests pin the stratification, the seeds, and what a call becomes when the populations or the sweep disagree.
    /// </summary>
    public sealed class MatchSweepTests
    {
        private sealed class QuietLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private static WinInterval Rate(float rate, int trials = 1000) => WinInterval.Wilson((int)Math.Round(rate * trials), trials);

        // ---- the hypercube ----

        [Test]
        public void LatinHypercube_EachAxisHasOnePointPerStratum()
        {
            const int n = 16;
            var points = MatchSweep.LatinHypercube(n, 99UL);
            Assert.AreEqual(n, points.Length);

            void Check(Func<PolicyParams, float> value, float lo, float hi, string axis)
            {
                var strata = points.Select(p => Math.Min(n - 1, (int)((value(p) - lo) / (hi - lo) * n))).OrderBy(i => i).ToArray();
                CollectionAssert.AreEqual(Enumerable.Range(0, n).ToArray(), strata, $"{axis}: exactly one point in each of the {n} strata");
                foreach (var p in points) Assert.That(value(p), Is.InRange(lo, hi), axis);
            }
            Check(p => p.ReactionDelay, MatchSweep.DelayMin, MatchSweep.DelayMax, "reaction delay");
            Check(p => p.ReturnThreshold, MatchSweep.ReturnMin, MatchSweep.ReturnMax, "return threshold");
            Check(p => p.FleeRadius, MatchSweep.FleeMin, MatchSweep.FleeMax, "flee radius");
            Check(p => p.HuntDetour, MatchSweep.DetourMin, MatchSweep.DetourMax, "hunt detour");

            // The two choices cover every level, as evenly as the count allows.
            var targets = MatchSweep.TargetLevels.Select(t => points.Count(p => p.Target == t)).ToArray();
            var routes = MatchSweep.RouteLevels.Select(r => points.Count(p => p.Route == r)).ToArray();
            Assert.That(targets.Min(), Is.GreaterThan(0));
            Assert.LessOrEqual(targets.Max() - targets.Min(), 2, "target rule levels");
            Assert.That(routes.Min(), Is.GreaterThan(0));
            Assert.LessOrEqual(routes.Max() - routes.Min(), 2, "route levels");
        }

        [Test]
        public void LatinHypercube_SameSeedSamePoints()
        {
            var a = MatchSweep.LatinHypercube(16, 5UL);
            var b = MatchSweep.LatinHypercube(16, 5UL);
            var c = MatchSweep.LatinHypercube(16, 6UL);
            for (int i = 0; i < a.Length; i++)
                for (int axis = 0; axis < MatchSweep.AxisCount; axis++)
                    Assert.AreEqual(MatchSweep.AxisValue(a[i], axis), MatchSweep.AxisValue(b[i], axis), $"point {i} axis {axis}");
            Assert.IsTrue(Enumerable.Range(0, a.Length).Any(i => MatchSweep.AxisValue(a[i], 0) != MatchSweep.AxisValue(c[i], 0)), "another seed, other points");
        }

        [Test]
        public void LatinHypercube_PointsKeepTheReportsJitter()
        {
            foreach (var p in MatchSweep.LatinHypercube(4, 1UL)) Assert.AreEqual(MatchRunSpec.DefaultReactionJitter, p.ReactionJitter);
        }

        // ---- the mixed population ----

        [Test]
        public void MixedPopulation_DrawsPerSeat_FromTheSetupStream()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var again = MatchRunner.MixedDraw(seed);
                var draw = MatchRunner.MixedDraw(seed);
                CollectionAssert.AreEqual(again, draw, "same seed, same draw");

                var rng = MatchRng.Create(seed, MatchRng.StreamSetup);
                for (int s = 0; s < 4; s++) Assert.AreEqual(rng.NextInt(2) == 1, draw[s], $"seed {seed} seat {s}: one draw per seat from the setup stream, in seat order");
            }
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 40; seed++) seen.Add(string.Join(",", MatchRunner.MixedDraw(seed)));
            Assert.Greater(seen.Count, 4, "the draws differ across seeds");
        }

        [Test]
        public void MixedPopulation_UsesBothPolicies()
        {
            int p1 = 0, p0 = 0;
            for (ulong seed = 1; seed <= 100; seed++)
                foreach (bool b in MatchRunner.MixedDraw(seed)) { if (b) p1++; else p0++; }
            Assert.Greater(p1, 0);
            Assert.Greater(p0, 0);
        }

        // ---- calls and tags ----

        [Test]
        public void Call_AgreeingPopulations_KeepTheirCall()
        {
            Assert.AreEqual(KitCall.Above, MatchRobustness.Call(Rate(0.36f), Rate(0.41f), Rate(0.55f)));
            Assert.AreEqual(KitCall.Below, MatchRobustness.Call(Rate(0.02f), Rate(0.10f), Rate(0.19f)));
            Assert.AreEqual(KitCall.InBand, MatchRobustness.Call(Rate(0.21f), Rate(0.25f), Rate(0.29f)));
        }

        [Test]
        public void Call_DisagreeingPopulations_ArePolicySensitive()
        {
            Assert.AreEqual(KitCall.PolicySensitive, MatchRobustness.Call(Rate(0.12f), Rate(0.24f), Rate(0.20f)));
            Assert.AreEqual(KitCall.PolicySensitive, MatchRobustness.Call(Rate(0.25f), Rate(0.25f), Rate(0.33f)));
            Assert.AreEqual(KitCall.NoCall, MatchRobustness.Call(Rate(0.25f), Rate(0.25f), WinInterval.Wilson(0, 0)), "a population with no matches gives no call");
        }

        [Test]
        public void Tag_TauBelowMin_IsPolicySensitive()
        {
            Assert.AreEqual(KitCall.PolicySensitive, MatchRobustness.Tag(KitCall.InBand, MatchRobustness.MinTau - 0.05f, 0f));
            Assert.AreEqual(KitCall.InBand, MatchRobustness.Tag(KitCall.InBand, MatchRobustness.MinTau, 0f), "at the floor it holds");
            Assert.AreEqual(KitCall.InBand, MatchRobustness.Tag(KitCall.InBand, float.NaN, 0f), "no pair to compare is no evidence against");
        }

        [Test]
        public void Tag_EdgeCrossAboveMax_IsPolicySensitive()
        {
            Assert.AreEqual(KitCall.PolicySensitive, MatchRobustness.Tag(KitCall.Above, 1f, MatchRobustness.MaxEdgeCrossPoints + 0.5f));
            Assert.AreEqual(KitCall.Above, MatchRobustness.Tag(KitCall.Above, 1f, MatchRobustness.MaxEdgeCrossPoints));
            Assert.AreEqual(KitCall.NoCall, MatchRobustness.Tag(KitCall.NoCall, 0f, 99f), "no call stays no call");
        }

        [Test]
        public void EdgeCrossPoints_IsHowFarPastTheEdgeTheCallSitsBehind()
        {
            Assert.AreEqual(0f, MatchRobustness.EdgeCrossPoints(KitCall.Above, 0.40f), 1e-4f);
            Assert.AreEqual(4f, MatchRobustness.EdgeCrossPoints(KitCall.Above, MatchReports.BandHigh - 0.04f), 1e-3f, "an Above kit that dipped four points under the top edge");
            Assert.AreEqual(5f, MatchRobustness.EdgeCrossPoints(KitCall.Below, MatchReports.BandLow + 0.05f), 1e-3f);
            Assert.AreEqual(2f, MatchRobustness.EdgeCrossPoints(KitCall.InBand, MatchReports.BandHigh + 0.02f), 1e-3f);
            Assert.AreEqual(3f, MatchRobustness.EdgeCrossPoints(KitCall.InBand, MatchReports.BandLow - 0.03f), 1e-3f);
        }

        [Test]
        public void TauAgainstDefault_IgnoresPairsTheDefaultCannotTellApart()
        {
            var def = new[] { 0.60f, 0.30f, 0.30f, 0.02f };
            // The two kits tied at 0.30 swap places; every other pair keeps its order: the swap does not count.
            Assert.AreEqual(1f, MatchRobustness.TauAgainstDefault(def, new[] { 0.55f, 0.28f, 0.33f, 0.05f }, 0.05f), 1e-6f);
            // A real inversion of two distinguishable kits does.
            Assert.Less(MatchRobustness.TauAgainstDefault(def, new[] { 0.05f, 0.30f, 0.30f, 0.60f }, 0.05f), 0f);
            Assert.IsNaN(MatchRobustness.TauAgainstDefault(new[] { 0.3f, 0.3f }, new[] { 0.3f, 0.4f }, 0.05f), "no distinguishable pair");
        }

        [Test]
        public void KitTau_OnlyCountsThePairsThatHoldTheKit()
        {
            var def = new[] { 0.60f, 0.30f, 0.05f, 0.04f };
            // Kits 2 and 3 swap, kits 0 and 1 stay: kit 0's place is untouched and kit 2's pair with kit 3 is within the noise (ignored).
            var swapped = new[] { 0.58f, 0.31f, 0.04f, 0.05f };
            Assert.AreEqual(1f, MatchRobustness.KitTauAgainstDefault(def, swapped, 0, 0.05f), 1e-6f);
            Assert.AreEqual(1f, MatchRobustness.KitTauAgainstDefault(def, swapped, 2, 0.05f), 1e-6f);
            // Kit 1 falls under both low kits: its pair with kit 0 holds, its two pairs with kits 2 and 3 flip.
            var fell = new[] { 0.60f, 0.03f, 0.05f, 0.04f };
            Assert.AreEqual(-1f / 3f, MatchRobustness.KitTauAgainstDefault(def, fell, 1, 0.05f), 1e-5f);
            Assert.IsNaN(MatchRobustness.KitTauAgainstDefault(new[] { 0.3f, 0.3f }, new[] { 0.3f, 0.4f }, 0, 0.05f));
        }

        [Test]
        public void Effect_OfANumber_IsTheSlopeTimesTheRange_AndOfAChoiceTheLevelSpread()
        {
            var x = new[] { 0f, 1f, 2f, 3f };
            Assert.AreEqual(12f, MatchSensitivity.Effect(x, new[] { 10f, 14f, 18f, 22f }, false, 3f), 1e-4f, "slope 4 per unit over a range of 3");
            Assert.AreEqual(-6f, MatchSensitivity.Effect(x, new[] { 10f, 8f, 6f, 4f }, false, 3f), 1e-4f);
            Assert.AreEqual(0f, MatchSensitivity.Effect(new[] { 1f, 1f, 1f }, new[] { 3f, 9f, 5f }, false, 3f));
            // Two levels (0 and 1): means 10 and 16.
            Assert.AreEqual(6f, MatchSensitivity.Effect(new[] { 0f, 0f, 1f, 1f }, new[] { 9f, 11f, 15f, 17f }, true, 1f), 1e-4f);
        }

        // ---- the report over the three populations ----

        private static MatchKit[] EightKits()
        {
            var kits = new MatchKit[8];
            for (int i = 0; i < kits.Length; i++)
            {
                kits[i] = MatchTestKit.Farmer("kit" + i, 6f, 20, 3f, 1f, 0.5f, 5f);
                kits[i].Class = (ChickenClass)(i / 2);
                kits[i].Passive = MatchPassive.Identity();
            }
            return kits;
        }

        private static MatchResult Won(int seat)
        {
            int causes = Enum.GetValues(typeof(CutCause)).Length;
            var counters = new SeatCounters[4];
            for (int s = 0; s < 4; s++)
                counters[s] = new SeatCounters
                {
                    FirstContact = -1f, StolenFromSeat = new float[4], CutsByCause = new int[causes], ExposedByCause = new int[causes],
                    CutWhileExposedByCause = new int[causes],
                };
            return new MatchResult
            {
                Winner = seat, WinnerWithoutSpoilerBonus = seat, Banked = new float[4], Kills = new int[4], EndSeconds = 30f, ReachedTarget = true,
                Counters = counters,
            };
        }

        private static MatchRunOutput Mixed(PolicyPopulation population, int winnerSeat)
        {
            var cells = Enumerable.Range(0, 10).Select(i => new MatchCell { Index = i, KitOfSeat = new[] { 0, 2, 4, 6 } }).ToArray();
            return new MatchRunOutput
            {
                Spec = new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, Population = population, RunSeed = 1UL },
                Cells = cells, Results = cells.Select(c => Won(winnerSeat)).ToArray(),
            };
        }

        [Test]
        public void Report_WithThreePopulations_CallsOnlyWhenTheyAgree()
        {
            var map = MatchTestKit.OpenMap(20f);
            try
            {
                var setup = MatchTestKit.Setup(map, new MatchKit[4], FarmOnlyPolicy.Factory, 20f);
                var context = new MatchContext { Map = map, Nav = setup.Nav, Food = setup.Food, Rules = setup.Rules, Kits = EightKits(), BalanceHash = "synthetic" };
                // Seat 0 (kit0) wins every match under all three; seat 2 (kit4) wins every match under P1 only.
                var runs = new[] { Mixed(PolicyPopulation.P0, 0), Mixed(PolicyPopulation.P1, 0), Mixed(PolicyPopulation.Mixed, 0) };
                var report = MatchReports.Build(context, runs, null);

                Assert.AreEqual(3, report.Policies.Count);
                Assert.AreEqual(KitCall.Above, report.Kits[0].Call, "all three say the kit wins every match");
                Assert.AreEqual(KitCall.Below, report.Kits[2].Call, "all three say it wins none");
                StringAssert.Contains("agree", report.Kits[0].Reason);

                runs = new[] { Mixed(PolicyPopulation.P0, 0), Mixed(PolicyPopulation.P1, 2), Mixed(PolicyPopulation.Mixed, 0) };
                report = MatchReports.Build(context, runs, null);
                Assert.AreEqual(KitCall.PolicySensitive, report.Kits[0].Call, "P1 sends the win elsewhere");
                Assert.AreEqual(KitCall.PolicySensitive, report.Kits[4].Call);
                StringAssert.Contains("range", report.Kits[4].Reason);
                Assert.AreEqual(0f, report.Kits[4].RangeLow, 1e-6f);
                Assert.AreEqual(1f, report.Kits[4].RangeHigh, 1e-6f);
                StringAssert.Contains("policy-sensitive", report.ToMarkdown());
                Assert.DoesNotThrow(() => JsonUtility.FromJson<Probe>(report.ToJson()));
            }
            finally { MatchTestKit.Release(); }
        }

        [Serializable]
        private sealed class Probe { public int schema; }

        // ---- the leader weight goes through the book ----

        [Test]
        public void LeaderPickWeightAxis_GoesThroughTheVerdict()
        {
            var inputs = new OracleInputs
            {
                Map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath),
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
            float half = OracleMapBaker.ArenaHalf();
            float arrival = OracleMenu.BotArrivalRadius();

            var shipped = MatchContext.From(inputs, half, arrival);
            float Weight(MatchContext c) => c.Kits.SelectMany(k => k.Abilities).Single(a => a.Job == AbilityJob.DeathMark).MarkLeaderPickWeight;
            float before = Weight(shipped);

            using (var book = new BalanceBook(new QuietLog(), inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                float other = Mathf.Approximately(before, 3f) ? 2f : 3f;
                var moved = MatchSweep.ContextWithLeaderWeight(inputs, other, half, arrival);
                Assert.AreEqual(other, Weight(moved), 1e-6f, "the built Death Mark carries the swept weight");
                Assert.AreNotEqual(moved.BalanceHash, shipped.BalanceHash, "and the context says it is a delta, not the shipped numbers");
            }
            Assert.AreEqual(before, Weight(MatchContext.From(inputs, half, arrival)), 1e-6f, "the book put the shipped value back");
        }

        [Test]
        public void LeaderPickWeightAxis_NeedsABook()
        {
            var inputs = new OracleInputs { Map = null };
            Assert.Throws<ArgumentException>(() => MatchSweep.ContextWithLeaderWeight(inputs, 2f, 20f, 1.5f));
        }
    }
}
