using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// One-shot non-combat ability: find the nearest enemy chicken in range and yank
    /// a chunk of their cargo over to yours. Bypasses the fight entirely — the only
    /// utility ability in the pool. Uses <see cref="ChickenCargo.RPC_DrainStolen"/>
    /// for cross-authority cargo moves; the thief credits itself optimistically and
    /// the victim's authority clamps to whatever's actually there.
    /// </summary>
    /// <remarks>
    /// <see cref="OnDeactivate"/> is a no-op — the steal happens at activation time
    /// and the active duration is purely cosmetic (UI cooldown overlay timing).
    /// </remarks>
    [CreateAssetMenu(fileName = "SneakySteal", menuName = "Cluck Wars/Ability/Sneaky Steal", order = 6)]
    public sealed class SneakyStealAbilitySO : AbilityBaseSO
    {
        [Tooltip("How close an enemy chicken needs to be to steal from. Generous so it feels reliable.")]
        [Min(0.5f)] public float StealRange = 3f;

        [Tooltip("How much cargo to steal. Capped to the victim's actual cargo and the thief's free space.")]
        [Min(1f)] public float StealAmount = 5f;

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            float spaceLeft = thiefCargo.Capacity - thiefCargo.Cargo;
            if (spaceLeft <= 0f) return;

            // Find nearest enemy chicken with cargo on board.
            var hits = Physics.OverlapSphere(thief.transform.position, StealRange, ~0, QueryTriggerInteraction.Ignore);
            ChickenCargo target = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var other = hits[i].GetComponentInParent<ChickenCargo>();
                if (other == null || other == thiefCargo) continue;
                if (other.Cargo <= 0f) continue;
                float sqr = (other.transform.position - thief.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    target = other;
                }
            }
            if (target == null) return;

            // Optimistic credit: take the smaller of (StealAmount, victim's cargo, our free space).
            float take = Mathf.Min(StealAmount, target.Cargo, spaceLeft);
            if (take <= 0f) return;

            thiefCargo.Cargo += take;
            target.RPC_DrainStolen(take);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            // No persistent state to clear — the steal was a one-shot at activation.
        }
    }
}
