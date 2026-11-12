using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 7 chunk D: the steal-proof state (Egg Shell "Full shell", Turtle Mode "Shell walk").
    /// </summary>
    /// <remarks>
    /// The enforcement hinges on a <c>[Networked]</c> <c>TickTimer</c> read against a live Runner,
    /// which EditMode never has. So, like <c>ControlImmunityFeedbackTests</c>, the wiring is
    /// pinned from comment-stripped source (ordering: the thief-side gate must come BEFORE the steal
    /// is asked for, so a shelled victim is never asked) and every pure rule is asserted for real.
    /// </remarks>
    public sealed class StealProofTests
    {
        private const string Dir = "Assets/_Game/Scripts/";

        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static string Src(string rel)
        {
            string path = Dir + rel;
            Assert.IsTrue(File.Exists(path), $"Expected source file not found: {path}");
            string src = Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var sb = new StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(slash >= 0 ? line.Substring(0, slash) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static string Body(string src, string signature, int span = 2200)
        {
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.Greater(i, 0, $"'{signature}' not found - the test anchor moved.");
            return src.Substring(i, Math.Min(span, src.Length - i));
        }

        private static AbilityBaseSO Asset(string name) =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).First(a => a.name == name);

        // ---- 1. Grant rule + chokepoints --------------------------------------

        [TestCase(0f, 2f, true)]
        [TestCase(1f, 3f, true)]
        [TestCase(2.5f, 2f, false)]
        [TestCase(2f, 2f, false)]
        public void StealProofGrant_LaterEndWins(float open, float grant, bool replaces) =>
            Assert.AreEqual(replaces, ControlImmunityRules.ShouldReplace(open, grant));

        [Test]
        public void ChickenController_StealProof_IsItsOwnNetworkedTimer_WithLaterEndWins()
        {
            string src = Src("Gameplay/ChickenController.cs");
            StringAssert.Contains("[Networked] private TickTimer StealProofTimer", src);
            StringAssert.Contains("public bool IsStealProof", src);
            string grant = Body(src, "public void GrantStealProof(", 700);
            StringAssert.Contains("ControlImmunityRules.ShouldReplace", grant);
            StringAssert.Contains("StealProofTimer = TickTimer.CreateFromSeconds", grant);
            StringAssert.DoesNotContain("ControlImmuneTimer", grant, "steal-proof is its own effect kind");
        }

        [Test]
        public void CanStealFrom_IsFalseForNull_AndTrueForAnUnspawnedChicken()
        {
            Assert.IsFalse(StealRules.CanStealFrom(null));
            var go = new GameObject("StealProofTests_Probe");
            try
            {
                var c = go.AddComponent<ChickenController>();
                Assert.IsFalse(c.IsStealProof, "no Runner -> no window");
                Assert.IsTrue(StealRules.CanStealFrom(c));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- 2. Victim-side refusal + refund ----------------------------------

        // A02: the thief no longer credits itself ahead of the drain, so a refusal has nothing to refund. It reports
        // zero, which closes the thief's open request (FoodTransferRulesTests plays the amounts out).

        [Test]
        public void RpcDrainStolen_RefusesWhenStealProof_AndReportsZero_BeforeAnyDrain()
        {
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_DrainStolen(", 3200);
            int proof  = body.IndexOf("_controller.IsStealProof", StringComparison.Ordinal);
            int report = body.IndexOf("ReportSteal(thief, 0f)", StringComparison.Ordinal);
            int drain  = body.IndexOf("StealRules.SplitDrain(", StringComparison.Ordinal);
            int stamp  = body.IndexOf("StealEventId++", StringComparison.Ordinal);
            Assert.Greater(proof, 0);
            Assert.Greater(report, proof, "the zero report is sent from the refusal branch");
            Assert.Greater(drain, report, "refusal must return before the drain");
            Assert.Greater(stamp, report, "a refused steal must not name a thief (no LastThiefId/StealEventId)");
            StringAssert.Contains("return;", body.Substring(proof, drain - proof));
        }

        [Test]
        public void RpcSettleSteal_TakesTheSender_AndOnlyHonoursTheVictimsAuthority()
        {
            var m = typeof(ChickenCargo).GetMethod(nameof(ChickenCargo.RPC_SettleSteal));
            Assert.IsNotNull(m);
            Assert.IsTrue(m.GetParameters().Any(p => p.ParameterType == typeof(Fusion.RpcInfo)));
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_SettleSteal(", 3000);
            StringAssert.Contains("SpeaksFor(victim, info)", body);
            StringAssert.Contains("FoodTransferRules.Credit(requested, amount)", body);

            string speaks = Body(Src("Gameplay/ChickenCargo.cs"), "private static bool SpeaksFor(", 600);
            StringAssert.Contains("owner.Object.StateAuthority == info.Source", speaks, "off the wire: the owner's state authority");
            StringAssert.Contains("owner.Object.HasStateAuthority", speaks, "local invoke: simulated here");
            Assert.IsNull(typeof(ChickenCargo).GetMethod("RPC_RefundRefusedSteal"), "the optimistic credit and its refund are back");
        }

        [Test]
        public void RpcTransferAllToBountyBag_RefusesAStealProofVictim_BeforeTouchingCargo()
        {
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_TransferAllToBountyBag(", 1400);
            int proof = body.IndexOf("_controller.IsStealProof", StringComparison.Ordinal);
            int zero  = body.IndexOf("Cargo = 0f", StringComparison.Ordinal);
            Assert.Greater(proof, 0);
            Assert.Greater(zero, proof);
        }

        // ---- 3. The six thief sites: gate BEFORE the self-credit ---------------

        [TestCase("Abilities/SnatchAbilitySO.cs",      "RequestSteal(")]
        [TestCase("Abilities/SneakyStealAbilitySO.cs", "RequestSteal(")]
        [TestCase("Abilities/ScrapAbilitySO.cs",       "RequestSteal(")]
        [TestCase("Abilities/RollTrampleAbilitySO.cs", "RequestSteal(")]
        [TestCase("Gameplay/ChickenController.cs",     "RequestSteal(")]
        public void ThiefSite_ChecksStealProof_BeforeCreditingItself(string file, string credit)
        {
            string src = Src(file);
            int gate = src.IndexOf("StealRules.CanStealFrom(", StringComparison.Ordinal);
            int cred = src.IndexOf(credit, StringComparison.Ordinal);
            Assert.Greater(gate, 0, $"{file}: no StealRules.CanStealFrom gate - the thief would credit itself against a shell and duplicate food.");
            Assert.Greater(cred, 0, $"{file}: anchor '{credit}' moved.");
            Assert.Less(gate, cred, $"{file}: the gate must come BEFORE '{credit}'.");
        }

        [Test]
        public void SnatchAndSpineCoat_SkipOnlyTheSteal_TheKnockbackIsUntouched()
        {
            string snatch = Src("Abilities/SnatchAbilitySO.cs");
            Assert.Less(snatch.IndexOf("StealRules.CanStealFrom(", StringComparison.Ordinal),
                        snatch.IndexOf("RPC_ApplyKnockback", StringComparison.Ordinal));
            StringAssert.DoesNotContain("continue", Body(snatch, "StealRules.CanStealFrom(", 200), "Snatch must keep the knockback");
            string spine = Body(Src("Gameplay/ChickenController.cs"), "StealRules.CanStealFrom(other)", 1400);
            StringAssert.Contains("other.RPC_ApplyKnockback", spine);
        }

        [Test]
        public void NearestTargetAbilities_DoNotFallBackToTheNextRival()
        {
            foreach (var f in new[] { "SneakyStealAbilitySO", "ScrapAbilitySO", "RollTrampleAbilitySO" })
            {
                string src = Src($"Abilities/{f}.cs");
                StringAssert.Contains("if (!StealRules.CanStealFrom(_scratch[0])) return;", src, f);
                StringAssert.DoesNotContain("_scratch[1]", src, f);
            }
        }

        [Test]
        public void DeathMark_DoesNotTransferFromAStealProofTarget_ButStillExecutes()
        {
            string src = Src("Gameplay/AssassinExecute.cs");
            // Whole method, brace-matched and comment-stripped, rather than a fixed span that
            // drifts as Press grows (CW-050 added a victim guard ahead of the gate).
            string press = AssassinExecuteCreditTests.MethodBody(Dir + "Gameplay/AssassinExecute.cs", "public bool Press(");
            string body = Body(press, "TryFindBehaviour(MarkedTarget", press.Length); // to the end of Press
            int gate = body.IndexOf("StealRules.CanStealFrom(target)", StringComparison.Ordinal);
            int rpc  = body.IndexOf("RPC_TransferAllToBountyBag", StringComparison.Ordinal);
            Assert.Greater(gate, -1);
            Assert.Less(gate, rpc);
            StringAssert.Contains("RPC_ExecuteRemoval", body);
            StringAssert.Contains("ResolveExecuteBounty", src);
        }

        [Test]
        public void EveryCallerOfTheStealRpcs_GoesThroughTheOneGate()
        {
            foreach (var path in Directory.GetFiles(Dir, "*.cs", SearchOption.AllDirectories))
            {
                string norm = path.Replace('\\', '/');
                if (norm.Contains("/Editor/") || Path.GetFileName(path) == "ChickenCargo.cs") continue;
                string src = Src(norm.Substring(Dir.Length));
                if (!src.Contains(".RPC_DrainStolen(") && !src.Contains(".RequestSteal(") && !src.Contains(".RPC_TransferAllToBountyBag(")) continue;
                StringAssert.Contains("StealRules.CanStealFrom(", src, $"{Path.GetFileName(path)} steals without the steal-proof gate.");
            }
        }

        // ---- 4. Feedback classification ---------------------------------------

        [Test]
        public void StealAbilities_DeclareTheirEffects_SoTheNullifiedMarkIsHonest()
        {
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
            {
                if (a is PassiveAbilitySO) continue;
                if (a.NominalStealAmount > 0f && a.AimShape != AbilityAimShape.None)
                    Assert.IsTrue(a.TargetEffectIncludesSteal, $"{a.name} steals on an aimed hit but does not declare TargetEffectIncludesSteal.");
                if (a.TargetEffectIsPurelyControl)
                    Assert.IsFalse(a.TargetEffectIncludesSteal, $"{a.name} cannot be both purely control and steal.");
            }
            Assert.IsTrue(Asset("Snatch").TargetEffectIncludesSteal);
            Assert.IsFalse(Asset("Snatch").TargetEffectIncludesControl, "the Steal (Snatch) no longer shoves: KnockbackStrength 0, 2026-11-05");
            foreach (var n in new[] { "SneakySteal", "Scrap", "DiveBomb" })
            {
                Assert.IsTrue(Asset(n).TargetEffectIncludesSteal, n);
                Assert.IsFalse(Asset(n).TargetEffectIncludesControl, $"{n} is a pure steal");
            }
        }

        [Test]
        public void IsEffectNullified_ConsultsStealProof_AndRequiresBothForMixedAbilities()
        {
            string body = Body(Src("Abilities/AbilityBaseSO.cs"), "public bool IsEffectNullified(", 900);
            StringAssert.Contains("candidate.IsStealProof && (!TargetEffectIncludesControl || candidate.IsControlImmune)", body);
            StringAssert.Contains("TargetEffectIncludesSteal", body);
        }

        [Test]
        public void SafePop_ExistsAndIsWiredIntoHitConfirm()
        {
            Assert.AreEqual("SAFE", UiText.Get(UiKeys.HudPopSafe));
            string body = Body(Src("Visuals/HitFeedback.cs"), "private void ConfirmHits(", 4200);
            StringAssert.Contains("UiKeys.HudPopSafe", body);
            StringAssert.Contains("victim.IsStealProof", body);
        }

        // ---- 5. Egg Shell + Turtle Mode ---------------------------------------

        [Test]
        public void EggShell_GrantsBothImmunities_ForItsDuration()
        {
            string body = Body(Src("Abilities/EggShellAbilitySO.cs"), "public override void OnActivate(", 500);
            StringAssert.Contains("SetMovementLock", body);
            StringAssert.Contains("GrantControlImmunity(Duration)", body);
            StringAssert.Contains("GrantStealProof(Duration)", body);
            var egg = Asset("EggShell");
            Assert.AreEqual(2f, egg.Duration, 1e-5f);
            Assert.AreEqual(8f, egg.Cooldown, 1e-5f);
            Assert.AreEqual("Hide in an egg for 2 seconds. You can't move, but nobody can push you, stop you or steal your food.", egg.DescriptionText);
            Assert.AreEqual(egg.DescriptionText, egg.Description, "stale .asset Description");
        }

        [Test]
        public void TurtleMode_IsStealProofAndControlImmune_WithTheApprovedNumbers()
        {
            string code = Src("Abilities/TurtleModeAbilitySO.cs");
            // Simplified 2026-11-05: the walk is slow-immune only; the egg that follows is steal-proof and control-immune.
            string egg = Body(code, "public override bool OnActiveTick(", 700);
            StringAssert.Contains("GrantStealProof(remainingSeconds)", egg);
            StringAssert.Contains("GrantControlImmunity(remainingSeconds)", egg);
            StringAssert.DoesNotContain("DamageResistance", code);

            var turtle = (TurtleModeAbilitySO)Asset("TurtleMode");
            Assert.AreEqual(3f, turtle.Duration, 1e-5f);
            Assert.AreEqual(8f, turtle.Cooldown, 1e-5f);
            Assert.AreEqual(0.5f, turtle.SpeedMultiplier, 1e-5f);
            Assert.AreEqual("Waddle for 1s (slows bounce off), then be an egg for 2s: nothing can touch you. Press again to hatch.", turtle.DescriptionText);
            Assert.AreEqual(turtle.DescriptionText, turtle.Description, "stale .asset Description");
            StringAssert.DoesNotContain("DamageResistance", File.ReadAllText("Assets/_Game/Data/Abilities/TurtleMode.asset"));
        }

        // ---- 6. Bots -----------------------------------------------------------

        [Test]
        public void BotTactics_IdentifiesStealers_AndShields()
        {
            foreach (var n in new[] { "Snatch", "SneakySteal", "Scrap", "DiveBomb", "SpineCoat", "MarkKill" })
                Assert.IsTrue(BotTactics.IsStealer(Asset(n)), n);
            Assert.IsFalse(BotTactics.IsStealer(Asset("EggShell")));
            Assert.IsFalse(BotTactics.IsStealer(null));
            Assert.IsTrue(BotTactics.IsStealShield(Asset("EggShell")));
            Assert.IsTrue(BotTactics.IsStealShield(Asset("TurtleMode")));
            Assert.IsFalse(BotTactics.IsStealShield(Asset("Snatch")));
        }

        [TestCase(5f, 5.4f, false, true)]
        [TestCase(4.9f, 3f, false, false)]            // not enough to protect
        [TestCase(8f, 5.5f, false, false)]            // stealer out of reach
        [TestCase(8f, float.MaxValue, false, false)]  // no stealer
        [TestCase(8f, 2f, true, false)]               // already shielded
        public void BotTactics_StealGuardDecision(float cargo, float dist, bool proof, bool expected) =>
            Assert.AreEqual(expected, BotTactics.ShouldStealGuard(cargo, dist, proof));
    }
}
