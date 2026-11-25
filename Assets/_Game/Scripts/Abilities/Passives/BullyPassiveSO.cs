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

        [Header("Hit-steal (design 2026-11-25; shipped off)")]
        [Tooltip("Food a Stun that really affects a rival takes into this chicken's cargo. 0 = off (shipped).")]
        [Min(0f)] public float HitStealStun = 0f;
        [Tooltip("Food a Shove that really moves a rival takes into this chicken's cargo. 0 = off (shipped).")]
        [Min(0f)] public float HitStealShove = 0f;
        [Tooltip("Food an instant Slow that lands takes into this chicken's cargo. 0 = off (designed 0: Slow Area never takes).")]
        [Min(0f)] public float HitStealSlow = 0f;
        [Tooltip("Seconds a take locks the same victim against every hitter of this chicken.")]
        [Min(0f)] public float HitStealLockSeconds = 3f;

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

        public override HitStealFacts HitSteal => new HitStealFacts
        {
            Stun = HitStealStun, Shove = HitStealShove, Slow = HitStealSlow, LockSeconds = HitStealLockSeconds,
        };

        public override float ModifyCooldown(float seconds, AbilityBaseSO ability, ChickenController self)
        {
            if (ability is PeckAbilitySO) return seconds; // foraging cadence is the Oracle's, not ours
            return seconds * CooldownMultiplier;
        }
    }
}
