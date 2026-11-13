using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Stun</b>, the one stun job (Maestro 2026-11-05): every rival within <c>StunRadius</c> of the caster is stunned
    /// (RangeOnly). The 120° cone ahead is the authored Shaped answer. Sets the same <c>ChickenController.IsStunned</c> as
    /// every stun (it is a <see cref="StunBurstAbilitySO"/>, like the retired Ambush), so it is the Spoiler's stun for the
    /// Death Mark kill.
    /// </summary>
    /// <remarks>
    /// Tuned 2026-10-22 (Maestro, decision A4 = proposal WS-A): cooldown 12 → 8 s, stun and
    /// Duration 1.5 → 1.3 s. With Bully (×0.75 cooldown) that is 6 s, about 21.7 % stun uptime per
    /// target. <b>Pre-agreed fallback (WS-B):</b> if the human session reports "stunned too
    /// often", go to cooldown 9 s with StunDuration and Duration 1.5 s (those three fields in
    /// WingSlam.asset, plus these defaults). Keep the asset and these defaults equal so a freshly
    /// created asset agrees.
    /// </remarks>
    [CreateAssetMenu(fileName = "WingSlam", menuName = "Cluck Wars/Ability/Control/Wing Slam", order = 10)]
    public sealed class WingSlamAbilitySO : StunBurstAbilitySO
    {
        public override AbilityJob Job => AbilityJob.Stun;

        public override string DescriptionKey => UiKeys.AbilityWingSlamDesc;

        public WingSlamAbilitySO()
        {
            DisplayName = "Stun";
            ShortLabel = "STUN";
            Description = "Stuns every rival within 5.4m for 1.3s.";
            AllowedClasses = ChickenClassFlags.All;
            StunRadius = 5.4f;
            StunDuration = 1.3f;
            Duration = 1.3f;
            Cooldown = 7f;
        }

        /// <summary>
        /// The authored shape, read only under <see cref="TargetingMode.Shaped"/>: a 120° forward cone of
        /// <c>StunRadius</c>. Under RangeOnly it is a circle of the same radius round the caster.
        /// </summary>
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.Cone;
        public override float AuthoredAimConeAngle => 120f;
    }
}
