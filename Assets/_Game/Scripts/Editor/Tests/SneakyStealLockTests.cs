using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>Sneaky Steal's per-victim cooldown.</summary>
    public class SneakyStealLockTests
    {
        [Test]
        public void ALockedVictim_IsRefused_UntilItsCooldownEnds_AndOthersStayFree()
        {
            var locks = new TargetCooldowns<int>();
            locks.Lock(1, 100.0, 110.0);
            Assert.IsTrue(locks.Blocks(1, 109.9));
            Assert.IsFalse(locks.Blocks(1, 110.0));
            Assert.IsFalse(locks.Blocks(2, 100.0));
        }

        [Test]
        public void TheLock_StartsAfterTheHitConfirmation_ThatRederivesTheTargets()
        {
            var locks = new TargetCooldowns<int>();
            locks.Lock(1, 100.5, 110.0);
            Assert.IsFalse(locks.Blocks(1, 100.2), "HitFeedback.ConfirmHits still sees the victim it just robbed");
            Assert.IsTrue(locks.Blocks(1, 100.5));

            var steal = TestAssets.Load<SneakyStealAbilitySO>(TestAssets.AbilitiesDir + "/SneakySteal.asset");
            Assert.Less(ChickenController.StealLockFeedbackGrace, steal.Cooldown, "a second cast must meet the lock");
        }

        [Test]
        public void ANewLock_ReplacesTheOld_AndANewRoundClearsThem()
        {
            var locks = new TargetCooldowns<int>();
            locks.Lock(1, 100.0, 110.0);
            locks.Lock(1, 100.0, 120.0);
            Assert.IsTrue(locks.Blocks(1, 115.0));
            locks.Clear();
            Assert.IsFalse(locks.Blocks(1, 115.0));
        }

        [Test]
        public void ShippedSneakySteal_Locks10Seconds_ThroughTheSharedFilter()
        {
            Assert.AreEqual(10f, TestAssets.Load<SneakyStealAbilitySO>(TestAssets.AbilitiesDir + "/SneakySteal.asset").SameVictimCooldownSeconds);
            var src = File.ReadAllText("Assets/_Game/Scripts/Abilities/SneakyStealAbilitySO.cs");
            StringAssert.Contains("!caster.IsStealLockedOn(candidate)", src);
            StringAssert.Contains("thief.LockStealOn(", src);
            StringAssert.Contains("_stealLocks.Clear()", File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs"));
        }
    }
}
