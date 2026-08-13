using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin Utility ability: short Blink dash phasing over walls along facing direction.
    /// </summary>
    [CreateAssetMenu(fileName = "Shadowstep", menuName = "Cluck Wars/Ability/Utility/Shadowstep", order = 11)]
    public sealed class ShadowstepAbilitySO : AbilityBaseSO
    {
        public ShadowstepAbilitySO()
        {
            DisplayName = "Shadowstep";
            ShortLabel = "STEP";
            Description = "Short blink dash phasing over walls along facing direction.";
            Category = AbilityCategory.Utility;
            TerrainTraversal = TerrainTraversal.Blink;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
            Duration = 0.4f;
            Cooldown = 6f;
        }

        [Tooltip("Speed multiplier applied during the blink dash.")]
        [Range(1.5f, 4f)] public float SpeedMultiplier = 3.0f;

        protected override string DefaultIcon => "👤";

        public override AbilityAimShape AimShape => AbilityAimShape.Jump;
        public override float AimRadius => JumpLandingRadius;
        public override float AimForwardOffset => JumpResolver.GetNominalDistance(JumpTier);

        /// <summary>
        /// Shadowstep touches nobody — it is a pure mobility cast. It inherited
        /// <c>AffectsEnemies = true</c>, so any rival who happened to be standing in the
        /// landing ring was counted in <c>LastCastHitCount</c> and the impact system styled
        /// the blink as a landed attack. That is a descriptor lie in the same family as the
        /// ones Stage 1 removed.
        ///
        /// <b>Why it does not simply flip both to false.</b> <see cref="AimShape"/> is
        /// <c>Jump</c> (a real, drawable landing ring the player needs to see), so
        /// <see cref="AbilityBaseSO.ReportsCastHits"/> is true and cannot be turned off
        /// without also giving up the telegraph. With <c>AffectsSelf</c> false as well,
        /// <c>GatherTargets</c> would return 0 on <i>every single cast</i> and the §3.3 grey
        /// whiff ring would fire every time — trading a phantom hit for a permanent phantom
        /// miss. Marking the caster instead makes the count honest: the one chicken this
        /// cast affects is the caster, so it reports exactly 1.
        ///
        /// <b>This depends on the asset's JumpTier staying None.</b> At JumpTier 0 the
        /// landing ring is centred on the caster (offset 0, radius 1 m), so the caster is
        /// inside their own shape. Raising JumpTier would push the ring away from them and
        /// silently drop the count back to 0 — pinned by
        /// <c>AbilityAimTests.Shadowstep_AlwaysReportsItsOwnCasterAsTheHit</c>.
        /// </summary>
        public override bool AffectsEnemies => false;
        public override bool AffectsSelf => true;

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
