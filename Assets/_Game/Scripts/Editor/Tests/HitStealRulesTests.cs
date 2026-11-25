using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Bully's hit-steal rules (design 2026-11-25): pure functions over facts. No test asserts a win rate.</summary>
    public sealed class HitStealRulesTests
    {
        [Test]
        public void Take_ClampsToRoomAndVictim()
        {
            Assert.AreEqual(2f, HitStealRules.Take(2f, 10f, 10f, false), 1e-6f, "room and food to spare: the whole amount");
            Assert.AreEqual(1f, HitStealRules.Take(2f, 1f, 10f, false), 1e-6f, "held to the caster's free room");
            Assert.AreEqual(0.5f, HitStealRules.Take(2f, 10f, 0.5f, false), 1e-6f, "held to what the victim holds");
            Assert.AreEqual(0f, HitStealRules.Take(2f, 0f, 10f, false), "a full caster takes nothing");
            Assert.AreEqual(0f, HitStealRules.Take(2f, 10f, 0f, false), "an empty victim gives nothing");
            Assert.AreEqual(StealMath.Clamp(3f, 2f, 5f), HitStealRules.Take(3f, 2f, 5f, false), "the shared steal clamp");
        }

        [Test]
        public void Take_LockedOrZeroAmount_IsZero()
        {
            Assert.AreEqual(0f, HitStealRules.Take(2f, 10f, 10f, true), "locked");
            Assert.AreEqual(0f, HitStealRules.Take(0f, 10f, 10f, false), "off");
            Assert.AreEqual(0f, HitStealRules.Take(-1f, 10f, 10f, false), "never negative");
            Assert.AreEqual(0f, HitStealRules.Take(float.NaN, 10f, 10f, false), "NaN asks for nothing");
        }

        [Test]
        public void Locked_UntilTheLockEnds()
        {
            Assert.IsTrue(HitStealRules.Locked(2.9f, 3f));
            Assert.IsFalse(HitStealRules.Locked(3f, 3f), "the lock ends at its time");
            Assert.IsFalse(HitStealRules.Locked(0f, 0f), "a lock never started is no lock");
        }

        [Test]
        public void AmountFor_PeckJumpAndEveryOtherJob_IsZero()
        {
            var f = new HitStealFacts { Stun = 2f, Shove = 3f, Slow = 1f, LockSeconds = 3f };
            Assert.AreEqual(2f, f.AmountFor(AbilityJob.Stun));
            Assert.AreEqual(3f, f.AmountFor(AbilityJob.Shove));
            Assert.AreEqual(1f, f.AmountFor(AbilityJob.Slow));
            foreach (AbilityJob job in System.Enum.GetValues(typeof(AbilityJob)))
            {
                if (job == AbilityJob.Stun || job == AbilityJob.Shove || job == AbilityJob.Slow) continue;
                Assert.AreEqual(0f, f.AmountFor(job), $"{job} never takes");
            }
        }

        [Test]
        public void Active_IsAnyAmountAboveZero_AndNoneIsOff()
        {
            Assert.IsFalse(HitStealFacts.None.Active);
            Assert.IsTrue(new HitStealFacts { Shove = 1f }.Active);
            Assert.IsFalse(new HitStealFacts { LockSeconds = 3f }.Active, "a lock alone is not a hit-steal");
        }
    }
}
