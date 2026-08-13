using CluckWars.Audio;
using CluckWars.Logging;
using CluckWars.Visuals;
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
    /// Cargo is a <c>float</c> so partial units accumulate smoothly across 32 Hz ticks.
    /// The HUD floors it for display.
    /// <para>
    /// Nothing drops food on the ground any more. The only "death" in the game is the
    /// Assassin's execute, which transfers the victim's cargo straight into the killer's
    /// bounty bag and pays a flat execute bounty on top — see <c>AssassinExecute</c>. The
    /// old <c>FoodPickup</c> subsystem that this class used to spawn and collect from was
    /// deleted along with that change.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCargo : NetworkBehaviour
    {
        private const string Source = "Cargo";

        public static readonly System.Collections.Generic.List<ChickenCargo> ActiveCargos = new System.Collections.Generic.List<ChickenCargo>();

        [Tooltip("Generous broadphase radius for finding piles/bases. Per-target ranges (FoodPile.IsWithinCollectRange / PlayerBase.DepositRadius) gate the actual interaction.")]
        [Min(0.5f)]
        [SerializeField] private float _searchRadius = 5f;

        [Tooltip("Layers searched for piles and bases. Default = Everything; tighten once a Pickup layer is authored.")]
        [SerializeField] private LayerMask _searchMask = ~0;

        [Networked] public float Cargo { get; set; }
        [Networked] public float BountyBag { get; set; }
        /// <summary>
        /// Cargo the chicken can hold, after its class specialization has had a say.
        /// Hoarder raises it to a full win's worth; Bully adds enough to hold a bigger steal.
        /// </summary>
        public float Capacity
        {
            get
            {
                if (_controller == null || _controller.Stats == null) return 0f;
                float capacity = _controller.Stats.CargoCapacity;
                var passive = _controller.Passive;
                return passive != null ? passive.ModifyCargoCapacity(capacity, _controller) : capacity;
            }
        }

        /// <summary>Deposit rate after the specialization's say (Drop &amp; Go).</summary>
        private float ResolveDepositRate()
        {
            float rate = _matchConfig != null ? _matchConfig.DepositRatePerSecond : 6f;
            var passive = _controller != null ? _controller.Passive : null;
            return passive != null ? passive.ModifyDepositRate(rate, _controller) : rate;
        }
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
                if (Cargo <= 0f && BountyBag <= 0f) return false;
                if (_combat != null && _combat.IsRemoved) return false;
                return FindNearestBaseInRange() != null;
            }
        }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private HitFeedback _hitFeedback;
        private ILogService _log;

        // ---- Local cargo-delta observation (FEEDBACK.md §3.2, case 12) --------
        // Lives here rather than in a separate component precisely because Fusion's
        // ChangeDetector/PropertyReader pair only resolves for the behaviour that DECLARES
        // the [Networked] property — a detector rooted on a sibling would read nothing for
        // Cargo. Strictly observation: nothing below writes networked state, and the whole
        // block runs in Render() on every peer, so no RPC and no extra bytes on the wire.
        private ChangeDetector _cargoDetector;
        private PropertyReader<float> _cargoReader;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private MatchConfigSO _matchConfig;

        // Accumulators for batched RPCs (Stage D)
        private PlayerBase _activeBaseTarget;
        private float _pendingBaseFood;
        private int _baseDepositTicks;

        // Static array for broadphase overlaps to prevent per-tick allocation
        private static readonly Collider[] _overlapHits = new Collider[32];

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry, MatchConfigSO matchConfig)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
            _matchConfig = matchConfig;
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
            _hitFeedback = GetComponent<HitFeedback>();

            _cargoDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _cargoReader = GetPropertyReader<float>(nameof(Cargo));

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveCargos.Remove(this);
        }

        /// <summary>
        /// Local-only: turns the replicated <see cref="Cargo"/> value into the §3.2
        /// <c>-5 🌽</c> / <c>+5 🌽</c> floating numbers, and feeds a *loss* into
        /// <see cref="HitFeedback"/> as one more victim-hit trigger (case 12 is a hit as
        /// much as a knockback is — someone just took your food).
        /// </summary>
        /// <remarks>
        /// The delta is read out of the change detector's previous/current buffers rather
        /// than from a cached field, so it is exactly the change Fusion applied, with no
        /// chance of drifting out of sync with the replicated value.
        ///
        /// Only discrete, meaningful changes surface: continuous collection and deposit
        /// drain move Cargo by a fraction per frame and are filtered by
        /// <see cref="FeedbackTuning.FloatingTextCargoDeltaThreshold"/> — see that constant
        /// for why a single threshold cleanly separates the two. Deposits deliberately get
        /// no popup: the base's own deposit burst and the score readout already tell that
        /// story, and a stream of <c>-1 🌽</c> at your own base would be noise, not feedback.
        /// </remarks>
        public override void Render()
        {
            // PropertyReader<T> is a struct (no null state) — the detector is the only
            // thing that can be missing, and only before Spawned has run.
            if (_cargoDetector == null) return;
            if (_controller != null && _controller.IsDecoy) return;

            foreach (var changed in _cargoDetector.DetectChanges(this, out var previous, out var current))
            {
                if (changed != nameof(Cargo)) continue;

                var (before, after) = _cargoReader.Read(previous, current);
                float delta = after - before;
                if (Mathf.Abs(delta) < FeedbackTuning.FloatingTextCargoDeltaThreshold) continue;

                // A drain into a base is not a loss to anyone — it is the point of the game.
                if (delta < 0f && IsDepositing) continue;

                FloatingCombatText.SpawnCargoDelta(
                    transform.position + Vector3.up * FeedbackTuning.FloatingTextSpawnHeight, delta);

                if (delta < 0f) _hitFeedback?.NotifyCargoLoss(-delta);
            }
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

            // Pile food is no longer drained here at all — PeckAbilitySO takes it, one
            // press at a time. What remains is the pile-slow flag, which is about STANDING
            // on a pile rather than about collecting from it, so it is updated every tick
            // regardless of control state.
            UpdatePileSlow();

            TryDepositAtNearbyBase();
        }

        /// <summary>
        /// Maintains <see cref="IsPileSlow"/> — the GDD §6.2 movement penalty for standing
        /// on a pile. This is all that survives of the old automatic collection: the drain
        /// itself moved to <c>PeckAbilitySO</c>, but the slow was never about collecting,
        /// only about being on the pile, so it still runs every tick.
        /// </summary>
        /// <remarks>
        /// Previously the flag was only written inside the collection path, so a stunned
        /// chicken kept whatever value it had when the stun landed. It is now written
        /// unconditionally, which is both simpler and correct.
        /// </remarks>
        private void UpdatePileSlow()
        {
            var pile = FindNearestPileInRange();
            IsPileSlow = pile != null && !pile.IsEmpty;
        }

        private void TryDepositAtNearbyBase()
        {
            var playerBase = FindNearestBaseInRange();
            if (playerBase == null)
            {
                FlushBaseDeposit();
                if (Cargo > 0f)
                {
                    if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
                    {
                        _log.Verbose(Source, $"TryDeposit: carrying {Cargo:0.0} but no owned base in range " +
                            $"(owner={Object.InputAuthority}, pos={transform.position}).");
                    }
                }
                return;
            }

            if (playerBase != _activeBaseTarget)
            {
                FlushBaseDeposit();
                _activeBaseTarget = playerBase;
            }

            if (Cargo <= 0f && BountyBag <= 0f)
            {
                FlushBaseDeposit();
                return;
            }

            if (playerBase.IsClaimed && playerBase.Owner != Object.InputAuthority && !(_controller != null && _controller.IsBot))
            {
                _log?.Warn(Source, $"Assert: base '{playerBase.name}' is claimed by {playerBase.Owner} but player {Object.InputAuthority} is depositing into it.");
            }

            float desiredTransfer = ResolveDepositRate() * Runner.DeltaTime;

            float transferFromBounty = Mathf.Min(BountyBag, desiredTransfer);
            BountyBag -= transferFromBounty;
            float remainingDesired = desiredTransfer - transferFromBounty;

            float transferFromCargo = Mathf.Min(Cargo, remainingDesired);
            Cargo -= transferFromCargo;

            float transfer = transferFromBounty + transferFromCargo;
            if (transfer <= 0f)
            {
                FlushBaseDeposit();
                return;
            }

            _pendingBaseFood += transfer;
            _baseDepositTicks++;

            int ticksToFlush = Mathf.RoundToInt(0.25f * Runner.TickRate);
            if (_baseDepositTicks >= ticksToFlush || (Cargo <= 0f && BountyBag <= 0f))
            {
                bool isDone = (Cargo <= 0f && BountyBag <= 0f);
                FlushBaseDeposit();
                if (isDone)
                {
                    Cargo = 0f;
                    BountyBag = 0f;
                    _audio?.PlaySFX(_audioReg != null ? _audioReg.Deposit : null);
                    _log?.Debug(Source, $"Deposited complete at {playerBase.name}.");
                }
            }
        }

        /// <summary>
        /// Squared horizontal (XZ) distance. Pickup/deposit proximity is a ground-plane
        /// test: a chicken's pivot floats ~1 unit above pickup/base pivots (capsule
        /// centre), so a full 3D distance pushes an in-range chicken outside the tuned
        /// radius and the interaction silently never fires. Playtest evidence: bots parked
        /// ~1.45 horizontal from a 1.6-radius pile, but 3D distance was ~1.8 → no pile ever
        /// drained, no one could score. Comparing XZ only fixes it robustly. Pile
        /// collection no longer comes through here at all — <c>FoodPile.DistanceToSurface</c>
        /// is XZ by construction.
        /// </summary>
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Nearest pile whose SURFACE this chicken is close enough to drain. Ranking is by
        /// surface distance too: against a 7×4 island, centre distance would rate a chicken
        /// standing on the island's rim as further away than a small pile several metres off.
        /// </summary>
        private FoodPile FindNearestPileInRange()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _searchRadius, _overlapHits, _searchMask, QueryTriggerInteraction.Collide);
            var selfPos = transform.position;
            FoodPile best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var col = _overlapHits[i];
                var pile = col.GetComponentInParent<FoodPile>();
                if (pile == null) continue;
                // Physics can hand back a pile whose NetworkObject isn't live yet; the
                // surface test reads [Networked] Amount / FootprintSize, which throws
                // on an unspawned object.
                if (pile.Object == null || !pile.Object.IsValid) continue;
                if (!pile.IsWithinCollectRange(selfPos)) continue;

                float distance = pile.DistanceToSurface(selfPos);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pile;
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
        /// Assassin execute cargo transfer: moves ALL cargo to the assassin's bounty bag.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_TransferAllToBountyBag(NetworkBehaviourId assassinId)
        {
            float amountToTransfer = Cargo;
            Cargo = 0f;
            if (Runner.TryFindBehaviour(assassinId, out ChickenCargo assassinCargo))
            {
                assassinCargo.BountyBag += amountToTransfer;
                _log?.Info(Source, $"Execute: transferred {amountToTransfer:0.0} cargo to assassin {assassinId} bounty bag.");
            }
        }

        /// <summary>
        /// Reset cargo state for a new round. Called by <c>GameManager.RestartMatch</c>
        /// from the master client; routes to each chicken's StateAuthority.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            // Drop any pending drains/deposits because a new round starts
            _pendingBaseFood = 0f;
            _activeBaseTarget = null;
            _baseDepositTicks = 0;

            Cargo = 0f;
            BountyBag = 0f;
            _log?.Debug(Source, "Reset for new match: Cargo=0, BountyBag=0.");
        }
    }
}
