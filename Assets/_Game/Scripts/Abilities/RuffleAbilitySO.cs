using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior.</b> A short burst of pace. Enough to close a gap or leave one.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately a worse Speed Burst.</b> Speed Burst runs at 2.5x; this runs at 1.5x for
    /// a shorter window. Speedy is <i>defined</i> as the fastest chicken in the roster, so a
    /// Warrior sprint that rivalled it would erase the one thing that class owns outright.
    ///
    /// <b>Grants no traversal, and must not.</b> <c>TerrainTraversal</c> stays None and there is
    /// no <c>JumpTier</c> — this is pace on the ground, so the map still constrains the Warrior
    /// exactly as before. That is also what keeps it distinct from Dive Bomb, which is the
    /// Warrior's actual gap-closer and pays for its Vault with a much longer cooldown.
    /// </remarks>
    [CreateAssetMenu(fileName = "Ruffle",
        menuName = "Cluck Wars/Ability/Utility/Ruffle", order = 7)]
    public sealed class RuffleAbilitySO : AbilityBaseSO
    {
        public RuffleAbilitySO()
        {
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
            BotRole = BotRole.Escape;
            Duration = 2.5f;
            Cooldown = 5f;
        }

        [Tooltip("Movement speed multiplier while active. 1.5 against Speed Burst's 2.5 — the "
               + "Warrior is never allowed to out-sprint the class built around sprinting.")]
        [Range(1.05f, 2f)] public float SpeedMultiplier = 1.5f;

        protected override string DefaultIcon => "💨";

        // Self-buff: nothing to aim at, so the indicator marks the caster's own ring
        // rather than a target area (FEEDBACK.md §2.2).
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = SpeedMultiplier;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = 1f;
        }
    }
}
