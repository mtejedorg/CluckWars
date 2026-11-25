using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The shipped data of the two Warrior kits (design 2026-11-25, section 16): Bully and Enforcer are registered and on; Relentless is retired and parked.</summary>
    public sealed class WarriorPassiveDataTests
    {
        public const string PassivesDir = TestAssets.AbilitiesDir + "/Passives";

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

        /// <summary>Relentless is out of the registry (retired, kept as a possible future idea): tests of its own behaviour and the what-if swap load the asset by path.</summary>
        public static PassiveAbilitySO ShelvedRelentless() => Passive("Relentless");

        [Test]
        public void Enforcer_IsRegistered_RelentlessIsShelved()
        {
            var enforcer = Passive("Enforcer");
            var relentless = ShelvedRelentless();
            Assert.IsTrue(Registry.All.Contains(enforcer), "the game can pick the Enforcer");
            Assert.IsTrue(Registry.Passives.Contains(enforcer));
            Assert.IsFalse(Registry.All.Contains(relentless), "Relentless left the roster");
            Assert.IsTrue(relentless.Retired, "and is marked retired, its asset kept");
            Assert.IsFalse(enforcer.Retired);
            Assert.IsTrue(enforcer.IsSignature, "the Enforcer is the Warrior's default pick");
            Assert.AreEqual(enforcer, Registry.GetDefaultPassiveForClass(ChickenClass.Warrior));
            Assert.IsEmpty(FixedKit.Problems(enforcer), string.Join(" | ", FixedKit.Problems(enforcer)));
            Assert.AreEqual(new[] { "Peck", "WingSlam", "CluckShock", "Snatch" }, enforcer.Kit.Select(k => k.name).ToArray(), "Peck, Stun, Shove, Steal");
            CollectionAssert.AreNotEqual(relentless.Kit, enforcer.Kit);
            Assert.AreEqual(relentless.Subclass, enforcer.Subclass, "the enum value did not move (serialized data keeps working)");
            Assert.AreEqual(ChickenSubclass.Warrior_Enforcer, enforcer.Subclass);
            Assert.AreEqual(ChickenClassFlags.Warrior, enforcer.AllowedClasses);
            Assert.AreEqual(1f, enforcer.OutgoingControlMultiplier, "no control multipliers");
            Assert.AreEqual(1f, enforcer.ModifyControlDuration(1f, ControlKind.Stun, null));
            Assert.AreEqual(1f, enforcer.ModifyCooldown(1f, null, null), "no cooldown multiplier");
            Assert.IsFalse(Registry.Passives.Any(p => p.Subclass == relentless.Subclass && p != enforcer), "one registered passive per subclass");
        }

        [Test]
        public void ShippedBully_HitStealIsOn_AndItsKitHoldsNoSteal()
        {
            var bully = (BullyPassiveSO)Registry.Passives.First(p => p.name == "Bully");
            Assert.IsTrue(bully.HitSteal.Active, "the shipped Bully takes food from a hit");
            Assert.Greater(bully.HitStealStun, 0f, "a Stun that lands takes food");
            Assert.Greater(bully.HitStealShove, 0f, "a Shove that moves takes food");
            Assert.AreEqual(0f, bully.HitStealSlow, "an instant Slow never takes (designed 0)");
            Assert.Greater(bully.HitStealLockSeconds, 0f, "a take locks its victim");
            Assert.Greater(bully.PeckCooldownMultiplier, 1f, "the Bully forages slower: it earns the difference by hitting");
            CollectionAssert.DoesNotContain(bully.Kit.Select(k => k.Job).ToArray(), AbilityJob.Steal, "no Steal in the fixed kit");
            Assert.IsTrue(bully.Kit.Any(k => k.Job == AbilityJob.Stun) && bully.Kit.Any(k => k.Job == AbilityJob.Shove), "the hits that take food are in the kit");
            Assert.IsEmpty(FixedKit.Problems(bully), string.Join(" | ", FixedKit.Problems(bully)));
            Assert.IsFalse(ShelvedRelentless().HitSteal.Active);
            foreach (var p in Registry.Passives.Where(p => p != bully)) Assert.IsFalse(p.HitSteal.Active, $"{p.name} takes nothing from a hit");
        }

        [Test]
        public void ShippedEnforcer_FreshAndDebtsOn_NoFoodBite()
        {
            var enforcer = Passive("Enforcer");
            Assert.IsTrue(enforcer.Bite.Active, "a rival can be fresh");
            Assert.Greater(enforcer.Debt.Steal, 0f, "debts inflate the Steal");
            Assert.AreEqual(0f, enforcer.Bite.FoodShare, "a fresh hit takes no food (Maestro, 2026-11-25); the game plays no food bite");
            Assert.IsTrue(enforcer.Debt.IsEnforcer);
            Assert.GreaterOrEqual(enforcer.Debt.Cap, 1);
            Assert.Greater(enforcer.Debt.Seconds, 0f);
            Assert.Greater(enforcer.PeckCooldownMultiplier, 1f, "the Enforcer forages slower too");
            foreach (var p in Registry.Passives.Where(p => p != enforcer)) Assert.IsFalse(p.Bite.Active || p.Debt.IsEnforcer, $"{p.name} is no Enforcer");
        }

        [Test]
        public void FreshBiteShare_IsDeniedToADelta_TheGamePlaysNoFoodBite()
        {
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            using (var book = new BalanceBook(new QuietLog(), Registry, classes, match))
            {
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"Enforcer\":{\"FreshBiteShare\":1}}}", "test");
                Assert.IsTrue(verdict.Changes.All(c => !c.Contains("FreshBiteShare")), "the denied field changes nothing");
                Assert.IsTrue(verdict.Ignored.Any(i => i.Contains("FreshBiteShare") && i.Contains("deny-listed")), "and is reported as skipped");
            }
        }

        [Test]
        public void Delta_CanSetEveryNewField_AndTheBullyKit()
        {
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            using (var book = new BalanceBook(new QuietLog(), Registry, classes, match))
            {
                const string json = "{\"schema\":1,\"assets\":{\"Bully\":{\"HitStealStun\":3,\"HitStealShove\":3,\"HitStealSlow\":1,\"HitStealLockSeconds\":4}}," +
                                    "\"kits\":{\"Bully\":[\"Peck\",\"Shadowstep\",\"WingSlam\",\"Snatch\"]}}";
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
