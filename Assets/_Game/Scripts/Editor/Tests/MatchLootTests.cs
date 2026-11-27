using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MatchEventKind = CluckWars.Balance.MatchEventKind;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Thief's loot mark (BL-217, design 2026-11-23): the pure lottery and rules, the mark's life cycle in the match simulator, the marked
    /// steal and its bounty, the clock prize, the switch that turns the mechanic off, and the report rows. Every number is the test's own on
    /// synthetic maps and kits; the shipped-asset tests read values at run time. No test asserts a win rate.
    /// </summary>
    public sealed class MatchLootTests
    {
        private const float Half = 40f;
        private const float Tick = 1f / 30f;
        private const float Big = 1000f;
        private const float StealCooldown = 4f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        // ---------------------------------------------------------------- the pure rules

        private static LootWeightTable Table(float b, float banked, float carried, float over, float bonus) =>
            new LootWeightTable { Base = b, Banked = banked, Carried = carried, OverThreshold = over, OverBonus = bonus };

        [Test]
        public void Weight_IsBasePlusBankedPlusCarried_AndTheBonusTestsBankedPlusCarried()
        {
            var t = Table(2f, 3f, 0.5f, 10f, 7f);
            Assert.AreEqual(2f, LootPickRules.Weight(0f, 0f, t), 1e-5f, "an empty bird keeps its base weight");
            Assert.AreEqual(2f + 3f * 4f + 0.5f * 2f, LootPickRules.Weight(4f, 2f, t), 1e-5f);
            Assert.AreEqual(2f + 3f * 6f + 0.5f * 4f, LootPickRules.Weight(6f, 4f, t), 1e-5f, "exactly at the threshold earns no bonus");
            Assert.AreEqual(2f + 3f * 6f + 0.5f * 5f + 7f, LootPickRules.Weight(6f, 5f, t), 1e-5f, "banked plus carried above it does (D3 b)");
            Assert.AreEqual(2f + 3f * 11f + 7f, LootPickRules.Weight(11f, 0f, t), 1e-5f, "banked alone can pass it");
            Assert.AreEqual(2f + 0.5f * 11f + 7f, LootPickRules.Weight(0f, 11f, t), 1e-5f, "carried alone can pass it");
        }

        [Test]
        public void Weight_NeverNegative_AndIgnoresNegativeFood()
        {
            Assert.AreEqual(0f, LootPickRules.Weight(0f, 0f, Table(0f, 1f, 1f, 5f, 5f)));
            Assert.AreEqual(1f, LootPickRules.Weight(-5f, -5f, Table(1f, 1f, 1f, 5f, 5f)), 1e-6f);
            Assert.AreEqual(0f, LootPickRules.Weight(0f, 0f, Table(-3f, 0f, 0f, 5f, 0f)));
        }

        [Test]
        public void Pick_IsProportional_ChanceIsWeightOverSum()
        {
            var weights = new[] { 1f, 3f, 0f, 4f };
            var hits = new int[weights.Length];
            const int steps = 8000;
            for (int i = 0; i < steps; i++) hits[LootPickRules.Pick(weights, (i + 0.5f) / steps)]++;
            Assert.AreEqual(1f / 8f, hits[0] / (float)steps, 2e-3f);
            Assert.AreEqual(3f / 8f, hits[1] / (float)steps, 2e-3f);
            Assert.AreEqual(0, hits[2], "a weight of zero is never picked");
            Assert.AreEqual(4f / 8f, hits[3] / (float)steps, 2e-3f);
        }

        [Test]
        public void Pick_FirstWhoseRunningSumPassesTheRoll_InTheCallersOrder()
        {
            var w = new[] { 2f, 2f };
            Assert.AreEqual(0, LootPickRules.Pick(w, 0f));
            Assert.AreEqual(0, LootPickRules.Pick(w, 0.499f));
            Assert.AreEqual(1, LootPickRules.Pick(w, 0.5f));
            Assert.AreEqual(1, LootPickRules.Pick(w, 0.999999f));
        }

        [Test]
        public void Pick_NoCandidate_IsMinusOne_AndAllZeroFallsBackToUniform()
        {
            Assert.AreEqual(-1, LootPickRules.Pick(new float[0], 0.5f));
            var zeros = new[] { 0f, 0f, 0f };
            Assert.AreEqual(MarkPickRules.Pick(3, 0.1f), LootPickRules.Pick(zeros, 0.1f));
            Assert.AreEqual(MarkPickRules.Pick(3, 0.9f), LootPickRules.Pick(zeros, 0.9f));
        }

        [Test]
        public void Pick_ARollThatRoundingPushedPastTheSum_StillLandsOnAWeightedCandidate()
        {
            Assert.AreEqual(1, LootPickRules.Pick(new[] { 0f, 1f, 0f }, 1.5f));
        }

        [Test]
        public void Filter_DropsTheLastMarked_UnlessHeIsTheOnlyRival()
        {
            var list = new List<int> { 1, 2, 3 };
            LootPickRules.Filter(list, true, 2, 3);
            CollectionAssert.AreEqual(new[] { 1, 3 }, list);
            list = new List<int> { 2 };
            LootPickRules.Filter(list, true, 2, 1);
            CollectionAssert.AreEqual(new[] { 2 }, list, "the only rival in the match can be marked again");
            list = new List<int> { 1, 2 };
            LootPickRules.Filter(list, false, 0, 2);
            CollectionAssert.AreEqual(new[] { 1, 2 }, list, "no last mark, nobody out");
        }

        [Test]
        public void CanTake_NeedsRange_NoWall_NoShell()
        {
            Assert.IsTrue(LootRules.CanTake(true, false, false));
            Assert.IsFalse(LootRules.CanTake(false, false, false));
            Assert.IsFalse(LootRules.CanTake(true, true, false));
            Assert.IsFalse(LootRules.CanTake(true, false, true));
        }

        [Test]
        public void DrawAt_IsLeadSecondsBeforeReady_AndNeverBeforeNow()
        {
            Assert.AreEqual(9f, LootRules.DrawAt(5f, 10f, 1f), 1e-6f);
            Assert.AreEqual(5f, LootRules.DrawAt(5f, 5.5f, 1f), 1e-6f, "the moment has passed: at once");
            Assert.AreEqual(10f, LootRules.DrawAt(5f, 10f, 0f), 1e-6f);
            Assert.AreEqual(10f, LootRules.DrawAt(5f, 10f, -3f), 1e-6f, "a negative lead is no lead");
        }

        // ---- rule v2 (2026-11-26): who may carry the mark, when one is drawn, how a live one ends ----
        // Eligible(removed, ally, decoy, insideOwnBase, cargo, minCargo, stealProof, hidden)

        [Test]
        public void Eligible_OnlyALivingRivalWithEnoughFood()
        {
            Assert.IsTrue(LootRules.Eligible(false, false, false, false, 6f, 6f, false, false), "exactly the minimum cargo is enough");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, 5.99f, 6f, false, false), "below the minimum cargo");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, 0f, 0f, false, false), "an empty beak is never eligible, even with no minimum");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, -3f, 0f, false, false), "a negative cargo is empty");
            Assert.IsTrue(LootRules.Eligible(false, false, false, false, 1f, 0f, false, false), "no minimum: any food a steal could take");
        }

        [Test]
        public void Eligible_EveryHardFilterAloneRulesTheBirdOut()
        {
            Assert.IsFalse(LootRules.Eligible(true, false, false, false, 10f, 6f, false, false), "removed");
            Assert.IsFalse(LootRules.Eligible(false, true, false, false, 10f, 6f, false, false), "an ally of the Thief's corner");
            Assert.IsFalse(LootRules.Eligible(false, false, true, false, 10f, 6f, false, false), "a decoy");
            Assert.IsFalse(LootRules.Eligible(false, false, false, true, 10f, 6f, false, false), "inside his own base's wall: unstealable");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, 10f, 6f, true, false), "steal-proof (shell)");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, 10f, 6f, false, true), "hidden");
        }

        [Test]
        public void Eligible_TheLastVictimIsFilterNotEligibleBusiness_AndCountsRivalsNotCarriers()
        {
            // v2: the last victim leaves unless he is the only rival IN THE MATCH. Two rivals, only the last victim carries: nobody is left (no repeat).
            var list = new List<int> { 2 };
            LootPickRules.Filter(list, true, 2, 2);
            Assert.AreEqual(0, list.Count, "never the same victim twice running while another rival exists, carrier or not");
            list = new List<int> { 2 };
            LootPickRules.Filter(list, true, 2, 1);
            CollectionAssert.AreEqual(new[] { 2 }, list, "the only rival of the match can be marked again");
        }

        [Test]
        public void HomeFactor_EndpointsAndMonotone()
        {
            Assert.AreEqual(0.05f, LootPickRules.HomeFactor(0f, 2.5f, 8f, 0.05f), 1e-6f, "on his base");
            Assert.AreEqual(0.05f, LootPickRules.HomeFactor(2.5f, 2.5f, 8f, 0.05f), 1e-6f, "at the wall");
            Assert.AreEqual(1f, LootPickRules.HomeFactor(8f, 2.5f, 8f, 0.05f), 1e-6f, "at the home radius");
            Assert.AreEqual(1f, LootPickRules.HomeFactor(30f, 2.5f, 8f, 0.05f), 1e-6f, "far away");
            Assert.AreEqual(0.525f, LootPickRules.HomeFactor(5.25f, 2.5f, 8f, 0.05f), 1e-5f, "linear: halfway is the middle of 0.05 and 1");
            float last = 0f;
            for (float d = 0f; d <= 12f; d += 0.25f)
            {
                float f = LootPickRules.HomeFactor(d, 2.5f, 8f, 0.05f);
                Assert.GreaterOrEqual(f, last - 1e-6f, $"not monotone at {d} m");
                last = f;
            }
        }

        [Test]
        public void HomeFactor_AHomeRadiusInsideTheWallSwitchesItOff_AndTheFloorIsClamped()
        {
            Assert.AreEqual(1f, LootPickRules.HomeFactor(0f, 2.5f, 0f, 0.05f), "a facts struct built without the field: no cut");
            Assert.AreEqual(1f, LootPickRules.HomeFactor(0f, 2.5f, 2.5f, 0.05f));
            Assert.AreEqual(0f, LootPickRules.HomeFactor(0f, 2.5f, 8f, -1f), "a negative floor is 0");
            Assert.AreEqual(1f, LootPickRules.HomeFactor(0f, 2.5f, 8f, 3f), "a floor above 1 is 1");
        }

        [Test]
        public void HomeFactor_ScalesTheLotteryWeight_ButNeverToANegative()
        {
            var t = Table(4f, 1f, 0.5f, 99f, 0f);
            float plain = LootPickRules.Weight(2f, 6f, t);
            Assert.AreEqual(plain * 0.05f, LootPickRules.Weight(2f, 6f, t, 0.05f), 1e-6f);
            Assert.AreEqual(plain, LootPickRules.Weight(2f, 6f, t, 1f), 1e-6f);
            Assert.AreEqual(0f, LootPickRules.Weight(2f, 6f, t, -1f), "a negative factor is no weight");
        }

        [Test]
        public void Draw_NeedsLeadOpeningGapRoomAndNoPendingSteal()
        {
            // MayDraw(stealLeft, lead, matchTime, opening, sinceUnpaidEnd, redrawGap, freeRoom, pending)
            float none = float.PositiveInfinity;
            Assert.IsTrue(LootRules.MayDraw(1f, 1f, 5f, 5f, none, 1.5f, 4f, false), "all hold, each exactly at its edge");
            Assert.IsFalse(LootRules.MayDraw(1.5f, 1f, 9f, 5f, none, 1.5f, 4f, false), "Steal is more than the lead from ready");
            Assert.IsFalse(LootRules.MayDraw(0f, 1f, 4.9f, 5f, none, 1.5f, 4f, false), "inside the opening seconds of the match");
            Assert.IsFalse(LootRules.MayDraw(0f, 1f, 9f, 5f, 1.4f, 1.5f, 4f, false), "inside the redraw gap of an unpaid end");
            Assert.IsTrue(LootRules.MayDraw(0f, 1f, 9f, 5f, 1.5f, 1.5f, 4f, false), "the gap has run");
            Assert.IsFalse(LootRules.MayDraw(0f, 1f, 9f, 5f, none, 1.5f, 0f, false), "a full beak draws nothing");
            Assert.IsTrue(LootRules.MayDraw(0f, 1f, 9f, 5f, none, 1.5f, 3f, false), "room below the bounty still draws (the bounty is lost in part)");
            Assert.IsFalse(LootRules.MayDraw(0f, 1f, 9f, 5f, none, 1.5f, 4f, true), "a marked steal is in flight");
        }

        [Test]
        public void Draw_ZeroedFieldsAreTheOldRule()
        {
            Assert.IsTrue(LootRules.MayDraw(0f, 1f, 0f, 0f, 0f, 0f, 1f, false), "no opening, no gap: a mark at GO, as before v2");
        }

        [Test]
        public void Keep_Order_GoneThenHomeThenEmptyThenLight()
        {
            // Keep(gone, insideOwnBase, cargo, keepCargo, stealReady, closerCandidate)
            Assert.AreEqual(LootEnd.Gone, LootRules.Keep(true, true, 0f, 3f, false, false), "gone first");
            Assert.AreEqual(LootEnd.Home, LootRules.Keep(false, true, 0f, 3f, false, false), "inside his wall, empty or not: home");
            Assert.AreEqual(LootEnd.Empty, LootRules.Keep(false, false, 0f, 3f, true, true), "nothing to take");
            Assert.AreEqual(LootEnd.Empty, LootRules.Keep(false, false, -1f, 3f, true, true), "a negative cargo is empty");
            Assert.AreEqual(LootEnd.None, LootRules.Keep(false, false, 10f, 3f, false, false), "plenty of food, Steal on cooldown: the mark stays");
        }

        [Test]
        public void Keep_ALightBird_LeavesWhileStealCools_StaysWhenReadyUnlessSomeoneIsAsClose()
        {
            Assert.AreEqual(LootEnd.Light, LootRules.Keep(false, false, 2f, 3f, false, false), "Steal on cooldown: ends unpaid");
            Assert.AreEqual(LootEnd.None, LootRules.Keep(false, false, 2f, 3f, true, false), "Steal ready, nobody as close: kept, so it does not vanish as the Thief arrives");
            Assert.AreEqual(LootEnd.Light, LootRules.Keep(false, false, 2f, 3f, true, true), "Steal ready and a candidate as close: the mark moves");
            Assert.AreEqual(LootEnd.None, LootRules.Keep(false, false, 3f, 3f, false, false), "exactly the keep cargo is not light");
            Assert.AreEqual(LootEnd.None, LootRules.Keep(false, false, 2f, 0f, false, false), "a keep cargo of 0 switches the light rule off");
            Assert.IsTrue(LootRules.IsLight(0.5f, 3f));
            Assert.IsFalse(LootRules.IsLight(0f, 3f), "empty is the Empty end, not light");
        }

        [Test]
        public void SwitchTarget_OnlyCandidatesNoFartherThanTheMarkedBirdPlusTheSlack()
        {
            var weights = new[] { 1f, 1f, 1f };
            // marked bird at 10 m, slack 3: the limit is 13 m
            Assert.AreEqual(-1, LootPickRules.SwitchTarget(new[] { 13.01f, 20f, 40f }, weights, 10f, 3f, 0.5f), "nobody within the limit");
            Assert.AreEqual(0, LootPickRules.SwitchTarget(new[] { 13f, 20f, 40f }, weights, 10f, 3f, 0.5f), "exactly at the limit counts");
            Assert.AreEqual(1, LootPickRules.SwitchTarget(new[] { 20f, 4f, 40f }, weights, 10f, 3f, 0.9f), "the only close one wins whatever the roll");
            Assert.AreEqual(-1, LootPickRules.SwitchTarget(new float[0], new float[0], 10f, 3f, 0.5f), "no candidate at all");
        }

        [Test]
        public void SwitchTarget_IsAWeightedPickAmongTheCloseOnes()
        {
            var distances = new[] { 5f, 6f, 50f };
            var weights = new[] { 1f, 3f, 100f };   // the far one weighs most and must never win
            Assert.AreEqual(0, LootPickRules.SwitchTarget(distances, weights, 10f, 3f, 0f));
            Assert.AreEqual(0, LootPickRules.SwitchTarget(distances, weights, 10f, 3f, 0.24f));
            Assert.AreEqual(1, LootPickRules.SwitchTarget(distances, weights, 10f, 3f, 0.25f));
            Assert.AreEqual(1, LootPickRules.SwitchTarget(distances, weights, 10f, 3f, 0.999999f));
            Assert.AreEqual(1, LootPickRules.SwitchTarget(distances, weights, 10f, 3f, 1.5f), "a roll past the range lands on the last weighted close one");
        }

        [Test]
        public void SwitchTarget_CloseCandidatesWithNoWeightFallBackToUniform_AndANegativeSlackIsNone()
        {
            var distances = new[] { 5f, 6f, 50f };
            var zeros = new[] { 0f, 0f, 9f };
            Assert.AreEqual(0, LootPickRules.SwitchTarget(distances, zeros, 10f, 3f, 0.1f));
            Assert.AreEqual(1, LootPickRules.SwitchTarget(distances, zeros, 10f, 3f, 0.9f));
            Assert.AreEqual(-1, LootPickRules.SwitchTarget(new[] { 11f }, new[] { 1f }, 10f, -5f, 0.5f), "a negative slack is no slack: 11 m is farther than 10 m");
        }

        [Test]
        public void TimedOut_OnlyWhileStealIsOnCooldown()
        {
            Assert.IsFalse(LootRules.TimedOut(11.9f, 12f, false), "not old enough");
            Assert.IsTrue(LootRules.TimedOut(12f, 12f, false), "old, Steal on cooldown: the mark moves");
            Assert.IsFalse(LootRules.TimedOut(30f, 12f, true), "old but Steal ready: the mark stays (Overdue)");
        }

        [Test]
        public void Advance_EveryStateOfTheLifeCycle_FollowsMaestrosRule()
        {
            const float limit = 12f;
            // Young: nothing moves it, whatever Steal does.
            Assert.AreEqual(LootStep.Stay, LootRules.Advance(LootState.Live, 5f, limit, true));
            Assert.AreEqual(LootStep.Stay, LootRules.Advance(LootState.Live, 5f, limit, false), "a young mark does not move when Steal is spent on someone else");
            // Old with Steal ready: it stays, as Overdue.
            Assert.AreEqual(LootStep.GoOverdue, LootRules.Advance(LootState.Live, limit, limit, true));
            Assert.AreEqual(LootStep.Stay, LootRules.Advance(LootState.Overdue, 40f, limit, true), "Overdue stays for as long as Steal is ready");
            // Old with Steal on cooldown: it moves, from Live or from Overdue (the 2026-11-24 correction).
            Assert.AreEqual(LootStep.Move, LootRules.Advance(LootState.Live, limit, limit, false));
            Assert.AreEqual(LootStep.Move, LootRules.Advance(LootState.Overdue, 13f, limit, false), "an overdue mark ends the moment Steal goes on cooldown");
            // A mark that is not live never steps.
            Assert.AreEqual(LootStep.Stay, LootRules.Advance(LootState.Waiting, 99f, limit, false));
            Assert.AreEqual(LootStep.Stay, LootRules.Advance(LootState.Off, 99f, limit, false));
        }

        [Test]
        public void MarkedGain_StolenFoodFirst_ThenTheBounty_BothHeldToTheFreeRoom()
        {
            LootRules.MarkedGain(5f, 1f, 20f, 6f, 10f, out float stolen, out float bounty, out float wanted);
            Assert.AreEqual(5f, stolen, 1e-5f, "an ordinary steal's take");
            Assert.AreEqual(10f, bounty, 1e-5f, "everything fits");
            Assert.AreEqual(15f, wanted, 1e-5f);

            LootRules.MarkedGain(5f, 1f, 12f, 6f, 10f, out stolen, out bounty, out wanted);
            Assert.AreEqual(5f, stolen, 1e-5f, "the stolen food comes first");
            Assert.AreEqual(7f, bounty, 1e-5f, "the bounty fills what room is left; the rest is lost");
            Assert.AreEqual(15f, wanted, 1e-5f);

            LootRules.MarkedGain(5f, 1f, 3f, 6f, 10f, out stolen, out bounty, out wanted);
            Assert.AreEqual(3f, stolen, 1e-5f, "held to the Thief's free room: the bird loses only what the Thief gains");
            Assert.AreEqual(0f, bounty, 1e-5f);

            LootRules.MarkedGain(5f, 1f, 0f, 6f, 10f, out stolen, out bounty, out _);
            Assert.AreEqual(0f, stolen + bounty, "a full Thief gains nothing from a marked steal");

            LootRules.MarkedGain(5f, 1.6f, 20f, 3f, 4f, out stolen, out bounty, out wanted);
            Assert.AreEqual(3f, stolen, 1e-5f, "held to what the bird carries");
            Assert.AreEqual(4f, bounty, 1e-5f, "an empty or poor bird still pays the bounty");
            Assert.AreEqual(7f, wanted, 1e-5f);

            LootRules.MarkedGain(5f, 1.6f, 20f, 20f, 0f, out stolen, out bounty, out _);
            Assert.AreEqual(8f, stolen, 1e-5f, "nominal times the Thief's factor");
        }

        // ---------------------------------------------------------------- the simulator

        private static MatchPassive LootPassive(float bounty = 10f, float seconds = 12f, float lead = 1f, int prize = 0)
        {
            var p = MatchPassive.Identity();
            p.MatchEndBonusFood = prize;
            p.Loot = new LootMarkFacts
            {
                IsThief = true, Bounty = bounty, Seconds = seconds, LeadSeconds = lead, OverFraction = 0.5f,
                Weights = new LootWeightTable { Base = 1f },
            };
            return p;
        }

        private static MatchKit Thief(MatchPassive passive, float radius = Big) =>
            MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(radius, 5f, StealCooldown) }, passive);

        private static MatchKit Idle(string label = "rival", params MatchAbility[] abilities) =>
            MatchTestKit.Fighter(label, 100f, abilities);

        // Rule v2 (2026-11-26): a mark lands only on a rival who carries food and is not inside his own base's wall. The simulator tests below hand every
        // rival a full beak at GO and switch its deposit off, so the mark mechanics (timing, timeout, shell, takes) are measured on birds that are markable.
        private const float RivalFood = 60f;
        private const float StolenPerMark = 5f;   // MatchTestKit.Steal(_, 5f, _) times the identity passive's factor

        private static MatchSetup Loaded(MatchSetup setup, float food = RivalFood)
        {
            setup.StartCargo = new float[setup.Seats.Length];
            for (int i = 1; i < setup.Seats.Length; i++)
            {
                if (setup.Seats[i] == null) continue;
                setup.StartCargo[i] = food;
                setup.Seats[i].Farm.DepositRate = 0f;
            }
            return setup;
        }

        // The v2 numbers on top of LootPassive's hand-built facts (every one left at 0 is the old rule, which the tests above rely on).
        private static MatchPassive Rule(MatchPassive p, float minCargo = 0f, float homeRadius = 0f, float homeMinWeight = 0.05f, float keepCargo = 0f,
            float slack = 3f, float opening = 0f, float gap = 0f, float fallback = 0f, float stall = 0f)
        {
            p.Loot.StallSeconds = stall;
            p.Loot.MinCargo = minCargo;
            p.Loot.FallbackMinCargo = fallback;
            p.Loot.HomeRadius = homeRadius;
            p.Loot.HomeMinWeight = homeMinWeight;
            p.Loot.KeepCargo = keepCargo;
            p.Loot.SwitchSlackMeters = slack;
            p.Loot.OpeningSeconds = opening;
            p.Loot.RedrawGapSeconds = gap;
            return p;
        }

        private delegate void Watch(MatchView view, int seat);

        // Stands still, watches, and casts slot 0 when `when(time)` says so and the slot is ready.
        private static PolicyFactory Caster(Func<float, bool> when, Watch watch = null) =>
            MatchTestKit.Policy((v, s) =>
            {
                watch?.Invoke(v, s);
                var intent = MatchTestKit.StandAt(v, s);
                if (when(v.Time) && v.Seats[s].CooldownLeft[1] <= 1e-4f) intent.CastSlot = 0;
                return intent;
            });

        private static MatchSetup Arena(MatchKit thief, int rivals, float clock, MatchKit rival = null)
        {
            var map = MatchTestKit.OpenMap(Half);
            var kits = new MatchKit[4];
            kits[0] = thief;
            for (int i = 1; i <= rivals; i++) kits[i] = rival ?? Idle("rival" + i);
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = clock;
            setup.RecordEvents = true;
            return Loaded(setup);
        }

        private static List<MatchEvent> Loots(MatchResult r) => r.Events.Where(e => e.Kind == MatchEventKind.Loot).ToList();

        [Test]
        public void MarkedSteal_PaysTheStolenFoodThenTheBounty_IntoTheCargo()
        {
            const float bounty = 7f;
            // ONE rival: the Steal is an area steal (Big), so with more rivals every cast would also take 5 from each unmarked rival into the cargo.
        // One rival is the only possible victim, so each cast takes 5 from him, adds the 7 bounty, and the cargo peaks at 5 + 7 = 12.
        var setup = Arena(Thief(LootPassive(bounty)), 1, 7f);   // marks at 0 s and 4 s; the second 12 food bank by 6.4 s
            float maxCargo = 0f;
            int markedBy = -2;
            setup.Policies[0] = Caster(t => true, (v, s) =>
            {
                maxCargo = Mathf.Max(maxCargo, v.Seats[s].Cargo);
                int target = v.Seats[s].LootTarget;
                if (target >= 0) markedBy = v.Seats[target].LootMarkedBy;
            });
            var r = MatchSim.Run(setup, 11);
            var c = r.Counters[0];
            Assert.GreaterOrEqual(c.LootTaken, 1, "a steal that reaches the marked rival consumes the mark");
            Assert.AreEqual(c.LootTaken * bounty, c.LootBountyFood, 1e-4f);
            Assert.AreEqual(c.LootTaken * StolenPerMark, c.LootTakeFood, 1e-4f, "v2: every marked rival carries food, so each mark takes an ordinary steal first");
            Assert.AreEqual(0f, c.LootLostToRoom, 1e-4f, "the beak had room");
            Assert.AreEqual(StolenPerMark + bounty, maxCargo, 0.5f, "the take and the bounty sat in the ordinary beak (banking at the deposit rate) before it banked");
            Assert.AreEqual(c.LootTaken * (StolenPerMark + bounty), c.LootBankedFood, 1e-3f, "the Thief stands in its base zone, so the loot banks at once");
            Assert.AreEqual(c.LootTaken * (StolenPerMark + bounty), r.Banked[0], 1e-3f);
            Assert.AreEqual(0, markedBy, "the marked rival's view says whose mark it is");
            Assert.AreEqual(c.LootTaken, Loots(r).Count);
            Assert.AreEqual(0, r.RejectedIntents);
        }

        // A Thief that walks away from its base and casts Steal whenever it is ready from `startAt` on, so what it gains stays in its beak.
        private static PolicyFactory Wanderer(Watch watch = null, float startAt = 1f) =>
            MatchTestKit.Policy((v, s) =>
            {
                watch?.Invoke(v, s);
                var intent = MatchIntent.Idle(Vector2.zero);
                if (v.Time >= startAt && v.Seats[s].CooldownLeft[1] <= 1e-4f) intent.CastSlot = 0;
                return intent;
            });

        [Test]
        public void AMarkedSteal_FillsTheBeakUpToCapacity_AndNoFurther()
        {
            // Capacity 12, a rival that carries 6, a bounty of 10: 5 stolen (an ordinary take), then 7 of the 10 fit, 3 are lost.
            const int capacity = 12;
            var map = MatchTestKit.OpenMap(Half);
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, LootPassive(10f), capacity: capacity);
            var rival = MatchTestKit.Fighter("rival", 100f, new MatchAbility[0], capacity: 100, peckAmount: 6f, peckCooldown: 50f, castSeconds: 0.1f);
            var setup = MatchTestKit.Duel(map, thief, rival, null, MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1)), Half, 3f);
            float maxCargo = 0f;
            setup.Policies[0] = Wanderer((v, s) => maxCargo = Mathf.Max(maxCargo, v.Seats[s].Cargo));
            var r = MatchSim.Run(setup, 2);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.LootTaken);
            Assert.AreEqual(5f, c.LootTakeFood, 1e-4f, "the stolen food first");
            Assert.AreEqual(7f, c.LootBountyFood, 1e-4f, "then the bounty into what room is left");
            Assert.AreEqual(3f, c.LootLostToRoom, 1e-4f, "what did not fit is lost, never held back");
            Assert.AreEqual(capacity, Loots(r)[0].Amount, 1e-4f);
            Assert.LessOrEqual(maxCargo, capacity + 1e-4f, "the beak never holds more than its capacity");
            Assert.AreEqual(capacity, maxCargo, 1e-3f);
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void AFullBeak_DrawsNoNewMark_SoNoBountyIsWastedOnIt()
        {
            // v2 rule 1: a full beak draws nothing (before v2 a full Thief was marked and gained nothing). Capacity 10, bounty 10: the first marked steal takes
            // 5 and fits 5 of the bounty (5 lost), the beak is full, and no second mark is drawn while it stays full.
            const int capacity = 10;
            var map = MatchTestKit.OpenMap(Half);
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, LootPassive(10f, lead: 0f), capacity: capacity);
            // ONE rival (area steal: with several, DoSteal could fill the beak from an unmarked one before the bounty, and the mark seat would matter).
            var kits = new[] { thief, Carrier("a"), null, null };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 11f;
            setup.RecordEvents = true;
            Loaded(setup);
            setup.Policies[1] = MatchTestKit.Stand;
            setup.Policies[0] = Wanderer();
            var r = MatchSim.Run(setup, 6);
            var loots = Loots(r);
            Assert.AreEqual(1, loots.Count, "one marked steal filled the beak; the next mark is never drawn");
            Assert.AreEqual(capacity, loots[0].Amount, 1e-4f, "the take and the bounty filled the beak");
            Assert.AreEqual(StolenPerMark, r.Counters[0].LootTakeFood, 1e-4f);
            Assert.AreEqual(capacity - StolenPerMark, r.Counters[0].LootBountyFood, 1e-4f);
            Assert.AreEqual(10f - capacity + StolenPerMark, r.Counters[0].LootLostToRoom, 1e-4f, "the bounty that did not fit is lost, as before");
            Assert.AreEqual(1, r.Counters[0].LootOffered, "no mark while the beak is full");
            Assert.Greater(r.Counters[0].LootSearchingSeconds, 0f, "lead 0: the draw is due only on the ticks Steal is ready (the Wanderer recasts at once), so the total is small but positive");
        }

        [Test]
        public void ARivalsSteal_TakesTheLootLikeAnyCargo()
        {
            var map = MatchTestKit.OpenMap(Half);
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, LootPassive(10f), capacity: 40);
            var rival = MatchTestKit.Fighter("rival", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, capacity: 100);
            var setup = Loaded(MatchTestKit.Duel(map, thief, rival, null, MatchTestKit.CastAt(2f, 0), Half, 4f));
            bool cast = false;
            setup.Policies[0] = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchIntent.Idle(Vector2.zero);
                if (!cast && v.Time >= 0.5f) { cast = true; intent.CastSlot = 0; }
                return intent;
            });
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(1, r.Counters[0].LootTaken);
            Assert.AreEqual(10f, r.Counters[0].LootBountyFood, 1e-4f);
            Assert.AreEqual(StolenPerMark, r.Counters[0].LootTakeFood, 1e-4f, "v2: the marked rival carried food, the mark took an ordinary steal first");
            Assert.AreEqual(5f, r.Counters[1].Stolen, 1e-4f, "the rival's Steal took the usual 5 from the Thief's beak");
            Assert.AreEqual(5f, r.Counters[0].LostToSteals, 1e-4f);
            Assert.AreEqual(5f, r.Counters[0].LootLostToSteals, 1e-4f, "all of the Thief's cargo is loot, so the theft took loot");
        }

        [Test]
        public void TheFirstMark_IsDrawnAtGo_AndTheNextOneAppearsLeadSecondsBeforeStealIsReady()
        {
            foreach (float lead in new[] { 0f, 1f, 2f })
            {
                var setup = Arena(Thief(LootPassive(lead: lead)), 3, 9f);
                float firstSeen = -1f, nextSeen = -1f;
                bool cast = false;
                setup.Policies[0] = Caster(t => !cast && (cast = t >= 0.2f), (v, s) =>
                {
                    bool marked = v.Seats[s].LootTarget >= 0;
                    if (marked && firstSeen < 0f) firstSeen = v.Time;
                    if (marked && cast && nextSeen < 0f && v.Time > 0.5f) nextSeen = v.Time;
                });
                var r = MatchSim.Run(setup, 3);
                Assert.AreEqual(1, r.Counters[0].LootTaken, $"lead {lead}: one cast, one marked steal");
                Assert.AreEqual(0f, firstSeen, Tick, $"lead {lead}: the first mark is drawn at GO");
                float castAt = Loots(r)[0].Time;
                float expected = Mathf.Max(castAt, castAt + StealCooldown - lead);
                Assert.AreEqual(expected, nextSeen, 2.5f * Tick, $"lead {lead}: the next mark lands lead seconds before Steal is ready");
            }
        }

        [Test]
        public void TheLastMarkedRival_IsNotMarkedAgain_WhileAnotherIsThere()
        {
            var setup = Arena(Thief(LootPassive(bounty: 1f, lead: 0f)), 3, 60f);
            setup.Rules.Farm.WinTarget = 10000f;   // v2 marked steals take food too (16 a cast): the default target of 40 would end the match after three
            setup.Policies[0] = Caster(t => true);
            var r = MatchSim.Run(setup, 5);
            var loots = Loots(r);
            Assert.GreaterOrEqual(loots.Count, 8);
            for (int i = 1; i < loots.Count; i++)
                Assert.AreNotEqual(loots[i - 1].Other, loots[i].Other, $"marked steal {i} repeats the last marked bird");
            Assert.AreEqual(3, loots.Select(e => e.Other).Distinct().Count(), "over a long match every rival carries it");
        }

        [Test]
        public void WithOneRival_TheOnlyRivalCanBeMarkedAgain()
        {
            var setup = Arena(Thief(LootPassive(bounty: 1f, lead: 0f)), 1, 20f);
            setup.Policies[0] = Caster(t => true);
            var r = MatchSim.Run(setup, 5);
            Assert.GreaterOrEqual(r.Counters[0].LootTaken, 3);
            Assert.IsTrue(Loots(r).All(e => e.Other == 1));
        }

        // A Steal that reaches nobody spends the cooldown and so shows when the mark moves (reach 1 m: the bases are far apart).
        private static (MatchResult result, List<(float time, int target)> seen) RunTimeoutArena(float[] castTimes, float clock)
        {
            var setup = Arena(Thief(LootPassive(seconds: 12f, lead: 1f), radius: 1f), 3, clock);
            var seen = new List<(float, int)>();
            int next = 0;
            setup.Policies[0] = Caster(t => next < castTimes.Length && t >= castTimes[next] && ++next > 0, (v, s) => seen.Add((v.Time, v.Seats[s].LootTarget)));
            return (MatchSim.Run(setup, 9), seen);
        }

        private static int TargetAt(List<(float time, int target)> seen, float t) => seen.Last(x => x.time <= t + 1e-4f).target;

        [Test]
        public void ACastOnOthersBeforeTheTimeout_DoesNotMoveTheMark()
        {
            var (r, seen) = RunTimeoutArena(new[] { 5f }, 11f);
            int mark = TargetAt(seen, 1f);
            Assert.GreaterOrEqual(mark, 1);
            Assert.AreEqual(mark, TargetAt(seen, 6f), "Steal cast and on cooldown, but the mark is young");
            Assert.AreEqual(mark, TargetAt(seen, 10.5f));
            Assert.AreEqual(0, r.Counters[0].LootTimeouts);
        }

        [Test]
        public void AfterTheTimeout_TheMarkMovesOnlyWhileStealIsOnCooldown()
        {
            // Overdue: Steal stays ready past the timeout, so the mark stays put however long it takes.
            var (idle, idleSeen) = RunTimeoutArena(new[] { 5f }, 30f);
            Assert.AreEqual(TargetAt(idleSeen, 1f), TargetAt(idleSeen, 29f), "Steal ready, mark overdue: it stays");
            Assert.AreEqual(0, idle.Counters[0].LootTimeouts);

            // A cast at 13 s (Steal ready since 9 s) puts Steal on cooldown with the mark overdue: it moves, unpaid.
            var (moved, seen) = RunTimeoutArena(new[] { 5f, 13f }, 30f);
            int first = TargetAt(seen, 1f);
            Assert.AreEqual(first, TargetAt(seen, 12.9f));
            Assert.AreEqual(1, moved.Counters[0].LootTimeouts);
            Assert.AreEqual(0, moved.Counters[0].LootTaken, "no pay for a mark that moved");
            float readyAt = 13f + StealCooldown;
            Assert.AreEqual(-1, TargetAt(seen, readyAt - 1.5f), "between the move and the next draw there is no mark");
            int second = TargetAt(seen, readyAt + 0.5f);
            Assert.GreaterOrEqual(second, 1, "the next mark lands a second before Steal is ready");
            Assert.AreNotEqual(first, second, "and not on the bird it just left");
        }

        // ---- an overdue mark and the Steal cooldown (Maestro 2026-11-24) ----
        // The synthetic map puts seat 0 at (32, 0), seat 1 at (0, 32), seat 2 at (-32, 0), seat 3 at (0, -32): a reach of 50 m covers seats 1 and 3, not seat 2.
        private const float ReachesTheNeighboursOnly = 50f;

        private static MatchKit Carrier(string label) =>
            MatchTestKit.Fighter(label, 100f, new MatchAbility[0], capacity: 100, peckAmount: 6f, peckCooldown: 50f, castSeconds: 0.1f);

        // A Thief that does not cast before `castAt` and then casts once; its mark is read back each tick. Without `shelled`, rivals 1 and 3 carry
        // food and rival 2 stands out of reach (all three start loaded, v2); with it, every rival is that kit and shells up at 6 s (its egg is up from 6.2 s to 14.2 s).
        private static (MatchResult result, List<(float time, int target)> seen) PlayOverdue(ulong seed, float castAt, float clock, MatchKit shelled = null)
        {
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(ReachesTheNeighboursOnly, 5f, StealCooldown) }, LootPassive(seconds: 12f, lead: 1f));
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { thief, shelled ?? Carrier("carrier1"), shelled ?? Idle("idle2"), shelled ?? Carrier("carrier3") };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = clock;
            setup.RecordEvents = true;
            Loaded(setup);   // v2: rival 2 must carry food too, or the mark could never sit on the rival the cast cannot reach
            if (shelled == null)
            {
                setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
                setup.Policies[3] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(3));
            }
            else
            {
                for (int i = 1; i <= 3; i++) setup.Policies[i] = MatchTestKit.CastAt(6f, 0);
            }
            var seen = new List<(float, int)>();
            bool cast = false;
            setup.Policies[0] = Caster(t => !cast && (cast = t >= castAt), (v, s) => seen.Add((v.Time, v.Seats[s].LootTarget)));
            return (MatchSim.Run(setup, seed), seen);
        }

        [Test]
        public void AnOverdueMark_EndsWhenStealIsSpentOnAnotherRival_AndTheNextMarkIsNotTheBirdJustLeft()
        {
            int checkedRuns = 0;
            for (ulong seed = 1; seed <= 80 && checkedRuns < 3; seed++)
            {
                // The first cast at 13 s: the mark (drawn at GO, 13 s old) is overdue with Steal ready. The cast reaches the carriers on seats 1 and 3.
                var (r, seen) = PlayOverdue(seed, 13f, 30f);
                int first = TargetAt(seen, 1f);
                if (first != 2) continue;   // the mark must sit on the rival the cast cannot reach
                checkedRuns++;
                Assert.AreEqual(2, TargetAt(seen, 12.9f), $"seed {seed}: overdue with Steal ready, the mark stays");
                Assert.Greater(r.Counters[0].Steals, 0, $"seed {seed}: the cast robbed a carrier that was not the marked bird");
                Assert.AreEqual(0, r.Counters[0].LootTaken, $"seed {seed}: the marked bird was out of reach: no pay");
                Assert.AreEqual(1, r.Counters[0].LootTimeouts, $"seed {seed}: Steal went on cooldown with the mark overdue: it ends");
                float readyAt = 13f + StealCooldown;
                Assert.AreEqual(-1, TargetAt(seen, readyAt - 1.5f), $"seed {seed}: no mark between the end and the next draw");
                int second = TargetAt(seen, readyAt + 0.5f);
                Assert.GreaterOrEqual(second, 1, $"seed {seed}: the next mark appears a second before Steal is ready");
                Assert.AreNotEqual(2, second, $"seed {seed}: and never on the bird it just left");
                Assert.AreEqual(0, r.RejectedIntents);
            }
            Assert.GreaterOrEqual(checkedRuns, 1, "no seed marked the far rival: the test would be vacuous");
        }

        [Test]
        public void AnOverdueMark_ConsumedOnItsBird_IsTheNormalPaidPath()
        {
            int checkedRuns = 0;
            for (ulong seed = 1; seed <= 80 && checkedRuns < 3; seed++)
            {
                var (r, seen) = PlayOverdue(seed, 13f, 30f);
                int first = TargetAt(seen, 1f);
                if (first != 1 && first != 3) continue;   // the mark sits on a rival the cast reaches
                checkedRuns++;
                Assert.AreEqual(first, TargetAt(seen, 12.9f), $"seed {seed}: overdue, Steal ready: it stays");
                Assert.AreEqual(1, r.Counters[0].LootTaken, $"seed {seed}: the cast covers the marked bird: the mark is consumed");
                Assert.AreEqual(0, r.Counters[0].LootTimeouts, $"seed {seed}: and it did not move");
                Assert.AreNotEqual(first, TargetAt(seen, 13f + StealCooldown + 0.5f), $"seed {seed}: the next mark is on another bird");
            }
            Assert.GreaterOrEqual(checkedRuns, 1, "no seed marked a reachable rival: the test would be vacuous");
        }

        [Test]
        public void AnOverdueMark_OnAShelledBird_IsRefused_ButTheCooldownIsSpent_SoItMoves()
        {
            var shell = MatchTestKit.Fighter("shell", 100f, new[] { MatchTestKit.Shield(0.2f, 1f, 8f, 30f) });
            var (r, seen) = PlayOverdue(7, 13f, 30f, shell);
            int first = TargetAt(seen, 1f);
            Assert.GreaterOrEqual(first, 1);
            Assert.AreEqual(first, TargetAt(seen, 12.9f), "Steal ready: the overdue mark stays");
            Assert.AreEqual(1, r.Counters[0].LootRefused, "the shell refuses the cast on the marked bird");
            Assert.AreEqual(0, r.Counters[0].LootTaken);
            Assert.AreEqual(1, r.Counters[0].LootTimeouts, "the refused cast spent Steal: the overdue mark ends");
            Assert.AreNotEqual(first, TargetAt(seen, 13f + StealCooldown + 0.5f), "and the next one is another bird");
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void ARemovedMarkedRival_EndsTheMark_WithoutPay()
        {
            int checkedOnVictim = 0, checkedOnOther = 0;
            for (ulong seed = 1; seed <= 40 && (checkedOnVictim < 2 || checkedOnOther < 2); seed++)
            {
                var map = MatchTestKit.OpenMap(Half);
                var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { MatchTestKit.DeathMark(1f, 4f, Big, 8f, 4f, 3f) });
                var loaded = MatchTestKit.Fighter("loaded", 100f, new MatchAbility[0]);
                var kits = new[] { Thief(LootPassive(), 1f), spoiler, loaded, null };
                var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
                setup.RecordEvents = true;
                setup.Rules.Farm.ClockSeconds = 6f;
                // v2: both rivals must carry food to be marked at GO, or the mark could only ever sit on the loaded one. The spoiler holds 1 and the loaded
                // rival 20 (and pecks on), so "Loaded" still picks seat 2 as the forced victim.
                setup.StartCargo = new[] { 0f, 1f, 20f, 0f };
                spoiler.Farm.DepositRate = 0f;
                loaded.Farm.DepositRate = 0f;
                setup.Forced = new ForcedKills { Spoiler = 1, Kills = 1, Victims = ForcedVictims.Loaded, Times = new[] { 3f } };
                setup.Policies[2] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(2));
                int markAtKill = -1;
                setup.Policies[0] = MatchTestKit.Policy((v, s) =>
                {
                    if (v.Time < 3f - 1e-4f) markAtKill = v.Seats[s].LootTarget;
                    return MatchTestKit.StandAt(v, s);
                });
                var r = MatchSim.Run(setup, seed);
                Assert.AreEqual(1, r.Kills[1], "the forced kill landed");
                var kill = r.Events.First(e => e.Kind == MatchEventKind.Kill);
                Assert.AreEqual(2, kill.Other, "the loaded rival is the victim");
                Assert.AreEqual(0, r.Counters[0].LootTaken, "nobody cast: no pay");
                if (markAtKill == 2)
                {
                    checkedOnVictim++;
                    Assert.AreEqual(1, r.Counters[0].LootTimeouts, $"seed {seed}: the mark ended with its bird");
                }
                else
                {
                    checkedOnOther++;
                    Assert.AreEqual(0, r.Counters[0].LootTimeouts, $"seed {seed}: a kill elsewhere leaves the mark alone");
                }
            }
            Assert.GreaterOrEqual(checkedOnVictim, 2, "the seeds never marked the victim: the test would be vacuous");
            Assert.GreaterOrEqual(checkedOnOther, 2, "the seeds always marked the victim: the control did not run");
        }

        [Test]
        public void AShelledMarkedRival_RefusesTheSteal_TheMarkStays_TheCooldownIsSpent()
        {
            var shielded = Idle("shell", MatchTestKit.Shield(0.2f, 1f, 8f, 30f));
            var setup = Arena(Thief(LootPassive()), 3, 6f, shielded);
            int before = -2, after = -2;
            float castAt = 1f;
            bool cast = false;
            setup.Policies[0] = Caster(t => !cast && (cast = t >= castAt), (v, s) =>
            {
                if (v.Time < castAt - 1e-4f) before = v.Seats[s].LootTarget;
                else if (v.Time >= castAt + 0.5f && v.Time < castAt + 1f) after = v.Seats[s].LootTarget;
            });
            for (int i = 1; i <= 3; i++) setup.Policies[i] = MatchTestKit.CastAt(0.3f, 0);
            var r = MatchSim.Run(setup, 4);
            Assert.AreEqual(1, r.Counters[0].LootRefused);
            Assert.AreEqual(0, r.Counters[0].LootTaken);
            Assert.GreaterOrEqual(before, 1);
            Assert.AreEqual(before, after, "a refused cast does not move the mark");
            Assert.AreEqual(0, r.RejectedIntents);
        }

        // ---- rule v2 in the simulator: the same pure rules as the game, on the simulator's own positions and cargo ----

        private static List<(float time, int target)> WatchMark(MatchSetup setup, Func<float, bool> cast = null)
        {
            var seen = new List<(float, int)>();
            setup.Policies[0] = cast != null
                ? Caster(cast, (v, s) => seen.Add((v.Time, v.Seats[s].LootTarget)))
                : MatchTestKit.Policy((v, s) => { seen.Add((v.Time, v.Seats[s].LootTarget)); return MatchTestKit.StandAt(v, s); });
            return seen;
        }

        [Test]
        public void NoMarkIsDrawn_WhileNoRivalCarriesFood()
        {
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { Thief(LootPassive()), Idle("a"), Idle("b"), Idle("c") };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 5f;
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(0, r.Counters[0].LootOffered, "every beak is empty: nobody is eligible");
            Assert.Greater(r.Counters[0].LootSearchingSeconds, 4f, "the draw was due from GO and found nobody");
        }

        [Test]
        public void ARivalInsideHisOwnBaseWall_IsNotACandidate()
        {
            var setup = Arena(Thief(LootPassive()), 1, 4f);   // the rival stands at his spawn, 1.5 m from his base centre
            setup.Rules.KeepOutRadius = 2.5f;
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(0, r.Counters[0].LootOffered, "inside the wall no ability reaches him: he is not marked");
            Assert.Greater(r.Counters[0].LootSearchingSeconds, 3f);
        }

        [Test]
        public void AMarkedBirdThatCrossesIntoHisBase_EndsTheMarkAsHome()
        {
            var setup = Arena(Thief(LootPassive(), 1f), 1, 6f);
            setup.Rules.KeepOutRadius = 2.5f;
            Vector2 baseCentre = setup.Map.Places[OracleMapSO.BaseIndex(1)].Pos;
            setup.Policies[1] = MatchTestKit.Policy((v, s) => MatchIntent.Idle(v.Time < 3f ? Vector2.zero : baseCentre));
            var seen = WatchMark(setup);
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(1, TargetAt(seen, 2.5f), "the bird left his base and carried the mark while walking about");
            Assert.AreEqual(1, r.Counters[0].LootEndedHome, "he crossed into his wall: the mark ends, it is not kept hoping he comes out");
            Assert.AreEqual(0, r.Counters[0].LootTimeouts);
            Assert.AreEqual(-1, TargetAt(seen, 5.5f), "and he is not eligible again while he stays inside");
        }

        [Test]
        public void ABirdThatEmpties_EndsTheMark_IsNotMarkedNext_AndTheRedrawGapHolds()
        {
            // Seat 1 starts with 10 and deposits it (5 per second): the mark drawn on him at GO ends "empty" at 2 s. Seat 2 starts empty and pecks, so it
            // is eligible from 0.6 s, but the next mark waits the 1.5 s redraw gap and does not go back to seat 1 (never twice in a row).
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { Thief(Rule(LootPassive(), gap: 1.5f)), Idle("depositor"), Idle("picker"), null };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 6f;
            setup.StartCargo = new[] { 0f, 10f, 0f, 0f };
            setup.Policies[2] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(2));
            var seen = WatchMark(setup);
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(1, TargetAt(seen, 1f), "seat 1 was the only bird with food at GO");
            Assert.AreEqual(1, r.Counters[0].LootEndedEmpty);
            Assert.AreEqual(0, r.Counters[0].LootTimeouts);
            Assert.AreEqual(-1, TargetAt(seen, 3f), "the redraw gap: no mark in the 1.5 s after the unpaid end");
            Assert.AreEqual(2, TargetAt(seen, 4f), "then the next bird, not the one just left");
            Assert.AreEqual(2, r.Counters[0].LootOffered);
        }

        [Test]
        public void ALightMarkedBird_WithStealReady_GivesTheMarkToABirdNoFartherPlusTheSlack_WithNoGap()
        {
            // Seat 1 carries 5 (light: the keep cargo is 8) and is marked at GO, the only bird with food. Steal is ready, so the mark is kept until seat 3
            // starts carrying (its peck lands near 0.6 s): it is as far from the Thief as seat 1, so the mark moves there straight away, despite a 5 s gap.
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { Thief(Rule(LootPassive(), keepCargo: 8f, gap: 5f)), Idle("light"), null, Idle("picker") };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 4f;
            setup.StartCargo = new[] { 0f, 5f, 0f, 0f };
            kits[1].Farm.DepositRate = 0f;
            setup.Policies[3] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(3));
            var seen = WatchMark(setup);
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(1, TargetAt(seen, 0.2f), "light but Steal is ready and nobody else carries: the mark is kept");
            Assert.AreEqual(3, TargetAt(seen, 2f), "the mark moved straight to the bird that started carrying");
            Assert.AreEqual(1, r.Counters[0].LootEndedLight);
            Assert.AreEqual(2, r.Counters[0].LootOffered);
            Assert.AreEqual(0, r.Counters[0].LootTimeouts);
        }

        [Test]
        public void ALightMarkedBird_WhileStealIsOnCooldown_EndsTheMark()
        {
            // Seat 1 carries 5 (light against a keep cargo of 8). Steal is ready at GO, so the mark is kept; the cast at 0.5 s reaches nobody (1 m) but spends
            // Steal: the light bird loses the mark, and there is nobody else to give it to.
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { Thief(Rule(LootPassive(), keepCargo: 8f), 1f), Idle("light"), null, null };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 2f;
            setup.StartCargo = new[] { 0f, 5f, 0f, 0f };
            kits[1].Farm.DepositRate = 0f;
            var seen = WatchMark(setup, t => t >= 0.5f);
            var r = MatchSim.Run(setup, 3);
            Assert.AreEqual(1, TargetAt(seen, 0.3f), "light, Steal ready, nobody closer: kept");
            Assert.AreEqual(1, r.Counters[0].LootEndedLight, "Steal is on cooldown: the light bird loses the mark");
            Assert.AreEqual(-1, TargetAt(seen, 1.5f), "and nobody else carries food to take it");
            Assert.AreEqual(0, r.Counters[0].LootTimeouts);
        }

        [Test]
        public void WhenNobodyCarriesTheMinimum_TheLastResortMarksAnyBirdWithFood_AndWithoutItNobody()
        {
            // The only rival carries 2 (under the minimum of 6). With a fallback floor of 1 he is marked at GO (Steal is ready); with the fallback off nobody is.
            MatchSetup Build(float fallback)
            {
                var map = MatchTestKit.OpenMap(Half);
                var kits = new[] { Thief(Rule(LootPassive(), minCargo: 6f, fallback: fallback)), Idle("poor"), null, null };
                var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
                setup.Rules.Farm.ClockSeconds = 1f;
                setup.StartCargo = new[] { 0f, 2f, 0f, 0f };
                kits[1].Farm.DepositRate = 0f;
                return setup;
            }
            Assert.GreaterOrEqual(MatchSim.Run(Build(1f), 3).Counters[0].LootOffered, 1, "the last resort takes the bird with 2");
            Assert.AreEqual(0, MatchSim.Run(Build(0f), 3).Counters[0].LootOffered, "no fallback: nobody reaches the minimum, so no mark");
        }

        [Test]
        public void WhenEveryoneIsEmptyForTheStallSeconds_TheMarkLandsOnARandomRival_AndStaysWhileHisBeakIsEmpty()
        {
            MatchSetup Build(float stall)
            {
                var map = MatchTestKit.OpenMap(Half);
                var kits = new[] { Thief(Rule(LootPassive(), minCargo: 6f, fallback: 1f, stall: stall)), Idle("empty"), null, null };
                var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
                setup.Rules.Farm.ClockSeconds = 8f;
                setup.Rules.Farm.WinTarget = 10000f;
                kits[1].Farm.DepositRate = 0f;
                return setup;
            }
            var withStall = Build(2f);
            var seen = WatchMark(withStall);
            var r = MatchSim.Run(withStall, 3);
            Assert.AreEqual(-1, TargetAt(seen, 1.5f), "nobody carries anything and the stall clock has not run out");
            Assert.AreEqual(1, TargetAt(seen, 3f), "after the stall seconds a random rival (the only one) is marked, empty beak included");
            Assert.AreEqual(1, TargetAt(seen, 7f), "and the mark stays on him while his beak is empty");
            Assert.AreEqual(0, r.Counters[0].LootEndedEmpty);
            Assert.AreEqual(0, MatchSim.Run(Build(0f), 3).Counters[0].LootOffered, "stall off: nobody carries anything, so no mark");
        }

        [Test]
        public void Keep_ASpeculativeMarkOnAnEmptyBird_IsNotEnded_ButHomeStillEndsIt()
        {
            Assert.AreEqual(LootEnd.Empty, LootRules.Keep(false, false, 0f, 3f, true, false));
            Assert.AreEqual(LootEnd.None, LootRules.Keep(false, false, 0f, 3f, true, false, speculative: true));
            Assert.AreEqual(LootEnd.Home, LootRules.Keep(false, true, 0f, 3f, true, false, speculative: true));
            Assert.IsTrue(LootRules.Eligible(false, false, false, false, 0f, 6f, false, false, needsFood: false), "the stalled draw takes an empty bird");
            Assert.IsFalse(LootRules.Eligible(false, false, false, false, 0f, 6f, false, false), "the ordinary draw does not");
        }

        [Test]
        public void TheFirstMark_WaitsForTheOpeningSeconds()
        {
            var setup = Arena(Thief(Rule(LootPassive(), opening: 2f)), 3, 5f);
            var seen = WatchMark(setup);
            MatchSim.Run(setup, 3);
            Assert.AreEqual(-1, TargetAt(seen, 1.9f));
            Assert.GreaterOrEqual(TargetAt(seen, 2.2f), 1, "drawn as soon as the opening seconds are over");
        }

        [Test]
        public void TheClockPrize_IsTheThiefs_PaidAtTheClockBeforeTheWinnerIsPicked()
        {
            const int prize = 20;
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Duel(map, Idle("rival"), Thief(LootPassive(prize: prize), 1f), MatchTestKit.Stand, MatchTestKit.Stand, Half, 4f);
            var r = MatchSim.Run(setup, 1);
            Assert.IsTrue(r.NobodyReachedTarget);
            Assert.AreEqual(prize, r.Banked[1], 1e-4f, "banked at the clock, not carried");
            Assert.AreEqual(0f, r.Banked[0]);
            Assert.AreEqual(1, r.Winner, "the prize decides a clock that nobody banked in");
            Assert.AreEqual(0, r.WinnerWithoutSpoilerBonus, "without any end-of-clock bonus the corner rule would have picked seat 0");
        }

        [Test]
        public void MechanicOff_BountyZero_PlaysExactlyAsABirdWithNoLootFacts()
        {
            MatchResult Play(MatchPassive passive)
            {
                var rival = Idle("farmer");
                var setup = Arena(Thief(passive), 3, 20f, rival);
                setup.Policies[0] = Caster(t => true);
                for (int i = 1; i <= 3; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(i));
                return MatchSim.Run(setup, 21);
            }
            var plain = Play(MatchPassive.Identity());
            var off = Play(LootPassive(bounty: 0f));
            Assert.AreEqual(0, off.Counters[0].LootOffered, "no mark is ever drawn");
            CollectionAssert.AreEqual(plain.Banked, off.Banked);
            Assert.AreEqual(plain.Winner, off.Winner);
            Assert.AreEqual(plain.EndSeconds, off.EndSeconds);
            for (int s = 0; s < 4; s++)
            {
                Assert.AreEqual(plain.Counters[s].Stolen, off.Counters[s].Stolen, $"seat {s}");
                Assert.AreEqual(plain.Counters[s].Steals, off.Counters[s].Steals, $"seat {s}");
            }
            Assert.Greater(plain.Counters[0].Stolen, 0f, "the control stole something, so the equality is not vacuous");
        }

        [Test]
        public void TheThiefsCapacity_ComesFromThePassive_AndTheSpoilersIsHisOwnFloor_NeitherMovesTheOther()
        {
            var inputs = ShippedInputs();
            using (var book = new BalanceBook(new QuietLog(), inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                var shipped = MatchContext.From(inputs, OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
                int thief = Array.FindIndex(shipped.Kits, k => k.Passive.Loot.IsThief);
                int spoiler = Array.FindIndex(shipped.Kits, MatchDesigns.HoldsDeathMark);
                Assert.GreaterOrEqual(thief, 0);
                Assert.GreaterOrEqual(spoiler, 0);
                float spoilerCapacity = shipped.Kits[spoiler].Farm.CargoCapacity;
                // A floor above whatever the Thief carries as shipped: it must raise his capacity, and only his.
                float floor = shipped.Kits[thief].Farm.CargoCapacity + 4f;
                var moved = MatchWhatIf.ContextFor(inputs, Delta(("Thief", "MinimumCapacity", floor.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius(), out _, out string reason);
                Assert.IsNotNull(moved, reason);
                Assert.AreEqual(floor, moved.Kits[thief].Farm.CargoCapacity, "the floor on the Thief's passive raises his capacity");
                Assert.AreEqual(spoilerCapacity, moved.Kits[spoiler].Farm.CargoCapacity, "the Thief's floor does not move the Spoiler's: each is on its own passive, the shared class stat is untouched");
            }
        }

        // ---------------------------------------------------------------- the shipped data

        private sealed class QuietLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private static OracleInputs ShippedInputs()
        {
            var map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return new OracleInputs
            {
                Map = map,
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
        }

        private static string Delta(params (string asset, string field, string value)[] edits)
        {
            var assets = edits.GroupBy(e => e.asset).Select(g =>
                "\"" + g.Key + "\":{" + string.Join(",", g.Select(e => "\"" + e.field + "\":" + e.value)) + "}");
            return "{\"schema\":1,\"assets\":{" + string.Join(",", assets) + "}}";
        }

        [Test]
        public void TheShippedThief_HasTheLootMark_AndTheClockPrize_AndNoOtherKitHasAPrize()
        {
            var context = MatchContext.From(ShippedInputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            var thief = context.Kits.Single(k => k.Passive.Loot.IsThief);
            Assert.IsTrue(thief.Passive.Loot.Active, "the loot mark is on in the shipped game (Maestro approved it after the simulator runs)");
            Assert.Greater(thief.Passive.MatchEndBonusFood, 0f, "the Thief's clock prize is shipped");
            Assert.AreEqual(1, context.Kits.Count(k => k.Passive.MatchEndBonusFood > 0f), "the Thief's is the only end-of-clock prize: the Spoiler's went with the loot mark");
            Assert.IsTrue(context.Kits.Where(k => !k.Passive.Loot.IsThief).All(k => !k.Passive.Loot.Active), "no other kit has a mark");
        }

        [Test]
        public void TheLootFields_AreReadableThroughTheBook_AndDeltaSweepable()
        {
            var inputs = ShippedInputs();
            using (var book = new BalanceBook(new QuietLog(), inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                foreach (var field in new[] { "LootBounty", "LootSeconds", "LootLeadSeconds", "LootBaseWeight", "LootBankedWeight", "LootCarriedWeight", "LootOverFraction", "LootOverBonus", "ClockPrize", "MinimumCapacity" })
                    Assert.IsTrue(book.TryProbe("Thief", field, out _, out _, out _, out string problem), $"{field}: {problem}");

                string delta = Delta(("Thief", "LootBounty", "9"), ("Thief", "ClockPrize", "13"), ("Thief", "MinimumCapacity", "15"), ("Thief", "LootSeconds", "8"),
                    ("Thief", "StealMultiplier", "1"), ("Spoiler", "BonusFood", "0"));
                var context = MatchWhatIf.ContextFor(inputs, delta, OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius(), out _, out string reason);
                Assert.IsNotNull(context, reason);
                var thief = context.Kits.Single(k => k.Passive.Loot.IsThief);
                Assert.IsTrue(thief.Passive.Loot.Active);
                Assert.AreEqual(9f, thief.Passive.Loot.Bounty);
                Assert.AreEqual(8f, thief.Passive.Loot.Seconds);
                Assert.AreEqual(15f, thief.Farm.CargoCapacity);
                Assert.AreEqual(13f, thief.Passive.MatchEndBonusFood);
                Assert.AreEqual(1f, thief.Passive.StealFactor);
                Assert.AreEqual(1, context.Kits.Count(k => k.Passive.MatchEndBonusFood > 0f), "the Spoiler's prize is gone, the Thief's is paid");

                Assert.IsNull(MatchWhatIf.ContextFor(inputs, Delta(("Thief", "LootBounty", "-1")), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius(), out _, out reason),
                    "a negative bounty is outside [Min]");
                Assert.IsFalse(string.IsNullOrEmpty(reason));
            }
        }

        [Test]
        public void WithTheLootMarkOn_P0AndP1_DrawAndConsumeMarks_AndNeverAskForTheImpossible()
        {
            var inputs = ShippedInputs();
            using (var book = new BalanceBook(new QuietLog(), inputs.Abilities, inputs.Classes, inputs.Match, inputs.Map))
            {
                inputs.Book = book;
                var context = MatchWhatIf.ContextFor(inputs, Delta(("Thief", "LootBounty", "10"), ("Thief", "ClockPrize", "20"), ("Thief", "StealMultiplier", "1"), ("Spoiler", "BonusFood", "0")),
                    OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius(), out _, out string reason);
                Assert.IsNotNull(context, reason);
                int thief = Array.FindIndex(context.Kits, k => k.Passive.Loot.IsThief);
                foreach (var population in new[] { PolicyPopulation.P0, PolicyPopulation.P1, PolicyPopulation.Mixed })
                    foreach (var design in new[] { MatchDesignKind.Mixed, MatchDesignKind.SeatCheck, MatchDesignKind.OnePerClass })
                    {
                        var run = MatchRunner.Run(context, new MatchRunSpec { Design = design, Replicates = 2, Population = population, RunSeed = 5UL, Threads = 4 });
                        int drawn = 0, taken = 0;
                        for (int i = 0; i < run.Results.Length; i++)
                        {
                            Assert.AreEqual(0, run.Results[i].RejectedIntents, $"{population} {design} match {i}");
                            int seat = Array.IndexOf(run.Cells[i].KitOfSeat, thief);
                            if (seat < 0) continue;
                            drawn += run.Results[i].Counters[seat].LootOffered;
                            taken += run.Results[i].Counters[seat].LootTaken;
                        }
                        // SeatCheck seats four Thieves, who live off marks and forage nothing: nobody carries food, so v2 draws no mark there.
                        if (design != MatchDesignKind.Mixed) continue;
                        Assert.Greater(drawn, 0, $"{population} {design}: no mark was ever drawn");
                        if (design == MatchDesignKind.Mixed) Assert.Greater(taken, 0, $"{population} {design}: no mark was ever consumed (a wiring smell, not a balance finding)");
                    }
            }
        }

        // ---------------------------------------------------------------- the report

        private static MatchResult Result(int winner, int winnerWithout, bool clock, int seat, float taken, float bounty, float bagBanked, float banked)
        {
            var counters = new SeatCounters[4];
            for (int i = 0; i < 4; i++) counters[i] = new SeatCounters { StolenFromSeat = new float[4] };
            counters[seat].LootTakeFood = taken;
            counters[seat].LootBountyFood = bounty;
            counters[seat].LootBankedFood = bagBanked;
            var b = new float[4];
            b[seat] = banked;
            return new MatchResult { Winner = winner, WinnerWithoutSpoilerBonus = winnerWithout, NobodyReachedTarget = clock, Banked = b, Counters = counters };
        }

        [Test]
        public void LootStealsToWin_CountsWinsWithAndWithoutThePrize_AndLabelsAgainstTheTargets()
        {
            var played = new List<(MatchResult, int)>
            {
                (Result(2, 2, false, 2, 4f, 10f, 10f, 40f), 2),   // won on the target
                (Result(2, 0, true, 2, 4f, 10f, 14f, 48f), 2),    // won only on the prize
                (Result(1, 1, false, 2, 0f, 10f, 0f, 12f), 2),    // lost
                (Result(2, 2, false, 2, 4f, 10f, 14f, 41f), 2),   // won
            };
            var targets = new List<ThiefLootTarget>
            {
                new ThiefLootTarget { Steals = 3, WithPassive = false, MinWinRate = 0.5f, MaxWinRate = 1f },
                new ThiefLootTarget { Steals = 3, WithPassive = true, MinWinRate = 0f, MaxWinRate = 0.4f },
            };
            var row = MatchStats.LootStealsToWin(played, 3, targets);
            Assert.AreEqual(4, row.Win.Trials);
            Assert.AreEqual(2f / 4f, row.Win.Rate, 1e-6f, "without the prize: the target decided two of four");
            Assert.AreEqual(3f / 4f, row.WinWithPrize.Rate, 1e-6f);
            Assert.AreEqual(0.25f, row.NobodyReachedTarget, 1e-6f);
            Assert.AreEqual(35.25f, row.MeanFinal, 1e-4f);
            Assert.AreEqual((10f + 14f + 0f + 14f) / (14f + 14f + 10f + 14f), row.BankedShare, 1e-4f);
            StringAssert.Contains("without prize IN", row.Label);
            StringAssert.Contains("with prize OUT", row.Label);
            Assert.IsNull(MatchStats.LootStealsToWin(played, 1, targets).Label, "no target for one steal");
            StringAssert.Contains("NO DATA", MatchStats.LootStealsToWin(new List<(MatchResult, int)>(), 3, targets).Label);
        }

        [Test]
        public void ReferenceAsset_ThiefLootTargets_AreWellFormed_AndTheSpoilersTargetsAreUntouched()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OracleReferenceSO>(OracleMenu.ReferencePath);
            Assert.IsNotNull(asset);
            Assert.IsNotNull(asset.ThiefLootTargets);
            foreach (var t in asset.ThiefLootTargets)
            {
                string who = $"{t.Steals} steals, withPassive {t.WithPassive}";
                Assert.That(t.Steals, Is.InRange(0, 8), who);
                Assert.That(t.MinWinRate, Is.InRange(0f, 1f), who);
                Assert.That(t.MaxWinRate, Is.InRange(0f, 1f), who);
                Assert.LessOrEqual(t.MinWinRate, t.MaxWinRate, who);
            }
            var keys = asset.ThiefLootTargets.Select(t => (t.Steals, t.WithPassive)).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "two rows for the same (steals, with prize)");
            Assert.IsNotEmpty(asset.KillTargets, "the Spoiler's list is still there");
        }
    }
}
