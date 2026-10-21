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

        /// <summary>
        /// The ONE thief-side gate for the steal-proof state: every caller of
        /// <c>RPC_DrainStolen</c> / <c>RPC_TransferAllToBountyBag</c> (Snatch, Sneaky Steal, Scrap,
        /// Dive Bomb, Spine Coat, Death Mark) asks this BEFORE crediting itself. Thieves credit
        /// optimistically ahead of the RPC, so skipping the call after crediting would create food.
        /// Death Mark credits nothing ahead (its loot is reported back by the victim), but still
        /// gates here so a shelled victim is not even asked to drain.
        /// </summary>
        public static bool CanStealFrom(ChickenController victim) => victim != null && !victim.IsStealProof;

        /// <summary>
        /// How much a thief gives back when the victim's authority refused a steal that was
        /// credited optimistically: the refused amount, never more than the thief holds and never
        /// negative.
        /// </summary>
        public static float RefundAmount(float refused, float thiefCargo) =>
            Mathf.Min(Mathf.Max(0f, refused), Mathf.Max(0f, thiefCargo));

        /// <summary>
        /// The most an Assassin execute may bank as looted cargo: the victim's carry capacity, or
        /// the cargo the assassin can see it holding if that is somehow larger. Both are values
        /// the assassin's own peer legitimately reads, never a number the victim's peer supplies.
        /// </summary>
        /// <remarks>
        /// Recorded by <c>ChickenCargo.ExpectExecuteLoot</c> and enforced by
        /// <c>RPC_CreditExecuteLoot</c>: the victim's authority reports what it actually drained,
        /// and the assassin banks <c>min(report, this cap)</c>. A forged or inflated report is
        /// therefore bounded by one full load of that victim, once per execute.
        /// </remarks>
        public static float ExecuteLootCap(float victimCapacity, float visibleVictimCargo) =>
            Mathf.Max(0f, Mathf.Max(victimCapacity, visibleVictimCargo));

        /// <summary>
        /// How long an execute's loot expectation stays payable. Longer than the steal refund's
        /// <see cref="PendingStealLedger{TKey}.WindowSeconds"/> because the report makes two
        /// relayed trips in Shared Mode (assassin -> victim's authority -> assassin), each through
        /// the Photon relay, rather than one round trip.
        /// </summary>
        public const float ExecuteLootWindowSeconds = 2.5f;

        /// <summary>The ledger an assassin's cargo keeps its pending executes in.</summary>
        public static PendingStealLedger<TKey> NewExecuteLootLedger<TKey>() where TKey : struct =>
            new PendingStealLedger<TKey>(ExecuteLootWindowSeconds);

        /// <summary>
        /// Assassin side, at press time: records that a loot report from <paramref name="victim"/>
        /// is expected, worth at most <see cref="ExecuteLootCap"/>.
        /// </summary>
        /// <returns>False (nothing recorded) when the cap is 0 - no report from that victim can pay.</returns>
        public static bool RecordExecuteExpectation<TKey>(PendingStealLedger<TKey> ledger, TKey victim,
            float victimCapacity, float visibleVictimCargo, float now, out float cap) where TKey : struct
        {
            cap = ExecuteLootCap(victimCapacity, visibleVictimCargo);
            if (ledger == null || cap <= 0f) return false;
            ledger.Record(victim, cap, now);
            return true;
        }

        /// <summary>True when a loot report's amount is a positive finite number.</summary>
        public static bool IsPlausibleLootReport(float reported) =>
            reported > 0f && !float.IsInfinity(reported);

        /// <summary>
        /// Assassin side, when the victim's report arrives: how much of
        /// <paramref name="reported"/> may be banked. Matches it against a pending expectation for
        /// <paramref name="victim"/> (consumed, once, inside the window) and clamps to its cap.
        /// </summary>
        /// <returns>False (and 0) for an implausible amount or when no expectation is pending.</returns>
        public static bool TryResolveExecuteLoot<TKey>(PendingStealLedger<TKey> ledger, TKey victim,
            float reported, float now, out float credited) where TKey : struct
        {
            credited = 0f;
            if (ledger == null || !IsPlausibleLootReport(reported)) return false;
            return ledger.TryConsume(victim, reported, now, out credited);
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
