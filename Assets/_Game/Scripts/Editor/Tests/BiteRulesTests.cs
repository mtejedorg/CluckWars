using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Enforcer's bite rules (design 2026-11-25, redesign): pure functions over facts.</summary>
    public sealed class BiteRulesTests
    {
        [Test]
        public void Fresh_MidCastOrJustLandedWithFood_NotBitten()
        {
            Assert.IsTrue(BiteRules.Fresh(true, 99f, false, false, 1f), "mid-cast");
            Assert.IsTrue(BiteRules.Fresh(false, 0.9f, true, false, 1f), "landed 0.9 s ago with food at a window of 1");
            Assert.IsFalse(BiteRules.Fresh(false, 1.1f, true, false, 1f), "1.1 s is past the window");
            Assert.IsFalse(BiteRules.Fresh(false, 0.5f, false, false, 1f), "a landing that took no food opens no state");
            Assert.IsFalse(BiteRules.Fresh(true, 0.5f, true, true, 1f), "already bitten");
        }

        [Test]
        public void Fresh_FreshSecondsZero_IsNeverFresh_EvenMidCast()
        {
            Assert.IsFalse(BiteRules.Fresh(true, 0f, true, false, 0f));
            Assert.IsFalse(BiteRules.Fresh(false, 0f, true, false, 0f));
            Assert.IsFalse(BiteFacts.None.Active);
            Assert.IsTrue(new BiteFacts { FreshSeconds = 0.6f }.Active);
        }

        [Test]
        public void FromCast_IsTheSmallestOfBitePileAndRoom()
        {
            Assert.AreEqual(2f, BiteRules.FromCast(3f, 2f, 10f), 1e-6f);
            Assert.AreEqual(1f, BiteRules.FromCast(3f, 10f, 1f), 1e-6f);
            Assert.AreEqual(3f, BiteRules.FromCast(3f, 10f, 10f), 1e-6f);
            Assert.AreEqual(0f, BiteRules.FromCast(-3f, 10f, 10f));
            Assert.AreEqual(0f, BiteRules.FromCast(3f, -1f, 10f));
            Assert.AreEqual(0f, BiteRules.FromCast(3f, 10f, -1f));
        }

        [Test]
        public void FromCargo_IsClampedToRoomAndVictim()
        {
            Assert.AreEqual(2f, BiteRules.FromCargo(3f, 2f, 10f), 1e-6f);
            Assert.AreEqual(1f, BiteRules.FromCargo(3f, 10f, 1f), 1e-6f);
            Assert.AreEqual(3f, BiteRules.FromCargo(3f, 10f, 10f), 1e-6f);
            Assert.AreEqual(0f, BiteRules.FromCargo(3f, 0f, 10f));
        }
    }
}
