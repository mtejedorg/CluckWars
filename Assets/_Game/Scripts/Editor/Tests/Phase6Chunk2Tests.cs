using System;
using System.IO;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 2: fizzle (A3), the touch edge-band cancel (A4) and keyboard / multi-touch slot switching.
    /// Everything here is the pure layer (<see cref="AbilityHoldStateMachine"/>, <see cref="AbilityFizzleRules"/>,
    /// <see cref="HoldCancelRules"/>) the live code calls, plus source guards for the wiring that needs a runner.
    /// </summary>
    public sealed class Phase6Chunk2Tests
    {
        private const float Tick = 1f / 32f;
        private const float Threshold = FeedbackTuning.TapHoldThresholdSeconds;

        private static bool[] Slots(params int[] on)
        {
            var a = new bool[4];
            foreach (int i in on) a[i] = true;
            return a;
        }

        private static readonly bool[] All = { true, true, true, true };

        private static ChargeDecision Decide(byte charging, int pending, float pendingSeconds, bool cancel,
                                             bool[] hold, bool[] press, bool[] canBegin) =>
            AbilityHoldStateMachine.Decide(charging, pending, pendingSeconds, Threshold,
                canCast: true, cancelPressed: cancel, hold, press, canBegin);

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Fizzle classification -------------------------------------------------------------

        [Test]
        public void Fizzle_IsExactlyTheNoTargetRefusal()
        {
            Assert.IsTrue(AbilityFizzleRules.IsFizzle(AbilityRefusal.NoTarget));
            foreach (var other in new[] { AbilityRefusal.None, AbilityRefusal.Cooldown, AbilityRefusal.Stunned,
                                          AbilityRefusal.OtherAbilityActive, AbilityRefusal.SlotUnavailable })
                Assert.IsFalse(AbilityFizzleRules.IsFizzle(other), $"{other} keeps today's denied bump");
        }

        [Test]
        public void Fizzle_HoldRelease_FromPending_EndsInFire_ThenClassifiesAsFizzle()
        {
            // Pending hold released inside the ramp: Fire -> TryActivate.
            var d = Decide(0, 1, 2 * Tick, false, Slots(), Slots(), All);
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(1, d.Slot);
            // Nobody in range: IsUsable is false -> the refusal table says NoTarget -> fizzle.
            var refusal = AbilityRefusalRules.Evaluate(false, false, false, noTarget: true);
            Assert.IsTrue(AbilityFizzleRules.IsFizzle(refusal));
        }

        [Test]
        public void Fizzle_HoldRelease_FromCharging_EndsInFire_ThenClassifiesAsFizzle()
        {
            var d = Decide(3, -1, 0f, false, Slots(), Slots(), All); // charging slot 2 (1-based 3), hold gone
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(2, d.Slot);
            Assert.IsTrue(AbilityFizzleRules.IsFizzle(AbilityRefusalRules.Evaluate(false, false, false, true)));
        }

        [Test]
        public void Fizzle_SubTickQuickTap_PressWithoutHold_EndsInFire_ThenClassifiesAsFizzle()
        {
            var d = Decide(0, -1, 0f, false, Slots(), Slots(0), All);
            Assert.AreEqual(ChargeAction.Fire, d.Action, "the idle press branch fires like a release");
            Assert.AreEqual(0, d.Slot);
            Assert.IsTrue(AbilityFizzleRules.IsFizzle(AbilityRefusalRules.Evaluate(false, false, false, true)));
        }

        [Test]
        public void Fizzle_CooldownAndStunnedStayDenied_EvenWhenNoTargetToo()
        {
            // Precedence: a cooling slot with nobody in range is a Cooldown refusal, not a fizzle.
            Assert.AreEqual(AbilityRefusal.Cooldown, AbilityRefusalRules.Evaluate(false, true, false, true));
            Assert.AreEqual(AbilityRefusal.Stunned, AbilityRefusalRules.Evaluate(false, false, true, true));
            Assert.IsFalse(AbilityFizzleRules.IsFizzle(AbilityRefusalRules.Evaluate(false, true, false, true)));
            Assert.IsFalse(AbilityFizzleRules.IsFizzle(AbilityRefusalRules.Evaluate(false, false, true, true)));
        }

        [Test]
        public void Fizzle_BurnsNoCooldown_AndIsNotAMoveUsed_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int fizzle = src.IndexOf("_fizzlePending = true;", StringComparison.Ordinal);
            int cooldown = src.IndexOf("SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ResolveCooldownFor(ability)));",
                                       StringComparison.Ordinal);
            int active = src.IndexOf("ActiveMask = AbilityRunRules.Begin(ActiveMask, slot);", fizzle, StringComparison.Ordinal);
            Assert.Greater(fizzle, 0);
            Assert.Greater(cooldown, fizzle, "the fizzle branch returns before any cooldown is started");
            Assert.Greater(active, fizzle, "...and before the slot becomes active (chunk 3's stealth-ending sits below)");
            string branch = src.Substring(fizzle, cooldown - fizzle);
            StringAssert.Contains("return;", branch.Substring(0, branch.IndexOf("_deniedPressPending", StringComparison.Ordinal)));
        }

        // ---- Re-arm ---------------------------------------------------------------------------

        [Test]
        public void Rearm_LastsPointFourSeconds_ThenEnds()
        {
            Assert.AreEqual(0.4f, AbilityFizzleRules.RearmSeconds, 1e-6f);
            float remaining = AbilityFizzleRules.Begin();
            Assert.IsTrue(AbilityFizzleRules.IsRearming(remaining));

            int ticks = 0;
            while (AbilityFizzleRules.IsRearming(remaining))
            {
                remaining = AbilityFizzleRules.Advance(remaining, Tick);
                ticks++;
                Assert.Less(ticks, 100);
            }
            // 0.4 s at 32 Hz is 12.8 ticks: still re-arming after 12, free on the 13th.
            Assert.AreEqual(13, ticks);
            Assert.IsFalse(AbilityFizzleRules.IsRearming(AbilityFizzleRules.Advance(0f, Tick)));
            Assert.IsTrue(AbilityFizzleRules.IsRearming(AbilityFizzleRules.Advance(AbilityFizzleRules.Begin(), 0.39f)));
            Assert.IsFalse(AbilityFizzleRules.IsRearming(AbilityFizzleRules.Advance(AbilityFizzleRules.Begin(), 0.4f)));
        }

        [Test]
        public void Rearm_SlotCannotBeginAHold_ButOtherSlotsCan()
        {
            // The controller feeds canBeginCharge = ready && !rearming. A rearming slot held: dead this tick (no
            // pending hold, no spam); the neighbour is unaffected.
            var canBegin = new[] { false, true, true, true };
            Assert.AreEqual(ChargeAction.None, Decide(0, -1, 0f, false, Slots(0), Slots(), canBegin).Action);
            Assert.AreEqual(ChargeAction.BeginPendingHold, Decide(0, -1, 0f, false, Slots(1), Slots(), canBegin).Action);
            // A fresh press on the rearming slot is a visible refusal (and TryActivate re-checks the rearm).
            Assert.AreEqual(ChargeAction.RefuseAttempt, Decide(0, -1, 0f, false, Slots(0), Slots(0), canBegin).Action);
        }

        [Test]
        public void Rearm_IsEnforcedInTryActivate_ForTheTapPath_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int start = src.IndexOf("private void TryActivate(int slot)", StringComparison.Ordinal);
            Assert.Greater(start, 0);
            int rearm = src.IndexOf("IsRearming(slot)", start, StringComparison.Ordinal);
            int eval = src.IndexOf("EvaluateRefusalInternal(slot, out var ability)", start, StringComparison.Ordinal);
            Assert.Greater(rearm, start);
            Assert.Less(rearm, eval, "the re-arm gate runs before the refusal table, so no path can skip it");
        }

        // ---- Edge-band cancel hysteresis --------------------------------------------------------

        [Test]
        public void EdgeBand_Arms_At19Dp_StaysArmedAt25Dp_DisarmsAt29Dp()
        {
            Assert.IsTrue(HoldCancelRules.NextArmed(false, 19f), "19 dp from the edge arms");
            Assert.IsFalse(HoldCancelRules.NextArmed(false, 25f), "25 dp does not arm from idle");
            Assert.IsTrue(HoldCancelRules.NextArmed(true, 25f), "25 dp stays armed (hysteresis)");
            Assert.IsFalse(HoldCancelRules.NextArmed(true, 29f), "29 dp disarms");
            Assert.IsTrue(HoldCancelRules.NextArmed(false, HoldCancelRules.ArmDp));
            Assert.IsTrue(HoldCancelRules.NextArmed(true, HoldCancelRules.DisarmDp));
        }

        [Test]
        public void EdgeBand_Evaluate_UsesTheRawScreenEdge_AtTheDeviceDpi()
        {
            // 420 dpi -> 1 dp = 2.625 px, so the 20 dp arm line is 52.5 px from the physical edge.
            const float w = 2400f, h = 1080f, dpi = 420f;
            Assert.IsTrue(HoldCancelRules.Evaluate(false, new Vector2(w - 50f, 500f), w, h, dpi, out var edge));
            Assert.AreEqual(ScreenEdge.Right, edge);
            Assert.IsFalse(HoldCancelRules.Evaluate(false, new Vector2(w - 60f, 500f), w, h, dpi, out _), "60 px = 22.9 dp");
            Assert.IsTrue(HoldCancelRules.Evaluate(true, new Vector2(w - 60f, 500f), w, h, dpi, out _), "...but stays armed once armed");
            Assert.IsFalse(HoldCancelRules.Evaluate(true, new Vector2(w - 80f, 500f), w, h, dpi, out _), "80 px = 30.5 dp disarms");
            Assert.IsTrue(HoldCancelRules.Evaluate(false, new Vector2(500f, 40f), w, h, dpi, out edge));
            Assert.AreEqual(ScreenEdge.Top, edge);
            Assert.IsTrue(HoldCancelRules.Evaluate(false, new Vector2(30f, 500f), w, h, dpi, out edge));
            Assert.AreEqual(ScreenEdge.Left, edge);
            Assert.IsTrue(HoldCancelRules.Evaluate(false, new Vector2(500f, h - 20f), w, h, dpi, out edge));
            Assert.AreEqual(ScreenEdge.Bottom, edge);
        }

        [Test]
        public void EdgeBand_UnknownDpi_FallsBackTo160()
        {
            Assert.AreEqual(160f, HoldCancelRules.SanitiseDpi(0f));
            Assert.AreEqual(160f, HoldCancelRules.SanitiseDpi(-1f));
            Assert.AreEqual(20f, HoldCancelRules.PxToDp(20f, 0f), 1e-4f, "1 dp = 1 px at the fallback");
            Assert.IsTrue(HoldCancelRules.Evaluate(false, new Vector2(15f, 300f), 1920f, 1080f, 0f, out _));
        }

        [Test]
        public void EdgeBand_CentreOfTheScreen_NeverArms()
        {
            Assert.IsFalse(HoldCancelRules.Evaluate(false, new Vector2(960f, 540f), 1920f, 1080f, 420f, out _));
        }

        // ---- PointerCancel / capture loss never fire -------------------------------------------------

        [Test]
        public void Release_PointerCancelAndCaptureOut_AreAlwaysCancel_NeverFire()
        {
            foreach (bool armed in new[] { false, true })
            {
                Assert.AreEqual(HexRelease.Cancel, HoldCancelRules.ResolveRelease(HexPointerEnd.Cancel, armed));
                Assert.AreEqual(HexRelease.Cancel, HoldCancelRules.ResolveRelease(HexPointerEnd.CaptureOut, armed));
            }
        }

        [Test]
        public void Release_Lift_FiresUnlessArmed()
        {
            Assert.AreEqual(HexRelease.Fire, HoldCancelRules.ResolveRelease(HexPointerEnd.Up, armed: false));
            Assert.AreEqual(HexRelease.Cancel, HoldCancelRules.ResolveRelease(HexPointerEnd.Up, armed: true));
        }

        [Test]
        public void TouchController_HandlesPointerCancel_AndHasNoRadialDragCancel_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("RegisterCallback<PointerCancelEvent>", src);
            StringAssert.Contains("HoldCancelRules.ResolveRelease(end, _cancelArmed[slot])", src);
            StringAssert.DoesNotContain("DragCancelDistancePx", src);
            StringAssert.DoesNotContain("DragCancelDistancePx", Read("Assets/_Game/Scripts/Visuals/FeedbackTuning.cs"));
            StringAssert.Contains("OnCancelArmed(", src, "the chunk-6 haptic call site");
        }

        [Test]
        public void CancelledHold_ReachesTheStateMachineAsCancel_NotFire()
        {
            // Release while armed sets hold=false AND the cancel bit in the same tick. Cancel must win in both
            // pre-fire states, so the move is never fired.
            Assert.AreEqual(ChargeAction.Cancel, Decide(0, 0, 3 * Tick, true, Slots(), Slots(), All).Action, "pending");
            Assert.AreEqual(ChargeAction.Cancel, Decide(1, -1, 0f, true, Slots(), Slots(), All).Action, "charging");
        }

        // ---- Slot switching + suppression ------------------------------------------------------------

        [Test]
        public void Switch_PressingAnotherSlot_WhilePending_SwitchesToIt()
        {
            var d = Decide(0, 0, 2 * Tick, false, Slots(0, 2), Slots(2), All);
            Assert.AreEqual(ChargeAction.SwitchHold, d.Action);
            Assert.AreEqual(2, d.Slot);
            Assert.AreEqual(0, d.FromSlot);
        }

        [Test]
        public void Switch_PressingAnotherSlot_WhileCharging_SwitchesToIt()
        {
            var d = Decide(1, -1, 0f, false, Slots(0, 3), Slots(3), All); // charging slot 0; press slot 3
            Assert.AreEqual(ChargeAction.SwitchHold, d.Action);
            Assert.AreEqual(3, d.Slot);
            Assert.AreEqual(0, d.FromSlot);
        }

        [Test]
        public void Switch_ReleaseOfTheCurrentSlot_WinsOverASwitchPress()
        {
            var d = Decide(0, 0, 2 * Tick, false, Slots(2), Slots(2), All); // slot 0 released, slot 2 pressed
            Assert.AreEqual(ChargeAction.Fire, d.Action);
            Assert.AreEqual(0, d.Slot);
        }

        [Test]
        public void Switch_BeatsPromotionOnTheSameTick()
        {
            var d = Decide(0, 0, Threshold, false, Slots(0, 1), Slots(1), All);
            Assert.AreEqual(ChargeAction.SwitchHold, d.Action);
        }

        [Test]
        public void Switch_DeadSlotNeverStealsTheGesture()
        {
            var canBegin = new[] { true, false, true, true };
            var d = Decide(0, 0, 2 * Tick, false, Slots(0, 1), Slots(1), canBegin);
            Assert.AreEqual(ChargeAction.None, d.Action, "slot 2 is cooling / rearming: the current aim continues");
        }

        [Test]
        public void Switch_NoOtherPress_KeepsClockingThePendingHold()
        {
            Assert.AreEqual(ChargeAction.None, Decide(0, 0, Tick, false, Slots(0, 1), Slots(), All).Action,
                "another slot merely HELD (already down) is not a press");
        }

        [Test]
        public void Suppression_OldSlotIsIgnoredUntilItsKeyIsReleased()
        {
            // After switching 0 -> 2, the old key is still down. It must read as released/not pressed, so the
            // idle scan cannot hand the tick back to it when slot 2 is let go.
            var suppressed = new[] { true, false, false, false };

            var hold = Slots(0, 2); var press = Slots();
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.IsFalse(hold[0], "suppressed slot reads as not held");
            Assert.IsTrue(hold[2]);
            Assert.IsTrue(suppressed[0], "still suppressed while its key is down");

            // Slot 2 (pending) is released while the old key is still down: slot 2 fires.
            Assert.AreEqual(ChargeAction.Fire, Decide(0, 2, 2 * Tick, false, Slots(0), Slots(), All).Action);

            // Next idle tick: the old key is STILL down. Unmasked it would claim the tick and start a new pending
            // hold that fires on release; masked, nothing happens.
            Assert.AreEqual(ChargeAction.BeginPendingHold, Decide(0, -1, 0f, false, Slots(0), Slots(), All).Action,
                "(control: without suppression the old key would re-arm)");
            hold = Slots(0); press = Slots();
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.AreEqual(ChargeAction.None, Decide(0, -1, 0f, false, hold, press, All).Action,
                "after slot 2 is gone, the still-down old key must NOT start a new pending hold");

            // The key finally comes up: suppression clears.
            hold = Slots(); press = Slots();
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.IsFalse(suppressed[0]);

            // And a fresh press afterwards works normally.
            hold = Slots(0); press = Slots(0);
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.AreEqual(ChargeAction.BeginPendingHold, Decide(0, -1, 0f, false, hold, press, All).Action);
        }

        [Test]
        public void Suppression_AlsoMasksAPressEdgeOnTheSuppressedSlot()
        {
            var suppressed = new[] { false, true, false, false };
            var hold = Slots(1); var press = Slots(1);
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.IsFalse(press[1]);
            Assert.AreEqual(ChargeAction.None, Decide(0, -1, 0f, false, hold, press, All).Action);
        }

        [Test]
        public void Cancel_WithTheKeyStillDown_DoesNotReArmThePendingHold()
        {
            // Esc / right mouse while Q is held: Cancel, and the controller suppresses the live slot, so the
            // still-down Q does not immediately start a new pending hold (which would fire on release).
            var d = Decide(0, 0, 2 * Tick, true, Slots(0), Slots(), All);
            Assert.AreEqual(ChargeAction.Cancel, d.Action);
            Assert.AreEqual(0, AbilityHoldStateMachine.LiveSlot(0, 0));

            var suppressed = new[] { true, false, false, false };
            var hold = Slots(0); var press = Slots();
            AbilityHoldStateMachine.ApplySuppression(hold, press, suppressed);
            Assert.AreEqual(ChargeAction.None, Decide(0, -1, 0f, false, hold, press, All).Action);
        }

        [Test]
        public void LiveSlot_ChargingBeatsPending_AndIdleIsMinusOne()
        {
            Assert.AreEqual(2, AbilityHoldStateMachine.LiveSlot(3, 1));
            Assert.AreEqual(1, AbilityHoldStateMachine.LiveSlot(0, 1));
            Assert.AreEqual(-1, AbilityHoldStateMachine.LiveSlot(0, -1));
        }

        [Test]
        public void Idle_LowestHeldSlotStillClaimsTheTick()
        {
            var d = Decide(0, -1, 0f, false, Slots(1, 3), Slots(), All);
            Assert.AreEqual(ChargeAction.BeginPendingHold, d.Action);
            Assert.AreEqual(1, d.Slot);
        }

        // ---- Provider contract -----------------------------------------------------------------------

        [Test]
        public void KeyboardProvider_ReadsRightMouseForCancel_AndIsNeverArmed_SourceGuard()
        {
            string src = Read("Assets/_Game/Scripts/Input/KeyboardInputProvider.cs");
            StringAssert.Contains("rightButton.wasPressedThisFrame", src);
            StringAssert.Contains("IsAbilityCancelArmed() => false", src);
            // GetBackPressed (menu back) must stay Esc only.
            int back = src.IndexOf("public bool GetBackPressed()", StringComparison.Ordinal);
            StringAssert.DoesNotContain("rightButton", src.Substring(back));
        }

        [Test]
        public void CancelArmed_FlowsThroughTheComposite_AndTheTelegraphReadsIt()
        {
            var armed = new FakeProvider { Armed = true };
            var idle = new FakeProvider();
            Assert.IsTrue(new CompositeInputProvider(idle, armed).IsAbilityCancelArmed());
            Assert.IsFalse(new CompositeInputProvider(idle, new FakeProvider()).IsAbilityCancelArmed());
            Assert.IsFalse(new CompositeInputProvider().IsAbilityCancelArmed());

            string tele = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            StringAssert.Contains("_input.IsAbilityCancelArmed()", tele);
        }

        private sealed class FakeProvider : IInputProvider
        {
            public bool Armed;
            public Vector2 GetMovement() => Vector2.zero;
            public bool GetAbility1Pressed() => false;
            public bool GetAbility2Pressed() => false;
            public bool GetAbility3Pressed() => false;
            public bool GetAbility4Pressed() => false;
            public bool GetAbilityHeld(int slot) => false;
            public bool GetAbilityCancelPressed() => false;
            public bool GetBackPressed() => false;
            public bool IsAbilityCancelArmed() => Armed;
        }

        // ---- Audio wiring ----------------------------------------------------------------------------

        [Test]
        public void AudioRegistry_HasFizzleAndCancelClips_Wired()
        {
            string asset = Read("Assets/_Game/Data/AudioRegistry.asset");
            StringAssert.Contains("AbilityFizzle: {fileID: 8300000, guid:", asset);
            StringAssert.Contains("AbilityCancel: {fileID: 8300000, guid:", asset);
            Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Assets/_Game/Audio/Abilities/ability_fizzle.wav")));
            Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Assets/_Game/Audio/Abilities/ability_cancel.wav")));
        }
    }
}
