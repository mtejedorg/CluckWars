using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Shove</b>, the one shove job (Maestro 2026-11-05): knocks every rival within <see cref="ShockRadius"/> away
    /// from the caster. Already a caster-centred circle, so the targeting mode does not change it.
    /// </summary>
    [CreateAssetMenu(fileName = "CluckShock",
        menuName = "Cluck Wars/Ability/Control/Cluck Shock", order = 6)]
    public sealed class CluckShockAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityCluckShockDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("r", Num(ShockRadius)) };

        public CluckShockAbilitySO()
        {
            DisplayName = "Shove";
            ShortLabel = "PUSH";
            Description = "Shoves every rival within 5.4m away.";
            Category = AbilityCategory.Control;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 0.5f;
            Cooldown = 7f;
        }

        [Tooltip("Shove radius around the caster. 5.4 = 4.5 x1.2 (Maestro 2026-11-05).")]
        [Min(0.5f)] public float ShockRadius = 5.4f;

        [Tooltip("Knockback impulse (world-units/sec) pushing rivals away. Scaled 12 -> 16.2 (x1.35) with the 2026-08-14 arena/move-speed rescale: this is a velocity added on top of the victim's own movement, so holding it while every MoveSpeed rose 35% would have quietly turned a shove into a nudge the victim can walk out of.")]
        [Min(1f)] public float KnockbackForce = 16.2f;

        public override float IndicatorRange => ShockRadius;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => ShockRadius;

        // FEEDBACK.md §2.2 / §3.2 case 15. Knockback and nothing else, so an Immovable rival is shoved by nothing at all.
        // See AbilityBaseSO.TargetEffectIsPurelyControl for why this is declared
        // rather than inferred from Category, and why a mixed ability stays false.
        public override bool TargetEffectIsPurelyControl => true;

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
