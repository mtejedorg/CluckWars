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

        /// <summary>Neither current value holds; tests set the local records explicitly.</summary>
        private static ExecuteRules.RemovalClaim NothingCurrent()
        {
            var c = Genuine();
            c.MarkedThisVictim = false;
            c.VictimStunned = false;
            c.Latency = ExecuteRules.RemoteLatency(0.1f);
            return c;
        }

        [Test]
        public void Removal_MarkClearedInThePressTick_StunnedNow_IsAccepted()
        {
            var c = NothingCurrent();
            c.VictimStunned = true;
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = 0.05f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_MarkedNow_StunRecordedRecently_IsAccepted()
        {
            // KillReady latches on the stun and outlives it (stuns 1-1.5 s, window 5 s).
            var c = NothingCurrent();
            c.MarkedThisVictim = true;
            c.StunEverSeen = true;
            c.SecondsSinceStunned = 2.5f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_MarkClearedAndStunOver_BothRecordedWithinWindow_IsAccepted()
        {
            // The CW-230 review race: the victim's peer reads marked=false, stunned=false, but its
            // own authority saw the mark one tick ago and was itself stunned 2 s ago.
            var c = NothingCurrent();
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = 0.05f;
            c.StunEverSeen = true;
            c.SecondsSinceStunned = 2f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_Forged_MarkOnly_IsRejected()
        {
            // The attacker controls its own MarkedTarget: a mark without a stun on the victim is no execute.
            var c = NothingCurrent();
            c.MarkedThisVictim = true;
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = 0f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_Forged_StunOnly_NoMarkEverSeen_IsRejected()
        {
            var c = NothingCurrent();
            c.VictimStunned = true;
            c.MarkEverSeen = false;
            c.SecondsSinceMarkSeen = 0f;   // a default age must not count as "just seen"
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_BothRecordsStale_IsRejected()
        {
            var c = NothingCurrent();
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = ExecuteRules.MaxLatencySeconds + ExecuteRules.RecentMarkGraceSeconds + 0.1f;
            c.StunEverSeen = true;
            c.SecondsSinceStunned = ExecuteRules.MaxLatencySeconds + ExecuteRules.RecentStunWindowSeconds + 0.1f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));

            c.SecondsSinceMarkSeen = 0.05f;   // fresh mark, stale stun
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_NaNOrNegativeAges_AreRejected()
        {
            var c = NothingCurrent();
            c.MarkEverSeen = true;
            c.StunEverSeen = true;
            c.SecondsSinceMarkSeen = float.NaN;
            c.SecondsSinceStunned = 1f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
            c.SecondsSinceMarkSeen = 0.05f;
            c.SecondsSinceStunned = float.NaN;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
            c.SecondsSinceStunned = -1f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_AHugeLatency_DoesNotWidenTheGrace()
        {
            var c = NothingCurrent();
            c.VictimStunned = true;
            c.MarkEverSeen = true;
            c.SecondsSinceMarkSeen = ExecuteRules.MaxLatencySeconds + ExecuteRules.RecentMarkGraceSeconds + 0.1f;
            c.Latency = 30f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c));
        }

        [TestCase(0f, ExecuteRules.MinRemoteLatencySeconds)]
        [TestCase(-1f, ExecuteRules.MinRemoteLatencySeconds)]
        [TestCase(float.NaN, ExecuteRules.MinRemoteLatencySeconds)]
        [TestCase(0.3f, 0.3f)]
        [TestCase(5f, ExecuteRules.MaxLatencySeconds)]
        [TestCase(float.PositiveInfinity, ExecuteRules.MaxLatencySeconds)]
        public void RemoteLatency_IsFlooredAndCapped(float measured, float expected) =>
            Assert.AreEqual(expected, ExecuteRules.RemoteLatency(measured), 1e-6f);

        [Test]
        public void CargoHalf_UsesTheSameEvidenceRule_AndTheVictimRecordsMarksAndStunsEachTick()
        {
            string guard = AssassinExecuteCreditTests.MethodBody(Dir + "Gameplay/ChickenCargo.cs", "private bool TryResolveExecutor(");
            StringAssert.Contains("ExecuteRules.HasExecuteEvidence(execute.MarkedTarget == _controller.Id, markSeen, sinceMark", guard,
                "the cargo transfer and the removal must accept and refuse the same executes");
            StringAssert.Contains("_controller.IsStunned, stunSeen, sinceStun, latency", guard);
            StringAssert.Contains("TrySecondsSinceMarkedBy(execute", guard);
            StringAssert.Contains("TrySecondsSinceStunned(", guard);

            string fun = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "public override void FixedUpdateNetwork(");
            int gate = fun.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            int observe = fun.IndexOf("ObserveMarksOnMe();", StringComparison.Ordinal);
            Assert.Greater(gate, -1);
            Assert.Greater(observe, gate, "the victim must record marks and its own stun on its own authority every tick");
            string observeBody = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "private void ObserveMarksOnMe(");
            StringAssert.Contains("_controller.IsStunned", observeBody);
            StringAssert.Contains("_markSeenAt[execute]", observeBody);

            string judge = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "private ExecuteRules.RemovalVerdict JudgeRemoval(");
            StringAssert.Contains("SenderLatency(info)", judge);
            StringAssert.Contains("TrySecondsSinceStunned(", judge);

            string reset = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, "public void RPC_ResetForNewMatch(");
            StringAssert.Contains("_markSeenAt.Clear()", reset);
            StringAssert.Contains("_stunEverSeen = false", reset);
        }

        [Test]
        public void Removal_FromAPeerThatDoesNotOwnTheAttacker_IsRejected()
        {
            var c = Genuine();
            c.SenderSpeaksForAttacker = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotAttributable, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void Removal_OfAnUnmarkedUnstunnedVictim_IsRejected() =>
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(NothingCurrent()));

        [Test]
        public void Removal_FromAcrossTheArena_IsRejected_EvenOnAStunnedVictim()
        {
            var c = Genuine();
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

        [Test]
        public void SoloPause_DoesNotAgeTheVictimsRecords()
        {
            // Stun ended and mark cleared at simulation 10 s; the player then pauses solo for 60 s
            // (ticks stop, the wall clock does not) and the execute lands at simulation 10.05 s.
            const double recordedSim = 10.0, pressSim = 10.05;
            const float wallClockAge = 60.05f;

            var c = NothingCurrent();
            c.MarkEverSeen = true;
            c.StunEverSeen = true;
            c.SecondsSinceMarkSeen = ExecuteRules.AgeSeconds(pressSim, recordedSim);
            c.SecondsSinceStunned = ExecuteRules.AgeSeconds(pressSim, recordedSim);
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c),
                "ages measured in simulation time keep a paused genuine execute valid");

            c.SecondsSinceMarkSeen = wallClockAge;
            c.SecondsSinceStunned = wallClockAge;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c),
                "the old wall-clock ages refused the same execute");
        }

        [Test]
        public void VictimRecords_UseSimulationTime_NeverTheWallClock()
        {
            foreach (var sig in new[]
                     {
                         "private void ObserveMarksOnMe(",
                         "internal bool TrySecondsSinceMarkedBy(",
                         "internal bool TrySecondsSinceStunned(",
                     })
            {
                string body = AssassinExecuteCreditTests.MethodBody(ChickenCombatPath, sig);
                StringAssert.DoesNotContain("Time.unscaledTime", body, sig);
                StringAssert.DoesNotContain("Time.time", body, sig);
                StringAssert.DoesNotContain("Time.realtimeSinceStartup", body, sig);
                StringAssert.Contains("Runner.SimulationTime", body, sig);
            }

            string src = string.Join("\n", CodeLines(ChickenCombatPath));
            StringAssert.Contains("Dictionary<AssassinExecute, double> _markSeenAt", src);
            StringAssert.Contains("private double _stunnedAt", src);
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
