using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Champion's Peck break rules (design 2026-11-25): pure functions over facts.</summary>
    public sealed class PeckBreakRulesTests
    {
        [Test]
        public void CanBreak_NeedsCastReachNoWallNoEggNoImmunity()
        {
            Assert.IsTrue(PeckBreakRules.CanBreak(true, 2f, 2.5f, false, false, false));
            Assert.IsTrue(PeckBreakRules.CanBreak(true, 2.5f, 2.5f, false, false, false), "exactly at reach is in reach");
            Assert.IsFalse(PeckBreakRules.CanBreak(false, 2f, 2.5f, false, false, false), "no cast running");
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 2.6f, 2.5f, false, false, false), "out of reach");
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 2f, 2.5f, true, false, false), "a base wall between");
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 2f, 2.5f, false, true, false), "the egg refuses");
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 2f, 2.5f, false, false, true), "break-immune");
        }

        [Test]
        public void CanBreak_ReachZero_IsNeverTrue()
        {
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 0f, 0f, false, false, false));
            Assert.IsFalse(PeckBreakRules.CanBreak(true, 0f, -1f, false, false, false));
            Assert.IsFalse(new PeckBreakFacts { Reach = 0f, Cooldown = 2.5f }.Active);
            Assert.IsTrue(new PeckBreakFacts { Reach = 2.5f }.Active);
        }

        [Test]
        public void Bite_IsTheSmallestOfVictimBitePileAndRoom()
        {
            Assert.AreEqual(3f, PeckBreakRules.Bite(3f, 10f, 10f), 1e-6f);
            Assert.AreEqual(2f, PeckBreakRules.Bite(3f, 2f, 10f), 1e-6f, "the pile holds less than a bite");
            Assert.AreEqual(1f, PeckBreakRules.Bite(3f, 10f, 1f), 1e-6f, "the breaker has little room");
            Assert.AreEqual(0f, PeckBreakRules.Bite(3f, 10f, 0f), "a full breaker takes none; the bite stays in the pile");
            Assert.AreEqual(0f, PeckBreakRules.Bite(3f, 0f, 10f), "an empty pile gives none");
            Assert.AreEqual(0f, PeckBreakRules.Bite(3f, 10f, -2f), "never negative");
        }

        [Test]
        public void Resolve_BreakThenPileThenCancel()
        {
            Assert.AreEqual(BreakOutcome.Break, PeckBreakRules.Resolve(true, true), "a break beats a pile");
            Assert.AreEqual(BreakOutcome.Break, PeckBreakRules.Resolve(true, false));
            Assert.AreEqual(BreakOutcome.FallbackPeck, PeckBreakRules.Resolve(false, true));
            Assert.AreEqual(BreakOutcome.Cancel, PeckBreakRules.Resolve(false, false), "nothing to do: a visible cancel");
        }

        [Test]
        public void Nearest_PicksTheSmallestDistance_MinusOneForNone()
        {
            Assert.AreEqual(1, PeckBreakRules.Nearest(new[] { 3f, 1f, 2f }));
            Assert.AreEqual(0, PeckBreakRules.Nearest(new[] { 1f, 1f }), "a tie goes to the first");
            Assert.AreEqual(-1, PeckBreakRules.Nearest(new float[0]));
            Assert.AreEqual(-1, PeckBreakRules.Nearest(null));
            Assert.AreEqual(2, PeckBreakRules.Nearest(new[] { float.NaN, float.PositiveInfinity, 5f }), "a non-candidate is skipped");
            Assert.AreEqual(-1, PeckBreakRules.Nearest(new[] { float.PositiveInfinity }));
        }
    }
}
