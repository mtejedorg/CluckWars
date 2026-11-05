using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior signature: tenacity and stronger crowd control.</b> Stuns, roots and timed slows on it end sooner,
    /// and the stuns and slows it applies last longer. Kit: Peck, Steal, Slow, Speed.
    /// </summary>
    /// <remarks>
    /// <b>Reworked 2026-11-05 (Maestro).</b> The cooldown reduction moved to Bully. Incoming control uses the same
    /// <see cref="PassiveAbilitySO.ModifyControlDuration"/> mechanism as Slippery and Bulwark (no knockback cut).
    /// Outgoing control is <see cref="PassiveAbilitySO.OutgoingControlMultiplier"/>: applied on this chicken's authority
    /// before a stun / slow RPC leaves, and to the Slow aura's run time (zone and slot). The receivers' control
    /// ceilings include it (<c>ControlClaimRules.CeilingFor</c>), so a boosted stun is not refused.
    /// </remarks>
    [CreateAssetMenu(fileName = "Relentless", menuName = "Cluck Wars/Passive/Relentless", order = 15)]
    public sealed class RelentlessPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on incoming stun / root / timed-slow duration. 0.75 = 25% shorter.")]
        [Range(0.1f, 1f)] public float DurationMultiplier = 0.75f;

        [Tooltip("Multiplier on the duration of the stuns and slows this chicken applies. 1.3 = 30% longer.")]
        [Range(1f, 2f)] public float OutgoingDurationMultiplier = 1.3f;

        public RelentlessPassiveSO()
        {
            DisplayName = "Relentless";
            ShortLabel = "RLNT";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override bool IsSignature => true;

        public override float ModifyControlDuration(float seconds, ControlKind kind, ChickenController self) =>
            seconds * DurationMultiplier;

        public override float OutgoingControlMultiplier => OutgoingDurationMultiplier;

        public override string PerkLineKey => UiKeys.PerkRelentlessLine;
        public override string PerkDetailKey => UiKeys.PerkRelentlessDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("out", Percent(OutgoingDurationMultiplier - 1f)), ("in", Percent(1f - DurationMultiplier)) };
    }
}
