using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A03 follow-up: a replicated loadout is its owner's claim, so every peer judges it before using it
    /// (<see cref="LoadoutClaimRules"/>). Asserted against the shipped registry and kits.
    /// </summary>
    public sealed class LoadoutClaimRulesTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";

        private static readonly ChickenClass[] Classes =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private static AbilityRegistrySO Registry() => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        private static AbilityBaseSO PlainPeck(AbilityRegistrySO r) => r.All.FirstOrDefault(a => a is PeckAbilitySO);

        [Test]
        public void APassive_IsLegalOnlyForTheClassesItAllows()
        {
            var registry = Registry();
            int refused = 0;
            foreach (var passive in registry.Passives)
                foreach (var cls in Classes)
                {
                    bool allowed = (passive.AllowedClasses & AbilityRegistrySO.FlagOf(cls)) != 0;
                    Assert.AreEqual(allowed, LoadoutClaimRules.IsLegalPassive(passive, cls), $"{passive.name} on {cls}");
                    if (!allowed) refused++;
                }
            Assert.Greater(refused, 0, "the shipped roster has class-bound passives; the rule must refuse some pairing");
            Assert.IsFalse(LoadoutClaimRules.IsLegalPassive(null, ChickenClass.Warrior));
        }

        [Test]
        public void FixedMode_OnlyTheKitsAbility_InItsOwnSlot()
        {
            var registry = Registry();
            var peck = PlainPeck(registry);
            int kits = 0;
            foreach (var passive in registry.Passives)
            {
                if (!FixedKit.TryResolve(LoadoutMode.Fixed, passive, out var kit, out _)) continue;
                Assert.IsTrue(PreEquippedLoadout.TryGetSingleClass(passive.AllowedClasses, out var cls), passive.name);
                kits++;
                for (int slot = 0; slot < kit.Length; slot++)
                {
                    Assert.IsTrue(LoadoutClaimRules.IsLegalSlot(kit[slot], slot, cls, passive, LoadoutMode.Fixed, registry.All, peck),
                        $"{passive.name}: its own kit ability {kit[slot].name} was refused in slot {slot} - every honest spawn would be rejected.");
                    Assert.AreSame(kit[slot], LoadoutClaimRules.SafeSlot(slot, passive, LoadoutMode.Fixed));

                    int other = (slot + 1) % kit.Length;
                    if (kit[other] != kit[slot])
                        Assert.IsFalse(LoadoutClaimRules.IsLegalSlot(kit[slot], other, cls, passive, LoadoutMode.Fixed, registry.All, peck),
                            $"{passive.name}: {kit[slot].name} accepted in slot {other}, which the kit gives to {kit[other].name}.");
                }

                var outsider = registry.ActiveAbilities.FirstOrDefault(a => !kit.Contains(a));
                if (outsider != null)
                    for (int slot = 0; slot < kit.Length; slot++)
                        Assert.IsFalse(LoadoutClaimRules.IsLegalSlot(outsider, slot, cls, passive, LoadoutMode.Fixed, registry.All, peck),
                            $"{passive.name}: {outsider.name} is not in its kit but was accepted in slot {slot}.");
            }
            Assert.Greater(kits, 0, "no shipped passive has a usable fixed kit: the test went blind");
        }

        [Test]
        public void FreeRules_ClassPool_PlusThePassivesPreEquips_AndNothingElse()
        {
            var registry = Registry();
            var peck = PlainPeck(registry);
            int refusedOffClass = 0;
            foreach (var passive in registry.Passives)
            {
                if (!PreEquippedLoadout.TryGetSingleClass(passive.AllowedClasses, out var cls)) continue;
                PreEquippedLoadout.Resolve(cls, passive, registry.All, peck, out var peckSlot, out var signature);

                foreach (var ability in registry.ActiveAbilities)
                {
                    bool expected = ability == peckSlot || ability == signature ||
                                    (!(ability is PeckAbilitySO) && AbilityRegistrySO.IsAllowedFor(ability, cls));
                    Assert.AreEqual(expected, LoadoutClaimRules.IsLegalSlot(ability, 2, cls, passive, LoadoutMode.Free, registry.All, peck),
                        $"{ability.name} on a {cls} with {passive.name}");
                    if (!expected) refusedOffClass++;
                }

                Assert.IsFalse(LoadoutClaimRules.IsLegalSlot(passive, 0, cls, passive, LoadoutMode.Free, registry.All, peck), "a passive is not a slot ability");
                Assert.IsFalse(LoadoutClaimRules.IsLegalSlot(null, 0, cls, passive, LoadoutMode.Free, registry.All, peck));
                Assert.IsNull(LoadoutClaimRules.SafeSlot(0, passive, LoadoutMode.Free), "Free mode has no kit to fall back to: keep what the peer holds");
            }
            Assert.Greater(refusedOffClass, 0, "the shipped pool has class-bound abilities; the rule must refuse some of them");
        }

        [Test]
        public void TheAssassin_IsNeverShownForaging()
        {
            var registry = Registry();
            var peck = PlainPeck(registry);
            Assert.IsNotNull(peck);
            foreach (var passive in registry.GetPassivesForClass(ChickenClass.Assassin))
                Assert.IsFalse(LoadoutClaimRules.IsLegalSlot(peck, 0, ChickenClass.Assassin, passive, LoadoutMode.Free, registry.All, peck),
                    $"{passive.name}: Peck accepted on an Assassin, which cannot forage.");
        }

        [Test]
        public void AbilityController_JudgesEveryResolvedEntry_BeforeItUsesIt()
        {
            string apply = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void ApplyReplicatedLoadout()");
            int passiveGate = apply.IndexOf("LoadoutClaimRules.IsLegalPassive(passive, cls)", System.StringComparison.Ordinal);
            int passiveWrite = apply.IndexOf("_passive = passive", System.StringComparison.Ordinal);
            int slotGate = apply.IndexOf("LoadoutClaimRules.IsLegalSlot(ability, i, cls, _passive, mode", System.StringComparison.Ordinal);
            // The slot fields are written only through AbilityController.SetSlot (AbilitySlotFanOutTests);
            // the call is the write, and it must still sit after the gate.
            int slotWrite = apply.IndexOf("SetSlot(i, ability)", System.StringComparison.Ordinal);
            Assert.Greater(passiveGate, 0);
            Assert.Greater(passiveWrite, passiveGate, "the passive is judged before it is applied");
            Assert.Greater(slotGate, passiveWrite, "the slots are judged after, and against, the passive");
            Assert.Greater(slotWrite, slotGate);
            StringAssert.Contains("wrongKind", apply, "the wrong-kind check stays");
            StringAssert.Contains("GetDefaultPassiveForClass(cls)", apply, "a refused passive falls back to the class default");
            StringAssert.Contains("LoadoutClaimRules.SafeSlot(", apply);

            string warn = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void WarnRefusedLoadout(");
            StringAssert.Contains("LoadoutRejectionLimiter.Allow(", warn, "refusals are rate-limited");
            StringAssert.Contains("_log?.Warn(", warn);
        }
    }
}
