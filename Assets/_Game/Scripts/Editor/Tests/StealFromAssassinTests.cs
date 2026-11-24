using System.IO;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// An Assassin's execute pays into his ordinary cargo (Maestro, 2026-11-24: the capacity-free bounty bag is gone), so there is
    /// one store of food on a chicken and every steal reads it through the one rule, <see cref="StealRules.Stealable"/>. A loaded
    /// Spoiler is stealable exactly like any loaded chicken; every steal ability and the victim-side drain ask the same question.
    /// </summary>
    public class StealFromAssassinTests
    {
        [TestCase(2f, 5f, 2f)]     // what was asked
        [TestCase(99f, 5f, 5f)]    // never more than there is
        [TestCase(0f, 5f, 0f)]
        [TestCase(-3f, 5f, 0f)]
        [TestCase(3f, 0f, 0f)]
        public void TheDrain_IsWhatWasAsked_NeverMoreThanTheVictimHolds(float amount, float cargo, float expected) =>
            Assert.AreEqual(expected, StealRules.DrainAmount(amount, cargo), 1e-5f);

        [TestCase(6f, 6f)]
        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        public void WhatAThiefCanTake_IsTheCargo(float cargo, float expected) =>
            Assert.AreEqual(expected, StealRules.Stealable(cargo), 1e-5f);

        [Test]
        public void ThereIsNoSecondStoreOfFood_TheCargoIsTheOnlyOne_SourceGuard()
        {
            foreach (string dir in new[] { "Assets/_Game/Scripts/Gameplay", "Assets/_Game/Scripts/Balance", "Assets/_Game/Scripts/UI", "Assets/_Game/Scripts/Abilities" })
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                    StringAssert.DoesNotContain("BountyBag", File.ReadAllText(file),
                        $"{file} names the bounty bag again: a kill's loot is ordinary cargo held to the capacity, nothing is stored beyond it.");
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
            StringAssert.DoesNotContain("cargo.Cargo > 0f", src, "the target filter asks the one rule, not the raw property");
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
            StringAssert.Contains("float actual = StealRules.DrainAmount(amount, Cargo);", body, "the drain takes what was asked, never more than the victim holds");
            StringAssert.Contains("Stealable <= 0f", body, "an empty load is read through the one rule");
            StringAssert.DoesNotContain("Cargo <= 0f", body);
            StringAssert.Contains("StealableFraction", Src("Gameplay/BotController.cs"));
            // A20: the Spine Coat contact steals through the one entry point, which clamps to what is stealable.
            StringAssert.Contains("Cargo.StealFrom(other, StealBackAmount)", Src("Gameplay/ChickenController.cs"));
            StringAssert.Contains("victimCargo.Stealable", AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs", "public float StealFrom("));
        }
    }
}
