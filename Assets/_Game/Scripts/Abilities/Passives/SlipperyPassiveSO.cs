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
    /// One <see cref="Resist"/> value drives both hooks, so the strength and the duration can never drift apart:
    /// <list type="bullet">
    ///   <item>Strength: the slow's reduction is scaled by it, factor' = 1 - (1 - factor) x 0.6 (a 0.55 slow becomes 0.73).</item>
    ///   <item>Timed ability slow (<c>RPC_ApplyAbilitySlow</c>): its duration x 0.6.</item>
    /// </list>
    /// A slow zone or aura gets the weaker slow only: it slows while the chicken is inside and stops the moment it leaves, so
    /// there is no duration to shorten (Maestro 2026-11-05 removed the first-60%-of-the-zone's-life window; how "40% shorter"
    /// applies to zones is undecided, BL-123).
    /// </remarks>
    [CreateAssetMenu(fileName = "Slippery", menuName = "Cluck Wars/Passive/Slippery", order = 10)]
    public sealed class SlipperyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("How much of an ability slow sticks: its strength (the speed it takes away), and a timed slow's duration are " +
                 "both multiplied by it (a slow zone gets the weaker slow only). 0.6 = 40% weaker and 40% " +
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

        public override string PerkLineKey => UiKeys.PerkSlipperyLine;
        public override string PerkDetailKey => UiKeys.PerkSlipperyDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("pct", Percent(1f - Resist)) };
    }
}
