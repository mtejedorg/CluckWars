using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// The balance lines of the MATCH SUMMARY block (F7 / Q8, 2026-10-22). Log greps after a
    /// harness or human session key on these shapes ("Winner Deposited: 0.0 (Assassin" is the
    /// Spoiler-wins-from-zero case the proposal could only count by hand), so they are pinned.
    /// Before this the block had no winner-deposited, execute or stun line at all.
    /// </summary>
    public sealed class MatchSummaryLinesTests
    {
        [Test]
        public void WinnerDeposited_ShowsAZeroDepositWinnerExplicitly()
        {
            Assert.AreEqual("Winner Deposited: 0.0 by P3 (Assassin)",
                MatchSummaryLines.WinnerDeposited(2, true, ChickenClass.Assassin, 0f));
            Assert.AreEqual("Winner Deposited: 24.0 by P1 (Fatty)",
                MatchSummaryLines.WinnerDeposited(0, true, ChickenClass.Fatty, 24f));
        }

        /// <summary>qa follow-up 11: the winner line and the player rows must name the same player the same way.</summary>
        [Test]
        public void WinnerLine_AndPlayerRow_UseTheSameOneBasedLabel()
        {
            string winner = MatchSummaryLines.WinnerDeposited(2, true, ChickenClass.Assassin, 0f);
            string row = MatchSummaryLines.PlayerRow(2, ChickenClass.Assassin, 0, 0f, 0);
            StringAssert.Contains("P3 (Assassin)", winner);
            StringAssert.StartsWith("  P3 (Assassin):", row);
            StringAssert.Contains("no stats for P4", MatchSummaryLines.WinnerDeposited(3, false, ChickenClass.Warrior, 0f));
        }

        [Test]
        public void WinnerDeposited_SaysNotApplicable_RatherThanPrintingAFakeZero()
        {
            // A missing winner must never read as "0.0 deposited": that is the exact signal the line exists for.
            StringAssert.StartsWith("Winner Deposited: n/a", MatchSummaryLines.WinnerDeposited(-1, false, ChickenClass.Warrior, 0f));
            StringAssert.StartsWith("Winner Deposited: n/a", MatchSummaryLines.WinnerDeposited(3, false, ChickenClass.Warrior, 0f));
        }

        [Test]
        public void Counters_HaveStableShapes()
        {
            Assert.AreEqual("Executes Landed: 3", MatchSummaryLines.ExecutesLanded(3));
            Assert.AreEqual("Stuns Landed (all sources, real players): 0", MatchSummaryLines.StunsLanded(0));
            StringAssert.StartsWith("Stuns Landed", MatchSummaryLines.StunsLanded(7), "grep prefix must stay stable");
        }

        [Test]
        public void PlayerRow_KeepsThePreviousPrefix_AndAppendsStunned()
        {
            Assert.AreEqual("  P3 (Assassin): Kills=1, Deposited=0.0, Stunned=2",
                MatchSummaryLines.PlayerRow(2, ChickenClass.Assassin, 1, 0f, 2));
        }

        [Test]
        public void Lines_UseTheInvariantCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("Winner Deposited: 12.5 by P2 (Speedy)",
                    MatchSummaryLines.WinnerDeposited(1, true, ChickenClass.Speedy, 12.5f));
                StringAssert.Contains("Deposited=12.5,", MatchSummaryLines.PlayerRow(1, ChickenClass.Speedy, 0, 12.5f, 0));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        /// <summary>The summary must actually print the three lines, not just have helpers for them.</summary>
        [Test]
        public void GameManagerEndMatch_PrintsTheBalanceCounters()
        {
            string body = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/GameManager.cs",
                "private void EndMatch(");
            int header = body.IndexOf("=== MATCH SUMMARY ===", StringComparison.Ordinal);
            Assert.Greater(header, 0, "EndMatch no longer prints the MATCH SUMMARY block");
            StringAssert.Contains("MatchSummaryLines.WinnerDeposited(", body);
            StringAssert.Contains("MatchSummaryLines.ExecutesLanded(", body);
            StringAssert.Contains("MatchSummaryLines.StunsLanded(", body);
            StringAssert.Contains("MatchSummaryLines.PlayerRow(", body);
        }

        /// <summary>The stun counter is fed from the one place a stun is applied.</summary>
        [Test]
        public void RpcApplyStun_CountsTheStun_AfterWritingTheTimer()
        {
            string body = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenController.cs",
                "public void RPC_ApplyStun(");
            int timer = body.IndexOf("StunTimer = TickTimer.CreateFromSeconds", StringComparison.Ordinal);
            int count = body.IndexOf("RecordStunTaken()", StringComparison.Ordinal);
            Assert.Greater(timer, 0);
            Assert.Greater(count, timer, "a stun swallowed by a longer running one returns before this, so it must come after the timer write");
            int decoy = body.IndexOf("if (!IsDecoy)", timer, StringComparison.Ordinal);
            Assert.That(decoy > timer && decoy < count, "decoy stuns must be skipped before counting (real players only)");
        }
    }
}
