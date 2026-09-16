using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CluckWars.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static CluckWars.Tests.ProgressionFixtures;
using LogLevel = CluckWars.Logging.LogLevel;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>Builders for slice 3's identity tests: runtime record definitions and profile events.</summary>
    internal static class IdentityFixtures
    {
        public const string Warrior = "class.warrior";
        public const string Speedy = "class.speedy";
        public const string Fatty = "class.fatty";
        public const string Assassin = "class.assassin";

        /// <summary>
        /// A record definition that exists only for one test. The key goes in through the private
        /// field the asset pipeline fills, because runtime code may only ever read it.
        /// </summary>
        public static RecordDefinitionSO Definition(string key, RecordScope scope = RecordScope.SingleRound,
            string title = "Test Title", RecordGroup group = RecordGroup.Foraging, bool losing = true)
        {
            var definition = ScriptableObject.CreateInstance<RecordDefinitionSO>();
            definition.name = key;
            definition.DisplayName = title;
            definition.Description = "A record made by a test.";
            definition.Title = title;
            definition.Group = group;
            definition.AchievableWhileLosing = losing;
            definition.Scope = scope;
            definition.Conditions = Array.Empty<RecordCondition>();
            definition.RoleKey = string.Empty;

            typeof(RecordDefinitionSO).GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(definition, key);
            return definition;
        }

        public static RecordCondition Condition(RecordMetric metric, RecordComparison comparison, float value) =>
            new RecordCondition { Metric = metric, Comparison = comparison, Value = value };

        public static ProfileEvent Event(string type, string value, string id, DateTime at) =>
            ProfileEvent.Of(type, value, id, at);

        /// <summary>A ledger's canonical evaluable rounds for these records — the input every fold takes.</summary>
        public static IReadOnlyList<RoundOutcome> Canonical(ProgressionConfigSO config, params JournalRecord[] records) =>
            ProgressionLedger.Fold(records, config, TimeZoneInfo.Utc).EvaluableRounds;
    }

    // ==== RecordEngine ==================================================================

    public sealed class RecordEngineTests
    {
        private ProgressionConfigSO _cfg;
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp() => _cfg = Config();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
            {
                if (made != null) Object.DestroyImmediate(made);
            }

            _made.Clear();
        }

        private RecordDefinitionSO Definition(string key, RecordScope scope = RecordScope.SingleRound)
        {
            var definition = IdentityFixtures.Definition(key, scope);
            _made.Add(definition);
            return definition;
        }

        private IReadOnlyList<RecordStanding> Evaluate(IReadOnlyList<RoundOutcome> rounds, params RecordDefinitionSO[] definitions) =>
            RecordEngine.Evaluate(rounds, definitions, _cfg.MasteryRoundThresholds, TimeZoneInfo.Utc);

        /// <summary>Five rounds whose facts several of the shipped records key off.</summary>
        private JournalRecord[] History() => new[]
        {
            Rec("r1", Noon, placement: 3, banked: 8f, stolen: 2f, roleKey: IdentityFixtures.Warrior),
            Rec("r2", Noon.AddHours(1), placement: 1, banked: 41f, stolen: 0f, roleKey: IdentityFixtures.Warrior),
            Rec("r3", Noon.AddHours(2), placement: 2, banked: 3f, stolen: 22f, roleKey: IdentityFixtures.Speedy, rivalsRobbed: 3),
            Rec("r4", Noon.AddHours(3), placement: 1, banked: 44f, stolen: 1f, roleKey: IdentityFixtures.Speedy),
            Rec("r5", Noon.AddHours(4), placement: 4, banked: 0f, stolen: 0f, roleKey: IdentityFixtures.Fatty),
        };

        [Test]
        public void TheSameHistory_InAnyOrder_GivesTheSameStandings()
        {
            var forward = History();
            var shuffled = new[] { forward[3], forward[0], forward[4], forward[2], forward[1] };

            var definition = Definition("record.rich");
            definition.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 40f) };

            var a = Evaluate(IdentityFixtures.Canonical(_cfg, forward), definition);
            var b = Evaluate(IdentityFixtures.Canonical(_cfg, shuffled), definition);

            Assert.AreEqual(a[0].Earned, b[0].Earned);
            Assert.AreEqual(a[0].EarnedRoundId, b[0].EarnedRoundId,
                "The earning round must not depend on the order the journal happened to be read in.");
            Assert.AreEqual("r2", a[0].EarnedRoundId, "The FIRST qualifying round earns it, not the best one.");
            Assert.AreEqual(a[0].EarnedLocalDay, b[0].EarnedLocalDay);
        }

        [Test]
        public void Evaluating_MutatesNothingItWasGiven()
        {
            var rounds = IdentityFixtures.Canonical(_cfg, History());
            string[] before = rounds.Select(JsonUtility.ToJson).ToArray();

            var definition = Definition("record.rich");
            definition.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 40f) };
            Evaluate(rounds, definition);
            Evaluate(rounds, definition);

            CollectionAssert.AreEqual(before, rounds.Select(JsonUtility.ToJson).ToArray(),
                "RecordEngine must read the rounds and never write to them.");
        }

        [Test]
        public void ARecordDefinedLater_CreditsPlayAlreadyDone()
        {
            var rounds = IdentityFixtures.Canonical(_cfg, History());

            // The history is folded before this record exists — exactly what shipping a new record in
            // a later build looks like. Nothing about a record is in the journal, so it can look back.
            var newcomer = Definition("record.newcomer");
            newcomer.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.StolenTotal, RecordComparison.AtLeast, 20f) };

            var standing = Evaluate(rounds, newcomer)[0];
            Assert.IsTrue(standing.Earned, "A record added later must credit rounds already played.");
            Assert.AreEqual("r3", standing.EarnedRoundId, "It must name the earliest round that qualified.");
            Assert.AreEqual(ProgressionCalendar.FormatDay(Noon.Date), standing.EarnedLocalDay);
        }

        [Test]
        public void ARoleFilter_OnlyCountsThatRole()
        {
            var rounds = IdentityFixtures.Canonical(_cfg, History());

            var speedyOnly = Definition("record.speedy_hauler");
            speedyOnly.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 40f) };
            speedyOnly.RoleKey = IdentityFixtures.Speedy;

            Assert.AreEqual("r4", Evaluate(rounds, speedyOnly)[0].EarnedRoundId,
                "r2 banked 41 but as Warrior; the role filter must skip it.");
        }

        [Test]
        public void EveryConditionMustHold_OnOneRound()
        {
            // Won, banked something, banked under 15 — r2 won but banked 41, r1 banked 8 but came 3rd.
            var unbanked = Definition("record.unbanked");
            unbanked.Conditions = new[]
            {
                IdentityFixtures.Condition(RecordMetric.Placement, RecordComparison.Equal, 1f),
                IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.MoreThan, 0f),
                IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.LessThan, 15f),
            };

            Assert.IsFalse(Evaluate(IdentityFixtures.Canonical(_cfg, History()), unbanked)[0].Earned,
                "No round in this history satisfies all three conditions at once.");

            var thin = Rec("r6", Noon.AddHours(5), placement: 1, banked: 9f, roleKey: IdentityFixtures.Fatty);
            var rounds = IdentityFixtures.Canonical(_cfg, History().Concat(new[] { thin }).ToArray());
            Assert.AreEqual("r6", Evaluate(rounds, unbanked)[0].EarnedRoundId);
        }

        [Test]
        public void AFourWayScorelessTie_DoesNotCountAsAQuietWin()
        {
            // The '> 0 banked' condition is what stops a 0-0-0-0 round awarding The Unbanked.
            var unbanked = Definition("record.unbanked");
            unbanked.Conditions = new[]
            {
                IdentityFixtures.Condition(RecordMetric.Placement, RecordComparison.Equal, 1f),
                IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.MoreThan, 0f),
                IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.LessThan, 15f),
            };

            var scoreless = IdentityFixtures.Canonical(_cfg, Rec("r0", Noon, placement: 1, banked: 0f));
            Assert.IsFalse(Evaluate(scoreless, unbanked)[0].Earned);
        }

        [Test]
        public void WinWithEveryRole_NeedsEveryRosterRole_AndNamesTheRoundItCompletedOn()
        {
            var flock = Definition("record.flock", RecordScope.Career);
            flock.CareerKind = CareerRecordKind.WinWithEveryRole;

            var wins = new List<JournalRecord>();
            int hour = 0;
            foreach (string role in UnlockKeyTable.RoleKeys.Take(UnlockKeyTable.RoleKeys.Count - 1))
            {
                wins.Add(Rec("w" + hour, Noon.AddHours(hour++), placement: 1, roleKey: role));
            }

            Assert.IsFalse(Evaluate(IdentityFixtures.Canonical(_cfg, wins.ToArray()), flock)[0].Earned,
                "One role short is not every role.");

            wins.Add(Rec("last", Noon.AddHours(hour), placement: 1, roleKey: UnlockKeyTable.RoleKeys.Last()));
            var standing = Evaluate(IdentityFixtures.Canonical(_cfg, wins.ToArray()), flock)[0];
            Assert.IsTrue(standing.Earned);
            Assert.AreEqual("last", standing.EarnedRoundId, "It is earned on the round that completed the set.");
        }

        [Test]
        public void MasteryLevelOnAnyRole_CountsRoundsPerRole()
        {
            var devoted = Definition("record.devoted_test", RecordScope.Career);
            devoted.CareerKind = CareerRecordKind.MasteryLevelOnAnyRole;
            devoted.CareerValue = 3f;

            // Thresholds 1, 3, 6, ... so level 3 needs six rounds on one role. Five as Warrior and
            // five as Speedy must not add up to it.
            var split = new List<JournalRecord>();
            for (int i = 0; i < 5; i++) split.Add(Rec("w" + i, Noon.AddHours(i), roleKey: IdentityFixtures.Warrior));
            for (int i = 0; i < 5; i++) split.Add(Rec("s" + i, Noon.AddHours(10 + i), roleKey: IdentityFixtures.Speedy));
            Assert.IsFalse(Evaluate(IdentityFixtures.Canonical(_cfg, split.ToArray()), devoted)[0].Earned,
                "Mastery is per role: two half-played roles are not one mastered one.");

            split.Add(Rec("w5", Noon.AddHours(20), roleKey: IdentityFixtures.Warrior));
            var standing = Evaluate(IdentityFixtures.Canonical(_cfg, split.ToArray()), devoted)[0];
            Assert.IsTrue(standing.Earned);
            Assert.AreEqual("w5", standing.EarnedRoundId);
        }

        [Test]
        public void ABrokenDefinition_IsReported_NotFatal()
        {
            var good = Definition("record.good");
            good.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 1f) };

            var keyless = Definition("record.keyless");
            typeof(RecordDefinitionSO).GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(keyless, string.Empty);

            var duplicate = Definition("record.good");
            duplicate.Conditions = good.Conditions;

            var conditionless = Definition("record.conditionless");

            IReadOnlyList<RecordStanding> standings = null;
            Assert.DoesNotThrow(() => standings = RecordEngine.Evaluate(
                IdentityFixtures.Canonical(_cfg, History()),
                new[] { good, null, keyless, duplicate, conditionless },
                _cfg.MasteryRoundThresholds, TimeZoneInfo.Utc));

            Assert.AreEqual(5, standings.Count, "One standing per definition, in order, whatever is wrong with them.");
            Assert.IsTrue(standings[0].Earned, "A good definition next to broken ones still evaluates.");
            foreach (int broken in new[] { 1, 2, 3, 4 })
            {
                Assert.IsFalse(standings[broken].IsValid, $"Definition {broken} must be reported as unusable.");
                Assert.IsFalse(standings[broken].Earned, "An unusable definition is never earned.");
                Assert.IsNotEmpty(standings[broken].Problem);
            }
        }

        [Test]
        public void Progress_IsOnlyShown_ForAReachNShape()
        {
            var reach = Definition("record.reach");
            reach.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 100f) };

            var backwards = Definition("record.backwards");
            backwards.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.LessThan, 5f) };

            var standings = Evaluate(IdentityFixtures.Canonical(_cfg, History()), reach, backwards);

            Assert.IsTrue(standings[0].HasProgress);
            Assert.AreEqual(44f, standings[0].Progress, 0.001f, "Progress is the best single round so far.");
            Assert.AreEqual(100f, standings[0].Target);
            Assert.IsFalse(standings[1].HasProgress, "'Best so far' reads backwards for a 'stay under' record.");
        }
    }

    // ==== Mastery =======================================================================

    public sealed class MasteryRulesTests
    {
        private static readonly int[] Thresholds = { 1, 3, 6, 10 };

        [Test]
        public void LevelIsHowManyThresholdsWereReached()
        {
            Assert.AreEqual(0, MasteryRules.LevelFor(0, Thresholds));
            Assert.AreEqual(1, MasteryRules.LevelFor(1, Thresholds));
            Assert.AreEqual(1, MasteryRules.LevelFor(2, Thresholds));
            Assert.AreEqual(2, MasteryRules.LevelFor(3, Thresholds));
            Assert.AreEqual(4, MasteryRules.LevelFor(99, Thresholds));
            Assert.AreEqual(4, MasteryRules.MaxLevel(Thresholds));
        }

        [Test]
        public void RoundsToNextLevel_IsNeverNegative_AndIsZeroAtTheTop()
        {
            Assert.AreEqual(1, MasteryRules.RoundsToNextLevel(0, Thresholds));
            Assert.AreEqual(1, MasteryRules.RoundsToNextLevel(2, Thresholds));
            Assert.AreEqual(0, MasteryRules.RoundsToNextLevel(10, Thresholds));
            Assert.AreEqual(0, MasteryRules.RoundsToNextLevel(500, Thresholds));
        }

        [Test]
        public void NoTable_IsNoLevels_NotAnException()
        {
            Assert.AreEqual(0, MasteryRules.LevelFor(50, null));
            Assert.AreEqual(0, MasteryRules.MaxLevel(null));
            Assert.AreEqual(0, MasteryRules.RoundsToNextLevel(50, null));
        }

        [Test]
        public void TheShippedThresholds_AreStrictlyIncreasing_AndPositive()
        {
            var thresholds = Config().MasteryRoundThresholds;
            Assert.IsNotNull(thresholds);
            Assert.AreEqual(10, thresholds.Length, "Mastery is authored as ten levels.");
            Assert.Greater(thresholds[0], 0, "Level 1 must take at least one round.");

            for (int i = 1; i < thresholds.Length; i++)
            {
                Assert.Greater(thresholds[i], thresholds[i - 1],
                    $"MasteryRoundThresholds[{i}] must be above [{i - 1}]: a flat or falling step would grant two " +
                    "levels for one round, or none ever.");
            }
        }
    }

    // ==== The name generator ============================================================

    public sealed class NameGeneratorTests
    {
        private static readonly Regex Shape = new Regex(@"^[A-Za-z]+#[0-9]{4}$");

        [Test]
        public void EveryDrawHasTheNameShape_AndIsDisplayable()
        {
            var random = new System.Random(4242);
            for (int i = 0; i < 400; i++)
            {
                string name = NameGenerator.Next(random);
                StringAssert.IsMatch(Shape.ToString(), name);
                Assert.IsTrue(ProfileEvent.IsDisplayableName(name), $"'{name}' would not be shown by its own build.");
            }
        }

        [Test]
        public void EveryWordPair_IsAsciiLetters_AndFitsTheNameLimit()
        {
            // The whole product, not a sample: there are only a few hundred of them, and one long pair
            // would produce a name the journal writes and the UI then refuses to draw.
            foreach (string word in NameGenerator.Adjectives.Concat(NameGenerator.Nouns))
            {
                Assert.IsTrue(word.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'),
                    $"'{word}' is not ASCII letters only.");
                Assert.IsTrue(char.IsUpper(word[0]), $"'{word}' must be capitalised.");
            }

            int longest = NameGenerator.Adjectives.Max(a => a.Length) + NameGenerator.Nouns.Max(n => n.Length) + 5;
            Assert.LessOrEqual(longest, ProfileEvent.MaxNameLength,
                $"The longest possible name is {longest} characters, over the {ProfileEvent.MaxNameLength} limit.");
        }

        [Test]
        public void ARerollNeverReturnsTheNameItReplaces()
        {
            var random = new System.Random(7);
            string current = NameGenerator.Next(random);
            for (int i = 0; i < 200; i++)
            {
                string next = NameGenerator.Next(random, current);
                Assert.AreNotEqual(current, next, "A re-roll that returns the same name reads as a broken button.");
                current = next;
            }
        }

        [Test]
        public void TheSameSeed_MakesTheSameNames()
        {
            var a = Enumerable.Range(0, 20).Select(_ => 0).ToList();
            var first = new System.Random(99);
            var second = new System.Random(99);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(NameGenerator.Next(first), NameGenerator.Next(second));
            }
        }

        [Test]
        public void ANameIsOnlyDisplayable_InTheShapeThisBuildMakes()
        {
            Assert.IsFalse(ProfileEvent.IsDisplayableName(null));
            Assert.IsFalse(ProfileEvent.IsDisplayableName(""));
            Assert.IsFalse(ProfileEvent.IsDisplayableName("NoHash"));
            Assert.IsFalse(ProfileEvent.IsDisplayableName("Two#Hash#1234"));
            Assert.IsFalse(ProfileEvent.IsDisplayableName("Short#12"));
            Assert.IsFalse(ProfileEvent.IsDisplayableName("Digits1#1234"));
            Assert.IsFalse(ProfileEvent.IsDisplayableName(new string('A', 40) + "#1234"));
            Assert.IsTrue(ProfileEvent.IsDisplayableName("SwiftBeak#2213"));
        }
    }

    // ==== Profile events and the nameplate ==============================================

    public sealed class NameplateTests
    {
        private ProgressionConfigSO _cfg;
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp() => _cfg = Config();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
            {
                if (made != null) Object.DestroyImmediate(made);
            }

            _made.Clear();
        }

        private ProgressionConfigSO Clone()
        {
            var copy = Object.Instantiate(_cfg);
            _made.Add(copy);
            return copy;
        }

        private ProgressionIdentity Build(IReadOnlyList<JournalRecord> rounds, IReadOnlyList<ProfileEvent> events,
            ProgressionConfigSO config = null, List<string> problems = null)
        {
            config = config != null ? config : _cfg;
            var ledger = ProgressionLedger.Fold(rounds ?? Array.Empty<JournalRecord>(), config, TimeZoneInfo.Utc);
            return IdentityFold.Build(ledger.EvaluableRounds, events, config, TimeZoneInfo.Utc, problems);
        }

        [Test]
        public void TheLastEventOfEachType_Wins_WhateverOrderTheyWereRead()
        {
            var events = new[]
            {
                IdentityFixtures.Event(ProfileEventTypes.Name, "FirstBeak#0001", "e1", Noon),
                IdentityFixtures.Event(ProfileEventTypes.Banner, "banner.barnwood", "e2", Noon.AddMinutes(1)),
                IdentityFixtures.Event(ProfileEventTypes.Name, "LastBeak#0002", "e3", Noon.AddMinutes(2)),
            };

            foreach (var order in new[] { events, events.Reverse().ToArray(), new[] { events[1], events[2], events[0] } })
            {
                var selections = IdentityFold.FoldSelections(order, null);
                Assert.AreEqual("LastBeak#0002", selections[ProfileEventTypes.Name]);
                Assert.AreEqual("banner.barnwood", selections[ProfileEventTypes.Banner]);
            }
        }

        [Test]
        public void ARepeatedEventId_IsFoldedOnce()
        {
            var once = IdentityFixtures.Event(ProfileEventTypes.Name, "OneBeak#0001", "same", Noon);
            var again = IdentityFixtures.Event(ProfileEventTypes.Name, "OneBeak#0001", "same", Noon);
            var later = IdentityFixtures.Event(ProfileEventTypes.Name, "TwoBeak#0002", "later", Noon.AddMinutes(1));

            var selections = IdentityFold.FoldSelections(new[] { once, again, later }, null);
            Assert.AreEqual("TwoBeak#0002", selections[ProfileEventTypes.Name]);
        }

        [Test]
        public void AnUnknownEventType_IsIgnoredAndReported_NeverFatal()
        {
            var problems = new List<string>();
            var selections = IdentityFold.FoldSelections(new[]
            {
                IdentityFixtures.Event("aura", "aura.newbuild", "e1", Noon),
                IdentityFixtures.Event(ProfileEventTypes.Name, "GoodBeak#0001", "e2", Noon.AddMinutes(1)),
            }, problems);

            Assert.AreEqual("GoodBeak#0001", selections[ProfileEventTypes.Name]);
            Assert.IsFalse(selections.ContainsKey("aura"), "A type this build does not know must not reach the plate.");
            Assert.IsTrue(problems.Any(p => p.Contains("aura")), "It must be reported, not swallowed.");
        }

        [Test]
        public void ANameThisBuildCannotDraw_IsIgnoredAndReported()
        {
            var problems = new List<string>();
            var identity = Build(null, new[] { IdentityFixtures.Event(ProfileEventTypes.Name, "<script>", "e1", Noon) }, problems: problems);

            Assert.IsFalse(identity.Plate.HasName, "A name outside the shape this build makes is not shown.");
            Assert.IsTrue(problems.Any(p => p.Contains("<script>")));
        }

        [Test]
        public void ATitleIsWornOnlyWhileItsRecordIsEarned()
        {
            var config = Clone();
            var record = IdentityFixtures.Definition("record.rich", title: "Rich Bird");
            _made.Add(record);
            record.Conditions = new[] { IdentityFixtures.Condition(RecordMetric.BankedTotal, RecordComparison.AtLeast, 40f) };
            config.Records = new[] { record };

            var events = new[] { IdentityFixtures.Event(ProfileEventTypes.Title, "record.rich", "e1", Noon) };

            var unearned = Build(new[] { Rec("r1", Noon, banked: 5f) }, events, config);
            Assert.IsNull(unearned.Plate.TitleText, "A title whose record is not earned must not be drawn.");

            var earned = Build(new[] { Rec("r1", Noon, banked: 41f) }, events, config);
            Assert.AreEqual("Rich Bird", earned.Plate.TitleText);
            Assert.AreEqual("record.rich", earned.Plate.TitleKey);
        }

        [Test]
        public void TheEmblemIsAlwaysPresent_AndFallsBackToTheMostPlayedRole()
        {
            var fresh = Build(null, Array.Empty<ProfileEvent>());
            Assert.AreEqual(UnlockKeyTable.RoleKeys[0], fresh.Plate.EmblemRoleKey,
                "With nothing played, the emblem is the first roster role, never absent.");

            var played = Build(new[]
            {
                Rec("r1", Noon, roleKey: IdentityFixtures.Speedy),
                Rec("r2", Noon.AddHours(1), roleKey: IdentityFixtures.Speedy),
                Rec("r3", Noon.AddHours(2), roleKey: IdentityFixtures.Warrior),
            }, Array.Empty<ProfileEvent>());
            Assert.AreEqual(IdentityFixtures.Speedy, played.Plate.EmblemRoleKey);

            var chosen = Build(new[] { Rec("r1", Noon, roleKey: IdentityFixtures.Warrior) },
                new[] { IdentityFixtures.Event(ProfileEventTypes.Emblem, IdentityFixtures.Fatty, "e1", Noon) });
            Assert.AreEqual(IdentityFixtures.Warrior, chosen.Plate.EmblemRoleKey,
                "An emblem for a role that has never been played is not worn.");
        }

        [Test]
        public void EveryPlate_HoldsAPartThatCouldNotHaveBeenBought()
        {
            // The catalogue is replaced with one where every banner is for sale, which is the only way
            // the invariant could ever be at risk. The emblem is what keeps it true.
            var forSale = Clone();
            forSale.Banners = forSale.Banners
                .Select(b => new BannerDefinition
                {
                    Key = b.Key, DisplayName = b.DisplayName, UssClass = b.UssClass,
                    Source = BannerSource.Purchasable, UnlockRecordKey = b.UnlockRecordKey,
                })
                .ToArray();

            var histories = new[]
            {
                Array.Empty<JournalRecord>(),
                new[] { Rec("r1", Noon, banked: 41f, roleKey: IdentityFixtures.Warrior) },
                new[] { Rec("r1", Noon, stolen: 25f, roleKey: IdentityFixtures.Speedy, rivalsRobbed: 3) },
            };

            var eventSets = new[]
            {
                Array.Empty<ProfileEvent>(),
                new[] { IdentityFixtures.Event(ProfileEventTypes.Banner, "banner.midnight", "e1", Noon) },
                new[] { IdentityFixtures.Event(ProfileEventTypes.Title, "record.full_coop", "e1", Noon) },
                new[] { IdentityFixtures.Event(ProfileEventTypes.Emblem, IdentityFixtures.Assassin, "e1", Noon) },
            };

            foreach (var config in new[] { _cfg, forSale })
            {
                foreach (var history in histories)
                {
                    foreach (var events in eventSets)
                    {
                        var plate = Build(history, events, config).Plate;
                        Assert.IsNotEmpty(plate.EmblemRoleKey ?? string.Empty,
                            "The emblem is the part no store could ever sell, so it must always be there.");

                        var identity = Build(history, events, config);
                        var banner = identity.Banners.FirstOrDefault(b =>
                            string.Equals(b.Key, plate.BannerKey, StringComparison.Ordinal));
                        Assert.IsFalse(banner != null && banner.Purchasable,
                            "A banner nobody in this build can own must never end up on the plate.");
                    }
                }
            }
        }

        [Test]
        public void NothingButABanner_EvenHasAPriceToAskAbout()
        {
            // Titles come from records and emblems from roles: neither view carries any notion of being
            // bought. If one ever grows one, this is where that decision gets noticed.
            foreach (var type in new[] { typeof(ProgressionIdentity.RecordView), typeof(ProgressionIdentity.RoleMastery) })
            {
                var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => Regex.IsMatch(m.Name, "purchas|price|cost|buy|store", RegexOptions.IgnoreCase))
                    .Select(m => m.Name)
                    .ToList();
                Assert.IsEmpty(members, $"{type.Name} must stay earn-only: {string.Join(", ", members)}");
            }

            Assert.IsNotNull(typeof(ProgressionIdentity.BannerView).GetProperty("Purchasable"),
                "The banner view is the one place the idea exists; if it went away this test is checking nothing.");

            foreach (var banner in Config().Banners)
            {
                Assert.AreNotEqual(BannerSource.Purchasable, banner.Source,
                    $"Banner '{banner.Key}' ships as purchasable, but this build has no store to buy it in.");
            }
        }

        [Test]
        public void AnEarnedBannerIsOwnedOnlyOnceItsRecordIs()
        {
            var locked = Build(null, Array.Empty<ProfileEvent>());
            var midnight = locked.Banners.Single(b => b.Key == "banner.midnight");
            Assert.IsFalse(midnight.Owned);
            Assert.IsTrue(locked.Banners.Single(b => b.Key == "banner.barnwood").Owned, "The starter banner is always owned.");

            var earned = Build(new[] { Rec("r1", Noon, stolen: 25f) }, Array.Empty<ProfileEvent>());
            Assert.IsTrue(earned.Banners.Single(b => b.Key == "banner.midnight").Owned,
                "Stealing 20 earns Highway Hen, which unlocks Midnight.");
        }
    }

    // ==== Career and mastery on the identity ============================================

    public sealed class CareerSummaryTests
    {
        private ProgressionConfigSO _cfg;

        [SetUp]
        public void SetUp() => _cfg = Config();

        private static JournalRecord[] TwentyFiveRounds()
        {
            var rounds = new List<JournalRecord>();
            for (int i = 0; i < 25; i++)
            {
                rounds.Add(Rec("r" + i.ToString("D2", CultureInfo.InvariantCulture), Noon.AddHours(i),
                    placement: i % 4 + 1, banked: i, stolen: i * 0.5f,
                    roleKey: UnlockKeyTable.RoleKeys[i % UnlockKeyTable.RoleKeys.Count], rivalsRobbed: i % 3));
            }

            return rounds.ToArray();
        }

        [Test]
        public void CareerTotals_AreTheSameNumbersAsTheProfileTotals()
        {
            var ledger = ProgressionLedger.Fold(TwentyFiveRounds(), _cfg, TimeZoneInfo.Utc);
            var identity = IdentityFold.Build(ledger.EvaluableRounds, Array.Empty<ProfileEvent>(), _cfg, TimeZoneInfo.Utc);

            // Two sums over one list, not two walks of the journal — so this cannot drift.
            Assert.AreEqual(ledger.RoundsPlayed, identity.Summary.Rounds);
            Assert.AreEqual(ledger.Wins, identity.Summary.Wins);
            Assert.AreEqual(ledger.TotalBanked, identity.Summary.Banked, 0.0001);
            Assert.AreEqual(ledger.TotalStolen, identity.Summary.Stolen, 0.0001);
        }

        [Test]
        public void RecentPlacements_AreTheLastTwenty_MostRecentLast()
        {
            var ledger = ProgressionLedger.Fold(TwentyFiveRounds(), _cfg, TimeZoneInfo.Utc);
            var identity = IdentityFold.Build(ledger.EvaluableRounds, Array.Empty<ProfileEvent>(), _cfg, TimeZoneInfo.Utc);

            Assert.AreEqual(20, identity.Summary.RecentPlacements.Count);
            Assert.AreEqual(ledger.EvaluableRounds[24].Placement, identity.Summary.RecentPlacements.Last(),
                "The newest round is the last pip.");
            Assert.AreEqual(ledger.EvaluableRounds[5].Placement, identity.Summary.RecentPlacements.First());
        }

        [Test]
        public void PersonalBests_AreTheBestSingleRound()
        {
            var ledger = ProgressionLedger.Fold(TwentyFiveRounds(), _cfg, TimeZoneInfo.Utc);
            var identity = IdentityFold.Build(ledger.EvaluableRounds, Array.Empty<ProfileEvent>(), _cfg, TimeZoneInfo.Utc);

            Assert.AreEqual(ledger.EvaluableRounds.Max(r => r.BankedTotal), identity.Summary.BestBankedInARound, 0.0001f);
            Assert.AreEqual(ledger.EvaluableRounds.Max(r => r.StolenTotal), identity.Summary.BestStolenInARound, 0.0001f);
            Assert.AreEqual(ledger.EvaluableRounds.Max(r => r.RivalsRobbed), identity.Summary.BestRivalsRobbedInARound);
        }

        [Test]
        public void EveryRosterRole_IsListed_EvenUnplayed()
        {
            var identity = IdentityFold.Build(Array.Empty<RoundOutcome>(), Array.Empty<ProfileEvent>(), _cfg, TimeZoneInfo.Utc);

            CollectionAssert.AreEqual(UnlockKeyTable.RoleKeys, identity.Roles.Select(r => r.RoleKey).ToList(),
                "The unplayed roles are the reason to play them; they must be listed, in roster order.");
            Assert.IsTrue(identity.Roles.All(r => !r.Playable && r.Level == 0));
        }

        [Test]
        public void ARoleKeyFromAnOlderRoster_IsStillCounted()
        {
            var ledger = ProgressionLedger.Fold(new[] { Rec("r1", Noon, roleKey: "class.retired") }, _cfg, TimeZoneInfo.Utc);
            var identity = IdentityFold.Build(ledger.EvaluableRounds, Array.Empty<ProfileEvent>(), _cfg, TimeZoneInfo.Utc);

            var retired = identity.Roles.SingleOrDefault(r => r.RoleKey == "class.retired");
            Assert.IsNotNull(retired, "Play the player actually did must not vanish because the roster changed.");
            Assert.AreEqual(1, retired.Rounds);
            Assert.AreEqual(UnlockKeyTable.RoleKeys.Count, identity.Roles.ToList().IndexOf(retired),
                "Roster roles come first; keys from an older build follow.");
        }
    }

    // ==== The shipped record assets =====================================================

    public sealed class RecordAssetTests
    {
        private const string RecordsDir = TestAssets.DataRoot + "/Progression/Records";

        private static readonly string[] ShippedKeys =
        {
            "record.full_coop",
            "record.featherweight_hauler",
            "record.three_time_thief",
            "record.highway_hen",
            "record.empty_beak",
            "record.the_unbanked",
            "record.whole_flock",
            "record.devoted",
        };

        private static RecordDefinitionSO[] Shipped() => Config().Records;

        [Test]
        public void TheConfigSlot_ResolvesToTheEightRealAssets()
        {
            var records = Shipped();
            Assert.IsNotNull(records, "ProgressionConfig.asset has no Records array at all.");
            CollectionAssert.AreEqual(ShippedKeys, records.Select(r => r == null ? "<missing>" : r.Key).ToList(),
                "The shipped records, in display order. Adding one is deliberate: update this list with it.");

            foreach (var record in records)
            {
                string path = AssetDatabase.GetAssetPath(record);
                StringAssert.StartsWith(RecordsDir + "/", path,
                    $"{record.name} must live under {RecordsDir}, where CONVENTIONS.md pins SO assets.");
            }
        }

        [Test]
        public void EveryKey_MatchesThePattern_AndIsUnique()
        {
            var records = Shipped();
            foreach (var record in records)
            {
                StringAssert.IsMatch(RecordDefinitionSO.KeyShape.ToString(), record.Key,
                    $"{record.name}'s key is not a stable record key.");
            }

            CollectionAssert.AllItemsAreUnique(records.Select(r => r.Key).ToList(),
                "Two records sharing a key means one of them can never be earned.");
        }

        [Test]
        public void EveryDefinition_IsEvaluable_AndSaysWhatItIs()
        {
            foreach (var record in Shipped())
            {
                Assert.IsNull(record.Problem(), $"{record.name}: {record.Problem()}");
                Assert.IsNotEmpty(record.DisplayName, $"{record.name} has no display name.");
                Assert.IsNotEmpty(record.Description, $"{record.name} has no description — the unearned list is the content.");
                Assert.IsNotEmpty(record.Title, $"{record.name} grants no title; every shipped record does.");
            }
        }

        [Test]
        public void ARoleFilteredRecord_NamesARealRosterRole()
        {
            foreach (var record in Shipped())
            {
                if (string.IsNullOrEmpty(record.RoleKey)) continue;
                CollectionAssert.Contains(UnlockKeyTable.RoleKeys, record.RoleKey,
                    $"{record.name} filters on '{record.RoleKey}', which no roster role has — it could never be earned.");
            }
        }

        [Test]
        public void EveryEarnedBanner_NamesARecordThatShips()
        {
            foreach (var banner in Config().Banners)
            {
                if (banner.Source != BannerSource.Earned) continue;
                CollectionAssert.Contains(ShippedKeys, banner.UnlockRecordKey,
                    $"Banner '{banner.Key}' unlocks from '{banner.UnlockRecordKey}', which no shipped record has.");
            }
        }
    }
}
