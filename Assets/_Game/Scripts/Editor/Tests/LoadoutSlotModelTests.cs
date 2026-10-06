using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The GEAR UP slot-arming state machine (<see cref="LoadoutSlotModel{T}"/>): which slot is
    /// armed, where the NEXT caret goes, what a card tap does, and that starters never move.</summary>
    public sealed class LoadoutSlotModelTests
    {
        private static readonly bool[] NoLocks = { false, false, false, false };

        private static LoadoutSlotModel<string> Model(string[] slots, bool[] locks)
        {
            var m = new LoadoutSlotModel<string>(4);
            m.Load(slots, locks);
            m.AutoArmFirstEmpty();
            return m;
        }

        [Test]
        public void FreshLoadout_ArmsFirstEmptyUnlockedSlot_AndCaretFollowsIt()
        {
            var m = Model(new[] { "peck", "sig", null, null }, new[] { true, true, false, false });
            Assert.AreEqual(2, m.Armed);
            Assert.AreEqual(2, m.NextCaretSlot);
            Assert.AreEqual(2, m.Missing);
        }

        [Test]
        public void Placing_FillsArmedSlot_AndAdvancesToNextEmpty()
        {
            var m = Model(new[] { "peck", null, null, null }, new[] { true, false, false, false });
            Assert.AreEqual(CardTapResult.Placed, m.TapCard("a", false));
            Assert.AreEqual("a", m.Get(1));
            Assert.AreEqual(2, m.Armed);
            Assert.IsTrue(m.IsPicked("a"));
        }

        [Test]
        public void TappingTheArmedSlotsOwnCard_ClearsIt_AndKeepsItArmed()
        {
            var m = Model(new[] { "a", "b", "c", "d" }, NoLocks);
            Assert.IsTrue(m.ArmSlot(1));
            Assert.AreEqual(CardTapResult.Cleared, m.TapCard("b", false));
            Assert.IsNull(m.Get(1));
            Assert.AreEqual(1, m.Armed);
        }

        [Test]
        public void TappingACardEquippedElsewhere_SwapsItIntoTheArmedSlot()
        {
            var m = Model(new[] { "a", "b", "c", "d" }, NoLocks);
            m.ArmSlot(0);
            Assert.AreEqual(CardTapResult.Swapped, m.TapCard("d", false));
            Assert.AreEqual("d", m.Get(0));
            Assert.AreEqual("a", m.Get(3));
        }

        [Test]
        public void LockedStarters_CannotBeArmedMovedOrCleared()
        {
            var m = Model(new[] { "peck", "sig", "x", null }, new[] { true, true, false, false });
            Assert.IsFalse(m.ArmSlot(0));
            Assert.AreEqual(3, m.Armed);
            Assert.AreEqual(CardTapResult.Ignored, m.TapCard("peck", true));
            // Even if a caller forgets the lock flag, a card sitting in a locked slot never swaps out.
            Assert.AreEqual(CardTapResult.Ignored, m.TapCard("sig", false));
            Assert.AreEqual("peck", m.Get(0));
            Assert.AreEqual("sig", m.Get(1));
        }

        [Test]
        public void FullLoadout_ArmsFirstUnlockedSlot()
        {
            var m = Model(new[] { "peck", "a", "b", "c" }, new[] { true, false, false, false });
            Assert.AreEqual(1, m.Armed);
            Assert.AreEqual(0, m.Missing);
        }

        [Test]
        public void AllSlotsLocked_HasNoCaret()
        {
            var m = Model(new[] { "a", "b", "c", "d" }, new[] { true, true, true, true });
            Assert.AreEqual(-1, m.NextCaretSlot);
            Assert.AreEqual(CardTapResult.Ignored, m.TapCard("e", false));
        }

        [Test]
        public void Load_KeepsArmingOnAnUnlockedSlot_ButMovesItOffANewlyLockedOne()
        {
            var m = Model(new string[] { null, null, null, null }, NoLocks);
            m.ArmSlot(2);
            m.Load(new[] { "x", null, null, null }, NoLocks);
            Assert.AreEqual(2, m.Armed, "A reload must not steal the player's chosen slot.");
            m.Load(new[] { "x", null, "s", null }, new[] { false, false, true, false });
            Assert.AreEqual(1, m.Armed);
        }
    }
}
