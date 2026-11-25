using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior alternative: the Enforcer</b> (Maestro 2026-11-25, took Relentless's seat). Hit a rival that is pecking, or has just pecked, and the
    /// hit stacks one extra debt on it (no food from cutting a Peck, only a stack for the Steal); every landed Stun, Shove and fresh hit is a debt on
    /// that rival, and each debt inflates your Steal on it. <see cref="FreshBiteShare"/> can bring the old bite back in the simulator. Kit: Peck,
    /// WingSlam (Stun), CluckShock (Shove), Snatch (Steal); no cooldown multiplier, a slower Peck.
    /// </summary>
    /// <remarks>
    /// <b>Live since 2026-11-25 (BL-226):</b> registered, the Warrior's default pick (<see cref="IsSignature"/>). The game reads <see cref="PassiveAbilitySO.Bite"/>
    /// (the fresh window) and <see cref="PassiveAbilitySO.Debt"/> through <c>EnforcerDebts</c>. <see cref="FreshBiteShare"/> is a simulator-only
    /// knob: the game plays no food bite, so the field is on the balance deny list. <c>RelentlessPassiveSO</c> stays as a retired, parked idea.
    /// </remarks>
    [CreateAssetMenu(fileName = "Enforcer", menuName = "Cluck Wars/Passive/Enforcer", order = 19)]
    public sealed class EnforcerPassiveSO : PassiveAbilitySO
    {
        [Header("Bite")]
        [Tooltip("Seconds a rival stays fresh after its own Peck landed with food (it is fresh during its cast too). 0 = no fresh hit at all.")]
        [Min(0f)] public float FreshSeconds = 0f;
        [Tooltip("Simulator only (the game plays no food bite; denied to a balance delta). Share (0 to 1) of the fresh rival's own Peck amount that a hit on it takes as food. 0 = a hit on a fresh rival takes no food and only stacks one more debt (shipped, Maestro 2026-11-25); 1 = the whole bite.")]
        [Range(0f, 1f)] public float FreshBiteShare = 0f;

        [Header("Debts (per enemy)")]
        [Tooltip("Food each debt on a rival adds to this chicken's Steal on it. 0 = debts add nothing.")]
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

        public override bool IsSignature => true;

        public override string PerkLineKey => UiKeys.PerkEnforcerLine;
        public override string PerkDetailKey => UiKeys.PerkEnforcerDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[]
            {
                ("fresh", Num(FreshSeconds)), ("steal", Num(DebtSteal)), ("cap", DebtCap), ("secs", Num(DebtSeconds)),
                ("peck", Percent(PeckCooldownMultiplier - 1f)),
            };

        public override BiteFacts Bite => new BiteFacts { FreshSeconds = FreshSeconds, FoodShare = FreshBiteShare };

        public override DebtFacts Debt => new DebtFacts { IsEnforcer = true, Steal = DebtSteal, Cap = DebtCap, Seconds = DebtSeconds };
    }
}
