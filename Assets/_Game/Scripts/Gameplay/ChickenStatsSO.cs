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
    }
}
