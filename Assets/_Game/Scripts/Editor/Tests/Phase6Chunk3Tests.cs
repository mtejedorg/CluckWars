using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 3: concurrent abilities, strongest-wins per effect kind, stealth / Peck ending on cast.
    /// The resolver, the traversal holders and the immunity rule are pure and tested directly; the live wiring
    /// (which needs a NetworkRunner) is pinned by source-order guards, as in chunk 2.
    /// </summary>
    public sealed class Phase6Chunk3Tests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // Source keys: the slots the abilities would hold.
        private const int S0 = 0, S1 = 1, S2 = 2, S3 = 3;

        // ---- Move speed: boost = max, penalty = min, final = boost * penalty -----------------------

        [Test]
        public void Speed_NothingRunning_IsOne()
        {
            Assert.AreEqual(1f, new AbilityEffectStack().MoveSpeedMultiplier, 1e-6f);
        }

        [Test]
        public void Speed_RuffleThenSpeedBurst_AreNotStacked_AndEndIndependently()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 1.5f); // Ruffle
            fx.SetMoveSpeed(S1, 1.5f); // Speed Burst
            Assert.AreEqual(1.5f, fx.MoveSpeedMultiplier, 1e-6f, "same-kind boosts never multiply");

            fx.RemoveMoveSpeed(S0);
            Assert.AreEqual(1.5f, fx.MoveSpeedMultiplier, 1e-6f, "Speed Burst still runs after Ruffle ends");
            fx.RemoveMoveSpeed(S1);
            Assert.AreEqual(1f, fx.MoveSpeedMultiplier, 1e-6f);
        }

        [Test]
        public void Speed_ShadowstepAndRuffle_StrongestWins_ThenTheRemainingOneTakesOver()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S2, 1.5f); // Ruffle
            fx.SetMoveSpeed(S0, 3.0f); // Shadowstep
            Assert.AreEqual(3.0f, fx.MoveSpeedMultiplier, 1e-6f);
            fx.RemoveMoveSpeed(S0);
            Assert.AreEqual(1.5f, fx.MoveSpeedMultiplier, 1e-6f, "Ruffle's remaining time is not lost");
        }

        [Test]
        public void Speed_TurtleSlowAndRollBoost_AreDifferentKinds_AndMultiply()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 0.25f); // Turtle Mode
            fx.SetMoveSpeed(S1, 2.0f);  // Roll & Push
            Assert.AreEqual(0.5f, fx.MoveSpeedMultiplier, 1e-6f);
            fx.RemoveMoveSpeed(S1);
            Assert.AreEqual(0.25f, fx.MoveSpeedMultiplier, 1e-6f);
        }

        [Test]
        public void Speed_TwoPenalties_TakeTheStrongest_NotTheProduct()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 0.25f);
            fx.SetMoveSpeed(S1, 0.5f);
            Assert.AreEqual(0.25f, fx.MoveSpeedMultiplier, 1e-6f);
        }

        [Test]
        public void Speed_ReSettingASource_ReplacesItsOwnModifier()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 1.5f);
            fx.SetMoveSpeed(S0, 2.0f);
            fx.RemoveMoveSpeed(S0);
            Assert.AreEqual(1f, fx.MoveSpeedMultiplier, 1e-6f, "one source never leaves a second modifier behind");
        }

        // ---- Opacity: min -------------------------------------------------------------------------

        [Test]
        public void Opacity_IsTheLowestActive_AndOneWhenNone()
        {
            var fx = new AbilityEffectStack();
            Assert.AreEqual(1f, fx.Opacity, 1e-6f);
            fx.SetOpacity(S0, 0.5f);
            fx.SetOpacity(S1, 0.2f);
            Assert.AreEqual(0.2f, fx.Opacity, 1e-6f);
            fx.RemoveOpacity(S1);
            Assert.AreEqual(0.5f, fx.Opacity, 1e-6f);
            fx.RemoveOpacity(S0);
            Assert.AreEqual(1f, fx.Opacity, 1e-6f);
        }

        // ---- Movement lock: any ------------------------------------------------------------------

        [Test]
        public void Lock_PeckEnding_DoesNotFreeARunningEggShell()
        {
            var fx = new AbilityEffectStack();
            fx.SetMovementLock(S0); // Peck
            fx.SetMovementLock(S1); // Egg Shell
            fx.RemoveMovementLock(S0);
            Assert.IsTrue(fx.MovementLocked, "Egg Shell still holds the lock");
            fx.RemoveMovementLock(S1);
            Assert.IsFalse(fx.MovementLocked);
        }

        // ---- Deposit rate: max ------------------------------------------------------------------

        [Test]
        public void DepositRate_IsTheMax_AndOneWhenNone()
        {
            var fx = new AbilityEffectStack();
            Assert.AreEqual(1f, fx.DepositRateMultiplier, 1e-6f);
            fx.SetDepositRate(S0, 4f);
            fx.SetDepositRate(S1, 2f);
            Assert.AreEqual(4f, fx.DepositRateMultiplier, 1e-6f);
            fx.RemoveDepositRate(S0);
            Assert.AreEqual(2f, fx.DepositRateMultiplier, 1e-6f);
        }

        // ---- Steal-back: any / max amount; aura: any -----------------------------------------------

        [Test]
        public void StealBack_IsActiveWhileAnySourceRuns_WithTheLargestAmount()
        {
            var fx = new AbilityEffectStack();
            Assert.IsFalse(fx.StealBackActive);
            fx.SetStealBack(S0, 4f);
            fx.SetStealBack(S1, 6f);
            Assert.IsTrue(fx.StealBackActive);
            Assert.AreEqual(6f, fx.StealBackAmount, 1e-6f);
            fx.RemoveStealBack(S1);
            Assert.AreEqual(4f, fx.StealBackAmount, 1e-6f);
            fx.RemoveStealBack(S0);
            Assert.IsFalse(fx.StealBackActive);
        }

        [Test]
        public void Aura_IsActiveWhileAnySourceRuns_StrongestSlowWins()
        {
            var fx = new AbilityEffectStack();
            Assert.IsFalse(fx.TryGetAura(out _, out _));
            fx.SetAura(S0, 0.55f, 4f);
            fx.SetAura(S1, 0.40f, 3f);
            Assert.IsTrue(fx.TryGetAura(out float factor, out float radius));
            Assert.AreEqual(0.40f, factor, 1e-6f);
            Assert.AreEqual(3f, radius, 1e-6f, "radius comes from the same (strongest) source");
            fx.RemoveAura(S1);
            Assert.IsTrue(fx.TryGetAura(out factor, out radius));
            Assert.AreEqual(0.55f, factor, 1e-6f);
            fx.RemoveAura(S0);
            Assert.IsFalse(fx.AuraActive);
        }

        // ---- Whole-stack ---------------------------------------------------------------------------

        [Test]
        public void RemoveSource_DropsEveryKindOfThatSourceOnly()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 1.5f); fx.SetOpacity(S0, 0.2f); fx.SetMovementLock(S0);
            fx.SetDepositRate(S0, 4f); fx.SetStealBack(S0, 4f); fx.SetAura(S0, 0.5f, 3f);
            fx.SetMoveSpeed(S1, 1.25f);
            fx.RemoveSource(S0);
            Assert.AreEqual(1.25f, fx.MoveSpeedMultiplier, 1e-6f);
            Assert.AreEqual(1f, fx.Opacity, 1e-6f);
            Assert.IsFalse(fx.MovementLocked);
            Assert.AreEqual(1f, fx.DepositRateMultiplier, 1e-6f);
            Assert.IsFalse(fx.StealBackActive);
            Assert.IsFalse(fx.AuraActive);
        }

        [Test]
        public void Clear_ReturnsEveryKindToItsDefault()
        {
            var fx = new AbilityEffectStack();
            fx.SetMoveSpeed(S0, 3f); fx.SetOpacity(S1, 0.2f); fx.SetMovementLock(S2); fx.SetAura(S3, 0.5f, 3f);
            fx.Clear();
            Assert.AreEqual(1f, fx.MoveSpeedMultiplier, 1e-6f);
            Assert.AreEqual(1f, fx.Opacity, 1e-6f);
            Assert.IsFalse(fx.MovementLocked);
            Assert.IsFalse(fx.AuraActive);
        }

        // ---- Control immunity: keep the later expiry ----------------------------------------------

        [TestCase(0f, 2f, true)]    // nothing open: grant
        [TestCase(3f, 2f, false)]   // a longer window is open: keep it
        [TestCase(1f, 2f, true)]    // a shorter one is open: extend
        [TestCase(2f, 2f, false)]   // equal: no change
        public void Immunity_ReGrant_KeepsWhicheverExpiryIsLater(float open, float grant, bool replaces)
        {
            Assert.AreEqual(replaces, ControlImmunityRules.ShouldReplace(open, grant));
        }

        // ---- Traversal: highest tier wins; window open until the last holder ends -----------------

        [Test]
        public void Traversal_TierRank_IsBargeVaultBlink_NotTheEnumOrder()
        {
            Assert.Less(TraversalRules.Rank(TerrainTraversal.None), TraversalRules.Rank(TerrainTraversal.Barge));
            Assert.Less(TraversalRules.Rank(TerrainTraversal.Barge), TraversalRules.Rank(TerrainTraversal.Vault));
            Assert.Less(TraversalRules.Rank(TerrainTraversal.Vault), TraversalRules.Rank(TerrainTraversal.Blink));
        }

        [Test]
        public void Traversal_HighestHolderWins_AndDowngradesAsHoldersEnd()
        {
            var h = new TraversalHolders();
            Assert.AreEqual(TerrainTraversal.None, h.Highest);
            h.Set(S0, TerrainTraversal.Barge);
            h.Set(S1, TerrainTraversal.Vault);
            Assert.AreEqual(TerrainTraversal.Vault, h.Highest, "Vault outranks Barge though its enum value is lower");
            h.Set(S2, TerrainTraversal.Blink);
            Assert.AreEqual(TerrainTraversal.Blink, h.Highest);

            Assert.IsTrue(h.Remove(S2));
            Assert.AreEqual(TerrainTraversal.Vault, h.Highest);
            Assert.AreEqual(2, h.Count, "the window stays open while any holder remains");
            Assert.IsTrue(h.Remove(S1));
            Assert.AreEqual(TerrainTraversal.Barge, h.Highest);
            Assert.IsTrue(h.Remove(S0));
            Assert.AreEqual(0, h.Count, "End only closes the window when the LAST holder ends");
            Assert.AreEqual(TerrainTraversal.None, h.Highest);
        }

        [Test]
        public void Traversal_RemovingAHolderThatNeverBegan_IsANoOp()
        {
            var h = new TraversalHolders();
            h.Set(S0, TerrainTraversal.Vault);
            Assert.IsFalse(h.Remove(S3));
            Assert.AreEqual(TerrainTraversal.Vault, h.Highest);
        }

        // ---- Which abilities can coexist; what a cast ends ------------------------------------------

        private static AbilityBaseSO[] All() =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).ToArray();

        [Test]
        public void OnlyStealthAndPeck_EndWhenAnotherMoveIsCast()
        {
            var ending = All().Where(a => a.EndsOnNextMove).Select(a => a.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Invisibility", "Peck", "SmokeRoost" }, ending,
                "Phase 6 chunk 3: only stealth (Invisibility, Smoke Roost) and Peck end on the next move.");

            var stealth = All().Where(a => a.IsStealth).Select(a => a.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Invisibility", "SmokeRoost" }, stealth);
        }

        [Test]
        public void Coexistence_ACastEndsExactlyTheRunningStealthAndPeck_AndNothingElse()
        {
            var all = All();
            int pairs = 0;
            foreach (var running in all)
            {
                foreach (var cast in all)
                {
                    if (ReferenceEquals(running, cast)) continue; // the same slot is refused while it runs
                    // running ability in slot 0, the new cast in slot 1, both read through the real asset flags
                    byte ended = AbilityRunRules.EndedByCast(0b01, 1,
                        new[] { running.EndsOnNextMove, cast.EndsOnNextMove, false, false });
                    bool expected = running.name == "Invisibility" || running.name == "SmokeRoost" || running.name == "Peck";
                    Assert.AreEqual(expected ? (byte)0b01 : (byte)0, ended,
                        $"{running.name} running, then casting {cast.name}: " + (expected ? "should end" : "must keep running"));
                    pairs++;
                }
            }
            Assert.Greater(pairs, 50, "every ordered pair of shipped abilities was checked");
        }

        [Test]
        public void Cast_EndsStealthAndPeck_OnlyAfterTheFizzleReturn_AndAfterCanActivate()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int fizzle = src.IndexOf("_fizzlePending = true;", StringComparison.Ordinal);
            int canActivate = src.IndexOf("if (!ability.CanActivate(_ctx))", StringComparison.Ordinal);
            int call = src.IndexOf("EndStealthAndPeckForCast(slot);", StringComparison.Ordinal);
            int onActivate = src.IndexOf("ability.OnActivate(_ctx);", StringComparison.Ordinal);
            int mask = src.IndexOf("ActiveMask = AbilityRunRules.Begin(ActiveMask, slot);", StringComparison.Ordinal);
            Assert.Greater(fizzle, 0);
            Assert.Greater(canActivate, fizzle);
            Assert.Greater(call, canActivate, "refused casts and a failed CanActivate never end stealth");
            Assert.Greater(mask, call, "stealth ends before the new slot becomes active");
            Assert.Greater(onActivate, call, "...and before the new cast applies its own modifiers");
            // Exactly one call site: holds, previews, fizzles and refusals have none.
            Assert.AreEqual(1, Regex.Matches(src, @"EndStealthAndPeckForCast\(slot\)").Count);
        }

        [Test]
        public void Players_AreNotGatedByARunningAbility_ButBotsStill_Are()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            StringAssert.DoesNotContain("otherAbilityActive", src);
            int bot = src.IndexOf("public bool BotTryActivate", StringComparison.Ordinal);
            int gate = src.IndexOf("if (AnyAbilityActive) return false;", bot, StringComparison.Ordinal);
            Assert.Greater(bot, 0);
            Assert.Greater(gate, bot, "bots keep refusing while any of their slots runs");
        }

        // The resolved effect properties (MoveSpeedMultiplier, MovementLocked, DepositRateMultiplier, StealBackAmount)
        // are get-only and the networked mirrors (VisualOpacity, StealBackActive, AuraSlow*) have private setters, so
        // the compiler already forbids an ability writing a resolved value; no source scan is needed.

        // ---- Run bookkeeping: mask, expiry, stealth break, most recent ---------------------------------

        [Test]
        public void Mask_BeginEnd_SetAndClearOnlyThatSlot()
        {
            byte m = AbilityRunRules.Begin(0, 2);
            m = AbilityRunRules.Begin(m, 0);
            Assert.IsTrue(AbilityRunRules.IsActive(m, 0));
            Assert.IsFalse(AbilityRunRules.IsActive(m, 1));
            Assert.IsTrue(AbilityRunRules.IsActive(m, 2));
            m = AbilityRunRules.End(m, 2);
            Assert.IsFalse(AbilityRunRules.IsActive(m, 2));
            Assert.IsTrue(AbilityRunRules.IsActive(m, 0), "ending one slot leaves the others");
            Assert.IsFalse(AbilityRunRules.IsActive(m, -1));
        }

        [Test]
        public void Expiry_OnlyRunningSlotsWhoseTimerRanOut_AreExpired()
        {
            // slots 0 and 2 run; 1 and 3 are idle. An idle slot's timer is always "expired" and must never count.
            var timersExpired = new[] { true, true, false, true };
            Assert.AreEqual((byte)0b0001, AbilityRunRules.ExpiredMask(0b0101, timersExpired));

            // Two overlapping abilities with different durations end independently, shorter one first.
            var fake = new FakeClock(); // 0..4 s
            float[] durations = { 3f, 1f, 0f, 0f };
            byte mask = 0b0011;
            var order = new System.Collections.Generic.List<int>();
            for (int t = 0; t <= 4; t++)
            {
                fake.Now = t;
                var exp = new bool[4];
                for (int i = 0; i < 4; i++) exp[i] = fake.Now >= durations[i];
                byte expired = AbilityRunRules.ExpiredMask(mask, exp);
                for (int i = 0; i < 4; i++)
                    if (AbilityRunRules.IsActive(expired, i)) { order.Add(i); mask = AbilityRunRules.End(mask, i); }
            }
            CollectionAssert.AreEqual(new[] { 1, 0 }, order, "the 1 s ability expires before the 3 s one, each exactly once");
            Assert.AreEqual((byte)0, mask);
        }

        private sealed class FakeClock { public float Now; }

        [Test]
        public void StealthBreak_CastEndsRunningInvisibilityAndPeck_NotItself_NotIdleSlots()
        {
            // slot0 Invisibility (ends), slot1 Ruffle (keeps), slot2 Peck (ends, idle), slot3 casts.
            var ends = new[] { true, false, true, false };
            Assert.AreEqual((byte)0b0001, AbilityRunRules.EndedByCast(0b0011, 3, ends), "idle Peck is not 'ended'");
            Assert.AreEqual((byte)0b0101, AbilityRunRules.EndedByCast(0b0111, 3, ends));
            Assert.AreEqual((byte)0, AbilityRunRules.EndedByCast(0b0001, 0, ends), "a slot's own cast never ends itself");
        }

        [Test]
        public void MostRecent_IsTheSmallestElapsed_RunningOnly_AndNoneWhenIdle()
        {
            var elapsed = new[] { 2.0f, 0.5f, 0.1f, 1.0f };
            Assert.AreEqual(1, AbilityRunRules.MostRecent(0b1011, elapsed), "slot 2 is idle so slot 1 (0.5 s) wins");
            Assert.AreEqual(2, AbilityRunRules.MostRecent(0b1111, elapsed));
            Assert.AreEqual(-1, AbilityRunRules.MostRecent(0, elapsed));
            Assert.AreEqual(0, AbilityRunRules.MostRecent(0b0011, new[] { 1f, 1f, 0f, 0f }), "ties keep the lower slot");
        }

        // ---- Teardown: the matching OnDeactivate, then ALWAYS the slot's modifiers and traversal hold ---

        private sealed class RecordingAbility : AbilityBaseSO
        {
            public int Activated = -2, Deactivated = -2;
            public bool Throw;
            public override void OnActivate(AbilityContext ctx) => Activated = ctx.Slot;
            public override void OnDeactivate(AbilityContext ctx)
            {
                Deactivated = ctx.Slot;
                if (Throw) throw new InvalidOperationException("boom");
            }
        }

        [Test]
        public void Release_CallsTheMatchingAbilitysOnDeactivate_WithItsSlot_AndResetsTheContext()
        {
            var a = ScriptableObject.CreateInstance<RecordingAbility>();
            var b = ScriptableObject.CreateInstance<RecordingAbility>();
            try
            {
                var ctx = new AbilityContext(null);
                AbilityRunRules.ReleaseSlot(2, b, ctx, new AbilityEffectStack(), null);
                Assert.AreEqual(2, b.Deactivated);
                Assert.AreEqual(-2, a.Deactivated, "only the slot's own ability is deactivated");
                Assert.AreEqual(-1, ctx.Slot, "the context's slot is cleared after the callback");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }

        [Test]
        public void Release_AlwaysDropsTheSlotsModifiersAndTraversalHold_EvenIfTheAbilityLeakedThem()
        {
            // OnDeactivate here removes nothing, i.e. a loadout-swapped or buggy ability that leaks.
            var leaky = ScriptableObject.CreateInstance<RecordingAbility>();
            try
            {
                var fx = new AbilityEffectStack();
                fx.SetMoveSpeed(S1, 3f); fx.SetOpacity(S1, 0.2f); fx.SetMovementLock(S1); fx.SetAura(S1, 0.5f, 3f);
                fx.SetMoveSpeed(S0, 1.25f);
                int endedHolder = -99;
                AbilityRunRules.ReleaseSlot(S1, leaky, new AbilityContext(null), fx, h => endedHolder = h);
                Assert.AreEqual(1.25f, fx.MoveSpeedMultiplier, 1e-6f, "the other slot's modifier survives");
                Assert.AreEqual(1f, fx.Opacity, 1e-6f);
                Assert.IsFalse(fx.MovementLocked);
                Assert.IsFalse(fx.AuraActive);
                Assert.AreEqual(S1, endedHolder, "the traversal hold of the slot is released too");
            }
            finally { UnityEngine.Object.DestroyImmediate(leaky); }
        }

        [Test]
        public void Release_EvenWhenOnDeactivateThrows_StillCleansUp()
        {
            var bad = ScriptableObject.CreateInstance<RecordingAbility>();
            bad.Throw = true;
            try
            {
                var fx = new AbilityEffectStack();
                fx.SetMovementLock(S0);
                bool traversalEnded = false;
                var ctx = new AbilityContext(null);
                Assert.Throws<InvalidOperationException>(() =>
                    AbilityRunRules.ReleaseSlot(S0, bad, ctx, fx, _ => traversalEnded = true));
                Assert.IsFalse(fx.MovementLocked);
                Assert.IsTrue(traversalEnded);
                Assert.AreEqual(-1, ctx.Slot);
            }
            finally { UnityEngine.Object.DestroyImmediate(bad); }
        }

        [Test]
        public void Death_ReleasingEveryRunningSlot_LeavesAnEmptyStack()
        {
            var abilities = new RecordingAbility[4];
            for (int i = 0; i < 4; i++) abilities[i] = ScriptableObject.CreateInstance<RecordingAbility>();
            try
            {
                var fx = new AbilityEffectStack();
                fx.SetMoveSpeed(S0, 3f); fx.SetOpacity(S1, 0.2f); fx.SetMovementLock(S2); fx.SetStealBack(S3, 4f);
                fx.SetDepositRate(S3, 4f); fx.SetAura(S2, 0.5f, 3f);
                byte mask = 0b1111;
                var ctx = new AbilityContext(null);
                for (int i = 0; i < 4; i++)
                {
                    if (!AbilityRunRules.IsActive(mask, i)) continue;
                    AbilityRunRules.ReleaseSlot(i, abilities[i], ctx, fx, null);
                    mask = AbilityRunRules.End(mask, i);
                }
                Assert.AreEqual((byte)0, mask);
                Assert.AreEqual(1f, fx.MoveSpeedMultiplier, 1e-6f);
                Assert.AreEqual(1f, fx.Opacity, 1e-6f);
                Assert.IsFalse(fx.MovementLocked);
                Assert.IsFalse(fx.StealBackActive);
                Assert.IsFalse(fx.AuraActive);
                Assert.AreEqual(1f, fx.DepositRateMultiplier, 1e-6f);
                for (int i = 0; i < 4; i++) Assert.AreEqual(i, abilities[i].Deactivated);
            }
            finally { foreach (var a in abilities) UnityEngine.Object.DestroyImmediate(a); }
        }

        // ---- Effects.Set with no real source: refused loudly ----------------------------------------------

        private sealed class RecordingLog : CluckWars.Logging.ILogService
        {
            public int Errors;
            public CluckWars.Logging.LogLevel MinLevel { get; set; }
            public bool IsEnabled(CluckWars.Logging.LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors++;
        }

        [Test]
        public void Effects_SetWithNegativeSource_LogsAnErrorAndAppliesNothing()
        {
            var log = new RecordingLog();
            var fx = new AbilityEffectStack { Log = log };
            fx.SetMoveSpeed(-1, 3f); fx.SetOpacity(-1, 0.1f); fx.SetMovementLock(-1);
            fx.SetDepositRate(-1, 4f); fx.SetStealBack(-1, 4f); fx.SetAura(-1, 0.5f, 3f);
            Assert.AreEqual(6, log.Errors, "each refused Set* reports");
            Assert.AreEqual(1f, fx.MoveSpeedMultiplier, 1e-6f);
            Assert.AreEqual(1f, fx.Opacity, 1e-6f);
            Assert.IsFalse(fx.MovementLocked);
            Assert.IsFalse(fx.StealBackActive);
            Assert.IsFalse(fx.AuraActive);
            Assert.AreEqual(1f, fx.DepositRateMultiplier, 1e-6f);
        }

        // ---- Traversal downgrade: which ignored colliders survive ---------------------------------------

        [TestCase(true, false, true)]    // the lower tier still clears it: keep
        [TestCase(false, true, true)]    // can't clear it but the caster is inside it: keep for the unstick pass
        [TestCase(false, false, false)]  // can't clear it, not inside it: restore collision now
        [TestCase(true, true, true)]
        public void Downgrade_KeepsAnIgnoreOnlyIfStillClearableOrOverlapped(bool clears, bool overlaps, bool keeps)
        {
            Assert.AreEqual(keeps, TraversalRules.KeepsIgnoreAfterDowngrade(clears, overlaps));
        }

        [Test]
        public void Downgrade_BlinkToVault_ReleasesTallButKeepsLowAndStandard()
        {
            var tier = TerrainTraversal.Vault; // what remains after Blink ends
            Assert.IsFalse(TraversalRules.KeepsIgnoreAfterDowngrade(TraversalRules.Clears(tier, ObstacleClass.Tall), false));
            Assert.IsTrue(TraversalRules.KeepsIgnoreAfterDowngrade(TraversalRules.Clears(tier, ObstacleClass.Standard), false));
            Assert.IsTrue(TraversalRules.KeepsIgnoreAfterDowngrade(TraversalRules.Clears(tier, ObstacleClass.Low), false));
            Assert.IsTrue(TraversalRules.KeepsIgnoreAfterDowngrade(TraversalRules.Clears(tier, ObstacleClass.Tall), true),
                "a Tall rock the caster stands inside stays ignored until unstick releases it");
        }

        // ---- Copy ------------------------------------------------------------------------------------

        [TestCase("Invisibility")]
        [TestCase("SmokeRoost")]
        public void StealthDescriptions_SayUsingAMoveBreaksIt_AndFitTheLimit(string asset)
        {
            var a = All().First(x => x.name == asset);
            StringAssert.Contains("Using a move breaks it.", a.Description);
            Assert.LessOrEqual(a.Description.Length, 110);
        }
    }
}
