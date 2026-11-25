using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior kit under test: the Champion</b> (design 2026-11-25, redesigned the same afternoon). Hit a rival that has just pecked, or is pecking,
    /// and the hit stacks one extra trophy on it (Maestro, 2026-11-25: no food from cutting a Peck, only a stack for the Steal); every landed Stun, Shove
    /// and fresh hit is a trophy on that rival, and each trophy inflates your Steal on it. <see cref="FreshBiteShare"/> can bring the old bite back. Kit: Peck,
    /// WingSlam (Stun), CluckShock (Shove), Snatch (Steal); no cooldown multiplier.
    /// </summary>
    /// <remarks>
    /// <b>Simulator only, shipped off.</b> The asset is not in <c>AbilityRegistrySO</c>, so the game cannot pick it; the what-if roster swap seats it in the
    /// match simulator. Its shipped numbers are off (fresh window 0, trophy Steal 0) whatever the swap. Nothing in the game reads
    /// <see cref="PassiveAbilitySO.Bite"/> or <see cref="PassiveAbilitySO.Trophy"/> yet. <c>RelentlessPassiveSO</c> stays as it was.
    /// </remarks>
    [CreateAssetMenu(fileName = "Champion", menuName = "Cluck Wars/Passive/Champion", order = 19)]
    public sealed class ChampionPassiveSO : PassiveAbilitySO
    {
        [Header("Bite")]
        [Tooltip("Seconds a rival stays fresh after its own Peck landed with food (it is fresh during its cast too). 0 = no bite at all (shipped).")]
        [Min(0f)] public float FreshSeconds = 0f;
        [Tooltip("Share (0 to 1) of the fresh rival's own Peck amount that a hit on it takes as food: from its pile mid-cast, from its cargo just after. 0 = a hit on a fresh rival takes no food and only stacks one more trophy (shipped, Maestro 2026-11-25); 1 = the whole bite.")]
        [Range(0f, 1f)] public float FreshBiteShare = 0f;

        [Header("Trophies (per enemy)")]
        [Tooltip("Food each trophy on a rival adds to this chicken's Steal on it. 0 = trophies add nothing (shipped).")]
        [Min(0f)] public float TrophySteal = 0f;
        [Tooltip("Most trophies held on one rival.")]
        [Min(0)] public int TrophyCap = 3;
        [Tooltip("Seconds without a gain on a rival after which its trophies go, all at once.")]
        [Min(0.5f)] public float TrophySeconds = 6f;

        public ChampionPassiveSO()
        {
            DisplayName = "Champion";
            ShortLabel = "CHMP";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override BiteFacts Bite => new BiteFacts { FreshSeconds = FreshSeconds, FoodShare = FreshBiteShare };

        public override TrophyFacts Trophy => new TrophyFacts { IsChampion = true, Steal = TrophySteal, Cap = TrophyCap, Seconds = TrophySeconds };
    }
}
