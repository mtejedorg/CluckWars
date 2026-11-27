using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>The Thief bot's loot-mark rules (<see cref="BotLootRules"/>, BL-217 round 2): pure, so the game's bot and P0 call the same ones.</summary>
    public sealed class BotLootRulesTests
    {
        [Test]
        public void Pursue_NeedsALiveKnownUnshelledMarkNotBehindItsWall()
        {
            Assert.IsTrue(BotLootRules.Pursue(true, true, false, false));
            Assert.IsFalse(BotLootRules.Pursue(false, true, false, false), "no live mark");
            Assert.IsFalse(BotLootRules.Pursue(true, false, false, false), "a hidden rival the bot has given up");
            Assert.IsFalse(BotLootRules.Pursue(true, true, true, false), "shelled: the cast would be refused");
            Assert.IsFalse(BotLootRules.Pursue(true, true, false, true), "home behind its wall: no ability crosses it");
        }

        [Test]
        public void Pursue_AnUncatchableMarkIsAbandoned()
        {
            Assert.IsFalse(BotLootRules.Pursue(true, true, false, false, false), "he is home before the Thief can reach him");
            Assert.IsTrue(BotLootRules.Pursue(true, true, false, false, true));
        }

        [Test]
        public void CanIntercept_ComparesTheThiefsWalkToTheWallWithTheBirdsWalkHome()
        {
            // wall 2.5, reach 5: the Thief 40 m from the bird's base needs (40-2.5-5)/11 = 2.95 s; the bird 10 m out needs (10-2.5)/10.8 = 0.69 s.
            Assert.IsFalse(BotLootRules.CanIntercept(40f, 10f, 2.5f, 5f, 11f, 10.8f, 30f), "a bird near home, a Thief far away: he gets there first");
            Assert.IsTrue(BotLootRules.CanIntercept(40f, 35f, 2.5f, 5f, 11f, 10.8f, 15f), "a bird far from home: the Thief cuts him off (2.95 s against 3.01 s)");
            Assert.IsTrue(BotLootRules.CanIntercept(40f, 4f, 2.5f, 5f, 11f, 10.8f, 4f), "already inside reach");
            Assert.IsTrue(BotLootRules.CanIntercept(40f, 10f, 2.5f, 5f, 11f, 0f, 30f), "unknown speed counts as catchable");
        }

        [Test]
        public void BehindWall_IsInsideTheKeepOutCircleOfItsOwnBase()
        {
            var centre = new Vector2(10f, 10f);
            Assert.IsTrue(BotLootRules.BehindWall(centre, centre, 6f));
            Assert.IsFalse(BotLootRules.BehindWall(centre + new Vector2(8f, 0f), centre, 6f));
        }

        [Test]
        public void HoldSteal_OnlyWhileTheCooldownWouldStillRunOnArrival()
        {
            Assert.IsTrue(BotLootRules.HoldSteal(true, 20f, 10f, 4f), "2 s away, a cast on someone else would still be cooling down");
            Assert.IsFalse(BotLootRules.HoldSteal(true, 60f, 10f, 4f), "6 s away: the cooldown is over on arrival, the cast is free");
            Assert.IsFalse(BotLootRules.HoldSteal(false, 1f, 10f, 4f), "not pursuing: nothing to keep it for");
            Assert.IsTrue(BotLootRules.HoldSteal(true, 60f, 0f, 4f), "no speed to estimate with: hold");
        }

        [Test]
        public void Retreat_FiresWhenTriggered_ExceptWhileThePursuitOfAMarkIsOn()
        {
            Assert.IsTrue(BotLootRules.Retreat(true, false), "loaded, rival close, no mark to walk to: run home");
            Assert.IsFalse(BotLootRules.Retreat(true, true), "the rival in reach is the marked one the Thief walks to: no retreat");
            Assert.IsFalse(BotLootRules.Retreat(false, false), "not triggered stays not triggered");
            Assert.IsFalse(BotLootRules.Retreat(false, true));
        }

        [Test]
        public void BankNow_ForTheClock_TheWin_OrANextMarkThatWouldNotFit()
        {
            // capacity 14, bounty 10: room below 10 means the next marked steal would lose part of its bounty.
            Assert.IsFalse(BotLootRules.BankNow(0f, 14f, 10f, 0f, 40f, 30f, 10f, 5f, 30f), "nothing carried: nothing to bank");
            Assert.IsFalse(BotLootRules.BankNow(4f, 14f, 10f, 0f, 40f, 30f, 10f, 5f, 30f), "room 10 fits the whole bounty");
            Assert.IsTrue(BotLootRules.BankNow(4.5f, 14f, 10f, 0f, 40f, 30f, 10f, 5f, 30f), "room 9.5 does not");
            Assert.IsTrue(BotLootRules.BankNow(2f, 14f, 10f, 38f, 40f, 30f, 10f, 5f, 30f), "banking it reaches the win target");
            Assert.IsTrue(BotLootRules.BankNow(1f, 14f, 10f, 0f, 40f, 30f, 10f, 5f, 3f), "the clock would run out on the walk");
        }

        [Test]
        public void NextMarkedStealWouldNotFit_IsRoomBelowTheBounty()
        {
            Assert.IsFalse(BotLootRules.NextMarkedStealWouldNotFit(4f, 14f, 10f));
            Assert.IsTrue(BotLootRules.NextMarkedStealWouldNotFit(5f, 14f, 10f));
        }
    }
}
