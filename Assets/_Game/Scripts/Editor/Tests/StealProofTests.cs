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
            StringAssert.Contains("EffectWindow.TryGrant(StealProofTimer, Runner, seconds, out var granted)", grant);   // the later-end-wins rule lives in EffectWindow.TryGrant (ControlImmunityRules.ShouldReplace)
            StringAssert.Contains("StealProofTimer = granted;", grant);
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
            // The drain body is DrainForThief since the Thief's marked steal (BL-217): RPC_DrainStolen and RPC_DrainStolenMarked share it.
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "private float DrainForThief(", 3200);
            int proof  = body.IndexOf("_controller.IsStealProof", StringComparison.Ordinal);
            int refusal = body.IndexOf("refused = true;", StringComparison.Ordinal);
            int drain  = body.IndexOf("float actual = StealRules.DrainAmount(", StringComparison.Ordinal);
            int stamp  = body.IndexOf("StealEventId++", StringComparison.Ordinal);
            Assert.Greater(proof, 0);
            Assert.Greater(refusal, proof, "the refusal is flagged from the shell branch");
            Assert.Greater(drain, refusal, "refusal must return before the drain");
            Assert.Greater(stamp, refusal, "a refused steal must not name a thief (no LastThiefId/StealEventId)");
            StringAssert.Contains("return 0f;", body.Substring(proof, drain - proof), "a refusal drains nothing");

            string rpc = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_DrainStolen(", 3200);
            StringAssert.Contains("ReportSteal(thief, DrainForThief(thief, amount, out _), requestId)", rpc, "the zero report of a refusal goes out through the same ReportSteal");
        }

        [Test]
        public void RpcSettleSteal_TakesTheSender_AndOnlyHonoursTheVictimsAuthority()
        {
            var m = typeof(ChickenCargo).GetMethod(nameof(ChickenCargo.RPC_SettleSteal));
            Assert.IsNotNull(m);
            Assert.IsTrue(m.GetParameters().Any(p => p.ParameterType == typeof(Fusion.RpcInfo)));
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_SettleSteal(", 3000);
            StringAssert.Contains("SpeaksFor(victim, info)", body);
            StringAssert.Contains("_requests.SettleSteal(victimId, requestId, amount", body);

            // The rule itself is RpcClaim's (A19): local invoke = simulated here, off the wire = the state authority.
            Assert.IsTrue(RpcClaim.SpeaksFor(isInvokeLocal: true, simulatedHere: true, senderIsStateAuthority: false));
            Assert.IsTrue(RpcClaim.SpeaksFor(isInvokeLocal: false, simulatedHere: false, senderIsStateAuthority: true));
            Assert.IsFalse(RpcClaim.SpeaksFor(isInvokeLocal: false, simulatedHere: true, senderIsStateAuthority: false));
            Assert.IsNull(typeof(ChickenCargo).GetMethod("RPC_RefundRefusedSteal"), "the optimistic credit and its refund are back");
        }

        [Test]
        public void RpcTransferAllToExecutor_RefusesAStealProofVictim_BeforeTouchingCargo()
        {
            string body = Body(Src("Gameplay/ChickenCargo.cs"), "public void RPC_TransferAllToExecutor(", 1400);
            int proof = body.IndexOf("_controller.IsStealProof", StringComparison.Ordinal);
            int zero  = body.IndexOf("Cargo = 0f", StringComparison.Ordinal);
            Assert.Greater(proof, 0);
            Assert.Greater(zero, proof);
        }

        // ---- 3. The six thief sites: gate BEFORE the self-credit ---------------

        [TestCase("Abilities/SnatchAbilitySO.cs",      "StealFrom(thief, _scratch[i], StealAmount, thief.Debts)")]   // the Enforcer's debts ride on the same entry point (BL-226)
        [TestCase("Abilities/SneakyStealAbilitySO.cs", "StealFrom(thief, _scratch[0], StealAmount)")]
        [TestCase("Abilities/ScrapAbilitySO.cs",       "StealFrom(thief, _scratch[0], StealAmount)")]
        [TestCase("Abilities/RollTrampleAbilitySO.cs", "StealFrom(thief, _scratch[0], StealAmount)")]
        [TestCase("Gameplay/ChickenController.cs",     "Cargo.StealFrom(other, StealBackAmount)")]
        public void ThiefSite_ChecksStealProof_BeforeCreditingItself(string file, string steal)
        {
            // A20: the gate is no longer written at each site; every site calls the one entry point that carries it.
            string src = Src(file);
            StringAssert.Contains(steal, src, $"{file}: anchor '{steal}' moved.");
            StringAssert.DoesNotContain("RequestSteal(", StripLineComments(src), $"{file} asks the victim without the steal-proof gate.");
            StringAssert.DoesNotContain("StealMath.Clamp(", src, $"{file} clamps by hand; ChickenCargo.StealFrom does.");
        }

        private static string StripLineComments(string src) =>
            string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));

        [Test]
        public void TheOneStealEntryPoint_Gates_Clamps_ThenAsks()
        {
            string body = AssassinExecuteCreditTests.MethodBody(Dir + "Gameplay/ChickenCargo.cs", "public float StealFrom(");
            int gate = body.IndexOf("if (!StealRules.CanStealFrom(victim)) return 0f;", StringComparison.Ordinal);
            int clamp = body.IndexOf("StealMath.Clamp(amount, Capacity - Cargo, victimCargo.Stealable)", StringComparison.Ordinal);
            int ask = body.IndexOf("RequestSteal(victimCargo, take);", StringComparison.Ordinal);
            Assert.Greater(gate, 0, "no steal-proof gate: the thief would ask a shelled victim");
            Assert.Greater(clamp, gate);
            Assert.Greater(ask, clamp);
            StringAssert.Contains("if (take <= 0f) return 0f;", body, "nothing is asked for an empty victim or a full thief");

            // The ability wrapper applies the thief's passive, then calls it.
            string wrapper = AssassinExecuteCreditTests.MethodBody(Dir + "Abilities/AbilityBaseSO.cs", "protected static float StealFrom(");
            StringAssert.Contains("cargo.StealFrom(victim, ResolveStealAmount(nominalAmount, thief))", wrapper);

            // RequestSteal is private: nothing outside ChickenCargo can ask without the gate.
            Assert.IsNull(typeof(ChickenCargo).GetMethod("RequestSteal"), "RequestSteal must not be public again");
        }

        [Test]
        public void TheTwoOldClamps_AreTheSameNumber()
        {
            // Snatch, Roll Trample and the Spine Coat used StealMath.Clamp; Sneaky Steal and Scrap used
            // Mathf.Min(amount, stealable, space) followed by "<= 0 -> nothing". Same answer for every finite input.
            foreach (float amount in new[] { 0f, 3f, 4.2f, 5f, 6f, 9.6f })
            foreach (float space in new[] { -2f, 0f, 0.5f, 3f, 12f })
            foreach (float stealable in new[] { 0f, 0.25f, 4f, 20f })
            {
                float viaMin = Mathf.Min(amount, stealable, space);
                float old = viaMin <= 0f ? 0f : viaMin;
                Assert.AreEqual(old, StealMath.Clamp(amount, space, stealable), 0f, $"{amount} {space} {stealable}");
            }
        }

        [Test]
        public void SnatchAndSpineCoat_SkipOnlyTheSteal_TheKnockbackIsUntouched()
        {
            string snatch = Src("Abilities/SnatchAbilitySO.cs");
            // The steal (gated inside StealFrom) and the shove are two unconditional statements: a shell stops only the first.
            StringAssert.IsMatch(@"StealFrom\(thief, _scratch\[i\], StealAmount, thief\.Debts\);\s*thief\.KnockAway\(_scratch\[i\], KnockbackStrength\);", snatch);
            string spine = Src("Gameplay/ChickenController.cs");
            StringAssert.IsMatch(@"Cargo\.StealFrom\(other, StealBackAmount\);\s*KnockAway\(other, StealBackKnockback\);", spine);
        }

        [Test]
        public void NearestTargetAbilities_DoNotFallBackToTheNextRival()
        {
            foreach (var f in new[] { "SneakyStealAbilitySO", "ScrapAbilitySO", "RollTrampleAbilitySO" })
            {
                string src = Src($"Abilities/{f}.cs");
                StringAssert.Contains("StealFrom(thief, _scratch[0], StealAmount)", src, f);
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
            int rpc  = body.IndexOf("RPC_TransferAllToExecutor", StringComparison.Ordinal);
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
                if (!src.Contains(".RPC_DrainStolen(") && !src.Contains(".RequestSteal(") && !src.Contains(".RPC_TransferAllToExecutor(")) continue;
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
            Assert.AreEqual("Walk slowly for 1s (slows can't stop you), then turn into an egg for 2s: nothing can touch you. Press again to come out early.", turtle.DescriptionText);
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
