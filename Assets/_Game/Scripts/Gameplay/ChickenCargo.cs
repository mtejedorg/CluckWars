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
        /// The chicken that last successfully robbed this one, and a one-shot event id bumped
        /// alongside it. Together they are the <i>only</i> way a victim's §3.2 direction cue
        /// can learn who a steal came from.
        /// </summary>
        /// <remarks>
        /// <b>Why replicated state and not the drain call itself.</b>
        /// <see cref="RPC_DrainStolen"/> already knows the thief — it validates the id — but it
        /// executes on the victim's StateAuthority alone, so pushing the attribution from inside
        /// it would light the bearing on exactly one peer. Writing the answer into replicated
        /// state instead lets every peer observe the same edge in <see cref="Render"/> and reach
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
        // Cargo, or for the StealEventId edge that rides the same detector. Strictly
        // observation: nothing in Render() writes networked state, and the whole block runs
        // on every peer, so no RPC and no VFX crossing the wire.
        private ChangeDetector _cargoDetector;
        private PropertyReader<float> _cargoReader;
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
        /// Local-only, on every peer. Two things come off one change detector:
        /// <list type="bullet">
        ///   <item>the replicated <see cref="Cargo"/> value turned into the §3.2
        ///   <c>-5 🌽</c> / <c>+5 🌽</c> floating numbers, with a *loss* fed into
        ///   <see cref="HitFeedback"/> as one more victim-hit trigger (case 12 is a hit as much
        ///   as a knockback is — someone just took your food);</item>
        ///   <item>the <see cref="StealEventId"/> edge turned into the attacker attribution
        ///   that gives that hit a <i>direction</i> — see <see cref="NameTheThief"/>.</item>
        /// </list>
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
                // Two independent branches over one detector, and they must STAY independent.
                // Fusion can report Cargo and StealEventId in either order inside a single
                // batch, and both orders already work:
                //   * Cargo first  — NotifyCargoLoss plays a directionless impact, which the
                //                    attribution below then retro-claims.
                //   * Event first  — the window is armed, and the impact claims it on arrival.
                // That is precisely what HitAttribution's window exists for, so do not add an
                // ordering dependency between the two (no hoisting one out of the loop, no
                // deferring one until the other has run).
                if (changed == nameof(StealEventId))
                {
                    NameTheThief();
                    continue;
                }

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

        /// <summary>
        /// The third attacker-attribution push point, and the one that closes the Spine Coat
        /// gap: a successful drain named a thief, so the victim's impact beat can finally say
        /// which direction it came from. A plain local call on every peer — same no-RPC shape
        /// as <see cref="HitFeedback.NotifyCargoLoss"/> and
        /// <see cref="HitFeedback.NotifyZoneTriggered"/>.
        /// </summary>
        /// <remarks>
        /// <b>Baseline seeding is the ChangeDetector's, and adding a polling-style one on top
        /// would be a bug.</b> Fusion pools <c>NetworkObject</c>s, so a recycled chicken can
        /// inherit a non-zero <see cref="StealEventId"/>, and a late joiner starts from whatever
        /// the id happens to be — neither may play a beat for somebody else's theft. The
        /// detector already answers that: <c>GetChangeDetector</c> snapshots current state when
        /// <see cref="Spawned"/> builds it, so an inherited value <i>is</i> the baseline and no
        /// change is reported for it. That is the same guarantee the <c>Cargo</c> branch above
        /// has always relied on — a recycled chicken does not pop a <c>+30 🌽</c>.
        /// <c>ControlStateVFX.ObserveKnockbackEdge</c> and <c>AbilityRangeIndicator.ObserveCast</c>
        /// need an explicit <c>_initialized</c> flag only because they <i>poll</i> a sibling's
        /// property and have no baseline of their own. Bolting one on here would be worse than
        /// redundant: the first observation a detector ever reports is by definition a real
        /// change, so an "ignore the first" flag would swallow the first genuine steal of every
        /// match.
        /// <para>
        /// <b>No <c>onBeforeSpawned</c> reset either, deliberately.</b> The two existing
        /// one-shots on this pattern — <c>ChickenController.KnockbackEventId</c> and
        /// <c>JumpEventId</c> — are never reset by any spawner, for the reason above: the value
        /// does not matter, only the change does. A reset would also have to be stamped by both
        /// of <c>MatchBootstrapper</c>'s chicken-spawn call sites, coupling match bootstrap to a
        /// presentation detail it has no business knowing. And a <i>mid-match</i> reset would be
        /// actively harmful: writing 0 over a non-zero id is itself an edge, which every peer
        /// would observe as a steal that never happened. Not resetting is the safer of the two
        /// options, not merely the cheaper one — which is why <see cref="RPC_ResetForNewMatch"/>
        /// leaves both properties alone.
        /// </para>
        /// <para>
        /// <b>A thief that does not resolve is a legitimate no-op and stays silent</b>
        /// (CONVENTIONS.md, "Error surfacing — the silent-failure sorting rule"). On a remote
        /// peer the thief's proxy may not be spawned yet, or may already have despawned, and
        /// both are reachable in a correctly built game rather than symptoms of a wiring fault.
        /// The consequence is proportionate too — the victim still gets the flash, the squash,
        /// the shake and the <c>-4 🌽</c>; only the direction is withheld, which is exactly what
        /// <see cref="HitFeedback.NotifyAttacker"/> is designed to do when nobody can be named.
        /// Logging here would put a line on a per-steal path for a non-event.
        /// </para>
        /// <para>
        /// Decoys need no handling on either side. The victim's is covered twice over —
        /// <see cref="Render"/> returns early on <c>IsDecoy</c> and <c>NotifyAttacker</c> guards
        /// it again — and the thief's cannot arise, because <see cref="TryResolveThief"/> rejects
        /// a decoy thief before anything is ever written to <see cref="LastThiefId"/>. A guard
        /// here would be unreachable code claiming to defend something.
        /// </para>
        /// </remarks>
        private void NameTheThief()
        {
            if (_hitFeedback == null || Runner == null) return;

            // Same mechanism TryResolveThief and RPC_TransferAllToBountyBag use, resolving to
            // the ChickenController because that is what carries the position.
            if (!Runner.TryFindBehaviour(LastThiefId, out ChickenController thief)) return;
            if (thief == null || thief.Object == null || !thief.Object.IsValid) return;

            _hitFeedback.NotifyAttacker(thief.transform.position);
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

            // The batch window is shared with PlayerBase's receiver-side bound — see
            // BaseDepositRules.FlushSeconds for why the two must not drift apart.
            int ticksToFlush = Mathf.RoundToInt(BaseDepositRules.FlushSeconds * Runner.TickRate);
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
        /// The untrusted steal path: a thief's ability asking this chicken's authority to hand
        /// over <paramref name="amount"/> of its <see cref="Cargo"/>. Five callers: Snatch,
        /// Scrap, Sneaky Steal and Dive Bomb cast at a target they resolved through an aim
        /// shape, and Spine Coat arrives by <i>contact</i> instead — it arms
        /// <c>ChickenController.StealBackAmount</c> and the drain fires later from
        /// <c>CheckCollisionSlow</c>, with no aim shape and so no reach of its own. The thief
        /// credits its own cargo optimistically first; this side clamps to whatever is actually
        /// there, so an over-request from a real ability is harmless.
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
        public void RPC_DrainStolen(float amount, NetworkBehaviourId thiefId, RpcInfo info = default)
        {
            if (!IsPlausibleAmount(amount, out string amountRejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_DrainStolen({amount:0.00}) from {info.Source} — {amountRejection}");
                return;
            }

            if (!TryResolveThief(thiefId, info, out var thief, out string thiefRejection))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_DrainStolen({amount:0.00}) from {info.Source} — {thiefRejection}");
                return;
            }

            if (Cargo <= 0f)
            {
                // Not an attack: the victim banked or lost the load between the thief's scan and
                // this tick. The thief's optimistic self-credit is corrected by its own clamp
                // against the cargo it saw, so nothing is created out of nothing here.
                _log?.Debug(Source, $"{thief.name} stole from an empty load — nothing to drain.");
                return;
            }

            var actual = Mathf.Min(amount, Cargo);
            Cargo -= actual;
            if (Cargo < 0f) Cargo = 0f;

            // Name the thief for the victim's §3.2 direction cue — see LastThiefId, and
            // NameTheThief for the observation half. Written only HERE, after the drain has
            // actually landed: a rejected steal and a steal against an empty load both return
            // above without touching these, because neither is an impact and neither may name
            // anybody. Both properties go out in this one tick, so every peer sees the id and
            // the name it points at in the same snapshot.
            LastThiefId = thiefId;
            StealEventId++;

            _log?.Debug(Source, $"Stolen: -{actual:0.00} by {thief.name} → Cargo={Cargo:0.0}.");
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
        /// having travelled while the message was in the air.
        /// </summary>
        private static float DriftSpeed(ChickenController chicken)
        {
            if (chicken == null) return 0f;
            float walk = chicken.Stats != null ? chicken.Stats.MoveSpeed : 0f;
            return walk + chicken.ExternalDisplacement.magnitude;
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
