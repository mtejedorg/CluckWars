using CluckWars.Audio;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

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
    /// 32 Hz ticks (each tick adds ~0.031). The HUD floors it for display.
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

        /// <summary>
        /// True when this chicken is currently depositing cargo at their own base.
        /// </summary>
        public bool IsDepositing
        {
            get
            {
                if (Cargo <= 0f) return false;
                if (_combat != null && _combat.IsStunned) return false;
                return FindNearestBaseInRange() != null;
            }
        }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private MatchConfigSO _matchConfig;
        private bool _subscribedToDeath;

        // Accumulators for batched RPCs (Stage D)
        private FoodPile _activePileTarget;
        private float _pendingPileDrain;
        private int _pileDrainTicks;

        private FoodPickup _activePickupTarget;
        private float _pendingPickupDrain;
        private int _pickupDrainTicks;

        private PlayerBase _activeBaseTarget;
        private float _pendingBaseFood;
        private int _baseDepositTicks;

        // Static array for broadphase overlaps to prevent per-tick allocation
        private static readonly Collider[] _overlapHits = new Collider[16];

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry, MatchConfigSO matchConfig)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
            _matchConfig = matchConfig;
        }

        private NetworkObject ResolveFoodPickupPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.FoodPickup != null) return _prefabRegistry.FoodPickup;
            return _foodPickupPrefab;
        }

        public override void Spawned()
        {
            ActiveCargos.Add(this);
            if (_log == null)
            {
                var sceneCtx = FindFirstObjectByType<SceneContext>();
                if (sceneCtx != null)
                    sceneCtx.Container.Inject(this);
                else
                    ProjectContext.Instance.Container.Inject(this);
            }

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();

            if (_combat != null && !_subscribedToDeath)
            {
                _combat.OnDeathAuthority += HandleDeath;
                _subscribedToDeath = true;
            }

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveCargos.Remove(this);
            if (_combat != null && _subscribedToDeath)
            {
                _combat.OnDeathAuthority -= HandleDeath;
                _subscribedToDeath = false;
            }
        }

        public void FlushPileDrain()
        {
            if (_pendingPileDrain > 0f)
            {
                if (_activePileTarget != null && _activePileTarget.Object != null && _activePileTarget.Object.IsValid)
                {
                    _activePileTarget.RPC_Drain(_pendingPileDrain);
                }
                _pendingPileDrain = 0f;
            }
            _activePileTarget = null;
            _pileDrainTicks = 0;
        }

        public void FlushPickupDrain()
        {
            if (_pendingPickupDrain > 0f)
            {
                if (_activePickupTarget != null && _activePickupTarget.Object != null && _activePickupTarget.Object.IsValid)
                {
                    _activePickupTarget.RPC_Drain(_pendingPickupDrain);
                }
                _pendingPickupDrain = 0f;
            }
            _activePickupTarget = null;
            _pickupDrainTicks = 0;
        }

        public void FlushBaseDeposit()
        {
            if (_pendingBaseFood > 0f)
            {
                if (_activeBaseTarget != null && _activeBaseTarget.Object != null && _activeBaseTarget.Object.IsValid)
                {
                    _activeBaseTarget.RPC_AddFood(_pendingBaseFood);
                    GetComponent<ChickenMatchStats>()?.RPC_AddDeposit(_pendingBaseFood);
                }
                _pendingBaseFood = 0f;
            }
            _activeBaseTarget = null;
            _baseDepositTicks = 0;
        }

        public void FlushAll()
        {
            FlushPileDrain();
            FlushPickupDrain();
            FlushBaseDeposit();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var stats = _controller != null ? _controller.Stats : null;
            if (stats == null)
            {
                FlushAll();
                return;
            }

            // Stunned chickens can't collect or deposit.
            if (_combat != null && _combat.IsStunned)
            {
                FlushAll();
                return;
            }

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

            if (pile == null)
            {
                FlushPileDrain();
                return;
            }

            if (pile != _activePileTarget)
            {
                FlushPileDrain();
                _activePileTarget = pile;
            }

            if (Cargo >= stats.CargoCapacity)
            {
                _log?.Verbose(Source, $"TryCollect: cargo full ({Cargo:0.0}/{stats.CargoCapacity}).");
                FlushPileDrain();
                return;
            }

            if (pile.IsEmpty)
            {
                _log?.Verbose(Source, $"TryCollect: nearest pile '{pile.name}' is empty.");
                FlushPileDrain();
                return;
            }

            float spaceLeft = stats.CargoCapacity - Cargo;
            float collectionRate = stats.CollectionRate;
            if (_controller != null && _controller.UnderdogSurgeActive)
            {
                collectionRate *= 1.5f;
            }
            float desired = collectionRate * Runner.DeltaTime;

            float availableInPile = Mathf.Max(0f, pile.Amount - _pendingPileDrain);
            float takeable = Mathf.Min(desired, spaceLeft, availableInPile);
            if (takeable <= 0f)
            {
                FlushPileDrain();
                return;
            }

            Cargo += takeable;
            _pendingPileDrain += takeable;
            _pileDrainTicks++;

            int ticksToFlush = Mathf.RoundToInt(0.25f * Runner.TickRate);
            if (_pileDrainTicks >= ticksToFlush)
            {
                FlushPileDrain();
            }

            _log?.Verbose(Source, $"Collected {takeable:0.000} from '{pile.name}'. Cargo={Cargo:0.0}/{stats.CargoCapacity}.");
        }

        private void TryCollectFromNearbyPickup(ChickenStatsSO stats)
        {
            var pickup = FindNearestPickupInRange();
            if (pickup == null)
            {
                FlushPickupDrain();
                return;
            }

            if (pickup != _activePickupTarget)
            {
                FlushPickupDrain();
                _activePickupTarget = pickup;
            }

            if (Cargo >= stats.CargoCapacity)
            {
                FlushPickupDrain();
                return;
            }

            if (pickup.IsEmpty)
            {
                FlushPickupDrain();
                return;
            }

            float spaceLeft = stats.CargoCapacity - Cargo;
            float availableInPickup = Mathf.Max(0f, pickup.Amount - _pendingPickupDrain);
            float takeable = Mathf.Min(availableInPickup, spaceLeft);
            if (takeable <= 0f)
            {
                FlushPickupDrain();
                return;
            }

            Cargo += takeable;
            _pendingPickupDrain += takeable;
            _pickupDrainTicks++;

            int ticksToFlush = Mathf.RoundToInt(0.25f * Runner.TickRate);
            if (_pickupDrainTicks >= ticksToFlush)
            {
                FlushPickupDrain();
            }

            _audio?.PlaySFX(_audioReg != null ? _audioReg.Pickup : null);
            _log?.Verbose(Source, $"Picked up {takeable:0.00} from {pickup.name}.");
        }

        private void TryDepositAtNearbyBase()
        {
            var playerBase = FindNearestBaseInRange();
            if (playerBase == null)
            {
                FlushBaseDeposit();
                if (Cargo > 0f)
                {
                    _log?.Verbose(Source, $"TryDeposit: carrying {Cargo:0.0} but no owned base in range " +
                        $"(owner={Object.InputAuthority}, pos={transform.position}).");
                }
                return;
            }

            if (playerBase != _activeBaseTarget)
            {
                FlushBaseDeposit();
                _activeBaseTarget = playerBase;
            }

            if (Cargo <= 0f)
            {
                FlushBaseDeposit();
                return;
            }

            if (playerBase.IsClaimed && playerBase.Owner != Object.InputAuthority && !(_controller != null && _controller.IsBot))
            {
                _log?.Warn(Source, $"Assert: base '{playerBase.name}' is claimed by {playerBase.Owner} but player {Object.InputAuthority} is depositing into it.");
            }

            float rate = _matchConfig != null ? _matchConfig.DepositRatePerSecond : 6f;
            float transfer = Mathf.Min(Cargo, rate * Runner.DeltaTime);
            if (transfer <= 0f)
            {
                FlushBaseDeposit();
                return;
            }

            Cargo -= transfer;
            _pendingBaseFood += transfer;
            _baseDepositTicks++;

            int ticksToFlush = Mathf.RoundToInt(0.25f * Runner.TickRate);
            if (_baseDepositTicks >= ticksToFlush || Cargo <= 0f)
            {
                bool isDone = (Cargo <= 0f);
                FlushBaseDeposit();
                if (isDone)
                {
                    Cargo = 0f;
                    _audio?.PlaySFX(_audioReg != null ? _audioReg.Deposit : null);
                    _log?.Debug(Source, $"Deposited complete at {playerBase.name}.");
                }
            }
        }

        /// <summary>
        /// Squared horizontal (XZ) distance. Collection/pickup/deposit proximity is a
        /// ground-plane test: a chicken's pivot floats ~1 unit above pile/base pivots
        /// (capsule centre), so a full 3D distance pushes an in-range chicken outside the
        /// tuned radius and the interaction silently never fires. Playtest evidence: bots
        /// parked ~1.45 horizontal from a 1.6-radius pile, but 3D distance was ~1.8 → no
        /// pile ever drained, no one could score. Comparing XZ only fixes it robustly.
        /// </summary>
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
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
                float sqr = HorizontalSqr(pile.transform.position, transform.position);
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
                if (pickup == null) continue;
                // Physics can hand back a pickup whose NetworkObject isn't live
                // (just despawned by RestartMatch, or not yet Spawned) — touching
                // its [Networked] Amount then throws InvalidOperationException.
                if (pickup.Object == null || !pickup.Object.IsValid) continue;
                if (pickup.IsEmpty) continue;
                float sqr = HorizontalSqr(pickup.transform.position, transform.position);
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
            // Deposit gate is the chicken's home corner, not InputAuthority — bots all
            // share [Player:None] authority, so an owner comparison would let every bot
            // deposit at every unowned base (pooled, misattributed scores).
            var bases = PlayerBase.ActiveBases;
            int homeCorner = _controller != null ? _controller.HomeCornerIndex : -1;
            if (homeCorner < 0) return null;
            PlayerBase best = null;
            float  bestSqr  = float.MaxValue;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                if (b.CornerIndex != homeCorner) continue;
                float sqr = HorizontalSqr(b.transform.position, transform.position);
                float r   = b.DepositRadius;
                if (sqr > r * r) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = b; }
            }
            return best;
        }

        private void HandleDeath(NetworkBehaviourId attackerId)
        {
            // Subscribed to OnDeathAuthority — fires synchronously on the
            // StateAuthority inside RPC_ApplyDamage, so the guard below is
            // redundant, but kept as cheap insurance.
            if (!HasStateAuthority) return;
            
            // Flush any pending collection/deposit before processing death drop.
            FlushAll();
            
            if (_controller != null && _controller.IsDecoy) return;

            Vector3 victimPos = transform.position;

            float dropped = Cargo;
            if (dropped > 0f)
            {
                Cargo = 0f;
                var pickupPrefab = ResolveFoodPickupPrefab();
                if (pickupPrefab != null)
                {
                    // Slight forward offset so the pickup doesn't spawn dead-center on the
                    // stunned chicken's collider; helps the dropping chicken not auto-collect
                    // it the instant stun ends.
                    var dropPosition = victimPos + transform.forward * 0.4f;
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
            }

            var pPrefab = ResolveFoodPickupPrefab();
            if (pPrefab != null)
            {
                // Kill Bounty (IP3): +5 food in pickups
                bool hasKiller = false;
                Vector3 killerPos = victimPos;
                if (attackerId != NetworkBehaviourId.None && attackerId != (_combat != null ? _combat.Id : NetworkBehaviourId.None))
                {
                    if (Runner.TryFindBehaviour(attackerId, out ChickenCombat attackerCombat))
                    {
                        killerPos = attackerCombat.transform.position;
                        hasKiller = true;
                    }
                }

                if (hasKiller)
                {
                    Vector3 toKiller = killerPos - victimPos;
                    float dist = toKiller.magnitude;
                    Vector3 dir = dist > 0.1f ? toKiller.normalized : transform.forward;
                    for (int i = 0; i < 5; i++)
                    {
                        float t = 0.5f + (i / 4f) * 0.3f; // 0.5 to 0.8
                        Vector3 spawnPos = victimPos + dir * (dist * t);
                        spawnPos += new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f));
                        SpawnSinglePickup(pPrefab, spawnPos, 1f);
                    }
                }
                else
                {
                    // No killer / self-kill
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = i * 72f * Mathf.Deg2Rad;
                        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.6f;
                        SpawnSinglePickup(pPrefab, victimPos + offset, 1f);
                    }
                }

                // Leader Bounty (IP2): +8 food in pickups
                if (_controller != null && _controller.LeaderBountyActive)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * 45f * Mathf.Deg2Rad;
                        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.8f;
                        SpawnSinglePickup(pPrefab, victimPos + offset, 1f);
                    }
                }
            }
        }

        private void SpawnSinglePickup(NetworkObject prefab, Vector3 position, float amount)
        {
            Runner.Spawn(
                prefab,
                position,
                Quaternion.identity,
                Object.StateAuthority,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var pickup = networkObject.GetComponent<FoodPickup>();
                    if (pickup != null)
                    {
                        pickup.Amount = amount;
                        pickup.MaxAmount = amount;
                    }
                });
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
            // Drop any pending drains/deposits because a new round starts
            _pendingPileDrain = 0f;
            _pendingPickupDrain = 0f;
            _pendingBaseFood = 0f;
            _activePileTarget = null;
            _activePickupTarget = null;
            _activeBaseTarget = null;
            _pileDrainTicks = 0;
            _pickupDrainTicks = 0;
            _baseDepositTicks = 0;

            Cargo = 0f;
            _log?.Debug(Source, "Reset for new match: Cargo=0.");
        }
    }
}
