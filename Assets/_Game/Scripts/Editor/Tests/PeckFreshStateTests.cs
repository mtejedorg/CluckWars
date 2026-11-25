using System;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Peck's fresh state on every chicken (design 2026-11-25, 16.2, BL-226): a stamp and three ids on <c>AbilityController</c>, written by the
    /// chicken's own authority at the two moments that open a state (a cast start, a landing that credited food) and spent by an Enforcer's
    /// <c>RPC_MarkBitten</c>. A live runner is needed to execute the networked members, so these are source guards plus the pure rule.
    /// </summary>
    public sealed class PeckFreshStateTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";
        private const string CargoPath = "Assets/_Game/Scripts/Gameplay/ChickenCargo.cs";
        private const string PeckPath = "Assets/_Game/Scripts/Abilities/PeckAbilitySO.cs";

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        [Test]
        public void SettlePeck_NotesFed_OnlyWhenFoodWasCredited()
        {
            string settle = Body(CargoPath, "public void RPC_SettlePeck(");
            int branch = settle.IndexOf("if (settled.ToCargo > 0f)", StringComparison.Ordinal);
            int fed = settle.IndexOf("NotePeckFed()", StringComparison.Ordinal);
            Assert.Greater(branch, 0, "the credit branch exists");
            Assert.Greater(fed, branch, "the fed note sits inside the branch that credits food, never before it");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(settle, @"NotePeckFed\(").Count, "one note, only at the settle (the cast end opens nothing)");
            Assert.Less(settle.IndexOf("Cargo += settled.ToCargo", StringComparison.Ordinal), fed, "the food is credited before the state opens");
            StringAssert.DoesNotContain("NotePeckFed(", Body(PeckPath, "public override void OnDeactivate("), "a landing that took 0 opens no state, so the cast end must not note one");
        }

        [Test]
        public void PeckOnActivate_StartsANewFreshState()
        {
            StringAssert.Contains("chicken.Abilities.NoteFreshStart();", Body(PeckPath, "public override void OnActivate("));
            string note = Body(ControllerPath, "internal void NoteFreshStart()");
            StringAssert.Contains("if (HasStateAuthority) FreshStateId++;", note, "only the chicken's own authority writes the id");
            string fed = Body(ControllerPath, "internal void NotePeckFed()");
            Assert.Less(fed.IndexOf("if (!HasStateAuthority", StringComparison.Ordinal), fed.IndexOf("FreshStateId++", StringComparison.Ordinal), "the authority gate comes first");
            Assert.Less(fed.IndexOf("FreshStateId++", StringComparison.Ordinal), fed.IndexOf("FedStateId = FreshStateId", StringComparison.Ordinal), "the landing opens a new state and names it");
            StringAssert.Contains("FedTick = Runner.Tick.Raw", fed);
        }

        [Test]
        public void MarkBitten_ChecksTheSenderBeforeAnyWrite()
        {
            string rpc = Body(ControllerPath, "public void RPC_MarkBitten(");
            int claim = rpc.IndexOf("RpcClaim.SenderSpeaksFor(hitter, info)", StringComparison.Ordinal);
            int enforcer = rpc.IndexOf("Debt.IsEnforcer", StringComparison.Ordinal);
            int write = rpc.IndexOf("BittenStateId = stateId", StringComparison.Ordinal);
            Assert.Greater(claim, 0, "the sender must speak for the named hitter (ARCH-NET-03)");
            Assert.Greater(enforcer, 0, "and the hitter must hold an Enforcer's debts");
            Assert.Greater(write, claim, "the write comes after the sender check");
            Assert.Greater(write, enforcer, "and after the Enforcer check");
            StringAssert.Contains("if (stateId == FreshStateId)", rpc, "a late claim cannot spend a newer state");
            StringAssert.Contains("rejected RPC_MarkBitten", rpc, "a refusal is a rate-limited Warn with the stable prefix (ARCH-NET-04, ARCH-ERR-06)");
            StringAssert.Contains("BiteRejectionLimiter.Allow(", rpc);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(RuntimeSourceGuardTests.Code(ControllerPath), @"BittenStateId = ").Count, "one writer of the bitten id");
        }

        [Test]
        public void IsFreshFor_GoesThroughBiteRulesFresh()
        {
            string fresh = Body(ControllerPath, "public bool IsFreshFor(");
            StringAssert.Contains("BiteRules.Fresh(", fresh, "the shared rule, not a copy");
            StringAssert.Contains("BiteRules.LandedWithFood(FedTick, FedStateId, FreshStateId)", fresh);
            StringAssert.Contains("IsPeckCasting", fresh);
            StringAssert.Contains("SecondsSinceFed", fresh);
        }

        [Test]
        public void RoundReset_ClearsTheFedStamp_AndLeavesTheIdsAlone()
        {
            string reset = Body(ControllerPath, "public void EndAllForReset()");
            StringAssert.Contains("FedTick = 0;", reset);
            StringAssert.DoesNotContain("FreshStateId = 0", RuntimeSourceGuardTests.Code(ControllerPath), "byte counters are never reset (ARCH-EVT-02)");
            StringAssert.DoesNotContain("BittenStateId = 0", RuntimeSourceGuardTests.Code(ControllerPath));
        }
    }
}
