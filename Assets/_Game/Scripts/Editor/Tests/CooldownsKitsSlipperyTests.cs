using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro's 2026-11-05 batch: shorter cooldowns (bands low 1-3 s, medium 4-6 s, high 7-8 s), three kit swaps, the
    /// Slippery rework (ability slows only, strength and duration; stuns, roots, pile and bump slows not resisted) and the
    /// Assassin's speed.
    /// </summary>
    public sealed class CooldownsKitsSlipperyTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string ScriptsDir = "Assets/_Game/Scripts";

        private static AbilityRegistrySO Registry => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
        private static T Shipped<T>() where T : PassiveAbilitySO => Registry.Passives.OfType<T>().Single();
        private static T Asset<T>() where T : AbilityBaseSO => TestAssets.LoadAllIn<T>(TestAssets.AbilitiesDir).Single();
        private static PassiveAbilitySO Spec(ChickenSubclass s) => Registry.Passives.Single(p => p.Subclass == s);
        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        private static string Between(string s, string from, string to)
        {
            int a = s.IndexOf(from, System.StringComparison.Ordinal);
            Assert.Greater(a, -1, from);
            int b = s.IndexOf(to, a + from.Length, System.StringComparison.Ordinal);
            Assert.Greater(b, a, to);
            return s.Substring(a, b - a);
        }

        // ---- 1. Cooldowns ---------------------------------------------------------------------

        [Test]
        public void ShippedCooldowns_AreTheNewBands()
        {
            Assert.AreEqual(2f, Asset<ShadowstepAbilitySO>().Cooldown, "Jump");
            Assert.AreEqual(4f, Asset<SnatchAbilitySO>().Cooldown, "Steal");
            Assert.AreEqual(4f, Asset<SpeedBurstAbilitySO>().Cooldown, "Speed");
            Assert.AreEqual(4.5f, Asset<InvisibilityAbilitySO>().Cooldown, 1e-5f, "Hide");
            Assert.AreEqual(5f, Asset<CluckShockAbilitySO>().Cooldown, "Shove");
            Assert.AreEqual(6f, Asset<SlowAbilitySO>().Cooldown, "Slow");
            Assert.AreEqual(7f, Asset<FeatherAuraAbilitySO>().Cooldown, "Slow Area");
            Assert.AreEqual(7f, Asset<WingSlamAbilitySO>().Cooldown, "Stun");
            Assert.AreEqual(8f, Asset<TurtleModeAbilitySO>().Cooldown, "Shield");
        }

        [Test]
        public void SpeedAndHide_NewNumbers_AndDescriptionsQuoteThem()
        {
            var speed = Asset<SpeedBurstAbilitySO>();
            Assert.AreEqual(1.3f, speed.SpeedMultiplier, 1e-5f);
            Assert.AreEqual(1.5f, speed.Duration, 1e-5f, "duration unchanged");
            StringAssert.Contains("1.3x", speed.Description);
            StringAssert.Contains("1.3x", speed.DescriptionText);

            var hide = Asset<InvisibilityAbilitySO>();
            Assert.AreEqual(2f, hide.Duration, 1e-5f);
            StringAssert.Contains("for 2s", hide.Description);
            StringAssert.Contains("for 2s", hide.DescriptionText);
        }

        [Test]
        public void DeathMark_OutcomeCooldowns_AndTheFallbacksMatchTheAsset()
        {
            var mark = Asset<MarkKillAbilitySO>();
            Assert.AreEqual(4f, mark.FailCooldownSeconds);
            Assert.AreEqual(8f, mark.SuccessCooldownSeconds);
            Assert.AreEqual(4f, mark.RoundStartCooldownSeconds, "unchanged");
            Assert.AreEqual(mark.FailCooldownSeconds, AssassinExecute.FailCooldown, "the no-asset fallback stays in step");
            Assert.AreEqual(mark.SuccessCooldownSeconds, AssassinExecute.SuccessCooldown, "the no-asset fallback stays in step");
        }

        [Test]
        public void BullyCooldownCut_OnTheNewCooldowns()
        {
            var bully = Shipped<BullyPassiveSO>();
            Assert.AreEqual(3f, bully.ModifyCooldown(Asset<SnatchAbilitySO>().Cooldown, Asset<SnatchAbilitySO>(), null), 1e-4f, "Steal 4 -> 3");
            Assert.AreEqual(5.25f, bully.ModifyCooldown(Asset<WingSlamAbilitySO>().Cooldown, Asset<WingSlamAbilitySO>(), null), 1e-4f, "Stun 7 -> 5.25");
            Assert.AreEqual(1.5f, bully.ModifyCooldown(Asset<ShadowstepAbilitySO>().Cooldown, Asset<ShadowstepAbilitySO>(), null), 1e-4f, "Jump 2 -> 1.5");
        }

        // ---- 2. Kits --------------------------------------------------------------------------

        [Test]
        public void Featherfoot_CannotSteal_AndItsKitIsSlipperys_ApprovedAsIs()
        {
            var featherfoot = Spec(ChickenSubclass.Speedy_Featherfoot);
            Assert.IsFalse(featherfoot.Kit.Any(BotTactics.IsStealer), "Speedys should not steal by design (Maestro 2026-11-05)");
            // Identical kits are approved (Maestro 2026-11-05); the two Speedys differ by passive only.
            CollectionAssert.AreEqual(Spec(ChickenSubclass.Speedy_Slippery).Kit, featherfoot.Kit);
        }

        [Test]
        public void NoSpeedyKit_Steals()
        {
            foreach (var s in new[] { ChickenSubclass.Speedy_Slippery, ChickenSubclass.Speedy_Featherfoot })
                Assert.IsFalse(Spec(s).Kit.Any(a => a.NominalStealAmount > 0f), s.ToString());
        }

        [Test]
        public void TheSwappedKits_StayLegal()
        {
            Assert.IsEmpty(FixedKit.ValidateRoster(Registry.Passives, Registry.All), "the roster");
            foreach (var s in new[] { ChickenSubclass.Speedy_Featherfoot, ChickenSubclass.Assassin_Thief, ChickenSubclass.Assassin_Spoiler })
                Assert.IsEmpty(FixedKit.Problems(Spec(s)), s.ToString());
        }

        // ---- 3. Slippery ----------------------------------------------------------------------

        [Test]
        public void Slippery_LeavesStunsAndRootsWhole_ShortensTimedSlows()
        {
            var slippery = Shipped<SlipperyPassiveSO>();
            Assert.AreEqual(0.6f, slippery.Resist, 1e-6f);
            Assert.AreEqual(1.3f, slippery.ModifyControlDuration(1.3f, ControlKind.Stun, null), 1e-6f, "the stun is Speedy's weakness");
            Assert.AreEqual(2f, slippery.ModifyControlDuration(2f, ControlKind.Root, null), 1e-6f, "roots are not resisted");
            Assert.AreEqual(1.8f, slippery.ModifyControlDuration(3f, ControlKind.Slow, null), 1e-5f, "timed slow x0.6");
        }

        [Test]
        public void Slippery_WeakensAbilitySlows_ByScalingTheReduction()
        {
            var slippery = Shipped<SlipperyPassiveSO>();
            Assert.AreEqual(0.73f, slippery.ModifyIncomingSlowFactor(0.55f, null), 1e-5f, "1 - (1 - 0.55) x 0.6");
            Assert.AreEqual(1f, slippery.ModifyIncomingSlowFactor(1f, null), 1e-6f, "no slow stays no slow");
            Assert.AreEqual(0.4f, slippery.ModifyIncomingSlowFactor(0f, null), 1e-6f, "even a full stop keeps 40% speed");
            // The resisted aura slow is still a visible slow (below the replicated VFX threshold).
            Assert.Less(slippery.ModifyIncomingSlowFactor(Asset<SlowAbilitySO>().SlowFactor, null), ChickenController.SlowVfxFlagThreshold);
        }

        [Test]
        public void OnlySlippery_ResistsSlowStrength_AndBulwarkRelentlessStillShortenEveryKind()
        {
            foreach (var p in Registry.Passives)
            {
                if (p is SlipperyPassiveSO) continue;
                Assert.AreEqual(0.55f, p.ModifyIncomingSlowFactor(0.55f, null), 1e-6f, p.name);
            }
            var bulwark = Shipped<BulwarkPassiveSO>();
            var relentless = (RelentlessPassiveSO)WarriorPassiveDataTests.ShelvedRelentless();   // shelved 2026-11-25: out of the registry, its numbers still pinned
            foreach (var kind in new[] { ControlKind.Stun, ControlKind.Root, ControlKind.Slow })
            {
                Assert.AreEqual(0.65f, bulwark.ModifyControlDuration(1f, kind, null), 1e-6f, "Bulwark " + kind);
                Assert.AreEqual(0.75f, relentless.ModifyControlDuration(1f, kind, null), 1e-6f, "Relentless " + kind);
            }
        }

        [Test]
        public void SlowZones_HaveNoLifetimeWindow_SlipperyGetsTheWeakerSlowOnly_SourceGuard()
        {
            // Maestro 2026-11-05 removed the first-60%-of-the-zone's-life window (BL-123 holds the open duration rule).
            string cc = Src(ControllerPath);
            string zoneLoop = Between(cc, "private void CheckAbilityZoneSlow()", "private ChickenStatsSO ResolveStatsForClass");
            StringAssert.DoesNotContain("AgeFraction", zoneLoop);
            StringAssert.DoesNotContain("LifetimeSeconds", Src(ScriptsDir + "/Gameplay/AbilityZone.cs"));
            Assert.IsNull(typeof(PassiveAbilitySO).GetMethod("ZoneSlowLifetimeFraction"));
        }

        [Test]
        public void AbilitySlows_GoThroughOneChokepoint_PileAndBumpSlowsDoNot_SourceGuard()
        {
            string cc = Src(ControllerPath);
            // The one writer of SlowSource.Ability, carrying the strength hook.
            string choke = Between(cc, "private void ApplyAbilitySlow(float factor)", "/// <summary>");
            StringAssert.Contains("ModifyIncomingSlowFactor(factor, this)", choke);
            StringAssert.Contains("ApplySlow(SlowSource.Ability,", choke);
            Assert.AreEqual(1, Regex.Matches(cc, @"ApplySlow\(SlowSource\.Ability").Count, "only ApplyAbilitySlow writes the Ability source");
            StringAssert.Contains("ApplyAbilitySlow(_abilitySlowFactor);", cc, "the timed ability slow");
            StringAssert.Contains("ApplyAbilitySlow(zone.SlowFactor);", Between(cc, "private void CheckAbilityZoneSlow()", "private ChickenStatsSO ResolveStatsForClass"), "the zone / aura slow");
            // Pile and bump slows are applied as authored.
            StringAssert.Contains("ApplySlow(SlowSource.Pile, PileSlowFactor);", cc);
            StringAssert.Contains("ApplySlow(SlowSource.Collision, CollisionSlowFactor);", cc);
            // Each control RPC passes its own kind to the duration hook.
            StringAssert.Contains("ApplyPassiveControlDuration(duration, ControlKind.Slow)", Between(cc, "public void RPC_ApplyAbilitySlow(", "public void RPC_ApplyStun("));
            StringAssert.Contains("ApplyPassiveControlDuration(seconds, ControlKind.Stun)", Between(cc, "public void RPC_ApplyStun(", "public void RPC_ApplyRoot("));
            StringAssert.Contains("ApplyPassiveControlDuration(duration, ControlKind.Root)", Between(cc, "public void RPC_ApplyRoot(", "ControlRejectionLimiter"));
        }

        [Test]
        public void EachNewHook_HasExactlyOneProductionCallSite()
        {
            var production = Directory.GetFiles(ScriptsDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Editor/"))
                .Select(f => File.ReadAllText(f)).ToArray();
            int Calls(string pattern) => production.Sum(s => Regex.Matches(s, pattern).Count);
            Assert.AreEqual(1, Calls(@"\.ModifyIncomingSlowFactor\("), "ModifyIncomingSlowFactor: one chokepoint");
        }

        [Test]
        public void SlipperyDefault_EqualsTheAsset()
        {
            var fresh = ScriptableObject.CreateInstance<SlipperyPassiveSO>();
            try { Assert.AreEqual(Shipped<SlipperyPassiveSO>().Resist, fresh.Resist, 1e-6f); }
            finally { Object.DestroyImmediate(fresh); }
        }

        // ---- 4. Assassin speed ----------------------------------------------------------------

        [Test]
        public void Assassin_MoveSpeed_Is11_4_SpeedyUnchanged()
        {
            var assassin = UnityEditor.AssetDatabase.LoadAssetAtPath<ChickenStatsSO>("Assets/_Game/Data/Classes/Assassin.asset");
            var speedy = UnityEditor.AssetDatabase.LoadAssetAtPath<ChickenStatsSO>("Assets/_Game/Data/Classes/Speedy.asset");
            Assert.IsNotNull(assassin);
            Assert.IsNotNull(speedy);
            Assert.AreEqual(11.4f, assassin.MoveSpeed, 1e-4f, "Maestro 2026-11-27: class speeds flattened (Fatty 10.9, Warrior 11.3, Sneaky 11.4, Speedy 12.15 unchanged)");
            Assert.AreEqual(12.15f, speedy.MoveSpeed, 1e-4f);
        }
    }
}
