using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro's 2026-11-05 Warrior rework: Bully is the cooldown spammer (x0.75, Peck excluded), Relentless is tenacity
    /// (incoming stun / root / timed slow x0.75) plus stronger crowd control (the stuns and slows it applies x1.3).
    /// The outgoing factor is applied on the caster's authority before the RPC, and the receivers' ceilings include the
    /// pool's strongest factor, so a boosted stun is accepted and a claim above the boosted ceiling is still refused.
    /// </summary>
    public sealed class WarriorSpecializationTests
    {
        private const string AbilitiesSrc = "Assets/_Game/Scripts/Abilities";

        private static AbilityRegistrySO Registry => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
        private static T Shipped<T>() where T : PassiveAbilitySO => Registry.Passives.OfType<T>().Single();
        private static T Asset<T>() where T : AbilityBaseSO => TestAssets.LoadAllIn<T>(TestAssets.AbilitiesDir).Single();
        private static string Src(string file) => File.ReadAllText(Path.Combine(AbilitiesSrc, file)).Replace("\r\n", "\n");

        private static ControlClaimRules.Claim Honest(ControlKind kind, float seconds, float factor = 1f) =>
            new ControlClaimRules.Claim
            {
                Kind = kind, Seconds = seconds, SlowFactor = factor,
                CasterResolved = true, SenderSpeaksForCaster = true, PlanarDistance = 1f,
            };

        // ---- Shipped numbers ------------------------------------------------------------------

        [Test]
        public void ShippedNumbers_AreTheRuling_AndTheConstructorDefaultsMatchTheAssets()
        {
            var bully = Shipped<BullyPassiveSO>();
            var relentless = Shipped<RelentlessPassiveSO>();
            Assert.AreEqual(0.75f, bully.CooldownMultiplier, 1e-6f);
            Assert.AreEqual(0.75f, relentless.DurationMultiplier, 1e-6f);
            Assert.AreEqual(1.3f, relentless.OutgoingDurationMultiplier, 1e-6f);

            var freshBully = ScriptableObject.CreateInstance<BullyPassiveSO>();
            var freshRelentless = ScriptableObject.CreateInstance<RelentlessPassiveSO>();
            try
            {
                Assert.AreEqual(bully.CooldownMultiplier, freshBully.CooldownMultiplier, "Bully default vs asset");
                Assert.AreEqual(relentless.DurationMultiplier, freshRelentless.DurationMultiplier, "Relentless incoming default vs asset");
                Assert.AreEqual(relentless.OutgoingDurationMultiplier, freshRelentless.OutgoingDurationMultiplier, "Relentless outgoing default vs asset");
            }
            finally
            {
                Object.DestroyImmediate(freshBully);
                Object.DestroyImmediate(freshRelentless);
            }
        }

        [Test]
        public void Bully_CutsEveryCooldownButPeck_AndTouchesNothingElse()
        {
            var bully = Shipped<BullyPassiveSO>();
            var peck = Asset<PeckAbilitySO>();
            var stun = Asset<WingSlamAbilitySO>();
            Assert.AreEqual(stun.Cooldown * 0.75f, bully.ModifyCooldown(stun.Cooldown, stun, null), 1e-4f);
            Assert.AreEqual(0.8f, bully.ModifyCooldown(0.8f, peck, null), 1e-6f, "Peck's cadence is the Oracle's");
            Assert.AreEqual(5f, bully.ModifyStealAmount(5f, null), "no steal bonus any more");
            Assert.AreEqual(1f, bully.MaxStealMultiplier);
            Assert.AreEqual(14f, bully.ModifyCargoCapacity(14f, null), "no cargo bonus any more");
            Assert.AreEqual(1f, bully.ModifyControlDuration(1f, null));
            Assert.AreEqual(1f, bully.OutgoingControlMultiplier);
        }

        [Test]
        public void Relentless_ShortensIncomingControl_ButNotKnockbackOrCooldowns()
        {
            var relentless = Shipped<RelentlessPassiveSO>();
            var stun = Asset<WingSlamAbilitySO>();
            Assert.AreEqual(0.75f, relentless.ModifyControlDuration(1f, null), 1e-6f);
            Assert.AreEqual(1f, relentless.ModifyKnockback(1f, null), "tenacity is control duration only, no knockback cut");
            Assert.AreEqual(stun.Cooldown, relentless.ModifyCooldown(stun.Cooldown, stun, null), "the cooldown cut moved to Bully");
        }

        [Test]
        public void OnlyRelentless_LengthensOutgoingControl()
        {
            foreach (var p in Registry.Passives)
            {
                if (p is RelentlessPassiveSO) Assert.AreEqual(1.3f, p.OutgoingControlMultiplier, 1e-6f, p.name);
                else Assert.AreEqual(1f, p.OutgoingControlMultiplier, p.name);
            }
            Assert.AreEqual(1.3f, ControlClaimRules.MaxOutgoingControlMultiplier(Registry.All), 1e-6f,
                "the ceiling's factor is read from the shipped passives");
            Assert.AreEqual(1f, ControlClaimRules.MaxOutgoingControlMultiplier(null));
        }

        // ---- Outgoing resolution and the order with the victim's passive -----------------------

        [Test]
        public void ARelentlessStun_OnASlipperyVictim_Is_Authored_x1_3_x0_6()
        {
            var relentless = Shipped<RelentlessPassiveSO>();
            var slippery = Shipped<SlipperyPassiveSO>();
            var stun = Asset<WingSlamAbilitySO>();

            // Caster's authority, before the RPC.
            float sent = AbilityBaseSO.ResolveOutgoingControlSeconds(stun.StunDuration, relentless);
            Assert.AreEqual(1.3f * 1.3f, sent, 1e-4f);
            // Victim's authority, on arrival (ChickenController.ApplyPassiveControlDuration).
            float landed = slippery.ModifyControlDuration(sent, null);
            Assert.AreEqual(1.3f * 1.3f * 0.6f, landed, 1e-4f);

            // A caster with no passive, or a passive without the factor, sends the authored stun.
            Assert.AreEqual(stun.StunDuration, AbilityBaseSO.ResolveOutgoingControlSeconds(stun.StunDuration, (PassiveAbilitySO)null));
            Assert.AreEqual(stun.StunDuration, AbilityBaseSO.ResolveOutgoingControlSeconds(stun.StunDuration, Shipped<BullyPassiveSO>()));
        }

        // ---- Network safety: the receiver ceilings include the factor --------------------------

        [Test]
        public void ABoostedStun_IsAccepted_AndAClaimAboveTheBoostedCeiling_IsRefused()
        {
            var relentless = Shipped<RelentlessPassiveSO>();
            var stun = Asset<WingSlamAbilitySO>();
            var ceiling = ControlClaimRules.CeilingFor(Registry.All, ControlKind.Stun);   // what ChickenController derives

            float boosted = AbilityBaseSO.ResolveOutgoingControlSeconds(stun.StunDuration, relentless);
            Assert.AreEqual(boosted, ceiling.MaxSeconds, 1e-4f, "stun ceiling = longest authored stun x strongest outgoing factor");
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.Evaluate(Honest(ControlKind.Stun, boosted), ceiling));
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration,
                ControlClaimRules.Evaluate(Honest(ControlKind.Stun, boosted + 0.01f), ceiling));
        }

        [Test]
        public void ABoostedTimedSlow_IsAccepted_AndAClaimAboveTheBoostedCeiling_IsRefused()
        {
            // No registered ability sends an RPC slow since 2026-11-05 (the Slow aura is a zone), so the mechanism is
            // checked on the authored Dust Kick plus the shipped Relentless: the day a slow sender is registered, it holds.
            var relentless = Shipped<RelentlessPassiveSO>();
            var kick = Asset<DustKickAbilitySO>();
            var ceiling = ControlClaimRules.CeilingFor(new AbilityBaseSO[] { kick, relentless }, ControlKind.Slow);

            float boosted = AbilityBaseSO.ResolveOutgoingControlSeconds(kick.SlowSeconds, relentless);
            Assert.AreEqual(kick.SlowSeconds * 1.3f, ceiling.MaxSeconds, 1e-4f);
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted,
                ControlClaimRules.Evaluate(Honest(ControlKind.Slow, boosted, kick.SlowFactor), ceiling));
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration,
                ControlClaimRules.Evaluate(Honest(ControlKind.Slow, boosted + 0.01f, kick.SlowFactor), ceiling));

            // The registry itself still refuses every RPC slow: 0 x 1.3 is 0.
            Assert.AreEqual(0f, ControlClaimRules.CeilingFor(Registry.All, ControlKind.Slow).MaxSeconds);
        }

        [Test]
        public void TheRootCeiling_IsNotLengthened()
        {
            // No passive lengthens a root, so its bound (and the Root Egg zone's lifetime evidence) stays the authored one.
            var quake = Asset<GroundQuakeAbilitySO>();
            var ceiling = ControlClaimRules.CeilingFor(new AbilityBaseSO[] { quake, Shipped<RelentlessPassiveSO>() }, ControlKind.Root);
            Assert.AreEqual(quake.EmittedControl.Seconds, ceiling.MaxSeconds, 1e-6f);
        }

        [Test]
        public void EveryStunOrSlowSender_LengthensThroughTheOneChokepoint_BeforeItsRpc()
        {
            foreach (var path in Directory.GetFiles(AbilitiesSrc, "*.cs", SearchOption.AllDirectories))
            {
                // Code only: doc comments name the RPCs too.
                string src = string.Join("\n", File.ReadAllText(path).Replace("\r\n", "\n").Split('\n')
                    .Where(l => !l.TrimStart().StartsWith("//")));
                int rpc = Mathf.Max(src.IndexOf(".RPC_ApplyStun(", System.StringComparison.Ordinal),
                                    src.IndexOf(".RPC_ApplyAbilitySlow(", System.StringComparison.Ordinal));
                if (rpc < 0) continue;
                int resolve = src.IndexOf("ResolveOutgoingControlSeconds(", System.StringComparison.Ordinal);
                Assert.Greater(resolve, 0, $"{Path.GetFileName(path)} sends a stun or slow without the caster's outgoing-control factor");
                Assert.Less(resolve, rpc, $"{Path.GetFileName(path)}: the factor must be resolved before the RPC");
            }
        }

        // ---- The Slow aura -----------------------------------------------------------------------

        [Test]
        public void TheSlowAura_UnderRelentless_RunsAndLives1_3xLonger_WithItsCooldownUnchanged()
        {
            var aura = Asset<FeatherAuraAbilitySO>();
            var relentless = Shipped<RelentlessPassiveSO>();
            Assert.IsTrue(aura.ActiveDurationIsOutgoingControl);
            foreach (var a in Registry.All.Where(a => a != null && !(a is FeatherAuraAbilitySO)))
                Assert.IsFalse(a.ActiveDurationIsOutgoingControl, $"{a.name}: only the aura's run time is its crowd control");

            float run = AbilityBaseSO.ResolveOutgoingControlSeconds(aura.Duration, relentless);
            Assert.AreEqual(3.9f, run, 1e-4f);
            Assert.AreEqual(aura.Cooldown, relentless.ModifyCooldown(aura.Cooldown, aura, null), "Relentless no longer touches cooldowns");
            Assert.Less(run, aura.Cooldown, "the boosted aura still ends before the slot is ready again");
            Assert.AreEqual(aura.Duration, aura.ResolveActiveDuration(null), "no caster: the authored run time");

            // The slot's run timer and the zone's lifetime come from the same resolution, so the zone ends with the slot.
            string ctl = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AbilityController.cs").Replace("\r\n", "\n");
            StringAssert.Contains("float activeSeconds = ability.ResolveActiveDuration(_controller);", ctl);
            StringAssert.Contains("SetActiveTimer(slot, TickTimer.CreateFromSeconds(Runner, activeSeconds));", ctl);
            StringAssert.Contains("var spec = AreaEffectSpec.AuraSlow(AuraRadius, ResolveActiveDuration(caster), AuraSlowFactor);",
                Src("FeatherAuraAbilitySO.cs"));
        }
    }
}
