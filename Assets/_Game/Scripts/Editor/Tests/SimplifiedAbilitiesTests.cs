using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro's 2026-11-05 "simplify abilities for kids" rulings: one ability per job (11 in the registry), range-only
    /// targeting behind one switch (<see cref="AbilityTargeting"/>, authored on <c>MatchConfig.asset</c>), the Shield folding
    /// in Immovable, the 8 kits, and the Spoiler's generic Stun feeding Death Mark's stun-at-kill rule.
    /// </summary>
    public class SimplifiedAbilitiesTests
    {
        private static AbilityRegistrySO Registry => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
        private static T Asset<T>(string file) where T : AbilityBaseSO => TestAssets.Load<T>(TestAssets.AbilitiesDir + "/" + file);
        private static string Src(string rel) => File.ReadAllText("Assets/_Game/Scripts/" + rel).Replace("\r\n", "\n");

        private static string Between(string src, string from, string to)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(a, 0, from);
            int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            Assert.Greater(b, a, to);
            return src.Substring(a, b - a);
        }

        private readonly List<ChickenController> _chickens = new();
        private TargetingMode _savedMode;

        [SetUp]
        public void SaveMode() => _savedMode = AbilityTargeting.Mode;

        [TearDown]
        public void Cleanup()
        {
            AbilityTargeting.Mode = _savedMode;
            foreach (var c in _chickens)
            {
                ChickenController.ActiveControllers.Remove(c);
                if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
            }
            _chickens.Clear();
        }

        /// <summary>A never-spawned chicken registered in ActiveControllers (the AbilityAimTests probe pattern).</summary>
        private ChickenController Chicken(Vector3 position, Vector3 forward)
        {
            var go = new GameObject("SimplifiedAbilitiesTests_Chicken");
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var c = go.AddComponent<ChickenController>();
            _chickens.Add(c);
            ChickenController.ActiveControllers.Add(c);
            return c;
        }

        // ---- the switch -----------------------------------------------------------------------------------------

        [Test]
        public void ShippedConfig_IsRangeOnly_AndTheStaticDefaultAgrees()
        {
            Assert.AreEqual(TargetingMode.RangeOnly, TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath).TargetingMode);
            Assert.AreEqual(TargetingMode.RangeOnly, ScriptableObject.CreateInstance<MatchConfigSO>().TargetingMode, "SO default");
            StringAssert.Contains("AbilityTargeting.Mode = matchConfig.TargetingMode;", Src("Installers/ProjectInstaller.cs"));
        }

        [TestCase(AbilityAimShape.Cone, true)]
        [TestCase(AbilityAimShape.SelfCircle, true)]
        [TestCase(AbilityAimShape.ForwardCircle, true)]
        [TestCase(AbilityAimShape.Capsule, true)]
        [TestCase(AbilityAimShape.SingleTarget, true)]
        [TestCase(AbilityAimShape.None, false)]
        [TestCase(AbilityAimShape.Aura, false)]
        [TestCase(AbilityAimShape.Jump, false)]
        public void RangeOnly_CollapsesExactlyTheVictimPickingShapes(AbilityAimShape shape, bool collapses)
        {
            Assert.AreEqual(collapses, AbilityTargeting.Collapses(shape, placesZone: false, TargetingMode.RangeOnly));
            Assert.IsFalse(AbilityTargeting.Collapses(shape, placesZone: false, TargetingMode.Shaped), "Shaped never collapses");
            Assert.IsFalse(AbilityTargeting.Collapses(shape, placesZone: true, TargetingMode.RangeOnly), "a placed zone keeps its footprint");
        }

        [TestCase("Snatch.asset")]
        [TestCase("WingSlam.asset")]
        [TestCase("CluckShock.asset")]
        public void RangeOnly_TheTargetedAbilities_AreCasterCircles_WithNoAimRotation(string file)
        {
            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            var a = Asset<AbilityBaseSO>(file);
            Assert.AreEqual(AbilityAimShape.SelfCircle, a.AimShape, file);
            Assert.AreEqual(0f, a.AimForwardOffset, file);
            Assert.AreEqual(360f, a.AimConeAngle, file);
            Assert.IsFalse(a.IsDirectionalAim, file + ": hold-to-aim must not turn the bird");
            Assert.IsFalse(a.RotatesToAim, file);
            Assert.IsFalse(a.UsesAim, file);
        }

        [Test]
        public void RangeOnly_TheBuffsJumpAuraAndDeathMark_KeepTheirShape()
        {
            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            Assert.AreEqual(AbilityAimShape.Aura, Asset<FeatherAuraAbilitySO>("FeatherAura.asset").AimShape);
            Assert.AreEqual(AbilityAimShape.Jump, Asset<ShadowstepAbilitySO>("Shadowstep.asset").AimShape);
            Assert.AreEqual(AbilityAimShape.None, Asset<MarkKillAbilitySO>("MarkKill.asset").AimShape);
            Assert.AreEqual(AbilityAimShape.None, Asset<TurtleModeAbilitySO>("TurtleMode.asset").AimShape);
        }

        // ---- (a) a rival directly behind, inside the radius, is hit ---------------------------------------------

        [TestCase("WingSlam.asset")]
        [TestCase("CluckShock.asset")]
        public void RangeOnly_ARivalDirectlyBehindInsideTheRadius_IsHit(string file)
        {
            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            var a = Asset<AbilityBaseSO>(file);
            var caster = Chicken(Vector3.zero, Vector3.forward);
            var behind = Chicken(new Vector3(0f, 0f, -(a.AimRadius - 0.05f)), Vector3.forward);
            var outside = Chicken(new Vector3(a.AimRadius + 0.05f, 0f, 0f), Vector3.forward);

            var hits = new List<ChickenController>();
            a.GatherTargets(caster, hits, Vector3.zero);
            CollectionAssert.AreEquivalent(new[] { behind }, hits, file + ": behind and inside is hit, just outside is not");
            Assert.IsTrue(a.HasAnyTarget(caster));
            Assert.IsFalse(a.WouldAffect(caster, outside));
        }

        [Test]
        public void RangeOnly_Steal_ReachesARivalDirectlyBehind_AndItsFilterIsCargoOnly()
        {
            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            var steal = Asset<SnatchAbilitySO>("Snatch.asset");
            var caster = Chicken(Vector3.zero, Vector3.forward);
            var behind = Chicken(new Vector3(0f, 0f, -(steal.SnatchRange - 0.05f)), Vector3.forward);
            Assert.IsTrue(steal.IsInAimShape(caster, behind), "a carrier behind the caster is inside the steal");
            // A probe chicken has no cargo, so the eligibility half is pinned on the source: carriers only, no facing.
            string filter = Between(Src("Abilities/SnatchAbilitySO.cs"), "protected override bool ExtraTargetFilter", "public override void OnActivate");
            StringAssert.Contains("Stealable", filter);
            foreach (var facing in new[] { "forward", "Angle", "Dot" }) StringAssert.DoesNotContain(facing, filter);
        }

        [Test]
        public void RangeOnly_EveryRivalInRange_IsHit_NotOnlyTheNearest()
        {
            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            var stun = Asset<WingSlamAbilitySO>("WingSlam.asset");
            var caster = Chicken(Vector3.zero, Vector3.forward);
            var a = Chicken(new Vector3(1f, 0f, 0f), Vector3.forward);
            var b = Chicken(new Vector3(-2f, 0f, -2f), Vector3.forward);
            var c = Chicken(new Vector3(0f, 0f, 4f), Vector3.forward);
            var hits = new List<ChickenController>();
            Assert.AreEqual(3, stun.GatherTargets(caster, hits, Vector3.zero));
            CollectionAssert.AreEquivalent(new[] { a, b, c }, hits);
        }

        // ---- (b) the switch brings the cone back ----------------------------------------------------------------

        [Test]
        public void Shaped_WingSlam_IsTheOldConeAgain()
        {
            AbilityTargeting.Mode = TargetingMode.Shaped;
            var stun = Asset<WingSlamAbilitySO>("WingSlam.asset");
            Assert.AreEqual(AbilityAimShape.Cone, stun.AimShape);
            Assert.AreEqual(120f, stun.AimConeAngle);
            Assert.IsTrue(stun.RotatesToAim, "a cone is aimed again");

            var caster = Chicken(Vector3.zero, Vector3.forward);
            var ahead = Chicken(new Vector3(0f, 0f, 2f), Vector3.forward);
            var behind = Chicken(new Vector3(0f, 0f, -2f), Vector3.forward);
            var hits = new List<ChickenController>();
            stun.GatherTargets(caster, hits, Vector3.zero);
            CollectionAssert.AreEquivalent(new[] { ahead }, hits, "Shaped: ahead only");

            AbilityTargeting.Mode = TargetingMode.RangeOnly;
            stun.GatherTargets(caster, hits, Vector3.zero);
            CollectionAssert.AreEquivalent(new[] { ahead, behind }, hits, "RangeOnly: both");
        }

        [Test]
        public void Shaped_Steal_IsTheOldArcAgain()
        {
            AbilityTargeting.Mode = TargetingMode.Shaped;
            var steal = Asset<SnatchAbilitySO>("Snatch.asset");
            var caster = Chicken(Vector3.zero, Vector3.forward);
            var behind = Chicken(new Vector3(0f, 0f, -2f), Vector3.forward);
            Assert.AreEqual(AbilityAimShape.Cone, steal.AimShape);
            Assert.IsFalse(steal.IsInAimShape(caster, behind));
        }

        [Test]
        public void TheBaseWall_IsStillAskedForEveryTarget_SourceGuard()
        {
            string body = Between(Src("Abilities/AbilityBaseSO.cs"),
                "public bool WouldAffect(Gameplay.ChickenController caster, Gameplay.ChickenController candidate, Vector3 forward)",
                "public int GatherTargets");
            StringAssert.Contains("PlayerBase.WallBetween(caster.transform.position, candidate.transform.position)", body);
        }

        // ---- (c) the Shield ----------------------------------------------------------------------------------------

        [Test]
        public void Shield_TheEgg_IsStealProofAndControlImmune_SourceGuard()
        {
            // Simplified 2026-11-05: the walk is slow-immune only (TurtleModeMergeTests); the egg carries the old guarantees.
            string egg = Between(Src("Abilities/TurtleModeAbilitySO.cs"), "public override bool OnActiveTick", "public override float OnPressWhileActive");
            StringAssert.Contains("GrantStealProof(remainingSeconds)", egg, "theft (and so the execute) is refused");
            StringAssert.Contains("GrantControlImmunity(remainingSeconds)", egg, "Immovable's window, the same call");
            Assert.IsFalse(ExecuteRules.CanBeExecuted(stealProof: true));
        }

        [Test]
        public void ControlImmunity_RefusesStunRootSlowShoveAndTheZoneSlow_SourceGuard()
        {
            string cc = Src("Gameplay/ChickenController.cs");
            StringAssert.Contains("if (IsControlImmune) return 0f;", Between(cc, "private float ApplyPassiveControlDuration", "// ---- Networking helpers"), "stun, root, ability slow");
            StringAssert.Contains("if (IsControlImmune) return;", Between(cc, "public void ApplyKnockback", "var p = Passive;"), "shove");
            StringAssert.Contains("if (IsSlowImmune) return;", Between(cc, "private void CheckAbilityZoneSlow()", "for (int i = 0;"), "the Slow aura (control immunity implies slow immunity)");
            StringAssert.Contains("=> victim != null && !victim.IsStealProof", Src("Gameplay/StealRules.cs"), "theft");
        }

        [Test]
        public void Shield_NullifiesEveryTargetedJob_AsTheTelegraphSeesIt()
        {
            // The steal is a pure steal now (no shove), so steal-proof alone nullifies it; Stun and Shove are purely control.
            Assert.IsTrue(Asset<SnatchAbilitySO>("Snatch.asset").TargetEffectIncludesSteal);
            Assert.IsFalse(Asset<SnatchAbilitySO>("Snatch.asset").TargetEffectIncludesControl, "KnockbackStrength 0: no shove");
            Assert.IsTrue(Asset<WingSlamAbilitySO>("WingSlam.asset").TargetEffectIsPurelyControl);
            Assert.IsTrue(Asset<CluckShockAbilitySO>("CluckShock.asset").TargetEffectIsPurelyControl);
        }

        // ---- (d) the roster and the kits -------------------------------------------------------------------------

        private static readonly Dictionary<string, Type> Jobs = new()
        {
            { "Peck", typeof(PeckAbilitySO) },
            { "Steal", typeof(SnatchAbilitySO) },
            { "Stun", typeof(WingSlamAbilitySO) },
            { "Shove", typeof(CluckShockAbilitySO) },
            { "Slow", typeof(FeatherAuraAbilitySO) },
            { "Speed", typeof(SpeedBurstAbilitySO) },
            { "Jump", typeof(ShadowstepAbilitySO) },
            { "Shield", typeof(TurtleModeAbilitySO) },
            { "Hide", typeof(InvisibilityAbilitySO) },
            { "Death Mark", typeof(MarkKillAbilitySO) },
        };

        [Test]
        public void TheRegistry_HoldsExactlyTheTenJobs()
        {
            // 11 on 2026-11-05, then Quick Drop became Featherfoot's passive the same day.
            var actives = Registry.ActiveAbilities.ToList();
            Assert.AreEqual(10, actives.Count, string.Join(", ", actives.Select(a => a.name)));
            foreach (var job in Jobs)
            {
                var a = actives.SingleOrDefault(x => x.GetType() == job.Value);
                Assert.IsNotNull(a, job.Key);
                Assert.AreEqual(job.Key, a.DisplayName, a.name);
                Assert.IsFalse(a.Retired, a.name);
            }
        }

        [Test]
        public void EveryRetiredAbility_IsOutOfTheRegistryAndEveryKit()
        {
            var registered = new HashSet<AbilityBaseSO>(Registry.All);
            var retired = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Where(a => a.Retired).ToList();
            Assert.AreEqual(19, retired.Count, "the 17 replaced on 2026-11-05, Egg Shell, and Quick Drop (now Featherfoot's passive)");
            foreach (var a in retired)
            {
                Assert.IsFalse(registered.Contains(a), a.name);
                foreach (var p in Registry.Passives) CollectionAssert.DoesNotContain(p.Kit, a, p.name);
            }
        }

        [TestCase(ChickenSubclass.Warrior_Bully, "Peck", "Steal", "Stun", "Jump")]          // Maestro 2026-11-05 Warrior rework
        [TestCase(ChickenSubclass.Warrior_Relentless, "Peck", "Steal", "Slow", "Speed")]
        [TestCase(ChickenSubclass.Speedy_Featherfoot, "Peck", "Speed", "Slow", "Steal")]
        [TestCase(ChickenSubclass.Speedy_Slippery, "Peck", "Speed", "Slow", "Shove")]
        [TestCase(ChickenSubclass.Fatty_Bulwark, "Peck", "Stun", "Shove", "Shield")]
        [TestCase(ChickenSubclass.Fatty_Hoarder, "Peck", "Shield", "Slow", "Steal")]
        [TestCase(ChickenSubclass.Assassin_Spoiler, "Death Mark", "Stun", "Jump", "Slow")]
        [TestCase(ChickenSubclass.Assassin_Thief, "Steal", "Hide", "Jump", "Shove")]
        public void TheKit_ResolvesExactlyAsRuled(ChickenSubclass subclass, string s0, string s1, string s2, string s3)
        {
            var passive = Registry.Passives.Single(p => p.Subclass == subclass);
            Assert.IsTrue(FixedKit.TryResolve(LoadoutMode.Fixed, passive, out var kit, out var problem), problem);
            CollectionAssert.AreEqual(new[] { s0, s1, s2, s3 }, kit.Select(a => a.DisplayName).ToArray(), passive.name);
        }

        [Test]
        public void EveryKitAbility_IsAllowedForItsClass_AndJumpIsNeverSpeedys()
        {
            foreach (var p in Registry.Passives)
            {
                Assert.IsTrue(PreEquippedLoadout.TryGetSingleClass(p.AllowedClasses, out var cls), p.name);
                foreach (var a in p.Kit)
                    if (a.AllowedClasses != ChickenClassFlags.None)   // Peck and Death Mark are pre-equip only
                        Assert.IsTrue(AbilityRegistrySO.IsAllowedFor(a, cls), $"{p.name}: {a.name} not allowed for {cls}");
            }
            var jump = Asset<ShadowstepAbilitySO>("Shadowstep.asset");
            Assert.IsFalse(AbilityRegistrySO.IsAllowedFor(jump, ChickenClass.Speedy), "Speedy never gets Jump");
        }

        [Test]
        public void TheShippedNumbers_MatchTheRuling()
        {
            var steal = Asset<SnatchAbilitySO>("Snatch.asset");
            Assert.AreEqual(5.16f, steal.SnatchRange, 1e-4f);
            Assert.AreEqual(5f, steal.StealAmount);
            Assert.AreEqual(0f, steal.KnockbackStrength);
            Assert.AreEqual(5f, steal.Cooldown);
            var stun = Asset<WingSlamAbilitySO>("WingSlam.asset");
            Assert.AreEqual(5.4f, stun.StunRadius, 1e-4f);
            Assert.AreEqual(1.3f, stun.StunDuration, 1e-4f);
            Assert.AreEqual(8f, stun.Cooldown);
            var shove = Asset<CluckShockAbilitySO>("CluckShock.asset");
            Assert.AreEqual(5.4f, shove.ShockRadius, 1e-4f);
            Assert.AreEqual(16.2f, shove.KnockbackForce, 1e-4f);
            Assert.AreEqual(7f, shove.Cooldown);
            var slow = Asset<FeatherAuraAbilitySO>("FeatherAura.asset");
            Assert.AreEqual(6.48f, slow.AuraRadius, 1e-4f);
            Assert.AreEqual(0.55f, slow.AuraSlowFactor, 1e-4f);
            Assert.AreEqual(3f, slow.Duration);
            Assert.AreEqual(7f, slow.Cooldown);
        }

        [TestCase("Snatch.asset")]
        [TestCase("WingSlam.asset")]
        [TestCase("CluckShock.asset")]
        [TestCase("FeatherAura.asset")]
        [TestCase("SpeedBurst.asset")]
        [TestCase("Shadowstep.asset")]
        [TestCase("TurtleMode.asset")]
        [TestCase("Invisibility.asset")]
        public void TheSoDefaults_EqualTheShippedAsset(string file)
        {
            var shipped = Asset<AbilityBaseSO>(file);
            var fresh = (AbilityBaseSO)ScriptableObject.CreateInstance(shipped.GetType());
            try
            {
                Assert.AreEqual(shipped.DisplayName, fresh.DisplayName, file);
                Assert.AreEqual(shipped.ShortLabel, fresh.ShortLabel, file);
                Assert.AreEqual(shipped.Description, fresh.Description, file);
                Assert.AreEqual(shipped.AllowedClasses, fresh.AllowedClasses, file);
                Assert.AreEqual(shipped.Category, fresh.Category, file);
                Assert.AreEqual(shipped.Duration, fresh.Duration, 1e-4f, file);
                Assert.AreEqual(shipped.Cooldown, fresh.Cooldown, 1e-4f, file);
                Assert.AreEqual(shipped.AimRadius, fresh.AimRadius, 1e-4f, file);
            }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        // ---- (e) Death Mark: the Spoiler's Stun satisfies the stun-at-kill rule -----------------------------------

        [Test]
        public void Spoiler_HasAStunSource_ThatSetsIsStunned_AndCountsAtTheKill()
        {
            var spoiler = Registry.Passives.Single(p => p.Subclass == ChickenSubclass.Assassin_Spoiler);
            CollectionAssert.IsEmpty(FixedKit.Problems(spoiler), "the kit rule 'Death Mark needs a stun source' holds");
            var stun = spoiler.Kit.Single(FixedKit.EmitsStun);
            Assert.IsInstanceOf<WingSlamAbilitySO>(stun, "the generic Stun");
            Assert.AreEqual(ControlKind.Stun, stun.EmittedControl.Kind);
            Assert.AreEqual(1.3f, stun.EmittedControl.Seconds, 1e-4f);

            // The stun goes out through the one stun RPC, whose authority sets the networked IsStunned that ExecuteRules reads.
            StringAssert.Contains("float stun = ResolveOutgoingControlSeconds(StunDuration, caster);", Src("Abilities/StunBurstAbilitySO.cs"));
            StringAssert.Contains("RPC_ApplyStun(stun, caster.Id)", Src("Abilities/StunBurstAbilitySO.cs"));
            StringAssert.Contains("IsStunned = true;", Src("Gameplay/ChickenController.cs"));
            Assert.IsTrue(ExecuteRules.HasStunAtKill(stunnedNow: true, stunEverSeen: true, secondsSinceStunned: 0f, latency: 0.1f));

            // The Spoiler bot casts it when it still runs past the arm: 1.3 s covers the 0.25 s lead.
            Assert.AreEqual(BotMarkStep.Stun, BotTactics.NextMarkStep(true, false, armRemaining: 1.0f, windowRemaining: 2f,
                stunInReach: true, stunSeconds: stun.EmittedControl.Seconds));
        }

        [Test]
        public void DeathMark_KillRange_GaveBackWhatTheStunsReachGained()
        {
            // Maestro 2026-11-05: the Stun reaches 5.4 m against Ambush's 3.6 m, so the kill range dropped 8 -> 6.2 m and
            // stun reach + kill range stays at its Ambush-era 11.6 m.
            var mark = Asset<MarkKillAbilitySO>("MarkKill.asset");
            var stun = Asset<WingSlamAbilitySO>("WingSlam.asset");
            Assert.AreEqual(6.2f, mark.KillRange, 1e-5f);
            Assert.AreEqual(AssassinExecute.DefaultKillRange, mark.KillRange, 1e-5f, "constructor default / fallback = asset");
            Assert.AreEqual(3.6f + 8f, stun.StunRadius + mark.KillRange, 1e-4f);
            StringAssert.Contains("within 6.2m", mark.DescriptionText);
            Assert.AreEqual(mark.DescriptionText, mark.Description, "stale .asset Description");
        }

        [Test]
        public void TheMarkHud_LightsAnyStun_NotAmbushOnly_SourceGuard()
        {
            StringAssert.DoesNotContain("AmbushAbilitySO", Src("Visuals/MarkFeedback.cs"));
            StringAssert.DoesNotContain("AmbushAbilitySO", Src("Visuals/AbilitySlotOverlay.cs"));
            StringAssert.DoesNotContain("AmbushAbilitySO", Src("Input/TouchControlsController.cs"));
            Assert.IsTrue(Visuals.AbilitySlotOverlay.ShowsGuideDuringMark(Asset<WingSlamAbilitySO>("WingSlam.asset"), assassinMarkActive: true));
            Assert.IsFalse(Visuals.AbilitySlotOverlay.ShowsGuideDuringMark(Asset<CluckShockAbilitySO>("CluckShock.asset"), assassinMarkActive: true));
        }
    }
}
