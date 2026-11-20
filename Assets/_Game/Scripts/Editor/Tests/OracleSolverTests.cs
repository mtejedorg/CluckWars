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
    /// The oracle solver's behaviour, not any design target: no test fails because a kit misses a number. Every value a problem
    /// uses is read from the shipped assets (through <see cref="BalanceBook.TryProbe"/>) or from a first run, never typed.
    /// </summary>
    public sealed class OracleSolverTests
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

        private const string StandardClass = "Warrior"; // the standard farmer and the class whose peck cast the tests set

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private IReadOnlyDictionary<string, float> _offsets;
        private BalanceBook _book;
        private OracleReport _baseline;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _offsets = TestAssets.Load<OracleReferenceSO>(OracleMenu.ReferencePath).Offsets();
            _book = new BalanceBook(new QuietLog(), _abilities, _classes, _match);
            _baseline = OracleRunner.Run(Inputs(), _offsets);
        }

        [OneTimeTearDown]
        public void DisposeOnce() => _book?.Dispose();

        private OracleInputs Inputs() => new OracleInputs
        {
            Map = _map, Book = _book, Abilities = _abilities, Classes = _classes, Match = _match,
        };

        private float Shipped(string asset, string field)
        {
            Assert.IsTrue(_book.TryProbe(asset, field, out float shipped, out _, out _, out string why), why);
            return shipped;
        }

        /// <summary>The centre pile's food as a free value (it may only shrink; the food rules do not involve it), with the band set under what the farmers bank today so the solver has a reason to move it.</summary>
        private OracleProblem LessCentreFoodProblem(int passes)
        {
            float centre = Shipped("MatchConfig", nameof(MatchConfigSO.CentrePileFood));
            float mean = _baseline.StandardSeatBanked.Average();
            return new OracleProblem
            {
                BandLow = mean - 6f, BandHigh = mean - 3f, MaxPasses = passes,
                Free = { new FreeValue { Asset = "MatchConfig", Field = nameof(MatchConfigSO.CentrePileFood), Min = 0f, Max = centre, Step = centre * 0.25f } },
            };
        }

        [Test]
        public void NoFreeValues_ReturnsTheInput()
        {
            var proposal = OracleSolver.Solve(Inputs(), new OracleProblem(), _offsets);

            Assert.AreEqual(proposal.ScoreBefore, proposal.ScoreAfter);
            Assert.AreEqual(0, proposal.Passes);
            Assert.AreEqual(0, proposal.OracleOnly.Count);
            Assert.AreEqual(0, proposal.Values.Count);
            Assert.AreEqual(_baseline.ToJson(), proposal.After.ToJson(), "no free value, so the numbers must not move");
            var verdict = _book.Evaluate(proposal.DeltaJson, "test");
            Assert.IsTrue(verdict.IsAccepted);
            Assert.IsTrue(verdict.IsEmpty, "the delta of an untouched input changes nothing");
        }

        [Test]
        public void OneFreeValue_NeverMakesTheScoreWorse()
        {
            var proposal = OracleSolver.Solve(Inputs(), LessCentreFoodProblem(2), _offsets);

            Assert.LessOrEqual(proposal.ScoreAfter, proposal.ScoreBefore);
            Assert.AreEqual(1, proposal.Values.Count);
            var moved = proposal.Values[0];
            Assert.GreaterOrEqual(moved.New, 0f, "stays inside the range it was given");
            Assert.LessOrEqual(moved.New, moved.Old + 0.001f);
        }

        [Test]
        public void HardLimits_AreNeverBroken()
        {
            float target = _match.FoodTargetToWin;
            float personal = Shipped("MatchConfig", nameof(MatchConfigSO.PersonalPileFood));
            float contested = Shipped("MatchConfig", nameof(MatchConfigSO.ContestedPileFood));
            // One step up lands past the own-pile-plus-one rule; the band wants more food than the piles give, so the solver is drawn to it.
            float step = target - personal;
            var upwards = new OracleFood { Personal = personal, Contested = contested + step, Centre = 0f };
            Assume.That(FoodRules.OwnPlusOneIsNotEnough(upwards, target), Is.False, "the test needs a step that would break the rule");

            var problem = new OracleProblem
            {
                BandLow = _baseline.StandardSeatBanked.Average() + 20f, BandHigh = _baseline.StandardSeatBanked.Average() + 30f, MaxPasses = 3,
                Free = { new FreeValue { Asset = "MatchConfig", Field = nameof(MatchConfigSO.ContestedPileFood), Min = 0f, Max = contested + 2f * step, Step = step } },
            };
            var proposal = OracleSolver.Solve(Inputs(), problem, _offsets);

            Assert.IsTrue(FoodRules.OwnPlusOneIsNotEnough(proposal.After.Food, proposal.After.WinTarget), "own pile plus one contested must stay under the target");
            Assert.IsTrue(FoodRules.OwnPlusTwoIsEnough(proposal.After.Food, proposal.After.WinTarget), "own pile plus two contested must reach the target");
            Assert.LessOrEqual(proposal.After.Food.Contested, contested + step - 0.001f, "the move past the rule must not be taken");
        }

        [Test]
        public void Proposal_IsAValidSchema1Delta()
        {
            var proposal = OracleSolver.Solve(Inputs(), LessCentreFoodProblem(1), _offsets);

            Assert.IsTrue(BalanceJson.TryParse(proposal.DeltaJson, out var root, out string error), error);
            Assert.IsTrue(root.TryGet("schema", out var schema));
            Assert.AreEqual(1d, schema.Number);

            var verdict = _book.Evaluate(proposal.DeltaJson, "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            Assert.AreEqual(0, verdict.Ignored.Count, string.Join("; ", verdict.Ignored));
            Assert.Greater(verdict.Changes.Count, 0, "the problem gives the solver a reason to move, so the delta must change something");
            Assert.AreEqual(verdict.Canonical, proposal.DeltaJson, "the proposal is already in the book's canonical form");
            Assert.AreEqual(verdict.Hash, proposal.After.BalanceHash, "the report measured exactly the delta that is proposed");
        }

        [Test]
        public void OracleOnlyValues_StayOutOfTheDelta()
        {
            var problem = new OracleProblem
            {
                MaxPasses = 2,
                Free =
                {
                    new FreeValue { Asset = OracleSolver.OracleAsset, Field = "Fatty." + OracleSolver.PeckCastField, Min = 0f, Max = 1f, Step = 0.25f },
                    new FreeValue { Asset = OracleSolver.OracleAsset, Field = StandardClass + "." + OracleSolver.PeckCastField, Min = 0f, Max = 1f, Step = 0.25f },
                },
            };
            var proposal = OracleSolver.Solve(Inputs(), problem, _offsets);

            StringAssert.DoesNotContain(OracleSolver.OracleAsset, proposal.DeltaJson);
            StringAssert.DoesNotContain(OracleSolver.PeckCastField, proposal.DeltaJson);
            CollectionAssert.AreEquivalent(
                new[] { "Fatty." + OracleSolver.PeckCastField, StandardClass + "." + OracleSolver.PeckCastField },
                proposal.OracleOnly.Keys);
            Assert.IsTrue(_book.Evaluate(proposal.DeltaJson, "test").IsAccepted);
            Assert.IsTrue(proposal.Values.All(v => v.OracleOnly));
        }
    }
}
