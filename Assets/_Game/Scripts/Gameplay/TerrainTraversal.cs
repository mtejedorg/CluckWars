namespace CluckWars.Gameplay
{
    /// <summary>
    /// The SKIP corner of ADR 0003 Decision 1's mobility triangle: what terrain an
    /// ability lets its caster cross while it is active. Authored per ability on
    /// <c>AbilityBaseSO</c>; read by <see cref="TraversalRules"/>.
    ///
    /// Backing type is <c>byte</c> to match <see cref="ObstacleClass"/> and the
    /// project's other gameplay enums. Order is stable — append new tiers at the
    /// end, do NOT renumber (assets serialize the integer).
    /// </summary>
    public enum TerrainTraversal : byte
    {
        /// <summary>Default. The ability grants no terrain traversal at all.</summary>
        None  = 0,
        /// <summary>Arcs over Low and Standard obstacles. Blocked by Tall. (Flying Peck)</summary>
        Vault = 1,
        /// <summary>Plows through Low obstacles only. (Roll &amp; Push)</summary>
        Barge = 2,
        /// <summary>Crosses every obstacle class — the only answer to Tall. (Doppelganger)</summary>
        Blink = 3,
    }

    /// <summary>
    /// ADR 0003 Decision 5's traversal table, as a total function of
    /// (<see cref="TerrainTraversal"/>, <see cref="ObstacleClass"/>). Pure — no
    /// Unity types, no state — so the whole matrix is pinned by EditMode tests.
    /// </summary>
    /// <remarks>
    /// | Class    | Run | Vault | Barge | Blink |
    /// |----------|-----|-------|-------|-------|
    /// | Low      |  ✗  |   ✓   |   ✓   |   ✓   |
    /// | Standard |  ✗  |   ✓   |   ✗   |   ✓   |
    /// | Tall     |  ✗  |   ✗   |   ✗   |   ✓   |
    /// | Pile     |  ✗  |   ✓   |   ✓   |   ✓   |
    /// | Boundary |  ✗  |   ✗   |   ✗   |   ✗   |
    ///
    /// <b>Boundary walls are deliberately absent from this API.</b> There is no
    /// tier that clears them and there must never be one — arena containment is
    /// non-negotiable. That is enforced structurally rather than by a rule here:
    /// <c>MapGenerator.CreateWall</c> produces a bare collider with neither a
    /// <see cref="TerrainObstacle"/> nor a <see cref="FoodPile"/> above it, and
    /// <c>ChickenTraversal.IsClearable</c> only ever clears colliders carrying one
    /// of those two components. A boundary wall therefore cannot enter the
    /// ignored-collider set even if someone adds a tier that clears everything.
    /// </remarks>
    public static class TraversalRules
    {
        /// <summary>
        /// Can <paramref name="tier"/> cross an obstacle tagged
        /// <paramref name="obstacle"/>? Total over both enums.
        /// </summary>
        public static bool Clears(TerrainTraversal tier, ObstacleClass obstacle) => tier switch
        {
            TerrainTraversal.Vault => obstacle == ObstacleClass.Low || obstacle == ObstacleClass.Standard,
            TerrainTraversal.Barge => obstacle == ObstacleClass.Low,
            TerrainTraversal.Blink => true,
            _                      => false, // None, and any tier added without a rule
        };

        /// <summary>
        /// Can <paramref name="tier"/> cross a food pile's blocker? Piles sit in the
        /// same row as <see cref="ObstacleClass.Low"/> in ADR 0003 Decision 5 — every
        /// tier clears them — but they are a separate question because a pile carries
        /// no <see cref="TerrainObstacle"/> tag (it is consumable terrain, not part of
        /// the permanent vocabulary).
        /// </summary>
        public static bool ClearsPiles(TerrainTraversal tier) =>
            Clears(tier, ObstacleClass.Low);
    }
}
