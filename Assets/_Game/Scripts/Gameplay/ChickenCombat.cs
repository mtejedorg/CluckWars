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

        [Networked] public bool IsStunned { get; set; }
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

        public bool IsDead => IsStunned;

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

            _stunReader = GetPropertyReader<bool>(nameof(IsStunned));

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

            if (IsStunned)
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
                    case nameof(IsStunned):
                    {
                        var (_, cur) = _stunReader.Read(previous, current);
                        _animator?.SetStunned(cur);
                        if (cur)
                        {
                            _log?.Info(Source, "Execute removal begin.");
                            _audio?.PlaySFX(_audioReg != null ? _audioReg.Stun : null);
                            OnDeath?.Invoke();
                            if (HasInputAuthority)
                                CluckWars.Visuals.MatchCamera.Instance?.ApplyShake(0.35f, 0.45f);
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
        /// Triggers execute removal on this chicken (called on StateAuthority by AssassinExecute).
        /// </summary>
        public void ExecuteRemoval(NetworkBehaviourId assassinId, float duration = 2.0f)
        {
            if (!HasStateAuthority) return;
            if (IsStunned) return;

            IsStunned = true;
            StunTimer = TickTimer.CreateFromSeconds(Runner, duration > 0f ? duration : _stunDuration);
            CreditKillToAttacker(assassinId);
            OnDeathAuthority?.Invoke(assassinId);
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
            IsStunned = false;
            StunTimer = default;
            _log?.Debug(Source, "Reset combat for new match.");
        }

        private void Respawn(ChickenStatsSO stats)
        {
            IsStunned = false;
            StunTimer = default;
        }
    }
}
