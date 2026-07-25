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

        public override bool IsUsable(ChickenController caster)
        {
            var exec = caster != null ? caster.GetComponent<AssassinExecute>() : null;
            if (exec != null && exec.MarkedTarget != Fusion.NetworkBehaviourId.None)
            {
                return exec.KillReady;
            }
            return HasEnemyInRange(caster, AssassinExecute.MaxMarkRange);
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var exec = ctx.Controller.GetComponent<AssassinExecute>();
            if (exec != null)
            {
                exec.Press();
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
