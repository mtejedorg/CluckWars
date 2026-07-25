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
            StunRadius = 2.5f;
            StunDuration = 1.5f;
            Duration = 1.5f;
            Cooldown = 12f;
        }

        protected override string DefaultIcon => "💥";
    }
}
