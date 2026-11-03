using System;
using CluckWars.Audio;
using CluckWars.Logging;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked removal and respawn state machine.
    /// Used by the Assassin Mark/Kill execute to temporarily remove and respawn a target.
    /// Class name retained so existing prefab component bindings stay valid.
    /// </summary>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCombat : NetworkBehaviour
    {
        private const string Source = "Combat";

        public static readonly System.Collections.Generic.List<ChickenCombat> ActiveCombats = new System.Collections.Generic.List<ChickenCombat>();

        [Tooltip("Removal duration after execute.")]
        [Min(0f)]
        [SerializeField] private float _stunDuration = 2f;

        [Networked] public bool IsRemoved { get; set; }
        [Networked] private TickTimer StunTimer { get; set; }

        private ChickenController _controller;
        private ChickenAnimator _animator;
        private CharacterController _body;
        private AssassinExecute _execute;
        private bool _missingControllerLogged;
        private ChangeDetector _detector;
        private PropertyReader<bool> _stunReader;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;

        /// <summary>
        /// Fires on every peer when this chicken transitions into execute removal.
        /// </summary>
        public event Action OnDeath;

        /// <summary>
        /// Fires only on the StateAuthority when execute removal occurs.
        /// Subscribe here for state-mutating consequences (cargo drop, ability cancel).
        /// </summary>
        public event Action<NetworkBehaviourId> OnDeathAuthority;

        public bool IsDead => IsRemoved;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
        }

        public override void Spawned()
        {
            ActiveCombats.Add(this);
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _controller = GetComponent<ChickenController>();
            _animator = GetComponent<ChickenAnimator>();
            _execute = GetComponent<AssassinExecute>();
            _detector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            _stunReader = GetPropertyReader<bool>(nameof(IsRemoved));

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActiveCombats.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            ObserveMarksOnMe();

            var stats = _controller.Stats;
            if (stats == null) return;

            if (IsRemoved)
            {
                switch (ExecuteRespawnRules.Next(StunTimer.Expired(Runner), _respawnTeleportQueued))
                {
                    case ExecuteRespawnStep.QueueTeleport:
                        QueueRespawnTeleport();
                        break;
                    case ExecuteRespawnStep.StandUp:
                        Respawn(stats);
                        break;
                }
            }
        }

        public override void Render()
        {
            if (_detector == null) return;
            foreach (var changedProperty in _detector.DetectChanges(this, out var previous, out var current))
            {
                switch (changedProperty)
                {
                    case nameof(IsRemoved):
                    {
                        var (_, cur) = _stunReader.Read(previous, current);
                        _animator?.SetStunned(cur);
                        // Out: no body to bump into on any peer (the respawn teleport toggles it around itself and leaves it as found).
                        if (_body == null) _body = GetComponent<CharacterController>();
                        if (_body != null && _controller != null && !_controller.IsDecoy) _body.enabled = !cur;
                        if (cur)
                        {
                            _log?.Info(Source, "Execute removal begin.");
                            _audio?.PlaySFX(_audioReg != null ? _audioReg.Stun : null);
                            OnDeath?.Invoke();
                            // FEEDBACK.md §3.4's death shake. Stays the SINGLE call site for
                            // it — Stage 4's HitFeedback deliberately does not re-issue it,
                            // it only layers the feather burst and hit-stop on top of this
                            // same OnDeath moment. The literals (0.35 / 0.45) that used to
                            // sit here are what FeedbackTuning.DeathShake* were derived
                            // from; reading them back keeps a re-tune effective.
                            // !IsDecoy as well as HasInputAuthority: a decoy shares its
                            // caster's InputAuthority, so HasInputAuthority is TRUE on the
                            // decoy on the caster's own peer. Without the second term,
                            // killing a decoy shook the caster's camera as if they had died.
                            if (HasInputAuthority && _controller != null && !_controller.IsDecoy)
                                CluckWars.Visuals.MatchCamera.Instance?.ApplyShake(
                                    CluckWars.Visuals.FeedbackTuning.DeathShakeMagnitude,
                                    CluckWars.Visuals.FeedbackTuning.DeathShakeDurationSeconds);
                        }
                        else
                        {
                            _log?.Info(Source, "Removal ended; respawned.");
                            if (HasInputAuthority && _controller != null && !_controller.IsDecoy) LocalRespawnCue.Raise();
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Triggers execute removal on this chicken. <b>Call <see cref="RPC_ExecuteRemoval"/>
        /// instead</b> unless you already hold this chicken's StateAuthority.
        /// </summary>
        /// <remarks>
        /// ⚠️ This is authority-local and returns silently when called from anywhere else.
        /// <c>AssassinExecute</c> used to call it directly, which meant that against a victim
        /// owned by another peer — i.e. every real PvP execute — the removal did nothing and the
        /// victim walked away unharmed and unstunned. Solo testing never caught it because bots
        /// share the host's authority, so the guard below incidentally passed.
        /// <para>
        /// <paramref name="assassinId"/> must be the assassin's <b>ChickenCombat</b> id:
        /// <see cref="CreditKillToAttacker"/> resolves it as one. <c>AssassinExecute</c> used to
        /// pass its own behaviour id, which never resolved, so no execute ever scored a kill
        /// (CW-050; pinned by <c>AssassinExecuteCreditTests</c>).
        /// </para>
        /// </remarks>
        public void ExecuteRemoval(NetworkBehaviourId assassinId, float duration = ExecuteRules.RemovalDurationSeconds)
        {
            if (!HasStateAuthority) return;
            if (IsRemoved) return;

            IsRemoved = true;
            StunTimer = TickTimer.CreateFromSeconds(Runner, duration > 0f ? duration : _stunDuration);
            _respawnTeleportQueued = false;
            // Everything this life had ends now: control states, running abilities and their effects (steal-proof, invisibility),
            // a mark it holds. Cooldowns keep running while it is out.
            _controller?.EndEverythingForRemoval();
            _execute?.DropMarkForRemoval();
            CreditKillToAttacker(assassinId);
            OnDeathAuthority?.Invoke(assassinId);
        }

        /// <summary>
        /// Cross-authority entry point for the Assassin's execute. Routes to this chicken's
        /// own StateAuthority, which is the only peer that may write <see cref="IsRemoved"/>.
        /// </summary>
        /// <remarks>
        /// Mirrors the <c>RPC_TransferAllToBountyBag</c> / <c>RPC_DrainStolen</c> pattern that
        /// every other cross-authority write in this codebase already uses. Both halves of an
        /// execute now travel the same way — previously the cargo transfer was an RPC and the
        /// removal was a plain call, so they disagreed about who could apply them and the
        /// removal silently lost.
        /// <para>
        /// <b>Receiver-validated (CW-230).</b> <c>RpcSources.All</c>, so the call is a claim. It is
        /// honoured only when the sender speaks for the named attacker (local invoke: the attacker
        /// is simulated here; off the wire: the sender is its state authority), that attacker has
        /// an <see cref="AssassinExecute"/> that marked this chicken and this chicken is stunned
        /// (the rule <c>ChickenCargo.TryResolveExecutor</c> applies to the cargo half), it is
        /// within kill range, and the duration is the execute's. A base wall between them is a legal
        /// no-op, not a rejection. See <see cref="ExecuteRules"/>.
        /// </para>
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ExecuteRemoval(NetworkBehaviourId assassinId, float duration, RpcInfo info = default)
        {
            var verdict = JudgeRemoval(assassinId, duration, info, out string attackerName);
            if (verdict == ExecuteRules.RemovalVerdict.Shelled)
            {
                // Silent, like the steal refund: a shell went up inside one round trip. No removal, no kill credit, no bounty
                // (both follow the removal); the Assassin gets its Kill back.
                _log?.Debug(Source, $"{name}: execute from {attackerName} landed on a shell; refused, Kill refunded.");
                RefundShelledExecute(assassinId);
                return;
            }
            if (ExecuteRules.IsLegalNoOp(verdict))
            {
                _log?.Debug(Source, $"{name}: execute from {attackerName} landed across a base wall; no effect.");
                return;
            }
            if (verdict != ExecuteRules.RemovalVerdict.Accepted)
            {
                if (RejectionLimiter.Allow(info.Source.RawEncoded, Time.realtimeSinceStartup, out int held))
                    _log?.Warn(Source, $"{name}: rejected RPC_ExecuteRemoval(attacker={attackerName}, " +
                        $"duration={duration:0.00}) from {info.Source} ({verdict}) - {ExecuteRules.Describe(verdict)}" +
                        (held > 0 ? $" (+{held} similar suppressed)" : string.Empty));
                return;
            }

            ExecuteRemoval(assassinId, duration);
        }

        // One rejection line per sender per 2 s, shared by every chicken (as PlayerBase's deposit rejections).
        private static readonly CluckWars.Diagnostics.PerKeyRateLimiter RejectionLimiter =
            new CluckWars.Diagnostics.PerKeyRateLimiter(2f);

        /// <summary>Gathers what this (the victim's) authority can see about a removal claim and judges it.</summary>
        private ExecuteRules.RemovalVerdict JudgeRemoval(NetworkBehaviourId attackerId, float duration, in RpcInfo info,
            out string attackerName)
        {
            attackerName = attackerId.ToString();
            var claim = new ExecuteRules.RemovalClaim { Duration = duration };

            if (Runner != null && Runner.TryFindBehaviour(attackerId, out ChickenCombat attacker) &&
                attacker != null && attacker.Object != null && attacker.Object.IsValid)
            {
                attackerName = attacker.name;
                var attackerController = attacker.GetComponent<ChickenController>();
                var execute = attacker.GetComponent<AssassinExecute>();

                claim.AttackerResolved = true;
                claim.AttackerIsVictim = attacker == this;
                claim.AttackerIsDecoy = attackerController != null && attackerController.IsDecoy;
                claim.SenderSpeaksForAttacker = info.IsInvokeLocal
                    ? attacker.Object.HasStateAuthority
                    : attacker.Object.StateAuthority == info.Source;
                claim.AttackerHasExecute = execute != null;
                if (execute != null) { claim.KillRange = execute.KillRange; claim.MaxDuration = execute.RemovalSeconds; }
                claim.MarkedThisVictim = execute != null && _controller != null && execute.MarkedTarget == _controller.Id;
                claim.VictimStunned = _controller != null && _controller.IsStunned;

                // A local invoke has no wire time (both chickens simulated here); a remote one is
                // floored because GetPlayerRtt is not confirmed to report remote players in Shared Mode.
                claim.Latency = SenderLatency(info);
                claim.MarkEverSeen = TrySecondsSinceMarkedBy(execute, out claim.SecondsSinceMarkSeen);
                claim.StunEverSeen = TrySecondsSinceStunned(out claim.SecondsSinceStunned);

                claim.RangeMargin = ExecuteRangeMargin(attackerController, claim.Latency);
                Vector3 a = attacker.transform.position, v = transform.position;
                claim.WallBetweenThem = PlayerBase.WallBetween(a, v);
                claim.VictimShelled = _controller != null && _controller.IsStealProof;
                claim.PlanarDistance = Mathf.Sqrt((a.x - v.x) * (a.x - v.x) + (a.z - v.z) * (a.z - v.z));
            }

            return ExecuteRules.EvaluateRemoval(claim);
        }

        /// <summary>The range slack both halves of an execute judge with. Two ticks cover the press landing a tick after its own check.</summary>
        internal float ExecuteRangeMargin(ChickenController attacker, float latency) =>
            StealRules.RangeMargin(ChickenCargo.DriftSpeed(attacker) + ChickenCargo.DriftSpeed(_controller),
                latency + 2f * Runner.DeltaTime);

        /// <summary>The RPC sender's latency for allowances: 0 for a local invoke, else the floored RTT.</summary>
        internal float SenderLatency(in RpcInfo info) => SenderLatency(Runner, info);

        /// <summary>The shared form, for receivers that are not a ChickenCombat (ChickenController's control RPCs, F4).</summary>
        internal static float SenderLatency(NetworkRunner runner, in RpcInfo info) =>
            info.IsInvokeLocal || runner == null ? 0f : ExecuteRules.RemoteLatency((float)runner.GetPlayerRtt(info.Source));

        // Victim-side, local, never replicated: when this chicken's authority last saw each
        // attacker's replicated MarkedTarget name it. Lets an execute whose mark was cleared in the
        // press tick still count as marked after the qualifying stun ended (CW-230 review).
        // Stored in simulation time (Runner.SimulationTime), like the execute window itself: a solo
        // pause stops ticks but not the wall clock, and must not age these records.
        private readonly System.Collections.Generic.Dictionary<AssassinExecute, double> _markSeenAt =
            new System.Collections.Generic.Dictionary<AssassinExecute, double>();

        /// <summary>
        /// Seconds since this chicken's authority last saw <paramref name="execute"/>'s mark name it.
        /// False when it never has (or the record was cleared by a round reset).
        /// </summary>
        internal bool TrySecondsSinceMarkedBy(AssassinExecute execute, out float seconds)
        {
            seconds = 0f;
            if (Runner == null || execute == null || !_markSeenAt.TryGetValue(execute, out double at)) return false;
            seconds = ExecuteRules.AgeSeconds(Runner.SimulationTime, at);
            return true;
        }

        // Victim-side, local, never replicated: when this chicken's own authority last saw itself
        // stunned. The stun can end between the press and this RPC landing; this is the victim's
        // own record that it really happened.
        private double _stunnedAt;   // Runner.SimulationTime, see _markSeenAt
        private bool _stunEverSeen;

        /// <summary>Seconds since this chicken's authority last saw itself stunned. False when never this round.</summary>
        internal bool TrySecondsSinceStunned(out float seconds)
        {
            seconds = 0f;
            if (Runner == null || !_stunEverSeen) return false;
            seconds = ExecuteRules.AgeSeconds(Runner.SimulationTime, _stunnedAt);
            return true;
        }

        // Authority only, every tick: this chicken's own stun, and every mark naming it (at most
        // four chickens, so a straight scan of the registry).
        private void ObserveMarksOnMe()
        {
            if (_controller == null || Runner == null) return;
            double now = Runner.SimulationTime;
            if (_controller.IsStunned)
            {
                _stunnedAt = now;
                _stunEverSeen = true;
            }

            var all = ActiveCombats;
            for (int i = 0; i < all.Count; i++)
            {
                var execute = all[i] != null ? all[i]._execute : null;
                if (execute != null && execute.Object != null && execute.Object.IsValid &&
                    execute.MarkedTarget == _controller.Id)
                    _markSeenAt[execute] = now;
            }
        }

        /// <summary>Victim's authority: tells the Assassin's authority its Kill met a shell (<see cref="AssassinExecute.RPC_RefundShelledKill"/>).</summary>
        private void RefundShelledExecute(NetworkBehaviourId attackerId)
        {
            if (_controller == null || Runner == null) return;
            if (!Runner.TryFindBehaviour(attackerId, out ChickenCombat attacker) || attacker == null) return;   // left: nothing to refund
            var execute = attacker.GetComponent<AssassinExecute>();
            if (execute != null) execute.RPC_RefundShelledKill(_controller.Id);
        }

        private void CreditKillToAttacker(NetworkBehaviourId attackerId)
        {
            if (attackerId == this.Id) return;
            if (_controller == null)
            {
                if (!_missingControllerLogged)
                {
                    _missingControllerLogged = true;
                    _log?.Error(Source, $"{name}: kill not credited - no ChickenController sibling (prefab wiring).");
                }
                return;
            }
            if (_controller.IsDecoy) return;

            if (Runner.TryFindBehaviour(attackerId, out ChickenCombat attackerCombat))
            {
                var s = attackerCombat.GetComponent<ChickenMatchStats>();
                if (s != null)
                {
                    // The victim's ChickenController id: the attacker's stats check it against the
                    // execute it recorded at press time (CW-230).
                    s.RPC_CreditKill(_controller.Id);
                    _log?.Debug(Source, $"Kill credit reported to {attackerId}.");
                }
                else
                {
                    _log?.Error(Source, $"{name}: kill not credited - {attackerCombat.name} has no ChickenMatchStats.");
                }
            }
            else
            {
                // Legitimate when the assassin left between the press and this landing.
                _log?.Info(Source, $"{name}: kill not credited - attacker {attackerId} no longer resolves.");
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            IsRemoved = false;
            StunTimer = default;
            _respawnTeleportQueued = false;   // the round reset's own teleport takes it home
            _markSeenAt.Clear();
            _stunEverSeen = false;
            _log?.Debug(Source, "Reset combat for new match.");
        }

        // Authority-local: the respawn teleport is queued; the chicken stands up the tick after it lands.
        private bool _respawnTeleportQueued;

        /// <summary>Out time over: back to its own corner's spawn point through the round reset's teleport (NetworkTransform
        /// teleport, interpolation suspended, RecentPositions cleared), still removed for that tick so nobody sees it at the
        /// death spot.</summary>
        private void QueueRespawnTeleport()
        {
            _respawnTeleportQueued = true;
            var map = FindFirstObjectByType<MapGenerator>();
            int corner = _controller != null ? _controller.HomeCornerIndex : -1;
            if (!ExecuteRespawnRules.TrySpawnPoint(corner, map != null ? map.SpawnPoints : null, out Vector3 home))
            {
                // A wiring / map bug: no spawn point for this corner. It stands up where it fell rather than staying out.
                _log?.Error(Source, $"{name}: no spawn point for corner {corner} (map {(map != null ? "has " + map.SpawnPoints.Count : "missing")}); " +
                    "the executed chicken respawns where it fell.");
                return;
            }
            _controller.QueueTeleport(home + Vector3.up * 0.05f);
        }

        private void Respawn(ChickenStatsSO stats)
        {
            IsRemoved = false;
            StunTimer = default;
            _respawnTeleportQueued = false;
            var cargo = _controller != null ? _controller.Cargo : null;
            if (cargo != null) cargo.ClearForRespawn();   // back with nothing: the execute took the load

            float cd = 0f;
            var abilities = _controller != null ? _controller.Abilities : null;
            if (abilities != null)
                for (int s = 0; s < AbilityController.SlotCount; s++) cd = Mathf.Max(cd, abilities.CooldownRemaining(s));
            var p = transform.position;
            int home = _controller != null ? _controller.HomeCornerIndex : -1;
            var map = FindFirstObjectByType<MapGenerator>();
            float dist = ExecuteRespawnRules.TrySpawnPoint(home, map != null ? map.SpawnPoints : null, out Vector3 sp)
                ? Vector2.Distance(new Vector2(p.x, p.z), new Vector2(sp.x, sp.z)) : -1f;
            // Matrix evidence (merge.ps1 / verify.ps1 can key on it): at its base, empty, cooldowns still running.
            _log?.Info("Reset", System.FormattableString.Invariant(
                $"Execute respawn chicken={name} home={home} pos={p.x:0.0},{p.z:0.0} dist_to_corner={dist:0.00} cargo={(cargo != null ? cargo.Cargo : 0f):0.00} maxcd={cd:0.00}"));
        }
    }
}
