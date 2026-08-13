using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy signature.</b> Control effects wear off faster. Also shortens the window
    /// the Assassin's execute has to arm on you, which is why it is the class's default —
    /// Speedy's fantasy is being hard to pin down, not being hard to hit.
    /// </summary>
    [CreateAssetMenu(fileName = "Slippery", menuName = "Cluck Wars/Passive/Slippery", order = 10)]
    public sealed class SlipperyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on incoming slow / root / stun duration. 0.6 = 40% shorter.")]
        [Range(0.1f, 1f)] public float DurationMultiplier = 0.6f;

        public SlipperyPassiveSO()
        {
            DisplayName = "Slippery";
            ShortLabel = "SLIP";
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        public override bool IsSignature => true;
        protected override string DefaultIcon => "\U0001F4A8";

        public override float ModifyControlDuration(float seconds, ChickenController self) =>
            seconds * DurationMultiplier;
    }
}
