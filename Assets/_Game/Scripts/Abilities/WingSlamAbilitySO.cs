using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Warrior Control ability: heavy 1.5s stun in a medium AoE around self.
    /// </summary>
    [CreateAssetMenu(fileName = "WingSlam", menuName = "Cluck Wars/Ability/Control/Wing Slam", order = 10)]
    public sealed class WingSlamAbilitySO : StunBurstAbilitySO
    {
        public WingSlamAbilitySO()
        {
            DisplayName = "Wing Slam";
            ShortLabel = "SLAM";
            Description = "Slams the ground, stunning nearby rivals for 1.5 seconds.";
            AllowedClasses = ChickenClassFlags.Warrior;
            StunRadius = 4.5f;
            StunDuration = 1.5f;
            Duration = 1.5f;
            Cooldown = 12f;
        }

        protected override string DefaultIcon => "💥";

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
