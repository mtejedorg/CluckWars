using System.Collections.Generic;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>An executed chicken is out for the asset's RemovalSeconds, then respawns at its own base with no cargo and its
    /// cooldowns still running; the victim's authority bounds the removal claim at the same data value.</summary>
    public class ExecuteRespawnTests
    {
        private static MarkKillAbilitySO Asset => TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");

        private static readonly List<Vector3> Corners = new List<Vector3>
            { new Vector3(21.8f, 0, 21.8f), new Vector3(-21.8f, 0, 21.8f), new Vector3(-21.8f, 0, -21.8f), new Vector3(21.8f, 0, -21.8f) };

        [Test]
        public void OutTime_IsData_AndTheFallbackMatches()
        {
            Assert.AreEqual(3f, Asset.RemovalSeconds);
            Assert.AreEqual(Asset.RemovalSeconds, AssassinExecute.DefaultRemovalSeconds);
            Assert.AreEqual(Asset.RemovalSeconds, ExecuteRules.RemovalDurationSeconds);
        }

        [Test]
        public void TheReceiver_BoundsTheClaimAtTheDataValue_NeverFailsOpen()
        {
            float max = Asset.RemovalSeconds;
            Assert.IsTrue(ExecuteRules.IsPlausibleRemovalDuration(max, max));
            Assert.IsFalse(ExecuteRules.IsPlausibleRemovalDuration(max + 0.5f, max), "a forged longer removal");
            Assert.IsFalse(ExecuteRules.IsPlausibleRemovalDuration(1e6f, max));
            Assert.IsFalse(ExecuteRules.IsPlausibleRemovalDuration(float.NaN, max));
            Assert.IsFalse(ExecuteRules.IsPlausibleRemovalDuration(0f, max));
            Assert.IsFalse(ExecuteRules.IsPlausibleRemovalDuration(1e6f, 0f), "no execute to read: the constant, not anything goes");
            var c = new ExecuteRules.RemovalClaim { Duration = max + 1f, MaxDuration = max, AttackerResolved = true };
            Assert.AreEqual(ExecuteRules.RemovalVerdict.ImplausibleDuration, ExecuteRules.EvaluateRemoval(c));
        }

        [Test]
        public void TheWayBack_OutThenTeleportThenStandUp()
        {
            Assert.AreEqual(ExecuteRespawnStep.None, ExecuteRespawnRules.Next(outTimeOver: false, teleportQueued: false));
            Assert.AreEqual(ExecuteRespawnStep.QueueTeleport, ExecuteRespawnRules.Next(true, false));
            Assert.AreEqual(ExecuteRespawnStep.StandUp, ExecuteRespawnRules.Next(true, true), "stands up the tick after the teleport");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Home_IsItsOwnCornersSpawnPoint_ClaimedOrNot(int corner)
        {
            Assert.IsTrue(ExecuteRespawnRules.TrySpawnPoint(corner, Corners, out var p));
            Assert.AreEqual(Corners[corner], p, "the corner spawn point: never a rival's base");
        }

        [Test]
        public void NoCorner_OrNoMap_NoSpawnPoint()
        {
            Assert.IsFalse(ExecuteRespawnRules.TrySpawnPoint(-1, Corners, out _));
            Assert.IsFalse(ExecuteRespawnRules.TrySpawnPoint(0, null, out _));
        }

        [Test]
        public void TheRemoval_EndsTheOldLife_ButNotTheCooldowns_SourceGuard()
        {
            string combat = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs");
            StringAssert.Contains("_controller?.EndEverythingForRemoval();", combat);
            StringAssert.Contains("_execute?.DropMarkForRemoval();", combat);
            StringAssert.Contains("cargo.ClearForRespawn();", combat);
            StringAssert.Contains("_controller.QueueTeleport(home", combat);
            StringAssert.Contains("_respawnTeleportQueued = false;   // the round reset's own teleport", combat);

            string ctrl = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            int at = ctrl.IndexOf("public void EndEverythingForRemoval()", System.StringComparison.Ordinal);
            string body = ctrl.Substring(at, ctrl.IndexOf("private PendingTeleport _pendingTeleport;", at, System.StringComparison.Ordinal) - at);
            StringAssert.Contains("_abilities?.EndAllForRemoval();", body);
            StringAssert.Contains("StealProofTimer      = TickTimer.None;", body);
            StringAssert.DoesNotContain("EndAllForReset", body, "the round reset clears cooldowns; a removal must not");

            string ab = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int e = ab.IndexOf("public void EndAllForRemoval()", System.StringComparison.Ordinal);
            StringAssert.DoesNotContain("SetCooldown", ab.Substring(e, 300));

            // The respawn uses the round reset's teleport: NetworkTransform teleport + RecentPositions cleared.
            StringAssert.Contains("_recentPositions.Clear();   // nothing from the previous round may vouch", ctrl);
        }

        [Test]
        public void BackInToast_FollowsTheToastTimings()
        {
            Assert.IsFalse(MatchHudController.BackInToast(float.PositiveInfinity, out _, out _), "never raised");
            Assert.IsTrue(MatchHudController.BackInToast(0.5f, out float a, out _));
            Assert.AreEqual(1f, a);
            Assert.IsFalse(MatchHudController.BackInToast(2f, out _, out _), "gone after ~1.5 s");
        }
    }
}
