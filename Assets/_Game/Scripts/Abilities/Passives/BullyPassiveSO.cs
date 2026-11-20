using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior alternative: the spammer.</b> Abilities come back faster. Kit: Peck, Steal, Stun, Jump.
    /// </summary>
    /// <remarks>
    /// <b>Reworked 2026-11-05 (Maestro).</b> The Warrior is the all-rounder: a bit of steal, crowd control, pecking
    /// and movement, each weaker than the specialist's. Bully took over the cooldown reduction Relentless had
    /// (x0.75); its old steal x1.4 and +2 cargo were dropped, so it farms exactly like a naked Warrior, so the
    /// Balance Oracle needs no special case for it.
    /// <para>
    /// ⚠️ <b>Deliberately excludes Peck.</b> Peck is an ability with a cooldown like any other, so a blanket
    /// reduction would hand the Warrior ~25% more farming throughput, an effect that lives entirely outside the
    /// Balance Oracle's naked axiom. This is a combat passive; making it a farming passive by accident is exactly
    /// the drift the Oracle exists to catch.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Bully", menuName = "Cluck Wars/Passive/Bully", order = 16)]
    public sealed class BullyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on ability cooldowns. 0.75 = 25% faster. Does NOT apply to Peck.")]
        [Range(0.25f, 1f)] public float CooldownMultiplier = 0.75f;

        public BullyPassiveSO()
        {
            DisplayName = "Bully";
            ShortLabel = "BULY";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override string PerkLineKey => UiKeys.PerkBullyLine;
        public override string PerkDetailKey => UiKeys.PerkBullyDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("pct", Percent(1f - CooldownMultiplier)) };

        public override float ModifyCooldown(float seconds, AbilityBaseSO ability, ChickenController self)
        {
            if (ability is PeckAbilitySO) return seconds; // foraging cadence is the Oracle's, not ours
            return seconds * CooldownMultiplier;
        }
    }
}
