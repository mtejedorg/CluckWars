using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Shield</b> (Maestro 2026-11-05): Turtle Mode with Egg Shell folded in (2026-11) and Immovable folded in (2026-11-05):
    /// walk, then lock.
    /// <list type="bullet">
    /// <item>First press: the shell walk for <see cref="AbilityBaseSO.Duration"/> (3 s) at <see cref="SpeedMultiplier"/>, steal-proof
    /// and so unkillable (<c>ExecuteRules.CanBeExecuted</c>), and control-immune: no stun, root, slow or shove lands. That is
    /// Immovable's window, the same <c>ChickenController.GrantControlImmunity</c>, enforced at the same chokepoints
    /// (<c>ApplyPassiveControlDuration</c>, <c>ApplyKnockback</c>, the zone slow). Like Immovable it does not clear control already
    /// on him. The cooldown starts here, once, for every path.</item>
    /// <item>Second press during the walk: lock in place for up to <see cref="LockSeconds"/>: immobile, immune to stun / root / slow /
    /// shove (Egg Shell), still steal-proof. A stun blocks the press like any cast. The lock ends the ability: no return to the walk.</item>
    /// <item>Third press: ends the lock early. Never locking: the walk ends at its limit with the full cooldown, no refund.</item>
    /// </list>
    /// The second and third presses go through <see cref="AbilityBaseSO.AcceptsPressWhileActive"/>: same button, same hold-to-preview,
    /// no new input path. Everything is the existing networked state (the slot's active timer drives the hex's duration ring;
    /// steal-proof and control immunity are networked windows), so no new networked field or RPC.
    /// </summary>
    [CreateAssetMenu(fileName = "TurtleMode", menuName = "Cluck Wars/Ability/Turtle Mode", order = 2)]
    public sealed class TurtleModeAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityTurtleModeDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("secs", Num(Duration)), ("lock", Num(LockSeconds)) };

        [Tooltip("Movement speed scalar during the walk. 0.5 = half speed, a shell walk.")]
        [Range(0.05f, 1f)] public float SpeedMultiplier = 0.5f;

        [Tooltip("Longest lock (second press during the walk): immobile, immune to control, steal-proof. A third press ends it early.")]
        [Min(0.1f)] public float LockSeconds = 2f;

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;
        public override bool AcceptsPressWhileActive => true;

        public TurtleModeAbilitySO()
        {
            DisplayName = "Shield";
            ShortLabel = "SHLD";
            Description = "Shell walk 3s: nobody can rob, kill, stun, slow or shove you. Press again to lock in place for 2s.";
            Category = AbilityCategory.Defense;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 3f;
            Cooldown = 10f;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.SetMoveSpeed(ctx.Slot, SpeedMultiplier);
            // The walk: steal-proof (and so unkillable) and control-immune (Immovable's window) for its own length.
            ctx.Controller.GrantStealProof(Duration);
            ctx.Controller.GrantControlImmunity(Duration);
        }

        public override float OnPressWhileActive(AbilityContext ctx)
        {
            var c = ctx.Controller;
            if (TurtleRules.IsLocked(c.Effects.HasMovementLock(ctx.Slot))) return 0f;   // third press: end the lock now

            c.Effects.RemoveMoveSpeed(ctx.Slot);
            c.Effects.SetMovementLock(ctx.Slot);
            c.GrantControlImmunity(LockSeconds);
            c.GrantStealProof(LockSeconds);
            return LockSeconds;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            var c = ctx.Controller;
            c.Effects.RemoveMoveSpeed(ctx.Slot);
            if (c.Effects.HasMovementLock(ctx.Slot))
            {
                // The lock ended (its limit, a third press, a reset or a removal): its windows close with it.
                c.Effects.RemoveMovementLock(ctx.Slot);
                c.EndShellWindows();
            }
        }
    }

    /// <summary>Turtle Mode's phase rules, pure.</summary>
    public static class TurtleRules
    {
        public static bool IsLocked(bool movementLockOnThisSlot) => movementLockOnThisSlot;

        /// <summary>What a press does to a running Turtle Mode: lock from the walk, end from the lock.</summary>
        public static bool PressEnds(bool locked) => locked;
    }
}
