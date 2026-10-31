using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The fixed-kits baseline. Every test reads the REAL assets and states rules, never kit contents: kits will be
    /// re-authored many times, and a test that repeats them as literals would only test itself.
    /// </summary>
    public class FixedKitTests
    {
        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(message);
        }

        private readonly List<UnityEngine.Object> _made = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in _made) UnityEngine.Object.DestroyImmediate(o);
            _made.Clear();
        }

        private static AbilityRegistrySO Registry => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
        private static PassiveAbilitySO Spec(ChickenSubclass s) => Registry.Passives.First(p => p.Subclass == s);
        private static AbilityBaseSO Pool<T>() where T : AbilityBaseSO => Registry.All.First(a => a is T);

        /// <summary>A throwaway copy of a real passive with its kit swapped, so a rule can be broken without touching an asset.</summary>
        private PassiveAbilitySO Broken(ChickenSubclass s, Func<List<AbilityBaseSO>, IEnumerable<AbilityBaseSO>> edit)
        {
            var copy = UnityEngine.Object.Instantiate(Spec(s));
            copy.name = s.ToString();
            copy.Kit = edit(copy.Kit.ToList()).ToArray();
            _made.Add(copy);
            return copy;
        }

        // ---- the shipped data ----------------------------------------------------------------------------------

        [Test]
        public void ShippedConfig_IsFixed()
        {
            Assert.AreEqual(LoadoutMode.Fixed, TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath).LoadoutMode);
        }

        [Test]
        public void ShippedRoster_BreaksNoKitRule()
        {
            var problems = FixedKit.ValidateRoster(Registry.Passives, Registry.All);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EverySpecialization_HasItsOwnKit()
        {
            var kits = Registry.Passives.Select(p => string.Join(",", p.Kit.Select(a => a.name))).ToList();
            Assert.AreEqual(8, kits.Count);
            Assert.AreEqual(kits.Count, kits.Distinct().Count(), "two specializations play the same kit");
        }

        // ---- break it: each rule must actually fire ------------------------------------------------------------

        [Test]
        public void TooFewAbilities_IsAProblem() =>
            StringAssert.Contains("expected 4", Only(Broken(ChickenSubclass.Warrior_Bully, k => k.Take(3))));

        [Test]
        public void ADuplicate_IsAProblem() =>
            StringAssert.Contains("twice", Only(Broken(ChickenSubclass.Warrior_Bully, k => { k[3] = k[2]; return k; })));

        [Test]
        public void ANullSlot_IsAProblem() =>
            StringAssert.Contains("empty", Only(Broken(ChickenSubclass.Warrior_Bully, k => { k[3] = null; return k; })));

        [Test]
        public void AForagerWithoutPeck_IsAProblem() =>
            StringAssert.Contains("Peck", Only(Broken(ChickenSubclass.Fatty_Hoarder, k => { k[0] = Pool<EggShellAbilitySO>(); return k; })));

        [Test]
        public void PeckOnAnAssassin_IsAProblem() =>
            StringAssert.Contains("Peck", Only(Broken(ChickenSubclass.Assassin_Thief, k => { k[0] = Pool<PeckAbilitySO>(); return k; })));

        [Test]
        public void TraversalOnASpeedy_IsAProblem() =>
            StringAssert.Contains("traversal", Only(Broken(ChickenSubclass.Speedy_Slippery, k => { k[3] = Pool<RollPushAbilitySO>(); return k; })));

        [Test]
        public void ASpoilerWithoutTheMark_IsAProblem() =>
            StringAssert.Contains("Death Mark", Only(Broken(ChickenSubclass.Assassin_Spoiler, k => { k[0] = Pool<SnatchAbilitySO>(); return k; })));

        [Test]
        public void TheMarkWithoutAStunSource_IsAProblem() =>
            StringAssert.Contains("stun", Only(Broken(ChickenSubclass.Assassin_Spoiler, k => { k[k.FindIndex(FixedKit.EmitsStun)] = Pool<SnatchAbilitySO>(); return k; })));

        private static string Only(PassiveAbilitySO p)
        {
            var problems = FixedKit.Problems(p);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            return problems[0];
        }

        [Test]
        public void ValidateRoster_ReportsAKitAbilityOutsideTheRegistry()
        {
            var stray = UnityEngine.Object.Instantiate(Pool<RootEggAbilitySO>());
            stray.name = "Stray";
            _made.Add(stray);
            var passives = Registry.Passives.ToList();
            passives[0] = Broken(passives[0].Subclass, k => { k[k.Count - 1] = stray; return k; });
            Assert.IsTrue(FixedKit.ValidateRoster(passives, Registry.All).Any(p => p.Contains("Stray")));
        }

        [Test]
        public void ValidateRoster_ReportsASubclassWithNoSpecialization()
        {
            var passives = Registry.Passives.Where(p => p.Subclass != ChickenSubclass.Fatty_Bulwark);
            Assert.IsTrue(FixedKit.ValidateRoster(passives, Registry.All).Any(p => p.Contains(nameof(ChickenSubclass.Fatty_Bulwark))));
        }

        // ---- resolution -------------------------------------------------------------------------------------------

        [Test]
        public void TryResolve_Fixed_ReturnsACopyInSlotOrder()
        {
            var spec = Spec(ChickenSubclass.Speedy_Featherfoot);
            Assert.IsTrue(FixedKit.TryResolve(LoadoutMode.Fixed, spec, out var kit, out var problem), problem);
            CollectionAssert.AreEqual(spec.Kit, kit);
            Assert.AreNotSame(spec.Kit, kit);
        }

        [Test]
        public void TryResolve_Free_DeclinesWithoutAProblem()
        {
            Assert.IsFalse(FixedKit.TryResolve(LoadoutMode.Free, Spec(ChickenSubclass.Warrior_Bully), out var kit, out var problem));
            Assert.IsNull(kit);
            Assert.IsNull(problem);
        }

        [Test]
        public void TryResolve_AMalformedKit_DeclinesAndNamesThePassive()
        {
            var broken = Broken(ChickenSubclass.Warrior_Bully, k => k.Take(2));
            Assert.IsFalse(FixedKit.TryResolve(LoadoutMode.Fixed, broken, out _, out var problem));
            StringAssert.Contains(nameof(ChickenSubclass.Warrior_Bully), problem);
        }

        // ---- the spawn chokepoint (real MatchBootstrapper.ResolveLegalLoadout) -------------------------------------

        private AbilityBaseSO[] Spawn(LoadoutMode mode, ChickenClass cls, PassiveAbilitySO passive, RecordingLog log, params AbilityBaseSO[] picks)
        {
            var config = ScriptableObject.CreateInstance<MatchConfigSO>();
            config.LoadoutMode = mode;
            _made.Add(config);
            var go = new GameObject("boot");
            _made.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<MatchBootstrapper>();
            void Set(string f, object v) => typeof(MatchBootstrapper).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(boot, v);
            Set("_abilityRegistry", Registry);
            Set("_matchConfig", config);
            Set("_log", log);
            var args = new object[]
            {
                cls, passive, picks.ElementAtOrDefault(0), picks.ElementAtOrDefault(1), picks.ElementAtOrDefault(2), picks.ElementAtOrDefault(3),
                null, null, null, null, null,
            };
            typeof(MatchBootstrapper).GetMethod("ResolveLegalLoadout", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(boot, args);
            return new[] { args[7], args[8], args[9], args[10] }.Cast<AbilityBaseSO>().ToArray();
        }

        [Test]
        public void Fixed_EverySpecializationSpawnsItsKit_WhateverTheSelectionSays()
        {
            var noise = Pool<EggShellAbilitySO>();
            foreach (var p in Registry.Passives)
            {
                var log = new RecordingLog();
                var slots = Spawn(LoadoutMode.Fixed, p.Subclass.ClassOf(), p, log, noise, noise, null, Pool<PeckAbilitySO>());
                CollectionAssert.AreEqual(p.Kit, slots, p.name);
                Assert.IsEmpty(log.Errors);
            }
        }

        [Test]
        public void Fixed_AMalformedKit_LogsAnErrorAndAForagerKeepsItsPeck()
        {
            var log = new RecordingLog();
            var broken = Broken(ChickenSubclass.Fatty_Hoarder, k => k.Take(1));
            var slots = Spawn(LoadoutMode.Fixed, ChickenClass.Fatty, broken, log);
            Assert.AreEqual(1, log.Errors.Count);
            StringAssert.Contains(nameof(ChickenSubclass.Fatty_Hoarder), log.Errors[0]);
            Assert.IsTrue(slots.Any(a => a is PeckAbilitySO), "the Free path must still give a forager its Peck");
        }

        [Test]
        public void Free_TheKitIsNotRead()
        {
            var bully = Spec(ChickenSubclass.Warrior_Bully);
            var log = new RecordingLog();
            var chosen = Registry.GetClassAbilitiesForClass(ChickenClass.Warrior).Where(a => !bully.Kit.Contains(a)).ToArray();
            Assert.IsNotEmpty(chosen, "test fixture: a Warrior ability outside the Bully kit");
            CollectionAssert.Contains(Spawn(LoadoutMode.Free, ChickenClass.Warrior, bully, log, chosen[0]), chosen[0]);
        }

        // ---- bots ---------------------------------------------------------------------------------------------------

        [Test]
        public void RollBotLineup_SameSeedSameLineup_ThreeDistinctClasses()
        {
            for (int seed = -50; seed < 50; seed++)
            {
                var a = FixedKit.RollBotLineup(seed, 3, Registry.Passives);
                var b = FixedKit.RollBotLineup(seed, 3, Registry.Passives);
                CollectionAssert.AreEqual(a, b, "the menu and the spawner must agree");
                Assert.AreEqual(3, a.Select(p => p.Subclass.ClassOf()).Distinct().Count(), $"seed {seed}");
            }
        }

        [Test]
        public void RollBotLineup_EverySpecializationIsReachable()
        {
            var seen = new HashSet<ChickenSubclass>();
            for (int seed = 0; seed < 400; seed++)
                foreach (var p in FixedKit.RollBotLineup(seed, 3, Registry.Passives)) seen.Add(p.Subclass);
            Assert.AreEqual(Registry.Passives.Count(), seen.Count);
        }

        [Test]
        public void RollBotLineup_APinWins_AndLeavesTheOtherBotsAsRolled()
        {
            var rolled = FixedKit.RollBotLineup(7, 3, Registry.Passives);
            var pinned = FixedKit.RollBotLineup(7, 3, Registry.Passives,
                new[] { ChickenSubclass.None, ChickenSubclass.Assassin_Spoiler });
            Assert.AreEqual(rolled[0], pinned[0]);
            Assert.AreEqual(ChickenSubclass.Assassin_Spoiler, pinned[1].Subclass);
            Assert.AreEqual(rolled[2], pinned[2]);
        }

        [Test]
        public void RollBotLineup_AnEmptyRoster_YieldsNulls_NotAThrow()
        {
            CollectionAssert.AreEqual(new PassiveAbilitySO[3], FixedKit.RollBotLineup(1, 3, null));
        }

        [Test]
        public void RollBotLineup_SkipsAPassiveWithNoSubclass()
        {
            var unnamed = Broken(ChickenSubclass.Warrior_Bully, k => k);
            unnamed.Subclass = ChickenSubclass.None;
            CollectionAssert.AreEqual(new PassiveAbilitySO[2], FixedKit.RollBotLineup(1, 2, new[] { unnamed }));
        }

        /// <summary>
        /// "No bot silently idle": every kit ability must be reachable by its class's strategy through a real CastPlan, or by
        /// the two paths outside the plans (Peck at a pile, a steal shell).
        /// </summary>
        [Test]
        public void EveryKitAbility_IsCastableByItsClassBot()
        {
            foreach (var p in Registry.Passives)
            {
                var profile = BotTactics.ProfileFor(p.Subclass.ClassOf());
                var situations = BotTactics.ReachableSituations(profile);
                foreach (var a in p.Kit)
                {
                    var role = a.ResolveBotRole();
                    if (role == BotRole.Forage || BotTactics.IsStealShield(a)) continue;
                    bool reachable = situations.Any(s =>
                        BotTactics.CastPlan(profile.Strategy, s, true).Contains(role) ||
                        BotTactics.CastPlan(profile.Strategy, s, false).Contains(role));
                    Assert.IsTrue(reachable, $"{p.name}: '{a.name}' (role {role}) is reachable by no {profile.Strategy} cast plan");
                }
            }
        }

        // ---- LastSetup ----------------------------------------------------------------------------------------------

        [Test]
        public void LastSetup_Fixed_IgnoresStoredAbilities_AndAnOldFreeRecordStillCounts()
        {
            var spec = Spec(ChickenSubclass.Speedy_Slippery);
            var rec = new LastSetupRecord
            {
                Class = (int)ChickenClass.Speedy,
                Subclass = (int)spec.Subclass,
                Abilities = new[] { "NotAnAbilityAnyMore", "Gone" },
                Mode = (int)SessionMode.Host,
            };

            Assert.IsTrue(LastSetupResolver.TryResolve(rec, Registry.All, out var r, out var problem, LoadoutMode.Fixed), problem);
            Assert.AreEqual(spec, r.Passive);
            Assert.AreEqual(SessionMode.Host, r.Mode);
            CollectionAssert.AreEqual(spec.Kit, r.Slots);

            Assert.IsFalse(LastSetupResolver.TryResolve(rec, Registry.All, out _, out _, LoadoutMode.Free), "Free keeps its strict checks");
        }

        [Test]
        public void LastSetup_Fixed_StillRefusesAPerkThatIsGone()
        {
            var rec = new LastSetupRecord { Class = (int)ChickenClass.Speedy, Subclass = 99, Abilities = new string[0], Mode = 0 };
            Assert.IsFalse(LastSetupResolver.TryResolve(rec, Registry.All, out _, out _, LoadoutMode.Fixed));
        }

        // ---- harness ------------------------------------------------------------------------------------------------

        [Test]
        public void CwPerk_SetsTheClass_AndWinsOverADisagreeingClass()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_perk", "speedy_featherfoot", "-cw_class", "Fatty" });
            Assert.AreEqual(ChickenSubclass.Speedy_Featherfoot, cfg.Perk);
            Assert.AreEqual(ChickenClass.Speedy, cfg.Class);
            Assert.AreEqual(1, cfg.Problems.Count);
        }

        [Test]
        public void CwPerk_Unknown_IsReportedAndIgnored()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_perk", "Ninja" });
            Assert.AreEqual(ChickenSubclass.None, cfg.Perk);
            Assert.AreEqual(1, cfg.Problems.Count);
        }

        [Test]
        public void CwBotPerks_PinsByIndex_DashRolls_BadItemsRollAndAreReported()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_botperks", "Warrior_Bully,Ninja,Assassin_Thief" });
            CollectionAssert.AreEqual(new[] { ChickenSubclass.Warrior_Bully, ChickenSubclass.None, ChickenSubclass.Assassin_Thief }, cfg.BotPerks);
            Assert.AreEqual(1, cfg.Problems.Count);
        }

        [Test]
        public void ZoneAndAuraChecks_FindTheirCarrierInTheKit()
        {
            Assert.GreaterOrEqual(AutoTestRules.PickZoneSlot(Spec(ChickenSubclass.Fatty_Hoarder).Kit), 0);
            Assert.GreaterOrEqual(AutoAuraRules.PickAuraSlot(Spec(ChickenSubclass.Speedy_Featherfoot).Kit.ToList()), 0);
        }

        // ---- GEAR UP wiring -----------------------------------------------------------------------------------------

        [Test]
        public void GearUp_FixedHidesTheDeckInUss_AndKeepsTheBindingContract()
        {
            StringAssert.Contains(".cw-gear-body--fixed .cw-deck-scroll { display: none; }", File.ReadAllText("Assets/UI/Styles/CluckWarsTheme.uss"));
            var uxml = File.ReadAllText("Assets/UI/CharacterSelect.uxml");
            StringAssert.Contains("name=\"KitTitle\"", uxml);
            StringAssert.Contains("name=\"Body\"", uxml);
        }
    }
}
