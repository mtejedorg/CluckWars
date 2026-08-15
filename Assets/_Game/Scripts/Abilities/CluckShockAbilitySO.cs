using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "CluckShock",
        menuName = "Cluck Wars/Ability/Control/Cluck Shock", order = 6)]
    public sealed class CluckShockAbilitySO : AbilityBaseSO
    {
        public CluckShockAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        [Tooltip("Shock radius around the caster.")]
        [Min(0.5f)] public float ShockRadius = 4.5f;

        [Tooltip("Knockback impulse (world-units/sec) pushing rivals away. Scaled 12 -> 16.2 (x1.35) with the 2026-08-14 arena/move-speed rescale: this is a velocity added on top of the victim's own movement, so holding it while every MoveSpeed rose 35% would have quietly turned a shove into a nudge the victim can walk out of.")]
        [Min(1f)] public float KnockbackForce = 16.2f;

        protected override string DefaultIcon => "⚡";

        public override float IndicatorRange => ShockRadius;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => ShockRadius;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var center = caster.transform.position;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var targetCtrl = _scratch[i];
                var dir = (targetCtrl.transform.position - center);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = caster.transform.forward;
                targetCtrl.RPC_ApplyKnockback(dir.normalized * KnockbackForce);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
