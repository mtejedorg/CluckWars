using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>Turtle Mode absorbs Egg Shell: walk (steal-proof, stunnable) then lock (immune to control); a second press
    /// through the running ability, the cooldown from the first press, unkillable in both phases; Egg Shell retired.</summary>
    public class TurtleModeMergeTests
    {
        private static TurtleModeAbilitySO Turtle => TestAssets.Load<TurtleModeAbilitySO>(TestAssets.AbilitiesDir + "/TurtleMode.asset");
        private static string Src(string f) => File.ReadAllText(f);

        [Test]
        public void TheNumbers_AreData_FattyOnly()
        {
            var t = Turtle;
            Assert.AreEqual(3f, t.Duration, "walk");
            Assert.AreEqual(0.5f, t.SpeedMultiplier, "walk speed");
            Assert.AreEqual(2f, t.LockSeconds, "lock");
            Assert.AreEqual(10f, t.Cooldown);
            Assert.AreEqual(ChickenClassFlags.Fatty, t.AllowedClasses);
            Assert.AreEqual("Turtle Mode", t.DisplayName);
            Assert.IsTrue(t.AcceptsPressWhileActive);
        }

        [Test]
        public void Walk_IsStealProofButStunnable_Lock_IsImmuneToControl_SourceGuard()
        {
            string s = Src("Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs");
            int act = s.IndexOf("public override void OnActivate", System.StringComparison.Ordinal);
            int press = s.IndexOf("public override float OnPressWhileActive", System.StringComparison.Ordinal);
            string walk = s.Substring(act, press - act), lockPart = s.Substring(press, s.IndexOf("public override void OnDeactivate", System.StringComparison.Ordinal) - press);
            StringAssert.Contains("GrantStealProof(Duration)", walk);
            StringAssert.DoesNotContain("GrantControlImmunity", walk, "the walk is stunnable");
            StringAssert.Contains("GrantControlImmunity(LockSeconds)", lockPart);
            StringAssert.Contains("GrantStealProof(LockSeconds)", lockPart);
            StringAssert.Contains("SetMovementLock(ctx.Slot)", lockPart);
        }

        [Test]
        public void UnkillableInBothPhases_TheyAreSteal_Proof() =>
            Assert.IsFalse(ExecuteRules.CanBeExecuted(stealProof: true), "walk and lock both grant steal-proof (above)");

        [Test]
        public void StunnedWhileWalking_TheLockPressIsRefused()
        {
            // A press on the running Turtle is not "cooling" (repress), so the stun gate is what answers.
            Assert.AreEqual(AbilityRefusal.Stunned,
                AbilityRefusalRules.Evaluate(slotUnavailable: false, onCooldown: false, stunned: true, noTarget: false));
            StringAssert.Contains("bool onCooldown = !slotUnavailable && !repress &&", Src("Assets/_Game/Scripts/Gameplay/AbilityController.cs"));
        }

        [Test]
        public void ThePhases_WalkThenLockThenEnd()
        {
            Assert.IsFalse(TurtleRules.PressEnds(locked: false), "second press: lock");
            Assert.IsTrue(TurtleRules.PressEnds(locked: true), "third press: end early");
        }

        [Test]
        public void Cooldown_RunsFromTheFirstPress_InEveryPath_SourceGuard()
        {
            string ac = Src("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            int repress = ac.IndexOf("if (IsSlotActive(slot) && ability.AcceptsPressWhileActive)", System.StringComparison.Ordinal);
            int setCd = ac.IndexOf("SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ResolveCooldownFor(ability)));", System.StringComparison.Ordinal);
            Assert.IsTrue(repress > 0 && setCd > repress, "the second / third press returns before any cooldown is set");
            string branch = ac.Substring(repress, setCd - repress);
            StringAssert.Contains("return true;", branch);
            // A walk that times out unlocked ends naturally: nothing refunds (no TriggerCooldown in the ability).
            StringAssert.DoesNotContain("TriggerCooldown", Src("Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs"));
        }

        [Test]
        public void Bots_LockOnlyWithAThreatRightOnThem()
        {
            Assert.IsTrue(BotTactics.ShouldTurtleLock(walking: true, locked: false, nearestThreatDist: 1f));
            Assert.IsFalse(BotTactics.ShouldTurtleLock(true, false, BotTactics.TurtleLockThreatRadius + 0.1f));
            Assert.IsFalse(BotTactics.ShouldTurtleLock(true, true, 1f), "already locked");
            Assert.IsFalse(BotTactics.ShouldTurtleLock(false, false, 1f), "not walking");
        }

        [Test]
        public void EggShell_IsRetired_NotRegistered_NotInAnyKit()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>("Assets/_Game/Data/AbilityRegistry.asset");
            Assert.IsFalse(reg.All.Any(a => a is EggShellAbilitySO), "Egg Shell left the pool");
            var egg = TestAssets.Load<EggShellAbilitySO>(TestAssets.AbilitiesDir + "/EggShell.asset");
            Assert.IsNotNull(egg, "kept in the repo");
            Assert.IsTrue(egg.Retired);
            foreach (var passive in reg.Passives)
                Assert.IsFalse(passive.Kit != null && passive.Kit.Any(a => a is EggShellAbilitySO), passive.name);
        }
    }
}
