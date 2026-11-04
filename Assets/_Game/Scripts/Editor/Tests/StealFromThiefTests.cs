using System;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-04: Thieves can be stolen from too. A Thief (Assassin_Thief) fills his beak with Snatch / Sneaky Steal /
    /// Scrap / Trample loot; nothing about the class, the passive, stealth or the bot scan may take him out of the steal rules.
    /// </summary>
    public class StealFromThiefTests
    {
        private static string Src(string rel) => File.ReadAllText("Assets/_Game/Scripts/" + rel).Replace("\r\n", "\n");

        private static string Between(string src, string from, string to)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(a, 0, from);
            int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            Assert.Greater(b, a, to);
            return src.Substring(a, b - a);
        }

        private static readonly string[] ClassOrStealthGates =
            { "ChickenClass", ".Class", "Subclass", "Passive", "IsStealth", "Invisib", "Alpha", "IsDecoy", "Thief" };

        [Test]
        public void TheThiefPassive_ChangesNeitherCapacityNorStealability()
        {
            var thief = TestAssets.Load<ThiefPassiveSO>(TestAssets.AbilitiesDir + "/Passives/Thief.asset");
            Assert.AreEqual(10f, thief.ModifyCargoCapacity(10f, null), "no capacity bonus or malus");
            var src = Src("Abilities/Passives/ThiefPassiveSO.cs");
            StringAssert.DoesNotContain("StealProof", src);
            StringAssert.DoesNotContain("ModifyCargoCapacity", src);
        }

        [Test]
        public void ALoadedThief_IsStealable_AndTheClampIsTheThiefsFreeSpace_NotTheVictims()
        {
            // The Assassin's capacity is 10; a Thief holding a full beak of stolen food.
            Assert.AreEqual(10f, StealRules.Stealable(10f, 0f));
            Assert.AreEqual(3f, StealMath.Clamp(4.2f, 3f, StealRules.Stealable(10f, 0f)), 1e-5f, "the thief's own free space limits it");
            Assert.AreEqual(4.2f, StealMath.Clamp(4.2f, 10f, StealRules.Stealable(10f, 0f)), 1e-5f);
            Assert.AreEqual(1.5f, StealMath.Clamp(4.2f, 10f, StealRules.Stealable(1.5f, 0f)), 1e-5f, "never more than he holds");
            Assert.AreEqual(0f, StealMath.Clamp(4.2f, 0f, StealRules.Stealable(10f, 0f)), "a full thief steals nothing");
            StealRules.SplitDrain(4.2f, 10f, 0f, out float beak, out float bag);
            Assert.AreEqual(4.2f, beak, 1e-5f);
            Assert.AreEqual(0f, bag);
        }

        [TestCase("Abilities/SnatchAbilitySO.cs")]
        [TestCase("Abilities/SneakyStealAbilitySO.cs")]
        [TestCase("Abilities/ScrapAbilitySO.cs")]
        [TestCase("Abilities/RollTrampleAbilitySO.cs")]
        public void TheStealTargetFilter_HasNoClassPassiveOrStealthGate_SourceGuard(string rel)
        {
            string body = Between(Src(rel), "protected override bool ExtraTargetFilter", "public override void OnActivate");
            foreach (var gate in ClassOrStealthGates) StringAssert.DoesNotContain(gate, body, rel);
            StringAssert.Contains("Stealable", body);
        }

        [Test]
        public void TheSharedEligibilityPredicate_HasNoStealthOrClassGate_SourceGuard()
        {
            string body = Between(Src("Abilities/AbilityBaseSO.cs"), "public bool WouldAffect(Gameplay.ChickenController caster, Gameplay.ChickenController candidate, Vector3 forward)", "public int GatherTargets");
            foreach (var gate in new[] { "ChickenClass", "Subclass", "IsStealth", "Invisib", "Alpha", "IsDecoy" })
                StringAssert.DoesNotContain(gate, body);
        }

        [Test]
        public void TheBotsPreyScan_SkipsOnlyRemovedBirds_AndValuesTheBeak_SourceGuard()
        {
            string scan = Between(Src("Gameplay/BotController.cs"), "if (c.Combat != null && c.Combat.IsRemoved) continue;\n\n                float dist", "if (_nearestRival == null)");
            foreach (var gate in new[] { "ChickenClass", ".Class", "Subclass", "Invisib", "IsStealth", "Alpha" })
                StringAssert.DoesNotContain(gate, scan);
            StringAssert.Contains("StealableFraction", scan);
        }
    }
}
