using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Centralized registry of every NetworkObject prefab the game spawns at
    /// runtime. Replaces scattered SerializeField slots on
    /// <c>MatchBootstrapper</c> / <c>MapGenerator</c> / <c>ChickenCargo</c> so
    /// reassigning a prefab is a one-stop operation. Bound app-wide in
    /// <c>ProjectInstaller</c>.
    /// </summary>
    /// <remarks>
    /// Sibling-SO to <c>AudioRegistrySO</c> and the (still-deferred) <c>ColorSchemeSO</c>
    /// — the asset-consolidation pattern from TDD §6.6. Consumers fall back to
    /// their existing SerializeField slot if the registry field is null, so the
    /// migration is gradual: drop a registry asset to centralize, leave a slot
    /// empty to keep using the per-component override.
    /// </remarks>
    [CreateAssetMenu(fileName = "PrefabRegistry", menuName = "Cluck Wars/Prefab Registry", order = 6)]
    public sealed class PrefabRegistrySO : ScriptableObject
    {
        [Header("Player")]
        public NetworkObject Chicken;
        public NetworkObject Doppelganger;

        [Header("World")]
        public NetworkObject FoodPile;
        public NetworkObject FoodPickup;
        public NetworkObject PlayerBase;

        [Header("Match")]
        public NetworkObject GameManager;
    }
}
