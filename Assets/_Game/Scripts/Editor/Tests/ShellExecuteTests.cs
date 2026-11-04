using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>A shelled (steal-proof) chicken cannot be executed: the Kill gate, the victim's receiver, the refund and the HUD.</summary>
    public class ShellExecuteTests
    {
        private static ExecuteRules.RemovalClaim Genuine() => new ExecuteRules.RemovalClaim
        {
            Duration = ExecuteRules.RemovalDurationSeconds, AttackerResolved = true, SenderSpeaksForAttacker = true,
            AttackerHasExecute = true, KillRange = 8f, MarkedThisVictim = true, VictimStunned = true, PlanarDistance = 3f,
            MarkEverSeen = true, MarkAgeSeconds = 3.1f, ArmSeconds = 3f,
        };

        [Test]
        public void TheRule_ShelledMeansNotExecutable()
        {
            Assert.IsFalse(ExecuteRules.CanBeExecuted(stealProof: true));
            Assert.IsTrue(ExecuteRules.CanBeExecuted(stealProof: false));
        }

        [Test]
        public void Receiver_AShellRaisedInsideTheRoundTrip_IsASilentNoOp()
        {
            var c = Genuine();
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(c));
            c.VictimShelled = true;   // Egg Shell (or Turtle) raised after the press
            var v = ExecuteRules.EvaluateRemoval(c);
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Shelled, v);
            Assert.IsTrue(ExecuteRules.IsLegalNoOp(v), "not logged or counted as a rejected RPC");
        }

        [Test]
        public void Receiver_AForgedClaimOnAShelledChicken_IsStillRejected()
        {
            var c = Genuine();
            c.VictimShelled = true;
            c.SenderSpeaksForAttacker = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotAttributable, ExecuteRules.EvaluateRemoval(c));
            c = Genuine();
            c.VictimShelled = true;
            c.MarkedThisVictim = false;
            c.VictimStunned = false;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(c),
                "the shell no-op (and its refund) is only reachable by a claim that would otherwise have landed");
        }

        [Test]
        public void Refund_RestoresTheMarkOnlyWhenItIsStillTheSameMarkWithTimeLeft()
        {
            Assert.IsTrue(ExecuteRules.ShouldRestoreShelledMark(noMarkNow: true, sameTarget: true, windowLeft: true));
            Assert.IsFalse(ExecuteRules.ShouldRestoreShelledMark(false, true, true), "a new mark replaced it");
            Assert.IsFalse(ExecuteRules.ShouldRestoreShelledMark(true, false, true), "a different victim");
            Assert.IsFalse(ExecuteRules.ShouldRestoreShelledMark(true, true, false), "its window ran out");
        }

        [Test]
        public void TheGates_AskTheRule_SourceGuard()
        {
            string exec = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs");
            // KillReady and CanKillNow both ask KillQualifies, which asks CanBeExecuted.
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(exec, @"ExecuteRules\.KillQualifies\(").Count);
            Assert.IsFalse(ExecuteRules.KillQualifies(armed: true, targetStunned: true, targetStealProof: true));
            StringAssert.Contains("_stats.TryCancelKillCredit(victimId)", exec);                              // refund needs a pending Kill
            string combat = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs");
            StringAssert.Contains("claim.VictimShelled = _controller != null && _controller.IsStealProof;", combat);
            StringAssert.Contains("RefundShelledExecute(assassinId);", combat);
            // MarkKill's button and the bots read CanKillNow, so they inherit the gate.
            StringAssert.Contains("exec.CanKillNow", File.ReadAllText("Assets/_Game/Scripts/Abilities/MarkKillAbilitySO.cs"));
            StringAssert.Contains("_execute.CanKillNow", File.ReadAllText("Assets/_Game/Scripts/Gameplay/BotController.cs"));
        }

        [Test]
        public void TurtleAndEggShell_AreShells()
        {
            StringAssert.Contains("GrantStealProof(", File.ReadAllText("Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs"));
            StringAssert.Contains("GrantStealProof(", File.ReadAllText("Assets/_Game/Scripts/Abilities/EggShellAbilitySO.cs"));
        }

        [Test]
        public void Hud_ShelledTarget_ShowsTheShellLook_NotKill_AndTheMarkKeepsDraining()
        {
            Assert.AreEqual(MarkMarkerMode.Shelled, MarkPresentationModel.MarkerModeFor(canKill: false, shelled: true, walled: true));
            Assert.AreEqual(MarkMarkerMode.Kill, MarkPresentationModel.MarkerModeFor(true, false, false));

            var m = new MarkPresentationModel();
            var input = new MarkInputs
            {
                MatchRunning = true, Role = MarkRole.Assassin, MarkKey = 7, Remaining = 6f, Window = 7f, ArmSeconds = 0.6f,
                TargetShelled = true, AmbushReadyOnTarget = true,
            };
            MarkOutputs o = default;
            for (int i = 0; i < 60; i++) o = m.Step(input, 1f / 60f);
            Assert.AreEqual(MarkMarkerMode.Shelled, o.MarkerMode);
            Assert.IsFalse(o.KillHex, "the Death Mark hex is not yellow");
            Assert.AreEqual(1f, o.MarkerAlpha, "the mark stays alive");
            input.Remaining = 3f;
            Assert.AreEqual(3f / 7f, m.Step(input, 1f / 60f).MarkerFill01, 1e-4f, "and its timer keeps draining");
        }
    }
}
