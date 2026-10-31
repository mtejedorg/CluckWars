using System;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>Anti-chase: the cold trail and home-breaks-the-mark rules, and the execute's numbers living in data.</summary>
    public class ColdTrailTests
    {
        private const int A = 1, B = 2;
        private const float Lockout = 15f;

        private static ColdTrail<int> After(int rival, double at = 100.0, bool decoy = false)
        {
            var trail = new ColdTrail<int>();
            trail.OnMarkEnded(rival, decoy, at, Lockout);
            return trail;
        }

        [Test]
        public void TheRival_IsRefusedDuringTheLockout_AndFreeAfterIt()
        {
            var trail = After(A);
            Assert.IsTrue(trail.Refuses(A, 114.9, 3));
            Assert.IsFalse(trail.Refuses(A, 115.0, 3));
        }

        [Test]
        public void OtherRivals_AreNeverRefused() => Assert.IsFalse(After(A).Refuses(B, 101.0, 3));

        [Test]
        public void MarkingSomeoneElse_ClearsTheTrail()
        {
            var trail = After(A);
            trail.OnMarked(B, isDecoy: false);
            Assert.IsFalse(trail.Refuses(A, 101.0, 3));
        }

        [Test]
        public void RemarkingTheSameRival_DoesNotClearTheTrail()
        {
            var trail = After(A);
            trail.OnMarked(A, isDecoy: false);   // only reachable after the lockout or in a duel; must not launder the trail
            Assert.IsTrue(trail.Refuses(A, 101.0, 3));
        }

        [Test]
        public void ADecoy_DoesNotCountAsSomeoneElse()
        {
            var trail = After(A);
            trail.OnMarked(B, isDecoy: true);
            Assert.IsTrue(trail.Refuses(A, 101.0, 3));
        }

        [Test]
        public void ADecoy_StartsNoTrail() => Assert.IsFalse(After(A, decoy: true).Refuses(A, 101.0, 3));

        [Test]
        public void ADuel_IsNeverLocked() => Assert.IsFalse(After(A).Refuses(A, 101.0, nonDecoyRivals: 1));

        [Test]
        public void ANewRound_ClearsTheTrail()
        {
            var trail = After(A);
            trail.Reset();
            Assert.IsFalse(trail.Refuses(A, 101.0, 3));
        }

        [Test]
        public void ZeroLockout_TurnsTheRuleOff()
        {
            var trail = new ColdTrail<int>();
            trail.OnMarkEnded(A, false, 100.0, 0f);
            Assert.IsFalse(trail.Refuses(A, 100.0, 3));
        }

        [Test]
        public void IsHome_IsPlanarAndInclusive()
        {
            var home = new Vector3(10f, 0f, 10f);
            Assert.IsTrue(HomeBreakRules.IsHome(new Vector3(12.5f, 9f, 10f), home, 2.5f));
            Assert.IsFalse(HomeBreakRules.IsHome(new Vector3(12.51f, 0f, 10f), home, 2.5f));
        }

        [Test]
        public void ShippedMarkKill_CarriesTheShippedNumbers()
        {
            var m = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            Assert.AreEqual(8f, m.MarkRange);
            Assert.AreEqual(2f, m.ArmSeconds);
            Assert.AreEqual(5f, m.WindowSeconds);
            Assert.AreEqual(5f, m.FailCooldownSeconds);
            Assert.AreEqual(15f, m.SuccessCooldownSeconds);
            Assert.AreEqual(8f, m.IsolationRadius);
            Assert.AreEqual(15f, m.ColdTrailSeconds);
        }

        [Test]
        public void TheAimFollowsTheAsset()
        {
            var m = ScriptableObject.CreateInstance<MarkKillAbilitySO>();
            try
            {
                m.MarkRange = 5.5f;
                Assert.AreEqual(5.5f, m.AimRadius);
                Assert.AreEqual(5.5f, m.IndicatorRange);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheReceiverBounds_FollowTheTunedNumbers()
        {
            var claim = new ExecuteRules.RemovalClaim
            {
                Duration = ExecuteRules.RemovalDurationSeconds, AttackerResolved = true, SenderSpeaksForAttacker = true,
                AttackerHasExecute = true, MarkedThisVictim = true, StunEverSeen = true, SecondsSinceStunned = 9f,
                PlanarDistance = 12f, MarkRange = 12f, MarkWindow = 10f,
            };
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(claim));

            claim.MarkWindow = 5f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.NoExecuteEvidence, ExecuteRules.EvaluateRemoval(claim));

            claim.MarkWindow = 10f;
            claim.MarkRange = 8f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.OutOfRange, ExecuteRules.EvaluateRemoval(claim));
        }

        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        [Test]
        public void TheFilter_RefusesAHomeRivalAndAColdOne_BeforeTheIsolationRule()
        {
            var src = Src("Assets/_Game/Scripts/Abilities/MarkKillAbilitySO.cs");
            int home = src.IndexOf("AssassinExecute.IsHome(candidate)", StringComparison.Ordinal);
            int cold = src.IndexOf("exec.IsColdTrail(candidate)", StringComparison.Ordinal);
            int iso = src.IndexOf("AssassinExecute.IsIsolated(candidate", StringComparison.Ordinal);
            Assert.IsTrue(home > 0 && cold > home && iso > cold);
        }

        [Test]
        public void EveryMarkEnd_LeavesATrail_ButTheRoundResetDoesNot()
        {
            var src = Src("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs");
            int press = src.IndexOf("public bool Press(", StringComparison.Ordinal);
            int fun = src.IndexOf("public override void FixedUpdateNetwork()", StringComparison.Ordinal);
            StringAssert.DoesNotContain("ResetMark();", src.Substring(press, fun - press), "Press must end marks through EndMark");

            int reset = src.IndexOf("public void ResetForNewRound()", StringComparison.Ordinal);
            string body = src.Substring(reset, src.IndexOf("\n        }", reset, StringComparison.Ordinal) - reset);
            StringAssert.Contains("_coldTrail.Reset()", body);
            StringAssert.DoesNotContain("EndMark", body);
        }

        [Test]
        public void TheProxyView_FailsOpen()
        {
            var src = Src("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs");
            StringAssert.Contains("!HasStateAuthority) return false;\n\n            int rivals", src);
        }
    }
}
