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
        public void Coexistence_EveryOtherRunningAbility_KeepsRunning_WhenAnotherIsCast()
        {
            var all = All();
            int coexisting = 0, ended = 0;
            foreach (var running in all)
            {
                foreach (var cast in all)
                {
                    if (ReferenceEquals(running, cast)) continue; // the same slot is refused while it runs
                    if (running.EndsOnNextMove) ended++;
                    else coexisting++;
                }
            }
            Assert.Greater(coexisting, 0, "most pairs run side by side");
            Assert.Greater(ended, 0, "stealth / Peck pairs end");
            // Every non-stealth, non-Peck ability must be left alone by any cast.
            foreach (var a in all.Where(x => x.name != "Invisibility" && x.name != "SmokeRoost" && x.name != "Peck"))
                Assert.IsFalse(a.EndsOnNextMove, $"{a.name} keeps running when another move is cast.");
        }

        [Test]
        public void Cast_EndsStealthAndPeck_OnlyAfterTheFizzleReturn_AndAfterCanActivate()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int fizzle = src.IndexOf("_fizzlePending = true;", StringComparison.Ordinal);
            int canActivate = src.IndexOf("if (!ability.CanActivate(_ctx))", StringComparison.Ordinal);
            int call = src.IndexOf("EndStealthAndPeckForCast(slot);", StringComparison.Ordinal);
            int onActivate = src.IndexOf("ability.OnActivate(_ctx);", StringComparison.Ordinal);
            int mask = src.IndexOf("ActiveMask = (byte)(ActiveMask | (1 << slot));", StringComparison.Ordinal);
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

        // ---- Writers: abilities add / remove modifiers, never write a resolved value ----------------

        [Test]
        public void NoAbilityWritesAResolvedEffectProperty_Directly()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets/_Game/Scripts/Abilities");
            var rx = new Regex(@"\.(MoveSpeedMultiplier|VisualOpacity|MovementLocked|DepositRateMultiplier|StealBackActive|StealBackAmount|AuraSlowActive|AuraSlowFactor|AuraSlowRadius)\s*=[^=]");
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string text = File.ReadAllText(file);
                var m = rx.Match(text);
                Assert.IsFalse(m.Success,
                    $"{Path.GetFileName(file)} writes '{m.Value.Trim()}' directly. Add / remove a per-slot modifier on " +
                    "ChickenController.Effects instead (strongest wins, Phase 6 chunk 3).");
            }
        }

        [Test]
        public void NetworkedEffectMirrors_AreWrittenOnlyWhenTheyChange()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            int sync = src.IndexOf("public void SyncAbilityEffects()", StringComparison.Ordinal);
            Assert.Greater(sync, 0);
            string body = src.Substring(sync, 1400);
            StringAssert.Contains("if (!Mathf.Approximately(VisualOpacity, opacity)) VisualOpacity = opacity;", body);
            StringAssert.Contains("if (StealBackActive != steal) StealBackActive = steal;", body);
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
