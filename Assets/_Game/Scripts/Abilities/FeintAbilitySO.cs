using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy.</b> A hard sidestep — instant lateral displacement, no wind-up.
    /// </summary>
    /// <remarks>
    /// <b>This is a dodge, NOT a traversal ability, and the distinction is load-bearing.</b>
    /// Maestro: Speedy should "never jump obstacles, because it would be unfair." So the
    /// sidestep is delivered as a knockback impulse on the caster rather than a teleport:
    /// an impulse is resolved by the <c>CharacterController</c> against real geometry, so a
    /// wall stops it exactly as it stops running. A teleport would not, and would have handed
    /// Speedy the terrain-skipping this class is specifically denied.
    ///
    /// <c>TerrainTraversal</c> stays None and there is no <c>JumpTier</c>, so
    /// <c>DataIntegrityTests.Speedy_HasNoTerrainTraversalAbilityAvailableToIt</c> stays green —
    /// and would fail loudly if someone later "upgraded" this to a blink.
    ///
    /// Direction is lateral to facing, not to input, so it reads as a juke rather than a
    /// second movement stick. Which side is chosen by the caster's current strafe intent;
    /// with none, it defaults to the right.
    /// </remarks>
    [CreateAssetMenu(fileName = "Feint",
        menuName = "Cluck Wars/Ability/Utility/Feint", order = 7)]
    public sealed class FeintAbilitySO : AbilityBaseSO
    {
        public FeintAbilitySO()
        {
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Speedy;
            BotRole = BotRole.Escape;
            Duration = 0.2f;
            Cooldown = 5f;
        }

        [Tooltip("Lateral impulse, in world-units/sec. Tuned to move roughly a body-and-a-half "
               + "before decay — enough to leave a grab, not enough to cross a corridor.")]
        [Min(1f)] public float SidestepImpulse = 11f;

        protected override string DefaultIcon => "↔️";

        // Self-only: nothing is aimed at, so the indicator marks the caster's own ring.
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            var right = caster.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f) return;

            // RPC_ApplySelfImpulse, not RPC_ApplyKnockback — same impulse, same physics, same
            // wall-stops-it delivery the remarks above insist on. What changes is that peers
            // observe it on SelfImpulseEventId instead of KnockbackEventId, so HitFeedback and
            // ControlStateVFX do not fire the victim beat on it. On the knockback byte, Speedy's
            // own dodge played the white body flash, the recoil and the victim-tier camera
            // shake with no attacker to attribute it to — flash + recoil + big shake + no
            // direction is exactly how the game says "you were hit from off-screen".
            caster.RPC_ApplySelfImpulse(right.normalized * SidestepImpulse);
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
