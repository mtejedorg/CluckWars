using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A1 (decisions 2026-10-21): re-applying a stun, root or timed slow may extend it, never cut it
    /// short. Before WP1 each RPC overwrote its timer, so a 1.5 s Wing Slam stun followed by a 0.35 s
    /// Headbutt stagger left the victim stunned for 0.35 s.
    /// </summary>
    /// <remarks>
    /// The rule tests pin <see cref="ControlReapplyRules"/>; the structural tests at the end pin
    /// that all three RPCs route through it (they fail on the old replace-the-timer bodies).
    /// </remarks>
    public sealed class ControlReapplyRulesTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";

        // ---- Stun / root: max(remaining, incoming) ----------------------------------------

        [Test]
        public void Timer_LongStunThenShortStagger_KeepsTheLongStun()
        {
            // The case in the decision: 1.5 then 0.35 -> 1.5. The old code answered 0.35.
            Assert.AreEqual(1.5f, ControlReapplyRules.MergeTimer(1.5f, 0.35f), 1e-6f);
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(1.5f, 0.35f),
                "a 0.35 s stagger must not restart (shorten) a stun with 1.5 s left");
        }

        [Test]
        public void Timer_ShortStunThenLongStun_TakesTheLongStun()
        {
            Assert.AreEqual(1.5f, ControlReapplyRules.MergeTimer(0.35f, 1.5f), 1e-6f);
            Assert.IsTrue(ControlReapplyRules.IncomingExtends(0.35f, 1.5f));
        }

        [Test]
        public void Timer_NothingRunning_TakesTheIncoming()
        {
            Assert.AreEqual(1.8f, ControlReapplyRules.MergeTimer(0f, 1.8f), 1e-6f);
            Assert.IsTrue(ControlReapplyRules.IncomingExtends(0f, 1.8f));
        }

        [Test]
        public void Timer_ZeroIncoming_FromAnImmunityWindow_NeverRestarts()
        {
            // ApplyPassiveControlDuration returns 0 while Immovable is open.
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(0f, 0f));
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(1f, 0f));
            Assert.AreEqual(1f, ControlReapplyRules.MergeTimer(1f, 0f), 1e-6f);
        }

        [Test]
        public void Timer_NaNAndNegative_ReadAsZero()
        {
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(1f, float.NaN));
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(1f, -3f));
            Assert.IsTrue(ControlReapplyRules.IncomingExtends(float.NaN, 0.5f));
            Assert.AreEqual(0f, ControlReapplyRules.MergeTimer(float.NaN, float.NaN), 1e-6f);
        }

        [Test]
        public void Timer_ShippedWingSlamThenHeadbutt_KeepsTheWingSlamStun()
        {
            // Read off the shipped assets so a WP2 re-tune (Wing Slam 1.3 s) keeps this honest.
            var wing = TestAssets.LoadAllIn<WingSlamAbilitySO>(TestAssets.AbilitiesDir).Single();
            var butt = TestAssets.LoadAllIn<HeadbuttAbilitySO>(TestAssets.AbilitiesDir).Single();
            Assume.That(wing.StunDuration > butt.StaggerSeconds, "the case needs the long stun first");
            Assert.AreEqual(wing.StunDuration, ControlReapplyRules.MergeTimer(wing.StunDuration, butt.StaggerSeconds), 1e-6f);
        }

        // ---- Timed slow: strongest factor, later expiry -----------------------------------

        private static ControlReapplyRules.TimedSlow Slow(float factor, double until) =>
            new ControlReapplyRules.TimedSlow(factor, until);

        [Test]
        public void Slow_WeakerShorterSlow_DoesNotWeakenOrShortenTheRunningOne()
        {
            var merged = ControlReapplyRules.MergeSlow(Slow(0.55f, 12.5), Slow(0.8f, 11.0), now: 10.0);
            Assert.AreEqual(0.55f, merged.Factor, 1e-6f, "the old code replaced the factor with the weaker one");
            Assert.AreEqual(12.5, merged.Until, 1e-9, "the old code replaced the expiry with the earlier one");
        }

        [Test]
        public void Slow_StrongerLaterSlow_TakesBoth()
        {
            var merged = ControlReapplyRules.MergeSlow(Slow(0.8f, 11.0), Slow(0.55f, 12.5), now: 10.0);
            Assert.AreEqual(0.55f, merged.Factor, 1e-6f);
            Assert.AreEqual(12.5, merged.Until, 1e-9);
        }

        [Test]
        public void Slow_StrongShortThenWeakLong_KeepsTheStrongFactorToTheLaterExpiry()
        {
            // The documented simplification (fable review rec. 9): slightly stronger than GDD 7.4.
            var merged = ControlReapplyRules.MergeSlow(Slow(0.45f, 11.0), Slow(0.8f, 14.0), now: 10.0);
            Assert.AreEqual(0.45f, merged.Factor, 1e-6f);
            Assert.AreEqual(14.0, merged.Until, 1e-9);
        }

        [Test]
        public void Slow_ExpiredCurrent_IsReplacedOutright()
        {
            var merged = ControlReapplyRules.MergeSlow(Slow(0.3f, 9.0), Slow(0.8f, 12.0), now: 10.0);
            Assert.AreEqual(0.8f, merged.Factor, 1e-6f, "a slow that already ended must not lend its factor");
            Assert.AreEqual(12.0, merged.Until, 1e-9);
        }

        [Test]
        public void Slow_NoSlowYet_TheResetSentinel_TakesTheIncoming()
        {
            // ChickenController starts and resets at (1, double.MinValue).
            var merged = ControlReapplyRules.MergeSlow(Slow(1f, double.MinValue), Slow(0.55f, 12.5), now: 10.0);
            Assert.AreEqual(0.55f, merged.Factor, 1e-6f);
            Assert.AreEqual(12.5, merged.Until, 1e-9);
        }

        [Test]
        public void Slow_InactiveIncoming_ChangesNothing()
        {
            var current = Slow(0.55f, 12.5);
            // Zero duration (immunity window): until == now.
            var zero = ControlReapplyRules.MergeSlow(current, Slow(0.2f, 10.0), now: 10.0);
            Assert.AreEqual(0.55f, zero.Factor, 1e-6f, "an immune (zero-length) slow must not strengthen the running one");
            Assert.AreEqual(12.5, zero.Until, 1e-9);
            // Not a slow at all.
            var none = ControlReapplyRules.MergeSlow(current, Slow(1f, 20.0), now: 10.0);
            Assert.AreEqual(12.5, none.Until, 1e-9);
            var nan = ControlReapplyRules.MergeSlow(current, Slow(float.NaN, 20.0), now: 10.0);
            Assert.AreEqual(0.55f, nan.Factor, 1e-6f);
        }

        // ---- Wiring: all three RPCs route through the rule --------------------------------

        [TestCase("public void RPC_ApplyStun(", "ControlReapplyRules.IncomingExtends(remaining, seconds)", "StunTimer = TickTimer.CreateFromSeconds")]
        [TestCase("public void RPC_ApplyRoot(", "ControlReapplyRules.IncomingExtends(remaining, duration)", "RootTimer = TickTimer.CreateFromSeconds")]
        [TestCase("public void RPC_ApplyAbilitySlow(", "ControlReapplyRules.MergeSlow(", "_abilitySlowUntil  = merged.Until")]
        public void ControlRpc_MergesAfterThePassive_BeforeWritingTheTimer(string signature, string merge, string write)
        {
            string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, signature);
            int passive = body.IndexOf("ApplyPassiveControlDuration(", StringComparison.Ordinal);
            int rule    = body.IndexOf(merge, StringComparison.Ordinal);
            int timer   = body.IndexOf(write, StringComparison.Ordinal);
            Assert.Greater(passive, 0, $"{signature} no longer shortens through ApplyPassiveControlDuration");
            Assert.Greater(rule, passive, $"{signature} must merge AFTER the passive shortened the incoming effect (A1)");
            Assert.Greater(timer, rule, $"{signature} writes its timer without consulting ControlReapplyRules (the A1 bug)");
        }
    }
}
