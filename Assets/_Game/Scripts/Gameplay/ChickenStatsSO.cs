using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Per-class tunables. One asset per class lives in /Assets/_Game/Data/Classes/.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ChickenStats",
        menuName = "Cluck Wars/Chicken Stats",
        order = 0)]
    public sealed class ChickenStatsSO : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Warrior";

        [Header("Movement")]
        [Min(0f)] public float MoveSpeed = 4f;
        [Min(0f)] public float TurnSpeed = 720f; // deg/sec

        [Header("Combat")]
        [Min(1f)] public float MaxHP = 100f;
        [Min(0f)] public float Attack = 10f;
        [Min(0f)] public float AttackRange = 1.5f;
        [Min(0f)] public float AttackCooldown = 0.4f;

        [Header("Cargo")]
        [Min(1)] public int CargoCapacity = 10;
        [Min(0f)] public float CollectionRate = 1f; // food per second

        [Header("Abilities")]
        [Tooltip("Pool of abilities this class is allowed to equip (GDD §7.1). Empty = no restriction (any ability can be equipped). Filled during balance pass.")]
        public AbilityBaseSO[] AvailableAbilities;

        /// <summary>
        /// True if <paramref name="ability"/> can be equipped on a chicken of this
        /// class, or if the allowlist is empty (no restriction).
        /// </summary>
        public bool Allows(AbilityBaseSO ability)
        {
            if (AvailableAbilities == null || AvailableAbilities.Length == 0) return true;
            for (int i = 0; i < AvailableAbilities.Length; i++)
            {
                if (AvailableAbilities[i] == ability) return true;
            }
            return false;
        }
    }
}
