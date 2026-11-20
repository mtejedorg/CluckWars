using System;
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
    /// <see cref="OracleKits"/> reads the eight shipped kits from the assets. Nothing here restates an asset value:
    /// each expectation is derived from the asset or the passive hook the kit must agree with.
    /// </summary>
    public sealed class OracleKitsTests
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

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private BalanceBook _book;

        [SetUp]
        public void SetUp()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _book = new BalanceBook(new QuietLog(), _abilities, _classes, _match);
        }

        [TearDown]
        public void TearDown() => _book?.Dispose();

        private OracleInputs Inputs(BalanceVerdict verdict = null) => new OracleInputs
        {
            Map = _map, Book = _book, Verdict = verdict, Abilities = _abilities, Classes = _classes, Match = _match,
        };

        private ChickenStatsSO Stats(ChickenClass cls)
        {
            Assert.IsTrue(_classes.TryGet(cls, out var entry) && entry.Stats != null, $"no stats for {cls}");
            return entry.Stats;
        }

        private static OracleKit Kit(OracleKit[] kits, ChickenSubclass subclass) =>
            kits.Single(k => k.Label == subclass.ToString());

        [Test]
        public void EightKits_OnePerShippedPassive()
        {
            var kits = OracleKits.All(Inputs());
            int subclasses = Enum.GetValues(typeof(ChickenSubclass)).Cast<ChickenSubclass>().Count(s => s != ChickenSubclass.None);

            Assert.AreEqual(_abilities.Passives.Count(), kits.Length);
            Assert.AreEqual(subclasses, kits.Length, "one kit per ChickenSubclass");
            CollectionAssert.AreEquivalent(_abilities.Passives.Select(p => p.Subclass.ToString()), kits.Select(k => k.Label));
        }

        [Test]
        public void Hoarder_CarriesWhatItsPassiveGives()
        {
            var passive = _abilities.Passives.Single(p => p.Subclass == ChickenSubclass.Fatty_Hoarder);
            float expected = passive.ModifyCargoCapacity(Stats(ChickenClass.Fatty).CargoCapacity, null);

            var kit = Kit(OracleKits.All(Inputs()), ChickenSubclass.Fatty_Hoarder);

            Assert.AreEqual(Mathf.RoundToInt(expected), kit.CargoCapacity);
        }

        [Test]
        public void Sneaky_DoesNotForage()
        {
            foreach (var kit in OracleKits.All(Inputs()))
            {
                var subclass = _abilities.Passives.Single(p => p.Subclass.ToString() == kit.Label).Subclass;
                Assert.AreEqual(ChickenClasses.MayForage(subclass.ClassOf()), kit.Forages, kit.Label);
            }
            Assert.IsFalse(Kit(OracleKits.All(Inputs()), ChickenSubclass.Assassin_Spoiler).Forages);
            Assert.IsFalse(Kit(OracleKits.All(Inputs()), ChickenSubclass.Assassin_Thief).Forages);
        }

        [Test]
        public void SpeedKits_HaveABurst_OthersDoNot()
        {
            var kits = OracleKits.All(Inputs());
            int withBurst = 0;
            foreach (var passive in _abilities.Passives)
            {
                var speed = passive.Kit.FirstOrDefault(a => a != null && a.Job == AbilityJob.Speed) as SpeedBurstAbilitySO;
                var kit = Kit(kits, passive.Subclass);
                if (speed == null)
                {
                    Assert.AreEqual(0f, kit.BurstSeconds, passive.name);
                    Assert.LessOrEqual(kit.BurstMultiplier, 1f, passive.name);
                    continue;
                }
                withBurst++;
                Assert.AreEqual(speed.SpeedMultiplier, kit.BurstMultiplier, passive.name);
                Assert.AreEqual(speed.Duration, kit.BurstSeconds, passive.name);
                Assert.AreEqual(passive.ModifyCooldown(speed.Cooldown, speed, null), kit.BurstCooldown, 1e-4f, passive.name);
            }
            Assert.Greater(withBurst, 0, "no shipped kit holds a Speed ability: the test would prove nothing");
            Assert.Less(withBurst, kits.Length, "every kit holds a Speed ability: the test would prove nothing");
        }

        [Test]
        public void BullyJumps_SpeedyNever()
        {
            var kits = OracleKits.All(Inputs());
            Assert.IsTrue(Kit(kits, ChickenSubclass.Warrior_Bully).Jumps);
            Assert.IsFalse(Kit(kits, ChickenSubclass.Speedy_Slippery).Jumps);
            Assert.IsFalse(Kit(kits, ChickenSubclass.Speedy_Featherfoot).Jumps);
            foreach (var passive in _abilities.Passives)
                Assert.AreEqual(passive.Kit.Any(a => a != null && a.Job == AbilityJob.Jump), Kit(kits, passive.Subclass).Jumps, passive.name);
        }

        [Test]
        public void StandardFarmer_IsTheNakedWarrior()
        {
            var warrior = Stats(ChickenClass.Warrior);
            var kit = OracleKits.StandardFarmer(Inputs());

            Assert.AreEqual("Standard", kit.Label);
            Assert.IsTrue(kit.Forages);
            Assert.AreEqual(warrior.MoveSpeed, kit.MoveSpeed);
            Assert.AreEqual(warrior.CargoCapacity, kit.CargoCapacity);
            Assert.AreEqual(warrior.PeckAmount, kit.PeckAmount);
            Assert.AreEqual(warrior.PeckCooldown, kit.PeckCooldown);
            Assert.AreEqual(_match.DepositRatePerSecond, kit.DepositRate);
            Assert.IsFalse(kit.Jumps);
            Assert.IsFalse(kit.IgnoresPileSlow);
            Assert.AreEqual(0f, kit.BurstSeconds);
            Assert.LessOrEqual(kit.BurstMultiplier, 1f);
        }

        [Test]
        public void Verdict_ChangesTheKit()
        {
            var fatty = Stats(ChickenClass.Fatty);
            var hoarder = (HoarderPassiveSO)_abilities.Passives.Single(p => p.Subclass == ChickenSubclass.Fatty_Hoarder);
            int raised = Mathf.CeilToInt(Mathf.Max(fatty.CargoCapacity, hoarder.MinimumCapacity)) + 2;
            var verdict = _book.Evaluate($"{{\"schema\":1,\"assets\":{{\"{fatty.name}\":{{\"CargoCapacity\":{raised}}}}}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);

            var before = OracleKits.All(Inputs());
            var after = OracleKits.All(Inputs(verdict));

            foreach (var passive in _abilities.Passives)
            {
                var b = Kit(before, passive.Subclass);
                var a = Kit(after, passive.Subclass);
                if (passive.Subclass.ClassOf() == ChickenClass.Fatty)
                    Assert.AreNotEqual(b.CargoCapacity, a.CargoCapacity, passive.name);
                else
                    Assert.AreEqual(b, a, passive.name);
            }
            Assert.AreEqual(raised, Kit(after, ChickenSubclass.Fatty_Hoarder).CargoCapacity);
            Assert.AreEqual(OracleKits.StandardFarmer(Inputs()), OracleKits.StandardFarmer(Inputs(verdict)), "the Warrior is untouched");
        }

        [Test]
        public void Preview_CopiesAreDestroyed()
        {
            var fatty = Stats(ChickenClass.Fatty);
            var verdict = _book.Evaluate($"{{\"schema\":1,\"assets\":{{\"{fatty.name}\":{{\"CargoCapacity\":{fatty.CargoCapacity + 1}}}}}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            int Count() => Resources.FindObjectsOfTypeAll<ScriptableObject>().Length;
            int before = Count();

            OracleKits.All(Inputs(verdict));
            OracleKits.StandardFarmer(Inputs(verdict));
            OracleKits.Rules(Inputs(verdict), true, FarmFoodEvent.None);

            Assert.AreEqual(before, Count(), "OracleKits left Preview copies behind");
        }

        [Test]
        public void Food_IsTheOverrideElseTheBakedMap()
        {
            var baked = OracleKits.Food(Inputs());
            Assert.AreEqual(_map.PersonalFood, baked.Personal);
            Assert.AreEqual(_map.ContestedFood, baked.Contested);
            Assert.AreEqual(_map.CentreFood, baked.Centre);
            Assert.Greater(baked.Personal, 0f, $"the map carries no pile food: run {OracleMapBaker.MenuPath}");

            var inputs = Inputs();
            inputs.FoodOverride = new OracleFood { Personal = baked.Personal + 1f, Contested = baked.Contested + 2f, Centre = baked.Centre + 3f };
            var overridden = OracleKits.Food(inputs);
            Assert.AreEqual(baked.Personal + 1f, overridden.Personal);
            Assert.AreEqual(baked.Contested + 2f, overridden.Contested);
            Assert.AreEqual(baked.Centre + 3f, overridden.Centre);
        }

        [Test]
        public void Rules_ReadTheMatchConfigAndTheGamesConstants()
        {
            var rules = OracleKits.Rules(Inputs(), true, FarmFoodEvent.Restock);

            Assert.AreEqual(_match.FoodTargetToWin, rules.WinTarget);
            Assert.AreEqual(_match.MatchDurationSeconds, rules.ClockSeconds);
            Assert.AreEqual(_match.ComebackEventSecondsLeft, rules.EventSecondsLeft);
            Assert.IsTrue(rules.StopAtTarget);
            Assert.AreEqual(FarmFoodEvent.Restock, rules.Event);
            Assert.AreEqual(GameManager.GoldenPileFood, rules.GoldenPileFood);
            Assert.AreEqual(GameManager.RestockFood, rules.RestockFood);
            Assert.AreEqual(ChickenController.PileSlowFactor, rules.PileSlowFactor);
            Assert.Greater(rules.TickSeconds, 0f);
            Assert.IsFalse(OracleKits.Rules(Inputs(), false, FarmFoodEvent.None).StopAtTarget);
        }
    }
}
