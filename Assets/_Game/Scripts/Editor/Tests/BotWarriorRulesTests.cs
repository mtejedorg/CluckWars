using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The bot's decisions about the Bully's hit-steal and the Trophy Hunter's break (design 2026-11-25, section 7): pure functions over facts.</summary>
    public sealed class BotWarriorRulesTests
    {
        [Test]
        public void CastForFood_AtOneFood()
        {
            Assert.IsTrue(BotHitStealRules.CastForFood(1f));
            Assert.IsTrue(BotHitStealRules.CastForFood(4f));
            Assert.IsFalse(BotHitStealRules.CastForFood(0.99f));
            Assert.IsFalse(BotHitStealRules.CastForFood(0f));
        }

        [Test]
        public void Gain_IsTheHitStealTake_WithNoRefusal()
        {
            Assert.AreEqual(2f, BotHitStealRules.Gain(2f, 5f, 5f, false), 1e-6f);
            Assert.AreEqual(1f, BotHitStealRules.Gain(2f, 1f, 5f, false), 1e-6f, "held to the rival's food");
            Assert.AreEqual(0f, BotHitStealRules.Gain(2f, 5f, 5f, true), "a locked rival gives nothing");
        }

        [Test]
        public void PickHitter_TieGoesToTheShorterCooldown()
        {
            Assert.AreEqual(BotHitStealRules.Stun, BotHitStealRules.PickHitter(3f, 2f, 5.25f, 3.75f), "the larger gain");
            Assert.AreEqual(BotHitStealRules.Shove, BotHitStealRules.PickHitter(2f, 3f, 5.25f, 3.75f));
            Assert.AreEqual(BotHitStealRules.Shove, BotHitStealRules.PickHitter(2f, 2f, 5.25f, 3.75f), "a tie: the Shove's shorter cooldown, the Stun kept for control");
            Assert.AreEqual(BotHitStealRules.Stun, BotHitStealRules.PickHitter(2f, 2f, 2f, 3f), "a tie goes to whichever recharges first");
            Assert.AreEqual(BotHitStealRules.Shove, BotHitStealRules.PickHitter(2f, 2f, 3f, 3f), "equal cooldowns too: the Shove");
            Assert.AreEqual(BotHitStealRules.None, BotHitStealRules.PickHitter(0f, 0f, 1f, 1f));
        }

        [Test]
        public void WorthHunting_SkipsAPreyLockedPastTheWalk()
        {
            Assert.IsTrue(BotHitStealRules.WorthHunting(2f, 0f, 1f));
            Assert.IsTrue(BotHitStealRules.WorthHunting(2f, 1f, 1f), "the lock ends as the walk does");
            Assert.IsFalse(BotHitStealRules.WorthHunting(2f, 1.5f, 1f), "still locked on arrival");
            Assert.IsFalse(BotHitStealRules.WorthHunting(0.5f, 0f, 1f), "less than a food");
        }

        [Test]
        public void Candidate_NeedsTheCastToOutlastTheReaction()
        {
            Assert.IsTrue(BotBreakRules.Candidate(true, 0.4f, 2f, 2.5f, false, false, 0.3f));
            Assert.IsFalse(BotBreakRules.Candidate(true, 0.3f, 2f, 2.5f, false, false, 0.3f), "the cast ends as the reaction does");
            Assert.IsFalse(BotBreakRules.Candidate(true, 0.2f, 2f, 2.5f, false, false, 0.3f), "gone before the press");
            Assert.IsFalse(BotBreakRules.Candidate(false, 0.4f, 2f, 2.5f, false, false, 0.3f), "not casting");
            Assert.IsFalse(BotBreakRules.Candidate(true, 0.4f, 3f, 2.5f, false, false, 0.3f), "out of reach");
            Assert.IsFalse(BotBreakRules.Candidate(true, 0.4f, 2f, 2.5f, true, false, 0.3f), "a wall");
            Assert.IsFalse(BotBreakRules.Candidate(true, 0.4f, 2f, 2.5f, false, true, 0.3f), "immune");
        }

        [Test]
        public void Approach_OnlyWhenTheWalkBeatsTheCast()
        {
            // 1 m outside the reach at 10 m/s is 0.1 s of walking, plus a 0.3 s reaction: 0.4 s.
            Assert.IsTrue(BotBreakRules.Approach(3.5f, 2.5f, 0.5f, 10f, 0.3f));
            Assert.IsFalse(BotBreakRules.Approach(3.5f, 2.5f, 0.4f, 10f, 0.3f), "the cast ends as the bot arrives");
            Assert.IsFalse(BotBreakRules.Approach(2f, 2.5f, 0.5f, 10f, 0.3f), "already in reach: that is a candidate, not an approach");
            Assert.IsFalse(BotBreakRules.Approach(3.5f, 2.5f, 0.5f, 0f, 0.3f), "cannot walk");
            Assert.IsFalse(BotBreakRules.Approach(3.5f, 0f, 0.5f, 10f, 0.3f), "no break at all");
        }

        [Test]
        public void ShouldBreak_AtAPileOnlyWhileATrophyIsLeft()
        {
            Assert.IsTrue(BotBreakRules.ShouldBreak(true, 3f, 2, 3), "at a pile, a trophy still to win");
            Assert.IsFalse(BotBreakRules.ShouldBreak(true, 3f, 3, 3), "at a pile with the cap held: peck instead");
            Assert.IsTrue(BotBreakRules.ShouldBreak(true, 0f, 3, 3), "away from a pile with nothing to peck: break");
            Assert.IsFalse(BotBreakRules.ShouldBreak(false, 0f, 0, 3), "no candidate");
        }

        [Test]
        public void ReturnFraction_GrowsWithTheTrophyRoom()
        {
            // A beak of 20 and a return amount of 12 is 0.6; each trophy of 2 room raises the amount by 2 over a bigger beak.
            Assert.AreEqual(0.6f, BotBreakRules.ReturnFraction(0.85f, 12f, 0, 2, 20f), 1e-6f);
            Assert.AreEqual(16f / 24f, BotBreakRules.ReturnFraction(0.85f, 12f, 2, 2, 24f), 1e-6f);
            Assert.Greater(BotBreakRules.ReturnFraction(0.85f, 12f, 2, 2, 24f), BotBreakRules.ReturnFraction(0.85f, 12f, 0, 2, 24f));
            Assert.AreEqual(0.5f, BotBreakRules.ReturnFraction(0.5f, 12f, 3, 5, 24f), 1e-6f, "never above the profile's own fraction");
            Assert.AreEqual(0.85f, BotBreakRules.ReturnFraction(0.85f, 0f, 3, 2, 24f), "no return amount: the profile's fraction");
            Assert.AreEqual(0.85f, BotBreakRules.ReturnFraction(0.85f, 12f, 0, 2, 10f), "a beak under the amount keeps the fraction");
        }
    }
}
