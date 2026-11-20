using System.Collections.Generic;
using System.Linq;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The food sweep: one row per pile setup. Every expectation is computed from the shipped assets, from a second run through the
    /// oracle, or from <see cref="FoodRules"/>; the sweep never changes an asset.
    /// </summary>
    public sealed class OracleSweepTests
    {
        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private OracleInputs _inputs;
        private OracleFood _shipped;
        private OracleFood _moreCentre;
        private OracleFood _breaksRules;
        private List<SweepRow> _rows;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _inputs = new OracleInputs { Map = _map, Abilities = _abilities, Classes = _classes, Match = _match };

            _shipped = OracleKits.Food(_inputs);
            _moreCentre = _shipped;
            _moreCentre.Centre += 10f;
            // The own pile alone reaches the target, so "own plus one contested is not enough" cannot hold.
            _breaksRules = new OracleFood { Personal = _match.FoodTargetToWin, Contested = _shipped.Contested, Centre = _shipped.Centre };

            _rows = OracleSweep.Food(_inputs, new[] { _shipped, _moreCentre, _breaksRules }, null);
        }

        [Test]
        public void EveryCandidateGetsARow_AndABreakingOneIsFlaggedNotDropped()
        {
            Assert.AreEqual(3, _rows.Count);
            var shipped = _rows[0];
            Assert.AreEqual(FoodRules.OwnPlusOneIsNotEnough(_shipped, _match.FoodTargetToWin), shipped.OwnPlusOneIsNotEnough);
            Assert.AreEqual(FoodRules.OwnPlusTwoIsEnough(_shipped, _match.FoodTargetToWin), shipped.OwnPlusTwoIsEnough);

            var broken = _rows[2];
            Assert.AreEqual(_breaksRules.Personal, broken.Food.Personal);
            Assert.IsFalse(broken.OwnPlusOneIsNotEnough);
            Assert.IsFalse(broken.RulesHold);
            Assert.IsNotEmpty(broken.Kits, "a flagged row still carries its numbers");
            Assert.Greater(broken.StandardTotal, 0f);

            string md = OracleSweep.ToMarkdown(_rows, "h", "m");
            StringAssert.Contains("BREAKS", md);
            StringAssert.Contains(OracleSweep.Name(_breaksRules), md);
        }

        [Test]
        public void MoreCentreFood_NeverBanksLessForTheFourPlainWarriors()
        {
            Assert.AreEqual(_rows[0].Food.Personal, _rows[1].Food.Personal);
            Assert.AreEqual(_rows[0].Food.Contested, _rows[1].Food.Contested);
            Assert.Greater(_rows[1].Food.Centre, _rows[0].Food.Centre);
            Assert.GreaterOrEqual(_rows[1].StandardTotal, _rows[0].StandardTotal - 1e-3f,
                "ten more food in the centre pile must not lower what four standard farmers bank together");
            Assert.AreEqual(_rows[0].TotalFood + 10f, _rows[1].TotalFood, 1e-4f);
        }

        [Test]
        public void RowNumbers_AreTheOracleReportNumbers_ForThatFood()
        {
            var report = OracleRunner.Run(new OracleInputs
            {
                Map = _map, Abilities = _abilities, Classes = _classes, Match = _match, FoodOverride = _moreCentre,
            }, null);
            var row = _rows[1];
            Assert.AreEqual(report.StandardSeatBanked.Min(), row.StandardMin, 1e-4f);
            Assert.AreEqual(report.StandardSeatBanked.Max(), row.StandardMax, 1e-4f);
            Assert.AreEqual(report.StandardPilesEmptyAt, row.PilesEmptyAt, 1e-4f);

            var foragers = report.Economy.Where(l => l.Applicable).ToList();
            CollectionAssert.AreEqual(foragers.Select(l => l.Label), row.Kits.Select(k => k.Label));
            foreach (var line in foragers)
            {
                var kit = row.Kits.Single(k => k.Label == line.Label);
                Assert.AreEqual(line.BestBanked, kit.FoodAtClock, 1e-4f, line.Label);
                Assert.AreEqual(line.BestSeconds, kit.SecondsToGoal, 1e-4f, line.Label);
                Assert.AreEqual(line.SeatsReachingGoal, kit.SeatsReachingGoal, line.Label);
            }
        }

        [Test]
        public void TheSweepChangesNoAsset()
        {
            string before = JsonUtility.ToJson(_match);
            var foodBefore = OracleKits.Food(_inputs);

            OracleSweep.Food(_inputs, new[] { _moreCentre, _breaksRules }, null);

            Assert.AreEqual(before, JsonUtility.ToJson(_match), "MatchConfig.asset field values");
            Assert.AreEqual(foodBefore.Personal, OracleKits.Food(_inputs).Personal);
            Assert.AreEqual(foodBefore.Contested, OracleKits.Food(_inputs).Contested);
            Assert.AreEqual(foodBefore.Centre, OracleKits.Food(_inputs).Centre);
            Assert.IsFalse(EditorUtility.IsDirty(_match), "the sweep must not dirty the match config");
        }

        [Test]
        public void Markdown_IsOneTableWithASentenceAboveIt_AndJsonParses()
        {
            var lines = OracleSweep.ToMarkdown(_rows, "h", "m").Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var starts = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith("|") && (i == 0 || !lines[i - 1].StartsWith("|"))).ToList();
            Assert.AreEqual(1, starts.Count, "one table");
            int at = starts[0];
            Assert.AreEqual(string.Empty, lines[at - 1]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(lines[at - 2]));
            Assert.IsFalse(lines[at - 2].StartsWith("#") || lines[at - 2].StartsWith("|"), "prose above the table");
            int tableRows = lines.Skip(at).TakeWhile(l => l.StartsWith("|")).Count();
            Assert.AreEqual(2 + _rows.Count, tableRows, "header, divider, one row per candidate");

            Assert.IsTrue(BalanceJson.TryParse(OracleSweep.ToJson(_rows, "h", "m"), out var root, out var error), error);
            Assert.IsTrue(root.TryGet("rows", out var rows));
            Assert.AreEqual(_rows.Count, rows.Items.Count);
        }

        [Test]
        public void TheDefaultCandidateList_IsTheTenAgreedSetups()
        {
            var names = OracleSweep.DefaultCandidates.Select(OracleSweep.Name).ToList();
            CollectionAssert.AreEqual(new[]
            {
                "5 / 10 / 20", "10 / 15 / 20", "10 / 15 / 30", "10 / 15 / 40", "8 / 16 / 30",
                "8 / 16 / 40", "5 / 18 / 30", "5 / 18 / 40", "10 / 18 / 30", "10 / 20 / 30",
            }, names);
        }
    }
}
