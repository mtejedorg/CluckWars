using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin locked signature ability: Mark/Kill execute (Task 5).
    /// Thin wrapper delegating activation to AssassinExecute.Press().
    /// </summary>
    [CreateAssetMenu(fileName = "MarkKill", menuName = "Cluck Wars/Ability/Control/Mark Kill", order = 12)]
    public sealed class MarkKillAbilitySO : AbilityBaseSO
    {
        public MarkKillAbilitySO()
        {
            DisplayName = "Mark/Kill";
            ShortLabel = "EXEC";
            Description = "Marks an isolated rival, arming a fatal execute once stunned.";
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
            Duration = 0.1f;
            Cooldown = 5f;
        }

        protected override string DefaultIcon => "🎯";

        public override float IndicatorRange => AssassinExecute.MaxMarkRange;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AimShape => AbilityAimShape.SingleTarget;
        public override float AimRadius => AssassinExecute.MaxMarkRange;

        /// <summary>
        /// The two rules <c>AssassinExecute.Press</c> enforced privately and the preview knew
        /// nothing about: the target must be <b>isolated</b> (crowd counterplay) and must be
        /// a <b>rival</b> (not a same-corner ally). Stating them here means <c>WouldAffect</c>,
        /// the telegraph, <c>IsUsable</c>'s grey-out and the press itself all refuse exactly
        /// the same chickens — previously the preview would happily mark a rival standing in
        /// a crowd, and the press would then silently decline.
        ///
        /// <b>Balance consequence, intended:</b> the button now greys out when no
        /// <i>isolated</i> rival is in range, where before it lit up for any rival at all.
        /// </summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            // Same-corner chickens are allies; a caster with no corner assigned yet (-1) has
            // no allies, so everyone is fair game — carried over verbatim from Press().
            if (TryGetCorner(caster, out int casterCorner) && casterCorner >= 0 &&
                TryGetCorner(candidate, out int candidateCorner) && candidateCorner == casterCorner)
                return false;

            return AssassinExecute.IsIsolated(candidate, caster);
        }

        /// <summary>
        /// <c>HomeCornerIndex</c> is <c>[Networked]</c>, and this filter runs from the HUD's
        /// usability poll as well as from the cast — so it can be reached on a chicken whose
        /// <c>NetworkObject</c> is not (or no longer) valid. Same guard
        /// <c>AbilityTelegraph.TryGetCharge</c> uses. A corner that cannot be read is treated
        /// as unassigned, which is the permissive branch <c>Press()</c> already took for -1.
        /// </summary>
        private static bool TryGetCorner(ChickenController chicken, out int corner)
        {
            corner = -1;
            var obj = chicken != null ? chicken.Object : null;
            if (obj == null || !obj.IsValid) return false;
            corner = chicken.HomeCornerIndex;
            return true;
        }

        /// <summary>
        /// Keeps its own override for the post-mark "kill ready" state — that has
        /// nothing to do with the aim shape. The pre-mark range half now routes
        /// through <see cref="AbilityBaseSO.HasAnyTarget"/> instead of a bespoke
        /// range scan, so it agrees with the preview and <c>AssassinExecute</c>'s own
        /// candidate search on the same registry.
        /// </summary>
        public override bool IsUsable(ChickenController caster)
        {
            var exec = caster != null ? caster.GetComponent<AssassinExecute>() : null;
            if (exec != null && exec.MarkedTarget != Fusion.NetworkBehaviourId.None)
            {
                return exec.KillReady;
            }
            return HasAnyTarget(caster);
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var exec = caster != null ? caster.GetComponent<AssassinExecute>() : null;
            if (exec == null)
            {
                // TODO(logging): SOs have no injected ILogService; surface this once
                // there is one. A Mark/Kill equipped on a chicken with no AssassinExecute
                // is a prefab-wiring error, not a gameplay state.
                return;
            }

            // Mark press: hand Press the one target the shared scan resolved. SingleTarget
            // truncates GatherTargets to the nearest eligible rival, and ExtraTargetFilter
            // above has already applied the isolation + rival rules, so Press needs no
            // candidate search of its own. Kill press: MarkedTarget is already set and the
            // target argument is irrelevant.
            ChickenController target = null;
            if (exec.MarkedTarget == Fusion.NetworkBehaviourId.None)
            {
                GatherTargets(caster, _scratch);
                if (_scratch.Count > 0) target = _scratch[0];
            }

            exec.Press(target);
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
