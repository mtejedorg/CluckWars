using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior alternative: the spammer.</b> Abilities come back faster, and the Stun and the Shove that land take food. Kit: Peck, Jump, Stun, Shove.
    /// </summary>
    /// <remarks>
    /// <b>Reworked 2026-11-05 (Maestro).</b> The Warrior is the all-rounder: a bit of steal, crowd control, pecking
    /// and movement, each weaker than the specialist's. Bully took over the cooldown reduction Relentless had (x0.75).
    /// <b>2026-11-25 (BL-226):</b> the Steal left the kit; a Stun or a Shove that really affects a rival takes
    /// <see cref="HitStealStun"/> / <see cref="HitStealShove"/> food, and <see cref="PassiveAbilitySO.PeckCooldownMultiplier"/> slows the Peck, so the
    /// kit forages less than a naked Warrior and earns the difference by hitting.
    /// <para>
    /// <b>The x0.75 still skips Peck.</b> Peck is an ability with a cooldown like any other, so a blanket reduction would hand the Warrior ~25% more
    /// farming throughput outside the Balance Oracle's naked axiom; the Peck's own cadence is the separate, deliberate
    /// <see cref="PassiveAbilitySO.PeckCooldownMultiplier"/>.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Bully", menuName = "Cluck Wars/Passive/Bully", order = 16)]
    public sealed class BullyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on ability cooldowns. 0.75 = 25% faster. Does NOT apply to Peck (see PeckCooldownMultiplier).")]
        [Range(0.25f, 1f)] public float CooldownMultiplier = 0.75f;

        [Header("Hit-steal (design 2026-11-25)")]
        [Tooltip("Food a Stun that really affects a rival takes into this chicken's cargo. 0 = off.")]
        [Min(0f)] public float HitStealStun = 0f;
        [Tooltip("Food a Shove that really moves a rival takes into this chicken's cargo. 0 = off.")]
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
            new (string, object)[]
            {
                ("pct", Percent(1f - CooldownMultiplier)), ("stun", Num(HitStealStun)), ("shove", Num(HitStealShove)),
                ("lock", Num(HitStealLockSeconds)), ("peck", Percent(PeckCooldownMultiplier - 1f)),
            };

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
