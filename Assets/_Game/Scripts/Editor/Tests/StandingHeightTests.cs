using System;
using System.IO;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-021 / CW-006 (the chicken must stand up before the countdown). Root cause: chickens were spawned and round-reset
    /// at Y 0.05 with the pivot at the capsule centre, so the capsule (and model) sat half under the grass until the first
    /// <c>CharacterController.Move</c> at GO lifted it. Fixed at the source: the spawn / reset Y is the true standing height.
    /// A per-tick settle <c>Move</c> was tried and rejected: it overwrote the networked round-reset teleports (CW-221).
    /// </summary>
    public class StandingHeightTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const float OldSpawnY = 0.05f;   // the spawn / reset Y before the fix (MatchBootstrapper, GameManager reset)

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static float CapsuleBottom(float pivotY, float centerY, float height, float scaleY) =>
            pivotY + (centerY - height * 0.5f) * scaleY;

        [TestCase(1f)]
        [TestCase(1.3f)]
        [TestCase(0.8f)]
        public void StandingPivotY_PutsTheCapsuleBottomOneSkinAboveTheGrass(float scale)
        {
            const float center = 0f, height = 1.6f, skin = 0.08f;
            float y = ChickenGround.StandingPivotY(center, height, skin, scale);
            Assert.That(CapsuleBottom(y, center, height, scale), Is.EqualTo(skin).Within(1e-4f));
        }

        [Test]
        public void OldSpawnHeight_BuriesTheCapsule_StandingHeightDoesNot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Chicken.prefab");
            Assert.That(prefab, Is.Not.Null);
            var cc = prefab.GetComponent<CharacterController>();
            Assert.That(cc, Is.Not.Null);

            float oldBottom = CapsuleBottom(OldSpawnY, cc.center.y, cc.height, 1f);
            Assert.That(oldBottom, Is.LessThan(-0.5f), "the old spawn Y left most of the capsule under the grass");

            float y = ChickenGround.StandingPivotY(cc.center.y, cc.height, cc.skinWidth, 1f);
            Assert.That(CapsuleBottom(y, cc.center.y, cc.height, 1f), Is.GreaterThanOrEqualTo(0f), "standing: not under the grass");
            Assert.That(y, Is.GreaterThan(0.5f));
        }

        [Test]
        public void RoundResetTeleport_AndSpawn_UseTheStandingHeight_AndNoPerTickSettleExists()
        {
            string src = Read(ControllerPath);
            int apply = src.IndexOf("private void ApplyPendingTeleport", StringComparison.Ordinal);
            int fixedUpdate = src.IndexOf("public override void FixedUpdateNetwork", StringComparison.Ordinal);
            Assert.That(apply, Is.GreaterThan(0));
            StringAssert.Contains("target.y = StandingPivotY();", src.Substring(apply, 600));
            StringAssert.Contains("SeatOnGroundAtSpawn();", src);

            // ApplyPendingTeleport still runs first in FixedUpdateNetwork (CW-221), and nothing moves the controller
            // before the IsMatchRunning gate.
            string fun = src.Substring(fixedUpdate, src.IndexOf("if (gm == null || !gm.IsMatchRunning) return;", fixedUpdate, StringComparison.Ordinal) - fixedUpdate);
            Assert.That(fun.IndexOf("ApplyPendingTeleport();", StringComparison.Ordinal), Is.LessThan(fun.IndexOf("SyncAbilityEffects();", StringComparison.Ordinal)));
            StringAssert.DoesNotContain(".Move(", fun, "a CharacterController.Move before GO undoes the networked teleport");
            StringAssert.DoesNotContain("SettleOntoGround", src);
            StringAssert.DoesNotContain("SettleOntoGround", Read("Assets/_Game/Scripts/Gameplay/ChickenMovement.cs"));
        }

        [Test]
        public void AbilitiesAndInput_StayLockedUntilGo()
        {
            StringAssert.Contains("!gm.IsMatchRunning", Read("Assets/_Game/Scripts/Gameplay/AbilityController.cs"));
            StringAssert.Contains("!gm.IsMatchRunning", Read("Assets/_Game/Scripts/Gameplay/BotController.cs"));
        }
    }
}
