using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior kit under test: the Trophy Hunter</b> (design 2026-11-25). Catch a rival mid Peck-cast and its bite is yours; every break and every
    /// Steal that takes food is a trophy, and each trophy makes the beak bigger. Kit: Peck, Speed, Steal, Slow (Relentless's kit).
    /// </summary>
    /// <remarks>
    /// <b>Simulator only, shipped off.</b> The asset is not in <c>AbilityRegistrySO</c>, so the game cannot pick it; the what-if roster swap seats it in the
    /// match simulator. Its shipped numbers are off (reach 0, room 0) whatever the swap. Nothing in the game reads <see cref="PassiveAbilitySO.PeckBreak"/>
    /// or <see cref="PassiveAbilitySO.Trophy"/> yet. <c>RelentlessPassiveSO</c> stays as it was.
    /// </remarks>
    [CreateAssetMenu(fileName = "TrophyHunter", menuName = "Cluck Wars/Passive/Trophy Hunter", order = 19)]
    public sealed class TrophyHunterPassiveSO : PassiveAbilitySO
    {
        [Header("Peck break")]
        [Tooltip("Centre to centre metres a break reaches. 0 = no break (shipped).")]
        [Min(0f)] public float BreakReach = 0f;
        [Tooltip("Seconds before this chicken may break again. Gates breaks only, never its Peck.")]
        [Min(0f)] public float BreakCooldown = 2.5f;
        [Tooltip("Seconds a broken rival cannot be broken again.")]
        [Min(0f)] public float BreakImmuneSeconds = 2.5f;

        [Header("Trophies")]
        [Tooltip("Beak capacity one trophy adds. 0 = trophies add nothing (shipped).")]
        [Min(0)] public int TrophyRoom = 0;
        [Tooltip("Most trophies held at once.")]
        [Min(0)] public int TrophyCap = 3;
        [Tooltip("true: banking clears every trophy (a streak). false: trophies are kept through banking (slow growth). Shipped false.")]
        public bool TrophiesClearOnBank = false;

        public TrophyHunterPassiveSO()
        {
            DisplayName = "Trophy Hunter";
            ShortLabel = "TRPH";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override PeckBreakFacts PeckBreak => new PeckBreakFacts
        {
            Reach = BreakReach, Cooldown = BreakCooldown, ImmuneSeconds = BreakImmuneSeconds,
        };

        public override TrophyFacts Trophy => new TrophyFacts { IsTrophyHunter = true, Room = TrophyRoom, Cap = TrophyCap, ClearOnBank = TrophiesClearOnBank };
    }
}
