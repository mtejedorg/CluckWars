using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-13: the one Slow job is split. <see cref="SlowAbilitySO"/> is the instant timed slow (every rival in
    /// reach, slowed for SlowSeconds even after leaving the circle); <see cref="FeatherAuraAbilitySO"/> is Slow Area, a lasting
    /// area in one of two shapes (an aura that follows the caster, or a placed zone that stays where it was cast).
    /// </summary>
    public sealed class SlowAbilityTests
    {
        private static T Asset<T>() where T : AbilityBaseSO => TestAssets.LoadAllIn<T>(TestAssets.AbilitiesDir).Single();
        private static AbilityBaseSO Ability(string name) =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Single(a => a.name == name);
        private static PassiveAbilitySO Passive(string name) => Passive<PassiveAbilitySO>(name);
        private static T Passive<T>(string name) where T : PassiveAbilitySO =>
            TestAssets.LoadAllIn<T>(TestAssets.AbilitiesDir).Single(p => p.name == name);

        // ---- the instant Slow ------------------------------------------------------------------------------------------

        [Test]
        public void Slow_ShippedValues_AreTheStartingValues()
        {
            var slow = Asset<SlowAbilitySO>();
            Assert.AreEqual("Slow", slow.DisplayName);
            Assert.AreEqual("SLOW", slow.ShortLabel);
            Assert.AreEqual(6.48f, slow.Radius, 1e-4f, "radius (the old reach)");
            Assert.AreEqual(0.55f, slow.SlowFactor, 1e-4f, "speed while slowed");
            Assert.AreEqual(3f, slow.SlowSeconds, 1e-4f, "how long the slow lasts");
            Assert.AreEqual(6f, slow.Cooldown, 1e-4f, "cooldown");
            Assert.AreEqual(AbilityJob.Slow, slow.Job);
        }

        [Test]
        public void Slow_IsAnInstantTimedSlow_NotAZone()
        {
            var slow = Asset<SlowAbilitySO>();
            Assert.AreEqual(AreaEffectKind.None, slow.AreaKind, "no lasting area: the slow is sent to each victim at the cast");
            Assert.IsFalse(slow.PlacesZone);
            Assert.IsFalse(slow.TryGetAreaEffect(out _), "no zone spec");
            Assert.AreEqual(ControlKind.Slow, slow.EmittedControl.Kind);
            Assert.AreEqual(slow.SlowSeconds, slow.EmittedControl.Seconds, 0f);
            Assert.AreEqual(slow.SlowFactor, slow.EmittedControl.SlowFactor, 0f);
            Assert.IsTrue(slow.TargetEffectIsPurelyControl, "a slow is the whole target effect, so the shield walk / egg greys it correctly");
            Assert.IsTrue(slow.RequiresEnemyInRange, "a burst: with no rival in reach it is a whiff");
        }

        [Test]
        public void Slow_RangeOnly_IsAPlanarCircleWithNoOffset()
        {
            var slow = Asset<SlowAbilitySO>();
            Assert.AreEqual(AbilityAimShape.SelfCircle, slow.AimShape, "RangeOnly keeps a plain circle on the caster");
            Assert.AreEqual(slow.Radius, slow.AimRadius, 0f);
            Assert.AreEqual(slow.Radius, slow.IndicatorRange, 0f, "the range telegraph is the reach");
            Assert.AreEqual(0f, slow.AimForwardOffset, 0f);
            Assert.AreEqual(AbilityJob.Slow, slow.Job);
            Assert.AreEqual(BotRole.Control, slow.ResolveBotRole(), "bots use it as a control ability");
        }

        [Test]
        public void Slow_GoesThroughTheTimedSlowPath_NoNewRpc()
        {
            // The emission is a ControlKind.Slow sent by SendControl, which is RPC_ApplyAbilitySlow: the path that already
            // honours Slippery, Relentless, Bulwark, the shield walk (slow immunity) and the base wall (JudgeControl).
            string src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Abilities/SlowAbilitySO.cs");
            StringAssert.Contains("GatherTargets(caster, _scratch);", src);
            StringAssert.Contains("SendControl(caster, _scratch[i]);", src);
            string cc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            StringAssert.Contains("case ControlKind.Slow: victim.RPC_ApplyAbilitySlow(seconds, emission.SlowFactor, caster); break;", cc);
            Assert.IsNotNull(Asset<SlowAbilitySO>());
        }

        [Test]
        public void TimedSlow_IsRefusedDuringTheShieldWalkAndTheEgg_SourceGuard()
        {
            // The receiver refuses a timed slow while slow-immune; the egg is control-immune, which implies slow immunity.
            string cc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            StringAssert.Contains("duration = IsSlowImmune ? 0f : ApplyPassiveControlDuration(duration, ControlKind.Slow);", cc);
            StringAssert.Contains("IsControlImmune || (Runner != null && EffectWindow.IsOpen(SlowImmuneTimer, Runner))", cc);
        }

        [Test]
        public void TimedSlow_SlipperyWeakensAndShortens_RelentlessLengthensOutgoing_BulwarkShortensIncoming()
        {
            var slippery = Passive<SlipperyPassiveSO>("Slippery");
            var relentless = Passive<RelentlessPassiveSO>("Relentless");
            var bulwark = Passive<BulwarkPassiveSO>("Bulwark");
            float three = Asset<SlowAbilitySO>().SlowSeconds;

            Assert.AreEqual(three * slippery.Resist, slippery.ModifyControlDuration(three, ControlKind.Slow, null), 1e-4f, "Slippery: 40% shorter");
            Assert.AreEqual(1f - (1f - 0.55f) * slippery.Resist, slippery.ModifyIncomingSlowFactor(0.55f, null), 1e-4f, "Slippery: 40% weaker");
            Assert.AreEqual(three * relentless.OutgoingControlMultiplier,
                AbilityBaseSO.ResolveOutgoingControlSeconds(three, relentless), 1e-4f, "Relentless: outgoing slow x1.3");
            Assert.AreEqual(three * relentless.DurationMultiplier, relentless.ModifyControlDuration(three, ControlKind.Slow, null), 1e-4f,
                "Relentless: incoming timed slow x0.75");
            Assert.AreEqual(three * bulwark.DurationMultiplier, bulwark.ModifyControlDuration(three, ControlKind.Slow, null), 1e-4f,
                "Bulwark: incoming timed slow x0.65");
        }

        // ---- Slow Area ---------------------------------------------------------------------------------------------------

        [Test]
        public void SlowArea_ShippedValues_AreTheAuraPlaceholders()
        {
            var area = Asset<FeatherAuraAbilitySO>();
            Assert.AreEqual("Slow Area", area.DisplayName);
            Assert.AreEqual("AREA", area.ShortLabel);
            Assert.AreEqual(AreaEffectKind.Aura, area.AreaKind, "the shipped default is the aura");
            Assert.AreEqual(5f, area.Duration, 1e-4f, "placeholder lifetime");
            Assert.AreEqual(7f, area.Cooldown, 1e-4f, "placeholder cooldown");
            Assert.AreEqual(6.48f, area.AuraRadius, 1e-4f);
            Assert.AreEqual(0.55f, area.AuraSlowFactor, 1e-4f);
            Assert.AreEqual(AbilityJob.SlowArea, area.Job);
        }

        [Test]
        public void SlowArea_Aura_FollowsTheCaster_AndEndsWithTheSlot()
        {
            var area = Asset<FeatherAuraAbilitySO>();
            Assert.IsFalse(area.IsPlaced);
            Assert.AreEqual(AbilityAimShape.Aura, area.AuthoredAimShape);
            Assert.IsTrue(area.TryGetAreaEffect(out var spec));
            Assert.AreEqual(AreaEffectKind.Aura, spec.Kind);
            Assert.AreEqual(ZoneAnchor.Caster, spec.Anchor);
            Assert.IsTrue(spec.EndsWithAbility);
            Assert.AreEqual(area.Duration, spec.Lifetime, 1e-4f);
        }

        [Test]
        public void SlowArea_PlacedZone_StaysWhereCast_WithNoForwardOffset()
        {
            var area = ScriptableObject.CreateInstance<FeatherAuraAbilitySO>();
            try
            {
                area.AreaKind = AreaEffectKind.StaticAoE;
                Assert.IsTrue(area.IsPlaced);
                Assert.AreEqual(AbilityJob.SlowArea, area.Job, "the placed zone is still the Slow Area job");
                Assert.IsTrue(area.PlacesZone);
                Assert.AreEqual(AbilityAimShape.SelfCircle, area.AimShape, "centred on the caster under RangeOnly");
                Assert.AreEqual(0f, area.AimForwardOffset, 0f, "no forward offset");
                Assert.IsTrue(area.TryGetAreaEffect(out var spec));
                Assert.AreEqual(AreaEffectKind.StaticAoE, spec.Kind);
                Assert.AreEqual(ZoneAnchor.World, spec.Anchor, "stays on the ground, it does not follow the caster");
                Assert.AreEqual(ZoneEffect.Slow, spec.Effect);
                Assert.AreEqual(AreaEffectChannel.WhileInside, spec.Channels);
                Assert.AreEqual(0f, spec.ForwardOffset, 0f);
                Assert.IsFalse(spec.EndsWithAbility, "a placed zone lives its own lifetime, not the slot's");
                Assert.AreEqual(area.AuraRadius, spec.Radius, 1e-4f);
                Assert.AreEqual(area.AuraSlowFactor, spec.SlowFactor, 1e-4f);
            }
            finally { Object.DestroyImmediate(area); }
        }

        [Test]
        public void SlowArea_TheAuraRingDrawsOnlyForAnAura_SourceGuard()
        {
            string src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/SlowAuraFeedback.cs");
            StringAssert.Contains("s.Is(AbilityJob.SlowArea) && s.AreaKind == AreaEffectKind.Aura", src);
        }

        // ---- kits ---------------------------------------------------------------------------------------------------------

        [Test]
        public void Kits_RelentlessFeatherfootSlippery_CarryTheInstantSlow_InSlotTwo()
        {
            var slow = Asset<SlowAbilitySO>();
            foreach (var name in new[] { "Relentless", "Featherfoot", "Slippery" })
            {
                var kit = Passive(name).Kit;
                Assert.AreEqual(slow, kit[2], $"{name}: the instant Slow stays in slot 3 (index 2)");
                Assert.IsFalse(kit.Any(a => a is FeatherAuraAbilitySO), $"{name}: no Slow Area in its kit");
            }
        }

        [Test]
        public void Kits_HoarderAndThief_CarrySlowArea_AndNoInstantSlow()
        {
            var area = Asset<FeatherAuraAbilitySO>();
            foreach (var name in new[] { "Hoarder", "Thief" })
            {
                var kit = Passive(name).Kit;
                Assert.IsTrue(kit.Contains(area), $"{name}: carries Slow Area");
                Assert.IsFalse(kit.Any(a => a is SlowAbilitySO), $"{name}: no instant Slow");
            }
            Assert.AreEqual(area, Passive("Hoarder").Kit[1], "Hoarder: slot 2 (index 1), the Fatty's second attack (2026-11-20 slot roles)");
            Assert.AreEqual(area, Passive("Thief").Kit[3], "Thief: slot 4 (index 3) as today");
        }

        [Test]
        public void Kits_FeatherfootStillEqualsSlippery_AndBothAreLegal()
        {
            CollectionAssert.AreEqual(Passive("Slippery").Kit, Passive("Featherfoot").Kit);
            Assert.IsEmpty(FixedKit.Problems(Passive("Featherfoot")));
            Assert.IsEmpty(FixedKit.Problems(Passive("Slippery")));
            Assert.IsEmpty(FixedKit.Problems(Passive("Relentless")));
            Assert.IsEmpty(FixedKit.Problems(Passive("Hoarder")));
            Assert.IsEmpty(FixedKit.Problems(Passive("Thief")));
        }

        [Test]
        public void Pool_HasBothSlowsRegistered_OneEachJob()
        {
            var registry = TestAssets.Load<CluckWars.Gameplay.AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            Assert.AreEqual(1, registry.All.Count(a => a is SlowAbilitySO), "one instant Slow in the registry");
            Assert.AreEqual(1, registry.All.Count(a => a is FeatherAuraAbilitySO), "one Slow Area in the registry");
            Assert.AreEqual(1, registry.All.Count(a => a.Job == AbilityJob.Slow));
            Assert.AreEqual(1, registry.All.Count(a => a.Job == AbilityJob.SlowArea));
        }
    }
}
