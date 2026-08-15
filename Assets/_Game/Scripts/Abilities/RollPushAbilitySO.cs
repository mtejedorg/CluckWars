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
        public RollPushAbilitySO()
        {
            TerrainTraversal = TerrainTraversal.Barge;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        [Tooltip("Length of the push lane, measured forward from the caster. With a Capsule " +
                 "aim shape this is the axis length, not a detached centre — total forward " +
                 "reach is ForwardOffset + PushRadius.")]
        [Min(0.5f)] public float ForwardOffset = 7.2f;

        [Tooltip("Half-width of the push lane (the capsule's sweep radius).")]
        [Min(0.5f)] public float PushRadius = 1.9f;

        [Tooltip("Knockback impulse strength (world-units/sec). Scaled 10 -> 13.5 (x1.35) with the 2026-08-14 arena/move-speed rescale — see CluckShockAbilitySO.KnockbackForce.")]
        [Min(1f)] public float PushStrength = 13.5f;

        [Tooltip("Speed multiplier applied to the caster while rolling.")]
        [Range(1f, 4f)] public float RollSpeedMultiplier = 2.0f;

        protected override string DefaultIcon => "🌀";

        public override float IndicatorRange => ForwardOffset + PushRadius;

        /// <summary>
        /// A swept lane, not a detached disc. The old ForwardCircle centred 1.2 m ahead with
        /// a 1.8 m radius left a dead spot for anything closer than that — you could whiff a
        /// barge on a chicken standing on your toes. The capsule's near cap covers
        /// point-blank (and slightly behind), and the long thin axis matches what a roll
        /// actually does: plough a line, not detonate a circle.
        /// </summary>
        public override AbilityAimShape AimShape => AbilityAimShape.Capsule;
        public override float AimRadius => PushRadius;
        public override float AimForwardOffset => ForwardOffset;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            // Caster rolls faster.
            caster.MoveSpeedMultiplier = RollSpeedMultiplier;

            // Push everyone in the forward sweep.
            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var targetCtrl = _scratch[i];
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
