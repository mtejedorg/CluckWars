using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Enforcer's debt on one rival as the game keeps it (design 2026-11-25, 16.2): the pure moves over the simulator's <see cref="DebtRules"/>.</summary>
    public sealed class DebtBookTests
    {
        private static DebtBook.DebtSlot Slot(int count, float until) => new DebtBook.DebtSlot { Count = count, Until = until };

        [Test]
        public void Gain_StopsAtTheCap_AndRestartsTheTimer()
        {
            var s = default(DebtBook.DebtSlot);
            s = DebtBook.Gain(s, 3, 10f, 6f);
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(16f, s.Until, 1e-5f, "the first gain starts the timer");
            s = DebtBook.Gain(s, 3, 12f, 6f);
            s = DebtBook.Gain(s, 3, 13f, 6f);
            Assert.AreEqual(3, s.Count);
            s = DebtBook.Gain(s, 3, 15f, 6f);
            Assert.AreEqual(3, s.Count, "at the cap a gain adds nothing");
            Assert.AreEqual(21f, s.Until, 1e-5f, "but it restarts the decay timer");
            Assert.AreEqual(0, DebtBook.Gain(default, 0, 1f, 6f).Count, "a cap of 0 earns none");
            Assert.AreEqual(2, DebtBook.Gain(Slot(2, 5f), 0, 1f, 6f).Count, "and leaves a slot as it was");
        }

        [Test]
        public void Decay_PutsTheCountToZeroAtOnce()
        {
            var held = Slot(3, 20f);
            var still = DebtBook.Decay(held, 20f, out int none);
            Assert.AreEqual(3, still.Count, "the debts last until their time has passed");
            Assert.AreEqual(0, none);
            var gone = DebtBook.Decay(held, 20.01f, out int lost);
            Assert.AreEqual(0, gone.Count, "all at once, not one by one");
            Assert.AreEqual(3, lost);
            DebtBook.Decay(default, 99f, out int empty);
            Assert.AreEqual(0, empty, "an empty slot loses nothing");
            Assert.AreEqual(0, DebtBook.Decay(Slot(2, float.NegativeInfinity), 0f, out _).Count, "a timer that is not running decays the count");
        }

        [Test]
        public void Cash_SubtractsWhatTheStealConsumed_OnlyOnAPositiveSettle()
        {
            var held = Slot(3, 30f);
            Assert.AreEqual(3, DebtBook.Cash(held, 2, 0f).Count, "an answer of 0 leaves the count");
            Assert.AreEqual(1, DebtBook.Cash(held, 2, 7f).Count, "a Stun that landed inside the round trip keeps its debt");
            Assert.AreEqual(30f, DebtBook.Cash(held, 2, 7f).Until, 1e-5f, "cashing does not restart the decay");
            Assert.AreEqual(0, DebtBook.Cash(held, 3, 0.5f).Count, "every debt consumed: none left");
            Assert.AreEqual(0, DebtBook.Cash(held, 9, 5f).Count, "never below 0");
            Assert.AreEqual(3, DebtBook.Cash(held, 0, 5f).Count, "a Steal that used no debts cashes none");
        }

        [Test]
        public void Gain_AndDecay_CallTheSharedRules()
        {
            string src = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/DebtBook.cs");
            StringAssert.Contains("class DebtBook", src);
            StringAssert.Contains("DebtRules.Add(", src);
            StringAssert.Contains("DebtRules.Decayed(", src);
        }
    }
}
