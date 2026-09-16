using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.Progression;
using NUnit.Framework;
using UnityEngine;
using static CluckWars.Tests.ProgressionFixtures;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>Shared builders for the slice 4 ramp/goal/daily-task tests.</summary>
    internal static class RampGoalFixtures
    {
        private static int _counter;

        /// <summary>A round for <paramref name="role"/>, defaulting to LAST place — every fixture here defaults to losing, so a test that forgets to override it still proves "completable while losing".</summary>
        public static RoundOutcome RoundFor(ChickenClass role, DateTime? at = null, int placement = 4, float banked = 0f,
            float stolen = 0f, int rivalsRobbed = 0, int opponentsDisabled = 0, AbilityTally[] abilities = null)
        {
            var outcome = Outcome("rg-" + (++_counter).ToString(), at ?? Noon, placement, banked, stolen,
                UnlockKeyTable.RoleKey(role), rivalsRobbed, opponentsDisabled);
            if (abilities != null) outcome.Abilities = abilities;
            return outcome;
        }

        public static AbilityTally Tally(string key, int casts, int connected) =>
            new AbilityTally { Key = key, Casts = casts, Connected = connected };

        public static RampStepSO Step(string name, string objective, string[] abilityKeys = null,
            string[] roleKeys = null, bool sequential = false)
        {
            var step = ScriptableObject.CreateInstance<RampStepSO>();
            step.DisplayName = name;
            step.ObjectiveDescription = objective;
            step.AbilityKeysGranted = abilityKeys ?? Array.Empty<string>();
            step.RoleKeysGranted = roleKeys ?? Array.Empty<string>();
            step.GrantRolesSequentially = sequential;
            return step;
        }

        /// <summary>
        /// The canonical 4-step shape <see cref="RampController"/>'s hard-coded thresholds (bank 10,
        /// bank 20 or headbutt x2, steal 5, sequential Fatty→Speedy→Assassin) actually advance through —
        /// RampController is not generic over an arbitrary step count, so every fold test needs this
        /// exact shape, not an arbitrary one.
        /// </summary>
        public static RampStepSO[] CanonicalSteps() => new[]
        {
            Step("Step1", "Bank 10", abilityKeys: new[] { "ability.a" }),
            Step("Step2", "Bank 20 or Headbutt x2", abilityKeys: new[] { "ability.b" }),
            Step("Step3", "Steal 5", abilityKeys: new[] { "ability.c" }),
            Step("Step4", "One round as each", roleKeys: new[]
            {
                UnlockKeyTable.RoleKey(ChickenClass.Fatty),
                UnlockKeyTable.RoleKey(ChickenClass.Speedy),
                UnlockKeyTable.RoleKey(ChickenClass.Assassin),
            }, sequential: true),
        };

        /// <summary>
        /// The minimal round history that clears <see cref="CanonicalSteps"/> end to end: one round
        /// that satisfies steps 1–3 at once (their thresholds are cumulative sums, not per-step
        /// windows), then one round as each of Fatty, Speedy and Assassin.
        /// </summary>
        public static List<RoundOutcome> RoundsThatClearTheCanonicalRamp(DateTime start) => new List<RoundOutcome>
        {
            RoundFor(ChickenClass.Warrior, at: start, placement: 4, banked: 20f, stolen: 5f),
            RoundFor(ChickenClass.Fatty, at: start.AddMinutes(1), placement: 4),
            RoundFor(ChickenClass.Speedy, at: start.AddMinutes(2), placement: 4),
            RoundFor(ChickenClass.Assassin, at: start.AddMinutes(3), placement: 4),
        };

        public static GoalTemplateSO Goal(string key, GoalMetric metric, params (float Threshold, int Grain)[] tiers)
        {
            var template = ScriptableObject.CreateInstance<GoalTemplateSO>();
            SetKey(template, key);
            template.DisplayNameFormat = key + " {0}";
            template.DescriptionFormat = "Reach {0}.";
            template.Metric = metric;
            template.Tiers = tiers.Select(t => new GoalTier { Threshold = t.Threshold, GrainReward = t.Grain }).ToArray();
            return template;
        }

        private static void SetKey(GoalTemplateSO template, string key)
        {
            var field = typeof(GoalTemplateSO).GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(template, key);
        }

        public static void Destroy(params Object[] objects)
        {
            foreach (var o in objects) if (o != null) Object.DestroyImmediate(o);
        }
    }

    // ==== RampStepSO — no scene-baked ability/class references =========================

    public sealed class RampStepStructuralTests
    {
        [Test]
        public void RampStepSO_HasNoDirectUnityObjectReference_OnlyStableStringKeys()
        {
            // The exact bug class from memory/bot-loadout-staleness-bug.md: a scene- or asset-baked
            // ability/class reference goes stale the moment the roster changes under it. RampStepSO
            // may only ever carry stable string keys, resolved against the live registry by whoever
            // reads them — never a direct AbilityBaseSO/ChickenClass object reference.
            var fields = typeof(RampStepSO).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotEmpty(fields);

            foreach (var field in fields)
            {
                var elementType = field.FieldType.IsArray ? field.FieldType.GetElementType() : field.FieldType;
                Assert.IsFalse(typeof(Object).IsAssignableFrom(elementType),
                    $"RampStepSO.{field.Name} is a direct Unity object reference ({field.FieldType}). Ramp grants " +
                    "must be stable string keys, never a baked asset reference.");
            }
        }

        [Test]
        public void ShippedRampSteps_AbilityKeys_ResolveAgainstTheLiveAbilityRegistry()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var liveKeys = new HashSet<string>(registry.All.Where(a => a != null).Select(a => a.UnlockKey));

            foreach (var step in Config().RampSteps)
            {
                Assert.IsNotNull(step, "ProgressionConfig.RampSteps has a missing slot.");
                foreach (string key in step.AbilityKeysGranted)
                {
                    CollectionAssert.Contains(liveKeys, key,
                        $"{step.name} grants '{key}', which no live ability's UnlockKey matches. Ramp grants must " +
                        "be resolved against the live registry, never against stale prose.");
                }
            }
        }

        [Test]
        public void ShippedRampSteps_RoleKeys_AreRealRosterRoles()
        {
            foreach (var step in Config().RampSteps)
            {
                foreach (string key in step.RoleKeysGranted)
                {
                    CollectionAssert.Contains(UnlockKeyTable.RoleKeys, key,
                        $"{step.name} grants role '{key}', which no roster role has.");
                }
            }
        }

        [Test]
        public void AbilityCategoryIndex_ResolvesByUnlockKey_RegardlessOfRegistryOrder()
        {
            // Simulates a roster/registry reshuffle (the bot-loadout-staleness bug's actual trigger):
            // resolution must depend only on the ability's own UnlockKey, never its position in the
            // registry array.
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var forward = new AbilityCategoryIndex(registry);

            var reordered = ScriptableObject.CreateInstance<AbilityRegistrySO>();
            try
            {
                reordered.All = registry.All.Where(a => a != null).Reverse().ToArray();
                var reversed = new AbilityCategoryIndex(reordered);

                foreach (var ability in registry.All.Where(a => a != null))
                {
                    forward.TryGetCategory(ability.UnlockKey, out string a);
                    reversed.TryGetCategory(ability.UnlockKey, out string b);
                    Assert.AreEqual(a, b, $"'{ability.UnlockKey}' resolved to a different category after the " +
                        "registry was reordered — resolution must be by key, never by array position.");
                }
            }
            finally
            {
                Object.DestroyImmediate(reordered);
            }
        }
    }

    // ==== RampController.Fold ===========================================================

    public sealed class RampControllerTests
    {
        private static RampStepSO[] FourSteps() => RampGoalFixtures.CanonicalSteps();

        [Test]
        public void Fold_NoRounds_IsStep1_NotComplete_StillGrantsStep1sOwnUnlocks()
        {
            // Step 1's grants are active from the moment the ramp starts — that is what lets the
            // player play their very first round at all ("Grants: Warrior + Peck" happens on entry,
            // not on completion).
            var state = RampController.Fold(Array.Empty<RoundOutcome>(), FourSteps());
            Assert.AreEqual(1, state.StepNumber);
            Assert.IsFalse(state.IsComplete);
            CollectionAssert.AreEqual(new[] { "ability.a" }, state.UnlockedAbilityKeys);
            CollectionAssert.IsEmpty(state.UnlockedRoleKeys);
        }

        [Test]
        public void Fold_NeverThrows_ForNullRoundsOrNullOrEmptySteps()
        {
            Assert.DoesNotThrow(() => RampController.Fold(null, FourSteps()));
            Assert.DoesNotThrow(() => RampController.Fold(new List<RoundOutcome> { RampGoalFixtures.RoundFor(ChickenClass.Warrior) }, null));
            Assert.DoesNotThrow(() => RampController.Fold(null, null));
            Assert.DoesNotThrow(() => RampController.Fold(Array.Empty<RoundOutcome>(), Array.Empty<RampStepSO>()));

            var withNulls = new RampStepSO[] { null, RampGoalFixtures.Step("X", "Y"), null };
            Assert.DoesNotThrow(() => RampController.Fold(Array.Empty<RoundOutcome>(), withNulls));
        }

        [Test]
        public void Fold_Bank10_AdvancesToStep2_AndGrantsBothStep1AndStep2Abilities()
        {
            // Grants are "the moment this step becomes active" (RampStepSO's own contract): clearing
            // step 1's objective makes step 2 ACTIVE immediately, so step 2's grants (ability.b) are
            // already unlocked here — they are not gated behind step 2's OWN objective too.
            var rounds = new[] { RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f) };
            var state = RampController.Fold(rounds, FourSteps());

            Assert.AreEqual(2, state.StepNumber);
            Assert.IsFalse(state.IsComplete);
            CollectionAssert.Contains(state.UnlockedAbilityKeys, "ability.a");
            CollectionAssert.Contains(state.UnlockedAbilityKeys, "ability.b");
            CollectionAssert.DoesNotContain(state.UnlockedAbilityKeys, "ability.c");
        }

        [Test]
        public void Fold_HeadbuttTwiceInARound_ClearsStep2_WithoutNeedingBank20()
        {
            var rounds = new[]
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior,
                    abilities: new[] { RampGoalFixtures.Tally("ability.headbutt", 3, 2) }),
            };
            var state = RampController.Fold(rounds, FourSteps());
            Assert.AreEqual(3, state.StepNumber);
            CollectionAssert.Contains(state.UnlockedAbilityKeys, "ability.b");
        }

        [Test]
        public void Fold_Bank20Cumulative_AlsoClearsStep2_WithoutHeadbutt()
        {
            var rounds = new[]
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),
            };
            var state = RampController.Fold(rounds, FourSteps());
            Assert.AreEqual(3, state.StepNumber);
        }

        [Test]
        public void Fold_OneConnectIsNotEnough_ForHeadbuttStep2Objective()
        {
            var rounds = new[]
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior,
                    abilities: new[] { RampGoalFixtures.Tally("ability.headbutt", 3, 1) }),
            };
            var state = RampController.Fold(rounds, FourSteps());
            Assert.AreEqual(2, state.StepNumber, "One connect must not satisfy 'connects twice'.");
        }

        [Test]
        public void Fold_Steal5Cumulative_AdvancesToStep4_AndGrantsFattyOnly()
        {
            var rounds = new[]
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 20f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, stolen: 5f),
            };
            var state = RampController.Fold(rounds, FourSteps());
            Assert.AreEqual(4, state.StepNumber);
            Assert.IsFalse(state.IsComplete);
            CollectionAssert.Contains(state.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Fatty));
            CollectionAssert.DoesNotContain(state.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Speedy));
            CollectionAssert.DoesNotContain(state.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Assassin));
        }

        [Test]
        public void Fold_SequentialStep4_GrantsSpeedyOnlyAfterAFattyRound_AndAssassinOnlyAfterASpeedyRound()
        {
            var toStep4 = new List<RoundOutcome>
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 20f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, stolen: 5f),
            };

            var stateAtStep4 = RampController.Fold(toStep4, FourSteps());
            CollectionAssert.Contains(stateAtStep4.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Fatty));
            CollectionAssert.DoesNotContain(stateAtStep4.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Speedy));

            var withFattyRound = new List<RoundOutcome>(toStep4) { RampGoalFixtures.RoundFor(ChickenClass.Fatty) };
            var stateAfterFatty = RampController.Fold(withFattyRound, FourSteps());
            CollectionAssert.Contains(stateAfterFatty.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Speedy));
            CollectionAssert.DoesNotContain(stateAfterFatty.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Assassin));
            Assert.IsFalse(stateAfterFatty.IsComplete);

            var withSpeedyRound = new List<RoundOutcome>(withFattyRound) { RampGoalFixtures.RoundFor(ChickenClass.Speedy) };
            var stateAfterSpeedy = RampController.Fold(withSpeedyRound, FourSteps());
            CollectionAssert.Contains(stateAfterSpeedy.UnlockedRoleKeys, UnlockKeyTable.RoleKey(ChickenClass.Assassin));
            Assert.IsFalse(stateAfterSpeedy.IsComplete, "Unlocking Assassin is not the same as having played as Assassin.");

            var withAssassinRound = new List<RoundOutcome>(withSpeedyRound) { RampGoalFixtures.RoundFor(ChickenClass.Assassin) };
            var stateComplete = RampController.Fold(withAssassinRound, FourSteps());
            Assert.IsTrue(stateComplete.IsComplete);
            Assert.AreEqual(RampController.StepCount, stateComplete.StepNumber);
        }

        [Test]
        public void Fold_CannotAdvancePastTheLastStep_AndCompletionNeverGoesOutOfRange()
        {
            // Overwhelming stats far beyond every threshold, funnelled through the full step-4
            // sequence — the ramp must still cap at exactly StepCount and never throw.
            var rounds = new List<RoundOutcome>
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 1000f, stolen: 1000f,
                    abilities: new[] { RampGoalFixtures.Tally("ability.headbutt", 10, 10) }),
                RampGoalFixtures.RoundFor(ChickenClass.Fatty, banked: 1000f),
                RampGoalFixtures.RoundFor(ChickenClass.Speedy, banked: 1000f),
                RampGoalFixtures.RoundFor(ChickenClass.Assassin, banked: 1000f),
            };

            RampState state = null;
            Assert.DoesNotThrow(() => state = RampController.Fold(rounds, FourSteps()));
            Assert.IsTrue(state.IsComplete);
            Assert.AreEqual(RampController.StepCount, state.StepNumber);
            Assert.LessOrEqual(state.StepNumber, RampController.StepCount, "StepNumber must never exceed StepCount.");
        }

        [Test]
        public void Fold_FewerThanFourUsableSteps_CapsGracefully_NeverClaimsCompletion()
        {
            var threeSteps = FourSteps().Take(3).ToArray();
            var rounds = new List<RoundOutcome>
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 20f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, stolen: 5f),
            };

            RampState state = null;
            Assert.DoesNotThrow(() => state = RampController.Fold(rounds, threeSteps));
            Assert.AreEqual(3, state.StepNumber);
            Assert.IsFalse(state.IsComplete, "A ramp with only 3 usable steps configured must never claim completion.");
        }

        [Test]
        public void ShippedRampSteps_HasExactlyFourSteps()
        {
            Assert.AreEqual(RampController.StepCount, RampController.UsableSteps(Config().RampSteps).Count);
        }

        [Test]
        public void EveryAbilityCategory_IsHeldAfterRampStep3_OnRealData()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var config = Config();

            var rounds = new List<RoundOutcome>
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior,
                    abilities: new[] { RampGoalFixtures.Tally("ability.headbutt", 2, 2) }),
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, stolen: 5f),
            };

            var state = RampController.Fold(rounds, config.RampSteps);
            Assert.AreEqual(4, state.StepNumber, "3 clean rounds should reach ramp step 4 on the shipped config.");

            var heldCategories = new HashSet<string>();
            foreach (string abilityKey in state.UnlockedAbilityKeys)
            {
                var ability = registry.All.FirstOrDefault(a => a != null && a.UnlockKey == abilityKey);
                Assert.IsNotNull(ability, $"Ramp grants '{abilityKey}', which no live ability has.");
                heldCategories.Add(UnlockKeyTable.AbilityCategoryKey(ability.Category));
            }

            CollectionAssert.AreEquivalent(UnlockKeyTable.CategoryKeys, heldCategories,
                "Every AbilityCategory should be represented in the unlocked pool once ramp step 3 is cleared.");
        }

        [Test]
        public void FreshProfile_WalksTheShippedRamp_InAtMostTwelveRounds()
        {
            var config = Config();
            var rounds = new List<RoundOutcome>
            {
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, banked: 10f),                                        // -> step2
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, abilities: new[] { RampGoalFixtures.Tally("ability.headbutt", 2, 2) }), // -> step3
                RampGoalFixtures.RoundFor(ChickenClass.Warrior, stolen: 5f),                                          // -> step4 (Fatty)
                RampGoalFixtures.RoundFor(ChickenClass.Fatty),                                                        // -> Speedy
                RampGoalFixtures.RoundFor(ChickenClass.Speedy),                                                       // -> Assassin
                RampGoalFixtures.RoundFor(ChickenClass.Assassin),                                                     // -> complete
            };

            Assert.LessOrEqual(rounds.Count, 12);
            var state = RampController.Fold(rounds, config.RampSteps);
            Assert.IsTrue(state.IsComplete, "A fresh profile playing this exact walk must finish the shipped ramp.");
        }
    }

    // ==== GoalRotation ===================================================================

    public sealed class GoalRotationTests
    {
        [Test]
        public void ForWeek_IsDeterministic_ForTheSameWeekKeyAndCatalogue()
        {
            var templates = new[]
            {
                RampGoalFixtures.Goal("goal.a", GoalMetric.BankedTotal, (10f, 1)),
                RampGoalFixtures.Goal("goal.b", GoalMetric.RivalsRobbed, (2f, 1)),
                RampGoalFixtures.Goal("goal.c", GoalMetric.RoundsPlayed, (3f, 1)),
                RampGoalFixtures.Goal("goal.d", GoalMetric.RoundsStolenExceededBanked, (1f, 1)),
            };
            try
            {
                var first = GoalRotation.ForWeek("2026-W38", templates);
                var second = GoalRotation.ForWeek("2026-W38", templates);
                CollectionAssert.AreEqual(first.Select(s => s.Template.Key).ToList(), second.Select(s => s.Template.Key).ToList());
                CollectionAssert.AreEqual(first.Select(s => s.TierIndex).ToList(), second.Select(s => s.TierIndex).ToList());
            }
            finally { RampGoalFixtures.Destroy(templates); }
        }

        [Test]
        public void ForWeek_PicksThreeDistinctTemplates_WhenAtLeastThreeAreUsable()
        {
            var templates = new[]
            {
                RampGoalFixtures.Goal("goal.a", GoalMetric.BankedTotal, (10f, 1)),
                RampGoalFixtures.Goal("goal.b", GoalMetric.RivalsRobbed, (2f, 1)),
                RampGoalFixtures.Goal("goal.c", GoalMetric.RoundsPlayed, (3f, 1)),
                RampGoalFixtures.Goal("goal.d", GoalMetric.RoundsStolenExceededBanked, (1f, 1)),
            };
            try
            {
                var selection = GoalRotation.ForWeek("2026-W12", templates);
                Assert.AreEqual(GoalRotation.GoalsPerWeek, selection.Count);
                CollectionAssert.AllItemsAreUnique(selection.Select(s => s.Template.Key).ToList());
            }
            finally { RampGoalFixtures.Destroy(templates); }
        }

        [Test]
        public void ForWeek_NeverThrows_ForNullOrEmptyOrAllBrokenCatalogue()
        {
            Assert.DoesNotThrow(() => GoalRotation.ForWeek("2026-W01", null));
            Assert.DoesNotThrow(() => GoalRotation.ForWeek(null, Array.Empty<GoalTemplateSO>()));

            var broken = ScriptableObject.CreateInstance<GoalTemplateSO>(); // no key assigned -> Problem() != null
            try
            {
                var result = GoalRotation.ForWeek("2026-W01", new[] { broken, null });
                CollectionAssert.IsEmpty(result);
            }
            finally { Object.DestroyImmediate(broken); }
        }

        [Test]
        public void ForWeek_FewerThanThreeUsableTemplates_ReturnsWhatIsAvailable()
        {
            var templates = new[] { RampGoalFixtures.Goal("goal.only", GoalMetric.BankedTotal, (10f, 1)) };
            try
            {
                var selection = GoalRotation.ForWeek("2026-W20", templates);
                Assert.AreEqual(1, selection.Count);
            }
            finally { RampGoalFixtures.Destroy(templates); }
        }

        [Test]
        public void ShippedGoalTemplates_HasAtLeastThreeUsableTemplates()
        {
            var usable = Config().GoalTemplates?.Where(t => t != null && t.Problem() == null).ToList();
            Assert.IsNotNull(usable);
            Assert.GreaterOrEqual(usable.Count, GoalRotation.GoalsPerWeek);
        }
    }

    // ==== DailyTask =======================================================================

    public sealed class DailyTaskTests
    {
        [Test]
        public void ForDay_IsDeterministic()
        {
            Assert.AreEqual(DailyTask.ForDay("2026-09-16").Key, DailyTask.ForDay("2026-09-16").Key);
        }

        [Test]
        public void ForDay_NeverThrows_ForNullOrEmptyKey()
        {
            Assert.DoesNotThrow(() => DailyTask.ForDay(null));
            Assert.DoesNotThrow(() => DailyTask.ForDay(string.Empty));
        }

        [Test]
        public void IsSatisfiedBy_IsFalse_ForNullTemplateOrRound()
        {
            var template = DailyTask.Templates[0];
            Assert.IsFalse(DailyTask.IsSatisfiedBy(null, RampGoalFixtures.RoundFor(ChickenClass.Warrior)));
            Assert.IsFalse(DailyTask.IsSatisfiedBy(template, null));
        }

        [Test]
        public void EveryDailyTaskTemplate_IsCompletableAtLastPlace()
        {
            // Every fixture in RampGoalFixtures.RoundFor defaults to placement 4 (last) unless
            // overridden — this test deliberately never overrides it.
            foreach (var template in DailyTask.Templates)
            {
                var round = RampGoalFixtures.RoundFor(ChickenClass.Warrior,
                    banked: 5f, stolen: 3f, rivalsRobbed: 2, opponentsDisabled: 1);
                Assert.IsTrue(DailyTask.IsSatisfiedBy(template, round),
                    $"'{template.Key}' must be completable without placing first.");
            }
        }

        [Test]
        public void PlayARoundTemplate_IsSatisfiedByAnyRound_EvenAnEmptyOne()
        {
            var template = DailyTask.Templates.Single(t => t.Key == "daily.play_a_round");
            var round = RampGoalFixtures.RoundFor(ChickenClass.Warrior);
            Assert.IsTrue(DailyTask.IsSatisfiedBy(template, round));
        }
    }

    // ==== UnlocksFold =====================================================================

    public sealed class UnlocksFoldTests
    {
        private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

        private static ProgressionConfigSO SyntheticConfig(RampStepSO[] rampSteps, GoalTemplateSO[] goals, int dailyBonus = 15)
        {
            var cfg = ScriptableObject.CreateInstance<ProgressionConfigSO>();
            cfg.RampSteps = rampSteps;
            cfg.GoalTemplates = goals;
            cfg.DailyTaskBonus = dailyBonus;
            return cfg;
        }

        [Test]
        public void Build_WhileRampActive_HidesGoalsAndDailyTask()
        {
            var steps = new[] { RampGoalFixtures.Step("S1", "Bank 10") };
            var cfg = SyntheticConfig(steps, Array.Empty<GoalTemplateSO>());
            try
            {
                var unlocks = UnlocksFold.Build(Array.Empty<RoundOutcome>(), cfg, null, Utc, Noon);
                Assert.IsFalse(unlocks.Ramp.IsComplete);
                CollectionAssert.IsEmpty(unlocks.Goals);
                Assert.IsNull(unlocks.TodaysTask);
            }
            finally { RampGoalFixtures.Destroy(cfg, steps[0]); }
        }

        [Test]
        public void Build_NeverThrows_ForNullConfig()
        {
            Assert.DoesNotThrow(() => UnlocksFold.Build(Array.Empty<RoundOutcome>(), null, null, Utc, Noon));
        }

        [Test]
        public void Build_OncePostRamp_ReturningProfile_GetsThreeGoalsAndADailyTask_CompletableWhileLosing()
        {
            var steps = RampGoalFixtures.CanonicalSteps();
            var goals = new[]
            {
                RampGoalFixtures.Goal("goal.bank", GoalMetric.BankedTotal, (10f, 40)),
                RampGoalFixtures.Goal("goal.rob", GoalMetric.RivalsRobbed, (2f, 40)),
                RampGoalFixtures.Goal("goal.play", GoalMetric.RoundsPlayed, (2f, 40)),
            };
            var cfg = SyntheticConfig(steps, goals);
            try
            {
                // Clear the ramp first (a "returning" profile), then play two MORE losing rounds this
                // same week — both last place — that between them satisfy every goal above.
                var rounds = RampGoalFixtures.RoundsThatClearTheCanonicalRamp(Noon);
                var clearedOnly = UnlocksFold.Build(rounds, cfg, null, Utc, Noon.AddMinutes(3));
                Assert.IsTrue(clearedOnly.Ramp.IsComplete, "Test setup: the fixture round history must clear the ramp.");

                rounds.Add(RampGoalFixtures.RoundFor(ChickenClass.Warrior, at: Noon.AddMinutes(4), placement: 4, banked: 10f, rivalsRobbed: 2));
                rounds.Add(RampGoalFixtures.RoundFor(ChickenClass.Warrior, at: Noon.AddMinutes(5), placement: 4, banked: 1f));

                var unlocks = UnlocksFold.Build(rounds, cfg, null, Utc, Noon.AddMinutes(5));
                Assert.IsTrue(unlocks.Ramp.IsComplete);
                Assert.AreEqual(GoalRotation.GoalsPerWeek, unlocks.Goals.Count);
                Assert.IsTrue(unlocks.Goals.All(g => g.Completed),
                    "Every one of the week's goals must be completable while losing every round.");
                Assert.IsNotNull(unlocks.TodaysTask);
            }
            finally { RampGoalFixtures.Destroy(cfg); RampGoalFixtures.Destroy(steps); RampGoalFixtures.Destroy(goals); }
        }

        [Test]
        public void DailyTask_SurvivesAFailedFirstRound_ASecondRoundTheSameDayStillPaysIt()
        {
            var steps = RampGoalFixtures.CanonicalSteps();
            var cfg = SyntheticConfig(steps, Array.Empty<GoalTemplateSO>());
            try
            {
                var clearing = RampGoalFixtures.RoundsThatClearTheCanonicalRamp(Noon);
                var afterClear = UnlocksFold.Build(clearing, cfg, null, Utc, Noon.AddMinutes(3));
                Assert.IsTrue(afterClear.Ramp.IsComplete, "Test setup: the fixture round history must clear the ramp.");

                // Find a day whose rotated task is non-trivial (has real conditions), so "a bad first
                // round" is an actual concept for it. Deterministic search, not randomness.
                DateTime day = Noon;
                DailyTask.Template task;
                int guard = 0;
                do
                {
                    task = DailyTask.ForDay(ProgressionCalendar.FormatDay(day));
                    if (task.Conditions.Length > 0) break;
                    day = day.AddDays(1);
                } while (++guard < 400);
                Assert.Greater(task.Conditions.Length, 0, "Could not find a day with a non-trivial daily task within 400 tries.");

                var failingRound = RampGoalFixtures.RoundFor(ChickenClass.Warrior, at: day, placement: 4, banked: 0f, stolen: 0f, rivalsRobbed: 0, opponentsDisabled: 0);
                var passingRound = RampGoalFixtures.RoundFor(ChickenClass.Warrior, at: day.AddHours(1), placement: 4, banked: 5f, stolen: 3f, rivalsRobbed: 2, opponentsDisabled: 1);

                Assert.IsFalse(DailyTask.IsSatisfiedBy(task, failingRound), "Test setup: the first round must fail the day's actual task.");
                Assert.IsTrue(DailyTask.IsSatisfiedBy(task, passingRound), "Test setup: the second round must pass it (it clears every template's conditions at once).");

                var rounds = new List<RoundOutcome>(clearing) { failingRound, passingRound };
                var unlocks = UnlocksFold.Build(rounds, cfg, null, Utc, day.AddHours(1));

                Assert.IsNotNull(unlocks.TodaysTask);
                Assert.IsTrue(unlocks.TodaysTask.Completed,
                    "A bad first round must not burn the daily task: a later round the same day still pays it.");
            }
            finally { RampGoalFixtures.Destroy(cfg); RampGoalFixtures.Destroy(steps); }
        }

        [Test]
        public void ComputeBonusGrain_NeverThrows_AndIsZeroForNoHistory()
        {
            var steps = RampGoalFixtures.CanonicalSteps();
            var cfg = SyntheticConfig(steps, Array.Empty<GoalTemplateSO>());
            try
            {
                Assert.AreEqual(0, UnlocksFold.ComputeBonusGrain(Array.Empty<RoundOutcome>(), cfg, null, Utc));
                Assert.DoesNotThrow(() => UnlocksFold.ComputeBonusGrain(null, cfg, null, Utc));
                Assert.DoesNotThrow(() => UnlocksFold.ComputeBonusGrain(Array.Empty<RoundOutcome>(), null, null, Utc));
            }
            finally { RampGoalFixtures.Destroy(cfg); RampGoalFixtures.Destroy(steps); }
        }
    }

    // ==== Ramp state is never networked =================================================

    public sealed class RampNetworkingBoundaryTests
    {
        private static readonly string[] RampFiles =
        {
            "RampStepSO.cs", "RampController.cs", "GoalTemplateSO.cs", "GoalRotation.cs",
            "DailyTask.cs", "ProgressionUnlocks.cs", "UnlocksFold.cs",
        };

        [Test]
        public void RampGoalFiles_NeverImportFusion_OrDeclareNetworkedState()
        {
            const string progressionDir = SourceScan.ScriptsRoot + "/Progression";
            var files = SourceScan.FilesUnder(progressionDir).ToList();

            var violations = new List<string>();
            foreach (string fileName in RampFiles)
            {
                string path = files.FirstOrDefault(p => p.EndsWith("/" + fileName, StringComparison.Ordinal));
                Assert.IsNotNull(path, $"{fileName} not found under {progressionDir}; the file set has changed — update RampFiles.");

                foreach (var (number, code) in SourceScan.CodeLines(path))
                {
                    if (code.Contains("using Fusion")) violations.Add($"{path}:{number} imports Fusion: {code}");
                    if (code.Contains("[Networked]")) violations.Add($"{path}:{number} declares a [Networked] member: {code}");
                    if (code.Contains(": NetworkBehaviour")) violations.Add($"{path}:{number} extends NetworkBehaviour: {code}");
                }
            }

            Assert.IsEmpty(violations,
                "Ramp/goal/daily-task state is local-only and must never be networked. Violations:\n" + string.Join("\n", violations));
        }
    }

    // ==== Shipped ramp/goal assets =======================================================

    public sealed class RampAssetTests
    {
        private const string RampDir = TestAssets.DataRoot + "/Progression/Ramp";

        [Test]
        public void TheConfigSlot_ResolvesToFourRealAssets_UnderTheRampFolder()
        {
            var steps = Config().RampSteps;
            Assert.IsNotNull(steps);
            Assert.AreEqual(4, steps.Length, "The ramp is exactly 4 fixed steps.");

            foreach (var step in steps)
            {
                Assert.IsNotNull(step);
                string path = UnityEditor.AssetDatabase.GetAssetPath(step);
                StringAssert.StartsWith(RampDir + "/", path, $"{step.name} must live under {RampDir}.");
            }
        }

        [Test]
        public void EveryStep_IsUsable_AndSaysWhatItIs()
        {
            foreach (var step in Config().RampSteps)
            {
                Assert.IsNull(step.Problem(), $"{step.name}: {step.Problem()}");
                Assert.IsNotEmpty(step.DisplayName);
                Assert.IsNotEmpty(step.ObjectiveDescription);
            }
        }

        [Test]
        public void OnlyTheLastStep_GrantsRolesSequentially()
        {
            var steps = Config().RampSteps;
            for (int i = 0; i < steps.Length - 1; i++)
            {
                Assert.IsFalse(steps[i].GrantRolesSequentially, $"{steps[i].name} (step {i + 1}) must not be the sequential step.");
            }
            Assert.IsTrue(steps[steps.Length - 1].GrantRolesSequentially, "The last ramp step must be the sequential Fatty→Speedy→Assassin one.");
        }
    }

    public sealed class GoalAssetTests
    {
        private const string GoalsDir = TestAssets.DataRoot + "/Progression/Goals";

        [Test]
        public void EveryTemplate_LivesUnderTheGoalsFolder_AndIsUsable()
        {
            var templates = Config().GoalTemplates;
            Assert.IsNotNull(templates);
            Assert.GreaterOrEqual(templates.Length, GoalRotation.GoalsPerWeek);

            foreach (var template in templates)
            {
                Assert.IsNotNull(template);
                Assert.IsNull(template.Problem(), $"{template.name}: {template.Problem()}");
                string path = UnityEditor.AssetDatabase.GetAssetPath(template);
                StringAssert.StartsWith(GoalsDir + "/", path, $"{template.name} must live under {GoalsDir}.");
            }
        }

        [Test]
        public void EveryKey_MatchesThePattern_AndIsUnique()
        {
            var templates = Config().GoalTemplates;
            foreach (var template in templates)
            {
                StringAssert.IsMatch(GoalTemplateSO.KeyShape.ToString(), template.Key, $"{template.name}'s key is not a stable goal key.");
            }
            CollectionAssert.AllItemsAreUnique(templates.Select(t => t.Key).ToList());
        }
    }
}
