using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.Progression;
using NUnit.Framework;
using UnityEngine;
using static CluckWars.Tests.ProgressionFixtures;
using LogLevel = CluckWars.Logging.LogLevel;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Shared builders for the slice 2 progression tests. Balance expectations are read from the loaded
    /// <c>ProgressionConfig.asset</c>, never written as literals: a test with hardcoded inputs once let a
    /// +20% drift stay green (BalanceOracle). Literals appear only with a synthetic config, where the
    /// test is about the formula's mechanics rather than the shipped numbers.
    /// </summary>
    internal static class ProgressionFixtures
    {
        /// <summary>A fixed instant, so rested-day arithmetic never depends on when the suite runs.</summary>
        public static readonly DateTime Noon = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        public static readonly RoundRuleset Ruleset = new RoundRuleset
        {
            ResourceTargetToWin = 40f,
            RoundDurationSeconds = 45f,
            MaxActors = 4,
        };

        public static ProgressionConfigSO Config() =>
            TestAssets.Load<ProgressionConfigSO>(TestAssets.ProgressionConfigPath);

        /// <param name="roleKey">Defaults to a key no roster role has, so slice 2's tests keep meaning what they meant.</param>
        public static RoundOutcome Outcome(string id, DateTime utc, int placement = 1, float banked = 0f, float stolen = 0f,
            string roleKey = "role.test", int rivalsRobbed = 0, int opponentsDisabled = 0) =>
            new RoundOutcome
            {
                SchemaVersion = RoundOutcome.CurrentSchemaVersion,
                RoundId = id,
                EndedAtUtc = ProgressionCalendar.FormatUtc(utc),
                Ruleset = Ruleset,
                DurationSeconds = 45f,
                RoleKey = roleKey,
                Placement = placement,
                BankedTotal = banked,
                StolenTotal = stolen,
                RivalsRobbed = rivalsRobbed,
                OpponentsDisabled = opponentsDisabled,
                Abilities = Array.Empty<AbilityTally>(),
            };

        public static JournalRecord Rec(string id, DateTime utc, int placement = 1, float banked = 0f, float stolen = 0f,
            string roleKey = "role.test", int rivalsRobbed = 0, int opponentsDisabled = 0) =>
            JournalRecord.Of(Outcome(id, utc, placement, banked, stolen, roleKey, rivalsRobbed, opponentsDisabled));

        public static RoundStandings Standings(params (int Actor, int Placement, float Total)[] rows) =>
            new RoundStandings
            {
                Entries = rows.Select(r => new RoundStandingEntry
                {
                    ActorId = r.Actor,
                    RoleKey = r.Actor == 0 ? null : "role." + r.Actor.ToString(CultureInfo.InvariantCulture),
                    Placement = r.Placement,
                    ResourceTotal = r.Total,
                }).ToArray(),
            };

        /// <summary>
        /// The Grain formula restated independently in <c>decimal</c>, where every value this suite
        /// uses is exact, so it needs no epsilon. Inputs must have at most 7 significant digits (the
        /// float → decimal conversion keeps 7).
        /// </summary>
        public static int Reference(ProgressionConfigSO c, float banked, float stolen, int placement, bool rested)
        {
            decimal steal = Math.Min((decimal)c.PerSteal * (decimal)stolen, c.StealCap);
            decimal raw = c.Participation + (decimal)c.PerBank * (decimal)banked + steal + c.PlacementGrain[placement - 1];
            decimal total = raw * (rested ? (decimal)c.RestedMultiplier : 1m);
            return (int)Math.Floor(total);
        }

        public static TimeZoneInfo FixedZone(double hours) => TimeZoneInfo.CreateCustomTimeZone(
            "cw-test-" + hours.ToString(CultureInfo.InvariantCulture), TimeSpan.FromHours(hours), "test", "test");
    }

    // ==== ProgressionRules.Evaluate ====================================================

    public sealed class ProgressionRulesTests
    {
        private ProgressionConfigSO _cfg;
        private readonly List<Object> _copies = new List<Object>();

        [SetUp]
        public void SetUp() => _cfg = Config();

        [TearDown]
        public void TearDown()
        {
            foreach (var copy in _copies)
            {
                if (copy != null) Object.DestroyImmediate(copy);
            }
            _copies.Clear();
        }

        /// <summary>An in-memory copy of the shipped asset, to break or reshape; never saved.</summary>
        private ProgressionConfigSO Copy()
        {
            var copy = Object.Instantiate(_cfg);
            _copies.Add(copy);
            return copy;
        }

        private static RoundOutcome At(int placement, float banked, float stolen) =>
            Outcome("r", Noon, placement, banked, stolen);

        [Test]
        public void Evaluate_MatchesTheDecimalReference_ForEveryPlacementOfTheShippedAsset()
        {
            float[] banked = { 0f, 2.25f, 15f, 39.5f, 44.97f, 200f };
            float[] stolen = { 0f, 1.5f, 3.33f, 9.99f, 10f, 50f };
            int[] priors = { 0, _cfg.RestedRounds };
            var mismatches = new List<string>();
            int cases = 0;

            for (int placement = 1; placement <= _cfg.PlacementGrain.Length; placement++)
            {
                foreach (float b in banked)
                foreach (float s in stolen)
                foreach (int prior in priors)
                {
                    cases++;
                    bool rested = prior < _cfg.RestedRounds;
                    var award = ProgressionRules.Evaluate(At(placement, b, s), _cfg, prior);
                    int expected = Reference(_cfg, b, s, placement, rested);
                    if (award.Status != EvaluationStatus.Ok || award.Grain != expected || award.Rested != rested)
                    {
                        mismatches.Add($"P{placement} banked {b.ToString(CultureInfo.InvariantCulture)} stolen " +
                            $"{s.ToString(CultureInfo.InvariantCulture)} prior {prior}: got {award.Status} {award.Grain} " +
                            $"rested={award.Rested}, expected {expected} rested={rested}");
                    }
                }
            }

            Assert.Greater(cases, 0, "No cases ran: the shipped placement table is empty.");
            Assert.IsEmpty(mismatches, "Evaluate disagrees with the decimal restatement of the formula over " +
                $"{TestAssets.ProgressionConfigPath}:\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Evaluate_IsTotal_RefusingWhatItCannotEvaluate_WithoutThrowing()
        {
            var ok = At(1, 10f, 1f);
            Expect(EvaluationStatus.Ok, ok, _cfg, 0);

            Expect(EvaluationStatus.MissingOutcome, null, _cfg, 0);
            Expect(EvaluationStatus.MissingConfig, ok, null, 0);

            Expect(EvaluationStatus.InvalidPlacement, At(0, 10f, 1f), _cfg, 0);
            Expect(EvaluationStatus.InvalidPlacement, At(-1, 10f, 1f), _cfg, 0);
            Expect(EvaluationStatus.InvalidPlacement, At(_cfg.PlacementGrain.Length + 1, 10f, 1f), _cfg, 0);

            foreach (int schema in new[] { 0, -1, RoundOutcome.CurrentSchemaVersion + 1 })
            {
                var other = At(1, 10f, 1f);
                other.SchemaVersion = schema;
                Expect(EvaluationStatus.UnknownSchema, other, _cfg, 0);
            }

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f })
            {
                Expect(EvaluationStatus.InvalidFacts, At(1, bad, 1f), _cfg, 0);
                Expect(EvaluationStatus.InvalidFacts, At(1, 10f, bad), _cfg, 0);
            }
            Expect(EvaluationStatus.InvalidFacts, ok, _cfg, -1);

            var overflow = Copy();
            overflow.PerBank = 1f;
            Expect(EvaluationStatus.InvalidFacts, At(1, float.MaxValue, 0f), overflow, 0);

            BreakConfig(c => c.PerBank = float.NaN);
            BreakConfig(c => c.PerSteal = -1f);
            BreakConfig(c => c.RestedMultiplier = float.PositiveInfinity);
            BreakConfig(c => c.PlacementGrain = null);
            BreakConfig(c => c.PlacementGrain = new int[0]);
            BreakConfig(c => c.PlacementGrain = new[] { 5, -1 });
            BreakConfig(c => c.Participation = -1);
            BreakConfig(c => c.StealCap = -1);
            BreakConfig(c => c.RestedRounds = -1);

            var destroyed = Copy();
            Object.DestroyImmediate(destroyed);
            Expect(EvaluationStatus.MissingConfig, ok, destroyed, 0);
        }

        private void BreakConfig(Action<ProgressionConfigSO> breakIt)
        {
            var broken = Copy();
            breakIt(broken);
            Expect(EvaluationStatus.InvalidConfig, At(1, 10f, 1f), broken, 0);
        }

        private static void Expect(EvaluationStatus status, RoundOutcome outcome, ProgressionConfigSO config, int prior)
        {
            GrainAward award = default;
            Assert.DoesNotThrow(() => award = ProgressionRules.Evaluate(outcome, config, prior),
                "ProgressionRules.Evaluate must be total: it reports, it never throws.");
            Assert.AreEqual(status, award.Status);
            if (status == EvaluationStatus.Ok) return;

            Assert.IsFalse(award.IsEvaluable);
            Assert.AreEqual(0, award.Grain, "A round that cannot be evaluated earns nothing.");
            Assert.IsFalse(award.Rested);
        }

        [Test]
        public void Rested_CoversExactly_TheFirstRestedRoundsOfADay()
        {
            var outcome = At(2, 12.5f, 2f);
            int n = _cfg.RestedRounds;

            if (n > 0)
            {
                var lastRested = ProgressionRules.Evaluate(outcome, _cfg, n - 1);
                Assert.IsTrue(lastRested.Rested, $"Round {n} of the day (prior {n - 1}) must still be rested.");
                Assert.AreEqual(Reference(_cfg, 12.5f, 2f, 2, true), lastRested.Grain);
            }

            var firstUnrested = ProgressionRules.Evaluate(outcome, _cfg, n);
            Assert.IsFalse(firstUnrested.Rested, $"Round {n + 1} of the day (prior {n}) must not be rested.");
            Assert.AreEqual(Reference(_cfg, 12.5f, 2f, 2, false), firstUnrested.Grain);
        }

        [Test]
        public void Steals_EarnAtMostTheCap()
        {
            Assert.Greater(_cfg.PerSteal, 0f, "The shipped asset must pay for steals, or there is no cap to test.");
            int placement = _cfg.PlacementGrain.Length;
            int unrested = _cfg.RestedRounds; // unrested, so the steal share adds linearly

            int none = ProgressionRules.Evaluate(At(placement, 0f, 0f), _cfg, unrested).Grain;
            float reachesCap = (float)((decimal)_cfg.StealCap / (decimal)_cfg.PerSteal);
            float wayPast = reachesCap * 4f + 10f;

            Assert.AreEqual(none + _cfg.StealCap, ProgressionRules.Evaluate(At(placement, 0f, wayPast), _cfg, unrested).Grain,
                "Past the cap, a round's steals earn exactly StealCap.");
            Assert.AreEqual(none + _cfg.StealCap, ProgressionRules.Evaluate(At(placement, 0f, wayPast * 2f), _cfg, unrested).Grain,
                "Stealing more past the cap earns nothing more.");

            float half = reachesCap / 2f;
            int belowCap = ProgressionRules.Evaluate(At(placement, 0f, half), _cfg, unrested).Grain;
            Assert.AreEqual(Reference(_cfg, 0f, half, placement, false), belowCap);
            if (_cfg.StealCap > 1) Assert.Less(belowCap, none + _cfg.StealCap, "Under the cap, steals earn less than the cap.");
        }

        [Test]
        public void Floor_KeepsOnPaperIntegers_AndNeverRoundsAFractionUp()
        {
            // Synthetic config: only PerBank pays, so the test is about the floor, not the balance.
            var c = Copy();
            c.Participation = 0;
            c.PerSteal = 0f;
            c.StealCap = 0;
            c.PlacementGrain = new[] { 0 };
            c.RestedMultiplier = 1f;
            c.RestedRounds = 0;

            c.PerBank = 0.29f; // 0.29 × 100 is 28.999999999999996 in double
            Assert.AreEqual(29, ProgressionRules.Evaluate(At(1, 100f, 0f), c, 0).Grain,
                "A product that is an exact integer on paper must not floor one short (FloorEpsilon).");
            Assert.AreEqual(28, ProgressionRules.Evaluate(At(1, 99.99f, 0f), c, 0).Grain,
                "The epsilon must never turn a genuinely fractional result up.");

            c.PerBank = 0.7f; // as a float, 0.699999988…, which would floor 0.7 × 1000 to 699
            Assert.AreEqual(700, ProgressionRules.Evaluate(At(1, 1000f, 0f), c, 0).Grain,
                "Float inputs must multiply as the decimal they were authored as.");
            Assert.AreEqual(699, ProgressionRules.Evaluate(At(1, 999.99f, 0f), c, 0).Grain,
                "One hundredth of a unit below is a real fraction and floors down.");
        }
    }

    // ==== ProgressionConfig.asset ======================================================

    public sealed class ProgressionConfigTests
    {
        [Test]
        public void ShippedConfig_IsStructurallySound()
        {
            var cfg = Config();
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            Assert.IsNotNull(cfg.PlacementGrain, "ProgressionConfig.PlacementGrain is null.");
            Assert.GreaterOrEqual(cfg.PlacementGrain.Length, match.MaxPlayers,
                $"ProgressionConfig.PlacementGrain has {cfg.PlacementGrain.Length} entries but MatchConfig.MaxPlayers is " +
                $"{match.MaxPlayers}: a round finished below the last entry cannot be evaluated and earns nothing.");

            for (int i = 0; i < cfg.PlacementGrain.Length; i++)
            {
                Assert.GreaterOrEqual(cfg.PlacementGrain[i], 0, $"PlacementGrain[{i}] is negative.");
                if (i > 0)
                {
                    Assert.LessOrEqual(cfg.PlacementGrain[i], cfg.PlacementGrain[i - 1],
                        $"PlacementGrain must not increase: placement {i + 1} pays more than placement {i}.");
                }
            }

            Assert.GreaterOrEqual(cfg.RestedMultiplier, 1f, "RestedMultiplier below 1 would make the rested bonus a penalty.");
            Assert.GreaterOrEqual(cfg.StealCap, 0);
            Assert.GreaterOrEqual(cfg.RestedRounds, 0);
            Assert.GreaterOrEqual(cfg.Participation, 0);
            Assert.IsTrue(cfg.PerBank >= 0f && !float.IsInfinity(cfg.PerBank), "PerBank must be finite and non-negative.");
            Assert.IsTrue(cfg.PerSteal >= 0f && !float.IsInfinity(cfg.PerSteal), "PerSteal must be finite and non-negative.");

            for (int placement = 1; placement <= match.MaxPlayers; placement++)
            {
                Assert.AreEqual(EvaluationStatus.Ok,
                    ProgressionRules.Evaluate(Outcome("r", Noon, placement), cfg, 0).Status,
                    $"A round finished at placement {placement} must be evaluable under the shipped config.");
            }
        }

        [Test]
        public void CodeDefaults_MirrorTheShippedAsset()
        {
            // ProjectInstaller falls back to a default instance when its slot is empty, so the field
            // initialisers are the numbers a mis-wired build would pay. Keep them equal to the asset.
            var asset = Config();
            var defaults = ScriptableObject.CreateInstance<ProgressionConfigSO>();
            try
            {
                var fields = typeof(ProgressionConfigSO).GetFields(BindingFlags.Instance | BindingFlags.Public);
                Assert.IsNotEmpty(fields);

                var exempt = fields.Where(IsAssetReferenceCatalogue).Select(f => f.Name).ToList();
                CollectionAssert.AreEqual(new[] { nameof(ProgressionConfigSO.Records) }, exempt,
                    "Only a catalogue of asset references may skip this check — a field initialiser cannot name an " +
                    "asset, so its code default is necessarily empty. RecordAssetTests checks that list instead. A " +
                    "new exemption here must be deliberate.");

                foreach (var field in fields)
                {
                    if (IsAssetReferenceCatalogue(field)) continue;

                    object a = field.GetValue(asset), d = field.GetValue(defaults);
                    if (a is Array arrayA && d is Array arrayD)
                        CollectionAssert.AreEqual(arrayA, arrayD, $"ProgressionConfigSO.{field.Name}: code default differs from the asset.");
                    else
                        Assert.AreEqual(a, d, $"ProgressionConfigSO.{field.Name}: code default differs from the asset.");
                }
            }
            finally
            {
                Object.DestroyImmediate(defaults);
            }
        }

        /// <summary>An array of Unity assets — a catalogue no field initialiser could ever mirror.</summary>
        private static bool IsAssetReferenceCatalogue(FieldInfo field) =>
            field.FieldType.IsArray && typeof(Object).IsAssignableFrom(field.FieldType.GetElementType());

        [Test]
        public void ProjectContextPrefab_ProgressionConfigSlot_ResolvesToTheRealAsset()
        {
            // The balance is re-derived from this config on every load. An empty slot is only a warning
            // at runtime (ProjectInstaller falls back to the SO's code defaults), so every past round's
            // Grain would silently come from numbers nobody authored.
            var prefab = TestAssets.Load<GameObject>(TestAssets.ProjectContextPrefabPath);
            var cfg    = Config();

            var installer = prefab.GetComponentInChildren<ProjectInstaller>(true);
            Assert.IsNotNull(installer,
                $"{TestAssets.ProjectContextPrefabPath} has no ProjectInstaller. Nothing would be bound at all.");

            var slot = TestAssets.PrivateField<ProgressionConfigSO>(installer, "_progressionConfig");

            Assert.IsNotNull(slot,
                "ProjectInstaller._progressionConfig is empty on ProjectContext.prefab. Grain would silently be " +
                $"computed from ProgressionConfigSO's C# field initialisers instead of {TestAssets.ProgressionConfigPath}.");
            Assert.AreSame(cfg, slot,
                $"ProjectInstaller._progressionConfig points at a different ProgressionConfigSO than {TestAssets.ProgressionConfigPath}.");
        }
    }

    // ==== ProgressionLedger.Fold =======================================================

    public sealed class ProgressionLedgerTests
    {
        private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
        private ProgressionConfigSO _cfg;
        private readonly List<Object> _copies = new List<Object>();

        [SetUp]
        public void SetUp() => _cfg = Config();

        [TearDown]
        public void TearDown()
        {
            foreach (var copy in _copies)
            {
                if (copy != null) Object.DestroyImmediate(copy);
            }
            _copies.Clear();
        }

        private static List<JournalRecord> SampleJournal()
        {
            var day1 = Noon;
            var day2 = Noon.AddDays(1);
            return new List<JournalRecord>
            {
                Rec("a", day1.AddHours(-3), 1, 40f, 2f),
                Rec("b", day1.AddHours(-2), 3, 12.5f),
                Rec("c", day1.AddHours(-1), 2, 20f, 11f),
                Rec("d", day1, 4),
                Rec("f", day1, 2, 5f),              // same instant as d: the RoundId breaks the tie
                Rec("e", day1.AddHours(1), 1, 44.97f, 3.33f),
                Rec("g", day2, 1, 40f),
                Rec("h", day2.AddMinutes(5), 99, 1f), // not evaluable: past the placement table
                Rec("b", day1.AddHours(-2), 3, 12.5f),  // exact duplicate of b
                Rec("c", day1.AddHours(-1), 1, 20f, 11f), // conflicting duplicate of c
            };
        }

        private static readonly string[] SampleIds = { "a", "b", "c", "d", "e", "f", "g", "h" };

        [Test]
        public void Fold_IsDeterministic_AndOrderIndependent()
        {
            var journal = SampleJournal();
            var baseline = ProgressionLedger.Fold(journal, _cfg, Utc);
            AssertSameLedger(baseline, ProgressionLedger.Fold(journal, _cfg, Utc), "refolding the same journal");

            Assert.Greater(baseline.RoundsPlayed, 0, "Precondition: the sample journal must earn something.");
            Assert.AreEqual(1, baseline.NotEvaluable, "Precondition: exactly one sample round is not evaluable.");

            foreach (int seed in new[] { 1, 2, 3, 7, 42, 1234, 99991 })
            {
                var shuffled = new List<JournalRecord>(journal);
                var rng = new System.Random(seed);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }
                AssertSameLedger(baseline, ProgressionLedger.Fold(shuffled, _cfg, Utc), $"shuffle seed {seed}");
            }
        }

        private static void AssertSameLedger(ProgressionLedger expected, ProgressionLedger actual, string what)
        {
            Assert.AreEqual(expected.GrainBalance, actual.GrainBalance, $"{what}: GrainBalance");
            Assert.AreEqual(expected.RoundsPlayed, actual.RoundsPlayed, $"{what}: RoundsPlayed");
            Assert.AreEqual(expected.Wins, actual.Wins, $"{what}: Wins");
            Assert.AreEqual(expected.TotalBanked, actual.TotalBanked, $"{what}: TotalBanked");
            Assert.AreEqual(expected.TotalStolen, actual.TotalStolen, $"{what}: TotalStolen");
            Assert.AreEqual(expected.NotEvaluable, actual.NotEvaluable, $"{what}: NotEvaluable");
            Assert.AreEqual(expected.DuplicateRecords, actual.DuplicateRecords, $"{what}: DuplicateRecords");
            CollectionAssert.AreEqual(expected.ConflictingRoundIds, actual.ConflictingRoundIds, $"{what}: ConflictingRoundIds");

            foreach (var id in SampleIds)
            {
                Assert.AreEqual(expected.TryGetAward(id, out var e), actual.TryGetAward(id, out var a), $"{what}: round {id} presence");
                Assert.AreEqual(e.Status, a.Status, $"{what}: round {id} status");
                Assert.AreEqual(e.Grain, a.Grain, $"{what}: round {id} Grain");
                Assert.AreEqual(e.Rested, a.Rested, $"{what}: round {id} rested");
            }
        }

        [Test]
        public void Fold_ADuplicateRoundId_CountsOnce()
        {
            var record = Rec("x", Noon, 1, 30f, 4f);
            var once = ProgressionLedger.Fold(new[] { record }, _cfg, Utc);
            var twice = ProgressionLedger.Fold(new[] { record, Rec("x", Noon, 1, 30f, 4f) }, _cfg, Utc);

            Assert.AreEqual(1, twice.RoundsPlayed);
            Assert.AreEqual(1, twice.DuplicateRecords);
            Assert.IsEmpty(twice.ConflictingRoundIds, "Identical duplicates are not a conflict.");
            Assert.AreEqual(once.GrainBalance, twice.GrainBalance, "A repeated line must not pay twice.");
            Assert.AreEqual(once.TotalBanked, twice.TotalBanked);
        }

        [Test]
        public void Fold_DifferingDuplicates_ResolveToTheOrdinalSmallestSerialization_InEitherOrder()
        {
            var first = Rec("x", Noon, 1, 30f);
            var last = Rec("x", Noon, _cfg.PlacementGrain.Length, 30f);
            Assert.AreNotEqual(first.Canonical, last.Canonical, "Precondition: the two records differ.");

            var forward = ProgressionLedger.Fold(new[] { first, last }, _cfg, Utc);
            var backward = ProgressionLedger.Fold(new[] { last, first }, _cfg, Utc);

            var chosen = string.CompareOrdinal(first.Canonical, last.Canonical) < 0 ? first : last;
            var expected = ProgressionRules.Evaluate(chosen.Outcome, _cfg, 0);

            foreach (var ledger in new[] { forward, backward })
            {
                CollectionAssert.AreEqual(new[] { "x" }, ledger.ConflictingRoundIds);
                Assert.AreEqual(1, ledger.RoundsPlayed);
                Assert.IsTrue(ledger.TryGetAward("x", out var award));
                Assert.AreEqual(expected.Grain, award.Grain, "The chosen version must not depend on read order.");
            }
        }

        [Test]
        public void Fold_Rested_CountsOnlyEvaluableRounds_PerDay()
        {
            int n = _cfg.RestedRounds;
            var records = new List<JournalRecord>
            {
                Rec("bad", Noon.AddMinutes(-30), _cfg.PlacementGrain.Length + 1), // earliest, not evaluable
            };
            for (int i = 0; i <= n; i++) records.Add(Rec("r" + i.ToString("D2", CultureInfo.InvariantCulture), Noon.AddMinutes(i), 2, 10f));
            records.Add(Rec("tomorrow", Noon.AddDays(1), 2, 10f));

            var ledger = ProgressionLedger.Fold(records, _cfg, Utc);

            Assert.AreEqual(1, ledger.NotEvaluable);
            for (int i = 0; i <= n; i++)
            {
                Assert.IsTrue(ledger.TryGetAward("r" + i.ToString("D2", CultureInfo.InvariantCulture), out var award));
                Assert.AreEqual(i < n, award.Rested,
                    $"Round {i + 1} of the day: the first {n} evaluable rounds are rested (an unevaluable one takes no slot).");
                Assert.AreEqual(Reference(_cfg, 10f, 0f, 2, i < n), award.Grain);
            }

            Assert.IsTrue(ledger.TryGetAward("tomorrow", out var next));
            Assert.AreEqual(n > 0, next.Rested, "A new day starts a new rested count.");
        }

        [Test]
        public void Fold_TheRestedDay_IsTheLocalDayOfTheGivenZone()
        {
            // Mechanics, so a synthetic config: one rested round a day.
            var c = Object.Instantiate(_cfg);
            _copies.Add(c);
            c.RestedRounds = 1;
            c.RestedMultiplier = 2f;

            var morning = Rec("morning", new DateTime(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc), 1, 10f);
            var lateUtc = Rec("late", new DateTime(2026, 9, 13, 23, 30, 0, DateTimeKind.Utc), 1, 10f);
            var journal = new[] { morning, lateUtc };

            ProgressionLedger.Fold(journal, c, TimeZoneInfo.Utc).TryGetAward("late", out var inUtc);
            ProgressionLedger.Fold(journal, c, FixedZone(2)).TryGetAward("late", out var inPlusTwo);

            Assert.IsFalse(inUtc.Rested, "In UTC both rounds fall on the 13th; the second is past the one rested slot.");
            Assert.IsTrue(inPlusTwo.Rested, "At UTC+2, 23:30Z is 01:30 on the 14th: the first round of a new local day.");
        }

        [Test]
        public void Fold_IsTotal_ForEmptyNullAndUnusableRecords()
        {
            Assert.DoesNotThrow(() => ProgressionLedger.Fold(null, _cfg, Utc));
            Assert.AreEqual(0, ProgressionLedger.Fold(null, _cfg, Utc).GrainBalance);
            Assert.AreEqual(0, ProgressionLedger.Fold(new JournalRecord[0], _cfg, null).RoundsPlayed);

            var noTime = Outcome("t", Noon);
            noTime.EndedAtUtc = "yesterday";
            var ledger = ProgressionLedger.Fold(new[] { default(JournalRecord), JournalRecord.Of(noTime), Rec("ok", Noon) }, _cfg, Utc);

            Assert.AreEqual(2, ledger.NotEvaluable, "A record with no outcome and one with no parseable instant earn nothing.");
            Assert.AreEqual(1, ledger.RoundsPlayed);
            Assert.IsTrue(ledger.TryGetAward("t", out var award));
            Assert.AreEqual(EvaluationStatus.InvalidFacts, award.Status);
        }
    }

    // ==== MatchTracker =================================================================

    public sealed class MatchTrackerTests
    {
        private const int Local = 1042, Bot = 1038, Rival = 1040, None = 0;

        private RecordingLogService _log;
        private MatchTracker _tracker;
        private List<RoundOutcome> _outcomes;
        private int _ids;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLogService();
            _ids = 0;
            _tracker = new MatchTracker(_log, () => Noon, () => "round-" + (++_ids).ToString(CultureInfo.InvariantCulture));
            _outcomes = new List<RoundOutcome>();
            _tracker.OutcomeReady += _outcomes.Add;
        }

        [TestCase(Local)]
        [TestCase(-7)] // NetworkId raw values are unchecked to int and may be negative
        public void BotEvents_NeverLandInTheLocalOutcome(int local)
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceStolen(Bot, Rival, 5f);
            _tracker.OpponentDisabled(Bot);
            _tracker.AbilityResolved(Bot, "ability.bot", true);
            _tracker.ResourceBanked(Bot, 7f);
            _tracker.ResourceStolen(local, Bot, 1.5f);
            _tracker.AbilityResolved(local, "ability.mine", false);
            _tracker.RoundEnded(Standings((Bot, 1, 20f), (local, 2, 6f), (Rival, 3, 0f)), local, 45f);

            var outcome = _outcomes.Single();
            Assert.AreEqual(1.5f, outcome.StolenTotal);
            Assert.AreEqual(1, outcome.RivalsRobbed);
            Assert.AreEqual(0, outcome.OpponentsDisabled);
            CollectionAssert.AreEqual(new[] { "ability.mine" }, outcome.Abilities.Select(a => a.Key));
            Assert.AreEqual(2, outcome.Placement);
            Assert.AreEqual(6f, outcome.BankedTotal);
            Assert.AreEqual("role." + local.ToString(CultureInfo.InvariantCulture), outcome.RoleKey);
        }

        [Test]
        public void RivalsRobbed_IsDistinct_AndExcludesNoneAndSelf()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceStolen(Local, Bot, 1f);
            _tracker.ResourceStolen(Local, Bot, 2f);
            _tracker.ResourceStolen(Local, Rival, 1f);
            _tracker.ResourceStolen(Local, None, 1f);
            _tracker.ResourceStolen(Local, Local, 1f);
            _tracker.ResourceStolen(Local, 1039, 0f);         // a zero take robs nobody
            _tracker.ResourceStolen(Local, 1039, float.NaN);
            _tracker.RoundEnded(Standings((Local, 1, 0f)), Local, 45f);

            Assert.AreEqual(2, _outcomes.Single().RivalsRobbed, "Bot and Rival only.");
            Assert.AreEqual(6f, _outcomes.Single().StolenTotal, "Every positive take counts toward the total.");
        }

        [Test]
        public void AnAbandonedRound_IsDiscarded_WhenTheNextStarts()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceStolen(Local, Bot, 4f);
            _tracker.OpponentDisabled(Local);
            _tracker.RoundStarted(Ruleset); // no RoundEnded for the first
            _tracker.ResourceStolen(Local, Bot, 1f);
            _tracker.RoundEnded(Standings((Local, 1, 0f)), Local, 45f);

            var outcome = _outcomes.Single();
            Assert.AreEqual(1f, outcome.StolenTotal, "The abandoned round's steal must not leak into the next.");
            Assert.AreEqual(0, outcome.OpponentsDisabled);
            Assert.IsEmpty(_log.OfLevel(LogLevel.Warn).Concat(_log.OfLevel(LogLevel.Error)),
                "An abandoned round is a legal state (a peer left mid-round): Debug, not Warn.");
        }

        [Test]
        public void ANoneLocalActor_RecordsNothing_AndLogsDebugOnly()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceStolen(Bot, Rival, 3f);
            _tracker.RoundEnded(Standings((Bot, 1, 3f)), None, 45f);

            Assert.IsEmpty(_outcomes);
            Assert.IsFalse(_tracker.IsRoundOpen);
            Assert.IsTrue(_log.OfLevel(LogLevel.Debug).Any(e => e.Source == "MatchTracker"));
            Assert.IsEmpty(_log.OfLevel(LogLevel.Warn).Concat(_log.OfLevel(LogLevel.Error)),
                "No local actor (a spectating peer) is a legal state.");
        }

        [Test]
        public void ALocalActorMissingFromTheStandings_RecordsNothing_AndWarns()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.RoundEnded(Standings((Bot, 1, 3f)), Local, 45f);

            Assert.IsEmpty(_outcomes);
            Assert.AreEqual(1, _log.OfLevel(LogLevel.Warn).Count);
        }

        [Test]
        public void RoundEnded_WithNoRoundOpen_RecordsNothing_AndWarns()
        {
            _tracker.RoundEnded(Standings((Local, 1, 3f)), Local, 45f);
            Assert.IsEmpty(_outcomes);
            Assert.AreEqual(1, _log.OfLevel(LogLevel.Warn).Count);
        }

        [Test]
        public void EventsOutsideARound_AreDropped()
        {
            _tracker.ResourceStolen(Local, Bot, 9f);
            _tracker.OpponentDisabled(Local);
            _tracker.AbilityResolved(Local, "ability.a", true);
            _tracker.RoundStarted(Ruleset);
            _tracker.RoundEnded(Standings((Local, 1, 0f)), Local, 45f);

            var outcome = _outcomes.Single();
            Assert.AreEqual(0f, outcome.StolenTotal);
            Assert.AreEqual(0, outcome.OpponentsDisabled);
            Assert.IsEmpty(outcome.Abilities);
        }

        [Test]
        public void BankedTotal_ComesFromTheStandings_NotFromResourceBanked()
        {
            // A Spoiler-style match-end bonus lands in the standings but is never announced as
            // ResourceBanked, and a flush can be refused: the standings are the authority.
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceBanked(Local, 3f);
            _tracker.ResourceBanked(Local, 2f);
            _tracker.RoundEnded(Standings((Local, 1, 15f), (Bot, 2, 5f)), Local, 45f);

            Assert.AreEqual(15f, _outcomes.Single().BankedTotal);
        }

        [Test]
        public void TheExecute_CountsAsASteal_OfTheTransferredCargoOnly()
        {
            // What ChickenCargo.AnnounceExecuteSteal emits: the victim's cargo, read before the transfer.
            // The ExecuteBounty is paid into the bounty bag and is never announced (pinned at source in
            // MatchEventSinkTests), so it cannot reach StolenTotal. A kill is a separate OpponentDisabled.
            const float victimCargo = 6.5f;
            _tracker.RoundStarted(Ruleset);
            _tracker.ResourceStolen(Local, Rival, victimCargo);
            _tracker.OpponentDisabled(Local);
            _tracker.RoundEnded(Standings((Local, 1, 20f), (Rival, 2, 0f)), Local, 45f);

            var outcome = _outcomes.Single();
            Assert.AreEqual(victimCargo, outcome.StolenTotal);
            Assert.AreEqual(1, outcome.RivalsRobbed);
            Assert.AreEqual(1, outcome.OpponentsDisabled);
        }

        [Test]
        public void TheOutcome_CarriesTheRoundsFacts_AndSortsAbilitiesOrdinally()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.AbilityResolved(Local, "ability.b", true);
            _tracker.AbilityResolved(Local, "ability.b", false);
            _tracker.AbilityResolved(Local, "ability.a", false);
            _tracker.AbilityResolved(Local, "ability.B", true);
            _tracker.RoundEnded(Standings((Local, 1, 12f)), Local, 44.97f);

            var o = _outcomes.Single();
            Assert.AreEqual(RoundOutcome.CurrentSchemaVersion, o.SchemaVersion);
            Assert.AreEqual("round-1", o.RoundId);
            Assert.AreEqual(ProgressionCalendar.FormatUtc(Noon), o.EndedAtUtc);
            Assert.AreEqual(Ruleset.ResourceTargetToWin, o.Ruleset.ResourceTargetToWin);
            Assert.AreEqual(Ruleset.RoundDurationSeconds, o.Ruleset.RoundDurationSeconds);
            Assert.AreEqual(Ruleset.MaxActors, o.Ruleset.MaxActors);
            Assert.AreEqual(44.97f, o.DurationSeconds);
            CollectionAssert.AreEqual(new[] { "ability.B", "ability.a", "ability.b" }, o.Abilities.Select(a => a.Key));
            Assert.AreEqual(2, o.Abilities[2].Casts);
            Assert.AreEqual(1, o.Abilities[2].Connected);
        }

        [Test]
        public void AMissingAbilityKey_IsOneError_PerSession()
        {
            _tracker.RoundStarted(Ruleset);
            _tracker.AbilityResolved(Local, null, true);
            _tracker.AbilityResolved(Local, "", false);
            _tracker.AbilityResolved(Bot, null, false);
            _tracker.RoundEnded(Standings((Local, 1, 0f)), Local, 45f);

            Assert.AreEqual(1, _log.OfLevel(LogLevel.Error).Count);
            Assert.IsEmpty(_outcomes.Single().Abilities);
        }

        [Test]
        public void AThrowingSubscriber_NeverEscapes_AndIsReportedOncePerEvent()
        {
            var tracker = new MatchTracker(_log, () => Noon, () => "id");
            int opened = 0;
            var received = new List<RoundOutcome>();
            tracker.RoundOpened += () => throw new InvalidOperationException("opened");
            tracker.RoundOpened += () => opened++;
            tracker.OutcomeReady += _ => throw new InvalidOperationException("outcome");
            tracker.OutcomeReady += received.Add;

            for (int i = 0; i < 2; i++)
            {
                Assert.DoesNotThrow(() => tracker.RoundStarted(Ruleset));
                Assert.DoesNotThrow(() => tracker.RoundEnded(Standings((Local, 1, 3f)), Local, 45f));
            }

            Assert.AreEqual(2, opened, "The other RoundOpened subscribers still run.");
            Assert.AreEqual(2, received.Count, "The other OutcomeReady subscribers still run.");
            Assert.AreEqual(4, tracker.SubscriberFailureCount);
            var errors = _log.OfLevel(LogLevel.Error);
            Assert.AreEqual(2, errors.Count, "One Error per event kind, then only counted.");
            Assert.IsTrue(errors.All(e => e.Exception is InvalidOperationException), "The Error carries the exception.");
            Assert.IsFalse(tracker.IsRoundOpen);
        }

        [Test]
        public void AnOutcomeWithNoListener_IsAWarning()
        {
            var tracker = new MatchTracker(_log);
            tracker.RoundStarted(Ruleset);
            tracker.RoundEnded(Standings((Local, 1, 3f)), Local, 45f);
            Assert.AreEqual(1, _log.OfLevel(LogLevel.Warn).Count);
        }
    }
}
