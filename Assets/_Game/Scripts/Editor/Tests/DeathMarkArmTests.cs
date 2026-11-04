using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-04: "Stun must be active when kill lands." The Kill needs the 3 s arm over and the target stunned at
    /// that moment (a stun that began before the arm and is still running counts), on the Assassin's side (<see
    /// cref="ExecuteRules.KillQualifies"/>) and, for the arm, again on the victim's own clock (<see cref="ExecuteRules.HasArmEvidence"/>).
    /// Times below are seconds since the mark; the shipped arm is 3.0 in a 4.0 window.
    /// </summary>
    public class DeathMarkArmTests
    {
        private const float Arm = 3f;

        /// <summary>The Assassin's side at time <paramref name="t"/>: armed once t reaches the arm, stunned while t is inside [from, to).</summary>
        private static bool AssassinSide(float t, float stunFrom, float stunTo, bool shelled = false) =>
            ExecuteRules.KillQualifies(armed: t >= Arm, targetStunned: t >= stunFrom && t < stunTo, targetStealProof: shelled);

        /// <summary>A genuine removal claim whose victim has watched the mark for <paramref name="markAge"/> s.</summary>
        private static ExecuteRules.RemovalClaim Claim(float markAge, bool stunnedNow = true) => new ExecuteRules.RemovalClaim
        {
            Duration = ExecuteRules.RemovalDurationSeconds, AttackerResolved = true, SenderSpeaksForAttacker = true,
            AttackerHasExecute = true, KillRange = 8f, MarkedThisVictim = true, VictimStunned = stunnedNow, PlanarDistance = 3f,
            MarkEverSeen = true, MarkAgeSeconds = markAge, ArmSeconds = Arm,
        };

        // ---- Assassin side ------------------------------------------------------------------------------------------

        [Test]
        public void KillBeforeTheArm_IsRefused_EvenOnAStunnedTarget() =>
            Assert.IsFalse(AssassinSide(t: 2.9f, stunFrom: 2.0f, stunTo: 3.5f));

        [Test]
        public void AStunThatBeganBeforeTheArm_AndStillRuns_Qualifies()
        {
            // Stun from 2.5 s (1 s Ambush), press at 3.1 s: the stun began before the arm, it is live at the press.
            Assert.IsTrue(AssassinSide(t: 3.1f, stunFrom: 2.5f, stunTo: 3.5f));
            // No "stun must begin after the arm" rule (Maestro ruling): the same is true one tick after the arm.
            Assert.IsTrue(AssassinSide(t: Arm, stunFrom: 2.1f, stunTo: 3.1f));
        }

        [Test]
        public void AStunThatEndedAtTheArm_PressAfterIt_IsRefused() =>
            Assert.IsFalse(AssassinSide(t: 3.2f, stunFrom: 2.0f, stunTo: 3.0f));

        [Test]
        public void AShelledTarget_StunnedAfterTheArm_IsRefused() =>
            Assert.IsFalse(AssassinSide(t: 3.2f, stunFrom: 3.0f, stunTo: 4.0f, shelled: true));

        [Test]
        public void CanKillNow_AsksTheRuleFresh_NotLastTicksKillReady_SourceGuard()
        {
            string can = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs", "public bool CanKillNow");
            StringAssert.DoesNotContain("KillReady", can, "KillReady is last tick's answer: one tick stale against the stun");
            StringAssert.Contains("ExecuteRules.KillQualifies(ArmTimer.Expired(Runner), target.CurrentControlState == ControlState.Stunned", can);
            // The press re-checks CanKillNow at press time.
            string press = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs", "public bool Press()");
            StringAssert.Contains("if (!CanKillNow)", press);
        }

        // ---- Victim side: the arm on the victim's own clock --------------------------------------------------------------

        [Test]
        public void Receiver_AKillBeforeTheArm_IsRefused()
        {
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotArmed, ExecuteRules.EvaluateRemoval(Claim(markAge: 1.0f)),
                "a modified client that skipped its own ArmTimer");
            Assert.IsFalse(ExecuteRules.IsLegalNoOp(ExecuteRules.RemovalVerdict.NotArmed), "logged as a rejection");
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NotArmed,
                ExecuteRules.EvaluateRemoval(Claim(markAge: Arm - ExecuteRules.ArmAllowanceSeconds - 0.01f)));
        }

        [Test]
        public void Receiver_AKillAtTheArm_IsAccepted_WithinOneLatencyCapOfEarly()
        {
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(Claim(markAge: 3.1f)));
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted,
                ExecuteRules.EvaluateRemoval(Claim(markAge: Arm - ExecuteRules.ArmAllowanceSeconds)),
                "the victim first saw the mark a replication delay late: the honest press reads up to one latency cap young");
            Assert.AreEqual(ExecuteRules.MaxLatencySeconds, ExecuteRules.ArmAllowanceSeconds);
        }

        [Test]
        public void Receiver_StunBeganBeforeTheArm_StillRunningAtTheKill_IsAccepted() =>
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(Claim(markAge: 3.1f, stunnedNow: true)));

        [Test]
        public void Receiver_AShelledStunnedTarget_IsRefused_AsTheShellNoOp()
        {
            var c = Claim(markAge: 3.2f);
            c.VictimShelled = true;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Shelled, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void ArmEvidence_NeedsARecord_AndAFiniteAge()
        {
            Assert.IsFalse(ExecuteRules.HasArmEvidence(markEverSeen: false, markAgeSeconds: 10f, armSeconds: Arm), "never saw the mark");
            Assert.IsFalse(ExecuteRules.HasArmEvidence(true, float.NaN, Arm));
            Assert.IsFalse(ExecuteRules.HasArmEvidence(true, -1f, Arm));
            Assert.IsTrue(ExecuteRules.HasArmEvidence(true, 2.6f, Arm));
            // A missing arm number reads as the shipped fallback, never as "no arm".
            Assert.IsFalse(ExecuteRules.HasArmEvidence(true, 1.0f, 0f));
            Assert.IsTrue(ExecuteRules.HasArmEvidence(true, AssassinExecute.ArmDuration, 0f));
            // An arm under the allowance needs no record (nothing to time).
            Assert.IsTrue(ExecuteRules.HasArmEvidence(false, 0f, ExecuteRules.ArmAllowanceSeconds));
        }

        [Test]
        public void MarkEpisodes_ContinueAcrossShortGaps_AndRestartAfterLongOnes()
        {
            Assert.IsTrue(ExecuteRules.ContinuesMarkEpisode(10.0, 9.98), "the next tick");
            Assert.IsTrue(ExecuteRules.ContinuesMarkEpisode(10.0, 10.0 - ExecuteRules.MarkEpisodeGapSeconds), "a shell refund one round trip later");
            Assert.IsFalse(ExecuteRules.ContinuesMarkEpisode(10.0, 10.0 - ExecuteRules.MarkEpisodeGapSeconds - 0.01), "a new mark");
            Assert.IsFalse(ExecuteRules.ContinuesMarkEpisode(10.0, 11.0), "a record from the future (round reset) starts over");
            Assert.IsFalse(ExecuteRules.ContinuesMarkEpisode(double.NaN, 10.0));
        }

        [Test]
        public void BothHalvesOfTheExecute_AskTheArm_SourceGuard()
        {
            string cargo = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs", "private bool TryResolveExecutor(");
            StringAssert.Contains("ExecuteRules.HasArmEvidence(markSeen, markAge, execute.ArmSeconds)", cargo);
            string judge = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs", "private ExecuteRules.RemovalVerdict JudgeRemoval(");
            StringAssert.Contains("claim.ArmSeconds = execute.ArmSeconds;", judge, "every peer judges by the registry asset");
            StringAssert.Contains("out claim.MarkAgeSeconds", judge);
            string observe = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs", "private void ObserveMarksOnMe(");
            StringAssert.Contains("ExecuteRules.ContinuesMarkEpisode(now, seen.Last)", observe);
        }

        // ---- D5: shells and control immunity (recorded, unchanged) -----------------------------------------------------

        [Test]
        public void D5_ATurtleWalkingChicken_TakesTheStun_ButStillCannotBeExecuted()
        {
            // Turtle Mode's walk is steal-proof and NOT control-immune (TurtleModeAbilitySO.OnActivate), so Ambush's stun lands,
            // and the steal-proof flag alone refuses the Kill: stunned, armed, in range, still not executable.
            string turtle = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs", "public override void OnActivate(");
            StringAssert.Contains("ctx.Controller.GrantStealProof(Duration);", turtle);
            StringAssert.DoesNotContain("GrantControlImmunity", turtle);
            Assert.IsFalse(ExecuteRules.KillQualifies(armed: true, targetStunned: true, targetStealProof: true));
            var c = Claim(markAge: 3.3f);
            c.VictimShelled = true;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Shelled, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void D5_TurtleLockAndImmovable_AreControlImmune_SoNoStunLands()
        {
            string lockPress = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs", "public override float OnPressWhileActive(");
            StringAssert.Contains("c.GrantControlImmunity(LockSeconds);", lockPress);
            StringAssert.Contains("c.GrantStealProof(LockSeconds);", lockPress);
            string immovable = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Abilities/ImmovableAbilitySO.cs", "public override void OnActivate(");
            StringAssert.Contains("ctx.Controller.GrantControlImmunity(Duration);", immovable);

            // Every incoming stun goes through the passive chokepoint, which returns 0 while control-immune, and a 0 s stun
            // never extends a running one, so nothing is applied.
            const string Controller = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
            string choke = AssassinExecuteCreditTests.MethodBody(Controller, "private float ApplyPassiveControlDuration(");
            StringAssert.Contains("if (IsControlImmune) return 0f;", choke);
            string stun = AssassinExecuteCreditTests.MethodBody(Controller, "public void RPC_ApplyStun(");
            int scaled = stun.IndexOf("seconds = ApplyPassiveControlDuration(seconds);", System.StringComparison.Ordinal);
            int applied = stun.IndexOf("IsStunned = true;", System.StringComparison.Ordinal);
            Assert.Greater(scaled, -1);
            Assert.Greater(applied, scaled, "the immunity is applied before the stun");
            Assert.IsFalse(ControlReapplyRules.IncomingExtends(0f, 0f), "a 0 s stun applies nothing");
        }
    }
}
