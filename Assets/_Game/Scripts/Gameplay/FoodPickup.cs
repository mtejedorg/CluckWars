using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// World-spawned food pickup. Drops at the position of a chicken that just died,
    /// carrying whatever <see cref="ChickenCargo"/> the chicken was holding. Any
    /// living chicken in range can collect it via <see cref="RPC_Drain"/>; despawns
    /// when empty or after <see cref="_despawnDelay"/> elapses (auto-cleanup so an
    /// unlucky map full of dropped food doesn't leak NetworkObjects).
    /// </summary>
    /// <remarks>
    /// Spawned by <c>ChickenCargo.HandleDeath</c> via <c>Runner.Spawn</c>; the dying
    /// chicken's StateAuthority owns the pickup. Initial <see cref="Amount"/> is set
    /// in the spawner's <c>onBeforeSpawned</c> callback so the value is replicated
    /// from the first tick on every peer (same pattern as <c>ChickenController.Class</c>).
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FoodPickup : NetworkBehaviour
    {
        private const string Source = "FoodPickup";

        [Tooltip("How close a chicken must be to drain this pickup.")]
        [Min(0f)]
        [SerializeField] private float _pickupRadius = 0.9f;

        [Tooltip("Auto-despawn after this many seconds even if uncollected. Prevents NetworkObject leaks across long matches.")]
        [Min(1f)]
        [SerializeField] private float _despawnDelay = 30f;

        [Networked] public float Amount { get; set; }
        [Networked] public float MaxAmount { get; set; }
        [Networked] private TickTimer ExpiryTimer { get; set; }

        public float PickupRadius => _pickupRadius;
        public bool IsEmpty => Amount <= 0f;

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            if (HasStateAuthority)
            {
                if (MaxAmount <= 0f) MaxAmount = Amount;
                ExpiryTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
                _log?.Debug(Source, $"{name}: Spawned with Amount={Amount:0.00}, despawn in {_despawnDelay}s.");
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (Amount <= 0f || ExpiryTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
        }

        /// <summary>
        /// Asks the pickup's StateAuthority to remove up to <paramref name="amount"/>
        /// of food. Caller is expected to credit its own cargo first; over-requests
        /// clamp to whatever's left. Pickup despawns once Amount hits zero.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Drain(float amount)
        {
            if (amount <= 0f || Amount <= 0f) return;
            var actual = Mathf.Min(amount, Amount);
            Amount -= actual;
            if (Amount < 0f) Amount = 0f;
            _log?.Verbose(Source, $"{name}: drained {actual:0.00} → {Amount:0.0}/{MaxAmount}.");
            if (Amount <= 0f)
            {
                Runner.Despawn(Object);
            }
        }
    }
}
