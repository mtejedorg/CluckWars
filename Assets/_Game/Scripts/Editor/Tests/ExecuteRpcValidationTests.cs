using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-230: <c>ChickenCombat.RPC_ExecuteRemoval</c> and <c>ChickenMatchStats.RPC_CreditKill</c>
    /// are <c>RpcSources.All</c> and used to honour any caller, so any peer could remove any chicken
    /// or award kills. Both now judge the claim through <see cref="ExecuteRules"/>.
    /// </summary>
    /// <remarks>
    /// The rule tests call the same <see cref="ExecuteRules"/> entry points production calls
    /// (pinned by the structural tests below), so they exercise the real decision. Sender lookup and
    /// authority need a live <c>NetworkRunner</c>, so the wiring around them is pinned structurally,
    /// as in <see cref="AssassinExecuteCreditTests"/>.
    /// </remarks>
    public sealed class ExecuteRpcValidationTests
    {
        private const string Dir               = "Assets/_Game/Scripts/";
        private const string ChickenCombatPath = Dir + "Gameplay/ChickenCombat.cs";
        private const string MatchStatsPath    = Dir + "Gameplay/ChickenMatchStats.cs";
        private const string AssassinPath      = Dir + "Gameplay/AssassinExecute.cs";

        // ---- 1. Removal rule ------------------------------------------------------------

        /// <summary>A genuine cross-peer execute: marked, in range, the execute's duration.</summary>
        private static ExecuteRules.RemovalClaim Genuine() => new ExecuteRules.RemovalClaim
        {
            Duration = ExecuteRules.RemovalDurationSeconds,
            AttackerResolved = true,
            SenderSpeaksForAttacker = true,
            AttackerHasExecute = true,
            MarkedThisVictim = true,
            VictimStunned = true,
            PlanarDistance = 5f,
            RangeMargin = 0.5f,
        };

        [Test]
        public void Removal_GenuineExecute_IsAccepted() =>
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(Genuine()));

        [Test]
        public void Removal_MarkResetRace_StunnedVictimStillAccepted()
        {
            // Press resets the mark in the tick it sends the RPC; the replicated mark can read None
            // on the victim's peer. The victim's own stun is the evidence then.
            var c = Genuine();
            c.MarkedThisVictim = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_MarkedButStunAlreadyOver_StillAccepted()
        {
            // KillReady latches; the stun may have ended before the press.
            var c = Genuine();
            c.VictimStunned = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_MarkClearedAndStunOver_ButMarkSeenJustNow_IsAccepted()
        {
            // The CW-230 review race: KillReady outlived a 1-1.5 s stun, and Press cleared the mark
            // in the tick it sent the RPC, so the victim's peer reads marked=false, stunned=false.
            // The victim saw that mark one tick ago.
            var c = Genuine();
            c.MarkedThisVictim = false;
            c.VictimStunned = false;
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = 0.05f;
            c.Latency = ExecuteRules.RemoteLatency(0f);
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_ForgedWithNoRecentMark_IsRejected()
        {
            var c = Genuine();
            c.MarkedThisVictim = false;
            c.VictimStunned = false;
            c.Latency = ExecuteRules.RemoteLatency(0.1f);

            c.MarkEverSeen = false;
            c.SecondsSinceMarkSeen = 0f;   // a default-initialised age must not count as "just seen"
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotMarkedNorStunned, ExecuteRules.EvaluateRemoval(c), "never marked");

            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = c.Latency + ExecuteRules.RecentMarkGraceSeconds + 0.1f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotMarkedNorStunned, ExecuteRules.EvaluateRemoval(c), "mark seen too long ago");

            c.SecondsSinceMarkSeen = float.NaN;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotMarkedNorStunned, ExecuteRules.EvaluateRemoval(c));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void RemoteLatency_IsFloored_SoAllowancesNeverAssumeZeroRtt(float measured) =>
            Assert.AreEqual(ExecuteRules.MinRemoteLatencySeconds, ExecuteRules.RemoteLatency(measured), 1e-6f);

        [Test]
        public void CargoHalf_UsesTheSameEvidenceRule_AndTheVictimRecordsMarksEachTick()
        {
            string guard = AssassinExecuteCreditTests.MethodBody(Dir + "Gameplay/ChickenCargo.cs", "private bool TryResolveExecutor(");
            StringAssert.Contains("ExecuteRules.HasExecuteEvidence(execute.MarkedTarget == _controller.Id, _controller.IsStunned", guard,
                "the cargo transfer and the removal must accept and refuse the same executes");
            StringAssert.Contains("TrySecondsSinceMarkedBy(execute", guard);

            string fun = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "public override void FixedUpdateNetwork(");
            int gate = fun.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            int observe = fun.IndexOf("ObserveMarksOnMe();", StringComparison.Ordinal);
            Assert.Greater(gate, -1);
            Assert.Greater(observe, gate, "the victim must record marks on its own authority every tick");
            StringAssert.Contains("SenderLatency(info)",
                AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "private ExecuteRules.RemovalVerdict JudgeRemoval("));
        }

        [Test]
        public void Removal_FromAPeerThatDoesNotOwnTheAttacker_IsRejected()
        {
            var c = Genuine();
            c.SenderSpeaksForAttacker = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotAttributable, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_OfAnUnmarkedUnstunnedVictim_IsRejected()
        {
            var c = Genuine();
            c.MarkedThisVictim = false;
            c.VictimStunned = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotMarkedNorStunned, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_FromAcrossTheArena_IsRejected_EvenOnAStunnedVictim()
        {
            var c = Genuine();
            c.MarkedThisVictim = false;
            c.PlanarDistance = 30f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.OutOfRange, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_AtTheMarkRangeEdgePlusDrift_IsAccepted()
        {
            var c = Genuine();
            c.PlanarDistance = AssassinExecute.MaxMarkRange + ExecuteRules.RangeSlackMeters + c.RangeMargin - 0.01f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
            c.PlanarDistance = float.NaN;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.OutOfRange, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_SelfDecoyUnresolvedOrNonAssassin_IsRejected()
        {
            var c = Genuine(); c.AttackerResolved = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.AttackerUnresolved, ExecuteRules.EvaluateRemoval(c));
            c = Genuine(); c.AttackerIsVictim = true;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.SelfExecute, ExecuteRules.EvaluateRemoval(c));
            c = Genuine(); c.AttackerIsDecoy = true;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.DecoyAttacker, ExecuteRules.EvaluateRemoval(c));
            c = Genuine(); c.AttackerHasExecute = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoAssassinExecute, ExecuteRules.EvaluateRemoval(c));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(2.5f)]
        [TestCase(999f)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NaN)]
        public void Removal_WithANonExecuteDuration_IsRejected(float duration)
        {
            var c = Genuine();
            c.Duration = duration;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.ImplausibleDuration, ExecuteRules.EvaluateRemoval(c));
        }

        // ---- 2. Kill-credit rule --------------------------------------------------------

        private const int Victim = 7;
        private const int Bystander = 9;

        private static ExecuteRules.KillCreditClaim FromVictimAuthority() =>
            new ExecuteRules.KillCreditClaim { VictimResolved = true, SenderSpeaksForVictim = true };

        [Test]
        public void KillCredit_ForARecordedExecute_PaysOnce()
        {
            var ledger = ExecuteRules.NewKillCreditLedger<int>();
            ExecuteRules.ExpectKillCredit(ledger, Victim, 0f);

            Assert.AreEqual(ExecuteRules.KillCreditVerdict.Accepted,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Victim, 0.4f));
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.NoPendingExecute,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Victim, 0.5f),
                "a replayed report scores nothing");
        }

        [Test]
        public void KillCredit_Unsolicited_OrForAnotherVictim_IsRejected()
        {
            var ledger = ExecuteRules.NewKillCreditLedger<int>();
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.NoPendingExecute,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Victim, 0f),
                "no execute was pressed: the old RPC_CreditKill() counted this");

            ExecuteRules.ExpectKillCredit(ledger, Victim, 0f);
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.NoPendingExecute,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Bystander, 0.1f));
        }

        [Test]
        public void KillCredit_FromAPeerThatDoesNotOwnTheVictim_IsRejected_WithoutBurningTheRealOne()
        {
            var ledger = ExecuteRules.NewKillCreditLedger<int>();
            ExecuteRules.ExpectKillCredit(ledger, Victim, 0f);

            var forged = FromVictimAuthority();
            forged.SenderSpeaksForVictim = false;
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.NotAttributable,
                ExecuteRules.EvaluateKillCredit(ledger, forged, Victim, 0.1f));
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.Accepted,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Victim, 0.2f));
        }

        [Test]
        public void KillCredit_SelfDecoyOrUnresolvedVictim_IsRejected()
        {
            var ledger = ExecuteRules.NewKillCreditLedger<int>();
            ExecuteRules.ExpectKillCredit(ledger, Victim, 0f);

            var c = FromVictimAuthority(); c.VictimResolved = false;
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.VictimUnresolved, ExecuteRules.EvaluateKillCredit(ledger, c, Victim, 0.1f));
            c = FromVictimAuthority(); c.VictimIsSelf = true;
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.SelfKill, ExecuteRules.EvaluateKillCredit(ledger, c, Victim, 0.1f));
            c = FromVictimAuthority(); c.VictimIsDecoy = true;
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.DecoyVictim, ExecuteRules.EvaluateKillCredit(ledger, c, Victim, 0.1f));
        }

        [Test]
        public void KillCredit_Window_MatchesTheExecuteLootWindow_AndExpires()
        {
            var ledger = ExecuteRules.NewKillCreditLedger<int>();
            Assert.AreEqual(StealRules.ExecuteLootWindowSeconds, ledger.Window, 1e-5f,
                "the kill report makes the same two relayed trips as the loot report");

            ExecuteRules.ExpectKillCredit(ledger, Victim, 0f);
            Assert.AreEqual(ExecuteRules.KillCreditVerdict.NoPendingExecute,
                ExecuteRules.EvaluateKillCredit(ledger, FromVictimAuthority(), Victim, ledger.Window + 0.5f));
        }

        // ---- 3. Production wiring ------------------------------------------------------

        [Test]
        public void ExecuteRemovalRpc_TakesRpcInfo_AndJudgesBeforeRemoving()
        {
            var method = typeof(ChickenCombat).GetMethod(nameof(ChickenCombat.RPC_ExecuteRemoval));
            Assert.IsNotNull(method);
            Assert.IsTrue(method.GetParameters().Any(p => p.ParameterType == typeof(Fusion.RpcInfo)),
                "RPC_ExecuteRemoval is RpcSources.All; without RpcInfo it cannot tell who sent it (CW-230).");
            Assert.IsTrue(method.GetCustomAttributes(typeof(Fusion.RpcAttribute), false).Any());

            string body = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "public void RPC_ExecuteRemoval(");
            int judge  = body.IndexOf("JudgeRemoval(", StringComparison.Ordinal);
            int reject = body.IndexOf("return;", StringComparison.Ordinal);
            int remove = body.IndexOf("ExecuteRemoval(assassinId, duration)", StringComparison.Ordinal);
            Assert.Greater(judge, 0, "the removal claim is no longer judged");
            Assert.Greater(reject, judge, "a rejected claim must return before removing");
            Assert.Greater(remove, reject);

            string judgeBody = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "private ExecuteRules.RemovalVerdict JudgeRemoval(");
            StringAssert.Contains("attacker.Object.StateAuthority == info.Source", judgeBody, "off the wire: the attacker's state authority");
            StringAssert.Contains("attacker.Object.HasStateAuthority", judgeBody, "local invoke (solo, bots, master): simulated here");
            StringAssert.Contains("execute.MarkedTarget == _controller.Id", judgeBody);
            StringAssert.Contains("_controller.IsStunned", judgeBody);
            StringAssert.Contains("ExecuteRules.EvaluateRemoval(claim)", judgeBody);
        }

        [Test]
        public void CreditKillRpc_NamesTheVictim_TakesRpcInfo_AndJudgesBeforeCounting()
        {
            var method = typeof(ChickenMatchStats).GetMethod(nameof(ChickenMatchStats.RPC_CreditKill));
            Assert.IsNotNull(method);
            var ps = method.GetParameters();
            Assert.IsTrue(ps.Any(p => p.ParameterType == typeof(Fusion.NetworkBehaviourId)),
                "RPC_CreditKill must name the victim so the report can be matched to an execute (CW-230).");
            Assert.IsTrue(ps.Any(p => p.ParameterType == typeof(Fusion.RpcInfo)),
                "RPC_CreditKill is RpcSources.All; without RpcInfo it cannot tell who is reporting.");

            string body = AssassinExecuteCreditTests.MethodBody(MatchStatsPath, "public void RPC_CreditKill(");
            int sender = body.IndexOf("victim.Object.StateAuthority == info.Source", StringComparison.Ordinal);
            int local  = body.IndexOf("victim.Object.HasStateAuthority", StringComparison.Ordinal);
            int judge  = body.IndexOf("ExecuteRules.EvaluateKillCredit(_pendingKills", StringComparison.Ordinal);
            int count  = body.IndexOf("Kills++", StringComparison.Ordinal);
            Assert.Greater(sender, 0);
            Assert.Greater(local, 0);
            Assert.Greater(judge, Math.Max(sender, local));
            Assert.Greater(count, judge, "a kill is counted only after the report is judged");
            Assert.AreEqual(1, Regex.Matches(string.Join("\n", CodeLines(MatchStatsPath)), @"Kills\s*\+\+").Count,
                "a second Kills++ would be an unvalidated path to a kill");
        }

        [Test]
        public void Press_RecordsTheKillExpectation_BeforeTheRemoval_WithTheExecuteDuration()
        {
            string press = AssassinExecuteCreditTests.MethodBody(AssassinPath, "public bool Press(");
            int expect  = press.IndexOf("_stats.ExpectKillCredit(target)", StringComparison.Ordinal);
            int removal = press.IndexOf("RPC_ExecuteRemoval(", StringComparison.Ordinal);
            Assert.Greater(expect, 0, "Press must record the kill it expects, or RPC_CreditKill refuses it");
            Assert.Greater(removal, expect, "record first: on a shared authority the kill report runs inside the RPC call");
            string beforeRemoval = press.Substring(expect, removal - expect);
            StringAssert.DoesNotContain("ResetMark()", beforeRemoval,
                "the mark must still name the victim when the removal is sent (the local receiver checks it)");
            StringAssert.Contains("RPC_ExecuteRemoval(_combat.Id, ExecuteRules.RemovalDurationSeconds)", press,
                "a different duration is refused by IsPlausibleRemovalDuration");
            Assert.IsTrue(ExecuteRules.IsPlausibleRemovalDuration(ExecuteRules.RemovalDurationSeconds));
        }

        [Test]
        public void KillReport_NamesTheVictimsControllerId_WhichIsWhatPressRecords()
        {
            // Press records target.Id (a ChickenController's); the report must use the same key.
            string credit = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "private void CreditKillToAttacker(");
            StringAssert.Contains(".RPC_CreditKill(_controller.Id)", credit);
            StringAssert.Contains("ExecuteRules.ExpectKillCredit(_pendingKills, victim.Id",
                AssassinExecuteCreditTests.MethodBody(MatchStatsPath, "public void ExpectKillCredit("));
        }

        [Test]
        public void TheExecuteRpcs_HaveOnlyTheirKnownCallers()
        {
            // A new caller must be checked against ExecuteRules, or it will be refused at runtime.
            AssertSoleCaller(".RPC_ExecuteRemoval(", "AssassinExecute.cs");
            AssertSoleCaller(".RPC_CreditKill(", "ChickenCombat.cs");
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static void AssertSoleCaller(string call, string expectedFile)
        {
            var callers = Directory.GetFiles(Dir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Editor/"))
                .Where(p => CodeLines(p).Any(l => l.Contains(call)))
                .Select(Path.GetFileName)
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { expectedFile }, callers,
                $"Unexpected callers of {call}: {string.Join(", ", callers)}");
        }

        private static string[] CodeLines(string path) =>
            File.ReadAllLines(path)
                .Select(l =>
                {
                    int slash = l.IndexOf("//", StringComparison.Ordinal);
                    return slash >= 0 ? l.Substring(0, slash) : l;
                })
                .ToArray();
    }
}
