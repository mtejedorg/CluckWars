using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior kit under test: the Enforcer</b> (design 2026-11-25, redesigned the same afternoon). Hit a rival that has just pecked, or is pecking,
    /// and the hit stacks one extra debt on it (Maestro, 2026-11-25: no food from cutting a Peck, only a stack for the Steal); every landed Stun, Shove
    /// and fresh hit is a debt on that rival, and each debt inflates your Steal on it. <see cref="FreshBiteShare"/> can bring the old bite back. Kit: Peck,
    /// WingSlam (Stun), CluckShock (Shove), Snatch (Steal); no cooldown multiplier.
    /// </summary>
    /// <remarks>
    /// <b>Simulator only, shipped off.</b> The asset is not in <c>AbilityRegistrySO</c>, so the game cannot pick it; the what-if roster swap seats it in the
    /// match simulator. Its shipped numbers are off (fresh window 0, debt Steal 0) whatever the swap. Nothing in the game reads
    /// <see cref="PassiveAbilitySO.Bite"/> or <see cref="PassiveAbilitySO.Debt"/> yet. <c>RelentlessPassiveSO</c> stays as it was.
    /// </remarks>
    [CreateAssetMenu(fileName = "Enforcer", menuName = "Cluck Wars/Passive/Enforcer", order = 19)]
    public sealed class EnforcerPassiveSO : PassiveAbilitySO
    {
        [Header("Bite")]
        [Tooltip("Seconds a rival stays fresh after its own Peck landed with food (it is fresh during its cast too). 0 = no bite at all (shipped).")]
        [Min(0f)] public float FreshSeconds = 0f;
        [Tooltip("Share (0 to 1) of the fresh rival's own Peck amount that a hit on it takes as food: from its pile mid-cast, from its cargo just after. 0 = a hit on a fresh rival takes no food and only stacks one more debt (shipped, Maestro 2026-11-25); 1 = the whole bite.")]
        [Range(0f, 1f)] public float FreshBiteShare = 0f;

        [Header("Debts (per enemy)")]
        [Tooltip("Food each debt on a rival adds to this chicken's Steal on it. 0 = debts add nothing (shipped).")]
        [Min(0f)] public float DebtSteal = 0f;
        [Tooltip("Most debts held on one rival.")]
        [Min(0)] public int DebtCap = 3;
        [Tooltip("Seconds without a gain on a rival after which its debts go, all at once.")]
        [Min(0.5f)] public float DebtSeconds = 6f;

        public EnforcerPassiveSO()
        {
            DisplayName = "Enforcer";
            ShortLabel = "ENFR";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override BiteFacts Bite => new BiteFacts { FreshSeconds = FreshSeconds, FoodShare = FreshBiteShare };

        public override DebtFacts Debt => new DebtFacts { IsEnforcer = true, Steal = DebtSteal, Cap = DebtCap, Seconds = DebtSeconds };
    }
}
