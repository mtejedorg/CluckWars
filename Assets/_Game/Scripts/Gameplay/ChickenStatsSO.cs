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
        [Min(0f)] public float MoveSpeed = 9f;
        [Min(0f)] public float TurnSpeed = 720f; // deg/sec

        [Header("Cargo")]
        [Min(1)] public int CargoCapacity = 10;

        [Tooltip("DEPRECATED — automatic pile drain, food per second. Still read by ChickenCargo " +
                 "until Peck replaces it; the Balance Oracle already ignores it in favour of " +
                 "PeckAmount/PeckCooldown. Delete both this field and ChickenCargo's automatic " +
                 "collection together, or the two will disagree about how fast a class farms.")]
        [Min(0f)] public float CollectionRate = 1f;

        [Header("Peck (foraging)")]
        [Tooltip("Food taken from a pile per Peck press. Authored per class from day one even " +
                 "though all four currently share a value: with a uniform amount, per-class " +
                 "COOLDOWN is the only lever, and it barely separates Speedy from Fatty " +
                 "(their solved cooldowns land within 0.01s). Amount is the lever that would " +
                 "actually differentiate them.")]
        [Min(0.1f)] public float PeckAmount = 3f;

        [Tooltip("Seconds between Peck presses. Solved against this class's SCT target — do NOT " +
                 "hand-tune it; change SctTargets and re-solve. Note a partial press still costs " +
                 "a full cooldown, so the effective rate (amount/cooldown) sits ~20-30% above the " +
                 "old CollectionRate for the same clear time. That gap is quantisation waste, " +
                 "not a buff.")]
        [Min(0.05f)] public float PeckCooldown = 1f;

        [Header("Visuals")]
        [Tooltip("Uniform scale applied to the chicken's transform at spawn.")]
        [Min(0.1f)] public float Scale = 1f;
    }
}
