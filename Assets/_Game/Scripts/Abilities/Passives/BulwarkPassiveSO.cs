using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Fatty alternative.</b> Shrugs off control: shorter slows and roots, and far less
    /// knockback. The immovable-object build, as opposed to Hoarder's one-trip payload.
    /// </summary>
    [CreateAssetMenu(fileName = "Bulwark", menuName = "Cluck Wars/Passive/Bulwark", order = 14)]
    public sealed class BulwarkPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on incoming slow / root / stun duration.")]
        [Range(0.1f, 1f)] public float DurationMultiplier = 0.65f;

        [Tooltip("Multiplier on incoming knockback impulse. Inherits the old Immovable passive's role.")]
        [Range(0f, 1f)] public float KnockbackMultiplier = 0.25f;

        public BulwarkPassiveSO()
        {
            DisplayName = "Bulwark";
            ShortLabel = "BULW";
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        protected override string DefaultIcon => "\U0001F6E1";

        public override float ModifyControlDuration(float seconds, ChickenController self) =>
            seconds * DurationMultiplier;

        public override float ModifyKnockback(float strength, ChickenController self) =>
            strength * KnockbackMultiplier;
    }
}
