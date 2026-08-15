using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin Control ability: precision 1.0s stun in a small AoE around self.
    /// </summary>
    [CreateAssetMenu(fileName = "Ambush", menuName = "Cluck Wars/Ability/Control/Ambush", order = 9)]
    public sealed class AmbushAbilitySO : StunBurstAbilitySO
    {
        public AmbushAbilitySO()
        {
            DisplayName = "Ambush";
            ShortLabel = "AMB";
            Description = "Stuns nearby rivals for 1 second.";
            AllowedClasses = ChickenClassFlags.Assassin;
            StunRadius = 3.6f;
            StunDuration = 1.0f;
            Duration = 1.0f;
            Cooldown = 10f;
        }

        protected override string DefaultIcon => "🗡️";
    }
}
