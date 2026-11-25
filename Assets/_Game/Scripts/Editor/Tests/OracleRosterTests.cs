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
    /// <summary>The what-if roster swap (design 2026-11-25, section 5): one passive gives its seat to another in a copy of the registry.</summary>
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
        private PassiveAbilitySO _relentless;
        private PassiveAbilitySO _hunter;
        private AbilityRegistrySO _copy;

        [SetUp]
        public void SetUp()
        {
            _shipped = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _relentless = _shipped.Passives.First(p => p.name == "Relentless");
            _hunter = TestAssets.Load<PassiveAbilitySO>(WarriorPassiveDataTests.SimulatorDir + "/TrophyHunter.asset");
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
            _copy = OracleRoster.Swap(_shipped, _relentless, _hunter);
            Assert.AreEqual(_shipped.All.Length, _copy.All.Length);
            int at = Array.IndexOf(_shipped.All, _relentless);
            for (int i = 0; i < _shipped.All.Length; i++)
                Assert.AreSame(i == at ? _hunter : _shipped.All[i], _copy.All[i], $"entry {i}");
            Assert.IsFalse(_copy.Passives.Contains(_relentless));
            Assert.IsTrue(_copy.Passives.Contains(_hunter));
        }

        [Test]
        public void Swap_LeavesTheShippedRegistryUntouched()
        {
            var before = (AbilityBaseSO[])_shipped.All.Clone();
            _copy = OracleRoster.Swap(_shipped, _relentless, _hunter);
            CollectionAssert.AreEqual(before, _shipped.All);
            Assert.AreNotSame(_shipped, _copy);
            Assert.AreNotSame(_shipped.All, _copy.All, "the copy holds its own array");
        }

        [Test]
        public void Swap_RefusesAPassiveOfAnotherClass_OrOneNotInTheRegistry()
        {
            var speedy = _shipped.Passives.First(p => p.AllowedClasses == ChickenClassFlags.Speedy);
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, speedy, _hunter), "a Warrior passive cannot take a Speedy seat");
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, _hunter, _relentless), "the outgoing passive must be in the registry");
            Assert.Throws<InvalidOperationException>(() => OracleRoster.Swap(_shipped, _relentless,
                _shipped.Passives.First(p => p.name == "Bully")), "the incoming passive must not be seated already");
            Assert.Throws<ArgumentNullException>(() => OracleRoster.Swap(_shipped, _relentless, null));
        }

        [Test]
        public void SwappedRegistry_LetsTheBookAddressTheTrophyHunter()
        {
            _copy = OracleRoster.Swap(_shipped, _relentless, _hunter);
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            using (var book = new BalanceBook(new QuietLog(), _copy, classes, match))
            {
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"TrophyHunter\":{\"BreakReach\":2.5,\"TrophyRoom\":2,\"TrophiesClearOnBank\":true}}}", "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                CollectionAssert.IsEmpty(verdict.Ignored);
                Assert.AreEqual(3, verdict.Changes.Count(c => c.Contains("TrophyHunter.")));
            }
            using (var book = new BalanceBook(new QuietLog(), _shipped, classes, match))
            {
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"TrophyHunter\":{\"BreakReach\":2.5}}}", "test");
                Assert.IsNotEmpty(verdict.Ignored, "over the shipped registry the Trophy Hunter is not addressable");
            }
        }
    }
}
