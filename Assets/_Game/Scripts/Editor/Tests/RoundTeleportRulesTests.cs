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

        [Test]
        public void RoundReset_ReachesEveryComponentThatArmsACooldownOfItsOwn_CW231()
        {
            // CW-231: AssassinExecute keeps a [Networked] mark and re-arms Death Mark's cooldown from its OWN tick when the
            // mark fizzles. A mark alive at PLAY AGAIN fizzled on the next tick (the reset teleport puts the target out of
            // range), after EndAllForReset had cleared the slots: P3 opened rounds 3 and 4 of session 20261021-150220
            // with 3.8 s of cooldown. Every script outside AbilityController that calls TriggerCooldown therefore needs a
            // ResetForNewRound() that RPC_ResetControlStates calls BEFORE EndAllForReset (the reset needs a live runner,
            // so the wiring is pinned from source, like the CW-224 test above).
            var ctrl = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs").Replace("\r\n", "\n");
            int start = ctrl.IndexOf("public void RPC_ResetControlStates()");
            Assert.GreaterOrEqual(start, 0, "RPC_ResetControlStates was renamed - update this test");
            var body = ctrl.Substring(start, ctrl.IndexOf("\n        }", start) - start);
            int endAll = body.IndexOf("_abilities?.EndAllForReset();");
            Assert.GreaterOrEqual(endAll, 0);

            int armers = 0;
            foreach (var path in Directory.GetFiles("Assets/_Game/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                var p = path.Replace('\\', '/');
                if (p.Contains("/Editor/") || p.Contains("/AbilityLab/") || p.EndsWith("/AbilityController.cs")) continue;
                var src = File.ReadAllText(path);
                if (!src.Contains(".TriggerCooldown(")) continue;
                armers++;
                string cls = Path.GetFileNameWithoutExtension(path);
                StringAssert.Contains("public void ResetForNewRound()", src,
                    $"{cls} arms a cooldown from its own state; it needs a ResetForNewRound() that drops that state without arming one");
                var field = System.Text.RegularExpressions.Regex.Match(ctrl, @"(_\w+) = GetComponent<" + cls + @">\(\);");
                Assert.IsTrue(field.Success, $"ChickenController must cache its {cls} to reset it");
                int call = body.IndexOf(field.Groups[1].Value + "?.ResetForNewRound();");
                Assert.GreaterOrEqual(call, 0, $"RPC_ResetControlStates must call {field.Groups[1].Value}?.ResetForNewRound()");
                Assert.Less(call, endAll, "reset it BEFORE EndAllForReset clears the cooldowns");
            }
            Assert.GreaterOrEqual(armers, 1, "AssassinExecute arms Death Mark's cooldown itself; the scan found nothing - update it");

            var assassin = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs").Replace("\r\n", "\n");
            int r = assassin.IndexOf("public void ResetForNewRound()");
            var reset = assassin.Substring(r, assassin.IndexOf("\n        }", r) - r);
            StringAssert.Contains("ResetMark();", reset);
            StringAssert.DoesNotContain("FizzleMark", reset, "the reset must not charge the fail cooldown");
            StringAssert.DoesNotContain("TriggerCooldown", reset);
        }
    }
}
