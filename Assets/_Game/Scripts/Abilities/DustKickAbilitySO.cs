using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy.</b> Kicks a spray of dust backwards, slowing whoever is chasing.
    /// </summary>
    /// <remarks>
    /// <b>It fires BEHIND the caster, which is the whole design.</b> Every other cone in the
    /// game points where you are looking; this one points where you came from. Speedy's essence
    /// is escape and denial-of-pursuit, and a forward cone would have made it a duelling tool —
    /// the opposite of the fantasy. Running away and hitting the button is the correct play.
    ///
    /// <b>Grants no traversal, and must not.</b> Maestro: Speedy should "never jump obstacles,
    /// because it would be unfair". Speedy already answers escape (highest MoveSpeed) and denial
    /// (this, Feather Aura, Feather Trap); terrain traversal would be a third currency with
    /// nothing traded for it. Pinned by
    /// <c>DataIntegrityTests.Speedy_HasNoTerrainTraversalAbilityAvailableToIt</c>.
    ///
    /// Implemented as a <see cref="AbilityAimShape.SelfCircle"/> plus a rear-arc filter rather
    /// than a Cone, because the aim shapes resolve a cone around <c>transform.forward</c> and
    /// there is no "backward cone" shape. The indicator therefore draws a ring; the filter is
    /// what makes it directional.
    /// </remarks>
    [CreateAssetMenu(fileName = "DustKick",
        menuName = "Cluck Wars/Ability/Control/Dust Kick", order = 7)]
    public sealed class DustKickAbilitySO : AbilityBaseSO
    {
        public DustKickAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Speedy;
            BotRole = BotRole.Escape;
            Duration = 0.3f;
            Cooldown = 6f;
        }

        [Tooltip("How far back the dust reaches.")]
        [Min(0.5f)] public float Reach = 4.5f;

        [Tooltip("Half-angle of the REAR arc, in degrees. 75 covers a pursuer who has drifted "
               + "off the direct line without catching someone alongside you.")]
        [Range(20f, 120f)] public float RearArcAngle = 75f;

        [Tooltip("Slow multiplier applied to anyone caught. 0.55 = 45% slower.")]
        [Range(0.1f, 1f)] public float SlowFactor = 0.55f;

        [Tooltip("How long the slow lasts.")]
        [Min(0.1f)] public float SlowSeconds = 2.5f;

        protected override string DefaultIcon => "🌫️";

        public override float IndicatorRange => Reach;

        /// <summary>
        /// False: this is an escape tool. Refusing to fire because nobody is close enough yet is
        /// exactly the moment a fleeing Speedy needs it — see <see cref="ZeroHitsIsAWhiff"/>.
        /// </summary>
        public override bool RequiresEnemyInRange => false;

        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => Reach;

        /// <summary>Only rivals in the rear arc eat the dust.</summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            var toTarget = candidate.transform.position - caster.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return false;

            var back = -caster.transform.forward;
            back.y = 0f;

            float angle = Vector3.Angle(back, toTarget.normalized);
            return angle <= RearArcAngle;
        }

        // FEEDBACK.md §2.2 / §3.2 case 15. The slow is the whole of the target effect, and it funnels through ApplyPassiveControlDuration.
        // See AbilityBaseSO.TargetEffectIsPurelyControl for why this is declared
        // rather than inferred from Category, and why a mixed ability stays false.
        public override bool TargetEffectIsPurelyControl => true;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
                _scratch[i].RPC_ApplyAbilitySlow(SlowSeconds, SlowFactor);
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
