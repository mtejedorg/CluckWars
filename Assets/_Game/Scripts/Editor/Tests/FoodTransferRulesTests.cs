using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A02 (architecture audit 2026-11-12, decision D3a): the owner of the food debits and reports the actual amount;
    /// the receiver credits only that. The three races the old optimistic credit lost are played out here with the
    /// production rules (<see cref="FoodTransferRules"/>, <see cref="PendingStealLedger{TKey}"/>,
    /// <see cref="FoodPileMath"/>, <see cref="StealRules.SplitDrain"/>); the RPC wiring is pinned by shape.
    /// </summary>
    public sealed class FoodTransferRulesTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string CargoPath = Scripts + "Gameplay/ChickenCargo.cs";
        private const string PilePath = Scripts + "Gameplay/FoodPile.cs";
        private const string BasePath = Scripts + "Gameplay/PlayerBase.cs";

        /// <summary>A chicken's hold as its own authority sees it: cargo, bag and the requests it has open.</summary>
        private sealed class Hold
        {
            public float Capacity = 10f, Cargo, Bag;
            public readonly PendingStealLedger<int> Open = FoodTransferRules.NewLedger<int>();

            public float FreeSpace(float now) => FoodTransferRules.FreeSpace(Capacity, Cargo, Open.Outstanding(now));

            /// <summary>The request half: asks <paramref name="owner"/> for food, credits nothing.</summary>
            public float Ask(int owner, float wanted, float now)
            {
                float amount = FoodTransferRules.Request(wanted, FreeSpace(now));
                if (amount > 0f) Open.Record(owner, amount, now);
                return amount;
            }

            /// <summary>The settle half: the owner's report arrives.</summary>
            public bool Settle(int owner, float reported, float now)
            {
                if (!Open.TryConsumeOldest(owner, now, out float requested)) return false;
                Cargo += FoodTransferRules.Credit(requested, reported);
                return true;
            }
        }

        private const int Pile = 100, Victim = 200, Base = 300;

        // ---- The three races -----------------------------------------------------

        [Test]
        public void TwoPeckers_OnAPileWithLessThanBothAsk_ShareWhatIsThere_NothingIsCreated()
        {
            float pile = 4f, max = 20f, floor = 0f;
            var a = new Hold();
            var b = new Hold();

            // Both see 4 on the pile and ask for a full bite of 3 in the same tick.
            float askA = a.Ask(Pile, 3f, 0f);
            float askB = b.Ask(Pile, 3f, 0f);
            Assert.AreEqual(3f, askA);
            Assert.AreEqual(3f, askB);
            Assert.AreEqual(0f, a.Cargo + b.Cargo, "nothing is credited on the request");

            // The pile's authority serves them in arrival order and reports what it really removed.
            float afterA = FoodPileMath.Drain(pile, max, floor, askA);
            float gaveA = pile - afterA;
            float afterB = FoodPileMath.Drain(afterA, max, floor, askB);
            float gaveB = afterA - afterB;
            Assert.IsTrue(a.Settle(Pile, gaveA, 0.1f));
            Assert.IsTrue(b.Settle(Pile, gaveB, 0.1f));

            Assert.AreEqual(3f, a.Cargo, 1e-5f);
            Assert.AreEqual(1f, b.Cargo, 1e-5f, "the second pecker gets the last grain, not a second full bite");
            Assert.AreEqual(4f, afterB + a.Cargo + b.Cargo, 1e-5f, "pile + cargo is what it was");
        }

        [Test]
        public void APermanentPile_NeverHandsOutItsFloor()
        {
            float max = 20f, floor = 0.25f, pile = 6f;   // floor at 5: one grain is food, the rest is terrain
            var a = new Hold();
            float ask = a.Ask(Pile, 3f, 0f);
            float after = FoodPileMath.Drain(pile, max, floor, ask);
            a.Settle(Pile, pile - after, 0.1f);
            Assert.AreEqual(5f, after, 1e-5f);
            Assert.AreEqual(1f, a.Cargo, 1e-5f);
        }

        [Test]
        public void TwoThieves_OnOneVictim_GetWhatWasReallyDrained_NotTheirEstimates()
        {
            float victimCargo = 4f, victimBag = 1f;
            var first = new Hold();
            var second = new Hold();

            // Both scan a victim carrying 5 and ask for 4.
            float ask1 = first.Ask(Victim, 4f, 0f);
            float ask2 = second.Ask(Victim, 4f, 0f);

            StealRules.SplitDrain(ask1, victimCargo, victimBag, out float c1, out float b1);
            victimCargo -= c1; victimBag -= b1;
            StealRules.SplitDrain(ask2, victimCargo, victimBag, out float c2, out float b2);
            victimCargo -= c2; victimBag -= b2;

            first.Settle(Victim, c1 + b1, 0.1f);
            second.Settle(Victim, c2 + b2, 0.1f);

            Assert.AreEqual(4f, first.Cargo, 1e-5f);
            Assert.AreEqual(1f, second.Cargo, 1e-5f, "the second thief gets the one unit that was left");
            Assert.AreEqual(5f, first.Cargo + second.Cargo + victimCargo + victimBag, 1e-5f, "5 food before, 5 after");
        }

        [Test]
        public void ARefusedSteal_SettlesAtZero_AndFreesTheReservedRoom()
        {
            var thief = new Hold { Capacity = 6f, Cargo = 2f };
            Assert.AreEqual(4f, thief.Ask(Victim, 5f, 0f), "clamped to the free room");
            Assert.AreEqual(0f, thief.FreeSpace(0f), "the open request holds the room");
            Assert.IsTrue(thief.Settle(Victim, 0f, 0.1f), "a shell or a base wall: the victim reports nothing drained");
            Assert.AreEqual(2f, thief.Cargo);
            Assert.AreEqual(4f, thief.FreeSpace(0.1f));
        }

        [Test]
        public void ADepositTheBaseRejects_ComesBackWhole_AndIsNotCounted()
        {
            var sent = FoodTransferRules.NewLedger<int>();
            float capacity = 10f, cargo = 6f, bag = 3f, banked = 0f, stat = 0f;
            float carried = cargo + bag;

            // The chicken debits first (bag, then cargo) and sends the flush.
            float fromBag = 1.5f, fromCargo = 0f;
            bag -= fromBag; cargo -= fromCargo;
            float flush = fromBag + fromCargo;
            sent.Record(Base, flush, 0f);

            // The base refuses it (the host sees the chicken outside the radius) and bounces the amount.
            Assert.IsTrue(sent.TryConsumeOldest(Base, 0.2f, out float recorded));
            float back = FoodTransferRules.Credit(recorded, flush);
            FoodTransferRules.SplitRefund(back, FoodTransferRules.FreeSpace(capacity, cargo, 0f), out float toCargo, out float toBag);
            cargo += toCargo; bag += toBag;

            Assert.AreEqual(carried, cargo + bag, 1e-5f, "the chicken carries what it carried before the flush");
            Assert.AreEqual(0f, banked);
            Assert.AreEqual(0f, stat, "a bounced flush is not deposited food");
            Assert.IsFalse(sent.TryConsumeOldest(Base, 0.2f, out _), "one flush, one settlement");
        }

        [Test]
        public void ADepositTheBaseAccepts_IsBankedAndCountedOnce()
        {
            var sent = FoodTransferRules.NewLedger<int>();
            float banked = 0f, stat = 0f;
            sent.Record(Base, 1.5f, 0f);
            banked += 1.5f;                                               // the base's authority
            Assert.IsTrue(sent.TryConsumeOldest(Base, 0.2f, out float recorded));
            stat += FoodTransferRules.Credit(recorded, 1.5f);             // the chicken's authority, on "banked"
            Assert.AreEqual(banked, stat, 1e-5f);
            Assert.IsFalse(sent.TryConsumeOldest(Base, 0.2f, out _), "a replayed acknowledgement counts nothing");
        }

        // ---- The rules themselves ------------------------------------------------

        [TestCase(3f, 3f, 3f)]
        [TestCase(3f, 1f, 1f)]
        [TestCase(3f, 0f, 0f)]
        [TestCase(3f, 9f, 3f)]           // an inflated report is worth the request, no more
        [TestCase(3f, -2f, 0f)]
        [TestCase(3f, float.NaN, 0f)]
        [TestCase(3f, float.PositiveInfinity, 3f)]
        [TestCase(-1f, 2f, 0f)]
        public void Credit_IsTheReport_NeverMoreThanTheRequest_NeverNegative(float requested, float reported, float expected) =>
            Assert.AreEqual(expected, FoodTransferRules.Credit(requested, reported), 1e-5f);

        [TestCase(10f, 4f, 0f, 6f)]
        [TestCase(10f, 4f, 3f, 3f)]
        [TestCase(10f, 4f, 9f, 0f)]
        [TestCase(10f, 12f, 0f, 0f)]
        [TestCase(0f, 0f, 0f, 0f)]
        public void FreeSpace_SubtractsWhatIsHeld_AndWhatIsAlreadyAskedFor(float capacity, float held, float reserved, float expected) =>
            Assert.AreEqual(expected, FoodTransferRules.FreeSpace(capacity, held, reserved), 1e-5f);

        [Test]
        public void OpenRequests_CannotOverbookTheHold()
        {
            var hold = new Hold { Capacity = 5f, Cargo = 1f };
            Assert.AreEqual(3f, hold.Ask(Pile, 3f, 0f));
            Assert.AreEqual(1f, hold.Ask(Victim, 4f, 0f), "only one unit of room is left unreserved");
            Assert.AreEqual(0f, hold.Ask(Victim, 4f, 0f), "and then none: no request is even sent");
            hold.Settle(Pile, 3f, 0.1f);
            hold.Settle(Victim, 1f, 0.1f);
            Assert.AreEqual(hold.Capacity, hold.Cargo, 1e-5f, "everything that arrived fits");
        }

        [TestCase(4f, 10f, 4f, 0f)]
        [TestCase(4f, 1.5f, 1.5f, 2.5f)]
        [TestCase(4f, 0f, 0f, 4f)]
        [TestCase(4f, -3f, 0f, 4f)]
        [TestCase(-1f, 5f, 0f, 0f)]
        public void SplitRefund_FillsTheCargoFirst_ThenTheBag_AndLosesNothing(float refund, float freeSpace, float toCargo, float toBag)
        {
            FoodTransferRules.SplitRefund(refund, freeSpace, out float c, out float b);
            Assert.AreEqual(toCargo, c, 1e-5f);
            Assert.AreEqual(toBag, b, 1e-5f);
            Assert.AreEqual(System.Math.Max(0f, refund), c + b, 1e-5f);
        }

        // ---- The ledger additions ------------------------------------------------

        [Test]
        public void TryConsumeOldest_IsFirstInFirstOut_PerOwner_AndOnce()
        {
            var ledger = FoodTransferRules.NewLedger<int>();
            ledger.Record(Pile, 3f, 0f);
            ledger.Record(Victim, 9f, 0.05f);
            ledger.Record(Pile, 1f, 0.1f);

            Assert.IsTrue(ledger.TryConsumeOldest(Pile, 0.2f, out float first));
            Assert.AreEqual(3f, first);
            Assert.IsTrue(ledger.TryConsumeOldest(Pile, 0.2f, out float second));
            Assert.AreEqual(1f, second);
            Assert.IsFalse(ledger.TryConsumeOldest(Pile, 0.2f, out float none));
            Assert.AreEqual(0f, none);
            Assert.IsTrue(ledger.TryConsumeOldest(Victim, 0.2f, out _), "another owner's request is untouched");
        }

        [Test]
        public void AnUnsolicitedReport_OrALateOne_IsRefused()
        {
            var hold = new Hold();
            Assert.IsFalse(hold.Settle(Pile, 3f, 0f), "nobody asked");
            hold.Ask(Pile, 3f, 0f);
            Assert.IsFalse(hold.Settle(Victim, 3f, 0.1f), "asked someone else");
            Assert.IsFalse(hold.Settle(Pile, 3f, FoodTransferRules.SettleWindowSeconds + 0.01f), "the request expired");
            Assert.AreEqual(0f, hold.Cargo);
        }

        [Test]
        public void Outstanding_CountsOpenRequestsOnly()
        {
            var ledger = FoodTransferRules.NewLedger<int>();
            Assert.AreEqual(0f, ledger.Outstanding(0f));
            ledger.Record(Pile, 3f, 0f);
            ledger.Record(Victim, 2f, 1f);
            Assert.AreEqual(5f, ledger.Outstanding(1f), 1e-5f);
            ledger.TryConsumeOldest(Pile, 1f, out _);
            Assert.AreEqual(2f, ledger.Outstanding(1f), 1e-5f, "settled requests release their room");
            Assert.AreEqual(0f, ledger.Outstanding(1f + FoodTransferRules.SettleWindowSeconds + 0.01f), 1e-5f, "so do expired ones");
        }

        [Test]
        public void TheSettleWindow_CoversTwoRelayedTrips()
        {
            const float rtt = 0.25f, ticks = 2f / 32f;
            Assert.GreaterOrEqual(FoodTransferRules.SettleWindowSeconds, 4f * (rtt + ticks),
                "request and report are each a relayed trip; a window shorter than that drops real food on a slow link");
        }

        // ---- Wiring (source guards) ----------------------------------------------

        private static string Code(string path) =>
            string.Join("\n", File.ReadAllLines(path).Select(l =>
            {
                int slash = l.IndexOf("//", System.StringComparison.Ordinal);
                return slash >= 0 ? l.Substring(0, slash) : l;
            }));

        [Test]
        public void Peck_AsksThePile_AndNeverCreditsItself()
        {
            string peck = Code(Scripts + "Abilities/PeckAbilitySO.cs");
            StringAssert.Contains("cargo.RequestPeck(pile, take)", peck);
            Assert.IsFalse(Regex.IsMatch(peck, @"\.Cargo\s*\+="), "Peck credits its own cargo again: two peckers can both take the last grain.");
            Assert.Less(peck.IndexOf("SetMovementLock(ctx.Slot)", System.StringComparison.Ordinal),
                        peck.IndexOf("RequestPeck(", System.StringComparison.Ordinal), "the movement lock still comes first (balance)");

            string request = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RequestPeck(");
            Assert.Less(request.IndexOf("_pendingPecks.Record(", System.StringComparison.Ordinal),
                        request.IndexOf(".RPC_Drain(", System.StringComparison.Ordinal), "a local pile settles synchronously: record first");
            Assert.IsFalse(Regex.IsMatch(request, @"\bCargo\s*\+="), "the request credits nothing");

            string drain = AssassinExecuteCreditTests.MethodBody(PilePath, "public void RPC_Drain(");
            StringAssert.Contains("RpcInfo info", drain);
            StringAssert.Contains(".RPC_SettlePeck(", drain, "the pile reports what it really removed");

            string settle = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RPC_SettlePeck(");
            Assert.Less(settle.IndexOf("_pendingPecks.TryConsumeOldest(", System.StringComparison.Ordinal),
                        settle.IndexOf("Cargo +=", System.StringComparison.Ordinal), "no open request, no credit");
            StringAssert.Contains("FoodTransferRules.Credit(", settle);
        }

        [Test]
        public void Steal_AsksTheVictim_AndIsCreditedOnlyByItsReport()
        {
            string request = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RequestSteal(");
            Assert.Less(request.IndexOf("_pendingSteals.Record(", System.StringComparison.Ordinal),
                        request.IndexOf("RPC_DrainStolen(amount", System.StringComparison.Ordinal));
            Assert.IsFalse(Regex.IsMatch(request, @"\bCargo\s*\+="), "the thief credits itself from its estimate again");
            StringAssert.Contains("FreeSpace", request, "a multi-target steal cannot overbook the hold");

            string drain = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RPC_DrainStolen(");
            Assert.GreaterOrEqual(Regex.Matches(drain, @"ReportSteal\(").Count, 3,
                "a refusal (shell, wall), an empty load and a real drain each close the thief's request");
            StringAssert.Contains("ReportSteal(thief, actual)", drain, "the report carries the amount actually drained");

            string settle = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RPC_SettleSteal(");
            StringAssert.Contains("RpcInfo info", settle);
            Assert.Less(settle.IndexOf("_pendingSteals.TryConsumeOldest(", System.StringComparison.Ordinal),
                        settle.IndexOf("Cargo +=", System.StringComparison.Ordinal));
            StringAssert.Contains("_stealCreditFrame = Time.frameCount;", settle, "the thief's own +n is still routed to the kernel hold");
        }

        [Test]
        public void Deposit_IsAcknowledgedOrBounced_AndTheStatCountsOnlyBankedFood()
        {
            string flush = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void FlushBaseDeposit()");
            Assert.Less(flush.IndexOf("_pendingDeposits.Record(", System.StringComparison.Ordinal),
                        flush.IndexOf(".RPC_AddFood(", System.StringComparison.Ordinal));
            StringAssert.DoesNotContain("RecordDeposit", flush, "the stat is counted on the base's acknowledgement, not on sending");

            string add = AssassinExecuteCreditTests.MethodBody(BasePath, "public void RPC_AddFood(");
            Assert.GreaterOrEqual(Regex.Matches(add, @"RPC_SettleDeposit\(").Count, 2, "accepted and rejected flushes are both answered");
            StringAssert.Contains("banked: true", add);
            StringAssert.Contains("banked: false", add);
            Assert.Less(add.IndexOf("FoodTotal += amount", System.StringComparison.Ordinal),
                        add.IndexOf("banked: true", System.StringComparison.Ordinal));

            string settle = AssassinExecuteCreditTests.MethodBody(CargoPath, "public void RPC_SettleDeposit(");
            StringAssert.Contains("RpcInfo info", settle);
            StringAssert.Contains("_pendingDeposits.TryConsumeOldest(", settle);
            StringAssert.Contains("RecordDeposit(", settle);
            StringAssert.Contains("FoodTransferRules.SplitRefund(", settle);

            Assert.IsNull(typeof(ChickenMatchStats).GetMethod("RPC_AddDeposit"),
                "the unvalidated deposit-stat RPC is back: any peer could inflate anyone's deposits again.");
        }

        [Test]
        public void CargoIsCredited_OnlyInsideASettlement()
        {
            // Every `Cargo +=` in ChickenCargo sits in one of the three settle RPCs.
            string src = Code(CargoPath);
            int credits = Regex.Matches(src, @"(?<![\w.])Cargo\s*\+=").Count;
            int inSettles = new[] { "public void RPC_SettlePeck(", "public void RPC_SettleSteal(", "public void RPC_SettleDeposit(" }
                .Sum(sig => Regex.Matches(AssassinExecuteCreditTests.MethodBody(CargoPath, sig), @"(?<![\w.])Cargo\s*\+=").Count);
            Assert.AreEqual(3, inSettles, "each settlement credits once");
            Assert.AreEqual(inSettles, credits, "ChickenCargo credits cargo outside a settlement: that is an optimistic credit again.");
        }
    }
}
