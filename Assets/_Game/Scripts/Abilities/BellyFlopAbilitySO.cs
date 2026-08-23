using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Fatty.</b> Launches his whole mass forward and lands hard enough to floor everyone
    /// nearby.
    /// </summary>
    /// <remarks>
    /// <b>This is the justified jump.</b> Maestro: Fatty's leaps "should be justified in the
    /// game narrative, knowing the big size and the low agility." So it is not a hop over an
    /// obstacle — it is a heavy body thrown forward and dropped. The tuning carries that
    /// reading rather than the fiction carrying it:
    ///
    /// <list type="bullet">
    ///   <item><b>Long wind-up</b> (<c>Duration</c>) so it is visibly telegraphed and can be
    ///   walked away from. A nimble class would get this instantly; Fatty does not.</item>
    ///   <item><b>Short jump tier</b> — the smallest in the ladder. He travels the least
    ///   distance of anyone who can leave the ground.</item>
    ///   <item><b>The payoff is on the landing</b>, not the travel. Distance is the price, the
    ///   stun is the product.</item>
    /// </list>
    ///
    /// <c>TerrainTraversal.Vault</c> is the deliberate exception to Fatty being ground-bound:
    /// mass is the justification. Note this is exactly the traversal Speedy is denied, and the
    /// asymmetry is intended — Speedy would pay nothing for it, Fatty pays a 10 s cooldown and
    /// a telegraph.
    ///
    /// <c>ResolvesBeforeJump</c> is NOT set: unlike Dive Bomb, whose peck lane must resolve at
    /// the press point, this ability's whole purpose is to hit what is at the DESTINATION.
    /// </remarks>
    [CreateAssetMenu(fileName = "BellyFlop",
        menuName = "Cluck Wars/Ability/Control/Belly Flop", order = 7)]
    public sealed class BellyFlopAbilitySO : AbilityBaseSO
    {
        public BellyFlopAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
            BotRole = BotRole.Offense;
            TerrainTraversal = TerrainTraversal.Vault;
            JumpTier = JumpLengthTier.Short;
            Duration = 0.7f;
            Cooldown = 10f;
        }

        [Tooltip("Radius of the landing shockwave, measured where he comes down.")]
        [Min(0.5f)] public float ImpactRadius = 4.0f;

        [Tooltip("Stun applied to everyone caught by the landing.")]
        [Min(0.1f)] public float StunSeconds = 1.4f;

        [Tooltip("Outward shove on landing. Modest — the stun is the point; this is the thump "
               + "that sells the weight.")]
        [Min(0f)] public float KnockbackForce = 6f;

        protected override string DefaultIcon => "🫃";

        public override float IndicatorRange => ImpactRadius;

        /// <summary>
        /// False: this is a gap-closer as well as a stun. Refusing to fire because nobody is in
        /// range yet would hold the mobility half hostage to the control half — the same bug
        /// that made Dive Bomb unusable (see <see cref="ZeroHitsIsAWhiff"/>).
        /// </summary>
        public override bool RequiresEnemyInRange => false;

        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => ImpactRadius;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var landing = caster.transform.position;

            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var victim = _scratch[i];
                victim.RPC_ApplyStun(StunSeconds);

                if (KnockbackForce <= 0f) continue;

                var dir = victim.transform.position - landing;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = caster.transform.forward;
                victim.RPC_ApplyKnockback(dir.normalized * KnockbackForce);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}
