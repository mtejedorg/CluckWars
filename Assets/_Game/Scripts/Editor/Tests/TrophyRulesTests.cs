using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Champion's trophy rules (design 2026-11-25): pure functions over facts.</summary>
    public sealed class TrophyRulesTests
    {
        [Test]
        public void Earn_StopsAtTheCap()
        {
            Assert.AreEqual(1, TrophyRules.Earn(0, 3));
            Assert.AreEqual(3, TrophyRules.Earn(2, 3));
            Assert.AreEqual(3, TrophyRules.Earn(3, 3), "at the cap a gain does nothing");
            Assert.AreEqual(0, TrophyRules.Earn(0, 0), "a cap of 0 earns none");
        }

        [Test]
        public void LoseOne_NeverBelowZero()
        {
            Assert.AreEqual(2, TrophyRules.LoseOne(3));
            Assert.AreEqual(0, TrophyRules.LoseOne(1));
            Assert.AreEqual(0, TrophyRules.LoseOne(0));
        }

        [Test]
        public void Room_IsTrophiesTimesRoom()
        {
            var f = new TrophyFacts { IsChampion = true, Room = 2, Cap = 3 };
            Assert.AreEqual(0f, TrophyRules.Room(0, f));
            Assert.AreEqual(6f, TrophyRules.Room(3, f));
            Assert.AreEqual(0f, TrophyRules.Room(3, new TrophyFacts { IsChampion = true, Room = 0, Cap = 3 }), "room 0: trophies add nothing");
            Assert.AreEqual(0f, TrophyRules.Room(3, TrophyFacts.None), "not the Champion's kit");
        }

        [Test]
        public void StealEarns_AtOneFood_NotBelow()
        {
            Assert.IsTrue(TrophyRules.StealEarns(1f));
            Assert.IsTrue(TrophyRules.StealEarns(5f));
            Assert.IsFalse(TrophyRules.StealEarns(0.99f));
            Assert.IsFalse(TrophyRules.StealEarns(0f));
        }

        [Test]
        public void Overflow_IsTheExcessOnly()
        {
            Assert.AreEqual(0f, TrophyRules.Overflow(10f, 20f));
            Assert.AreEqual(0f, TrophyRules.Overflow(20f, 20f));
            Assert.AreEqual(3f, TrophyRules.Overflow(23f, 20f));
            Assert.AreEqual(0f, TrophyRules.Overflow(0f, 0f));
        }
    }
}
