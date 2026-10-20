using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Where a static networked object (a base, a food pile) stands on peers that did not spawn it.
    /// </summary>
    /// <remarks>
    /// <b>Fusion 2 does not replicate the spawn position of a NetworkObject without a NetworkTransform.</b> The master calls
    /// <c>runner.Spawn(prefab, pos, ...)</c>; on every other peer the object is created at the prefab's own root position.
    /// PlayerBase.prefab's root sits at (9.47, 0, 9.38) and FoodPile.prefab's at the origin, so on every client all four
    /// bases stood at (9.5, 9.4) and all nine piles at the arena centre. Clients banked "at their base" there and the host,
    /// which sees the chicken where it really is, rejected every deposit (4-seat test session 2026-10-20). The fix is the
    /// same one the piles already use for their yaw: the spawner stamps the position into a <c>[Networked]</c> property in
    /// <c>onBeforeSpawned</c> (replicated from tick zero) and every peer that is not the state authority moves the root
    /// there. Pure rule, tested.
    /// </remarks>
    public static class SpawnPlacement
    {
        /// <summary>True when this peer must move the object to its stamped spawn position.</summary>
        public static bool ProxyMustMove(bool hasStateAuthority, bool stamped, Vector3 stampedPosition, Vector3 current) =>
            !hasStateAuthority && stamped && (stampedPosition - current).sqrMagnitude > 1e-6f;
    }
}
