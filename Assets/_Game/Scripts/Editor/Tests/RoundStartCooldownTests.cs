using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>Death Mark opens every round on a cooldown counted from GO, the first round and every PLAY AGAIN alike.</summary>
    public class RoundStartCooldownTests
    {
        private const float Tick = 1f / 32f;   // the project's simulation rate

        /// <summary>
        /// One round as the authority sees it: the slot's cooldown timer (an expiry tick, -1 = none) is cleared by the reset,
        /// then every intro tick applies the rule exactly as <c>AbilityController.ApplyRoundStartCooldowns</c> does. Returns
        /// the seconds left on the slot at GO.
        /// </summary>
        private static float SecondsLeftAtGo(int introStartTick, int introTicks, float seconds, ref int slotExpiry)
        {
            slotExpiry = -1;   // EndAllForReset (PLAY AGAIN) / a fresh spawn (first round): every slot ready
            int introEnd = introStartTick + introTicks;
            for (int now = introStartTick; now <= introEnd + 64; now++)
            {
                int expiry = RoundStartCooldownRules.ExpiryTick(true, introEnd, now, seconds, Tick);
                if (expiry >= 0 && slotExpiry != expiry) slotExpiry = expiry;
            }
            return (slotExpiry - introEnd) * Tick;
        }

        private static MarkKillAbilitySO MarkKill =>
            TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");

        [Test]
        public void MarkKillAsset_OpensEachRoundOnFourSeconds()
        {
            Assert.AreEqual(4f, MarkKill.RoundStartCooldownSeconds);
            Assert.AreEqual(4f, MarkKill.RoundStartCooldown, "AbilityController reads the virtual, so it must forward the field");
        }

        [Test]
        public void NoOtherAbility_HasARoundStartCooldown()
        {
            var others = AssetDatabase.FindAssets("t:AbilityBaseSO", new[] { TestAssets.AbilitiesDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AbilityBaseSO>)
                .Where(a => a != null && !(a is MarkKillAbilitySO));
            foreach (var a in others) Assert.AreEqual(0f, a.RoundStartCooldown, a.name);
        }

        [Test]
        public void FirstRound_And_PlayAgain_BothOpenOnTheAssetsCooldownAtGo()
        {
            float seconds = MarkKill.RoundStartCooldownSeconds;
            int slot = -1;
            float first = SecondsLeftAtGo(introStartTick: 200, introTicks: 96, seconds, ref slot);
            Assert.AreEqual(4f, first, Tick, "first round");

            slot = 12345;   // a leftover cooldown from round 1, wiped by the reset before the new intro
            float again = SecondsLeftAtGo(introStartTick: 5000, introTicks: 96, seconds, ref slot);
            Assert.AreEqual(4f, again, Tick, "PLAY AGAIN");
        }

        [Test]
        public void AfterGo_NothingIsReapplied()
        {
            Assert.AreEqual(-1, RoundStartCooldownRules.ExpiryTick(true, 300, 301, 4f, Tick), "late join / authority migration");
            Assert.AreEqual(300 + 128, RoundStartCooldownRules.ExpiryTick(true, 300, 300, 4f, Tick), "the GO tick itself still applies");
        }

        [Test]
        public void NoIntro_OrNoCooldown_AppliesNothing()
        {
            Assert.AreEqual(-1, RoundStartCooldownRules.ExpiryTick(false, 300, 100, 4f, Tick));
            Assert.AreEqual(-1, RoundStartCooldownRules.ExpiryTick(true, 300, 100, 0f, Tick));
        }

        [Test]
        public void TheController_AppliesItBeforeTheMatchRunningGate_SourceGuard()
        {
            var src = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int apply = src.IndexOf("ApplyRoundStartCooldowns(gm);", System.StringComparison.Ordinal);
            int gate = src.IndexOf("if (gm == null || !gm.IsMatchRunning)", System.StringComparison.Ordinal);
            Assert.IsTrue(apply > 0 && gate > apply, "the intro is not 'match running': the apply must come before that early return");
        }
    }
}
