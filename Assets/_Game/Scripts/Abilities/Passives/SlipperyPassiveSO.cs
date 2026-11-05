using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy signature.</b> Ability slows barely hold it: every ability slow on a Slippery chicken is weaker and ends
    /// sooner. Stuns land in full (the stun is the Speedy's weakness), and so do roots, the pile slow and the bump slow
    /// (Maestro 2026-11-05; before that it shortened every stun, root and timed slow by 40%).
    /// </summary>
    /// <remarks>
    /// One <see cref="Resist"/> value drives all three hooks, so the strength and the duration can never drift apart:
    /// <list type="bullet">
    ///   <item>Strength: the slow's reduction is scaled by it, factor' = 1 - (1 - factor) x 0.6 (a 0.55 slow becomes 0.73).</item>
    ///   <item>Timed ability slow (<c>RPC_ApplyAbilitySlow</c>): its duration x 0.6.</item>
    ///   <item>Zone / aura slow: it slows a Slippery chicken only during the first 60% of the zone's lifetime (a Relentless
    ///   aura's lengthened 3.9 s included: slowed for the first 2.34 s).</item>
    /// </list>
    /// </remarks>
    [CreateAssetMenu(fileName = "Slippery", menuName = "Cluck Wars/Passive/Slippery", order = 10)]
    public sealed class SlipperyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("How much of an ability slow sticks: its strength (the speed it takes away), a timed slow's duration and the " +
                 "share of a slow zone's lifetime that slows this chicken are all multiplied by it. 0.6 = 40% weaker and 40% " +
                 "shorter. Stuns, roots, pile and bump slows are not resisted.")]
        [Range(0.1f, 1f)] public float Resist = 0.6f;

        public SlipperyPassiveSO()
        {
            DisplayName = "Slippery";
            ShortLabel = "SLIP";
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        public override bool IsSignature => true;

        public override float ModifyControlDuration(float seconds, ControlKind kind, ChickenController self) =>
            kind == ControlKind.Slow ? seconds * Resist : seconds;

        public override float ModifyIncomingSlowFactor(float factor, ChickenController self) =>
            1f - (1f - factor) * Resist;

        public override float ZoneSlowLifetimeFraction(ChickenController self) => Resist;

        public override string PerkLineKey => UiKeys.PerkSlipperyLine;
        public override string PerkDetailKey => UiKeys.PerkSlipperyDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("pct", Percent(1f - Resist)) };
    }
}
