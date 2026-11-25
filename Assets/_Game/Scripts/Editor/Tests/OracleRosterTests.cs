using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The what-if roster swap (design 2026-11-25, section 5): one passive gives its seat to another in a copy of the registry. Since the build (BL-226)
    /// the Enforcer holds the seat and the shelved Relentless is the passive that is not registered, so a what-if can swap it back.
    /// </summary>
    public sealed class OracleRosterTests
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

        private AbilityRegistrySO _shipped;
        private PassiveAbilitySO _enforcer;   // seated (shipped)
        private PassiveAbilitySO _relentless; // shelved: not in the registry
        private AbilityRegistrySO _copy;

        [SetUp]
        public void SetUp()
        {
            _shipped = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _enforcer = _shipped.Passives.First(p => p.name == "Enforcer");
            _relentless = WarriorPassiveDataTests.ShelvedRelentless();
        }

        [TearDown]
        public void TearDown()
        {
            if (_copy != null) UnityEngine.Object.DestroyImmediate(_copy);
            _copy = null;
        }

        [Test]
        public void Swap_ReplacesOneSeat_KeepsEveryOtherEntry()
        {
            _copy = OracleRoster.Swap(_shipped, _enforcer, _relentless);
            Assert.AreEqual(_shipped.All.Length, _copy.All.Length);
            int at = Array.IndexOf(_shipped.All, _enforcer);
            for (int i = 0; i < _shipped.All.Length; i++)
                Assert.AreSame(i == at ? _relentless : _shipped.All[i], _copy.All[i], $"entry {i}");
            Assert.IsFalse(_copy.Passives.Contains(_enforcer));
            Assert.IsTrue(_copy.Passives.Contains(_relentless));
        }

        [Test]
        public void Swap_LeavesTheShippedRegistryUntouched()
        {
            var before = (AbilityBaseSO[])_shipped.All.Clone();
            _copy = OracleRoster.Swap(_shipped, _enforcer, _relentless);
            CollectionAssert.AreEqual(before, _shipped.All);
            Assert.AreNotSame(_shipped, _copy);
            Assert.AreNotSame(_shipped.All, _copy.All, "the copy holds its own array");
        }

        [Test]
        public void Swap_RefusesAPassiveOfAnotherClass_OrOneNotInTheRegistry()
        {
            var speedy = _shipped.Passives.First(p => p.AllowedClasses == ChickenClassFlags.Speedy);
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, speedy, _relentless), "a Warrior passive cannot take a Speedy seat");
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, _relentless, _enforcer), "the outgoing passive must be in the registry");
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, _enforcer,
                _shipped.Passives.First(p => p.name == "Bully")), "the incoming passive must not be seated already");
            Assert.Throws<ArgumentNullException>(() => OracleRoster.Swap(_shipped, _enforcer, null));
        }

        [Test]
        public void SwappedRegistry_LetsTheBookAddressTheShelvedKit_AndTheShippedOneAddressesTheEnforcer()
        {
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            using (var book = new BalanceBook(new QuietLog(), _shipped, classes, match))
            {
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"Enforcer\":{\"FreshSeconds\":1,\"FreshBiteShare\":0.5,\"DebtSteal\":3,\"DebtCap\":4,\"DebtSeconds\":9},\"Warrior\":{\"PeckCooldown\":0.95,\"PeckAmount\":2}}}", "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                Assert.AreEqual(1, verdict.Ignored.Count, "only the deny-listed FreshBiteShare is skipped");
                StringAssert.Contains("FreshBiteShare", verdict.Ignored[0]);
                Assert.AreEqual(4, verdict.Changes.Count(c => c.Contains("Enforcer.")), "the other Enforcer fields are overridable");
                Assert.AreEqual(2, verdict.Changes.Count(c => c.Contains("Warrior.")), "and the Warrior's foraging is a plain class delta");
            }
            _copy = OracleRoster.Swap(_shipped, _enforcer, _relentless);
            using (var book = new BalanceBook(new QuietLog(), _copy, classes, match))
            {
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"Relentless\":{\"DurationMultiplier\":0.7}}}", "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                CollectionAssert.IsEmpty(verdict.Ignored);
                Assert.AreEqual(1, verdict.Changes.Count(c => c.Contains("Relentless.")), "over the swapped registry the shelved kit is addressable");
                var gone = book.Evaluate("{\"schema\":1,\"assets\":{\"Enforcer\":{\"FreshSeconds\":1}}}", "test");
                Assert.IsNotEmpty(gone.Ignored, "and the seat's old holder is not");
            }
        }
    }
}
