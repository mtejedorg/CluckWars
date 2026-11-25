using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>When a Brawler bot may chase a rival (BL-224): pure functions over facts, and the pin that no shipped kit changes.</summary>
    public sealed class BotEngageRulesTests
    {
        private static readonly HitStealFacts NoHit = new HitStealFacts { LockSeconds = 3f };

        [Test]
        public void MayEngage_OnlyAClosedGate_ForABrawlerWithNothingToTake()
        {
            Assert.IsFalse(BotEngageRules.MayEngage(true, false, false), "a Brawler with no Steal and no paying hit forages");
            Assert.IsTrue(BotEngageRules.MayEngage(true, true, false), "a Steal opens it");
            Assert.IsTrue(BotEngageRules.MayEngage(true, false, true), "so does a hit that pays");
            Assert.IsTrue(BotEngageRules.MayEngage(false, false, false), "every other strategy keeps its chase");
        }

        [Test]
        public void HitPays_NeedsAnAmountOnAHeldHitter_OrABiteWithFood()
        {
            var stun = new HitStealFacts { Stun = 2f, LockSeconds = 3f };
            Assert.IsTrue(BotEngageRules.HitPays(stun, BiteFacts.None, true, false, false, false));
            Assert.IsFalse(BotEngageRules.HitPays(stun, BiteFacts.None, false, true, false, false), "the amount is on a Stun the kit does not hold");
            Assert.IsFalse(BotEngageRules.HitPays(NoHit, BiteFacts.None, true, true, true, false), "shipped: every amount 0");
            Assert.IsTrue(BotEngageRules.HitPays(new HitStealFacts { Slow = 1f }, BiteFacts.None, false, false, true, false));

            var stackOnly = new BiteFacts { FreshSeconds = 1f, FoodShare = 0f };
            Assert.IsFalse(BotEngageRules.HitPays(NoHit, stackOnly, true, true, false, false), "a fresh stack alone is no food");
            var food = new BiteFacts { FreshSeconds = 1f, FoodShare = 1f };
            Assert.IsTrue(BotEngageRules.HitPays(NoHit, food, true, false, false, false));
            Assert.IsFalse(BotEngageRules.HitPays(NoHit, new BiteFacts { FreshSeconds = 0f, FoodShare = 1f }, true, true, false, true), "no fresh window, no bite");
        }

        [Test]
        public void ShippedWarriorKits_BothHoldSteal_SoNoShippedBehaviourChanges()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var warriors = registry.Passives.Where(p => p.AllowedClasses == ChickenClassFlags.Warrior).ToList();
            Assert.AreEqual(2, warriors.Count, "Bully and Relentless");
            foreach (var passive in warriors)
            {
                Assert.IsTrue(FixedKit.TryResolve(LoadoutMode.Fixed, passive, out var kit, out var problem), problem);
                Assert.IsTrue(kit.Any(a => a != null && a.Job == AbilityJob.Steal), passive.name + " holds Steal");
                Assert.IsTrue(BotEngageRules.MayEngage(true, true, false), passive.name + ": the gate stays open");
            }
        }
    }
}
