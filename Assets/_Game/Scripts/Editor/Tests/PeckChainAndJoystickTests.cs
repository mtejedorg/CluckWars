using System;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Networking;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>Phase 6 chunk 4: the Peck auto-chain, Auto-Peck, its input bit, and the touch stick dead zone.</summary>
    public sealed class PeckChainAndJoystickTests
    {
        // A chain that is running happily: every condition holds and Peck is ready.
        private static PeckChainInputs Healthy() => new PeckChainInputs
        {
            Active = true, MatchRunning = true, Alive = true, Moving = false, CargoFull = false,
            PileInReach = true, CanCast = true, OtherAbilityCast = false, PeckReady = true,
        };

        [Test]
        public void Chain_Healthy_FiresWhenReady_AndWaitsOnCooldown()
        {
            var s = Healthy();
            Assert.AreEqual(PeckChainAction.Fire, PeckChainRules.Decide(s, out var stop));
            Assert.AreEqual(PeckChainStop.None, stop);

            s.PeckReady = false;
            Assert.AreEqual(PeckChainAction.Wait, PeckChainRules.Decide(s, out stop));
            Assert.AreEqual(PeckChainStop.None, stop);
        }

        [Test]
        public void Chain_EachStopCondition_Stops_WithItsReason()
        {
            AssertStops(s => { s.MatchRunning = false; return s; }, PeckChainStop.MatchEnded);
            AssertStops(s => { s.Alive = false; return s; }, PeckChainStop.Dead);
            AssertStops(s => { s.CanCast = false; return s; }, PeckChainStop.Stunned);
            AssertStops(s => { s.Knocked = true; return s; }, PeckChainStop.Knocked);
            AssertStops(s => { s.OtherAbilityCast = true; return s; }, PeckChainStop.OtherCast);
            AssertStops(s => { s.Moving = true; return s; }, PeckChainStop.Moved);
            AssertStops(s => { s.CargoFull = true; return s; }, PeckChainStop.CargoFull);
            AssertStops(s => { s.PileInReach = false; return s; }, PeckChainStop.NoPile);
        }

        private static void AssertStops(Func<PeckChainInputs, PeckChainInputs> mutate, PeckChainStop expected)
        {
            var s = mutate(Healthy());
            Assert.AreEqual(PeckChainAction.Stop, PeckChainRules.Decide(s, out var stop), expected.ToString());
            Assert.AreEqual(expected, stop);

            // A stop is never reported as Wait, even while Peck is still cooling.
            s.PeckReady = false;
            Assert.AreEqual(PeckChainAction.Stop, PeckChainRules.Decide(s, out _), expected + " while cooling");
        }

        [Test]
        public void Chain_Knockback_StopsIt_ButNotWhenNoEventLanded()
        {
            var s = Healthy();
            s.Knocked = false; // a body push raises no KnockbackEventId, so the flag stays false
            Assert.AreEqual(PeckChainAction.Fire, PeckChainRules.Decide(s, out _));
            s.Knocked = true;
            Assert.AreEqual(PeckChainAction.Stop, PeckChainRules.Decide(s, out var stop));
            Assert.AreEqual(PeckChainStop.Knocked, stop);
        }

        [Test]
        public void Chain_FullCargoWithNoPileUsable_ReadsCargoFull_NotNoPile()
        {
            // Peck IsUsable is false when the hold is full, so the pile flag drops too; the sound-bearing reason wins.
            var s = Healthy();
            s.CargoFull = true;
            s.PileInReach = false;
            PeckChainRules.Decide(s, out var stop);
            Assert.AreEqual(PeckChainStop.CargoFull, stop);
        }

        [Test]
        public void Chain_NeverStartsOrRestartsItself_AfterAStopOrASteal()
        {
            // Not active (stopped earlier, e.g. cargo full) is Idle, however favourable everything now looks:
            // a steal emptied the hold and the pile is still in reach.
            var s = Healthy();
            s.Active = false;
            Assert.AreEqual(PeckChainAction.Idle, PeckChainRules.Decide(s, out var stop));
            Assert.AreEqual(PeckChainStop.None, stop);

            s.CargoFull = false;
            s.PeckReady = true;
            Assert.AreEqual(PeckChainAction.Idle, PeckChainRules.Decide(s, out _));
        }

        [Test]
        public void MoveInput_OnlyInputCounts_ShoveIsNotInput()
        {
            Assert.IsFalse(PeckChainRules.IsMoving(Vector2.zero), "no input");
            Assert.IsTrue(PeckChainRules.IsMoving(new Vector2(0.01f, 0f)), "any input after the provider dead zone");
            Assert.IsTrue(PeckChainRules.IsMoving(new Vector2(0f, -1f)));
        }

        [Test]
        public void AutoPeck_IdleTimer_NoStartAt017_StartsAt018_AnyMoveResets()
        {
            Assert.IsFalse(AutoStart(0.17f), "0.17 s is too soon");
            Assert.IsTrue(AutoStart(PeckChainRules.AutoPeckIdleSeconds), "0.18 s starts");
            Assert.AreEqual(0.18f, PeckChainRules.AutoPeckIdleSeconds);

            Assert.AreEqual(0.5f, PeckChainRules.AdvanceIdle(0.4f, moving: false, dt: 0.1f), 1e-6f);
            Assert.AreEqual(0f, PeckChainRules.AdvanceIdle(0.4f, moving: true, dt: 0.03125f), "any move input resets");

            // Real tick cadence: 5 ticks (0.15625 s) is not enough, 6 (0.1875 s) is.
            float t = 0f;
            for (int i = 0; i < 5; i++) t = PeckChainRules.AdvanceIdle(t, false, 1f / 32f);
            Assert.IsFalse(AutoStart(t));
            t = PeckChainRules.AdvanceIdle(t, false, 1f / 32f);
            Assert.IsTrue(AutoStart(t));
        }

        [Test]
        public void AutoPeck_NeedsTheBit_AndEveryManualPeckCondition()
        {
            const float idle = 0.2f;
            Assert.IsTrue(AutoStart(idle));
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(false, idle, false, true, false, true, true, false, false, false), "bit off");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, true, true, false, true, true, false, false, false), "chain already running");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, false, false, true, true, false, false, false), "no pile");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, true, true, true, false, false, false), "cargo full");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, false, false, true, false, false, false), "stunned");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, false, true, false, false, false, false), "cooling");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, false, true, true, true, false, false), "another move this tick");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, false, true, true, false, true, false), "mid-aim");
            Assert.IsFalse(PeckChainRules.ShouldAutoStart(true, idle, false, true, false, true, true, false, false, true), "would break stealth");
        }

        private static bool AutoStart(float idle) =>
            PeckChainRules.ShouldAutoStart(true, idle, false, true, false, true, true, false, false, false);

        [Test]
        public void AutoPeckInputBit_IsAppended_AfterEveryExistingBit_AndFitsInt32()
        {
            int autoPeck = (int)InputButton.AutoPeck;
            foreach (InputButton b in Enum.GetValues(typeof(InputButton)))
            {
                if (b == InputButton.AutoPeck) continue;
                Assert.Greater(autoPeck, (int)b, $"AutoPeck must be appended after {b}");
            }
            Assert.Less(autoPeck, 32, "NetworkButtons is Int32-backed");
            Assert.AreEqual(8, (int)InputButton.AbilityHold4, "existing bits are never renumbered");
        }

        [Test]
        public void Joystick_DeadZone_RampsFromZeroAtTheEdge_ToOne_Monotonically()
        {
            Assert.AreEqual(0f, JoystickRules.ApplyDeadZone(new Vector2(0.1f, 0f)).magnitude, 1e-6f);
            Assert.AreEqual(0f, JoystickRules.ApplyDeadZone(new Vector2(0.12f, 0f)).magnitude, 1e-6f);
            Assert.AreEqual(1f, JoystickRules.ApplyDeadZone(new Vector2(1f, 0f)).magnitude, 1e-6f);
            Assert.AreEqual(1f, JoystickRules.ApplyDeadZone(new Vector2(0f, -1.2f)).magnitude, 1e-6f, "over-throw clamps to 1");

            float prev = 0f;
            for (float m = 0f; m <= 1.0001f; m += 0.01f)
            {
                float o = JoystickRules.ApplyDeadZone(new Vector2(m, 0f)).magnitude;
                Assert.GreaterOrEqual(o, prev - 1e-6f, $"not monotonic at {m}");
                prev = o;
            }

            // Direction survives the remap.
            var diag = JoystickRules.ApplyDeadZone(new Vector2(0.5f, 0.5f));
            Assert.AreEqual(diag.x, diag.y, 1e-6f);
            Assert.Greater(diag.x, 0f);
        }

        [Test]
        public void Joystick_Throw_Is60Dp_WithDpiFallback_AndNeverBelowTheOldReach()
        {
            // Pixel 9 landscape: ~422 dpi, 2424 px wide screen, panel ~2158 wide (1 dp is ~2.3 panel px there).
            float pixel9 = JoystickRules.ThrowRadiusPanelPx(422f, 2158f, 2424f);
            Assert.GreaterOrEqual(pixel9, 60f * 2.2f);

            // Unknown dpi falls back to 160 (1 dp = 1 px) and the floor keeps today's reach.
            Assert.AreEqual(JoystickRules.MinThrowPanelPx, JoystickRules.ThrowRadiusPanelPx(0f, 1920f, 1920f));
            Assert.GreaterOrEqual(JoystickRules.ThrowRadiusPanelPx(96f, 1920f, 1920f), JoystickRules.MinThrowPanelPx);
        }
    }
}
