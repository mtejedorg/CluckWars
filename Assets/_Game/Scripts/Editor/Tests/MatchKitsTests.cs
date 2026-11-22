using System;
using System.Globalization;
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
    /// <see cref="MatchKits"/> reads the shipped kits through <c>BalanceBook.Preview</c>. Every expectation is derived from the asset,
    /// the passive hook or a synthetic passive the test builds; nothing restates an asset value.
    /// </summary>
    public sealed class MatchKitsTests
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

        private static string Num(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private PassiveAbilitySO PassiveOf(MatchKit kit) => _abilities.Passives.Single(p => p.Subclass.ToString() == kit.Label);

        [Test]
        public void All_MatchesOracleKits_LabelsAndFarmNumbers()
        {
            var farms = OracleKits.All(Inputs());
            var kits = MatchKits.All(Inputs());

            Assert.AreEqual(farms.Length, kits.Length);
            for (int i = 0; i < kits.Length; i++)
            {
                Assert.AreEqual(farms[i].Label, kits[i].Label);
                Assert.AreEqual(farms[i], kits[i].Farm, kits[i].Label);
                var passive = PassiveOf(kits[i]);
                Assert.AreEqual(passive.Subclass.ClassOf(), kits[i].Class, kits[i].Label);
                CollectionAssert.AreEqual(passive.Kit.Select(a => a.name), kits[i].Abilities.Select(a => a.Asset), $"{kits[i].Label}: fixed-kit order");
                CollectionAssert.AreEqual(passive.Kit.Select(a => a.Job), kits[i].Abilities.Select(a => a.Job), kits[i].Label);
                Assert.AreEqual(BotTactics.ProfileFor(kits[i].Class).Strategy, kits[i].Bot.Strategy, kits[i].Label);
                Assert.AreEqual(passive.name, kits[i].Passive.Asset, kits[i].Label);
            }
        }

        [Test]
        public void AbilityCooldown_PassesThroughThePassive()
        {
            int changed = 0;
            foreach (var kit in MatchKits.All(Inputs()))
            {
                var passive = PassiveOf(kit);
                for (int slot = 0; slot < kit.Abilities.Length; slot++)
                {
                    var asset = passive.Kit[slot];
                    var ability = kit.Abilities[slot];
                    if (ability.Job == AbilityJob.Peck)
                    {
                        Assert.AreEqual(kit.Farm.PeckCooldown, ability.Cooldown, $"{kit.Label}: the Peck cools down by the class stat");
                        Assert.AreEqual(kit.Farm.PeckCastSeconds, ability.Duration, $"{kit.Label}: the Peck casts for the class stat");
                        continue;
                    }
                    float expected = passive.ModifyCooldown(asset.Cooldown, asset, null);
                    Assert.AreEqual(expected, ability.Cooldown, 1e-4f, $"{kit.Label}/{asset.name}");
                    if (Mathf.Abs(expected - asset.Cooldown) > 1e-4f) changed++;
                }
            }
            Assert.Greater(changed, 0, "no shipped passive changes a cooldown: the test would prove nothing");
        }

        [Test]
        public void Rules_ReadTheMatchConfigAndTheNamedConstants()
        {
            var rules = MatchKits.Rules(Inputs(), 12.5f, 1.25f);

            Assert.AreEqual(_match.BaseKeepOutRadius, rules.KeepOutRadius);
            Assert.AreEqual((float)_match.ExecuteBounty, rules.ExecuteBounty);
            Assert.AreEqual(ChickenMovement.KnockbackDecayRate, rules.KnockbackDecayRate);
            Assert.AreEqual(ChickenMovement.UnderdogSurgeSpeedFactor, rules.SurgeSpeedFactor);
            Assert.AreEqual(12.5f, rules.ArenaHalf);
            Assert.AreEqual(1.25f, rules.SightArrivalRadius);
            Assert.AreEqual(OracleKits.Rules(Inputs(), false, FarmFoodEvent.None).WinTarget, rules.Farm.WinTarget);
        }

        [Test]
        public void Verdict_ChangesAbilityCooldownAndRadius()
        {
            var stun = _abilities.All.First(a => a != null && a.Job == AbilityJob.Stun);
            string radiusField = stun.AimRadiusField;
            string cooldownField = stun.CooldownFields[0];
            float newRadius = stun.AimRadius + 1f;
            float newCooldown = stun.Cooldown + 1f;
            var verdict = _book.Evaluate(
                $"{{\"schema\":1,\"assets\":{{\"{stun.name}\":{{\"{cooldownField}\":{Num(newCooldown)},\"{radiusField}\":{Num(newRadius)}}}}}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);

            var before = MatchKits.All(Inputs());
            var after = MatchKits.All(Inputs(verdict));

            int seen = 0;
            for (int i = 0; i < before.Length; i++)
                for (int slot = 0; slot < before[i].Abilities.Length; slot++)
                {
                    if (before[i].Abilities[slot].Asset != stun.name) continue;
                    seen++;
                    var b = before[i].Abilities[slot];
                    var a = after[i].Abilities[slot];
                    Assert.AreEqual(newRadius, a.Radius, 1e-4f, before[i].Label);
                    Assert.AreNotEqual(b.Radius, a.Radius, before[i].Label);
                    Assert.AreEqual(PassiveOf(before[i]).ModifyCooldown(newCooldown, stun, null), a.Cooldown, 1e-4f, before[i].Label);
                    Assert.AreNotEqual(b.Cooldown, a.Cooldown, before[i].Label);
                }
            Assert.Greater(seen, 0, "no shipped kit holds the Stun ability: the test would prove nothing");
        }

        [Test]
        public void Preview_CopiesAreDestroyed()
        {
            var stun = _abilities.All.First(a => a != null && a.Job == AbilityJob.Stun);
            var verdict = _book.Evaluate($"{{\"schema\":1,\"assets\":{{\"{stun.name}\":{{\"{stun.CooldownFields[0]}\":{Num(stun.Cooldown + 1f)}}}}}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            int Count() => Resources.FindObjectsOfTypeAll<ScriptableObject>().Length;
            int before = Count();

            MatchKits.All(Inputs(verdict));
            MatchKits.Rules(Inputs(verdict), 1f, 1f);

            Assert.AreEqual(before, Count(), "MatchKits left Preview copies behind");
        }

        [Test]
        public void UnknownJob_Throws_NamingKitAndJob()
        {
            // The Bank ability is not in the shipped registry, so no verdict can put it in a kit: build a registry whose first legal
            // swap holds it, from a clone of a shipped passive. Nothing shipped is touched.
            var bank = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).FirstOrDefault(a => a.Job == AbilityJob.Bank);
            Assert.IsNotNull(bank, "no Bank-job ability exists under the abilities folder: the test has nothing to feed the builder");
            PassiveAbilitySO clone = null;
            AbilityRegistrySO registry = null;
            try
            {
                foreach (var shipped in _abilities.Passives)
                {
                    for (int slot = 0; slot < shipped.Kit.Length && clone == null; slot++)
                    {
                        var candidate = UnityEngine.Object.Instantiate(shipped);
                        candidate.Kit = (AbilityBaseSO[])shipped.Kit.Clone();
                        candidate.Kit[slot] = bank;
                        if (FixedKit.Problems(candidate).Count == 0) clone = candidate;
                        else UnityEngine.Object.DestroyImmediate(candidate);
                    }
                    if (clone != null)
                    {
                        registry = ScriptableObject.CreateInstance<AbilityRegistrySO>();
                        registry.All = _abilities.All.Select(a => a == shipped ? clone : a).ToArray();
                        break;
                    }
                }
                Assert.IsNotNull(clone, "no passive can hold the Bank ability in a legal kit: the test has nothing to feed the builder");
                var inputs = Inputs();
                inputs.Abilities = registry;

                var ex = Assert.Throws<InvalidOperationException>(() => MatchKits.All(inputs));

                StringAssert.Contains(clone.Subclass.ToString(), ex.Message);
                StringAssert.Contains(nameof(AbilityJob.Bank), ex.Message);
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (registry != null) UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        private static TestHookPassive Synthetic(Action<TestHookPassive> set)
        {
            var passive = ScriptableObject.CreateInstance<TestHookPassive>();
            passive.name = "SyntheticPassive";
            set(passive);
            return passive;
        }

        [Test]
        public void SyntheticPassive_ChangesOnlyItsQuantity()
        {
            const float k = 0.37f;
            var passive = Synthetic(p => p.Knockback = s => s * k);
            try
            {
                var read = MatchKits.ReadPassive(passive, new[] { 0.5f });

                Assert.AreEqual(k, read.KnockbackFactor, 1e-5f);
                Assert.AreEqual(1f, read.StunInFactor);
                Assert.AreEqual(1f, read.SlowInDurationFactor);
                Assert.AreEqual(1f, read.OutgoingControlFactor);
                Assert.AreEqual(1f, read.StealFactor);
                Assert.AreEqual(1f, read.MaxStealMultiplier);
                Assert.AreEqual(0f, read.MatchEndBonusFood);
                Assert.AreEqual(0.5f, read.ReceivedSlowFactor(0.5f), "a passive with no slow hook leaves the factor as it came");
                Assert.Throws<InvalidOperationException>(() => read.ReceivedSlowFactor(0.25f), "a factor no kit emits was never read");
            }
            finally { UnityEngine.Object.DestroyImmediate(passive); }
        }

        [Test]
        public void NonLinearHook_Throws_NamingThePassive()
        {
            var passive = Synthetic(p => p.Control = (s, kind) => kind == ControlKind.Stun ? Mathf.Min(s, 1.5f) : s);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => MatchKits.ReadPassive(passive, new float[0]));

                StringAssert.Contains(passive.name, ex.Message);
                StringAssert.Contains("ModifyControlDuration", ex.Message);
            }
            finally { UnityEngine.Object.DestroyImmediate(passive); }
        }

        [Test]
        public void SlowFactors_AreTheOnesTheKitsEmit_AndEachPassiveAnswersThem()
        {
            var kits = MatchKits.All(Inputs());
            foreach (var kit in kits)
            {
                Assert.IsNotEmpty(kit.Passive.SlowFactorsEmitted, kit.Label);
                foreach (float emitted in kit.Passive.SlowFactorsEmitted)
                    Assert.AreEqual(PassiveOf(kit).ModifyIncomingSlowFactor(emitted, null), kit.Passive.ReceivedSlowFactor(emitted), kit.Label);
            }
            var emittedByAbilities = kits.SelectMany(k => k.Abilities)
                .SelectMany(a => new[] { a.Control.Kind == ControlKind.Slow ? a.Control.SlowFactor : 1f, a.AreaAura ? a.AreaSlowFactor : 1f })
                .Where(f => f < 1f).Distinct().OrderBy(f => f);
            CollectionAssert.AreEqual(emittedByAbilities, kits[0].Passive.SlowFactorsEmitted.OrderBy(f => f));
        }
    }
}
