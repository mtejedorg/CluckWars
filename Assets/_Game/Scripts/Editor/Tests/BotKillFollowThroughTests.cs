using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>A Spoiler bot presses Kill while its own Ambush still runs (the stun IS Ambush's run time); every other ability
    /// keeps the bots' one-at-a-time rule.</summary>
    public class BotKillFollowThroughTests
    {
        [Test]
        public void AnAmbushRunning_DoesNotBlockTheKill_ButBlocksEverythingElse()
        {
            Assert.IsTrue(AbilityController.BotMayPress(anyActive: true, repress: false, ignoresOneAtATime: true), "Kill during Ambush");
            Assert.IsFalse(AbilityController.BotMayPress(true, false, false), "any other ability stays gated");
            Assert.IsTrue(AbilityController.BotMayPress(false, false, false), "nothing running");
            Assert.IsTrue(AbilityController.BotMayPress(true, true, false), "Turtle's lock press");
        }

        [Test]
        public void OnlyDeathMark_IsExempt()
        {
            var mark = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            var ambush = TestAssets.Load<AmbushAbilitySO>(TestAssets.AbilitiesDir + "/Ambush.asset");
            Assert.IsTrue(mark.BotIgnoresOneAtATime);
            Assert.IsFalse(ambush.BotIgnoresOneAtATime);
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
                if (!(a is MarkKillAbilitySO)) Assert.IsFalse(a.BotIgnoresOneAtATime, a.name);
        }

        [Test]
        public void AmbushRunsForItsWholeStun_WhichIsWhyTheExemptionIsNeeded()
        {
            var ambush = TestAssets.Load<AmbushAbilitySO>(TestAssets.AbilitiesDir + "/Ambush.asset");
            Assert.GreaterOrEqual(ambush.Duration, ambush.StunDuration);
        }
    }
}
