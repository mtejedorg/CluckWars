using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Warrior Control ability: a 1.3 s stun in a 120° cone ahead (Standard cooldown tier, GDD §7.3).
    /// </summary>
    /// <remarks>
    /// Tuned 2026-10-22 (Maestro, decision A4 = proposal WS-A): cooldown 12 → 8 s, stun and
    /// Duration 1.5 → 1.3 s. With Relentless (×0.75) that is 6 s, about 21.7 % stun uptime per
    /// target. <b>Pre-agreed fallback (WS-B):</b> if the human session reports "stunned too
    /// often", go to cooldown 9 s with StunDuration and Duration 1.5 s (those three fields in
    /// WingSlam.asset, plus these defaults). Keep the asset and these defaults equal so a freshly
    /// created asset agrees.
    /// </remarks>
    [CreateAssetMenu(fileName = "WingSlam", menuName = "Cluck Wars/Ability/Control/Wing Slam", order = 10)]
    public sealed class WingSlamAbilitySO : StunBurstAbilitySO
    {
        public override string DescriptionKey => UiKeys.AbilityWingSlamDesc;

        public WingSlamAbilitySO()
        {
            DisplayName = "Wing Slam";
            ShortLabel = "SLAM";
            Description = "Slams the ground ahead, stunning rivals within 4.5m for 1.3s.";
            AllowedClasses = ChickenClassFlags.Warrior;
            StunRadius = 4.5f;
            StunDuration = 1.3f;
            Duration = 1.3f;
            Cooldown = 8f;
        }

        /// <summary>
        /// Directional, aimable per FEEDBACK.md §4: a 120° forward cone instead of the
        /// SelfCircle Ambush keeps. Its own <c>StunRadius</c> (4.5 m) and therefore the same
        /// AimRadius — this is a shape change, not a reach change, and the area drop is the
        /// price of being able to slam a specific direction.
        /// </summary>
        public override AbilityAimShape AimShape => AbilityAimShape.Cone;
        public override float AimConeAngle => 120f;
    }
}
