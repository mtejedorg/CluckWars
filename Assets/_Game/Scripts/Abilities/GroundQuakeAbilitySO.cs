using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Fatty, Bulwark signature.</b> A stomp that pins everyone standing nearby.
    /// </summary>
    /// <remarks>
    /// Roots rather than knocks back, and that is the point. Fatty's essence is "opportunity
    /// and control" — a shove creates distance, which a slow chicken cannot exploit. Pinning
    /// rivals in place lets the slowest thing on the map arrive anyway.
    ///
    /// It is the <b>Bulwark</b> signature because the two Fatty specializations are
    /// deliberately on different axes: Hoarder's signature is economy (a heavy-beakful Peck),
    /// Bulwark's is control. Bulwark also shrugs off incoming control itself, so the build
    /// reads as the immovable object that immobilises everyone else.
    /// </remarks>
    [CreateAssetMenu(fileName = "GroundQuake",
        menuName = "Cluck Wars/Ability/Control/Ground Quake", order = 7)]
    public sealed class GroundQuakeAbilitySO : AbilityBaseSO
    {
        public GroundQuakeAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
            BotRole = BotRole.Control;
            Duration = 0.4f;
            Cooldown = 9f;
        }

        [Tooltip("Radius of the stomp.")]
        [Min(0.5f)] public float QuakeRadius = 4.5f;

        [Tooltip("How long rivals are rooted. Long enough for a slow chicken to close, which "
               + "is the entire reason this roots instead of shoving.")]
        [Min(0.1f)] public float RootSeconds = 1.8f;

        protected override string DefaultIcon => "🌋";

        public override float IndicatorRange => QuakeRadius;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => QuakeRadius;

        // FEEDBACK.md §2.2 / §3.2 case 15. The root is the whole of the target effect, and it funnels through ApplyPassiveControlDuration.
        // See AbilityBaseSO.TargetEffectIsPurelyControl for why this is declared
        // rather than inferred from Category, and why a mixed ability stays false.
        public override bool TargetEffectIsPurelyControl => true;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
                _scratch[i].RPC_ApplyRoot(RootSeconds);
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
