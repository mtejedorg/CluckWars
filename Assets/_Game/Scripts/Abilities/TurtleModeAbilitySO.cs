using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Shield</b> (simplified by Maestro 2026-11-05): a slow-immune walk, then an egg, on one press.
    /// <list type="bullet">
    /// <item><b>Walk</b>, <see cref="WalkSeconds"/> (1 s): <see cref="SpeedMultiplier"/> walk, immune to slows only
    /// (<c>ChickenController.GrantSlowImmunity</c>: the timed ability slow and the zone / aura slow). Stuns, roots, shoves,
    /// theft and the Death Mark execution still land.</item>
    /// <item><b>Egg</b>, <see cref="EggSeconds"/> (2 s), starts on its own when the walk ends: can't move, immune to stun,
    /// slow, root and shove (<c>GrantControlImmunity</c>) and steal-proof, so unkillable (<c>GrantStealProof</c>,
    /// <c>ExecuteRules.CanBeExecuted</c>).</item>
    /// <item>Pressing Shield again at any point ends the whole ability (<see cref="OnPressWhileActive"/>).</item>
    /// <item>Casting any other move ends the egg only (<see cref="OnOtherCast"/>): a running egg ends there (it was the rest of
    /// the run); during the walk the pending egg is cancelled and the walk runs out on its own.</item>
    /// </list>
    /// The slot runs <see cref="AbilityBaseSO.Duration"/> = walk + egg (3 s) and drives the hex's duration ring; the cooldown
    /// (10 s) starts at the cast, once, for every path. The phase is read from the slot's remaining time
    /// (<see cref="ShieldRules"/>), so no new networked field: the windows are the existing networked timers.
    /// </summary>
    [CreateAssetMenu(fileName = "TurtleMode", menuName = "Cluck Wars/Ability/Turtle Mode", order = 2)]
    public sealed class TurtleModeAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityTurtleModeDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("walk", Num(WalkSeconds)), ("egg", Num(EggSeconds)) };

        [Tooltip("Movement speed scalar during the walk. 0.5 = half speed, a shell walk.")]
        [Range(0.05f, 1f)] public float SpeedMultiplier = 0.5f;

        [Tooltip("Phase 1: the slow-immune walk. Stun, shove, theft and execution still land.")]
        [Min(0.1f)] public float WalkSeconds = 1f;

        [Tooltip("Phase 2, automatic after the walk: immobile, immune to every control effect, steal-proof and unkillable.")]
        [Min(0.1f)] public float EggSeconds = 2f;

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;
        public override bool AcceptsPressWhileActive => true;
        public override bool HasFollowUpPhase => true;

        public TurtleModeAbilitySO()
        {
            DisplayName = "Shield";
            ShortLabel = "SHLD";
            Description = "Walk 1s immune to slows, then be an egg for 2s: no stun, slow, shove, theft or kill. Press again to stop.";
            Category = AbilityCategory.Defense;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 3f;
            Cooldown = 10f;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.SetMoveSpeed(ctx.Slot, SpeedMultiplier);
            ctx.Controller.GrantSlowImmunity(WalkSeconds);
        }

        /// <summary>The walk turns into the egg when the run has <see cref="EggSeconds"/> left.</summary>
        public override bool OnActiveTick(AbilityContext ctx, float remainingSeconds)
        {
            var c = ctx.Controller;
            if (!ShieldRules.EggStartsNow(remainingSeconds, EggSeconds, c.Effects.HasMovementLock(ctx.Slot))) return false;

            c.Effects.RemoveMoveSpeed(ctx.Slot);
            c.Effects.SetMovementLock(ctx.Slot);
            c.GrantControlImmunity(remainingSeconds);
            c.GrantStealProof(remainingSeconds);
            return true;
        }

        /// <summary>A second press ends the whole Shield, in either phase.</summary>
        public override float OnPressWhileActive(AbilityContext ctx) => 0f;

        public override float OnOtherCast(AbilityContext ctx, float remainingSeconds) =>
            ShieldRules.RunAfterOtherCast(remainingSeconds, EggSeconds, ctx.Controller.Effects.HasMovementLock(ctx.Slot));

        public override void OnDeactivate(AbilityContext ctx)
        {
            var c = ctx.Controller;
            c.Effects.RemoveMoveSpeed(ctx.Slot);
            c.Effects.RemoveMovementLock(ctx.Slot);
            // Whatever ended the run (its limit, a press, another cast, a reset or a removal), its windows close with it.
            c.EndShellWindows();
        }
    }

    /// <summary>The Shield's phase rules, pure.</summary>
    public static class ShieldRules
    {
        /// <summary>The egg starts on the first tick the run has <paramref name="eggSeconds"/> or less left, once.</summary>
        public static bool EggStartsNow(float remainingSeconds, float eggSeconds, bool eggRunning) =>
            !eggRunning && remainingSeconds > 0f && remainingSeconds <= eggSeconds;

        /// <summary>
        /// Another move was cast: a running egg ends the Shield now (0); during the walk the pending egg is dropped and the
        /// run goes on for the rest of the walk only (remaining minus the egg), or ends if that is nothing.
        /// </summary>
        public static float RunAfterOtherCast(float remainingSeconds, float eggSeconds, bool eggRunning) =>
            eggRunning ? 0f : Mathf.Max(0f, remainingSeconds - eggSeconds);
    }
}
