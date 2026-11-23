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
    /// The solver's match term and its guard (spec 7.1): behaviour of the score, the noise rule, the cap, the holdout and the abort, on synthetic numbers.
    /// No test fails because a kit, class or seat misses a target or a rate.
    /// </summary>
    public sealed class MatchSolverTermTests
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

        private static WinInterval W(int wins, int trials) => WinInterval.Wilson(wins, trials);

        private static KitWinLine Kit(string label, int wins, KitCall call, int trials = 1000) =>
            new KitWinLine { Label = label, Class = ChickenClass.Warrior, Win = W(wins, trials), WinP1 = W(wins, trials), WinMixed = W(wins, trials), Call = call };

        // ---- the problem file ----

        [Test]
        public void OldProblemJson_ParsesWithMatchWeightZero()
        {
            const string old = "{\"bandLow\":28,\"bandHigh\":36,\"maxPasses\":2,\"free\":[{\"asset\":\"Fatty\",\"field\":\"CargoCapacity\",\"min\":10,\"max\":60,\"step\":5}]}";
            Assert.IsTrue(OracleProblem.TryParse(old, out var problem, out string error), error);
            Assert.AreEqual(0f, problem.MatchWeight);
            Assert.IsNull(problem.Match);
            Assert.AreEqual(3, problem.MaxMovedValues);
            StringAssert.DoesNotContain("\"match\"", problem.ToJson(), "an old problem writes back as it was read");
        }

        [Test]
        public void MatchBlock_RoundTripsThroughJson()
        {
            const string json = "{\"free\":[],\"match\":{\"weight\":2.5,\"maxMovedValues\":2,\"screenReplicates\":1,\"confirmReplicates\":4,\"confirmBest\":2,\"threads\":6,\"seatCheckReplicates\":9,\"seed\":\"0xdeadbeefcafef00d\"}}";
            Assert.IsTrue(OracleProblem.TryParse(json, out var problem, out string error), error);
            Assert.AreEqual(2.5f, problem.MatchWeight);
            Assert.AreEqual(2, problem.MaxMovedValues);
            Assert.AreEqual(1, problem.Match.ScreenReplicates);
            Assert.AreEqual(4, problem.Match.ConfirmReplicates);
            Assert.AreEqual(2, problem.Match.ConfirmBest);
            Assert.AreEqual(6, problem.Match.Threads);
            Assert.AreEqual(9, problem.Match.SeatCheckReplicates);
            Assert.AreEqual(0xdeadbeefcafef00dUL, problem.Match.RunSeed);

            Assert.IsTrue(OracleProblem.TryParse(problem.ToJson(), out var again, out error), error);
            Assert.AreEqual(problem.ToJson(), again.ToJson());
            Assert.AreEqual(problem.Match.RunSeed, again.Match.RunSeed);
        }

        [Test]
        public void MatchBlock_AnEmptyBlockTakesTheDefaults_AndBadKeysAreErrors()
        {
            Assert.IsTrue(OracleProblem.TryParse("{\"match\":{}}", out var problem, out string error), error);
            Assert.AreEqual(0f, problem.MatchWeight, "no weight key, so the term is off");
            Assert.AreEqual(3, problem.Match.ScreenReplicates);
            Assert.AreEqual(10, problem.Match.ConfirmReplicates);
            Assert.AreEqual(3, problem.Match.ConfirmBest);
            Assert.AreEqual(MatchProblemBlock.DefaultSeed, problem.Match.RunSeed);

            Assert.IsFalse(OracleProblem.TryParse("{\"match\":{\"wait\":1}}", out _, out error));
            StringAssert.Contains("unknown key", error);
            Assert.IsFalse(OracleProblem.TryParse("{\"match\":{\"weight\":-1}}", out _, out _));
            Assert.IsFalse(OracleProblem.TryParse("{\"match\":{\"confirmReplicates\":1}}", out _, out _), "one block has no noise to estimate");
            Assert.IsFalse(OracleProblem.TryParse("{\"match\":{\"seed\":\"nope\"}}", out _, out _));
        }

        // ---- the score ----

        [Test]
        public void Score_RatesInsideTheDeadZone_ScoreZero()
        {
            var s = new MatchReportSection();
            s.Kits.Add(Kit("A", 260, KitCall.InBand));      // 1 point off, margin about 2.7 points
            s.Kits.Add(Kit("B", 240, KitCall.InBand));
            s.Classes.Add(new ClassWinLine { Class = ChickenClass.Warrior, Pooled = W(255, 1000), PooledP1 = W(245, 1000), PooledMixed = W(250, 1000) });
            Assert.AreEqual(0f, MatchSolverTerm.Score(s));
        }

        [Test]
        public void Score_IsTheSquaredDistanceBeyondTheMargin_InPoints()
        {
            var s = new MatchReportSection();
            var far = Kit("Far", 50, KitCall.Below);        // 5 %
            s.Kits.Add(far);
            float half = (far.Win.High - far.Win.Low) * 0.5f;
            float expected = ((0.25f - 0.05f - half) * 100f) * ((0.25f - 0.05f - half) * 100f);
            Assert.AreEqual(expected, MatchSolverTerm.Score(s), expected * 1e-3f);
            Assert.Greater(expected, 0f);
        }

        [Test]
        public void Score_PolicySensitiveAndNoCallKits_AddNothing()
        {
            var s = new MatchReportSection();
            s.Kits.Add(Kit("Disputed", 50, KitCall.PolicySensitive));
            s.Kits.Add(Kit("Unknown", 600, KitCall.NoCall));
            Assert.AreEqual(0f, MatchSolverTerm.Score(s), "no kit is tuned where the populations disagree or no call exists");

            s.Kits.Add(Kit("Plain", 50, KitCall.Below));
            Assert.Greater(MatchSolverTerm.Score(s), 0f);
        }

        [Test]
        public void Score_ASingleOddPopulation_CountsOnlyAThirdOfItsDistance()
        {
            // D4: the term never optimises one policy: each population carries a third of the kit's weight.
            var all = new MatchReportSection();
            all.Kits.Add(Kit("K", 50, KitCall.Below));
            var one = new MatchReportSection();
            one.Kits.Add(new KitWinLine { Label = "K", Class = ChickenClass.Warrior, Win = W(50, 1000), WinP1 = W(250, 1000), WinMixed = W(250, 1000), Call = KitCall.Below });
            Assert.AreEqual(MatchSolverTerm.Score(all) / 3f, MatchSolverTerm.Score(one), MatchSolverTerm.Score(all) * 1e-3f);
        }

        // ---- the guard ----

        private static readonly float[] Alternating = { 1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f };

        [Test]
        public void Accepts_GainBelowTwiceThePairedNoise_IsRejected()
        {
            float twiceSe = 2f * MatchSolverTerm.StandardError(Alternating);
            Assert.AreEqual(0.6667f, twiceSe, 1e-3f, "the textbook value for ten blocks of plus and minus one");
            Assert.IsFalse(MatchSolverTerm.Accepts(twiceSe * 0.75f, Alternating, MatchSolverTerm.MinGain));
        }

        [Test]
        public void Accepts_GainAboveThePairedNoise_IsAccepted()
        {
            float twiceSe = 2f * MatchSolverTerm.StandardError(Alternating);
            Assert.IsTrue(MatchSolverTerm.Accepts(twiceSe * 1.25f, Alternating, MatchSolverTerm.MinGain));
            Assert.IsTrue(MatchSolverTerm.Accepts(0.5f, new float[] { 0.5f, 0.5f, 0.5f, 0.5f }, MatchSolverTerm.MinGain), "no spread between blocks, any real gain counts");
        }

        [Test]
        public void Accepts_NoGainOrOneBlock_IsRejected()
        {
            Assert.IsFalse(MatchSolverTerm.Accepts(0f, new float[] { 0f, 0f, 0f }, MatchSolverTerm.MinGain));
            Assert.IsFalse(MatchSolverTerm.Accepts(-1f, new float[] { -1f, -1f, -1f }, MatchSolverTerm.MinGain));
            Assert.IsFalse(MatchSolverTerm.Accepts(5f, new float[] { 5f }, MatchSolverTerm.MinGain), "one block cannot estimate noise");
        }

        [Test]
        public void PassSeed_IsTheSameForEveryCandidateOfAPass()
        {
            Assert.AreEqual(MatchSolverTerm.PassSeed(7UL, 2), MatchSolverTerm.PassSeed(7UL, 2));
            var seeds = Enumerable.Range(0, 8).Select(p => MatchSolverTerm.PassSeed(7UL, p)).Distinct().Count();
            Assert.AreEqual(8, seeds, "each pass has its own seed");
            Assert.AreNotEqual(MatchSolverTerm.PassSeed(7UL, 1), MatchSolverTerm.PassSeed(8UL, 1), "the run seed matters");

            // And the search really hands one seed to every candidate of a pass (common random numbers).
            var seen = new List<ulong>();
            var spec = Quadratic(new float[] { 0f, 0f }, new float[] { 3f, 3f }, 1, seen);
            MatchSolverTerm.Search(spec);
            ulong first = MatchSolverTerm.PassSeed(spec.RunSeed, 0);
            ulong holdout = MatchSolverTerm.PassSeed(spec.RunSeed, spec.MaxPasses + 1);
            CollectionAssert.AreEquivalent(new[] { first, holdout }, seen.Distinct().ToArray(), "one pass, so the pass seed and the holdout seed");
            Assert.Greater(seen.Count(x => x == first), 4, "the current point, every screened move and the confirmed ones all share it");
        }

        [Test]
        public void FreeIndices_AfterTheCap_OnlyMovedValuesStayFree()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, MatchSolverTerm.FreeIndices(new[] { false, false, false, false }, 2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, MatchSolverTerm.FreeIndices(new[] { true, false, false, false }, 2), "one moved, the cap is two: all stay free");
            CollectionAssert.AreEqual(new[] { 0, 2 }, MatchSolverTerm.FreeIndices(new[] { true, false, true, false }, 2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, MatchSolverTerm.FreeIndices(new[] { true, false, true, false }, 3));
        }

        [Test]
        public void RevertCount_RevertsLastFirst_UntilTheGainHolds()
        {
            var bar = new[] { 0.1f, 0.1f, 0.1f };
            Assert.AreEqual(0, MatchSolverTerm.RevertCount(new[] { 0.5f, 0.6f, 0.7f }, bar), "everything holds");
            Assert.AreEqual(2, MatchSolverTerm.RevertCount(new[] { 0.5f, 0.01f, 0.02f }, bar), "the last two do not hold; the first does");
            Assert.AreEqual(3, MatchSolverTerm.RevertCount(new[] { 0.0f, 0.01f, 0.02f }, bar), "nothing holds");
            Assert.AreEqual(1, MatchSolverTerm.RevertCount(new[] { 0.5f, 0.6f, 0.05f }, bar), "only the last");
            Assert.AreEqual(0, MatchSolverTerm.RevertCount(new[] { 0.5f, 0.0f, 0.5f }, bar), "the moves left hold as a whole, so none goes");
            Assert.AreEqual(0, MatchSolverTerm.RevertCount(new float[0], new float[0]));
        }

        // ---- the search on a synthetic score ----

        // Four blocks that differ only by a constant: the paired differences between two candidates have no spread.
        private static MatchEval Make(float score, bool blocks) =>
            new MatchEval { Score = score, Blocks = blocks ? new[] { score, score, score, score } : null, Matches = 10 };

        // f(v) = sum of (v_i - target_i)^2 on every seed but the holdout's, where moving value 1 above zero costs 100 (a gain that does not hold).
        private static MatchSearchSpec Quadratic(float[] start, float[] target, int passes, List<ulong> seeds, int maxMoved = 3)
        {
            var spec = new MatchSearchSpec
            {
                Keys = start.Select((_, i) => "V" + i).ToArray(),
                Start = start,
                Lo = start.Select(_ => 0f).ToArray(),
                Hi = start.Select(_ => 5f).ToArray(),
                Step = start.Select(_ => 1f).ToArray(),
                MaxPasses = passes,
                MaxMovedValues = maxMoved,
                ScreenReplicates = 1,
                ConfirmReplicates = 4,
                ConfirmBest = 3,
                RunSeed = 42UL,
            };
            ulong holdoutSeed = MatchSolverTerm.PassSeed(spec.RunSeed, passes + 1);
            spec.Evaluate = (values, reps, seed, blocks) =>
            {
                seeds?.Add(seed);
                float f = 0f;
                for (int i = 0; i < values.Length; i++) f += (values[i] - target[i]) * (values[i] - target[i]);
                if (seed == holdoutSeed && values.Length > 1 && values[1] > 0f) f += 100f;
                return Make(f, blocks);
            };
            return spec;
        }

        [Test]
        public void Search_MovesTowardTheTarget_NeverPastTheCap()
        {
            var spec = Quadratic(new float[] { 0f, 0f, 0f, 0f }, new float[] { 3f, 3f, 3f, 3f }, 6, null, maxMoved: 2);
            // No holdout penalty here: the same score on every seed.
            spec.Evaluate = (v, r, s, b) =>
            {
                float f = 0f;
                for (int i = 0; i < v.Length; i++) f += (v[i] - 3f) * (v[i] - 3f);
                return Make(f, b);
            };
            var result = MatchSolverTerm.Search(spec);

            int distinct = result.Moves.Select(m => m.Index).Distinct().Count();
            Assert.LessOrEqual(distinct, 2, "at most MaxMovedValues distinct values change");
            Assert.AreEqual(result.Moves.Count, result.Passes);
            Assert.Greater(result.Passes, 0);
            Assert.Less(result.Values.Select(v => (v - 3f) * (v - 3f)).Sum(), 36f, "the score fell");
            foreach (var m in result.Kept) Assert.AreEqual(1f, Math.Abs(m.To - m.From), "one step at a time");
        }

        [Test]
        public void Search_RevertsAMoveThatDoesNotHoldOnFreshSeeds()
        {
            // Pass 1 moves V0 (first of two equal gains), pass 2 moves V1; V1 costs 100 on the holdout seeds only.
            var spec = Quadratic(new float[] { 0f, 0f }, new float[] { 3f, 3f }, 2, null);
            var result = MatchSolverTerm.Search(spec);

            Assert.AreEqual(2, result.Moves.Count);
            Assert.AreEqual(0, result.Moves[0].Index);
            Assert.AreEqual(1, result.Moves[1].Index);
            Assert.IsFalse(result.Moves[0].Reverted, "the first move holds");
            Assert.IsTrue(result.Moves[1].Reverted, "the second does not hold on fresh seeds and goes");
            CollectionAssert.AreEqual(new[] { 1f, 0f }, result.Values, "only the move that held is in the proposal");
            Assert.AreEqual(1, result.Reverted.Count());
            Assert.Less(result.HoldoutScoreFinal, result.HoldoutScoreStart, "what is left still beats the start on fresh seeds");
        }

        [Test]
        public void Search_NoMoveThatBeatsTheGuard_ChangesNothing()
        {
            var spec = Quadratic(new float[] { 3f, 3f }, new float[] { 3f, 3f }, 3, null);
            var result = MatchSolverTerm.Search(spec);
            Assert.AreEqual(0, result.Moves.Count);
            CollectionAssert.AreEqual(spec.Start, result.Values);
            Assert.IsNull(result.Aborted);
        }

        [Test]
        public void Search_NeverLeavesTheRangeItWasGiven()
        {
            var spec = Quadratic(new float[] { 4f }, new float[] { 100f }, 6, null);   // wants to run far past the top limit of 5
            var result = MatchSolverTerm.Search(spec);
            Assert.AreEqual(5f, result.Values[0], "stops at the limit");
        }

        [Test]
        public void SeatCheckFlagged_AbortsTheSolve()
        {
            var spec = Quadratic(new float[] { 0f, 0f }, new float[] { 3f, 3f }, 3, null);
            spec.Evaluate = (v, r, s, b) => throw new InvalidOperationException("nothing may be searched after a flagged seat check");
            spec.SeatCheck = values => "P0 seat 2 wins 41.0 %";
            var result = MatchSolverTerm.Search(spec);

            Assert.IsNotNull(result.Aborted);
            StringAssert.Contains("seat 2", result.Aborted);
            CollectionAssert.AreEqual(spec.Start, result.Values, "no value changed");
            Assert.AreEqual(0, result.Moves.Count);
            Assert.AreEqual(0, result.Evaluations);
        }

        // ---- the problem and the solver on the shipped assets ----

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private BalanceBook _book;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _book = new BalanceBook(new QuietLog(), _abilities, _classes, _match);
        }

        [OneTimeTearDown]
        public void DisposeOnce() => _book?.Dispose();

        private OracleInputs Inputs() => new OracleInputs { Map = _map, Book = _book, Abilities = _abilities, Classes = _classes, Match = _match };

        [Test]
        public void MatchWeightZero_ProposalHasNoMatchSection()
        {
            Assert.IsTrue(_book.TryProbe("MatchConfig", nameof(MatchConfigSO.CentrePileFood), out float centre, out _, out _, out string why), why);
            var problem = new OracleProblem
            {
                MaxPasses = 1, BandLow = 0f, BandHigh = 1f,
                Free = { new FreeValue { Asset = "MatchConfig", Field = nameof(MatchConfigSO.CentrePileFood), Min = 0f, Max = centre, Step = centre * 0.25f } },
            };
            var offsets = TestAssets.Load<OracleReferenceSO>(OracleMenu.ReferencePath).Offsets();

            var proposal = OracleSolver.Solve(Inputs(), problem, offsets);

            Assert.IsNull(proposal.Match);
            Assert.IsNull(proposal.Before.Match);
            Assert.IsNull(proposal.After.Match);
            Assert.IsNull(proposal.Aborted);
            Assert.AreEqual(0, proposal.Reverted.Count);
            StringAssert.DoesNotContain("matchSearch", proposal.ToJson());
            StringAssert.DoesNotContain("The match search", proposal.ToMarkdown());
        }

        [Test]
        public void MatchWeight_NeedsTheMatchBlockAndTheArena()
        {
            Assert.IsTrue(_book.TryProbe("MatchConfig", nameof(MatchConfigSO.CentrePileFood), out float centre, out _, out _, out string why), why);
            var free = new FreeValue { Asset = "MatchConfig", Field = nameof(MatchConfigSO.CentrePileFood), Min = 0f, Max = centre, Step = 1f };
            var offsets = TestAssets.Load<OracleReferenceSO>(OracleMenu.ReferencePath).Offsets();

            var noBlock = new OracleProblem { MatchWeight = 1f, Free = { free } };
            Assert.Throws<ArgumentException>(() => OracleSolver.Solve(Inputs(), noBlock, offsets));
            var noArena = new OracleProblem { MatchWeight = 1f, Match = new MatchProblemBlock(), Free = { free } };
            var e = Assert.Throws<ArgumentException>(() => OracleSolver.Solve(Inputs(), noArena, offsets));
            StringAssert.Contains("ArenaHalf", e.Message);
        }

        [Test]
        public void DefaultMatchFree_ListsEveryD8Value_AndEachResolves()
        {
            var free = OracleProblem.DefaultMatchFree(Inputs());
            var keys = free.Select(f => f.Key).ToList();
            CollectionAssert.AllItemsAreUnique(keys);

            // What the kits hold, read from the shipped data: every non-Peck ability's cooldown fields and aim radius.
            var expected = new HashSet<string>();
            var classes = new HashSet<ChickenClass>();
            bool deathMark = false;
            foreach (var passive in _abilities.Passives.Where(p => p != null))
            {
                classes.Add(passive.Subclass.ClassOf());
                Assert.IsTrue(FixedKit.TryResolve(LoadoutMode.Fixed, passive, out var kit, out string problem), problem);
                foreach (var a in kit.Where(a => a != null && a.Job != AbilityJob.Peck))
                {
                    foreach (string field in a.CooldownFields) expected.Add(a.name + "." + field);
                    if (a.AimRadiusField != null) expected.Add(a.name + "." + a.AimRadiusField);
                    if (a.Job == AbilityJob.DeathMark) { deathMark = true; expected.Add(a.name + ".LeaderPickWeight"); }
                }
            }
            Assert.IsTrue(deathMark, "the roster holds a Death Mark");
            foreach (string field in new[] { nameof(MatchConfigSO.PersonalPileFood), nameof(MatchConfigSO.ContestedPileFood), nameof(MatchConfigSO.CentrePileFood) })
                expected.Add(_match.name + "." + field);
            foreach (var cls in classes)
            {
                Assert.IsTrue(_classes.TryGet(cls, out var entry));
                expected.Add(entry.Stats.name + "." + nameof(ChickenStatsSO.PeckCastSeconds));
            }

            CollectionAssert.AreEquivalent(expected, keys, "exactly the D8 (b) set: weight, three piles, one cast per class, each ability's cooldowns and radius");

            foreach (var f in free)
            {
                Assert.IsTrue(_book.TryProbe(f.Asset, f.Field, out float shipped, out float lo, out float hi, out string why), $"{f.Key}: {why}");
                Assert.Greater(f.Step, 0f, f.Key);
                float low = float.IsNaN(f.Min) ? lo : Math.Max(f.Min, lo);
                float high = float.IsNaN(f.Max) ? hi : Math.Min(f.Max, hi);
                Assert.IsFalse(float.IsInfinity(low) || float.IsInfinity(high), $"{f.Key}: the solver needs finite limits");
                Assert.LessOrEqual(low, shipped, f.Key);
                Assert.GreaterOrEqual(high, shipped, f.Key);
            }
        }

        private OracleProposal SmallMatchSolve(int threads)
        {
            var free = OracleProblem.DefaultMatchFree(Inputs()).Take(1).ToList();
            var problem = new OracleProblem
            {
                MatchWeight = 1f, MaxPasses = 1, MaxMovedValues = 1, BandLow = 0f, BandHigh = 100f,
                Match = new MatchProblemBlock
                {
                    ScreenReplicates = 1, ConfirmReplicates = 2, ConfirmBest = 1, SeatCheckReplicates = 1, FullReports = false, Threads = threads, RunSeed = 7UL,
                    ArenaHalf = OracleMapBaker.ArenaHalf(), SightArrivalRadius = OracleMenu.BotArrivalRadius(),
                },
            };
            problem.Free.AddRange(free);
            return OracleSolver.Solve(Inputs(), problem, TestAssets.Load<OracleReferenceSO>(OracleMenu.ReferencePath).Offsets());
        }

        // About a minute a solve on 12 threads (every candidate is 1 260 matches per replicate), so the default three-minute limit is too short.
        [Test, Timeout(900000)]
        public void MatchSolve_OnTheShippedAssets_IsThreadIndependent_AndKeepsTheCap()
        {
            var a = SmallMatchSolve(12);
            var b = SmallMatchSolve(5);

            Assert.IsNotNull(a.Match);
            Assert.AreEqual(a.DeltaJson, b.DeltaJson, "the proposal does not depend on the thread count");
            CollectionAssert.AreEqual(a.Match.Moves.Select(m => (m.Key, m.To, m.Reverted)).ToList(), b.Match.Moves.Select(m => (m.Key, m.To, m.Reverted)).ToList());
            Assert.AreEqual(a.Match.Matches, b.Match.Matches);
            Assert.AreEqual(a.Aborted, b.Aborted);
            Assert.LessOrEqual(a.Values.Count(v => v.Changed), 1, "at most MaxMovedValues values change");
            Assert.AreEqual(a.Values.Count(v => v.Changed), a.Match.Kept.Count(), "the values changed are the moves that held");
            if (a.Aborted == null) Assert.Greater(a.Match.Evaluations, 2);
            else Assert.AreEqual(0, a.Values.Count(v => v.Changed), "an aborted solve changes nothing");
            var verdict = _book.Evaluate(a.DeltaJson, "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            StringAssert.Contains("The match search", a.ToMarkdown());
            StringAssert.Contains("\"matchSearch\"", a.ToJson());
        }
    }
}
