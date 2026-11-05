using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Jump</b> (Maestro 2026-11-05): short Blink dash phasing over walls along the movement direction. Never on a
    /// Speedy (standing ruling: the SCT trip is measured without terrain traversal), hence its AllowedClasses.
    /// </summary>
    [CreateAssetMenu(fileName = "Shadowstep", menuName = "Cluck Wars/Ability/Utility/Shadowstep", order = 11)]
    public sealed class ShadowstepAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityShadowstepDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("mult", Num(SpeedMultiplier)), ("secs", Num(Duration)) };

        public ShadowstepAbilitySO()
        {
            DisplayName = "Jump";
            ShortLabel = "JUMP";
            Description = "Dashes forward at 3x speed for 0.18s, straight over walls.";
            Category = AbilityCategory.Utility;
            TerrainTraversal = TerrainTraversal.Blink;
            AllowedClasses = ChickenClassFlags.Warrior | ChickenClassFlags.Fatty | ChickenClassFlags.Assassin;
            Duration = 0.18f;
            Cooldown = 4f;
        }

        [Tooltip("Speed multiplier applied during the blink dash.")]
        [Range(1.5f, 4f)] public float SpeedMultiplier = 3.0f;

        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.Jump;

        /// <summary>A movement ability: its direction follows where the player moves, never the aim
        /// (Phase 6 chunk 5, A7).</summary>
        public override bool FollowsMovementNotAim => true;
        public override float AimRadius => JumpLandingRadius;
        public override float AuthoredAimForwardOffset => JumpResolver.GetNominalDistance(JumpTier);

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
            ctx.Controller.Effects.SetMoveSpeed(ctx.Slot, SpeedMultiplier);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.RemoveMoveSpeed(ctx.Slot);
        }
    }
}
