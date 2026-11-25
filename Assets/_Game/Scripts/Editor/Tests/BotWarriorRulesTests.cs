using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The bot's decisions about the Bully's hit-steal and the Champion's bite and trophies (design 2026-11-25, section 7): pure functions over facts.</summary>
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
        public void FreshLeft_IsCastLeftPlusWindow_OrWindowMinusSinceLanded_ZeroWhenBitten()
        {
            Assert.AreEqual(1.3f, BotChampionRules.FreshLeft(true, 0.3f, 99f, false, false, 1f), 1e-6f, "mid-cast: the cast left plus the window");
            Assert.AreEqual(0.4f, BotChampionRules.FreshLeft(false, 0f, 0.6f, true, false, 1f), 1e-6f, "just landed: the window minus the time since");
            Assert.AreEqual(0f, BotChampionRules.FreshLeft(true, 0.3f, 0f, true, true, 1f), "bitten");
            Assert.AreEqual(0f, BotChampionRules.FreshLeft(false, 0f, 1.5f, true, false, 1f), "past the window");
            Assert.AreEqual(0f, BotChampionRules.FreshLeft(true, 0.3f, 0f, true, false, 0f), "no window, no bite");
        }

        [Test]
        public void HitFresh_NeedsTheWindowToOutlastTheReaction()
        {
            Assert.IsTrue(BotChampionRules.HitFresh(0.4f, 0.3f));
            Assert.IsFalse(BotChampionRules.HitFresh(0.3f, 0.3f), "gone as the reaction ends");
            Assert.IsFalse(BotChampionRules.HitFresh(0f, 0f), "not fresh at all");
        }

        [Test]
        public void PickBuilder_ShoveFirst_MinusOneWhenNoneReady()
        {
            Assert.AreEqual(BotChampionRules.Shove, BotChampionRules.PickBuilder(true, true, 7f, 5f), "the shorter cooldown: the Shove");
            Assert.AreEqual(BotChampionRules.Stun, BotChampionRules.PickBuilder(true, true, 3f, 5f));
            Assert.AreEqual(BotChampionRules.Shove, BotChampionRules.PickBuilder(true, true, 5f, 5f), "a tie goes to the Shove");
            Assert.AreEqual(BotChampionRules.Stun, BotChampionRules.PickBuilder(true, false, 7f, 5f), "only the Stun is ready");
            Assert.AreEqual(BotChampionRules.Shove, BotChampionRules.PickBuilder(false, true, 7f, 5f));
            Assert.AreEqual(BotChampionRules.None, BotChampionRules.PickBuilder(false, false, 1f, 1f));
        }

        [Test]
        public void Cash_AtTheCap_OrWhenNoBuilderBeatsTheDecay_NeverAtCountZero()
        {
            Assert.IsTrue(BotChampionRules.Cash(3, 3, 5f, 1f, 11f), "at the cap");
            Assert.IsFalse(BotChampionRules.Cash(2, 3, 5f, 1f, 9f), "a builder is back before the count decays: build first");
            Assert.IsTrue(BotChampionRules.Cash(2, 3, 5f, 5f, 9f), "no builder back before the decay: cash now");
            Assert.IsFalse(BotChampionRules.Cash(0, 3, 5f, 99f, 5f), "count 0 is the ordinary Steal rule");
            Assert.IsFalse(BotChampionRules.Cash(3, 3, 5f, 1f, 0.5f), "it would take less than a food");
        }

        [Test]
        public void StealGain_IsTheInflatedAmountClamped()
        {
            Assert.AreEqual(11f, BotChampionRules.StealGain(5f, 1f, 2f, 3, 20f, 20f), 1e-6f);
            Assert.AreEqual(8f, BotChampionRules.StealGain(5f, 1f, 2f, 3, 8f, 20f), 1e-6f, "held to the room");
            Assert.AreEqual(4f, BotChampionRules.StealGain(5f, 1f, 2f, 3, 20f, 4f), 1e-6f, "held to the rival's food");
            Assert.AreEqual(5f, BotChampionRules.StealGain(5f, 1f, 0f, 3, 20f, 20f), 1e-6f, "shipped: the plain Steal");
        }

        [Test]
        public void Fight_OnlyWhenTheFightBeatsThePeckRate()
        {
            // A fight worth 8 food in 2.5 s is 3.2 a second. A Peck of 3 over a 0.71 s cycle is 4.2: stay at the pile.
            Assert.IsFalse(BotChampionRules.Fight(8f, 2.5f, 3f, 0.71f));
            // The same fight against a Peck slowed to a 1.065 s cycle (2.8 a second) pays.
            Assert.IsTrue(BotChampionRules.Fight(8f, 2.5f, 3f, 1.065f));
            Assert.IsFalse(BotChampionRules.Fight(0f, 1f, 3f, 5f), "a fight that gains nothing never pays");
        }
    }
}
