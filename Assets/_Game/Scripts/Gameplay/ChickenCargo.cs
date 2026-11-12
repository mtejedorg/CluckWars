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
    /// Food crosses authorities by request and settlement (<see cref="FoodTransferRules"/>): this
    /// chicken asks a pile or a victim, the owner debits what it really has and reports that amount,
    /// and only the report credits <see cref="Cargo"/>. A deposit is acknowledged or bounced by the base.
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
        /// The chicken that last successfully robbed this one, and a one-shot event id bumped
        /// alongside it. Together they are the <i>only</i> way a victim's §3.2 direction cue
        /// can learn who a steal came from.
        /// </summary>
        /// <remarks>
        /// <b>Why replicated state and not the drain call itself.</b>
        /// <see cref="RPC_DrainStolen"/> already knows the thief — it validates the id — but it
        /// executes on the victim's StateAuthority alone, so pushing the attribution from inside
        /// it would light the bearing on exactly one peer. Writing the answer into replicated
        /// state instead lets every peer observe the same edge (<see cref="ChickenLocalEvents.Stolen"/>) and reach
        /// the same conclusion locally, which is the project's rule for animation and VFX: no
        /// RPC, no second channel.
        /// <para>
        /// The event id exists because <see cref="LastThiefId"/> alone has no edge — robbed twice
        /// by the same chicken, the id never changes and the second theft would go unattributed.
        /// Same one-shot-byte pattern as <c>ChickenController.KnockbackEventId</c>, wrapping at
        /// 255 because only the change matters. Both are <c>private set</c>: a forged thief is
        /// exactly the manufactured blame <c>HitAttribution</c> exists to prevent, so the writer
        /// stays inside the validated drain path.
        /// </para>
        /// </remarks>
        [Networked] public NetworkBehaviourId LastThiefId { get; private set; }

        /// <inheritdoc cref="LastThiefId"/>
        [Networked] public byte StealEventId { get; private set; }

        /// <summary>
        /// Cargo the chicken can hold, after its class specialization has had a say.
        /// Hoarder raises it to a full win's worth.
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

        /// <summary>
        /// Deposit rate after both the specialization's say and any active ability's.
        /// </summary>
        /// <remarks>
        /// The single chokepoint for banking speed. Two independent levers feed it and they
        /// stack multiplicatively: a PASSIVE (via <c>ModifyDepositRate</c>) and an ABILITY (via
        /// <see cref="ChickenController.DepositRateMultiplier"/>). Keeping both here means
        /// neither can silently shadow the other, and the SCT-sensitive number stays readable
        /// in one place.
        /// </remarks>
        private float ResolveDepositRate()
        {
            float rate = _matchConfig != null ? _matchConfig.DepositRatePerSecond : 6f;

            var passive = _controller != null ? _controller.Passive : null;
            if (passive != null) rate = passive.ModifyDepositRate(rate, _controller);

            if (_controller != null) rate *= Mathf.Max(0f, _controller.DepositRateMultiplier);

            return rate;
        }
        public float Fraction => Capacity > 0f ? Mathf.Clamp01(Cargo / Capacity) : 0f;

        /// <summary>What a thief can take from this chicken right now: its cargo plus its bounty bag (<see cref="StealRules.Stealable"/>).</summary>
        public float Stealable => StealRules.Stealable(Cargo, BountyBag);

        /// <summary><see cref="Stealable"/> as a share of <see cref="Capacity"/>, 0 to 1: how loaded this chicken looks to a thief.</summary>
        public float StealableFraction => Capacity > 0f ? Mathf.Clamp01(Stealable / Capacity) : 0f;
        public bool IsFull => Cargo >= Capacity;

        /// <summary>
        /// Room this chicken may still ask food for: <see cref="Capacity"/> less its <see cref="Cargo"/> and less what
        /// it has already asked for and not been answered (<see cref="FoodTransferRules.FreeSpace"/>). The open
        /// requests are local to the state authority; on any other peer this is simply capacity minus cargo.
        /// </summary>
        public float FreeSpace
        {
            get
            {
                return _requests.FreeSpace(Capacity, Cargo, Time.unscaledTime);
            }
        }

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
        // Cargo. (The StealEventId edge is a one-shot counter: ChickenLocalEvents polls it.) Strictly
        // observation: nothing in Render() writes networked state, and the whole block runs
        // on every peer, so no RPC and no VFX crossing the wire.
        private ChangeDetector _cargoDetector;
        private PropertyReader<float> _cargoReader;
        private SilentCargoWrite _silentWrite;   // lab-only no-pop refill, see SetCargoWithoutPop
        private int _stealCreditFrame = -1;      // I5: frame of this thief own steal credit (local, presentation only)
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private MatchConfigSO _matchConfig;
        private AbilityRegistrySO _abilityRegistry;

        // Receiver-side bounds for RPC_DrainStolen, derived once from the shipped ability pool
        // (see ResolveStealBounds). Cached because the derivation walks the whole registry and
        // the RPC can fire several times in one tick — Snatch robs every carrier in its arc.
        private float _maxSingleSteal;
        private float _maxStealReach;
        private bool  _stealBoundsResolved;

        // This chicken's side of every food transfer (FoodTransferRules): the steals and pecks it has asked for and
        // not been answered, and the deposit flushes it has sent. Open requests gate the settle RPCs and hold their
        // room in the hold (FreeSpace). Local to this state authority, never replicated.
        private readonly FoodRequests<NetworkBehaviourId> _requests = new FoodRequests<NetworkBehaviourId>();

        // Executes this assassin has sent and not yet been paid for, keyed by victim, valued at
        // the most that victim could have carried; gates RPC_CreditExecuteLoot. A separate ring
        // from the steal requests so a loot report can never close a steal request or vice versa,
        // and with its own longer window (StealRules.ExecuteLootWindowSeconds): the report makes
        // two relayed trips in Shared Mode, not the refund's one round trip.
        private readonly PendingStealLedger<NetworkBehaviourId> _pendingExecutes = StealRules.NewExecuteLootLedger<NetworkBehaviourId>();

        // Accumulators for batched RPCs (Stage D)
        private PlayerBase _activeBaseTarget;
        private float _pendingBaseFood;
        private int _baseDepositTicks;

        // Static array for broadphase overlaps to prevent per-tick allocation
        private static readonly Collider[] _overlapHits = new Collider[32];

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry, MatchConfigSO matchConfig, AbilityRegistrySO abilityRegistry)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
            _matchConfig = matchConfig;
            _abilityRegistry = abilityRegistry;
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

            ResolveStealBounds();

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveCargos.Remove(this);
        }

        /// <summary>
        /// Local-only, on every peer: the replicated <see cref="Cargo"/> value turned into the §3.2
        /// <c>-5 🌽</c> / <c>+5 🌽</c> floating numbers, with a *loss* fed into
        /// <see cref="HitFeedback"/> as one more victim-hit trigger (case 12 is a hit as much
        /// as a knockback is — someone just took your food). The <see cref="StealEventId"/> edge that gives that
        /// hit a <i>direction</i> is observed by <see cref="ChickenLocalEvents"/> (its <c>Stolen</c> event).
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

            // The robbery edge (StealEventId) is ChickenLocalEvents' to observe, like every one-shot counter. Observed
            // here too so it lands in the same Render as the cargo delta below, whichever sibling renders first.
            _controller?.LocalEvents.Observe();

            foreach (var changed in _cargoDetector.DetectChanges(this, out var previous, out var current))
            {
                if (changed != nameof(Cargo)) continue;

                if (_silentWrite.ConsumeChange()) continue;

                var (before, after) = _cargoReader.Read(previous, current);
                float delta = after - before;
                if (Mathf.Abs(delta) < FeedbackTuning.FloatingTextCargoDeltaThreshold) continue;

                // A drain into a base is not a loss to anyone — it is the point of the game.
                if (delta < 0f && IsDepositing) continue;

                // I5: the thief own +n waits for the stolen kernels (HitFeedback releases it on their arrival);
                // pile pickups, the victim -n and every other peer view pop at once, as before.
                if (delta > 0f && StealGainPopHold.IsStealCredit(_stealCreditFrame, Time.frameCount) &&
                    _hitFeedback != null && FloatingCombatText.IsSelfPopTarget(_controller))
                {
                    _stealCreditFrame = -1;
                    _hitFeedback.HoldStealGainPop(delta);
                    continue;
                }

                FloatingCombatText.SpawnCargoPop(_controller,
                    _controller.PopAnchor, delta);

                if (delta < 0f) _hitFeedback?.NotifyCargoLoss(-delta);
            }

            _silentWrite.EndRender(Time.frameCount);
        }

        /// <summary>
        /// Ability Lab only: writes <see cref="Cargo"/> without the local <c>+n</c> / <c>-n</c>
        /// pop or the cargo-loss hit beat, so the dummy refill after a steal does not read as the
        /// victim gaining food. State authority only. The write replicates like any other; only
        /// this peer's presentation of it is skipped, which is enough because the lab is solo
        /// (<c>GameMode.Single</c>). Gameplay code must keep writing <see cref="Cargo"/> directly.
        /// </summary>
        public void SetCargoWithoutPop(float value)
        {
            if (!HasStateAuthority)
            {
                _log?.Warn(Source, $"{name}: SetCargoWithoutPop({value:0.0}) ignored - no state authority here.");
                return;
            }
            Cargo = value;
            _silentWrite.Arm(Time.frameCount);
        }

        /// <summary>The specialization's name for the steal log ("Assassin_Thief"), or "-" with none.</summary>
        private static string SpecOf(ChickenController c) => c != null && c.Passive != null ? c.Passive.Subclass.ToString() : "-";

        public void FlushBaseDeposit()
        {
            if (_pendingBaseFood > 0f)
            {
                if (_activeBaseTarget != null && _activeBaseTarget.Object != null && _activeBaseTarget.Object.IsValid)
                {
                    // Dev-tools diagnostics (Source Base): this peer's view of the flush the host is about to validate.
                    if (CluckWars.Diagnostics.DevTools.Available && _log != null)
                    {
                        var c = _controller;
                        _log.Info("Base", CluckWars.Diagnostics.DepositDiagnostics.FlushLine(
                            c != null && c.Object != null ? c.Object.InputAuthority.ToString() : name, c != null ? c.HomeCornerIndex : -1,
                            transform.position, _activeBaseTarget.CornerIndex, _activeBaseTarget.transform.position,
                            _activeBaseTarget.DepositRadius, _pendingBaseFood));
                    }
                    // Recorded before the RPC: a base this peer owns (master, bots) settles synchronously inside the call.
                    // The cargo was debited tick by tick; the base answers with "banked" (the stat is counted then)
                    // or bounces the flush back (RPC_SettleDeposit).
                    ushort flushId = _requests.SendDeposit(_activeBaseTarget.Id, _pendingBaseFood, Time.unscaledTime);
                    _activeBaseTarget.RPC_AddFood(_pendingBaseFood, Id, flushId);
                }
                else
                {
                    // The base went away between the debit and the flush (despawn, scene unload). Nobody can bank
                    // it or bounce it, so it goes straight back into the hold instead of being dropped.
                    AddToHold(_pendingBaseFood);
                    _log?.Warn(Source, $"{name}: the base went away with {_pendingBaseFood:0.00} food debited for it - returned to the hold.");
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

            // The batch window is shared with PlayerBase's receiver-side bound — see
            // BaseDepositRules.FlushSeconds for why the two must not drift apart.
            int ticksToFlush = Mathf.RoundToInt(BaseDepositRules.FlushSeconds * Runner.TickRate);
            if (_baseDepositTicks >= ticksToFlush || (Cargo <= 0f && BountyBag <= 0f))
            {
                // Nothing is zeroed and nothing is played here: both were already drained to exactly 0 above, a base
                // this peer owns may bounce the flush back inside FlushBaseDeposit, and "deposit complete" is only
                // true once the base says so (RPC_SettleDeposit).
                FlushBaseDeposit();
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
        /// Derives the receiver-side bounds <see cref="RPC_DrainStolen"/> validates against from
        /// the shipped ability pool, once per spawn.
        /// </summary>
        /// <remarks>
        /// Leaving <see cref="_stealBoundsResolved"/> false is graceful degradation, not a
        /// silent pass: the magnitude and range checks are skipped so a missing binding cannot
        /// reject every legitimate steal in the match, the failure is logged as an Error, and
        /// the attribution check — which needs no authored data — still stands on its own. Same
        /// shape as <c>PlayerBase</c>'s missing-<c>MatchConfigSO</c> path.
        /// </remarks>
        private void ResolveStealBounds()
        {
            if (_abilityRegistry != null)
            {
                _maxSingleSteal = StealRules.MaxSingleSteal(_abilityRegistry.All);
                _maxStealReach  = StealRules.MaxReach(_abilityRegistry.All);
            }

            _stealBoundsResolved = _maxSingleSteal > 0f && _maxStealReach > 0f;
            if (_stealBoundsResolved) return;

            _log?.Error(Source, $"{name}: no steal bounds could be derived from the AbilityRegistry " +
                $"(maxAmount={_maxSingleSteal:0.00}, maxReach={_maxStealReach:0.00}), so RPC_DrainStolen " +
                "cannot check how much a caller asks for or how far away the thief is standing. " +
                "Check that ProjectInstaller._abilityRegistry is assigned and that every steal " +
                "ability is listed in its All array.");
        }

        /// <summary>
        /// True when the sender of an RPC may speak for <paramref name="owner"/>: on a local invoke the owner is
        /// simulated here (solo, bots, the master's own objects); off the wire the sender is its state authority.
        /// A remote peer cannot forge <c>IsInvokeLocal</c>.
        /// </summary>
        private static bool SpeaksFor(NetworkBehaviour owner, in RpcInfo info) =>
            owner != null && owner.Object != null && owner.Object.IsValid &&
            (info.IsInvokeLocal ? owner.Object.HasStateAuthority : owner.Object.StateAuthority == info.Source);

        /// <summary>Puts arriving food into the hold: the cargo up to its free room, the rest into the bounty bag.</summary>
        private void AddToHold(float amount)
        {
            FoodTransferRules.Place(amount, FreeSpace, out float toCargo, out float toBag);
            if (toCargo > 0f) Cargo += toCargo;
            if (toBag > 0f) BountyBag += toBag;
        }

        /// <summary>One bite of this chicken's class: the most a single peck can be worth (<c>PeckAbilitySO</c>).</summary>
        public float MaxPeck => Abilities.PeckAbilitySO.AmountFor(_controller);

        // One "settled without an open request" line per owner per 2 s (a stall produces a burst of them).
        private static readonly CluckWars.Diagnostics.PerKeyRateLimiter LateSettleLimiter =
            new CluckWars.Diagnostics.PerKeyRateLimiter(2f);
        private static readonly CluckWars.Diagnostics.PerKeyRateLimiter ResetRejectionLimiter =
            new CluckWars.Diagnostics.PerKeyRateLimiter(2f);

        private void WarnLateSettle(string rpc, in RpcInfo info, string owner, ushort requestId, float credited)
        {
            if (!LateSettleLimiter.Allow(info.Source.RawEncoded, Time.realtimeSinceStartup, out int held)) return;
            _log?.Warn(Source, $"{name}: {rpc} #{requestId} from {owner} matched no open request (expired or overwritten); " +
                $"the sender is its state authority, so {credited:0.00} was honoured, capped at one request's worth." +
                (held > 0 ? $" (+{held} similar suppressed)" : string.Empty));
        }

        /// <summary>
        /// The ONE pecker-side entry point for foraging: asks <paramref name="pile"/>'s authority for up to
        /// <paramref name="amount"/> and records the request. Nothing is credited here; the pile answers with what
        /// it really removed (<see cref="RPC_SettlePeck"/>), one round trip later at most, at once on the pile's own peer.
        /// </summary>
        public void RequestPeck(FoodPile pile, float amount)
        {
            if (pile == null) return;

            // Opened before the RPC: a pile this peer owns settles synchronously inside the call.
            if (!_requests.AskPeck(pile.Id, amount, Capacity, Cargo, Time.unscaledTime, out float asked, out ushort requestId)) return;
            pile.RPC_Drain(asked, Id, requestId);
        }

        /// <summary>
        /// The pile's answer to <see cref="RequestPeck"/>: <paramref name="amount"/> is what it actually removed for
        /// request <paramref name="requestId"/> (0 when another pecker got there first, or the pile refused).
        /// Honoured only from the pile's own authority. With that verified, a report whose request is no longer open
        /// (expired, overwritten) is still credited, up to one bite: the pile has already debited it.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SettlePeck(float amount, NetworkBehaviourId pileId, ushort requestId, RpcInfo info = default)
        {
            if (Runner == null || !Runner.TryFindBehaviour(pileId, out FoodPile pile) || !SpeaksFor(pile, info))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_SettlePeck({amount:0.00}) from {info.Source} - {pileId} is not a pile it speaks for.");
                return;
            }

            var settled = _requests.SettlePeck(pileId, requestId, amount, MaxPeck, Capacity, Cargo, Time.unscaledTime);
            if (settled.Credited <= 0f) return;   // the pile was emptied first, or it refused: a legal no-op

            if (settled.ToCargo > 0f) Cargo += settled.ToCargo;
            if (settled.ToBag > 0f) BountyBag += settled.ToBag;
            if (settled.Outcome == SettleOutcome.Late) WarnLateSettle(nameof(RPC_SettlePeck), info, pile.name, requestId, settled.Credited);
            else if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
                _log.Verbose(Source, $"Pecked +{settled.Credited:0.00} from {pile.name} (asked {settled.Requested:0.00}) → Cargo={Cargo:0.0}.");
        }

        /// <summary>
        /// The ONE thief-side entry point for a steal: asks <paramref name="victim"/>'s authority to drain up to
        /// <paramref name="amount"/> and records the request. Nothing is credited here; the victim answers with what
        /// it really drained (<see cref="RPC_SettleSteal"/>). Callers keep their own
        /// <see cref="StealRules.CanStealFrom"/> gate and call this only after it passes, so a shelled victim is not
        /// even asked.
        /// </summary>
        /// <remarks>
        /// The open request is what lets <see cref="RPC_SettleSteal"/> refuse a report no steal asked for. It is opened
        /// before the RPC because a local invoke (bot or solo victim) settles synchronously inside the call.
        /// </remarks>
        public void RequestSteal(ChickenCargo victim, float amount)
        {
            if (amount <= 0f || victim == null) return;
            if (_controller == null || victim._controller == null)
            {
                _log?.Error(Source, $"{name}: RequestSteal({amount:0.00}) skipped - thief or victim has no ChickenController yet.");
                return;
            }

            // The caller clamped to capacity minus cargo; requests still open hold room too (the other rivals of one
            // multi-target Snatch), so the free room has the last word.
            if (!_requests.AskSteal(victim._controller.Id, amount, Capacity, Cargo, Time.unscaledTime, out float asked, out ushort requestId)) return;
            victim.RPC_DrainStolen(asked, _controller.Id, requestId);
        }

        /// <summary>
        /// The untrusted steal path: a thief's ability asking this chicken's authority to hand
        /// over <paramref name="amount"/> of its <see cref="Cargo"/>. Five callers: Snatch,
        /// Scrap, Sneaky Steal and Dive Bomb cast at a target they resolved through an aim
        /// shape, and Spine Coat arrives by <i>contact</i> instead — it arms
        /// <c>ChickenController.StealBackAmount</c> and the drain fires later from
        /// <c>CheckCollisionSlow</c>, with no aim shape and so no reach of its own. The thief
        /// has credited nothing yet: this side drains what is actually there and reports that
        /// amount back (<see cref="RPC_SettleSteal"/>), so two thieves on one load share it.
        /// </summary>
        /// <remarks>
        /// <c>RpcSources.All</c> means <em>any</em> peer can call this on <em>any</em> chicken,
        /// so everything it is handed is a claim to be checked, not a fact.
        /// <para>
        /// <b><paramref name="thiefId"/> exists because the receiver cannot infer the thief.</b>
        /// <paramref name="info"/> carries a <c>PlayerRef</c>, and every bot shares
        /// <c>PlayerRef.None</c> (<c>MatchBootstrapper.TrySpawnBots</c>), so a sender-only rule
        /// could not tell one bot thief from another — or from no one. Naming the thief turns
        /// the question into one the receiver can answer: is the caller entitled to act for that
        /// chicken, and was that chicken close enough to reach this one. Same
        /// <c>Runner.TryFindBehaviour</c> mechanism <see cref="RPC_TransferAllToBountyBag"/>
        /// already uses; it resolves to the <c>ChickenController</c> rather than the thief's
        /// <c>ChickenCargo</c> because every fact the rules need — authority, decoy status,
        /// position, move speed — is on the controller.
        /// </para>
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_DrainStolen(float amount, NetworkBehaviourId thiefId, ushort requestId, RpcInfo info = default)
        {
            if (!IsPlausibleAmount(amount, out string amountRejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_DrainStolen({amount:0.00}) from {info.Source} — {amountRejection}");
                ReportRefusalToSender(thiefId, requestId, info);
                return;
            }

            if (!TryResolveThief(thiefId, info, out var thief, out string thiefRejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_DrainStolen({amount:0.00}) from {info.Source} — {thiefRejection}");
                ReportRefusalToSender(thiefId, requestId, info);
                return;
            }

            // Steal-proof (Egg Shell / Turtle Mode). Every thief checks StealRules.CanStealFrom
            // before asking, so reaching this point means the shell went up inside one round trip
            // of the thief's scan (the same sub-RTT race Immovable has). The thief has credited
            // nothing, so there is nothing to give back: a report of zero closes its request.
            // A base wall arrived the same way: the victim stepped through it inside one round trip (BaseWallRules).
            bool walled = PlayerBase.WallBetween(thief.transform.position, transform.position);
            if (_controller.IsStealProof || walled)
            {
                _log?.Info(Source, $"{name}: steal by {thief.name} hit {(walled ? "a base wall" : "a shell that went up")} " +
                                   "inside one round trip — nothing drained.");
                ReportSteal(thief, 0f, requestId);
                return;
            }

            if (Stealable <= 0f)
            {
                // Not an attack: the victim banked or lost the load between the thief's scan and
                // this tick (another thief may have taken it first). The thief is told so and
                // credits nothing: its estimate of this load never became food.
                _log?.Debug(Source, $"{thief.name} stole from an empty load — nothing to drain.");
                ReportSteal(thief, 0f, requestId);
                return;
            }

            // The beak first, then the bounty bag (an Assassin's execute pays into the bag): see StealRules.SplitDrain.
            StealRules.SplitDrain(amount, Cargo, BountyBag, out float fromCargo, out float fromBag);
            Cargo = Mathf.Max(0f, Cargo - fromCargo);
            BountyBag = Mathf.Max(0f, BountyBag - fromBag);
            float actual = fromCargo + fromBag;

            // Name the thief for the victim's §3.2 direction cue — see LastThiefId, and
            // HitFeedback.OnStolen for the observation half. Written only HERE, after the drain has
            // actually landed: a rejected steal and a steal against an empty load both return
            // above without touching these, because neither is an impact and neither may name
            // anybody. Both properties go out in this one tick, so every peer sees the id and
            // the name it points at in the same snapshot.
            LastThiefId = thiefId;
            StealEventId++;

            _log?.Info(Source, $"Stolen: -{actual:0.00} (beak {fromCargo:0.00}, bag {fromBag:0.00}) from {name} [{_controller.Class}/{SpecOf(_controller)}] by {thief.name} " +
                               $"[{thief.Class}/{SpecOf(thief)}] → Cargo={Cargo:0.0}, BountyBag={BountyBag:0.0}.");

            ReportSteal(thief, actual, requestId);
        }

        /// <summary>
        /// Victim side: tells the thief's authority what was really drained for its request, 0 for a refusal or an
        /// empty load, so that request is closed and the thief credits exactly that.
        /// </summary>
        private void ReportSteal(ChickenController thief, float actual, ushort requestId)
        {
            if (thief.Cargo != null)
            {
                thief.Cargo.RPC_SettleSteal(actual, _controller.Id, requestId);
                return;
            }

            // A thief that passed TryResolveThief is a real chicken, and every real chicken carries a ChickenCargo.
            if (actual > 0f)
                _log?.Error(Source, $"{name}: cannot report the steal — {thief.name} has no ChickenCargo, so {actual:0.00} drained food is lost.");
        }

        /// <summary>
        /// A rejected <see cref="RPC_DrainStolen"/> still answers when the named thief is a chicken the sender owns: a
        /// report of zero for that request, so it is closed now instead of holding room in the thief's hold until it
        /// expires (an honest steal at the edge of reach is refused this way). A call naming a chicken the sender
        /// does not own gets no answer.
        /// </summary>
        private void ReportRefusalToSender(NetworkBehaviourId thiefId, ushort requestId, in RpcInfo info)
        {
            if (Runner == null || !Runner.TryFindBehaviour(thiefId, out ChickenController named)) return;
            if (named == _controller || !SpeaksFor(named, info)) return;
            ReportSteal(named, 0f, requestId);
        }

        /// <summary>
        /// The victim's answer to <see cref="RequestSteal"/>: <paramref name="amount"/> is what its authority
        /// actually drained for request <paramref name="requestId"/> (0 when the steal was refused or the load was gone).
        /// </summary>
        /// <remarks>
        /// Honoured only when the sender is entitled to speak for the named victim (<see cref="SpeaksFor"/>: a bot
        /// victim is owned by the master) and only against the open request it names, which is closed: a report
        /// cannot be sent without a steal having been asked for, and it is worth at most what was asked. Strict on
        /// purpose, unlike the pile and base reports: the sender is any client that owns a chicken.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SettleSteal(float amount, NetworkBehaviourId victimId, ushort requestId, RpcInfo info = default)
        {
            if (Runner == null || !Runner.TryFindBehaviour(victimId, out ChickenController victim) ||
                victim == _controller || !SpeaksFor(victim, info))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_SettleSteal({amount:0.00}) from {info.Source} - {victimId} is not a victim it speaks for.");
                return;
            }

            var settled = _requests.SettleSteal(victimId, requestId, amount, Capacity, Cargo, Time.unscaledTime);
            if (settled.Outcome == SettleOutcome.Refused)
            {
                _log?.Warn(Source, $"{name}: rejected RPC_SettleSteal({amount:0.00}) #{requestId} from {info.Source} - no such steal is open on {victim.name} " +
                    "(unsolicited, already settled or expired).");
                return;
            }
            if (settled.Credited <= 0f) return;   // refused, or the load was gone: a legal no-op

            if (settled.ToCargo > 0f) Cargo += settled.ToCargo;
            if (settled.ToBag > 0f) BountyBag += settled.ToBag;
            _stealCreditFrame = Time.frameCount;   // I5: Render holds this gain own +n until the kernels land
            _log?.Debug(Source, $"Steal settled: +{settled.Credited:0.00} from {victim.name} (asked {settled.Requested:0.00}) → Cargo={Cargo:0.0}.");
        }

        /// <summary>
        /// The base's answer to deposit flush <paramref name="requestId"/> (<see cref="FlushBaseDeposit"/>):
        /// <paramref name="banked"/> when it was added to the base's total, otherwise the base refused it and the
        /// food comes back.
        /// </summary>
        /// <remarks>
        /// The cargo was debited when the flush was built, so a refusal used to destroy the food (the host seeing the
        /// chicken a step outside the radius was enough). A bounce returns it to the cargo, or to the bounty bag for
        /// whatever no longer fits. The deposit stat is counted here, on "banked" only, and so is the deposit sound.
        /// Honoured only from the base's own authority; with that verified, an answer whose flush is no longer on
        /// record is still honoured, up to one flush.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SettleDeposit(float amount, bool banked, NetworkBehaviourId baseId, ushort requestId, RpcInfo info = default)
        {
            if (Runner == null || !Runner.TryFindBehaviour(baseId, out PlayerBase playerBase) || !SpeaksFor(playerBase, info))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_SettleDeposit({amount:0.00}, banked={banked}) from {info.Source} - {baseId} is not a base it speaks for.");
                return;
            }

            float now = Time.unscaledTime;
            float maxFlush = BaseDepositRules.MaxSingleDeposit(_matchConfig != null ? _matchConfig.DepositRatePerSecond : 6f);
            var settled = _requests.SettleDeposit(baseId, requestId, amount, banked, maxFlush, Capacity, Cargo, now);
            if (settled.Outcome == SettleOutcome.Late && settled.Banked + settled.Credited > 0f)
                WarnLateSettle(nameof(RPC_SettleDeposit), info, playerBase.name, requestId, settled.Banked + settled.Credited);

            if (banked)
            {
                if (settled.Banked <= 0f) return;
                if (TryGetComponent(out ChickenMatchStats stats)) stats.RecordDeposit(settled.Banked);
                else _log?.Warn(Source, $"{name}: no ChickenMatchStats - {settled.Banked:0.00} banked food not counted in MATCH SUMMARY.");

                // The whole load is in the base: nothing left to carry, nothing still unanswered.
                if (Cargo <= 0f && BountyBag <= 0f && _pendingBaseFood <= 0f && _requests.DepositsInFlight(now) <= 0f)
                {
                    _audio?.PlaySFX(_audioReg != null ? _audioReg.Deposit : null);
                    _log?.Debug(Source, $"Deposit complete at {playerBase.name}.");
                }
                return;
            }

            if (settled.Credited <= 0f) return;
            if (settled.ToCargo > 0f) Cargo += settled.ToCargo;
            if (settled.ToBag > 0f) BountyBag += settled.ToBag;
            _log?.Info(Source, $"Deposit of {settled.Credited:0.00} bounced by {playerBase.name}: back in the hold " +
                               $"(cargo +{settled.ToCargo:0.00}, bag +{settled.ToBag:0.00}) → Cargo={Cargo:0.0}, BountyBag={BountyBag:0.0}.");
        }

        /// <summary>Magnitude check, with the reason so the caller can log it.</summary>
        private bool IsPlausibleAmount(float amount, out string rejection)
        {
            if (amount <= 0f)
            {
                rejection = "the amount is not positive.";
                return false;
            }

            // Without a resolved bound there is no honest maximum to derive, so the magnitude
            // check is skipped rather than guessed at. Spawned() has already logged that as an
            // Error, and the thief checks below still stand on their own.
            if (!_stealBoundsResolved)
            {
                rejection = null;
                return true;
            }

            if (!StealRules.IsPlausibleAmount(amount, _maxSingleSteal))
            {
                rejection = $"it exceeds the largest steal any ability in the pool can land ({_maxSingleSteal:0.00}).";
                return false;
            }

            rejection = null;
            return true;
        }

        /// <summary>
        /// Turns <paramref name="thiefId"/> into the chicken the RPC's sender is entitled to
        /// steal with, and checks it was close enough to do so.
        /// </summary>
        /// <remarks>
        /// <b>Attribution mirrors <c>PlayerBase.TryResolveDepositor</c>, and for the same
        /// reason.</b> Bots are simulated by the master and carry <c>PlayerRef.None</c> input
        /// authority, so their steals arrive as a <em>local</em> invocation on that peer rather
        /// than stamped with an identity. On a local invoke, accept a thief this peer already
        /// simulates; on a message off the wire, require the thief's input authority to be the
        /// sender. A remote peer cannot forge <c>IsInvokeLocal</c>.
        /// <para>
        /// <c>Object.StateAuthority</c> is deliberately NOT used: it reports
        /// <c>[Player:None]</c> in <c>GameMode.Single</c>, which is precisely the wrong answer
        /// in the mode bots live in (docs/STATE.md, "Design deviations").
        /// </para>
        /// </remarks>
        private bool TryResolveThief(NetworkBehaviourId thiefId, in RpcInfo info,
            out ChickenController thief, out string rejection)
        {
            thief = null;

            if (Runner == null || !Runner.TryFindBehaviour(thiefId, out ChickenController resolved) ||
                resolved == null || resolved.Object == null || !resolved.Object.IsValid)
            {
                rejection = $"no live chicken resolves from thief id {thiefId}.";
                return false;
            }

            if (resolved == _controller)
            {
                rejection = "the named thief is this chicken — a steal from yourself is not a steal.";
                return false;
            }

            // Matches PlayerBase's exclusion: a Doppelganger decoy carries no cargo and cannot
            // bank, so it has no business moving food around either.
            if (resolved.IsDecoy)
            {
                rejection = $"{resolved.name} is a Doppelganger decoy, which steals nothing.";
                return false;
            }

            bool attributable = info.IsInvokeLocal
                ? resolved.Object.HasStateAuthority
                : resolved.Object.InputAuthority == info.Source;
            if (!attributable)
            {
                rejection = $"{resolved.name} is not a chicken {info.Source} is entitled to act for.";
                return false;
            }

            if (_stealBoundsResolved)
            {
                // A local invocation has no wire time; asking for a remote player's RTT would be
                // meaningless (and Source may be None).
                float latency = info.IsInvokeLocal ? 0f : (float)Runner.GetPlayerRtt(info.Source);
                float margin = StealRules.RangeMargin(DriftSpeed(resolved) + DriftSpeed(_controller), latency);

                if (!StealRules.IsWithinStealRange(
                        resolved.transform.position, transform.position, _maxStealReach, margin))
                {
                    float distance = Mathf.Sqrt(HorizontalSqr(resolved.transform.position, transform.position));
                    rejection = $"{resolved.name} is {distance:0.0} m away, past the longest steal reach in " +
                                $"the pool ({_maxStealReach:0.00} m) plus {margin:0.00} m of drift.";
                    return false;
                }
            }

            thief = resolved;
            rejection = null;
            return true;
        }

        /// <summary>
        /// How fast <paramref name="chicken"/> can be separating from the other end of a steal:
        /// its own top walking speed plus any knockback impulse still in flight. Knockback is
        /// not a rounding error here — Snatch shoves at 8.1 u/s against move speeds around 12 —
        /// and a chicken shoved by an earlier hit that then gets robbed must not be rejected for
        /// having travelled while the message was in the air. Also used by
        /// <c>ChickenCombat</c>'s execute range check (CW-230).
        /// </summary>
        internal static float DriftSpeed(ChickenController chicken)
        {
            if (chicken == null) return 0f;
            float walk = chicken.Stats != null ? chicken.Stats.MoveSpeed : 0f;
            return walk + chicken.ExternalDisplacement.magnitude;
        }

        /// <summary>
        /// Assassin-side half of an execute, called by <c>AssassinExecute.Press</c> on this
        /// (the assassin's) state authority just before it sends
        /// <see cref="RPC_TransferAllToBountyBag"/>: records that a loot report from
        /// <paramref name="victim"/> is expected, and the most it may be worth.
        /// </summary>
        /// <remarks>
        /// Recorded BEFORE the transfer RPC because on a shared authority (solo, bots) the
        /// transfer and the report back both invoke locally and synchronously inside that call.
        /// The cap uses only what this peer legitimately sees — see
        /// <see cref="StealRules.ExecuteLootCap"/>.
        /// </remarks>
        public void ExpectExecuteLoot(ChickenController victim)
        {
            if (!HasStateAuthority || victim == null || victim.Cargo == null)
            {
                // Press is authority-gated and validates the victim first, so this is a wiring bug.
                _log?.Error(Source, $"{name}: ExpectExecuteLoot ignored (authority={HasStateAuthority}, " +
                    $"victim={(victim != null ? victim.name : "null")}) - the execute's loot will be refused.");
                return;
            }

            if (!StealRules.RecordExecuteExpectation(_pendingExecutes, victim.Id,
                    victim.Cargo.Capacity, victim.Cargo.Cargo, Time.unscaledTime, out _))
            {
                // Capacity 0 means the victim's Stats did not resolve on this peer - a wiring or
                // spawn-order fault, not a game state. Any loot it reports will be refused; the
                // flat execute bounty is paid by Press regardless.
                _log?.Warn(Source, $"{name}: execute on {victim.name} expects no loot - its carry " +
                    "capacity reads 0 here (Stats unresolved?) and no cargo is visible, so any loot report will be refused.");
            }
        }

        /// <summary>
        /// Assassin execute cargo transfer, victim side: drains ALL of this chicken's cargo and
        /// reports the amount actually drained back to the assassin's authority, which credits
        /// its own bounty bag (<see cref="RPC_CreditExecuteLoot"/>).
        /// </summary>
        /// <remarks>
        /// <c>RpcSources.All</c>, so the call is a claim: it is honoured only when the caller is
        /// the assassin's state authority (local invoke on the assassin's authority, or the wire
        /// sender is that authority) AND that assassin's <see cref="AssassinExecute"/> currently
        /// has this chicken as <c>MarkedTarget</c>. A Kill cannot qualify until <c>MarkKillAbilitySO.ArmSeconds</c>
        /// after the mark, which is above the latency cap, so replication lag cannot make a genuine call fail the check.
        /// <para>
        /// <b>Nothing is written to the assassin's cargo from here.</b> This runs on the
        /// VICTIM's authority, where the assassin's cargo is a proxy; a <c>[Networked]</c> write
        /// through a proxy never replicates, so the bag credit used to vanish in Shared-mode PvP
        /// while appearing to work solo. The amount reported is the one zeroed HERE, at the
        /// moment of the drain, so food the victim banked while stunned in its own base before
        /// this landed is never paid to the assassin as well (CW-050).
        /// </para>
        /// </remarks>
        /// <summary>An executed chicken stands up at its base with no cargo (the execute took the load). State authority only;
        /// the bounty bag is its own food and stays.</summary>
        public void ClearForRespawn()
        {
            if (!HasStateAuthority) return;
            Cargo = 0f;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_TransferAllToBountyBag(NetworkBehaviourId assassinId, RpcInfo info = default)
        {
            if (!TryResolveExecutor(assassinId, info, out var assassinCargo, out bool walled, out string rejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_TransferAllToBountyBag from {info.Source} - {rejection}");
                return;
            }

            // Same answer as the removal half, in the same tick: the target reached home inside one round trip.
            // Nothing was credited yet, so refusing creates no food; the pending record simply expires.
            if (walled)
            {
                _log?.Debug(Source, $"{name}: execute transfer landed across a base wall; no effect.");
                return;
            }

            // Steal-proof victim: the transfer is refused here. The assassin credits nothing until
            // the report below, so refusing creates no food; its pending record simply expires.
            // The thief-side gate in AssassinExecute normally stops the call first; this catches
            // the sub-round-trip race.
            if (_controller.IsStealProof)
            {
                _log?.Info(Source, $"{name}: execute transfer refused - steal-proof (a shell went up inside one round trip).");
                return;
            }

            float drained = Cargo;
            Cargo = 0f;
            if (drained <= 0f)
            {
                // Legitimate: the victim banked or lost the load before the execute landed.
                _log?.Info(Source, $"Execute: {name} carried nothing to drain for {assassinCargo.name}.");
                return;
            }

            assassinCargo.RPC_CreditExecuteLoot(drained, _controller.Id);
            _log?.Info(Source, $"Execute: drained {drained:0.0} cargo, reported to {assassinCargo.name}.");
        }

        /// <summary>
        /// Assassin side of an execute's loot: the victim's authority reports how much cargo
        /// it actually drained, and this (the assassin's) authority banks it into its own
        /// <see cref="BountyBag"/>.
        /// </summary>
        /// <remarks>
        /// <c>RpcSources.All</c>, so every report is a claim. Same receiver-validated shape as
        /// <see cref="RPC_SettleSteal"/>: the sender must be entitled to speak for the
        /// named victim (local invoke: victim simulated here; off the wire: the victim's state
        /// authority), and the report must match an execute this chicken recorded in
        /// <see cref="ExpectExecuteLoot"/>, consumed once, inside the ledger window, and never
        /// worth more than the cap recorded there.
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_CreditExecuteLoot(float amount, NetworkBehaviourId victimId, RpcInfo info = default)
        {
            if (!StealRules.IsPlausibleLootReport(amount))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_CreditExecuteLoot({amount:0.00}) from {info.Source} - the amount is not a positive finite number.");
                return;
            }

            if (Runner == null || !Runner.TryFindBehaviour(victimId, out ChickenController victim) ||
                victim == null || victim.Object == null || !victim.Object.IsValid)
            {
                _log?.Warn(Source, $"{name}: rejected RPC_CreditExecuteLoot({amount:0.00}) - no live chicken resolves from victim id {victimId}.");
                return;
            }

            bool attributable = victim != _controller &&
                (info.IsInvokeLocal ? victim.Object.HasStateAuthority : victim.Object.StateAuthority == info.Source);
            if (!attributable)
            {
                _log?.Warn(Source, $"{name}: rejected RPC_CreditExecuteLoot({amount:0.00}) from {info.Source} - {victim.name} is not a victim it speaks for.");
                return;
            }

            if (!StealRules.TryResolveExecuteLoot(_pendingExecutes, victimId, amount, Time.unscaledTime, out float allowed))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_CreditExecuteLoot({amount:0.00}) from {info.Source} - no pending execute on {victim.name} (unsolicited, expired or already paid).");
                return;
            }

            BountyBag += allowed;
            if (allowed + 1e-4f < amount)
            {
                _log?.Warn(Source, $"{name}: execute loot from {victim.name} clamped {amount:0.00} -> {allowed:0.00} (above the victim's carry cap).");
            }
            _log?.Info(Source, $"Execute loot: +{allowed:0.0} from {victim.name} into the bounty bag (BountyBag={BountyBag:0.0}).");
        }

        /// <summary>
        /// Resolves <paramref name="assassinId"/> to the assassin's cargo if the caller may act
        /// for it and it has marked this chicken. Attribution split as in
        /// <see cref="RPC_SettleSteal"/>: local invoke needs state authority here, a wire
        /// message must come from the assassin's state authority.
        /// </summary>
        private bool TryResolveExecutor(NetworkBehaviourId assassinId, in RpcInfo info,
            out ChickenCargo assassinCargo, out bool walled, out string rejection)
        {
            assassinCargo = null;
            walled = false;

            if (Runner == null || !Runner.TryFindBehaviour(assassinId, out AssassinExecute execute) ||
                execute == null || execute.Object == null || !execute.Object.IsValid)
            {
                rejection = $"no live assassin resolves from id {assassinId}.";
                return false;
            }

            bool attributable = info.IsInvokeLocal
                ? execute.Object.HasStateAuthority
                : execute.Object.StateAuthority == info.Source;
            if (!attributable)
            {
                rejection = $"{execute.name} is not a chicken {info.Source} is entitled to act for.";
                return false;
            }

            // An execute needs this chicken marked by that assassin, for the arm, AND stunned when the Kill
            // lands. AssassinExecute.Press sends this RPC and resets its mark in the same tick, so the
            // replicated MarkedTarget can already read None when the RPC lands: the mark counts when current
            // or recorded recently by this victim's own authority. The stun counts only when current or
            // recorded within one capped latency (the RPC's flight). Same rules, same order, as
            // ChickenCombat.RPC_ExecuteRemoval: HasMarkEvidence, HasArmEvidence, HasStunAtKill (CW-230).
            var combat = _controller != null ? _controller.Combat : null;
            bool markSeen = false, stunSeen = false;
            float sinceMark = 0f, markAge = 0f, sinceStun = 0f, latency = 0f;
            if (combat != null)
            {
                markSeen = combat.TrySecondsSinceMarkedBy(execute, out sinceMark, out markAge);
                stunSeen = combat.TrySecondsSinceStunned(out sinceStun);
                latency = combat.SenderLatency(info);
            }
            if (_controller == null ||
                !ExecuteRules.HasMarkEvidence(execute.MarkedTarget == _controller.Id, markSeen, sinceMark, latency))
            {
                rejection = $"{name} has not seen a recent mark from {execute.name}.";
                return false;
            }
            // The arm, timed on this chicken's own clock: the same rule as ChickenCombat's NotArmed verdict.
            if (!ExecuteRules.HasArmEvidence(markSeen, markAge, execute.ArmSeconds))
            {
                rejection = $"{name} has seen {execute.name}'s mark for {markAge:0.00} s, under its {execute.ArmSeconds:0.0} s arm.";
                return false;
            }
            // The stun, from this chicken's own records only: the same rule as ChickenCombat's NotStunned verdict.
            if (!ExecuteRules.HasStunAtKill(_controller.IsStunned, stunSeen, sinceStun, latency))
            {
                rejection = $"{name} was not stunned when {execute.name}'s Kill landed " +
                            (stunSeen ? $"(stun ended {sinceStun:0.00} s ago, allowance {ExecuteRules.CapLatency(latency):0.00} s)." : "(never stunned).");
                return false;
            }

            // The other half of ChickenCombat's verdict: same range bound, same wall, same order.
            float dx = execute.transform.position.x - transform.position.x, dz = execute.transform.position.z - transform.position.z;
            var assassinController = execute.GetComponent<ChickenController>();
            float margin = combat != null ? combat.ExecuteRangeMargin(assassinController, latency) : 0f;
            if (!(dx * dx + dz * dz <= Mathf.Pow(ExecuteRules.MaxExecuteDistance(margin, execute.KillRange), 2f)))
            {
                rejection = $"{execute.name} is too far away to have executed this chicken.";
                return false;
            }
            walled = PlayerBase.WallBetween(execute.transform.position, transform.position);

            assassinCargo = execute.GetComponent<ChickenCargo>();
            if (assassinCargo == null || assassinCargo == this)
            {
                rejection = $"{execute.name} has no usable ChickenCargo.";
                return false;
            }

            rejection = null;
            return true;
        }

        /// <summary>
        /// Reset cargo state for a new round. Called by <c>GameManager.ResetWorldForNewRound</c>
        /// from the master client; routes to each chicken's StateAuthority.
        /// </summary>
        /// <remarks>
        /// Sender-restricted like <c>ChickenController.RPC_ResetControlStates</c> (A19): it zeroes the hold and the
        /// bounty bag, so any peer could otherwise empty a rival. Its two callers are the match authority's round
        /// reset and the Ability Lab's dummy reset, which is a local invoke (solo).
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch(RpcInfo info = default)
        {
            if (!ChickenController.IsFromMatchAuthority(info, out string rejection))
            {
                if (ResetRejectionLimiter.Allow(info.Source.RawEncoded, Time.realtimeSinceStartup, out int held))
                    _log?.Warn(Source, $"{name}: rejected RPC_ResetForNewMatch from {info.Source} — {rejection}" +
                        (held > 0 ? $" (+{held} more held back)" : string.Empty));
                return;
            }

            // Drop any pending drains/deposits because a new round starts
            _pendingBaseFood = 0f;
            _activeBaseTarget = null;
            _baseDepositTicks = 0;
            _requests.Clear();
            _pendingExecutes.Clear();

            Cargo = 0f;
            BountyBag = 0f;
            _log?.Debug(Source, "Reset for new match: Cargo=0, BountyBag=0.");
        }
    }
}
