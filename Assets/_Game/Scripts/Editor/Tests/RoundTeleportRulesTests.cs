using System.IO;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Regression for CW-221 "the second match starts where the first one ended" (seen by Maestro in solo, and in every
    /// round >= 2 of the 2026-10-20 sessions for the host's own chicken): which chickens the round reset sends home, and
    /// the one-shot pending target the chicken applies in its own tick.
    /// </summary>
    public sealed class RoundTeleportRulesTests
    {
        [Test]
        public void EveryHumanAndBotGoesHome_NeverADecoy()
        {
            Assert.IsTrue(RoundTeleportRules.ShouldTeleport(true, false, false, true, false), "a networked human");
            Assert.IsTrue(RoundTeleportRules.ShouldTeleport(true, false, true, false, false), "a bot (PlayerRef.None)");
            Assert.IsTrue(RoundTeleportRules.ShouldTeleport(true, false, false, false, true),
                "Single mode: the local human goes home even if its PlayerRef does not count as a real player (old filter skipped it)");
            Assert.IsFalse(RoundTeleportRules.ShouldTeleport(true, true, false, true, false), "a Doppelganger decoy stays");
            Assert.IsFalse(RoundTeleportRules.ShouldTeleport(false, false, false, true, false), "a despawned chicken");
        }

        [Test]
        public void PendingTeleport_IsAppliedExactlyOnce_LastRequestWins()
        {
            var p = new PendingTeleport();
            Assert.IsFalse(p.TryConsume(out _), "nothing queued");
            p.Request(new Vector3(21.8f, 0.05f, -21.8f));
            p.Request(new Vector3(-21.8f, 0.05f, 21.8f));
            Assert.IsTrue(p.IsPending);
            Assert.IsTrue(p.TryConsume(out var target));
            Assert.AreEqual(new Vector3(-21.8f, 0.05f, 21.8f), target, "the latest reset wins");
            Assert.IsFalse(p.TryConsume(out _), "applied once, then gone: the next tick must not snap the bird back");
            p.Request(Vector3.one);
            p.Clear();
            Assert.IsFalse(p.TryConsume(out _));
        }

        [Test]
        public void RoundReset_ClearsEveryAbilityCooldown_CW224()
        {
            // The reset path (GameManager.ResetWorldForNewRound -> ChickenController.RPC_ResetControlStates ->
            // AbilityController.EndAllForReset) needs a live runner, so pin the body: it must end the running abilities
            // AND clear every slot's cooldown, the hold charge and the peck chain. Bots go through the same RPC.
            var src = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AbilityController.cs").Replace("\r\n", "\n");
            int start = src.IndexOf("public void EndAllForReset()");
            Assert.GreaterOrEqual(start, 0, "EndAllForReset was renamed - update this test");
            int end = src.IndexOf("\n        }", start);
            var body = src.Substring(start, end - start);
            StringAssert.Contains("DeactivateAll();", body);
            StringAssert.Contains("for (int slot = 0; slot < SlotCount; slot++) SetCooldown(slot, TickTimer.None);", body);
            StringAssert.Contains("ChargingSlot = 0;", body);
            StringAssert.Contains("PeckChainActive = false", body);

            var gm = File.ReadAllText("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            StringAssert.Contains("RPC_ResetControlStates()", gm, "the reset still asks every chicken (bots included) to reset");
            var ctrl = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            StringAssert.Contains("_abilities?.EndAllForReset();", ctrl);
        }
    }
}
