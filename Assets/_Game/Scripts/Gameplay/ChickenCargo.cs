using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked cargo: chicken's carried food, automatic collection from nearby
    /// <see cref="FoodPile"/>s, and deposit into the nearest <see cref="PlayerBase"/>.
    /// State authority drains piles by RPC and credits its own <see cref="Cargo"/>
    /// optimistically — pile clamps to its actual stock so any over-request is harmless.
    /// </summary>
    /// <remarks>
    /// Cargo is a <c>float</c> so a 1 unit/sec collection rate accumulates smoothly across
    /// 30 Hz ticks (each tick adds ~0.033). The HUD floors it for display.
    /// On death, cargo is zeroed — pickup-prefab drops are deferred to Phase 4b.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCargo : NetworkBehaviour
    {
        private const string Source = "Cargo";

        [Tooltip("Generous broadphase radius for finding piles/bases. Per-target ranges (FoodPile.CollectRadius / PlayerBase.DepositRadius) gate the actual interaction.")]
        [Min(0.5f)]
        [SerializeField] private float _searchRadius = 5f;

        [Tooltip("Layers searched for piles and bases. Default = Everything; tighten once a Pickup layer is authored.")]
        [SerializeField] private LayerMask _searchMask = ~0;

        [Networked] public float Cargo { get; set; }

        public float Capacity => _controller != null && _controller.Stats != null ? _controller.Stats.CargoCapacity : 0f;
        public float Fraction => Capacity > 0f ? Mathf.Clamp01(Cargo / Capacity) : 0f;
        public bool IsFull => Cargo >= Capacity;

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ILogService _log;
        private bool _subscribedToDeath;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();

            if (_combat != null && !_subscribedToDeath)
            {
                _combat.OnDeath += HandleDeath;
                _subscribedToDeath = true;
            }

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null && _subscribedToDeath)
            {
                _combat.OnDeath -= HandleDeath;
                _subscribedToDeath = false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var stats = _controller != null ? _controller.Stats : null;
            if (stats == null) return;

            // Stunned chickens can't collect or deposit.
            if (_combat != null && _combat.IsStunned) return;

            TryCollectFromNearbyPile(stats);
            TryDepositAtNearbyBase();
        }

        private void TryCollectFromNearbyPile(ChickenStatsSO stats)
        {
            if (Cargo >= stats.CargoCapacity) return;

            var pile = FindNearestPileInRange();
            if (pile == null || pile.IsEmpty) return;

            float spaceLeft = stats.CargoCapacity - Cargo;
            float desired = stats.CollectionRate * Runner.DeltaTime;
            float takeable = Mathf.Min(desired, spaceLeft, pile.Amount);
            if (takeable <= 0f) return;

            Cargo += takeable;
            pile.RPC_Drain(takeable);
        }

        private void TryDepositAtNearbyBase()
        {
            if (Cargo <= 0f) return;

            var playerBase = FindNearestBaseInRange();
            if (playerBase == null) return;

            float dropped = Cargo;
            Cargo = 0f;
            playerBase.RPC_AddFood(dropped);
            _log?.Debug(Source, $"Deposited {dropped:0.00} at {playerBase.name}.");
        }

        private FoodPile FindNearestPileInRange()
        {
            var hits = Physics.OverlapSphere(transform.position, _searchRadius, _searchMask, QueryTriggerInteraction.Collide);
            FoodPile best = null;
            float bestSqr = float.MaxValue;
            foreach (var col in hits)
            {
                var pile = col.GetComponentInParent<FoodPile>();
                if (pile == null) continue;
                float sqr = (pile.transform.position - transform.position).sqrMagnitude;
                float r = pile.CollectRadius;
                if (sqr > r * r) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = pile;
                }
            }
            return best;
        }

        private PlayerBase FindNearestBaseInRange()
        {
            var hits = Physics.OverlapSphere(transform.position, _searchRadius, _searchMask, QueryTriggerInteraction.Collide);
            PlayerBase best = null;
            float bestSqr = float.MaxValue;
            foreach (var col in hits)
            {
                var b = col.GetComponentInParent<PlayerBase>();
                if (b == null) continue;
                float sqr = (b.transform.position - transform.position).sqrMagnitude;
                float r = b.DepositRadius;
                if (sqr > r * r) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = b;
                }
            }
            return best;
        }

        private void HandleDeath()
        {
            // Authority-only mutation: only the chicken's owner zeros its cargo.
            // OnDeath fires on every peer (it's driven by ChangeDetector on IsStunned),
            // so guard with HasStateAuthority.
            if (!HasStateAuthority) return;
            if (Cargo <= 0f) return;
            _log?.Info(Source, $"Death drop: {Cargo:0.00} cargo lost.");
            Cargo = 0f;
        }
    }
}
