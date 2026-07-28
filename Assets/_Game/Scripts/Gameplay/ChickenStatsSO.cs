using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Per-class tunables. One asset per class lives in /Assets/_Game/Data/Classes/.
    /// </summary>
    /// <remarks>
    /// v0.3: <c>Attack</c>, <c>AttackRange</c>, <c>AttackCooldown</c> removed (basic attack
    /// deleted). <c>AvailableAbilities</c> / <c>Allows()</c> removed — all abilities are
    /// available to all classes (GDD §7.1). <c>Passive</c> field added.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ChickenStats",
        menuName = "Cluck Wars/Chicken Stats",
        order = 0)]
    public sealed class ChickenStatsSO : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Warrior";

        [Header("Passive")]
        [Tooltip("This class's unique passive mechanic (GDD v0.3 §5.2). Set in the Inspector after recompile.")]
        public ChickenPassive Passive = ChickenPassive.None;

        [Header("Movement")]
        [Min(0f)] public float MoveSpeed = 4f;
        [Min(0f)] public float TurnSpeed = 720f; // deg/sec

        [Header("Cargo")]
        [Min(1)] public int CargoCapacity = 10;
        [Min(0f)] public float CollectionRate = 1f; // food per second

        [Header("Visuals")]
        [Tooltip("Uniform scale applied to the chicken's transform at spawn.")]
        [Min(0.1f)] public float Scale = 1f;
    }
}
