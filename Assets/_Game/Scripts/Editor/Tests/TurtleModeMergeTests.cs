using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Shield (Turtle Mode, which absorbed Egg Shell and Immovable), simplified by Maestro 2026-11-05: one press gives a
    /// 1 s slow-immune walk, then a 2 s egg that starts on its own (immobile, immune to every control effect, steal-proof and
    /// so unkillable). A second press ends the whole Shield; casting any other move ends only the egg (a running egg ends the
    /// Shield, a pending one is dropped and the walk runs out). The cooldown starts at the cast.
    /// </summary>
    public class TurtleModeMergeTests
    {
        private static TurtleModeAbilitySO Turtle => TestAssets.Load<TurtleModeAbilitySO>(TestAssets.AbilitiesDir + "/TurtleMode.asset");
        private static string Src(string f) => File.ReadAllText(f).Replace("\r\n", "\n");
        private const string ShieldPath = "Assets/_Game/Scripts/Abilities/TurtleModeAbilitySO.cs";
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string AbilityCtlPath = "Assets/_Game/Scripts/Gameplay/AbilityController.cs";

        private static string Between(string s, string from, string to)
        {
            int a = s.IndexOf(from, System.StringComparison.Ordinal);
            Assert.Greater(a, -1, from);
            int b = s.IndexOf(to, a + from.Length, System.StringComparison.Ordinal);
            Assert.Greater(b, a, to);
            return s.Substring(a, b - a);
        }

        [Test]
        public void TheNumbers_AreData_EveryClass_AndTheRunIsWalkPlusEgg()
        {
            var t = Turtle;
            Assert.AreEqual(1f, t.WalkSeconds, "walk");
            Assert.AreEqual(2f, t.EggSeconds, "egg");
            Assert.AreEqual(t.WalkSeconds + t.EggSeconds, t.Duration, 1e-5f, "the slot runs walk + egg");
            Assert.AreEqual(0.5f, t.SpeedMultiplier, "walk speed");
            Assert.AreEqual(8f, t.Cooldown);
            Assert.AreEqual(ChickenClassFlags.All, t.AllowedClasses);
            Assert.AreEqual("Egg Shell", t.DisplayName);
            Assert.AreEqual("EGG", t.ShortLabel);
            Assert.IsTrue(t.AcceptsPressWhileActive);
            Assert.IsTrue(t.HasFollowUpPhase);
            Assert.IsFalse(t.EndsOnNextMove, "another cast ends the egg only, never the walk");

            var fresh = UnityEngine.ScriptableObject.CreateInstance<TurtleModeAbilitySO>();
            try
            {
                Assert.AreEqual(t.WalkSeconds, fresh.WalkSeconds);
                Assert.AreEqual(t.EggSeconds, fresh.EggSeconds);
                Assert.AreEqual(t.Duration, fresh.Duration);
                Assert.AreEqual(t.Description, fresh.Description);
            }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        [Test]
        public void TheLiveMove_IsNamedEggShell_AndTheRetiredOneIsMarkedOld()
        {
            // Spec 2026-11-06 D2a: the live move takes the name, the retired asset steps aside so no two shown names collide.
            Assert.AreEqual("Egg Shell", Turtle.DisplayName);
            Assert.AreEqual("EGG", Turtle.ShortLabel);
            var old = TestAssets.Load<AbilityBaseSO>(TestAssets.AbilitiesDir + "/EggShell.asset");
            Assert.IsTrue(old.Retired, "EggShell.asset is the retired one");
            Assert.AreEqual("Egg Shell (old)", old.DisplayName);
            Assert.AreEqual("OLD", old.ShortLabel, "EGG belongs to the live move; the label guard counts retired assets too");
        }

        // ---- Phase 1: the walk blocks slows only -----------------------------------------------------------------

        [Test]
        public void Walk_GrantsSlowImmunityOnly_SourceGuard()
        {
            string walk = Between(Src(ShieldPath), "public override void OnActivate", "public override bool OnActiveTick");
            StringAssert.Contains("GrantSlowImmunity(WalkSeconds)", walk);
            StringAssert.Contains("SetMoveSpeed(ctx.Slot, SpeedMultiplier)", walk);
            StringAssert.DoesNotContain("GrantControlImmunity", walk, "stun, root and shove still land on the walk");
            StringAssert.DoesNotContain("GrantStealProof", walk, "theft and the execution still land on the walk");
            StringAssert.DoesNotContain("SetMovementLock", walk);
        }

        [Test]
        public void SlowImmunity_RefusesTheTimedAndTheZoneSlow_ButNotStunRootOrShove_SourceGuard()
        {
            string cc = Src(ControllerPath);
            StringAssert.Contains("duration = IsSlowImmune ? 0f : ApplyPassiveControlDuration(duration, ControlKind.Slow);",
                Between(cc, "public void RPC_ApplyAbilitySlow(", "public void RPC_ApplyStun("), "the timed slow");
            StringAssert.Contains("if (IsSlowImmune) return;", Between(cc, "private void CheckAbilityZoneSlow()", "for (int i = 0;"), "zone / aura slow");
            // Stun, root and shove keep reading the full control-immunity window only.
            StringAssert.Contains("if (IsControlImmune) return 0f;", Between(cc, "private float ApplyPassiveControlDuration", "// ---- Networking helpers"));
            StringAssert.DoesNotContain("IsSlowImmune", Between(cc, "private float ApplyPassiveControlDuration", "// ---- Networking helpers"));
            StringAssert.Contains("if (IsControlImmune) return;", Between(cc, "public void ApplyKnockback", "var p = Passive;"));
            // Theft and the execution read the steal-proof window, which the walk does not open.
            StringAssert.Contains("=> victim != null && !victim.IsStealProof", Src("Assets/_Game/Scripts/Gameplay/StealRules.cs"));
            Assert.IsTrue(ExecuteRules.CanBeExecuted(stealProof: false), "the walk is killable");
            // The egg's full immunity implies slow immunity.
            StringAssert.Contains("IsControlImmune || (Runner != null && !SlowImmuneTimer.ExpiredOrNotRunning(Runner))", cc);
        }

        // ---- Phase 2: the egg blocks everything -------------------------------------------------------------------

        [Test]
        public void Egg_IsImmobile_ControlImmune_AndStealProof_SourceGuard()
        {
            string egg = Between(Src(ShieldPath), "public override bool OnActiveTick", "public override float OnPressWhileActive");
            StringAssert.Contains("c.Effects.SetMovementLock(ctx.Slot);", egg);
            StringAssert.Contains("c.Effects.RemoveMoveSpeed(ctx.Slot);", egg);
            StringAssert.Contains("c.GrantControlImmunity(remainingSeconds);", egg, "stun, slow, root and shove");
            StringAssert.Contains("c.GrantStealProof(remainingSeconds);", egg, "theft and the execution");
            Assert.IsFalse(ExecuteRules.CanBeExecuted(stealProof: true), "the egg is unkillable");
        }

        [Test]
        public void TheEgg_StartsByItself_WhenTheWalkEnds_Once()
        {
            var t = Turtle;
            float run = t.Duration;
            Assert.IsFalse(ShieldRules.EggStartsNow(run, t.EggSeconds, eggRunning: false), "cast tick: walking");
            Assert.IsFalse(ShieldRules.EggStartsNow(run - 0.5f, t.EggSeconds, false), "0.5 s in: walking");
            Assert.IsTrue(ShieldRules.EggStartsNow(run - t.WalkSeconds, t.EggSeconds, false), "at 1 s the egg starts");
            Assert.IsTrue(ShieldRules.EggStartsNow(run - t.WalkSeconds - 0.03f, t.EggSeconds, false), "first tick past 1 s");
            Assert.IsFalse(ShieldRules.EggStartsNow(1.5f, t.EggSeconds, eggRunning: true), "only once");
            Assert.IsFalse(ShieldRules.EggStartsNow(0f, t.EggSeconds, false), "a run that is over starts nothing");

            // The controller ticks a live follow-up every authority tick, before input.
            string ac = Src(AbilityCtlPath);
            StringAssert.Contains("TickFollowUpPhases();", Between(ac, "public override void FixedUpdateNetwork()", "if (!GetInput<PlayerNetworkInput>(out var input)) return;"));
            StringAssert.Contains("changed |= running.OnActiveTick(_ctx, remaining);", ac);
        }

        // ---- Presses and other casts ----------------------------------------------------------------------------

        [Test]
        public void ASecondPress_EndsTheWholeShield_InEitherPhase()
        {
            var t = Turtle;
            var ctx = new AbilityContext(null);
            Assert.LessOrEqual(t.OnPressWhileActive(ctx), 0f, "0 = end now");
            StringAssert.Contains("public override float OnPressWhileActive(AbilityContext ctx) => 0f;", Src(ShieldPath));
            // Ending closes every window the run opened, whatever ended it.
            string end = Between(Src(ShieldPath), "public override void OnDeactivate", "/// <summary>The Shield's phase rules");
            StringAssert.Contains("c.EndShellWindows();", end);
            StringAssert.Contains("SlowImmuneTimer = TickTimer.None;", Between(Src(ControllerPath), "public void EndShellWindows()", "public bool IsSlowImmune"));
        }

        [Test]
        public void AnotherCast_DuringTheEgg_EndsTheShield()
        {
            var t = Turtle;
            Assert.AreEqual(0f, ShieldRules.RunAfterOtherCast(1.5f, t.EggSeconds, eggRunning: true));
        }

        [Test]
        public void AnotherCast_DuringTheWalk_DropsTheEgg_AndTheWalkRunsOut()
        {
            var t = Turtle;
            // 0.4 s into the walk (2.6 s of the run left): 0.6 s of walk remain, no egg.
            Assert.AreEqual(0.6f, ShieldRules.RunAfterOtherCast(t.Duration - 0.4f, t.EggSeconds, eggRunning: false), 1e-5f);
            Assert.AreEqual(0f, ShieldRules.RunAfterOtherCast(t.EggSeconds, t.EggSeconds, false), "walk already over: ends");

            // The controller marks the follow-up cancelled (no more ticks, so no egg) and keeps or ends the run as told.
            string cast = Between(Src(AbilityCtlPath), "private void EndStealthAndPeckForCast(int castSlot)", "private void HandleOwnerDeath");
            StringAssert.Contains("_followUpCancelled = AbilityRunRules.Begin(_followUpCancelled, i);", cast);
            StringAssert.Contains("next = running.OnOtherCast(_ctx, remaining);", cast);
            StringAssert.Contains("if (next > 0f) SetActiveTimer(i, TickTimer.CreateFromSeconds(Runner, next));", cast);
            StringAssert.Contains("_followUpCancelled = AbilityRunRules.End(_followUpCancelled, slot);", Src(AbilityCtlPath), "a new cast re-arms the egg");
            // Auto-Peck must not cancel the egg for the player.
            StringAssert.Contains("if (HasLiveFollowUp(i, running)) return true;", Between(Src(AbilityCtlPath), "private bool AnyStealthRunning", "private byte _followUpCancelled"));
        }

        [Test]
        public void StunnedWhileWalking_TheEndPressIsRefused()
        {
            // A press on the running Shield is not "cooling" (repress), so the stun gate is what answers.
            Assert.AreEqual(AbilityRefusal.Stunned,
                AbilityRefusalRules.Evaluate(slotUnavailable: false, onCooldown: false, stunned: true, noTarget: false));
            StringAssert.Contains("bool onCooldown = !slotUnavailable && !repress &&", Src(AbilityCtlPath));
        }

        [Test]
        public void Cooldown_RunsFromTheCast_InEveryPath_SourceGuard()
        {
            string ac = Src(AbilityCtlPath);
            int repress = ac.IndexOf("if (IsSlotActive(slot) && ability.AcceptsPressWhileActive)", System.StringComparison.Ordinal);
            int setCd = ac.IndexOf("SetCooldown(slot, TickTimer.CreateFromSeconds(Runner, ResolveCooldownFor(ability)));", System.StringComparison.Ordinal);
            Assert.IsTrue(repress > 0 && setCd > repress, "the second press returns before any cooldown is set");
            StringAssert.Contains("return true;", ac.Substring(repress, setCd - repress));
            StringAssert.DoesNotContain("TriggerCooldown", Src(ShieldPath));
            StringAssert.DoesNotContain("SetCooldown", Between(ac, "private void EndStealthAndPeckForCast(int castSlot)", "private void HandleOwnerDeath"));
        }

        [Test]
        public void Bots_LeaveARunningShieldAlone()
        {
            // A second press would end it now, so the old "lock when a threat is close" press is gone.
            string bot = Src("Assets/_Game/Scripts/Gameplay/BotController.cs");
            StringAssert.Contains("if (_abilities.GetSlot(slot) is TurtleModeAbilitySO && _abilities.IsSlotActive(slot)) return;", bot);
            StringAssert.DoesNotContain("ShouldTurtleLock", bot);
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
