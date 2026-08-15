using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Shared base class for AoE-around-self stun abilities (Ambush & Wing Slam).
    /// Calls target.Controller.RPC_ApplyStun(StunDuration) for each rival in radius.
    /// </summary>
    public abstract class StunBurstAbilitySO : AbilityBaseSO
    {
        public StunBurstAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
        }

        [Tooltip("Stun radius around the caster.")]
        [Min(0.5f)] public float StunRadius = 3.6f;

        [Tooltip("Stun duration applied to hit rivals.")]
        [Min(0.1f)] public float StunDuration = 1.0f;

        public override float IndicatorRange => StunRadius;
        public override bool RequiresEnemyInRange => true;

        // Ambush is a plain circle around the caster and now says so directly, instead of
        // declaring a Cone and leaning on the 360°-degenerates-to-a-circle rule to undo it.
        // (That rule stays in AbilityAim, and stays tested — it is defence-in-depth against
        // a hand-authored 360 in an asset, not something an ability should be routed
        // through on purpose.) Wing Slam overrides both AimShape and AimConeAngle for its
        // real directional cone; AimRadius stays here so both subclasses mirror StunRadius.
        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => StunRadius;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                _scratch[i].RPC_ApplyStun(StunDuration);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
