using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// A networked deposit zone. Chickens within <see cref="DepositRadius"/> dump their
    /// cargo into <see cref="FoodTotal"/> via <see cref="RPC_AddFood"/>. State authority
    /// owns the running total — Phase 7 will read this for win-condition checks.
    /// </summary>
    /// <remarks>
    /// Phase 4 ships a single shared base for solo testing. Per-player ownership and
    /// proper allegiance checks land alongside the win condition in Phase 7.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerBase : NetworkBehaviour
    {
        private const string Source = "PlayerBase";

        [Tooltip("How close a chicken must be to deposit. Stays generous for placeholder geometry.")]
        [Min(0f)]
        [SerializeField] private float _depositRadius = 2.5f;

        [Networked] public float FoodTotal { get; set; }
        [Networked] public PlayerRef Owner { get; set; }

        /// <summary>
        /// Which corner this base represents (0..3). Set by <c>MapGenerator</c> at
        /// spawn time via <c>onBeforeSpawned</c>; pairs with
        /// <c>MapGenerator.SpawnPoints[CornerIndex]</c> so a player joining at
        /// corner N is also assigned the base at corner N.
        ///
        /// <c>GameManager.AssignBasesToPlayers</c> looks for a CornerIndex match
        /// first and falls back to first-unowned, so scene-baked legacy bases
        /// (which all default to 0) still receive an owner.
        /// </summary>
        [Networked] public int CornerIndex { get; set; }

        public float DepositRadius => _depositRadius;

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            _log?.Debug(Source, $"{name}: Spawned. HasStateAuthority={HasStateAuthority}, Owner={Owner}.");
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddFood(float amount)
        {
            if (amount <= 0f) return;
            FoodTotal += amount;
            _log?.Debug(Source, $"{name}: +{amount:0.00} → FoodTotal={FoodTotal:0.0}.");
        }
    }
}
