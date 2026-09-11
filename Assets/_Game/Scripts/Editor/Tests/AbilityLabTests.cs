using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.AbilityLab;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pins the Ability Lab's loadout rules, which deliberately differ from the match's.
    /// </summary>
    /// <remarks>
    /// <c>MatchBootstrapper.ResolveLegalLoadout</c> force-inserts Peck and the chosen
    /// specialization's signature so a live match can never spawn a chicken unable to
    /// score. The lab must NOT do that — being handed Peck plus a signature in half your
    /// slots when you asked to look at one ability on its own is what makes a test tool
    /// useless. That difference is invisible at a glance and would be trivially "tidied
    /// away" by a future reader reaching for the shared sanitiser, so it is asserted here.
    /// </remarks>
    public sealed class AbilityLabTests
    {
        private static AbilityRegistrySO Registry() =>
            TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        private static AbilityBaseSO Peck(AbilityRegistrySO registry) =>
            registry.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);

        // ---- Legality filtering -------------------------------------------------

        [Test]
        public void FilterActives_HonoursClassLegality_ByDefault()
        {
            var registry = Registry();

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                var filtered = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: false);
                CollectionAssert.IsNotEmpty(filtered, $"No active ability is legal for {cls}.");

                foreach (var ability in filtered)
                {
                    Assert.IsTrue(AbilityRegistrySO.IsAllowedFor(ability, cls),
                        $"FilterActives offered '{ability.name}' to {cls}, which cannot equip it.");
                }
            }
        }

        [Test]
        public void FilterActives_IgnoreLegality_OffersTheWholePool()
        {
            var registry = Registry();
            int total = registry.ActiveAbilities.Count(a => a != null);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                var filtered = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: true);
                Assert.AreEqual(total, filtered.Count,
                    $"ignoreLegality must offer every ability to {cls} — that is the whole point of " +
                    $"the toggle. Got {filtered.Count} of {total}.");
            }
        }

        [Test]
        public void IgnoreLegality_IsASupersetOfTheLegalPool()
        {
            var registry = Registry();

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                var legal = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: false);
                var all = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: true);
                CollectionAssert.IsSubsetOf(legal, all,
                    $"The legality-filtered pool for {cls} must be a subset of the unfiltered one.");
            }
        }

        // ---- Slot resolution ----------------------------------------------------

        [Test]
        public void ResolveSlots_FillsEverySlot_WithDistinctAbilities()
        {
            var registry = Registry();
            var resolved = new AbilityBaseSO[AbilityController.SlotCount];

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                bool full = AbilityLabLoadout.ResolveSlots(registry, cls, false, null, resolved);

                Assert.IsTrue(full, $"Could not fill {AbilityController.SlotCount} slots for {cls}.");
                CollectionAssert.AllItemsAreNotNull(resolved,
                    $"{cls} resolved a null slot. AbilityController.SetSlots ignores nulls, so a null " +
                    "here silently leaves the chicken prefab's own default in that slot.");
                CollectionAssert.AllItemsAreUnique(resolved, $"{cls} got the same ability twice.");
            }
        }

        [Test]
        public void ResolveSlots_KeepsThePicksItIsGiven_InOrder()
        {
            var registry = Registry();
            const ChickenClass cls = ChickenClass.Warrior;

            var pool = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: false);
            Assume.That(pool.Count, Is.GreaterThanOrEqualTo(AbilityController.SlotCount));

            // Pick from the back of the pool so backfill (which walks front-to-back) cannot
            // reproduce this order by accident.
            var picks = new List<AbilityBaseSO>
            {
                pool[pool.Count - 1],
                pool[pool.Count - 2],
            };

            var resolved = new AbilityBaseSO[AbilityController.SlotCount];
            AbilityLabLoadout.ResolveSlots(registry, cls, false, picks, resolved);

            Assert.AreSame(picks[0], resolved[0], "The lab must honour pick order — slot 0 was displaced.");
            Assert.AreSame(picks[1], resolved[1], "The lab must honour pick order — slot 1 was displaced.");
        }

        [Test]
        public void ResolveSlots_DropsIllegalPicks_UnlessLegalityIsIgnored()
        {
            var registry = Registry();
            var peck = Peck(registry);
            Assume.That(peck, Is.Not.Null, "Registry has no Peck ability to test class gating with.");
            Assume.That(AbilityRegistrySO.IsAllowedFor(peck, ChickenClass.Assassin), Is.False,
                "This test relies on the Assassin being unable to forage.");

            var picks = new List<AbilityBaseSO> { peck };
            var resolved = new AbilityBaseSO[AbilityController.SlotCount];

            AbilityLabLoadout.ResolveSlots(registry, ChickenClass.Assassin, false, picks, resolved);
            CollectionAssert.DoesNotContain(resolved, peck,
                "An illegal pick must be dropped while legality is enforced.");

            AbilityLabLoadout.ResolveSlots(registry, ChickenClass.Assassin, true, picks, resolved);
            Assert.AreSame(peck, resolved[0],
                "With ignoreLegality on, the lab must equip the off-class ability that was asked for — " +
                "testing exactly these combinations is why the toggle exists.");
        }

        /// <summary>
        /// The behaviour that separates the lab from the match. If someone ever routes the lab
        /// through <c>ResolveLegalLoadout</c>, this is the test that says why they should not.
        /// </summary>
        [Test]
        public void ResolveSlots_DoesNotForcePeck_WhenThePicksFillEverySlot()
        {
            var registry = Registry();
            var peck = Peck(registry);
            Assume.That(peck, Is.Not.Null);

            const ChickenClass cls = ChickenClass.Warrior;
            Assume.That(AbilityRegistrySO.IsAllowedFor(peck, cls), Is.True,
                "This test relies on the Warrior being a forager, so a match spawn WOULD force Peck.");

            var picks = AbilityLabLoadout.FilterActives(registry, cls, ignoreLegality: false)
                .Where(a => a != peck)
                .Take(AbilityController.SlotCount)
                .ToList();
            Assume.That(picks.Count, Is.EqualTo(AbilityController.SlotCount));

            var resolved = new AbilityBaseSO[AbilityController.SlotCount];
            AbilityLabLoadout.ResolveSlots(registry, cls, false, picks, resolved);

            CollectionAssert.DoesNotContain(resolved, peck,
                "The lab must not force Peck into a fully-specified loadout. The match sanitiser does " +
                "that so a live chicken can always score; a lab that did it would overwrite one of the " +
                "four abilities under test.");
        }

        [Test]
        public void ResolveSlots_RejectsAnUndersizedBuffer()
        {
            var registry = Registry();
            var tooSmall = new AbilityBaseSO[AbilityController.SlotCount - 1];

            Assert.IsFalse(AbilityLabLoadout.ResolveSlots(registry, ChickenClass.Warrior, false, null, tooSmall),
                "A buffer smaller than SlotCount must be refused, not partially filled.");
        }

        // ---- Passive resolution -------------------------------------------------

        [Test]
        public void ResolvePassive_FallsBackToTheClassDefault_WhenThePickIsIllegal()
        {
            var registry = Registry();

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                var offClass = registry.Passives.FirstOrDefault(p => !AbilityRegistrySO.IsAllowedFor(p, cls));
                if (offClass == null) continue; // every passive is legal for this class; nothing to test

                var resolved = AbilityLabLoadout.ResolvePassive(registry, cls, false, offClass);
                Assert.AreNotSame(offClass, resolved,
                    $"{cls} was handed the off-class passive '{offClass.name}' while legality was enforced.");
                Assert.AreSame(registry.GetDefaultPassiveForClass(cls), resolved,
                    $"An illegal passive pick must fall back to {cls}'s default.");

                Assert.AreSame(offClass, AbilityLabLoadout.ResolvePassive(registry, cls, true, offClass),
                    "With ignoreLegality on, the off-class passive must be honoured.");
            }
        }

        [Test]
        public void ResolvePassive_GivesEveryClassAPassive_WhenNoneIsPicked()
        {
            var registry = Registry();

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsNotNull(AbilityLabLoadout.ResolvePassive(registry, cls, false, null),
                    $"{cls} resolved no passive at all. AbilityController.SetSlots ignores nulls, so the " +
                    "lab chicken would silently keep the prefab's passive instead of the class default.");
            }
        }
    }
}
