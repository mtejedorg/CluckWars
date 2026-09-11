using System.Collections.Generic;
using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Receiver-side rules that decide whether a <c>ChickenCargo.RPC_DrainStolen</c> call is a
    /// real steal. Pure static maths over authored data, deliberately free of Fusion types, so
    /// the EditMode suite can exercise every branch without a live <c>NetworkRunner</c> — the
    /// same split <see cref="BaseDepositRules"/>, <see cref="StealMath"/>, <c>BotTactics</c>
    /// and <c>AbilityAim</c> already use.
    /// </summary>
    /// <remarks>
    /// <c>RPC_DrainStolen</c> is <c>RpcSources.All</c>, so any connected peer can call it on any
    /// chicken. Its only guard used to be <c>amount &gt; 0</c>, which put a compromised client
    /// one line away from <c>someChicken.Cargo.RPC_DrainStolen(9999)</c> and an emptied rival
    /// from across the map. Two independent facts have to hold for a steal to be real, and each
    /// has a predicate here: the amount is a plausible single hit, and the named thief was close
    /// enough to land one.
    /// <para>
    /// <b>Both bounds are derived from the shipped ability pool, never authored here.</b>
    /// <see cref="BaseDepositRules"/> could name a single constant because exactly one ability
    /// moves the deposit rate; four abilities steal, with different reaches and amounts, and the
    /// receiver cannot know which one fired. So the bound is the worst case across the whole
    /// pool — see <see cref="MaxSingleSteal"/> and <see cref="MaxReach"/>.
    /// </para>
    /// </remarks>
    public static class StealRules
    {
        /// <summary>
        /// The largest cargo any single <c>RPC_DrainStolen</c> call can legitimately carry:
        /// the biggest nominal steal in <paramref name="pool"/>, with the biggest steal-scaling
        /// passive in the same pool applied on top.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately the product of two independent maxima, not of a legal pairing.</b>
        /// Today's biggest steal (Dive Bomb, 8) is Warrior-only and today's biggest multiplier
        /// (Thief, 1.6) is Assassin-only, so no chicken can actually reach 12.8 — 11.2 is the
        /// real ceiling. Teaching this method about class legality would make a security bound
        /// depend on the loadout-composition rules, which change for balance reasons; a
        /// conservative superset costs an attacker 1.6 food of slack on a check they still
        /// cannot pass with a fabricated number.
        /// <para>
        /// Returns 0 for an empty or steal-free pool. The caller must read that as "no honest
        /// bound could be derived" and skip the check rather than reject everything — see
        /// <c>ChickenCargo.ResolveStealBounds</c>.
        /// </para>
        /// </remarks>
        public static float MaxSingleSteal(IEnumerable<AbilityBaseSO> pool)
        {
            float maxAmount = 0f;
            float maxMultiplier = 1f;

            if (pool != null)
            {
                foreach (var ability in pool)
                {
                    if (ability == null) continue;
                    maxAmount = Mathf.Max(maxAmount, ability.NominalStealAmount);
                    if (ability is PassiveAbilitySO passive)
                        maxMultiplier = Mathf.Max(maxMultiplier, passive.MaxStealMultiplier);
                }
            }

            return maxAmount * maxMultiplier;
        }

        /// <summary>True when <paramref name="amount"/> could have come from one real steal.</summary>
        public static bool IsPlausibleAmount(float amount, float maxSingleSteal) =>
            amount > 0f && amount <= maxSingleSteal;

        /// <summary>
        /// The furthest any steal ability in <paramref name="pool"/> can be from its victim at
        /// the moment its RPC is applied. Returns 0 for a pool with no steal abilities.
        /// </summary>
        public static float MaxReach(IEnumerable<AbilityBaseSO> pool)
        {
            float max = 0f;
            if (pool == null) return max;

            foreach (var ability in pool)
                max = Mathf.Max(max, Reach(ability));

            return max;
        }

        /// <summary>
        /// How far apart <paramref name="ability"/>'s caster and victim can be when its steal
        /// lands. 0 for an ability that does not steal.
        /// </summary>
        /// <remarks>
        /// <b>Not just the authored reach — the caster may have moved since it was measured.</b>
        /// <c>AbilityController.TryActivate</c> normally jumps first and scans after, so the
        /// shape is resolved from the pose the RPC is sent in. A
        /// <see cref="AbilityBaseSO.CastPoseIsUnreconstructable"/> ability is the exception:
        /// its lane resolves from the take-off pose and it teleports <i>after</i>
        /// <c>OnActivate</c> has already sent the RPC, so by the time the victim's authority
        /// applies it the thief is a whole jump further on. Dive Bomb is the ability this
        /// exists for — a bound measured against plain <see cref="AbilityBaseSO.AimRadius"/>
        /// would silently reject every real Dive Bomb steal.
        /// <para>
        /// Keyed off <see cref="AbilityBaseSO.CastPoseIsUnreconstructable"/> rather than off an
        /// ability's type, so a future gap-closing steal inherits the allowance without anyone
        /// remembering to special-case it.
        /// </para>
        /// </remarks>
        public static float Reach(AbilityBaseSO ability)
        {
            if (ability == null || ability.NominalStealAmount <= 0f) return 0f;

            // Total forward reach of every aim shape is radius + forward offset — see
            // AbilityBaseSO.AimForwardOffset for why one field serves both readings.
            float shapeReach = Mathf.Max(0f, ability.AimRadius) + Mathf.Max(0f, ability.AimForwardOffset);
            return shapeReach + (ability.CastPoseIsUnreconstructable ? MaxJumpTravel(ability.JumpTier) : 0f);
        }

        /// <summary>
        /// The furthest a <paramref name="tier"/> jump can actually move a chicken: the tier's
        /// nominal distance plus the tolerance <c>JumpResolver.Resolve</c> is allowed to extend
        /// by while searching for a clear landing. Composed from <see cref="JumpResolver"/>'s
        /// own primitives so it cannot drift from what a jump really does.
        /// </summary>
        public static float MaxJumpTravel(JumpLengthTier tier)
        {
            float nominal = JumpResolver.GetNominalDistance(tier);
            return nominal <= 0f ? 0f : nominal + JumpResolver.GetMaxExtension(nominal);
        }

        /// <summary>
        /// How far past <see cref="MaxReach"/> a legitimate thief and victim may have drifted
        /// apart by the time the steal is applied. Both ends move, so
        /// <paramref name="combinedDriftSpeed"/> is the sum of the two chickens' speeds; the
        /// only time they have to separate is the message's flight time.
        /// </summary>
        /// <remarks>
        /// Rejecting that drift would drop real steals, which is a worse failure than an
        /// attacker gaining a couple of metres of slack on a check they still cannot pass from
        /// across the arena — the same trade <see cref="BaseDepositRules.RangeMargin"/> makes.
        /// There is no flush window to add here: a steal is a one-shot at activation, not a
        /// batch accrued over time.
        /// </remarks>
        public static float RangeMargin(float combinedDriftSpeed, float latencySeconds) =>
            Mathf.Max(0f, combinedDriftSpeed) * Mathf.Max(0f, latencySeconds);

        /// <summary>
        /// Planar (XZ) proximity test, matching the aim shapes it is bounding —
        /// <see cref="AbilityAim"/> is planar by construction for the same reason
        /// <c>ChickenCargo.HorizontalSqr</c> is: a chicken's pivot floats about a capsule
        /// half-height up, and a full 3D distance would push an in-range pair outside a tuned
        /// radius.
        /// </summary>
        public static bool IsWithinStealRange(
            Vector3 thiefPosition, Vector3 victimPosition, float maxReach, float margin)
        {
            float limit = Mathf.Max(0f, maxReach) + Mathf.Max(0f, margin);
            float dx = thiefPosition.x - victimPosition.x;
            float dz = thiefPosition.z - victimPosition.z;
            return dx * dx + dz * dz <= limit * limit;
        }
    }
}
