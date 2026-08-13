using NUnit.Framework;
using CluckWars.Gameplay;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the two pure decision tables Stage 2 (v0.6 hold-to-aim, FEEDBACK.md §2/§6)
    /// factored out of <c>AbilityController</c> specifically so they could be tested
    /// without a <c>NetworkRunner</c>: <see cref="AbilityRefusalRules"/> (which refusal
    /// reason wins when several apply) and <see cref="AbilityHoldStateMachine"/> (what a
    /// tick of hold/release/cancel input resolves to). Both are the exact same code paths
    /// <c>AbilityController.TryActivate</c> / <c>FixedUpdateNetwork</c> use — these tests
    /// are pinning real behaviour, not a parallel reimplementation.
    /// </summary>
    public sealed class AbilityActivationRulesTests
    {
        // ---- AbilityRefusalRules — precedence table -----------------------------

        [Test]
        public void Refusal_NothingWrong_ReturnsNone()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: false, stunned: false, otherAbilityActive: false, noTarget: false);
            Assert.AreEqual(AbilityRefusal.None, r);
        }

        [Test]
        public void Refusal_SlotUnavailable_WinsOverEveryOtherReason()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: true, onCooldown: true, stunned: true, otherAbilityActive: true, noTarget: true);
            Assert.AreEqual(AbilityRefusal.SlotUnavailable, r);
        }

        [Test]
        public void Refusal_StunnedPlayerAlsoOnCooldown_ReportsCooldown()
        {
            // The exact scenario named in the Stage 2 spec: a stunned player whose slot
            // also happens to be on cooldown must report the SAME reason TryActivate
            // would actually refuse with. Cooldown outranks Stunned in the table, so
            // both this query and the real activation gate must agree on Cooldown.
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: true, stunned: true, otherAbilityActive: false, noTarget: false);
            Assert.AreEqual(AbilityRefusal.Cooldown, r);
        }

        [Test]
        public void Refusal_Cooldown_WinsOverStunnedOtherActiveAndNoTarget()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: true, stunned: true, otherAbilityActive: true, noTarget: true);
            Assert.AreEqual(AbilityRefusal.Cooldown, r);
        }

        [Test]
        public void Refusal_Stunned_WinsOverOtherActiveAndNoTarget()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: false, stunned: true, otherAbilityActive: true, noTarget: true);
            Assert.AreEqual(AbilityRefusal.Stunned, r);
        }

        [Test]
        public void Refusal_OtherAbilityActive_WinsOverNoTarget()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: false, stunned: false, otherAbilityActive: true, noTarget: true);
            Assert.AreEqual(AbilityRefusal.OtherAbilityActive, r);
        }

        [Test]
        public void Refusal_NoTarget_OnlyReported_WhenNothingElseApplies()
        {
            var r = AbilityRefusalRules.Evaluate(
                slotUnavailable: false, onCooldown: false, stunned: false, otherAbilityActive: false, noTarget: true);
            Assert.AreEqual(AbilityRefusal.NoTarget, r);
        }

        // ---- AbilityHoldStateMachine — tick decision ----------------------------

        private static readonly bool[] NoHold  = { false, false, false };
        private static readonly bool[] NoPress = { false, false, false };
        private static readonly bool[] AllCanBegin = { true, true, true };
        private static readonly bool[] NoneCanBegin = { false, false, false };

        private const int NoPending = AbilityHoldStateMachine.NoPendingSlot;

        /// <summary>The live tuning value, not a test-local copy — these tests must fail if
        /// the shipped threshold and the state machine ever stop agreeing.</summary>
        private const float Threshold = FeedbackTuning.TapHoldThresholdSeconds;

        /// <summary>Fusion's Shared-Mode tick length at 32 Hz, i.e. one <c>Runner.DeltaTime</c>
        /// worth of accumulation in <c>AbilityController.FixedUpdateNetwork</c>.</summary>
        private const float Tick = 1f / 32f;

        /// <summary>
        /// Argument-order shim so each test reads as "what the tick looked like" rather than
        /// as ten positional values. Defaults describe the common case: no hold pending, and
        /// the real shipped threshold.
        /// </summary>
        private static ChargeDecision Decide(
            byte chargingSlot, bool canCast, bool otherAbilityActive, bool cancelPressed,
            bool[] hold, bool[] press, bool[] canBeginCharge,
            int pendingSlot = NoPending, float pendingHeldSeconds = 0f)
            => AbilityHoldStateMachine.Decide(
                chargingSlot, pendingSlot, pendingHeldSeconds, Threshold,
                canCast, otherAbilityActive, cancelPressed, hold, press, canBeginCharge);

        // ---- Slot count is defined by the caller's arrays, not by a constant -----

        [Test]
        public void Decide_FourthSlot_IsReachable_WhenTheCallerPassesFourSlots()
        {
            // The state machine used to carry `private const int SlotCount = 3`, so growing
            // the loadout would have silently made slot 3 unpressable — the hold bit would
            // be set and simply never looked at. The count now comes from hold.Length.
            var hold  = new[] { false, false, false, true };
            var press = new[] { false, false, false, false };
            var can   = new[] { true, true, true, true };

            var d = Decide(0, true, false, false, hold, press, can);

            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(3, d.Slot, "Slot 3 must be reachable once four slots are passed in.");
        }

        [Test]
        public void Decide_StillWorks_WithThreeSlots_SoTheCountIsNotBakedIn()
        {
            // The mirror of the test above: passing three slots must still behave exactly as
            // it did before, or the array-length derivation has quietly become a 4-only path.
            var d = Decide(0, true, false, false, new[] { false, false, true }, NoPress, AllCanBegin);

            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(2, d.Slot);
        }

        [Test]
        public void Decide_RaggedArrays_RefuseTheTick_RatherThanIndexPastTheEnd()
        {
            // hold says four slots, press and canBeginCharge say three. Reading hold[3] and
            // then press[3] would throw; worse, a partial read could fire an ability the
            // player never pressed. The caller owns all three arrays and sizes them together,
            // so a mismatch is a bug — refuse the tick and stay silent rather than guess.
            var d = Decide(0, true, false, false,
                new[] { false, false, false, true }, NoPress, AllCanBegin);

            Assert.AreEqual(ChargeAction.None, d.Action);
        }

        [Test]
        public void Decide_Idle_NothingHeldOrPressed_ReturnsNone()
        {
            var d = Decide(0, true, false, false, NoHold, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.None, d.Action);
        }

        [Test]
        public void Decide_CannotCast_WithNoActiveCharge_IsANoOp()
        {
            // Stunned with nothing charging and nothing pending: nothing to cancel, nothing to begin.
            var d = Decide(0, false, false, false, new[] { true, false, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.None, d.Action);
        }

        [Test]
        public void Decide_CannotCast_MidHold_CancelsWithoutFiring()
        {
            // Caster gets stunned while charging slot 1 (chargingSlot=2). Must cancel,
            // never fire — this is the "cast becomes illegal mid-hold" case (§2.5/§2.7).
            var d = Decide(2, false, false, false, new[] { false, true, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_CannotCast_MidPendingHold_CancelsWithoutFiring()
        {
            // Same story one state earlier: the stun lands while the hold is still inside
            // the cost ramp and has drawn nothing yet. It must still be torn down, and it
            // must NOT be treated as a release — a stunned player never gets a free cast.
            var d = Decide(0, false, false, false, new[] { true, false, false }, NoPress, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: Tick);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_OtherAbilityBecameActive_MidHold_CancelsWithoutFiring()
        {
            var d = Decide(1, true, true, false, new[] { true, false, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_OtherAbilityBecameActive_MidPendingHold_CancelsWithoutFiring()
        {
            var d = Decide(0, true, true, false, new[] { true, false, false }, NoPress, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: Tick);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_CancelBit_WhileCharging_Cancels()
        {
            var d = Decide(3, true, false, cancelPressed: true,
                new[] { false, false, true }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_CancelBit_WhilePendingHold_Cancels()
        {
            // Touch drag-off (FeedbackTuning.DragCancelDistancePx) inside the cost ramp:
            // the gesture is abandoned, so it must cancel rather than fall through to the
            // idle scan and immediately re-pend the still-held button.
            var d = Decide(0, true, false, cancelPressed: true,
                new[] { true, false, false }, NoPress, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: 2f * Tick);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
        }

        [Test]
        public void Decide_CancelBit_WithNothingCharging_IsANoOp()
        {
            var d = Decide(0, true, false, cancelPressed: true, NoHold, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.None, d.Action, "Esc with nothing held must not spuriously do anything.");
        }

        // ---- Rising edge: a hold goes PENDING, never straight to charge ---------

        [Test]
        public void Decide_HoldRises_BeginsPendingHold_OnSlot0()
        {
            var d = Decide(0, true, false, false, new[] { true, false, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action,
                "A rising hold must NOT go straight to BeginCharge — that is the bug this model replaces " +
                "(every human tap paid for a telegraph flash and an aim-rotate movement lock).");
            Assert.AreEqual(0, d.Slot);
        }

        [Test]
        public void Decide_HoldRises_BeginsPendingHold_OnSlot1()
        {
            var d = Decide(0, true, false, false, new[] { false, true, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(1, d.Slot);
        }

        [Test]
        public void Decide_HoldRises_BeginsPendingHold_OnSlot2()
        {
            var d = Decide(0, true, false, false, new[] { false, false, true }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(2, d.Slot);
        }

        [Test]
        public void Decide_MultipleSlotsHeldAtOnce_Slot0TakesPriority()
        {
            // Mirrors the pre-hold-to-aim press priority (Ability1 > Ability2 > Ability3).
            var d = Decide(0, true, false, false, new[] { true, true, true }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(0, d.Slot);
        }

        // ---- The tap: under the threshold, fires and never charges --------------

        [Test]
        public void Decide_TapUnderThreshold_FiresOnRelease_WithoutEverCharging()
        {
            // The whole point of the model. Replays a human tap tick by tick and asserts
            // BeginCharge is never returned, so ChargingSlot never goes non-zero and none of
            // its four consumers (telegraph, target marks, wind-up glow, aim-rotate lock)
            // ever engage. "If not, there is speed."
            var held = new[] { true, false, false };

            var rise = Decide(0, true, false, false, held, new[] { true, false, false }, AllCanBegin);
            Assert.AreEqual(ChargeAction.BeginPendingHold, rise.Action);
            Assert.AreEqual(0, rise.Slot);

            // Two more ticks still held — 0.0625 s, comfortably inside the 0.12 s ramp.
            for (int tick = 1; tick <= 2; tick++)
            {
                var mid = Decide(0, true, false, false, held, NoPress, AllCanBegin,
                    pendingSlot: 0, pendingHeldSeconds: tick * Tick);
                Assert.AreEqual(ChargeAction.None, mid.Action,
                    $"tick {tick}: still inside the ramp — nothing to do but keep clocking.");
            }

            // Release.
            var release = Decide(0, true, false, false, NoHold, NoPress, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: 3f * Tick);
            Assert.AreEqual(ChargeAction.Fire, release.Action, "Release ALWAYS fires — there is no tap window to wait out.");
            Assert.AreEqual(0, release.Slot);
        }

        [Test]
        public void Decide_HoldPastThreshold_PromotesToChargeExactlyOnce()
        {
            var held = new[] { false, true, false };

            // Ticks 1..3 of accumulation are all under 0.12 s and must not promote.
            for (int tick = 1; tick <= 3; tick++)
            {
                var d = Decide(0, true, false, false, held, NoPress, AllCanBegin,
                    pendingSlot: 1, pendingHeldSeconds: tick * Tick);
                Assert.AreEqual(ChargeAction.None, d.Action,
                    $"tick {tick} ({tick * Tick:0.#####}s) is under the {Threshold}s ramp.");
            }

            // Tick 4 = 0.125 s, the first 32 Hz multiple that clears 0.12.
            var promote = Decide(0, true, false, false, held, NoPress, AllCanBegin,
                pendingSlot: 1, pendingHeldSeconds: 4f * Tick);
            Assert.AreEqual(ChargeAction.BeginCharge, promote.Action);
            Assert.AreEqual(1, promote.Slot);

            // Promotion is the caller's job to apply (it sets ChargingSlot and clears
            // pending), so the very next tick is a plain charging tick — not a second
            // BeginCharge. This is what makes "exactly once" structural.
            var after = Decide(2, true, false, false, held, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.None, after.Action);
        }

        [Test]
        public void Decide_PendingHeldExactlyAtThreshold_Promotes()
        {
            // Boundary choice is >=, not >. The caller accumulates in whole ticks, so exact
            // equality is genuinely reachable if the threshold is ever re-tuned to a tick
            // multiple; treating it as "not yet" would silently push the real boundary out
            // by a full tick without anyone editing the constant.
            var d = Decide(0, true, false, false, new[] { true, false, false }, NoPress, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: Threshold);
            Assert.AreEqual(ChargeAction.BeginCharge, d.Action);
            Assert.AreEqual(0, d.Slot);
        }

        [Test]
        public void Decide_ReleaseAfterCompletedHold_Fires()
        {
            // chargingSlot=2 means slot index 1 is charging (1-based encoding).
            var d = Decide(2, true, false, false, NoHold, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(1, d.Slot);
        }

        [Test]
        public void Decide_PressAndReleaseInsideOneTickGap_StillFires_NetworkTimingEdgeCase()
        {
            // NOT a description of human tapping — no thumb clears a 31 ms tick gap, and an
            // ordinary tap goes through the pending path (see
            // Decide_TapUnderThreshold_FiresOnRelease_WithoutEverCharging). This pins the
            // network-layer guarantee underneath it: when press AND release both land
            // between two simulation ticks, FusionNetworkService's press latch still
            // delivers the press edge with the hold bit already false, and that input must
            // fire rather than sit waiting for a hold that will never arrive. Renamed from
            // Decide_SubTickTap_FiresImmediately_WhenHoldNeverObserved, which read as though
            // it described the tap case it does not.
            var d = Decide(0, true, false, false, NoHold, new[] { false, true, false }, AllCanBegin);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(1, d.Slot);
        }

        [Test]
        public void Decide_HoldRises_ButSlotCannotChargeAndNoPress_QuietlyWaits()
        {
            // On cooldown / unavailable, held but no press this tick: must not spam a
            // refusal every tick the player keeps the dead button held down.
            var d = Decide(0, true, false, false, new[] { true, false, false }, NoPress, NoneCanBegin);
            Assert.AreEqual(ChargeAction.None, d.Action);
        }

        [Test]
        public void Decide_HoldRises_ButSlotCannotCharge_AndPressLandsSameTick_RefusesOnce()
        {
            // The very first tick of a doomed hold typically carries BOTH the edge press
            // and the live hold bit — must surface the refusal via TryActivate rather
            // than going silent. A dead slot never enters the pending state either.
            var d = Decide(0, true, false, false,
                new[] { true, false, false }, new[] { true, false, false }, NoneCanBegin);
            Assert.AreEqual(ChargeAction.RefuseAttempt, d.Action);
            Assert.AreEqual(0, d.Slot);
        }

        [Test]
        public void Decide_HoldStillDown_OnChargingSlot_KeepsCharging()
        {
            var d = Decide(1, true, false, false, new[] { true, false, false }, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.None, d.Action, "Still held — not time to fire yet.");
        }

        [Test]
        public void Decide_HoldFalls_OnChargingSlot0_EncodingRoundTrips()
        {
            // ChargingSlot's 1-based encoding for slot 0 is 1, not 0 (0 means "nothing
            // charging") — the off-by-one this whole state machine hinges on. The pending
            // slot uses the opposite convention (plain 0-based index, -1 for none), so this
            // also guards the two encodings never being confused for each other.
            var d = Decide(1, true, false, false, NoHold, NoPress, AllCanBegin);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(0, d.Slot);
        }

        [Test]
        public void Decide_ChargingSlotOutranksPendingSlot()
        {
            // Both states set at once should be unreachable — the caller clears pending on
            // promotion — but if it ever happens, the committed, already-drawn charge is the
            // gesture the player can see, so it must win rather than the invisible one.
            var d = Decide(1, true, false, false, NoHold, NoPress, AllCanBegin,
                pendingSlot: 2, pendingHeldSeconds: 10f);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(0, d.Slot, "Fired the charging slot (encoding 1 → index 0), not the pending one.");
        }

        [Test]
        public void Decide_PendingHold_IgnoresPressesOnOtherSlots()
        {
            // One live gesture at a time: a stray press on another slot mid-ramp must not
            // pre-empt the gesture in flight.
            var d = Decide(0, true, false, false, new[] { true, false, false }, new[] { false, true, false }, AllCanBegin,
                pendingSlot: 0, pendingHeldSeconds: Tick);
            Assert.AreEqual(ChargeAction.None, d.Action);
        }
    }
}
