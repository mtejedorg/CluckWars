using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin Control ability: precision 1.0s stun in a small AoE around self.
    /// </summary>
    [CreateAssetMenu(fileName = "Ambush", menuName = "Cluck Wars/Ability/Control/Ambush", order = 9)]
    public sealed class AmbushAbilitySO : StunBurstAbilitySO
    {
        public override string DescriptionKey => UiKeys.AbilityAmbushDesc;

        public AmbushAbilitySO()
        {
            DisplayName = "Ambush";
            ShortLabel = "AMB";
            Description = "Stuns rivals within 3.6m for 1s.";
            AllowedClasses = ChickenClassFlags.Assassin;
            StunRadius = 3.6f;
            StunDuration = 1.0f;
            Duration = 1.0f;
            Cooldown = 10f;
        }

    }
}
