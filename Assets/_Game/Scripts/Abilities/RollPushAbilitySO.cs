using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Roll &amp; Push — Control ability. Rolls the caster forward and pushes any
    /// chicken in the path away. <b>No damage.</b> Uses
    /// <see cref="ChickenController.RPC_ApplyKnockback"/> so Fatty's Immovable
    /// passive naturally reduces the push on Fatty targets.
    /// </summary>
    /// <remarks>
    /// The caster gets a brief <see cref="ChickenController.MoveSpeedMultiplier"/>
    /// boost during the roll window (duration defined by the base class). The push
    /// fires once at activation time — not per-tick — so this is a burst push, not
    /// a continuous one.
    ///
    /// Cooldown tier: Short (3–6 s). Default values are balance-pass placeholders.
    /// </remarks>
    [CreateAssetMenu(fileName = "RollPush",
        menuName = "Cluck Wars/Ability/Control/Roll and Push", order = 8)]
    public sealed class RollPushAbilitySO : AbilityBaseSO
    {
        [Tooltip("How far ahead the push sweep is centered.")]
        [Min(0.5f)] public float ForwardOffset = 1.2f;

        [Tooltip("Radius of the push sweep.")]
        [Min(0.5f)] public float PushRadius = 1.8f;

        [Tooltip("Knockback impulse strength (world-units/sec).")]
        [Min(1f)] public float PushStrength = 10f;

        [Tooltip("Speed multiplier applied to the caster while rolling.")]
        [Range(1f, 4f)] public float RollSpeedMultiplier = 2.0f;

        protected override string DefaultIcon => "🌀";

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            // Caster rolls faster.
            caster.MoveSpeedMultiplier = RollSpeedMultiplier;

            // Push all chickens in the forward cone.
            var center = caster.transform.position + caster.transform.forward * ForwardOffset;
            var hits = Physics.OverlapSphere(center, PushRadius, SearchMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var targetCtrl = hits[i].GetComponentInParent<ChickenController>();
                if (targetCtrl == null || targetCtrl == caster) continue;
                if (targetCtrl.Combat != null && targetCtrl.Combat.IsDead) continue;

                var dir = (targetCtrl.transform.position - caster.transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = caster.transform.forward;
                targetCtrl.RPC_ApplyKnockback(dir.normalized * PushStrength);
            }
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            // Restore speed when the roll window ends.
            ctx.Controller.MoveSpeedMultiplier = 1f;
        }
    }
}
