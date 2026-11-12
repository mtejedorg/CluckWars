using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A03 (architecture audit 2026-11-12): a chicken's loadout is replicated as registry codes, so every peer resolves
    /// the same assets. The code mapping is asserted against the shipped registry; the wiring by shape (no runner here).
    /// </summary>
    public sealed class LoadoutReplicationTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";

        private static AbilityRegistrySO Registry() => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        [Test]
        public void EveryRegisteredAbilityAndPassive_RoundTripsThroughItsCode()
        {
            var registry = Registry();
            Assert.IsNotEmpty(registry.All);
            int passives = 0;
            for (int i = 0; i < registry.All.Length; i++)
            {
                var ability = registry.All[i];
                Assert.IsNotNull(ability, $"AbilityRegistry.All[{i}] is empty: every code after it would still resolve, but the hole is an authoring bug.");
                byte code = registry.CodeOf(ability);
                Assert.AreNotEqual(AbilityRegistrySO.NoAbilityCode, code, $"{ability.name} has no code");
                Assert.AreSame(ability, registry.FromCode(code), $"{ability.name}: code {code} resolves to another asset");
                Assert.AreEqual(code, registry.CodeOf(registry.FromCode(code)), $"{ability.name}: index -> asset -> index moved");
                Assert.AreEqual(i + 1, code, $"{ability.name}: the code is the registry position + 1 (0 is 'none')");
                if (ability is PassiveAbilitySO) passives++;
            }
            Assert.Greater(passives, 0, "passives live in the same array and share the code space");
        }

        [Test]
        public void TheRegistry_HasNoDuplicate_AndFitsAByte()
        {
            var all = Registry().All;
            Assert.AreEqual(all.Length, all.Distinct().Count(),
                "an asset listed twice has two codes; peers would still agree, but CodeOf(FromCode(code)) would not be stable.");
            Assert.LessOrEqual(all.Length, AbilityRegistrySO.MaxCodedAbilities, "the replicated loadout code is one byte");
        }

        [Test]
        public void None_Null_AndUnknown_AreTheSameAnswerOnEveryPeer()
        {
            var registry = Registry();
            Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, registry.CodeOf(null));
            Assert.IsNull(registry.FromCode(AbilityRegistrySO.NoAbilityCode));
            Assert.IsNull(registry.FromCode((byte)(registry.All.Length + 1)), "one past the end");
            Assert.IsNull(registry.FromCode(byte.MaxValue));

            var stranger = ScriptableObject.CreateInstance<PeckAbilitySO>();
            try { Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, registry.CodeOf(stranger), "an unregistered asset cannot be replicated"); }
            finally { Object.DestroyImmediate(stranger); }

            var empty = ScriptableObject.CreateInstance<AbilityRegistrySO>();
            try
            {
                Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, empty.CodeOf(registry.All[0]), "an unpopulated registry codes nothing");
                Assert.IsNull(empty.FromCode(1));
            }
            finally { Object.DestroyImmediate(empty); }
        }

        [Test]
        public void RetiredAbilityAssets_NotInTheRegistry_CodeToNone()
        {
            var registry = Registry();
            var registered = new HashSet<AbilityBaseSO>(registry.All);
            var retired = TestAssets.LoadAllIn<ScriptableObject>(TestAssets.AbilitiesDir).OfType<AbilityBaseSO>()
                .Where(a => !registered.Contains(a)).ToList();
            foreach (var a in retired)
                Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, registry.CodeOf(a), a.name);
        }

        [TestCase(TestAssets.ChickenPrefabPath)]
        [TestCase(TestAssets.DoppelgangerPrefabPath)]
        public void PrefabDefaults_AreAllRegistered_SoEveryPeerCanBeToldAboutThem(string prefabPath)
        {
            var registry = Registry();
            var abilities = TestAssets.Load<GameObject>(prefabPath).GetComponent<AbilityController>();
            if (abilities == null) Assert.Pass($"{prefabPath} has no AbilityController.");
            for (int slot = 0; slot < AbilityController.SlotCount; slot++)
            {
                var ability = abilities.GetSlot(slot);
                if (ability != null)
                    Assert.AreNotEqual(AbilityRegistrySO.NoAbilityCode, registry.CodeOf(ability),
                        $"{prefabPath} slot {slot} holds {ability.name}, which is not in AbilityRegistry.All: it cannot be replicated.");
            }
            if (abilities.Passive != null)
                Assert.AreNotEqual(AbilityRegistrySO.NoAbilityCode, registry.CodeOf(abilities.Passive), abilities.Passive.name);
        }

        [Test]
        public void TheLoadout_IsNetworkedState_PublishedByTheAuthority_AppliedByEveryoneElse()
        {
            string src = File.ReadAllText(ControllerPath).Replace("\r\n", "\n");
            StringAssert.Contains("[Networked, Capacity(SlotCount + 1)]", src, "four slots and the passive, as registry codes");

            string set = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public void SetSlots(PassiveAbilitySO passive");
            StringAssert.Contains("PublishLoadout();", set, "onBeforeSpawned and a live swap both replicate the choice");

            string spawned = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public override void Spawned()");
            int apply = spawned.IndexOf("ApplyReplicatedLoadout();", System.StringComparison.Ordinal);
            int publish = spawned.IndexOf("PublishLoadout();", System.StringComparison.Ordinal);
            int passive = spawned.IndexOf("_passive.OnActivate(_ctx)", System.StringComparison.Ordinal);
            Assert.Greater(apply, 0, "a proxy resolves the replicated loadout in Spawned");
            Assert.Greater(publish, 0, "the authority publishes in Spawned too (a spawn that never went through SetSlots)");
            Assert.Less(apply, passive, "the passive that activates is the replicated one, not the prefab default");
            Assert.Less(publish, passive);

            string render = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public override void Render()");
            StringAssert.Contains("ApplyReplicatedLoadout();", render, "a live swap reaches the proxies");

            string publishBody = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void PublishLoadout()");
            StringAssert.Contains("HasStateAuthority", publishBody, "only the state authority writes the codes");
        }

        [TestCase("Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs", 2)]
        [TestCase("Assets/_Game/Scripts/AbilityLab/AbilityLabBootstrapper.cs", 2)]
        public void EverySpawner_GoesThroughSetSlots(string path, int expected)
        {
            int calls = File.ReadAllLines(path).Count(l => !l.TrimStart().StartsWith("//") && l.Contains("SetSlots("));
            Assert.AreEqual(expected, calls, $"{path}: players, bots and the lab all hand their loadout to AbilityController.SetSlots, the one place that replicates it.");
        }
    }
}
