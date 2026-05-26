using CluckWars.Audio;
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
    /// On death, the StateAuthority spawns a <see cref="FoodPickup"/> NetworkObject
    /// at the chicken's position carrying the cargo amount, then zeros local Cargo.
    /// Any chicken (including the dropper, after stun ends) can pick it up by walking
    /// over it.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCargo : NetworkBehaviour
    {
        private const string Source = "Cargo";

        public static readonly System.Collections.Generic.List<ChickenCargo> ActiveCargos = new System.Collections.Generic.List<ChickenCargo>();

        [Tooltip("Generous broadphase radius for finding piles/bases. Per-target ranges (FoodPile.CollectRadius / PlayerBase.DepositRadius) gate the actual interaction.")]
        [Min(0.5f)]
        [SerializeField] private float _searchRadius = 5f;

        [Tooltip("Layers searched for piles and bases. Default = Everything; tighten once a Pickup layer is authored.")]
        [SerializeField] private LayerMask _searchMask = ~0;

        [Tooltip("Legacy per-component override. If null, PrefabRegistry.FoodPickup is used. If both are null, cargo is silently lost on death.")]
        [SerializeField] private NetworkObject _foodPickupPrefab;

        [Networked] public float Cargo { get; set; }

        public float Capacity => _controller != null && _controller.Stats != null ? _controller.Stats.CargoCapacity : 0f;
        public float Fraction => Capacity > 0f ? Mathf.Clamp01(Cargo / Capacity) : 0f;
        public bool IsFull => Cargo >= Capacity;

        /// <summary>
        /// True when this chicken was standing on a non-empty food pile during the
        /// previous simulation tick. Read by <see cref="ChickenController"/> to apply
        /// the pile-slow source (GDD §6.2). One-tick lag is imperceptible; piles
        /// don't move.
        /// </summary>
        public bool IsPileSlow { get; private set; }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private bool _subscribedToDeath;

        // Static array for broadphase overlaps to prevent per-tick allocation
        private static readonly Collider[] _overlapHits = new Collider[16];

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
        }

        private NetworkObject ResolveFoodPickupPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.FoodPickup != null) return _prefabRegistry.FoodPickup;
            return _foodPickupPrefab;
        }

        public override void Spawned()
        {
            ActiveCargos.Add(this);
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
            ActiveCargos.Remove(this);
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
            TryCollectFromNearbyPickup(stats);
            TryDepositAtNearbyBase();
        }

        private void TryCollectFromNearbyPile(ChickenStatsSO stats)
        {
            var pile = FindNearestPileInRange();

            // IsPileSlow is true whenever the chicken is within a pile's collect
            // radius, regardless of cargo capacity. ChickenController reads this flag
            // the NEXT tick to apply the pile-slow source (GDD §6.2).
            IsPileSlow = (pile != null && !pile.IsEmpty);

            if (Cargo >= stats.CargoCapacity)
            {
                _log?.Verbose(Source, $"TryCollect: cargo full ({Cargo:0.0}/{stats.CargoCapacity}).");
                return;
            }

            if (pile == null)
            {
                _log?.Verbose(Source, "TryCollect: no pile in range.");
                return;
            }
            if (pile.IsEmpty)
            {
                _log?.Verbose(Source, $"TryCollect: nearest pile '{pile.name}' is empty.");
                return;
            }

            float spaceLeft = stats.CargoCapacity - Cargo;
            float desired = stats.CollectionRate * Runner.DeltaTime;
            float takeable = Mathf.Min(desired, spaceLeft, pile.Amount);
            if (takeable <= 0f) return;

            Cargo += takeable;
            pile.RPC_Drain(takeable);
            _log?.Verbose(Source, $"Collected {takeable:0.000} from '{pile.name}'. Cargo={Cargo:0.0}/{stats.CargoCapacity}.");
        }

        private void TryCollectFromNearbyPickup(ChickenStatsSO stats)
        {
            if (Cargo >= stats.CargoCapacity) return;

            var pickup = FindNearestPickupInRange();
            if (pickup == null || pickup.IsEmpty) return;

            float spaceLeft = stats.CargoCapacity - Cargo;
            float takeable = Mathf.Min(pickup.Amount, spaceLeft);
            if (takeable <= 0f) return;

            Cargo += takeable;
            pickup.RPC_Drain(takeable);
            _audio?.PlaySFX(_audioReg != null ? _audioReg.Pickup : null);
            _log?.Verbose(Source, $"Picked up {takeable:0.00} from {pickup.name}.");
        }

        private void TryDepositAtNearbyBase()
        {
            if (Cargo <= 0f) return;

            var playerBase = FindNearestBaseInRange();
            if (playerBase == null)
            {
                _log?.Verbose(Source, $"TryDeposit: carrying {Cargo:0.0} but no owned base in range " +
                    $"(owner={Object.InputAuthority}, pos={transform.position}).");
                return;
            }

            float dropped = Cargo;
            Cargo = 0f;
            playerBase.RPC_AddFood(dropped);
            // Track food deposited for the match-end stats overlay.
            GetComponent<ChickenMatchStats>()?.RPC_AddDeposit(dropped);
            _audio?.PlaySFX(_audioReg != null ? _audioReg.Deposit : null);
            _log?.Debug(Source, $"Deposited {dropped:0.00} at {playerBase.name}.");
        }

        private FoodPile FindNearestPileInRange()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _searchRadius, _overlapHits, _searchMask, QueryTriggerInteraction.Collide);
            FoodPile best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var col = _overlapHits[i];
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

        private FoodPickup FindNearestPickupInRange()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _searchRadius, _overlapHits, _searchMask, QueryTriggerInteraction.Collide);
            FoodPickup best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var col = _overlapHits[i];
                var pickup = col.GetComponentInParent<FoodPickup>();
                if (pickup == null || pickup.IsEmpty) continue;
                float sqr = (pickup.transform.position - transform.position).sqrMagnitude;
                float r = pickup.PickupRadius;
                if (sqr > r * r) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = pickup;
                }
            }
            return best;
        }

        private PlayerBase FindNearestBaseInRange()
        {
            var bases = PlayerBase.ActiveBases;
            var ownerRef = Object.InputAuthority;
            PlayerBase best = null;
            float  bestSqr  = float.MaxValue;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                if (b.Owner != ownerRef) continue;
                float sqr = (b.transform.position - transform.position).sqrMagnitude;
                float r   = b.DepositRadius;
                if (sqr > r * r) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = b; }
            }
            return best;
        }

        private void HandleDeath()
        {
            // Authority-only mutation: only the chicken's owner spawns the pickup
            // and zeros its own cargo. OnDeath fires on every peer (it's driven by
            // a ChangeDetector on IsStunned in ChickenCombat), so guard with HasStateAuthority.
            if (!HasStateAuthority) return;

            float dropped = Cargo;
            if (dropped <= 0f) return;
            Cargo = 0f;

            var pickupPrefab = ResolveFoodPickupPrefab();
            if (pickupPrefab == null)
            {
                _log?.Warn(Source, $"Death drop: {dropped:0.00} cargo lost — no FoodPickup prefab (neither PrefabRegistry nor legacy slot).");
                return;
            }

            // Slight forward offset so the pickup doesn't spawn dead-center on the
            // stunned chicken's collider; helps the dropping chicken not auto-collect
            // it the instant stun ends.
            var dropPosition = transform.position + transform.forward * 0.4f;
            Runner.Spawn(
                pickupPrefab,
                dropPosition,
                Quaternion.identity,
                Object.StateAuthority,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var pickup = networkObject.GetComponent<FoodPickup>();
                    if (pickup != null)
                    {
                        pickup.Amount = dropped;
                        pickup.MaxAmount = dropped;
                    }
                });

            _log?.Info(Source, $"Death drop: spawned FoodPickup with {dropped:0.00} food at {dropPosition}.");
        }

        /// <summary>
        /// Asks this chicken's StateAuthority to remove up to <paramref name="amount"/>
        /// from its <see cref="Cargo"/>. The thief credits its own cargo optimistically
        /// before calling this RPC; the victim's authority clamps to whatever's
        /// actually there, so any over-request is harmless. Used by Sneaky Steal.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_DrainStolen(float amount)
        {
            if (amount <= 0f || Cargo <= 0f) return;
            var actual = Mathf.Min(amount, Cargo);
            Cargo -= actual;
            if (Cargo < 0f) Cargo = 0f;
            _log?.Debug(Source, $"Stolen: -{actual:0.00} → Cargo={Cargo:0.0}.");
        }

        /// <summary>
        /// Reset cargo state for a new round. Called by <c>GameManager.RestartMatch</c>
        /// from the master client; routes to each chicken's StateAuthority.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            Cargo = 0f;
            _log?.Debug(Source, "Reset for new match: Cargo=0.");
        }
    }
}
