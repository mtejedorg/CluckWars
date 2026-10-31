using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Death Mark. Pre-equip-only (AllowedClasses = None). One button, two presses: the first marks a random living rival
    /// anywhere on the map, the second kills it once it is stunned and in reach. Thin wrapper over <see cref="AssassinExecute"/>,
    /// which owns the state and decides who is marked; the numbers live here, on the asset.
    /// </summary>
    /// <remarks>
    /// <b>No aim shape, on purpose.</b> The mark has no range, so a shape or a ring would lie. <see cref="RequiresEnemyInRange"/>
    /// is false for the same reason: the press is gated by <see cref="IsUsable"/> alone, which asks the execute state both
    /// halves. Everything shown about the kill's range is on the target (<c>MarkFeedback</c>).
    /// </remarks>
    [CreateAssetMenu(fileName = "MarkKill", menuName = "Cluck Wars/Ability/Control/Mark Kill", order = 12)]
    public sealed class MarkKillAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityMarkKillDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("r", Num(KillRange)) };

        private const string Source = "MarkKill";

        public MarkKillAbilitySO()
        {
            DisplayName = "Death Mark";
            ShortLabel = "EXEC";
            Description = "Marks a random rival anywhere. Stun them within 8m, then press again to take everything they carry.";
            Category = AbilityCategory.Control;
            AllowedClasses = ChickenClassFlags.None; // pre-equip-only: obtainable solely as a subclass pre-equip (Spoiler Peck slot)
            Duration = 0.1f;
            Cooldown = 5f;
        }

        [Header("Execute (AssassinExecute reads these)")]
        [Tooltip("The Kill press reaches this far (planar m). The mark itself has no range.")]
        [Min(1f)] public float KillRange = AssassinExecute.DefaultKillRange;
        [Tooltip("Seconds after the mark before a stun can qualify the target. 0: crossing the map already costs the time.")]
        [Min(0f)] public float ArmSeconds = AssassinExecute.ArmDuration;
        [Tooltip("Seconds from the mark to its expiry.")]
        [Min(0.1f)] public float WindowSeconds = AssassinExecute.WindowDuration;
        [Tooltip("Cooldown after a mark that expired or lost its target.")]
        [Min(0f)] public float FailCooldownSeconds = AssassinExecute.FailCooldown;
        [Tooltip("Cooldown after a kill.")]
        [Min(0f)] public float SuccessCooldownSeconds = AssassinExecute.SuccessCooldown;

        public override bool RequiresEnemyInRange => false;
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsEnemies => false;

        /// <summary>Marked: only a Kill that would land. Unmarked: a rival exists to be marked. Nothing else greys it.</summary>
        public override bool IsUsable(ChickenController caster)
        {
            var exec = caster != null ? caster.GetComponent<AssassinExecute>() : null;
            if (exec == null) return false;
            return exec.MarkedTarget != Fusion.NetworkBehaviourId.None ? exec.CanKillNow : exec.HasMarkCandidate;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            // Both bail-outs below are wiring errors, not gameplay states, and the press has
            // already cost the player a cast and its cooldown, so they get logged rather than eaten.
            var caster = ctx.Controller;
            if (caster == null)
            {
                ctx.Log?.Error(Source, "Mark/Kill cast lost: the ability context has no caster " +
                    "ChickenController. The cast and its cooldown were consumed with nothing marked.");
                return;
            }

            var exec = caster.GetComponent<AssassinExecute>();
            if (exec == null)
            {
                ctx.Log?.Error(Source, "Mark/Kill cast lost: the caster has no AssassinExecute component, " +
                    "add it to the chicken prefab. The cast and its cooldown were consumed with nothing marked.");
                return;
            }

            exec.Press();
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
