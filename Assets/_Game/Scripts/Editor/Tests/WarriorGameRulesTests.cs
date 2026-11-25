using System;
using System.IO;
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

        // ---- the three hit abilities report every victim: predict, then send, then report ----
        // The prediction reads the victim's replicated state. A victim this peer also owns applies the effect INSIDE the send (RPC_ApplyStun runs locally),
        // so a prediction made after it reads the new StunRemaining and a strict-greater "does it extend" test is false: the stun would never count.

        private static void AssertPredictSendReport(string body, string predict, string send)
        {
            int p = body.IndexOf(predict, StringComparison.Ordinal);
            int s = body.IndexOf(send, StringComparison.Ordinal);
            int r = body.IndexOf("ReportHit(", StringComparison.Ordinal);
            Assert.Greater(p, 0, "the prediction is made");
            Assert.Less(p, s, "predicted BEFORE the effect is sent, while the victim's state is still the pre-hit one");
            Assert.Less(s, r, "the report comes last");
            Assert.AreEqual(1, Regex.Matches(body, Regex.Escape(predict)).Count, "predicted once, never again after the send");
        }

        [Test]
        public void StunBurst_ReportsEveryVictim_AfterSendControl()
        {
            string body = Body(StunBurstPath, "public override void OnActivate(");
            Assert.AreEqual(1, Regex.Matches(body, @"ReportHit\(").Count, "one report per victim of the loop");
            AssertPredictSendReport(body, "caster.PredictStunLands(", "SendControl(");
            StringAssert.Contains("AbilityJob.Stun", body);
        }

        [Test]
        public void CluckShock_ReportsEveryVictim_AfterKnockAway()
        {
            string body = Body(ShockPath, "public override void OnActivate(");
            Assert.AreEqual(1, Regex.Matches(body, @"ReportHit\(").Count);
            AssertPredictSendReport(body, "caster.PredictShoveMoves(", "KnockAway(");
            StringAssert.Contains("AbilityJob.Shove", body);
        }

        [Test]
        public void Slow_ReportsEveryVictim_AfterSendControl_ForTheInstantSlowOnly()
        {
            string body = Body(SlowPath, "public override void OnActivate(");
            AssertPredictSendReport(body, "caster.PredictSlowLands(", "SendControl(");
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

        // ---- the Enforcer's debts (G5) ----

        private const string DebtsPath = Scripts + "Gameplay/EnforcerDebts.cs";
        private const string DebtBookPath = Scripts + "Gameplay/DebtBook.cs";
        private const string SnatchPath = Scripts + "Abilities/SnatchAbilitySO.cs";
        private const string AbilityBasePath = Scripts + "Abilities/AbilityBaseSO.cs";
        private const string AbilityControllerPath = Scripts + "Gameplay/AbilityController.cs";

        [Test]
        public void Snatch_ReportsTheHitBeforeItsSteal_AndAsksTheInflatedAmount()
        {
            string body = Body(SnatchPath, "public override void OnActivate(");
            int report = body.IndexOf("ReportHit(thief, _scratch[i], AbilityJob.Steal,", StringComparison.Ordinal);
            int steal = body.IndexOf("StealFrom(thief, _scratch[i], StealAmount, thief.Debts)", StringComparison.Ordinal);
            Assert.Greater(report, 0, "a plain Steal on a carrier is a hit");
            Assert.Greater(steal, report, "the fresh stack counts toward this very Steal, so the hit is reported first (design 4.2 step 3 before 4)");
            StringAssert.Contains("StealRules.CanStealFrom(", body.Substring(report, steal - report), "a shell is no hit");

            string inflated = Body(AbilityBasePath, "protected static float StealFrom(Gameplay.ChickenController thief, Gameplay.ChickenController victim, float nominalAmount, Gameplay.EnforcerDebts debts)");
            StringAssert.Contains("debts.StealAmountOn(victim, resolved)", inflated, "the amount comes from the debts' own rule");
            int count = inflated.IndexOf("debts.CountOn(victim)", StringComparison.Ordinal);
            int begin = inflated.IndexOf("debts.BeginAsk(consumed)", StringComparison.Ordinal);
            int ask = inflated.IndexOf("cargo.StealFrom(victim, debts.StealAmountOn", StringComparison.Ordinal);
            Assert.Greater(count, 0);
            Assert.Greater(begin, count, "the count the ask used is read, then staged");
            Assert.Greater(ask, begin, "before the ask");
            StringAssert.Contains("finally { debts.EndAsk(); }", inflated, "the staged count can never leak into another request");
            StringAssert.Contains("return cargo.StealFrom(victim, resolved);", inflated, "no debts component: exactly the plain steal");
        }

        [Test]
        public void ReportHit_ForwardsToTheDebts_AndTheSettleTellsThem()
        {
            string report = Body(ControllerPath, "public void ReportHit(");
            Assert.Less(report.IndexOf("_hitSteal?.OnHit(", StringComparison.Ordinal), report.IndexOf("_debts?.OnHit(", StringComparison.Ordinal));
            string settle = Body(CargoPath, "public void RPC_SettleSteal(");
            StringAssert.Contains("_controller.Debts?.OnStealSettled(victimId, requestId, settled.Credited)", settle);
            Assert.Less(settle.IndexOf("Debts?.OnStealSettled(", StringComparison.Ordinal), settle.IndexOf("if (settled.Credited <= 0f", StringComparison.Ordinal),
                "told before the zero-credit early return, or the pending ask would never close");
            StringAssert.Contains("_debts?.ResetForNewRound();", Body(ControllerPath, "public void RPC_ResetControlStates("), "a new round starts with no debts");
        }

        [Test]
        public void TheCargoTellsTheDebts_BeforeItSendsTheDrain()
        {
            string request = Body(CargoPath, "private void RequestSteal(");
            int told = request.IndexOf("_controller.Debts?.OnStealOpened(", StringComparison.Ordinal);
            Assert.Greater(told, request.IndexOf("_requests.AskSteal(", StringComparison.Ordinal), "told after the request is open");
            Assert.Less(told, request.IndexOf("RPC_DrainStolen(asked, _controller.Id, requestId)", StringComparison.Ordinal), "and before the drain is sent: a local victim answers inside it");
            string opened = Body(DebtsPath, "internal void OnStealOpened(");
            StringAssert.Contains("_stagedConsumed <= 0", opened, "only a Steal that staged debts is recorded");
        }

        [Test]
        public void Game_CallsTheSharedRules_NotCopies_Enforcer()
        {
            string debts = Code(DebtsPath), book = Code(DebtBookPath), abilities = Code(AbilityControllerPath);
            StringAssert.Contains("class EnforcerDebts", debts);
            StringAssert.Contains("class DebtBook", book);
            StringAssert.Contains("class AbilityController", abilities);
            StringAssert.Contains("BiteRules.Fresh(", abilities, "freshness is the simulator's rule");
            StringAssert.Contains("IsFreshFor(", debts, "and the Enforcer reads it through the one composition");
            StringAssert.Contains("DebtRules.Add(", book);
            StringAssert.Contains("DebtRules.Decayed(", book);
            StringAssert.Contains("DebtRules.StealAmount(", debts, "the inflated Steal is the simulator's rule");
        }

        [Test]
        public void EnforcerDebts_FixedUpdateNetwork_OpensWithTheAuthorityGate()
        {
            string body = Body(DebtsPath, "public override void FixedUpdateNetwork()");
            int gate = body.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            Assert.Greater(gate, 0, "ARCH-NET-01: FixedUpdateNetwork opens with the authority gate");
            string beforeGate = body.Substring(body.IndexOf('{') + 1, gate - body.IndexOf('{') - 1);
            Assert.IsTrue(string.IsNullOrWhiteSpace(beforeGate), "nothing runs before the gate");
            string src = Code(DebtsPath);
            StringAssert.Contains("SelfInject.Ensure(this, _log != null);", src);
            StringAssert.DoesNotContain("Debug.Log", src);
            StringAssert.DoesNotContain("Container.Inject(this)", src);
            StringAssert.Contains("ClearAll();", body, "no debts outside a running round, nor for a removed or decoy chicken");
        }

        [Test]
        public void DebtCapacity_HoldsEveryRivalOfMaxPlayers()
        {
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            Assert.GreaterOrEqual(EnforcerDebts.Slots, match.MaxPlayers - 1, "one slot per rival of a full room");
            string src = Code(DebtsPath);
            Assert.AreEqual(3, Regex.Matches(src, @"\[Networked, Capacity\(Slots\)\]").Count, "the three arrays share the one capacity");
            StringAssert.Contains("no free debt slot", File.ReadAllText(DebtsPath), "a full table is reported, never silent");
        }

        [Test]
        public void DebtTimers_AreReadThroughEffectWindow()
        {
            string src = Code(DebtsPath);
            StringAssert.Contains("EffectWindow.IsOpen(", src);
            StringAssert.Contains("EffectWindow.Remaining(", src);
            StringAssert.DoesNotContain(".RemainingTime(", src, "ARCH-NET-15: a window is read through EffectWindow");
            StringAssert.DoesNotContain(".Expired(", src);
            StringAssert.DoesNotContain(".ExpiredOrNotRunning(", src);
        }

        [Test]
        public void DebtEvents_WriteTheirFieldsBeforeTheirIds()
        {
            string hit = Body(DebtsPath, "public void OnHit(");
            Assert.Less(hit.IndexOf("FreshHitVictim = victim.Id;", StringComparison.Ordinal), hit.IndexOf("FreshHitEventId++", StringComparison.Ordinal), "ARCH-EVT-02: the payload first");
            string gain = Body(DebtsPath, "private void Gain(");
            Assert.Less(gain.IndexOf("DebtGainedVictim = victim.Id;", StringComparison.Ordinal), gain.IndexOf("DebtEventId++", StringComparison.Ordinal));
            string settled = Body(DebtsPath, "internal void OnStealSettled(");
            int bump = settled.IndexOf("DebtCashEventId++", StringComparison.Ordinal);
            Assert.Greater(bump, 0);
            Assert.Less(settled.IndexOf("DebtCashedVictim = victimId;", StringComparison.Ordinal), bump);
            Assert.Less(settled.IndexOf("LastCashBonus =", StringComparison.Ordinal), bump);
            Assert.Less(settled.IndexOf("if (!(credited > 0f)) return;", StringComparison.Ordinal), settled.IndexOf("DebtBook.Cash(", StringComparison.Ordinal), "only a positive answer cashes");
            string events = Code(Scripts + "Gameplay/ChickenLocalEvents.cs");
            foreach (var id in new[] { "FreshHitEventId", "DebtEventId", "DebtCashEventId" })
                StringAssert.Contains(id, events, $"the one observer raises {id} (ARCH-EVT-03)");
        }

        [Test]
        public void FreshHit_StacksTheExtraDebt_ThenSpendsTheState_ThenTheHitsOwnDebt()
        {
            string hit = Body(DebtsPath, "public void OnHit(");
            int fresh = hit.IndexOf("IsFreshFor(", StringComparison.Ordinal);
            int extra = hit.IndexOf("Gain(victim, facts);", StringComparison.Ordinal);
            int mark = hit.IndexOf("RPC_MarkBitten(", StringComparison.Ordinal);
            int own = hit.IndexOf("if (job == AbilityJob.Stun || job == AbilityJob.Shove) Gain(victim, facts);", StringComparison.Ordinal);
            Assert.Greater(fresh, 0);
            Assert.Greater(extra, fresh, "the extra stack follows the fresh read");
            Assert.Greater(mark, extra, "the state is spent after the stack");
            Assert.Greater(own, mark, "a Stun or Shove adds its own debt last; a Steal's own hit adds none");
            StringAssert.Contains("!facts.IsEnforcer", hit, "every other kit returns at once");
            StringAssert.DoesNotContain("Cargo", hit, "a fresh hit moves no food (Maestro, 2026-11-25)");
        }

        [Test]
        public void ChickenPrefab_CarriesEnforcerDebts_AndListsItAmongItsNetworkedBehaviours()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var debts = prefab.GetComponent<EnforcerDebts>();
            Assert.IsNotNull(debts, "Chicken.prefab has no EnforcerDebts: no Enforcer would ever keep a debt (CODE-ASSET-11)");
            Assert.AreEqual(1, prefab.GetComponentsInChildren<EnforcerDebts>(true).Length);
            AssertListed(prefab, debts, "EnforcerDebts is on the prefab but not in NetworkObject.NetworkedBehaviours: Fusion would not replicate it");

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Doppelganger.prefab");
            var inherited = variant.GetComponent<EnforcerDebts>();
            Assert.IsNotNull(inherited, "the decoy variant inherits it (CODE-ASSET-06)");
            AssertListed(variant, inherited, "the variant's own NetworkedBehaviours list must be baked with the new component");
        }

        // ---- review fixes (BL-226) ----

        [Test]
        public void EachDebtEvent_HasItsOwnVictimField_SoTwoRivalsInOneTickCannotCrossOver()
        {
            string src = Code(DebtsPath);
            StringAssert.DoesNotContain("LastDebtVictim", src, "one shared victim field mis-attributes two events of one tick");
            foreach (var field in new[] { "FreshHitVictim", "DebtGainedVictim", "DebtCashedVictim" })
                Assert.AreEqual(1, Regex.Matches(src, @"\[Networked\] public NetworkBehaviourId " + field + @"\b").Count, $"{field} is networked, once");
            string events = Code(Scripts + "Gameplay/ChickenLocalEvents.cs");
            StringAssert.DoesNotContain("DebtVictim", events.Replace("FreshHitVictim", "").Replace("DebtGainedVictim", "").Replace("DebtCashedVictim", ""), "the observer reads the three fields, no shared one");
            StringAssert.Contains("new FreshHitEvent(s.FreshHitVictim)", events);
            StringAssert.Contains("new DebtGainedEvent(s.DebtGainedVictim)", events);
            StringAssert.Contains("new DebtCashedEvent(s.DebtCashedVictim,", events);
        }

        [Test]
        public void FreshHit_SpendsTheStateInTheAuthorityLocalBook_BeforeStacking()
        {
            string hit = Body(DebtsPath, "public void OnHit(");
            int fresh = hit.IndexOf("IsFreshFor(", StringComparison.Ordinal);
            int spend = hit.IndexOf("_spentFresh.TrySpend(victim.Id, rival.FreshStateId)", StringComparison.Ordinal);
            int gain = hit.IndexOf("Gain(victim, facts);", StringComparison.Ordinal);
            Assert.Greater(spend, fresh, "the book is consulted next to the replicated fresh test");
            Assert.Less(spend, gain, "and decides before the extra stack");
            string src = Code(DebtsPath);
            Assert.GreaterOrEqual(Regex.Matches(src, @"_spentFresh\.Clear\(\)").Count, 2, "cleared at spawn and wherever the debts are cleared");
            StringAssert.Contains("_spentFresh.Clear();", Body(DebtsPath, "private void ClearAll()"));
            StringAssert.Contains("_spentFresh.Forget(victimId)", Body(DebtsPath, "public override void FixedUpdateNetwork()"), "a rival that left is forgotten");
        }

        [Test]
        public void FreshSpendBook_SpendsAStateOnce_PerVictim()
        {
            var book = new FreshSpendBook<int>();
            Assert.IsTrue(book.TrySpend(1, 7), "first hit on the state spends it");
            Assert.IsFalse(book.TrySpend(1, 7), "a second hit before RPC_MarkBitten came back finds it spent");
            Assert.IsTrue(book.TrySpend(2, 7), "another rival's state with the same id is its own");
            Assert.IsTrue(book.TrySpend(1, 8), "a newer state is fresh again");
            Assert.IsFalse(book.TrySpend(1, 8));
            book.Forget(1);
            Assert.IsTrue(book.TrySpend(1, 8), "a forgotten rival starts clean");
            book.Clear();
            Assert.IsTrue(book.TrySpend(2, 7), "Clear drops every record");
        }

        [Test]
        public void NextFreshState_NeverLandsOnTheBittenId_EvenAcrossTheWrap()
        {
            Assert.AreEqual(6, BiteRules.NextFreshState(5, 5), "plain step");
            Assert.AreEqual(1, BiteRules.NextFreshState(0, 9));
            Assert.AreEqual(0, BiteRules.NextFreshState(255, 9), "byte wrap");
            Assert.AreEqual(2, BiteRules.NextFreshState(0, 1), "the step that would equal the bitten id skips it");
            Assert.AreEqual(1, BiteRules.NextFreshState(255, 0), "wrap onto the bitten id skips it too");
            for (int bitten = 0; bitten < 256; bitten++)
                for (int cur = 0; cur < 256; cur++)
                    Assert.AreNotEqual(bitten, BiteRules.NextFreshState((byte)cur, (byte)bitten), $"fresh {cur}, bitten {bitten}");
        }

        [Test]
        public void DebtBook_PendingExpired_OnlyAfterTheWindow()
        {
            Assert.IsFalse(DebtBook.PendingExpired(10f, 10f, 5f));
            Assert.IsFalse(DebtBook.PendingExpired(10f, 15f, 5f), "exactly the window is still open");
            Assert.IsTrue(DebtBook.PendingExpired(10f, 15.01f, 5f));
        }

        [Test]
        public void Debts_ClearTheirPendingAsks_WhenOffOrStale()
        {
            string clear = Body(DebtsPath, "private void ClearAll()");
            StringAssert.Contains("System.Array.Clear(_pending", clear, "the debts-off branch drops every open ask");
            string tick = Body(DebtsPath, "public override void FixedUpdateNetwork()");
            StringAssert.Contains("DebtBook.PendingExpired(", tick);
            StringAssert.Contains("FoodTransferRules.SettleWindowSeconds", tick, "an ask lives for the cargo's own settle window");
            StringAssert.DoesNotContain("out ushort)", File.ReadAllText(DebtsPath), "the removed StealFrom overload is not referenced, not even in a cref");
        }

        [Test]
        public void FreshState_IncrementsGoThroughTheSkipRule()
        {
            string src = Code(AbilityControllerPath);
            StringAssert.DoesNotContain("FreshStateId++", src, "a bare increment can wrap onto BittenStateId");
            Assert.AreEqual(2, Regex.Matches(src, Regex.Escape("FreshStateId = BiteRules.NextFreshState(FreshStateId, BittenStateId)")).Count, "the cast start and the landing");
        }

        [Test]
        public void TheAffectedPredictionComments_AdmitTheAcceptedRace()
        {
            string hitSteal = RuntimeSourceGuardTests.Code(HitStealPath);
            string raw = File.ReadAllText(HitStealPath) + File.ReadAllText(ControllerPath);
            StringAssert.DoesNotContain("can move no food", raw, "a wrong prediction can move food: at most one hit amount per lock");
            StringAssert.DoesNotContain("a miss moves no food", raw);
            StringAssert.Contains("ARCH-NET-05", File.ReadAllText(HitStealPath));
            Assert.IsNotEmpty(hitSteal);
        }

}
}
