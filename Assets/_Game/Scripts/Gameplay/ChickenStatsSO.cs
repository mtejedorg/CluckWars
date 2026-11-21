using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Per-class tunables. One asset per class lives in /Assets/_Game/Data/Classes/.
    /// </summary>
    /// <remarks>
    /// v0.3: <c>Attack</c>, <c>AttackRange</c>, <c>AttackCooldown</c> removed (basic attack
    /// deleted). <c>AvailableAbilities</c> / <c>Allows()</c> removed — all abilities are
    /// available to all classes (GDD §7.1).
    /// v0.7: <c>Passive</c> removed — the class specialization is the equipped
    /// <c>PassiveAbilitySO</c> now, not an enum here. <c>CollectionRate</c> removed —
    /// foraging is <c>PeckAmount</c> / <c>PeckCooldown</c>; one forage cycle is
    /// <c>max(PeckCooldown, PeckCastSeconds)</c> (BL-206: Peck is an interruptible cast).
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ChickenStats",
        menuName = "Cluck Wars/Chicken Stats",
        order = 0)]
    public sealed class ChickenStatsSO : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Warrior";

        [Header("Movement")]
        [Tooltip("World-units/sec. The Balance Oracle reports on this value and it may be tuned by hand. " +
                 "Scaled x1.35 on 2026-08-14 alongside the arena, which leaves every travel term " +
                 "(distance / speed) unchanged.")]
        [Min(0f)] public float MoveSpeed = 12.15f;
        [Min(0f)] public float TurnSpeed = 720f; // deg/sec

        [Header("Cargo")]
        [Min(1)] public int CargoCapacity = 10;

        [Header("Peck (foraging)")]
        [Tooltip("Food taken from a pile per Peck press. Authored per class from day one even " +
                 "though all four currently share a value: with a uniform amount, per-class " +
                 "COOLDOWN is the only lever, and it does not separate Speedy from Fatty " +
                 "(both solve to 0.71s). Amount is the lever that would " +
                 "actually differentiate them.")]
        [Min(0.1f)] public float PeckAmount = 3f;

        [Tooltip("Seconds between Peck presses. The Balance Oracle reports on this value and it may be tuned by hand. " +
                 "Note a partial press still costs a full cooldown, so the effective rate (amount/cooldown) " +
                 "sits ~20-30% above the old CollectionRate for the same clear time. That gap is " +
                 "quantisation waste, not a buff.")]
        [Min(0.05f)] public float PeckCooldown = 1f;

        [Tooltip("Seconds a Peck takes from press to food landing; the chicken is rooted for the whole cast and a stun, a shove, " +
                 "an execution or its own next move cuts it (no food). PLACEHOLDER values for Maestro and the Balance Oracle to tune; " +
                 "keep it readable, under about 0.9 s. The Peck clip is scaled to this value on every peer.")]
        [Range(0.2f, 1.2f)] public float PeckCastSeconds = 0.4f;

        [Header("Visuals")]
        [Tooltip("Uniform scale applied to the chicken's transform at spawn.")]
        [Min(0.1f)] public float Scale = 1f;
    }
}
