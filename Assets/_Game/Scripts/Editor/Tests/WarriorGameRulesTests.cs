using System;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Bully's hit-steal and the Enforcer's debts as the game plays them (design 2026-11-25, section 16, BL-226). A live runner is needed to run the
    /// networked behaviours, so these are source guards over the call sites, the one steal path and the shared rules, plus the prefab wiring.
    /// Nothing here asserts a win rate or restates a shipped number.
    /// </summary>
    public sealed class WarriorGameRulesTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string HitStealPath = Scripts + "Gameplay/ChickenHitSteal.cs";
        private const string LedgerPath = Scripts + "Gameplay/HitStealLedger.cs";
        private const string ControllerPath = Scripts + "Gameplay/ChickenController.cs";
        private const string CargoPath = Scripts + "Gameplay/ChickenCargo.cs";
        private const string StunBurstPath = Scripts + "Abilities/StunBurstAbilitySO.cs";
        private const string ShockPath = Scripts + "Abilities/CluckShockAbilitySO.cs";
        private const string SlowPath = Scripts + "Abilities/SlowAbilitySO.cs";

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);
        private static string Code(string path) => RuntimeSourceGuardTests.Code(path);

        // ---- the three hit abilities report every victim, after the effect is sent ----

        [Test]
        public void StunBurst_ReportsEveryVictim_AfterSendControl()
        {
            string body = Body(StunBurstPath, "public override void OnActivate(");
            Assert.AreEqual(1, Regex.Matches(body, @"ReportHit\(").Count, "one report per victim of the loop");
            Assert.Less(body.IndexOf("SendControl(", StringComparison.Ordinal), body.IndexOf("ReportHit(", StringComparison.Ordinal), "the effect is sent first");
            StringAssert.Contains("AbilityJob.Stun", body);
            StringAssert.Contains("caster.PredictStunLands(", body);
        }

        [Test]
        public void CluckShock_ReportsEveryVictim_AfterKnockAway()
        {
            string body = Body(ShockPath, "public override void OnActivate(");
            Assert.AreEqual(1, Regex.Matches(body, @"ReportHit\(").Count);
            Assert.Less(body.IndexOf("KnockAway(", StringComparison.Ordinal), body.IndexOf("ReportHit(", StringComparison.Ordinal));
            StringAssert.Contains("AbilityJob.Shove", body);
            StringAssert.Contains("caster.PredictShoveMoves(", body);
        }

        [Test]
        public void Slow_ReportsEveryVictim_AfterSendControl_ForTheInstantSlowOnly()
        {
            string body = Body(SlowPath, "public override void OnActivate(");
            Assert.Less(body.IndexOf("SendControl(", StringComparison.Ordinal), body.IndexOf("ReportHit(", StringComparison.Ordinal));
            StringAssert.Contains("AbilityJob.Slow", body);
            StringAssert.DoesNotContain("ReportHit(", Code(Scripts + "Gameplay/AbilityZone.cs"), "the Slow Area aura never reports a hit (design section 3)");
        }

        // ---- food moves only through the one authority-confirmed steal path ----

        [Test]
        public void HitSteal_MovesFoodOnlyThroughChickenCargoStealFrom()
        {
            string src = Code(HitStealPath);
            StringAssert.Contains("_cargo.StealFrom(victim, take)", src, "the take is a steal request like every other");
            StringAssert.DoesNotContain("RPC_DrainStolen", src, "the victim's drain is the cargo's business");
            StringAssert.DoesNotContain("RPC_SettleSteal", src);
            Assert.IsFalse(Regex.IsMatch(src, @"\bCargo\s*(\+|-)?=[^=]"), "ChickenHitSteal never writes a Cargo value");
            Assert.IsFalse(Regex.IsMatch(src, @"\.Cargo\s*(\+|-)?=[^=]"), "nor through a reference");
            string settle = Body(CargoPath, "public void RPC_SettleSteal(");
            StringAssert.Contains("_controller.Hits?.OnStealSettled(victimId, requestId, settled.Credited)", settle, "the settle tells the hit-steal what a request credited");
            Assert.Less(settle.IndexOf("OnStealSettled(", StringComparison.Ordinal), settle.IndexOf("if (settled.Credited <= 0f", StringComparison.Ordinal),
                "told before the zero-credit early return, or a refused take could never give its lock back");
        }

        [Test]
        public void TheCargoTellsTheHitSteal_BeforeItSendsTheDrain()
        {
            // A victim this peer also owns (a bot, solo) answers inside RPC_DrainStolen, so the lock must hold the request id before the call.
            string request = Body(CargoPath, "private void RequestSteal(");
            int ask = request.IndexOf("_requests.AskSteal(", StringComparison.Ordinal);
            int told = request.IndexOf("_controller.Hits?.OnStealOpened(", StringComparison.Ordinal);
            int rpc = request.IndexOf("RPC_DrainStolen(asked, _controller.Id, requestId)", StringComparison.Ordinal);
            Assert.Greater(ask, 0);
            Assert.Greater(told, ask, "told after the request is open");
            Assert.Greater(rpc, told, "and before the drain is sent");

            string opened = Body(HitStealPath, "internal void OnStealOpened(");
            StringAssert.Contains("if (!_asking", opened, "only the request this chicken's own hit opens is a hit-steal's");
            StringAssert.Contains("_ledger.Ask(", opened);
            string onHit = Body(HitStealPath, "public void OnHit(");
            StringAssert.Contains("_asking = true;", onHit);
            StringAssert.Contains("finally { _asking = false; }", onHit, "the staging flag can never stay set");
        }

        [Test]
        public void Game_CallsTheSharedRules_NotCopies()
        {
            // Bully half. Each scan proves it saw its own file first (CODE-TEST-13).
            string hit = Code(HitStealPath), ledger = Code(LedgerPath), controller = Code(ControllerPath);
            StringAssert.Contains("class ChickenHitSteal", hit);
            StringAssert.Contains("class HitStealLedger", ledger);
            StringAssert.Contains("class ChickenController", controller);
            StringAssert.Contains("HitStealRules.Take(", hit, "the take is the simulator's rule");
            StringAssert.Contains("HitStealRules.Locked(", ledger, "the lock is the simulator's rule");
            StringAssert.Contains("HitAffectRules.StunAffects(", controller, "the stun prediction is the shared rule");
            StringAssert.Contains("HitAffectRules.ShoveMoves(", controller, "the shove prediction is the shared rule");
        }

        [Test]
        public void ReportHit_IsANoOpForAPassiveWithoutFacts()
        {
            // The facts of every passive that is not a Bully's are off: no job is priced.
            foreach (AbilityJob job in Enum.GetValues(typeof(AbilityJob)))
                Assert.AreEqual(0f, HitStealFacts.None.AmountFor(job), $"{job}: no facts, no take");

            string onHit = Body(HitStealPath, "public void OnHit(");
            int amount = onHit.IndexOf("float amount = facts.AmountFor(job);", StringComparison.Ordinal);
            int guard = onHit.IndexOf("if (amount <= 0f) return;", StringComparison.Ordinal);
            Assert.Greater(amount, 0, "the price of this job is read from the passive's facts");
            Assert.Greater(guard, amount, "an unpriced job returns before anything is asked or locked");
            Assert.Less(guard, onHit.IndexOf("StealFrom(", StringComparison.Ordinal));
            string facts = Body(HitStealPath, "public HitStealFacts Facts");
            StringAssert.Contains("HitStealFacts.None", facts, "no passive, no facts");

            string report = Body(ControllerPath, "public void ReportHit(");
            StringAssert.Contains("victim.IsDecoy", report, "a decoy victim is no take");
            StringAssert.Contains("!HasStateAuthority", report);
        }

        [Test]
        public void ChickenHitSteal_FixedUpdateNetwork_OpensWithTheAuthorityGate()
        {
            string body = Body(HitStealPath, "public override void FixedUpdateNetwork()");
            int gate = body.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            Assert.Greater(gate, 0, "ARCH-NET-01: FixedUpdateNetwork opens with the authority gate");
            string beforeGate = body.Substring(body.IndexOf('{') + 1, gate - body.IndexOf('{') - 1);
            Assert.IsTrue(string.IsNullOrWhiteSpace(beforeGate), "nothing runs before the gate");
            string src = Code(HitStealPath);
            StringAssert.Contains("SelfInject.Ensure(this, _log != null);", src);
            StringAssert.DoesNotContain("Debug.Log", src);
            StringAssert.DoesNotContain("Container.Inject(this)", src);
            StringAssert.Contains("_ledger.Clear()", body, "a lock never outlives the round or the chicken");
        }

        [Test]
        public void HitStealEvent_WritesItsFieldsBeforeTheId()
        {
            string settled = Body(HitStealPath, "internal void OnStealSettled(");
            int bump = settled.IndexOf("HitStealEventId++", StringComparison.Ordinal);
            Assert.Greater(bump, 0);
            foreach (var field in new[] { "LastHitVictim =", "LastHitTake =" })
                Assert.Less(settled.IndexOf(field, StringComparison.Ordinal), bump, $"{field} is written before the bump (ARCH-EVT-02)");
            StringAssert.Contains("HitStealEventId", Code(Scripts + "Gameplay/ChickenLocalEvents.cs"), "the one observer raises it (ARCH-EVT-03)");
        }

        // ---- the prefab ----

        private static void AssertListed(GameObject prefab, Component behaviour, string why)
        {
            var listed = new SerializedObject(prefab.GetComponent<NetworkObject>()).FindProperty("NetworkedBehaviours");
            bool found = false;
            for (int i = 0; i < listed.arraySize; i++)
                if (listed.GetArrayElementAtIndex(i).objectReferenceValue == behaviour) found = true;
            Assert.IsTrue(found, why);
        }

        [Test]
        public void ChickenPrefab_CarriesChickenHitSteal_AndListsItAmongItsNetworkedBehaviours()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var hit = prefab.GetComponent<ChickenHitSteal>();
            Assert.IsNotNull(hit, "Chicken.prefab has no ChickenHitSteal: no Bully would ever take (CODE-ASSET-11)");
            Assert.AreEqual(1, prefab.GetComponentsInChildren<ChickenHitSteal>(true).Length);
            AssertListed(prefab, hit, "ChickenHitSteal is on the prefab but not in NetworkObject.NetworkedBehaviours: Fusion would not replicate it");
        }

        [Test]
        public void DoppelgangerVariant_ListsEveryWarriorBehaviourItInherits()
        {
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Doppelganger.prefab");
            Assert.IsNotNull(variant);
            var hit = variant.GetComponent<ChickenHitSteal>();
            Assert.IsNotNull(hit, "the decoy variant inherits the component (CODE-ASSET-06)");
            AssertListed(variant, hit, "the variant's own NetworkedBehaviours list must be baked with the new component");
        }
    }
}
