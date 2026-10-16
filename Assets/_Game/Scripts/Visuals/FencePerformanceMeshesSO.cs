using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Which mesh stands in for which fence mesh while Performance Mode is on (Phase 8 S5). The map ships the
    /// full 4 000-tri panel; with Performance Mode the ~3 000-tri protected-cap panel replaces it (the C1
    /// candidate: same pivot, bounds and UV layout, +0.037 % see-through of the fence silhouette at the match camera).
    /// </summary>
    /// <remarks>
    /// Bound project-wide by <c>ProjectInstaller</c> and injected into <see cref="FencePerformanceSwap"/> (added at
    /// runtime by <c>MatchCamera</c>). The perf meshes must be readable: the swap re-batches them at runtime.
    /// </remarks>
    [CreateAssetMenu(fileName = "FencePerformanceMeshes", menuName = "Cluck Wars/Fence Performance Meshes")]
    public sealed class FencePerformanceMeshesSO : ScriptableObject
    {
        [Tooltip("Outer fence panel (Wall_*) for Performance Mode: Assets/Generated/wall_segment_perf.fbx. Read/Write on.")]
        [SerializeField] private Mesh _outerPerformance;
        [Tooltip("Arm wall panel (InnerWall_*) for Performance Mode: Art/Meshes/wall_segment_inner_perf.asset. Read/Write on.")]
        [SerializeField] private Mesh _innerPerformance;

        public Mesh OuterPerformance => _outerPerformance;
        public Mesh InnerPerformance => _innerPerformance;

        /// <summary>The Performance Mode mesh for an arm wall (<paramref name="inner"/>) or an outer fence panel.</summary>
        public Mesh For(bool inner) => inner ? _innerPerformance : _outerPerformance;
    }
}
