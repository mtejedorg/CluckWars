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

namespace CluckWars.Tests
{
    /// <summary>
    /// The what-if report (BL-217): file shape, the delta path, common random numbers and the report text. Plays at a tiny replicate scale. No test
    /// asserts a win rate, and no asset value is restated: values to move are read from the shipped assets at run time.
    /// </summary>
    public sealed class MatchWhatIfTests
    {
        private const double TinyScale = 0.04;
        private const ulong Seed = 7UL;

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

        private static string Delta(string asset, string field, float value) =>
            "{\"schema\":1,\"assets\":{\"" + asset + "\":{\"" + field + "\":" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}}}";

        private static string Key(WhatIfLine l) => l.Group + "|" + l.Label + "|" + l.Population;

        private static WhatIfOutput Play(params WhatIfScenario[] scenarios)
        {
            var inputs = ShippedInputs();
            var spec = new WhatIfSpec { ReplicateScale = TinyScale };
            spec.Scenarios.AddRange(scenarios);
            using (var book = new BalanceBook(new QuietLog(), inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                return MatchWhatIf.Run(inputs, OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius(), spec, Seed, 4, null);
            }
        }

        // ---- the file ----

        [Test]
        public void File_ReadsScenarios_ObjectOrStringDelta_AndTheScale()
        {
            const string text = "{\"replicateScale\":0.5,\"scenarios\":[{\"name\":\"a\",\"delta\":{\"schema\":1,\"assets\":{\"X\":{\"Y\":2.50}}}},{\"name\":\"b\",\"delta\":\"{\\\"schema\\\":1}\"}]}";
            Assert.IsTrue(WhatIfSpec.TryParse(text, out var spec, out string error), error);
            Assert.AreEqual(0.5, spec.ReplicateScale);
            Assert.AreEqual(2, spec.Scenarios.Count);
            StringAssert.Contains("2.50", spec.Scenarios[0].DeltaJson, "a number keeps its token");
            Assert.AreEqual("{\"schema\":1}", spec.Scenarios[1].DeltaJson);
            Assert.IsTrue(WhatIfSpec.TryParse("{\"scenarios\":[]}", out spec, out error), error);
            Assert.AreEqual(1.0, spec.ReplicateScale, "no scale means the report's own replicates");
        }

        [Test]
        public void File_RefusesBadShapes_WithAReason()
        {
            foreach (var bad in new[]
            {
                "[]", "{\"scenarios\":{}}", "{\"nope\":1}", "{\"replicateScale\":0}",
                "{\"scenarios\":[{\"delta\":{}}]}", "{\"scenarios\":[{\"name\":\"a\"}]}",
                "{\"scenarios\":[{\"name\":\"a\",\"delta\":{}},{\"name\":\"a\",\"delta\":{}}]}",
                "{\"scenarios\":[{\"name\":\"" + WhatIfSpec.BaselineName + "\",\"delta\":{}}]}",
            })
            {
                Assert.IsFalse(WhatIfSpec.TryParse(bad, out var spec, out string error), bad);
                Assert.IsNull(spec);
                Assert.IsFalse(string.IsNullOrEmpty(error), bad);
            }
        }

        [Test]
        public void ScaledReplicates_IsOneAtScaleOne_AndNeverBelowOne()
        {
            Assert.AreEqual(MatchReports.MixedReplicates, MatchReports.ScaledReplicates(MatchReports.MixedReplicates, 1.0));
            Assert.AreEqual(1, MatchReports.ScaledReplicates(MatchReports.ForcedKillsReplicates, 0.0001));
            Assert.AreEqual(MatchReports.MixedReplicates / 2, MatchReports.ScaledReplicates(MatchReports.MixedReplicates, 0.5));
        }

        // ---- the run ----

        [Test]
        public void ScenarioEqualToBaseline_ShowsZeroDifferenceOnEveryLine()
        {
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var output = Play(
                new WhatIfScenario { Name = "empty delta", DeltaJson = "{\"schema\":1}" },
                new WhatIfScenario { Name = "shipped value", DeltaJson = Delta("MatchConfig", "ExecuteBounty", match.ExecuteBounty) });
            Assert.AreEqual(2, output.Scenarios.Count);
            foreach (var s in output.Scenarios)
            {
                Assert.IsFalse(s.Refused, s.Name + ": " + s.Reason);
                var lines = MatchWhatIf.Lines(output, s);
                Assert.Greater(lines.Count, 0);
                foreach (var l in lines)
                {
                    Assert.IsFalse(float.IsNaN(l.Base) != float.IsNaN(l.Scenario), $"{s.Name}: {Key(l)} exists on one side only");
                    if (float.IsNaN(l.Base)) continue;
                    Assert.AreEqual(0f, l.Diff, $"{s.Name}: {Key(l)}");
                }
            }
        }

        [Test]
        public void ARefusedDelta_IsReportedWithItsReason_AndTheRestStillRun()
        {
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var output = Play(
                new WhatIfScenario { Name = "deny-listed", DeltaJson = Delta("MatchConfig", "MaxPlayers", 2f) },
                new WhatIfScenario { Name = "unknown asset", DeltaJson = Delta("NoSuchAsset", "ExecuteBounty", 1f) },
                new WhatIfScenario { Name = "not json", DeltaJson = "{\"schema\":" },
                new WhatIfScenario { Name = "fine", DeltaJson = Delta("MatchConfig", "ExecuteBounty", match.ExecuteBounty + 3f) });
            Assert.AreEqual(4, output.Scenarios.Count);
            for (int i = 0; i < 3; i++)
            {
                var s = output.Scenarios[i];
                Assert.IsTrue(s.Refused, s.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.Reason), s.Name);
                Assert.IsNull(s.Report, s.Name);
                Assert.AreEqual(0, MatchWhatIf.Lines(output, s).Count);
            }
            var fine = output.Scenarios[3];
            Assert.IsFalse(fine.Refused, fine.Reason);
            Assert.IsNotNull(fine.Report);
            Assert.AreNotEqual(output.Baseline.Hash, fine.Hash, "the scenario really played a different game");

            string md = MatchWhatIf.Markdown(output);
            foreach (var s in output.Scenarios.Take(3)) { StringAssert.Contains(s.Name, md); StringAssert.Contains(s.Reason, md); }
            Assert.IsTrue(BalanceJson.TryParse(MatchWhatIf.Json(output), out var json, out string error), error);
            Assert.AreEqual(JsonKind.Object, json.Kind);
        }

        [Test]
        public void ScenarioOrder_DoesNotChangeAnyNumber()
        {
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var a = new WhatIfScenario { Name = "a", DeltaJson = Delta("MatchConfig", "ExecuteBounty", match.ExecuteBounty + 4f) };
            var b = new WhatIfScenario { Name = "b", DeltaJson = Delta("MatchConfig", "FoodTargetToWin", match.FoodTargetToWin - 5) };
            var forward = Play(a, b);
            var reverse = Play(b, a);
            foreach (var name in new[] { "a", "b" })
            {
                var x = MatchWhatIf.Lines(forward, forward.Scenarios.Single(s => s.Name == name));
                var y = MatchWhatIf.Lines(reverse, reverse.Scenarios.Single(s => s.Name == name));
                Assert.AreEqual(x.Count, y.Count, name);
                for (int i = 0; i < x.Count; i++)
                {
                    Assert.AreEqual(Key(x[i]), Key(y[i]), name);
                    Assert.AreEqual(x[i].Scenario, y[i].Scenario, name + ": " + Key(x[i]));
                    Assert.AreEqual(x[i].Base, y[i].Base, name + ": " + Key(x[i]));
                }
            }
        }

        [Test]
        public void Numbers_CarryTheWarriorGroup()
        {
            var report = new MatchReportSection { Population = PolicyPopulation.P0 };
            report.Warriors.Add(new WarriorLine
            {
                Kit = "Warrior_Enforcer", Matches = 10, BiteActive = true, DebtKit = true, BitesFromCastPerMatch = 1.5f, BitesFromCargoPerMatch = 1f, BiteFoodFromCastPerMatch = 3f,
                BiteFoodFromCargoPerMatch = 2f, DebtsEarnedPerMatch = 4f, DebtsPerCollect = 2.5f, StealsPerMatch = 3f, MeanStealSize = 7.5f, IdentityFoodPerMatch = 6f,
                WorstLossMean = 3f, WorstLossP95 = 7f,
            });
            report.Warriors.Add(new WarriorLine { Kit = "Warrior_Bully", Matches = 10, HitActive = true, HitTakesPerMatch = 3f, HitFoodPerMatch = 6f, IdentityFoodPerMatch = 6f });

            var numbers = MatchWhatIf.Numbers(report).Where(n => n.Group == "warrior").ToList();

            float Value(string label) => numbers.Single(n => n.Label == label && n.Population == "P0").Value;
            Assert.AreEqual(2.5f, Value("Warrior_Enforcer: bites per match"));
            Assert.AreEqual(5f, Value("Warrior_Enforcer: bite food per match"));
            Assert.AreEqual(4f, Value("Warrior_Enforcer: debts earned per match"));
            Assert.AreEqual(2.5f, Value("Warrior_Enforcer: debts consumed per cash"));
            Assert.AreEqual(7.5f, Value("Warrior_Enforcer: mean Steal size"));
            Assert.AreEqual(7f, Value("Warrior_Enforcer: worst loss for a victim in 5 s (95th percentile)"));
            Assert.AreEqual(3f, Value("Warrior_Bully: hit takes per match"));
            Assert.IsFalse(numbers.Any(n => n.Label == "Warrior_Bully: bites per match"), "a kit without a bite carries no bite rows");
            Assert.IsFalse(numbers.Any(n => n.Label == "Warrior_Enforcer: hit takes per match"), "and one without a hit-steal no hit rows");
        }

    }
}
