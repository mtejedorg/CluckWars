using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The shipped data of the two Warrior kits under test (design 2026-11-25): both ship off, the Champion is not in the game's registry.</summary>
    public sealed class WarriorPassiveDataTests
    {
        private const string PassivesDir = TestAssets.AbilitiesDir + "/Passives";
        /// <summary>Simulator-only assets live outside the Abilities folder, whose tests treat every asset in it as the shipped roster.</summary>
        public const string SimulatorDir = TestAssets.DataRoot + "/Simulator";

        private sealed class QuietLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private static AbilityRegistrySO Registry => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
        private static PassiveAbilitySO Passive(string name) => TestAssets.Load<PassiveAbilitySO>($"{PassivesDir}/{name}.asset");
        private static PassiveAbilitySO Champion() => TestAssets.Load<PassiveAbilitySO>($"{SimulatorDir}/Champion.asset");

        [Test]
        public void ShippedBully_HitStealIsOff()
        {
            var bully = (BullyPassiveSO)Registry.Passives.First(p => p.name == "Bully");
            Assert.IsFalse(bully.HitSteal.Active, "the shipped Bully takes nothing from a hit");
            Assert.AreEqual(0f, bully.HitStealStun);
            Assert.AreEqual(0f, bully.HitStealShove);
            Assert.AreEqual(0f, bully.HitStealSlow);
            Assert.AreEqual(3f, bully.HitStealLockSeconds, "the designed lock is already authored; with every amount 0 it never starts");
            Assert.IsFalse(Passive("Relentless").HitSteal.Active);
            foreach (var p in Registry.Passives) Assert.IsFalse(p.Bite.Active, $"{p.name} has no bite");
        }

        [Test]
        public void Champion_IsNotRegistered_AndItsKitIsLegal()
        {
            var hunter = Champion();
            var relentless = Passive("Relentless");
            Assert.IsFalse(Registry.All.Contains(hunter), "the game cannot pick the Champion");
            Assert.IsFalse(Registry.Passives.Contains(hunter));
            Assert.IsEmpty(FixedKit.Problems(hunter), string.Join(" | ", FixedKit.Problems(hunter)));
            Assert.AreEqual(new[] { "Peck", "WingSlam", "CluckShock", "Snatch" }, hunter.Kit.Select(k => k.name).ToArray(), "Peck, Stun, Shove, Steal");
            CollectionAssert.AreNotEqual(relentless.Kit, hunter.Kit, "no longer Relentless's kit");
            Assert.AreEqual(relentless.Subclass, hunter.Subclass);
            Assert.AreEqual(ChickenClassFlags.Warrior, hunter.AllowedClasses);
            Assert.AreEqual(1f, hunter.OutgoingControlMultiplier, "no multipliers");
            Assert.AreEqual(1f, hunter.ModifyControlDuration(1f, ControlKind.Stun, null));
            Assert.AreEqual(1f, hunter.ModifyCooldown(1f, null, null));
        }

        [Test]
        public void ShippedChampion_BiteAndTrophyStealAreOff()
        {
            var hunter = Champion();
            Assert.IsFalse(hunter.Bite.Active, "shipped: no rival is ever fresh");
            Assert.AreEqual(0f, hunter.Trophy.Steal, "shipped: trophies add nothing");
            Assert.AreEqual(0f, hunter.Bite.FoodShare, "shipped: a fresh hit takes no food (Maestro, 2026-11-25)");
            Assert.IsTrue(hunter.Trophy.IsChampion);
            Assert.AreEqual(3, hunter.Trophy.Cap);
            Assert.AreEqual(6f, hunter.Trophy.Seconds);
        }

        [Test]
        public void Delta_CanSetEveryNewField_AndTheBullyKit()
        {
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            using (var book = new BalanceBook(new QuietLog(), Registry, classes, match))
            {
                const string json = "{\"schema\":1,\"assets\":{\"Bully\":{\"HitStealStun\":2,\"HitStealShove\":2,\"HitStealSlow\":1,\"HitStealLockSeconds\":4}}," +
                                    "\"kits\":{\"Bully\":[\"Peck\",\"Shadowstep\",\"WingSlam\",\"CluckShock\"]}}";
                var verdict = book.Evaluate(json, "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                CollectionAssert.IsEmpty(verdict.Ignored, "nothing ignored: every new Bully field is overridable");
                foreach (string field in new[] { "HitStealStun", "HitStealShove", "HitStealSlow", "HitStealLockSeconds" })
                    Assert.IsTrue(verdict.Changes.Any(c => c.Contains("Bully." + field)), field);
                Assert.IsTrue(verdict.Changes.Any(c => c.Contains("Bully.Kit")), "the kit change is read");
            }
        }
    }
}
