using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A01 (architecture audit 2026-11-12): the comeback flags are written only on the chicken's own state authority,
    /// through one match-authority RPC. The rule is asserted for real; the wiring by shape (no runner in EditMode).
    /// </summary>
    public sealed class ComebackRulesTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";
        private const string OwnerFile = "ChickenController.cs";

        [TestCase(ComebackGrant.None, false)]
        [TestCase(ComebackGrant.UnderdogSurge, true)]
        [TestCase((ComebackGrant)2, false)]   // the removed LeaderBounty bit, from an older build
        [TestCase((ComebackGrant)3, false)]
        [TestCase((ComebackGrant)4, false)]
        public void IsValid_NeedsAKnownEffect_AndNothingElse(ComebackGrant grant, bool expected) =>
            Assert.AreEqual(expected, ComebackRules.IsValid(grant));

        [TestCase(ComebackGrant.UnderdogSurge, false, true)]
        [TestCase(ComebackGrant.UnderdogSurge, true, true)]
        [TestCase(ComebackGrant.None, true, true)]            // an invalid grant changes nothing
        [TestCase(ComebackGrant.None, false, false)]
        [TestCase((ComebackGrant)3, false, false)]
        public void Apply_OnlyEverSwitchesOn(ComebackGrant grant, bool surge, bool expectSurge)
        {
            ComebackRules.Apply(grant, ref surge);
            Assert.AreEqual(expectSurge, surge, "surge");
        }

        [Test]
        public void ComebackEvent_IsNeverLeaderBounty()
        {
            // Every roll the GameManager can draw maps to one of the three remaining kinds (D7b: Leader Bounty is gone).
            var allowed = new[] { MatchEventKind.GoldenPile, MatchEventKind.UnderdogSurge, MatchEventKind.Restock };
            var seen = new System.Collections.Generic.HashSet<MatchEventKind>();
            for (int roll = 0; roll < ComebackRules.Events.Length; roll++)
            {
                var kind = ComebackRules.EventForRoll(roll);
                CollectionAssert.Contains(allowed, kind, $"roll {roll}");
                seen.Add(kind);
            }
            Assert.AreEqual(3, seen.Count, "all three remaining kinds are reachable, none twice");
            foreach (var name in System.Enum.GetNames(typeof(MatchEventKind)))
                StringAssert.DoesNotContain("LeaderBounty", name);
            Assert.AreEqual(1, (byte)MatchEventKind.GoldenPile, "the remaining members keep their numbers");
            Assert.AreEqual(2, (byte)MatchEventKind.UnderdogSurge);
            Assert.AreEqual(4, (byte)MatchEventKind.Restock);
            string gm = File.ReadAllText($"{ScriptsRoot}/Gameplay/GameManager.cs");
            StringAssert.Contains("ComebackRules.EventForRoll(Random.Range(0, ComebackRules.Events.Length))", gm,
                "GameManager rolls over the remaining kinds only");
        }

        [Test]
        public void LeaderBountyActive_IsGoneFromTheScripts()
        {
            foreach (var path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string file = Path.GetFileName(path);
                if (file == nameof(ComebackRulesTests) + ".cs") continue;   // this file quotes the name
                string src = File.ReadAllText(path);
                StringAssert.DoesNotContain("LeaderBountyActive", src, path);
                StringAssert.DoesNotContain("LeaderExecuteBountyMultiplier", src, path);
            }
        }

        [TestCase(nameof(ChickenController.UnderdogSurgeActive))]
        public void Flag_SetterIsPrivate(string property)
        {
            var setter = typeof(ChickenController).GetProperty(property).GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter, $"{property} lost its setter.");
            Assert.IsTrue(setter.IsPrivate, $"{property} is writable from outside ChickenController again. A write from a " +
                "peer that does not own the chicken is silently lost in Shared Mode: go through RPC_GrantComeback.");
        }

        [Test]
        public void NoOtherFile_AssignsTheFlag()
        {
            var assignment = new Regex(@"\b(UnderdogSurgeActive)\s*(=(?!=)|\+\+|--|[-+*/|&^]=)");
            foreach (var path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string file = Path.GetFileName(path);
                if (file == OwnerFile || file == nameof(ComebackRulesTests) + ".cs") continue;   // this file quotes the write
                var m = assignment.Match(File.ReadAllText(path));
                Assert.IsFalse(m.Success, $"{path} assigns {m.Groups[1].Value}. Only ChickenController may write the comeback " +
                    "flags (RPC_GrantComeback on the chicken's state authority, the round reset to clear them).");
            }
        }

        [Test]
        public void TheReset_ThatClearsTheFlags_IsSenderCheckedToo()
        {
            // R-7: source guard (the branch needs a live runner). An unvalidated reset let any peer strip a rival's surge.
            var rpc = typeof(ChickenController).GetMethod(nameof(ChickenController.RPC_ResetControlStates), BindingFlags.Public | BindingFlags.Instance);
            Assert.IsTrue(System.Array.Exists(rpc.GetParameters(), p => p.ParameterType == typeof(Fusion.RpcInfo)));
            string body = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/{OwnerFile}", "public void RPC_ResetControlStates(");
            int gate = body.IndexOf("IsFromMatchAuthority(info", System.StringComparison.Ordinal);
            Assert.Greater(gate, 0, "the match-authority gate is gone");
            Assert.Greater(body.IndexOf("UnderdogSurgeActive  = false", System.StringComparison.Ordinal), gate);
            Assert.Greater(body.IndexOf("EndAllForReset()", System.StringComparison.Ordinal), gate, "nothing is reset before the gate");
        }

        [TestCase("ChickenCargo.cs", typeof(ChickenCargo), "Cargo = 0f;")]
        [TestCase("ChickenCombat.cs", typeof(ChickenCombat), "IsRemoved = false;")]
        public void TheRoundResets_OfCargoAndCombat_AreSenderCheckedToo(string file, System.Type type, string firstWrite)
        {
            // A19: source guard (the branch needs a live runner). Unvalidated, any peer could empty a rival's hold,
            // or stand an executed chicken back up.
            var rpc = type.GetMethod("RPC_ResetForNewMatch", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(rpc);
            Assert.IsTrue(System.Array.Exists(rpc.GetParameters(), p => p.ParameterType == typeof(Fusion.RpcInfo)));
            string body = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/{file}", "public void RPC_ResetForNewMatch(");
            int gate = body.IndexOf("ChickenController.IsFromMatchAuthority(info", System.StringComparison.Ordinal);
            Assert.Greater(gate, 0, "the match-authority gate is gone");
            int ret = body.IndexOf("return;", gate, System.StringComparison.Ordinal);
            Assert.Greater(ret, gate, "a rejected call must return");
            Assert.Greater(body.IndexOf(firstWrite, System.StringComparison.Ordinal), ret, "nothing is reset before the gate");
            foreach (string write in new[] { "_requests.Clear()", "_pendingExecutes.Clear()", "StunTimer = default", "_markSeenAt.Clear()" })
            {
                int at = body.IndexOf(write, System.StringComparison.Ordinal);
                if (at >= 0) Assert.Greater(at, ret, $"{write} runs before the gate");
            }
        }

        [Test]
        public void Grant_IsSenderChecked_AndGameManagerUsesIt()
        {
            var rpc = typeof(ChickenController).GetMethod(nameof(ChickenController.RPC_GrantComeback), BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(rpc);
            Assert.IsTrue(System.Array.Exists(rpc.GetParameters(), p => p.ParameterType == typeof(Fusion.RpcInfo)),
                "RPC_GrantComeback is RpcSources.All: without RpcInfo it cannot tell the match authority from anyone else.");

            string ctrl = File.ReadAllText($"{ScriptsRoot}/Gameplay/{OwnerFile}");
            int at = ctrl.IndexOf("public void RPC_GrantComeback(", System.StringComparison.Ordinal);
            Assert.Greater(at, 0);
            string body = ctrl.Substring(at, 1200);
            int gate = body.IndexOf("IsFromMatchAuthority(info", System.StringComparison.Ordinal);
            int write = body.IndexOf("UnderdogSurgeActive =", System.StringComparison.Ordinal);
            Assert.Greater(gate, 0, "the match-authority gate is gone");
            Assert.Greater(write, gate, "the gate must come before the write");

            string gm = File.ReadAllText($"{ScriptsRoot}/Gameplay/GameManager.cs");
            StringAssert.Contains("RPC_GrantComeback(ComebackGrant.UnderdogSurge)", gm);
        }
    }
}
