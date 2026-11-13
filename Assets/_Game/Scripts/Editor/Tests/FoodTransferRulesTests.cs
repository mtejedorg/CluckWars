using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A02 (architecture audit 2026-11-12, decision D3a, review fixes R-1..R-6): the owner of the food debits and reports
    /// the actual amount; the receiver credits only that. Every behavioural test here drives the code production runs:
    /// <see cref="FoodRequests{TKey}"/> is the object <c>ChickenCargo</c> holds, <see cref="FoodTransferRules.ServePeck"/>
    /// is what <c>FoodPile.RPC_Drain</c> calls, <see cref="StealRules.SplitDrain"/> what <c>RPC_DrainStolen</c> calls.
    /// The Chicken below only does what the RPC bodies do with the results: add ToCargo / ToBag / Banked.
    /// </summary>
    /// <remarks>
    /// Source-text guards are kept only for what EditMode cannot execute (a live runner is needed): that the RPCs
    /// route through this code at all, the sender checks, the request id on the wire, and RPC ordering. They are the
    /// last region of the file.
    /// </remarks>
    public sealed class FoodTransferRulesTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string CargoPath = Scripts + "Gameplay/ChickenCargo.cs";
        private const string PilePath = Scripts + "Gameplay/FoodPile.cs";
        private const string BasePath = Scripts + "Gameplay/PlayerBase.cs";

        private const int Pile = 100, Victim = 200, Base = 300;
        private const float MaxPeck = 3f, MaxFlush = 3f;
        private const float Window = FoodTransferRules.SettleWindowSeconds;

        /// <summary>A chicken's hold, applying settlements exactly as the RPC bodies in ChickenCargo do.</summary>
        private sealed class Chicken
        {
            public float Capacity = 10f, Cargo, Bag, Stat;
            public readonly FoodRequests<int> Requests = new FoodRequests<int>();

            public float Carried => Cargo + Bag;

            private void Apply(FoodSettlement s) { Cargo += s.ToCargo; Bag += s.ToBag; Stat += s.Banked; }

            public FoodSettlement Peck(int pile, ushort id, float reported, float now)
            {
                var s = Requests.SettlePeck(pile, id, reported, MaxPeck, Capacity, Cargo, now);
                Apply(s);
                return s;
            }

            public FoodSettlement Steal(int victim, ushort id, float reported, float now)
            {
                var s = Requests.SettleSteal(victim, id, reported, Capacity, Cargo, now);
                Apply(s);
                return s;
            }

            public FoodSettlement Deposit(int playerBase, ushort id, float reported, bool banked, float now)
            {
                var s = Requests.SettleDeposit(playerBase, id, reported, banked, MaxFlush, Capacity, Cargo, now);
                Apply(s);
                return s;
            }

            /// <summary>What TryDepositAtNearbyBase + FlushBaseDeposit do: debit the cargo, then send the flush.</summary>
            public ushort Flush(int playerBase, float amount, float now)
            {
                Cargo -= amount;
                return Requests.SendDeposit(playerBase, amount, now);
            }
        }

        // ---- The races the optimistic credit lost --------------------------------

        [Test]
        public void TwoPeckers_OnAPileWithLessThanBothAsk_ShareWhatIsThere_NothingIsCreated()
        {
            float pile = 4f, max = 20f, floor = 0f;
            var a = new Chicken();
            var b = new Chicken();

            Assert.IsTrue(a.Requests.AskPeck(Pile, 3f, a.Capacity, a.Cargo, 0f, out float askA, out ushort idA));
            Assert.IsTrue(b.Requests.AskPeck(Pile, 3f, b.Capacity, b.Cargo, 0f, out float askB, out ushort idB));
            Assert.AreEqual(0f, a.Cargo + b.Cargo, "nothing is credited on the request");

            float gaveA = FoodTransferRules.ServePeck(askA, MaxPeck, true, pile, max, floor, out pile);
            float gaveB = FoodTransferRules.ServePeck(askB, MaxPeck, true, pile, max, floor, out pile);
            a.Peck(Pile, idA, gaveA, 0.1f);
            b.Peck(Pile, idB, gaveB, 0.1f);

            Assert.AreEqual(3f, a.Cargo, 1e-5f);
            Assert.AreEqual(1f, b.Cargo, 1e-5f, "the second pecker gets the last grain, not a second full bite");
            Assert.AreEqual(4f, pile + a.Cargo + b.Cargo, 1e-5f, "pile + cargo is what it was");
        }

        [Test]
        public void TwoThieves_OnOneVictim_GetWhatWasReallyDrained_NotTheirEstimates()
        {
            float victimCargo = 4f, victimBag = 1f;
            var first = new Chicken();
            var second = new Chicken();
            first.Requests.AskSteal(Victim, 4f, first.Capacity, first.Cargo, 0f, out float ask1, out ushort id1);
            second.Requests.AskSteal(Victim, 4f, second.Capacity, second.Cargo, 0f, out float ask2, out ushort id2);

            StealRules.SplitDrain(ask1, victimCargo, victimBag, out float c1, out float b1);
            victimCargo -= c1; victimBag -= b1;
            StealRules.SplitDrain(ask2, victimCargo, victimBag, out float c2, out float b2);
            victimCargo -= c2; victimBag -= b2;

            first.Steal(Victim, id1, c1 + b1, 0.1f);
            second.Steal(Victim, id2, c2 + b2, 0.1f);

            Assert.AreEqual(4f, first.Cargo, 1e-5f);
            Assert.AreEqual(1f, second.Cargo, 1e-5f, "the second thief gets the one unit that was left");
            Assert.AreEqual(5f, first.Cargo + second.Cargo + victimCargo + victimBag, 1e-5f, "5 food before, 5 after");
        }

        [Test]
        public void R1_ASilentlyRefusedSteal_DoesNotEatTheNextStealsReport()
        {
            // Snatch at the edge of reach: the victim's authority rejects it and (before R-1) said nothing, so the
            // request for 1 stayed open. A Scrap on the same victim then drains 3 and reports 3.
            var thief = new Chicken();
            thief.Requests.AskSteal(Victim, 1f, thief.Capacity, thief.Cargo, 0f, out _, out ushort snatch);
            thief.Requests.AskSteal(Victim, 3f, thief.Capacity, thief.Cargo, 0.5f, out _, out ushort scrap);
            Assert.AreNotEqual(snatch, scrap, "each request has its own id");

            var settled = thief.Steal(Victim, scrap, 3f, 0.6f);
            Assert.AreEqual(SettleOutcome.Matched, settled.Outcome);
            Assert.AreEqual(3f, thief.Cargo, 1e-5f, "the report settles the request it names: all 3 drained units arrive");
            Assert.AreEqual(1f, thief.Requests.Reserved(0.6f), 1e-5f, "only the unanswered Snatch still holds room");

            // With R-1 the refusal does report: zero, for the Snatch's id. The room is free at once.
            thief.Steal(Victim, snatch, 0f, 0.6f);
            Assert.AreEqual(0f, thief.Requests.Reserved(0.6f), 1e-5f);
            Assert.AreEqual(3f, thief.Cargo, 1e-5f);
        }

        [Test]
        public void ASteal_IsStrict_AReportWithNoOpenRequest_IsWorthNothing()
        {
            var thief = new Chicken();
            Assert.AreEqual(SettleOutcome.Refused, thief.Steal(Victim, 7, 3f, 0f).Outcome, "nobody asked");

            thief.Requests.AskSteal(Victim, 3f, thief.Capacity, thief.Cargo, 0f, out _, out ushort id);
            Assert.AreEqual(SettleOutcome.Refused, thief.Steal(Base, id, 3f, 0.1f).Outcome, "asked someone else");
            Assert.AreEqual(SettleOutcome.Refused, thief.Steal(Victim, (ushort)(id + 1), 3f, 0.1f).Outcome, "another id");
            Assert.AreEqual(SettleOutcome.Refused, thief.Steal(Victim, id, 3f, Window + 0.01f).Outcome, "expired");
            Assert.AreEqual(0f, thief.Carried, "the sender only owns the named victim: no open request, no food");

            thief.Requests.AskSteal(Victim, 3f, thief.Capacity, thief.Cargo, 5f, out _, out ushort again);
            Assert.AreEqual(SettleOutcome.Matched, thief.Steal(Victim, again, 9f, 5.1f).Outcome);
            Assert.AreEqual(3f, thief.Cargo, 1e-5f, "an inflated report is worth the request, no more");
            Assert.AreEqual(SettleOutcome.Refused, thief.Steal(Victim, again, 3f, 5.2f).Outcome, "a replay settles nothing");
        }

        [Test]
        public void R2_APeckReport_AfterTheWindow_IsStillCredited_CappedAtOneBite()
        {
            float pile = 10f;
            var pecker = new Chicken();
            pecker.Requests.AskPeck(Pile, 3f, pecker.Capacity, pecker.Cargo, 0f, out float asked, out ushort id);
            float gave = FoodTransferRules.ServePeck(asked, MaxPeck, true, pile, 20f, 0f, out pile);

            var late = pecker.Peck(Pile, id, gave, Window + 1f);
            Assert.AreEqual(SettleOutcome.Late, late.Outcome);
            Assert.AreEqual(3f, pecker.Cargo, 1e-5f, "the pile debited 3; refusing the late report would destroy them");
            Assert.AreEqual(10f, pile + pecker.Carried, 1e-5f);

            var forged = pecker.Peck(Pile, 999, 50f, Window + 2f);
            Assert.AreEqual(SettleOutcome.Late, forged.Outcome);
            Assert.AreEqual(MaxPeck, forged.Credited, 1e-5f, "with no request on record a report is worth one bite at most");
        }

        [Test]
        public void R2_ADepositBounce_AfterTheWindow_StillComesBack()
        {
            var chicken = new Chicken { Cargo = 6f };
            ushort id = chicken.Flush(Base, 1.5f, 0f);

            var late = chicken.Deposit(Base, id, 1.5f, banked: false, Window + 1f);
            Assert.AreEqual(SettleOutcome.Late, late.Outcome);
            Assert.AreEqual(6f, chicken.Carried, 1e-5f, "the bounce arrived late, the food still comes home");
            Assert.AreEqual(0f, chicken.Stat);

            Assert.AreEqual(MaxFlush, chicken.Deposit(Base, 999, 50f, banked: false, Window + 2f).Credited, 1e-5f,
                "an answer with no flush on record is capped at one flush");
        }

        [Test]
        public void ADepositTheBaseRejects_ComesBackWhole_AndIsNotCounted()
        {
            var chicken = new Chicken { Cargo = 6f, Bag = 3f };
            float carried = chicken.Carried;
            ushort id = chicken.Flush(Base, 1.5f, 0f);

            var bounced = chicken.Deposit(Base, id, 1.5f, banked: false, 0.2f);
            Assert.AreEqual(SettleOutcome.Matched, bounced.Outcome);
            Assert.AreEqual(carried, chicken.Carried, 1e-5f, "the chicken carries what it carried before the flush");
            Assert.AreEqual(0f, chicken.Stat, "a bounced flush is not deposited food");
        }

        [Test]
        public void ADepositTheBaseAccepts_IsCountedOnce()
        {
            var chicken = new Chicken { Cargo = 6f };
            ushort id = chicken.Flush(Base, 1.5f, 0f);
            chicken.Deposit(Base, id, 1.5f, banked: true, 0.2f);
            Assert.AreEqual(1.5f, chicken.Stat, 1e-5f);
            Assert.AreEqual(4.5f, chicken.Carried, 1e-5f, "banked food does not come back");
            Assert.AreEqual(0f, chicken.Requests.DepositsInFlight(0.2f));
        }

        [Test]
        public void TwoFlushesInOneFrame_ThenABounce_ReturnsExactlyTheBouncedOne()
        {
            // Leaving one base and entering the next flushes twice at the same timestamp: the ids tell them apart (R-6).
            var chicken = new Chicken { Cargo = 6f };
            ushort small = chicken.Flush(Base, 0.5f, 1f);
            ushort big = chicken.Flush(Base, 2f, 1f);

            chicken.Deposit(Base, big, 2f, banked: false, 1.1f);
            chicken.Deposit(Base, small, 0.5f, banked: true, 1.1f);

            Assert.AreEqual(5.5f, chicken.Carried, 1e-5f, "the 2 that bounced are back; the 0.5 that banked are not");
            Assert.AreEqual(0.5f, chicken.Stat, 1e-5f);
        }

        [Test]
        public void TheRing_HoldsEveryFlushOfAFullSettleWindow_AndAWrapLosesNoFood()
        {
            int worstCase = (int)System.Math.Ceiling(Window / BaseDepositRules.FlushSeconds);
            Assert.GreaterOrEqual(FoodTransferRules.LedgerSize, worstCase + 2,
                "one flush per FlushSeconds, each open for the settle window, plus the extra flush of a base change");
            Assert.AreEqual(FoodTransferRules.LedgerSize, FoodTransferRules.NewLedger<int>().Size);

            // More unanswered flushes than the ring holds (a stalled link), then every answer arrives as a bounce.
            int flushes = FoodTransferRules.LedgerSize + 4;
            var chicken = new Chicken { Capacity = 100f, Cargo = 50f };
            var ids = new ushort[flushes];
            for (int i = 0; i < flushes; i++) ids[i] = chicken.Flush(Base, 1f, i * 0.01f);
            int late = 0;
            for (int i = 0; i < flushes; i++)
                if (chicken.Deposit(Base, ids[i], 1f, banked: false, 1f).Outcome == SettleOutcome.Late) late++;

            Assert.AreEqual(4, late, "the four overwritten records are answered late");
            Assert.AreEqual(50f, chicken.Carried, 1e-4f, "and still returned: a wrapped ring loses no food");
        }

        // ---- The pile's side (R-4) -----------------------------------------------

        [TestCase(3f, 3f, true, 10f, 3f)]
        [TestCase(50f, 3f, true, 10f, 3f)]      // over the clamp: one bite, whatever was asked
        [TestCase(3f, 3f, false, 10f, 0f)]      // out of reach: nothing
        [TestCase(3f, 3f, true, 1f, 1f)]        // less than asked: what is there
        [TestCase(-1f, 3f, true, 10f, 0f)]
        [TestCase(float.NaN, 3f, true, 10f, 0f)]
        [TestCase(3f, 0f, true, 10f, 0f)]
        public void ServePeck_ClampsToOneBite_NeedsReach_AndConserves(float requested, float maxPeck, bool inReach, float pile, float expected)
        {
            float served = FoodTransferRules.ServePeck(requested, maxPeck, inReach, pile, 20f, 0f, out float after);
            Assert.AreEqual(expected, served, 1e-5f);
            Assert.AreEqual(pile, after + served, 1e-5f, "what leaves the pile is what is reported");
        }

        [Test]
        public void ServePeck_NeverHandsOutAPermanentPilesFloor()
        {
            float served = FoodTransferRules.ServePeck(3f, 3f, true, 6f, 20f, 0.25f, out float after);   // floor at 5
            Assert.AreEqual(1f, served, 1e-5f);
            Assert.AreEqual(5f, after, 1e-5f);
        }

        [TestCase(0.9f, 1f, 0f, 0f, true)]
        [TestCase(1.1f, 1f, 0f, 0f, false)]
        [TestCase(2.5f, 1f, 12f, 0.15f, true)]     // moved 1.8 m while the request flew
        [TestCase(30f, 1f, 12f, 0.15f, false)]     // across the arena
        public void PeckReach_IsTheCollectReach_PlusDriftOverTheLatency(float distance, float reach, float speed, float latency, bool expected) =>
            Assert.AreEqual(expected, FoodTransferRules.IsWithinPeckReach(distance, reach, speed, latency));

        // ---- Playtest 2026-11-13: an honest remote peck refused at 1.0 - 1.1 m ---------------------------------
        // Measured in a 2-client Shared Mode player session: Runner.GetPlayerRtt(info.Source) read 0.0000 for the remote
        // pecker on every request, so the drift margin was 0 and the pile judged a proxy that was still arriving
        // (1.033 m, 1.1 m) against the bare 1.0 m reach the pecker itself had passed on its own, newer position.

        private const float SpeedyWalk = 12.15f;   // the drift speed logged for the refused pecker

        [TestCase(1.033f)]   // session-20261113-095528
        [TestCase(1.1f)]     // session-20261113-094129, twice
        [TestCase(1f + SpeedyWalk * ExecuteRules.MinRemoteLatencySeconds)]   // the whole floor allowance
        public void ARemotePeck_FromASenderWhoseRttReadsZero_IsServedAtTheEdgeOfReach(float distance) =>
            Assert.IsTrue(FoodTransferRules.IsWithinPeckReach(distance, 1f, SpeedyWalk, ExecuteRules.RemoteLatency(0f)),
                "a remote request always gets the latency floor: the measured RTT cannot be relied on in Shared Mode");

        [Test]
        public void ALocalPeck_HasNoAllowance_TheBoundIsTheRequestersOwnCheck()
        {
            // Same peer, same tick, same position as PeckAbilitySO.FindPile: exactly the collect reach.
            Assert.IsTrue(FoodTransferRules.IsWithinPeckReach(1f, 1f, SpeedyWalk, 0f));
            Assert.IsFalse(FoodTransferRules.IsWithinPeckReach(1.1f, 1f, SpeedyWalk, 0f));
        }

        [TestCase(0f)]
        [TestCase(0.116f)]
        [TestCase(60f)]            // a sender causing a huge RTT cannot widen the bound past the cap
        [TestCase(float.NaN)]
        public void APeckFromAcrossTheArena_IsRefused_WhateverTheSendersRtt(float measuredRtt)
        {
            float latency = ExecuteRules.RemoteLatency(measuredRtt);
            Assert.IsFalse(FoodTransferRules.IsWithinPeckReach(30f, 1f, SpeedyWalk, latency), "across the arena");
            Assert.IsFalse(FoodTransferRules.IsWithinPeckReach(1f + SpeedyWalk * ExecuteRules.MaxLatencySeconds + 0.01f, 1f, SpeedyWalk, latency),
                "just past the largest allowance there is");
        }

        [Test]
        public void ThePilesFullFootprint_IsNeverFurtherThanItsShrunkenOne()
        {
            // The requester judged its reach on its replica of the pile, which can still be a fill step larger than the
            // authority's (session-20261113-095528: same spot, 0.670 m at 7.80x6.50, 0.983 m one step later at 7.16x5.96).
            var full = new UnityEngine.Vector2(7.8f, 6.5f);
            var shrunk = new UnityEngine.Vector2(7.16f, 5.96f);
            var centre = new UnityEngine.Vector3(2f, 0f, -3f);
            for (int deg = 0; deg < 360; deg += 15)
            {
                float rad = deg * UnityEngine.Mathf.Deg2Rad;
                foreach (float r in new[] { 1f, 3.5f, 4.6f, 9f })
                {
                    var pos = centre + new UnityEngine.Vector3(UnityEngine.Mathf.Cos(rad), 0f, UnityEngine.Mathf.Sin(rad)) * r;
                    float dFull = PileFootprintShape.DistanceToSurfaceXZ(pos, centre, 30f, full);
                    float dShrunk = PileFootprintShape.DistanceToSurfaceXZ(pos, centre, 30f, shrunk);
                    Assert.LessOrEqual(dFull, dShrunk + 1e-4f, $"deg={deg} r={r}");
                }
            }
        }

        [Test]
        public void TheDrainRpc_AllowsARemoteSenderTheFlooredLatency_AndMeasuresToTheFullFootprint()
        {
            string drain = AssassinExecuteCreditTests.MethodBody(PilePath, "public void RPC_Drain(");
            StringAssert.Contains("ChickenCombat.SenderLatency(Runner, info)", drain, "0 for a local invoke, else the floored and capped RTT");
            StringAssert.DoesNotContain("GetPlayerRtt", drain, "the raw RTT reads 0 for a remote player in Shared Mode");
            StringAssert.Contains("DistanceToFullSurface(pecker.transform.position)", drain);
        }

        [Test]
        public void ARefusedPeck_ReportsZero_AndFreesTheReservedRoom()
        {
            var pecker = new Chicken { Capacity = 6f, Cargo = 2f };
            pecker.Requests.AskPeck(Pile, 5f, pecker.Capacity, pecker.Cargo, 0f, out float asked, out ushort id);
            Assert.AreEqual(4f, asked, "clamped to the free room");
            Assert.AreEqual(0f, pecker.Requests.FreeSpace(pecker.Capacity, pecker.Cargo, 0f), "the open request holds the room");

            float served = FoodTransferRules.ServePeck(asked, MaxPeck, false, 10f, 20f, 0f, out _);
            pecker.Peck(Pile, id, served, 0.1f);
            Assert.AreEqual(2f, pecker.Cargo);
            Assert.AreEqual(4f, pecker.Requests.FreeSpace(pecker.Capacity, pecker.Cargo, 0.1f));
        }

        // ---- Room ------------------------------------------------------------------

        [Test]
        public void OpenRequests_CannotOverbookTheHold()
        {
            var c = new Chicken { Capacity = 5f, Cargo = 1f };
            Assert.IsTrue(c.Requests.AskPeck(Pile, 3f, c.Capacity, c.Cargo, 0f, out float a1, out ushort p));
            Assert.IsTrue(c.Requests.AskSteal(Victim, 4f, c.Capacity, c.Cargo, 0f, out float a2, out ushort s));
            Assert.AreEqual(3f, a1);
            Assert.AreEqual(1f, a2, "only one unit of room is left unreserved");
            Assert.IsFalse(c.Requests.AskSteal(Victim, 4f, c.Capacity, c.Cargo, 0f, out _, out _), "and then none: no request is sent");
            c.Peck(Pile, p, 3f, 0.1f);
            c.Steal(Victim, s, 1f, 0.1f);
            Assert.AreEqual(c.Capacity, c.Cargo, 1e-5f, "everything that arrived fits");
            Assert.AreEqual(0f, c.Bag);
        }

        [Test]
        public void WhatDoesNotFitOnArrival_GoesToTheBag_NeverLost()
        {
            var c = new Chicken { Capacity = 4f, Cargo = 4f };
            var late = c.Peck(Pile, 5, 3f, 0f);   // a late report: no room was reserved for it
            Assert.AreEqual(0f, late.ToCargo);
            Assert.AreEqual(3f, late.ToBag, 1e-5f);
            Assert.AreEqual(7f, c.Carried, 1e-5f);
        }

        [TestCase(3f, 3f, 3f)]
        [TestCase(3f, 1f, 1f)]
        [TestCase(3f, 0f, 0f)]
        [TestCase(3f, 9f, 3f)]
        [TestCase(3f, -2f, 0f)]
        [TestCase(3f, float.NaN, 0f)]
        [TestCase(3f, float.PositiveInfinity, 3f)]
        [TestCase(-1f, 2f, 0f)]
        public void Credit_IsTheReport_NeverMoreThanTheRequest_NeverNegative(float requested, float reported, float expected) =>
            Assert.AreEqual(expected, FoodTransferRules.Credit(requested, reported), 1e-5f);

        [TestCase(4f, 10f, 4f, 0f)]
        [TestCase(4f, 1.5f, 1.5f, 2.5f)]
        [TestCase(4f, 0f, 0f, 4f)]
        [TestCase(4f, -3f, 0f, 4f)]
        [TestCase(-1f, 5f, 0f, 0f)]
        public void Place_FillsTheCargoFirst_ThenTheBag_AndLosesNothing(float amount, float freeSpace, float toCargo, float toBag)
        {
            FoodTransferRules.Place(amount, freeSpace, out float c, out float b);
            Assert.AreEqual(toCargo, c, 1e-5f);
            Assert.AreEqual(toBag, b, 1e-5f);
            Assert.AreEqual(System.Math.Max(0f, amount), c + b, 1e-5f);
        }

        // ---- The ledger additions --------------------------------------------------

        [Test]
        public void TryClose_ClosesExactlyTheNamedRecord_Once()
        {
            var ledger = FoodTransferRules.NewLedger<int>();
            ushort a = ledger.Open(Pile, 3f, 0f);
            ushort b = ledger.Open(Pile, 1f, 0f);   // same key, same timestamp
            Assert.AreNotEqual(a, b);

            Assert.IsTrue(ledger.TryClose(Pile, b, 0.2f, out float second));
            Assert.AreEqual(1f, second);
            Assert.IsFalse(ledger.TryClose(Pile, b, 0.2f, out _), "once");
            Assert.IsFalse(ledger.TryClose(Victim, a, 0.2f, out _), "the id alone is not enough: the key must match");
            Assert.IsTrue(ledger.TryClose(Pile, a, 0.2f, out float first));
            Assert.AreEqual(3f, first);
        }

        [Test]
        public void Outstanding_CountsOpenRequestsOnly()
        {
            var ledger = FoodTransferRules.NewLedger<int>();
            ushort a = ledger.Open(Pile, 3f, 0f);
            ledger.Open(Victim, 2f, 1f);
            Assert.AreEqual(5f, ledger.Outstanding(1f), 1e-5f);
            ledger.TryClose(Pile, a, 1f, out _);
            Assert.AreEqual(2f, ledger.Outstanding(1f), 1e-5f, "settled requests release their room");
            Assert.AreEqual(0f, ledger.Outstanding(1f + Window + 0.01f), 1e-5f, "so do expired ones");
        }

        [Test]
        public void TheSettleWindow_CoversTwoRelayedTrips()
        {
            const float rtt = 0.25f, ticks = 2f / 32f;
            Assert.GreaterOrEqual(Window, 4f * (rtt + ticks));
        }

        // ---- Wiring (source guards: needs a live runner to execute) -----------------

        private static string Code(string path) =>
            string.Join("\n", File.ReadAllLines(path).Select(l =>
            {
                int slash = l.IndexOf("//", System.StringComparison.Ordinal);
                return slash >= 0 ? l.Substring(0, slash) : l;
            }));

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        [Test]
        public void Peck_AsksThePile_AndIsCreditedOnlyByItsReport()
        {
            string peck = Code(Scripts + "Abilities/PeckAbilitySO.cs");
            StringAssert.Contains("cargo.RequestPeck(pile, take)", peck);
            Assert.IsFalse(Regex.IsMatch(peck, @"\.Cargo\s*\+="), "Peck credits its own cargo again");
            Assert.Less(peck.IndexOf("SetMovementLock(ctx.Slot)", System.StringComparison.Ordinal),
                        peck.IndexOf("RequestPeck(", System.StringComparison.Ordinal), "the movement lock still comes first (balance)");

            string request = Body(CargoPath, "public void RequestPeck(");
            Assert.Less(request.IndexOf("_requests.AskPeck(", System.StringComparison.Ordinal),
                        request.IndexOf(".RPC_Drain(asked, Id, requestId)", System.StringComparison.Ordinal), "a local pile settles synchronously: open first");

            string drain = Body(PilePath, "public void RPC_Drain(");
            StringAssert.Contains("RpcInfo info", drain);
            StringAssert.Contains("FoodTransferRules.ServePeck(amount, pecker.MaxPeck, inReach", drain);
            StringAssert.Contains("FoodTransferRules.IsWithinPeckReach(", drain);
            StringAssert.Contains("pecker.RPC_SettlePeck(actual, Id, requestId)", drain, "every served or refused request is answered with its id");

            string settle = Body(CargoPath, "public void RPC_SettlePeck(");
            Assert.Less(settle.IndexOf("SpeaksFor(pile, info)", System.StringComparison.Ordinal),
                        settle.IndexOf("_requests.SettlePeck(pileId, requestId", System.StringComparison.Ordinal),
                        "the late-report credit is only safe behind the pile-authority check");
        }

        [Test]
        public void Steal_AsksTheVictim_EveryExitThatKnowsTheThiefReports()
        {
            string request = Body(CargoPath, "public void RequestSteal(");
            Assert.Less(request.IndexOf("_requests.AskSteal(", System.StringComparison.Ordinal),
                        request.IndexOf("RPC_DrainStolen(asked, _controller.Id, requestId)", System.StringComparison.Ordinal));

            string drain = Body(CargoPath, "public void RPC_DrainStolen(");
            Assert.AreEqual(2, Regex.Matches(drain, @"ReportRefusalToSender\(thiefId, requestId, info\)").Count,
                "the implausible-amount and unresolved / out-of-reach thief exits both answer (R-1)");
            Assert.AreEqual(2, Regex.Matches(drain, @"ReportSteal\(thief, 0f, requestId\)").Count, "shell / wall, and the empty load");
            StringAssert.Contains("ReportSteal(thief, actual, requestId)", drain);

            string refusal = Body(CargoPath, "private void ReportRefusalToSender(");
            StringAssert.Contains("SpeaksFor(named, info)", refusal, "only a thief the sender owns is answered");

            string settle = Body(CargoPath, "public void RPC_SettleSteal(");
            StringAssert.Contains("_requests.SettleSteal(victimId, requestId, amount", settle);
            StringAssert.Contains("_stealCreditFrame = Time.frameCount;", settle);
        }

        [Test]
        public void Deposit_IsAnsweredWithItsId_AndNeverDroppedOnTheFloor()
        {
            string flush = Body(CargoPath, "public void FlushBaseDeposit()");
            Assert.Less(flush.IndexOf("_requests.SendDeposit(", System.StringComparison.Ordinal),
                        flush.IndexOf(".RPC_AddFood(_pendingBaseFood, Id, flushId)", System.StringComparison.Ordinal));
            Assert.Less(flush.IndexOf("AddToHold(_pendingBaseFood)", System.StringComparison.Ordinal),
                        flush.IndexOf("_pendingBaseFood = 0f", System.StringComparison.Ordinal), "an invalid base returns the food before the counter is zeroed (R-5)");

            string add = Body(BasePath, "public void RPC_AddFood(");
            Assert.AreEqual(2, Regex.Matches(add, @"banked: false, Id, requestId\)").Count, "both refusals bounce");
            Assert.AreEqual(1, Regex.Matches(add, @"banked: true, Id, requestId\)").Count);
            Assert.Less(add.IndexOf("FoodTotal += amount", System.StringComparison.Ordinal),
                        add.IndexOf("banked: true", System.StringComparison.Ordinal));

            string settle = Body(CargoPath, "public void RPC_SettleDeposit(");
            Assert.Less(settle.IndexOf("SpeaksFor(playerBase, info)", System.StringComparison.Ordinal),
                        settle.IndexOf("_requests.SettleDeposit(baseId, requestId", System.StringComparison.Ordinal));
            StringAssert.Contains("RecordDeposit(settled.Banked)", settle);
            StringAssert.Contains("DepositEventId++", settle, "the deposit moment (and so its sound) is raised on 'banked'");
            StringAssert.DoesNotContain("DepositEventId++", Body(CargoPath, "private void TryDepositAtNearbyBase()"));

            Assert.IsNull(typeof(ChickenMatchStats).GetMethod("RPC_AddDeposit"),
                "the unvalidated deposit-stat RPC is back: any peer could inflate anyone's deposits again.");
        }

        [Test]
        public void CargoIsCredited_OnlyFromASettlement_OrAFlushThatCouldNotBeSent()
        {
            string src = Code(CargoPath);
            int credits = Regex.Matches(src, @"(?<![\w.])Cargo\s*\+=").Count;
            int allowed = new[]
                {
                    "public void RPC_SettlePeck(", "public void RPC_SettleSteal(", "public void RPC_SettleDeposit(",
                    "private void AddToHold(",
                }
                .Sum(sig => Regex.Matches(Body(CargoPath, sig), @"(?<![\w.])Cargo\s*\+=").Count);
            Assert.AreEqual(4, allowed);
            Assert.AreEqual(allowed, credits, "ChickenCargo credits cargo outside a settlement: that is an optimistic credit again.");
        }
    }
}
