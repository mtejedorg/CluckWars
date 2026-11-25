using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Champion's per-enemy trophy rules (design 2026-11-25, redesign): pure functions over facts.</summary>
    public sealed class TrophyRulesTests
    {
        [Test]
        public void Add_StopsAtTheCap()
        {
            Assert.AreEqual(1, TrophyRules.Add(0, 3));
            Assert.AreEqual(3, TrophyRules.Add(2, 3));
            Assert.AreEqual(3, TrophyRules.Add(3, 3), "at the cap a gain adds nothing");
            Assert.AreEqual(0, TrophyRules.Add(0, 0), "a cap of 0 earns none");
        }

        [Test]
        public void StealAmount_IsBaseTimesFactorPlusPerTrophyTimesCount()
        {
            Assert.AreEqual(11f, TrophyRules.StealAmount(5f, 1f, 2f, 3), 1e-6f);
            Assert.AreEqual(5f, TrophyRules.StealAmount(5f, 1f, 2f, -1), 1e-6f, "a negative count is no count");
            Assert.AreEqual(5f, TrophyRules.StealAmount(5f, 1f, 0f, 3), 1e-6f, "shipped: trophies add nothing");
            Assert.AreEqual(8f, TrophyRules.StealAmount(5f, 1.6f, 0f, 0), 1e-6f, "the thief's factor scales the base only");
        }

        [Test]
        public void Decayed_AfterTheUntilOnly()
        {
            Assert.IsFalse(TrophyRules.Decayed(5.9f, 6f));
            Assert.IsFalse(TrophyRules.Decayed(6f, 6f), "at the moment itself the count still stands");
            Assert.IsTrue(TrophyRules.Decayed(6.01f, 6f));
        }
    }
}
