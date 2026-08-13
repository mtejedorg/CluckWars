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
                            if (HasInputAuthority)
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
        /// owned by another peer — i.e. every real PvP execute — the removal did nothing while
        /// the sibling <c>RPC_TransferAllToBountyBag</c> succeeded: the Assassin took 100% of
        /// the victim's cargo and the victim walked away unharmed, uncredited and unstunned.
        /// Solo testing never caught it because bots share the host's authority, so the guard
        /// below incidentally passed.
        /// </remarks>
        public void ExecuteRemoval(NetworkBehaviourId assassinId, float duration = 2.0f)
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
        /// </remarks>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ExecuteRemoval(NetworkBehaviourId assassinId, float duration)
        {
            ExecuteRemoval(assassinId, duration);
        }

        private void CreditKillToAttacker(NetworkBehaviourId attackerId)
        {
            if (attackerId == this.Id) return;
            if (_controller != null && _controller.IsDecoy) return;

            if (Runner.TryFindBehaviour(attackerId, out ChickenCombat attackerCombat))
            {
                var s = attackerCombat.GetComponent<ChickenMatchStats>();
                if (s != null)
                {
                    s.RPC_CreditKill();
                    _log?.Debug(Source, $"Kill credited to {attackerId}.");
                }
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            IsRemoved = false;
            StunTimer = default;
            _log?.Debug(Source, "Reset combat for new match.");
        }

        private void Respawn(ChickenStatsSO stats)
        {
            IsRemoved = false;
            StunTimer = default;
        }
    }
}
