using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// One-shot non-combat ability: find the nearest enemy chicken in range and yank
    /// a chunk of their cargo over to yours. Bypasses the fight entirely — the only
    /// utility ability in the pool. Uses <see cref="ChickenCargo.RPC_DrainStolen"/>
    /// for cross-authority cargo moves; the thief asks, the victim's authority drains
    /// whatever is actually there and reports it, and only that report credits the thief.
    /// </summary>
    /// <remarks>
    /// <see cref="OnDeactivate"/> is a no-op — the steal happens at activation time
    /// and the active duration is purely cosmetic (UI cooldown overlay timing).
    /// </remarks>
    [CreateAssetMenu(fileName = "SneakySteal", menuName = "Cluck Wars/Ability/Sneaky Steal", order = 6)]
    public sealed class SneakyStealAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilitySneakyStealDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("n", Num(StealAmount)), ("r", Num(StealRange)), ("secs", Num(SameVictimCooldownSeconds)) };

        [Tooltip("How close an enemy chicken needs to be to steal from. Generous so it feels reliable.")]
        [Min(0.5f)] public float StealRange = 5.4f;

        [Tooltip("How much cargo to steal. Capped to the victim's actual cargo and the thief's free space.")]
        [Min(1f)] public float StealAmount = 6f;

        [Tooltip("Seconds before this thief's Sneaky Steal can take from the same bird again. The next nearest rival is fair game.")]
        [Min(0f)] public float SameVictimCooldownSeconds = 10f;

        public override float IndicatorRange => StealRange;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.SingleTarget;
        public override float AimRadius => StealRange;

        public override float NominalStealAmount => StealAmount;

        public override bool TargetEffectIncludesSteal => true;

        /// <summary>Only a cargo-carrier is a valid steal target.</summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            var cargo = candidate.Cargo;
            return cargo != null && cargo.Stealable > 0f && !caster.IsStealLockedOn(candidate);
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            // Nearest enemy chicken with cargo on board — GatherTargets sorts ascending
            // by distance, so index 0 is always "nearest valid target".
            GatherTargets(thief, _scratch);
            if (_scratch.Count == 0) return;

            // Nearest target stays the telegraphed target: if it is steal-proof there is no steal (and no fallback
            // to the next rival). Asks for the smaller of (StealAmount, victim's cargo, our free space); the victim
            // reports what it really gave. The lock is set only when something was asked for.
            if (StealFrom(thief, _scratch[0], StealAmount) <= 0f) return;
            thief.LockStealOn(_scratch[0], SameVictimCooldownSeconds);
        }
    }
}
