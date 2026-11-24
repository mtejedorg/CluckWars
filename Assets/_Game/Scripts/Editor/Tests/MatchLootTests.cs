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
            CollectionAssert.AreEqual(new[] { 2 }, list, "the only rival can be marked again");
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
            return setup;
        }

        private static List<MatchEvent> Loots(MatchResult r) => r.Events.Where(e => e.Kind == MatchEventKind.Loot).ToList();

        [Test]
        public void MarkedSteal_PaysTheBounty_IntoTheCargo_EvenOffAnEmptyBird()
        {
            const float bounty = 7f;
            var setup = Arena(Thief(LootPassive(bounty)), 3, 6f);
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
            Assert.AreEqual(0f, c.LootTakeFood, "the rivals carry nothing: nothing to take, the bounty paid anyway");
            Assert.AreEqual(0f, c.LootLostToRoom, 1e-4f, "the beak had room");
            Assert.AreEqual(bounty, maxCargo, 0.5f, "the bounty sat in the ordinary beak (banking at the deposit rate) before it banked");
            Assert.AreEqual(c.LootTaken * bounty, c.LootBankedFood, 1e-3f, "the Thief stands in its base zone, so the loot banks at once");
            Assert.AreEqual(c.LootTaken * bounty, r.Banked[0], 1e-3f);
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
        public void AFullThief_GainsNothingFromAMarkedSteal_AndTheBirdLosesNothing()
        {
            const int capacity = 10;
            var map = MatchTestKit.OpenMap(Half);
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, LootPassive(10f, lead: 0f), capacity: capacity);
            var kits = new[] { thief, Carrier("a"), Carrier("b"), Carrier("c") };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = 11f;
            setup.RecordEvents = true;
            for (int i = 1; i <= 3; i++) setup.Policies[i] = MatchTestKit.Stand;
            setup.Policies[0] = Wanderer();
            var r = MatchSim.Run(setup, 6);
            var loots = Loots(r);
            Assert.GreaterOrEqual(loots.Count, 2, "a second mark was consumed with the beak already full");
            Assert.AreEqual(capacity, loots[0].Amount, 1e-4f, "the first filled the beak with its bounty");
            Assert.AreEqual(0f, loots[1].Amount, 1e-4f, "a full Thief gains nothing from the next one");
            Assert.AreEqual(0f, r.Counters[0].LootTakeFood, "and took nothing from the bird");
            Assert.AreEqual(capacity, r.Counters[0].LootBountyFood, 1e-4f);
            Assert.Greater(r.Counters[0].LootLostToRoom, capacity, "the whole second bounty was lost to the full beak");
        }

        [Test]
        public void ARivalsSteal_TakesTheLootLikeAnyCargo()
        {
            var map = MatchTestKit.OpenMap(Half);
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, LootPassive(10f), capacity: 40);
            var rival = MatchTestKit.Fighter("rival", 100f, new[] { MatchTestKit.Steal(Big, 5f, StealCooldown) }, capacity: 100);
            var setup = MatchTestKit.Duel(map, thief, rival, null, MatchTestKit.CastAt(2f, 0), Half, 4f);
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
        // food and rival 2 does not; with it, every rival is that kit and shells up at 6 s (its egg is up from 6.2 s to 14.2 s).
        private static (MatchResult result, List<(float time, int target)> seen) PlayOverdue(ulong seed, float castAt, float clock, MatchKit shelled = null)
        {
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(ReachesTheNeighboursOnly, 5f, StealCooldown) }, LootPassive(seconds: 12f, lead: 1f));
            var map = MatchTestKit.OpenMap(Half);
            var kits = new[] { thief, shelled ?? Carrier("carrier1"), shelled ?? Idle("idle2"), shelled ?? Carrier("carrier3") };
            var setup = MatchTestKit.Setup(map, kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = clock;
            setup.RecordEvents = true;
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
                        if (design != MatchDesignKind.SeatCheck && design != MatchDesignKind.Mixed) continue;
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
