using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior, Relentless signature.</b> A short forward shove that staggers whoever it
    /// catches. Cheap, frequent, unspectacular.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately a worse Cluck Shock.</b> The Warrior's essence is "good for all, expert
    /// in nothing" (docs/design/class-essence-and-signatures.md §2), and that has to be a
    /// number rather than flavour text: this shoves at less than half Cluck Shock's impulse and
    /// staggers for a fraction of Wing Slam's stun. What it buys instead is <b>frequency</b> —
    /// the lowest cooldown of any Warrior control tool.
    ///
    /// That is why it is the <b>Relentless</b> signature specifically. Relentless returns
    /// abilities faster, so it compounds with the one stat this ability actually competes on.
    /// Under Bully (the magnitude passive) it stays unimpressive, and that asymmetry is what
    /// makes the specialization choice read.
    /// </remarks>
    [CreateAssetMenu(fileName = "Headbutt",
        menuName = "Cluck Wars/Ability/Control/Headbutt", order = 7)]
    public sealed class HeadbuttAbilitySO : AbilityBaseSO
    {
        public HeadbuttAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
            BotRole = BotRole.Control;
            Duration = 0.25f;
            Cooldown = 4f;
        }

        [Tooltip("Forward reach of the shove. Short — this is a contact move, not a ranged poke.")]
        [Min(0.5f)] public float Reach = 3.6f;

        [Tooltip("Half-angle of the forward cone, in degrees.")]
        [Range(15f, 90f)] public float ConeAngle = 70f;

        [Tooltip("Knockback impulse. Well under Cluck Shock's 16.2 on purpose — the Warrior's "
               + "shove is meant to be the weaker version of the specialist's.")]
        [Min(1f)] public float KnockbackForce = 7.5f;

        [Tooltip("Stagger applied on contact. A hitch, not a stun — Wing Slam is the real one.")]
        [Range(0.05f, 1f)] public float StaggerSeconds = 0.35f;

        protected override string DefaultIcon => "🐏";

        public override float IndicatorRange => Reach;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AimShape => AbilityAimShape.Cone;
        public override float AimRadius => Reach;
        public override float AimConeAngle => ConeAngle;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var origin = caster.transform.position;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var victim = _scratch[i];

                var dir = victim.transform.position - origin;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = caster.transform.forward;

                victim.RPC_ApplyKnockback(dir.normalized * KnockbackForce);
                victim.RPC_ApplyStun(StaggerSeconds);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
