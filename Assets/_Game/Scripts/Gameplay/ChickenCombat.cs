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
                if (StunTimer.Expired(Runner))
                    Respawn(stats);
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
        /// an <see cref="AssassinExecute"/> that marked this chicken or this chicken is stunned
        /// (the rule <c>ChickenCargo.TryResolveExecutor</c> applies to the cargo half), it is
        /// within execute range, and the duration is the execute's. See <see cref="ExecuteRules"/>.
        /// </para>
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ExecuteRemoval(NetworkBehaviourId assassinId, float duration, RpcInfo info = default)
        {
            var verdict = JudgeRemoval(assassinId, duration, info, out string attackerName);
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
                claim.MarkedThisVictim = execute != null && _controller != null && execute.MarkedTarget == _controller.Id;
                claim.VictimStunned = _controller != null && _controller.IsStunned;

                // A local invoke has no wire time (both chickens simulated here); a remote one is
                // floored because GetPlayerRtt is not confirmed to report remote players in Shared Mode.
                claim.Latency = SenderLatency(info);
                claim.MarkEverSeen = TrySecondsSinceMarkedBy(execute, out claim.SecondsSinceMarkSeen);

                // Two ticks cover the press landing a tick after the mark's own range check.
                claim.RangeMargin = StealRules.RangeMargin(
                    ChickenCargo.DriftSpeed(attackerController) + ChickenCargo.DriftSpeed(_controller),
                    claim.Latency + 2f * Runner.DeltaTime);
                Vector3 a = attacker.transform.position, v = transform.position;
                claim.PlanarDistance = Mathf.Sqrt((a.x - v.x) * (a.x - v.x) + (a.z - v.z) * (a.z - v.z));
            }

            return ExecuteRules.EvaluateRemoval(claim);
        }

        /// <summary>The RPC sender's latency for allowances: 0 for a local invoke, else the floored RTT.</summary>
        internal float SenderLatency(in RpcInfo info) =>
            info.IsInvokeLocal || Runner == null ? 0f : ExecuteRules.RemoteLatency((float)Runner.GetPlayerRtt(info.Source));

        // Victim-side, local, never replicated: when this chicken's authority last saw each
        // attacker's replicated MarkedTarget name it. Lets an execute whose mark was cleared in the
        // press tick still count as marked after the qualifying stun ended (CW-230 review).
        private readonly System.Collections.Generic.Dictionary<AssassinExecute, float> _markSeenAt =
            new System.Collections.Generic.Dictionary<AssassinExecute, float>();

        /// <summary>
        /// Seconds since this chicken's authority last saw <paramref name="execute"/>'s mark name it.
        /// False when it never has (or the record was cleared by a round reset).
        /// </summary>
        internal bool TrySecondsSinceMarkedBy(AssassinExecute execute, out float seconds)
        {
            seconds = 0f;
            if (execute == null || !_markSeenAt.TryGetValue(execute, out float at)) return false;
            seconds = Time.unscaledTime - at;
            return true;
        }

        // Authority only, every tick: at most four chickens, so a straight scan of the registry.
        private void ObserveMarksOnMe()
        {
            if (_controller == null) return;
            var all = ActiveCombats;
            for (int i = 0; i < all.Count; i++)
            {
                var execute = all[i] != null ? all[i]._execute : null;
                if (execute != null && execute.Object != null && execute.Object.IsValid &&
                    execute.MarkedTarget == _controller.Id)
                    _markSeenAt[execute] = Time.unscaledTime;
            }
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
            _markSeenAt.Clear();
            _log?.Debug(Source, "Reset combat for new match.");
        }

        private void Respawn(ChickenStatsSO stats)
        {
            IsRemoved = false;
            StunTimer = default;
        }
    }
}
