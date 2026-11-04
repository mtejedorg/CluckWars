using System.IO;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// An Assassin's execute pays into the capacity-exempt bounty bag, never the beak. Every steal read the beak only, so an
    /// Assassin carrying a full execute bounty looked empty-handed to Snatch, Sneaky Steal, Scrap, Roll Trample, the bots and the
    /// victim-side drain. What a thief can take is cargo plus bag, drained cargo first.
    /// </summary>
    public class StealFromAssassinTests
    {
        [TestCase(0f, 6f, 6f)]
        [TestCase(3f, 4f, 7f)]
        [TestCase(5f, 0f, 5f)]
        [TestCase(0f, 0f, 0f)]
        [TestCase(-1f, -2f, 0f)]
        public void WhatAThiefCanTake_IsTheCargoPlusTheBountyBag(float cargo, float bag, float expected) =>
            Assert.AreEqual(expected, StealRules.Stealable(cargo, bag), 1e-5f);

        [Test]
        public void ABountyBagOnly_Assassin_IsStealable_AndTheDrainComesOutOfTheBag()
        {
            Assert.Greater(StealRules.Stealable(0f, 6f), 0f, "the filter every steal ability asks");
            StealRules.SplitDrain(2f, 0f, 6f, out float fromCargo, out float fromBag);
            Assert.AreEqual(0f, fromCargo, 1e-5f);
            Assert.AreEqual(2f, fromBag, 1e-5f);
        }

        [TestCase(2f, 5f, 4f, 2f, 0f)]    // the beak first
        [TestCase(7f, 5f, 4f, 5f, 2f)]    // then the bag
        [TestCase(99f, 5f, 4f, 5f, 4f)]   // never more than there is
        [TestCase(0f, 5f, 4f, 0f, 0f)]
        [TestCase(-3f, 5f, 4f, 0f, 0f)]
        [TestCase(3f, 0f, 0f, 0f, 0f)]
        public void SplitDrain_TakesTheBeakFirst_ThenTheBag_AndConservesFood(float amount, float cargo, float bag, float expCargo, float expBag)
        {
            StealRules.SplitDrain(amount, cargo, bag, out float fromCargo, out float fromBag);
            Assert.AreEqual(expCargo, fromCargo, 1e-5f);
            Assert.AreEqual(expBag, fromBag, 1e-5f);
            Assert.LessOrEqual(fromCargo + fromBag, StealRules.Stealable(cargo, bag) + 1e-5f);
        }

        private static string Src(string rel) => File.ReadAllText("Assets/_Game/Scripts/" + rel).Replace("\r\n", "\n");

        [TestCase("Abilities/SnatchAbilitySO.cs")]
        [TestCase("Abilities/SneakyStealAbilitySO.cs")]
        [TestCase("Abilities/ScrapAbilitySO.cs")]
        [TestCase("Abilities/RollTrampleAbilitySO.cs")]
        public void EveryStealAbility_AsksWhatIsStealable_NotTheBeakAlone_SourceGuard(string rel)
        {
            var src = Src(rel);
            StringAssert.Contains("Stealable", src);
            StringAssert.DoesNotContain("cargo.Cargo > 0f", src, "the target filter must see the bounty bag");
            StringAssert.DoesNotContain("targetCargo.Cargo)", src);
            StringAssert.DoesNotContain("victim.Cargo,", src);
            StringAssert.DoesNotContain("target.Cargo,", src);
        }

        [Test]
        public void TheVictimSideDrain_AndTheBotsAndSpineCoat_ReadTheSameRule_SourceGuard()
        {
            var cargo = Src("Gameplay/ChickenCargo.cs");
            int drain = cargo.IndexOf("public void RPC_DrainStolen", System.StringComparison.Ordinal);
            string body = cargo.Substring(drain, cargo.IndexOf("StealEventId++", drain, System.StringComparison.Ordinal) - drain);
            StringAssert.Contains("StealRules.SplitDrain", body);
            StringAssert.DoesNotContain("Cargo <= 0f", body, "an Assassin holding only a bounty bag is not an empty load");
            StringAssert.Contains("StealableFraction", Src("Gameplay/BotController.cs"));
            StringAssert.Contains("Stealable", Src("Gameplay/ChickenController.cs"));
        }
    }
}
