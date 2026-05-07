using System;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Networked combat: button-mash proximity attack, HP, hit reaction, death stun.
    /// Damage is applied on the target's StateAuthority via RPC; visuals (hit / attack /
    /// stun bool) fire locally on every peer in response to <c>[Networked]</c> state
    /// changes via a <see cref="ChangeDetector"/>. Lives as a sibling component on the
    /// Chicken prefab; reads <see cref="ChickenStatsSO"/> from <see cref="ChickenController"/>.
    /// </summary>
    /// <remarks>
    /// Phase 3 scope: HP, swing, damage RPC, 5-sec death stun, hit/attack/stun anim hooks.
    /// Cargo drop on death is deferred to Phase 4 — subscribe to <see cref="OnDeath"/>
    /// from <c>ChickenCargo</c> when that lands.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCombat : NetworkBehaviour
    {
        private const string Source = "Combat";

        [Tooltip("Stun duration after dying. GDD calls for 5 seconds.")]
        [Min(0f)]
        [SerializeField] private float _stunDuration = 5f;

        [Tooltip("Layers searched for attack targets. Default = Everything; tighten once a Chicken layer is authored.")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Networked] public float HP { get; set; }
        [Networked] public bool IsStunned { get; set; }
        [Networked] private TickTimer StunTimer { get; set; }
        [Networked] private TickTimer AttackTimer { get; set; }
        // Bumped on each swing so every peer can locally trigger the attack animation
        // via ChangeDetector, with no extra RPC traffic.
        [Networked] private int AttackEpoch { get; set; }

        private ChickenController _controller;
        private ChickenAnimator _animator;
        private ChangeDetector _detector;
        private PropertyReader<float> _hpReader;
        private PropertyReader<bool> _stunReader;
        private PropertyReader<int> _attackEpochReader;
        private ILogService _log;
        private bool _hpInitialized;

        /// <summary>Fires on every peer when this chicken transitions into death stun.</summary>
        public event Action OnDeath;

        public bool IsDead => HP <= 0f;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            // Same self-inject pattern as ChickenController — Fusion spawns NetworkBehaviours
            // outside Zenject's normal injection path. Reading .Instance triggers lazy load.
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _controller = GetComponent<ChickenController>();
            _animator = GetComponent<ChickenAnimator>();
            _detector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            // Cache PropertyReaders once — Fusion 2 ChangeDetector reads buffers via these.
            _hpReader = GetPropertyReader<float>(nameof(HP));
            _stunReader = GetPropertyReader<bool>(nameof(IsStunned));
            _attackEpochReader = GetPropertyReader<int>(nameof(AttackEpoch));

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}.");
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var stats = _controller.Stats;
            if (stats == null) return;

            // Lazy first-tick init: ChickenController.Spawned may not have resolved Stats
            // yet by the time our Spawned ran (component order on the prefab is fragile).
            if (!_hpInitialized)
            {
                HP = stats.MaxHP;
                _hpInitialized = true;
                _log?.Info(Source, $"HP initialized to {HP}.");
            }

            if (IsStunned)
            {
                if (StunTimer.Expired(Runner))
                {
                    Respawn(stats);
                }
                return;
            }

            if (!GetInput<PlayerNetworkInput>(out var input)) return;

            if (input.Buttons.IsSet((int)InputButton.Attack) && AttackTimer.ExpiredOrNotRunning(Runner))
            {
                Swing(stats);
            }
        }

        public override void Render()
        {
            if (_detector == null) return;
            foreach (var changedProperty in _detector.DetectChanges(this, out var previous, out var current))
            {
                switch (changedProperty)
                {
                    case nameof(HP):
                    {
                        var (prev, cur) = _hpReader.Read(previous, current);
                        if (cur < prev) _animator?.TriggerHit();
                        break;
                    }
                    case nameof(IsStunned):
                    {
                        var (_, cur) = _stunReader.Read(previous, current);
                        _animator?.SetStunned(cur);
                        if (cur)
                        {
                            _log?.Info(Source, "Death stun begin.");
                            OnDeath?.Invoke();
                        }
                        else
                        {
                            _log?.Info(Source, "Stun ended; respawned.");
                        }
                        break;
                    }
                    case nameof(AttackEpoch):
                        _animator?.TriggerAttack();
                        break;
                }
            }
        }

        private void Swing(ChickenStatsSO stats)
        {
            AttackTimer = TickTimer.CreateFromSeconds(Runner, stats.AttackCooldown);
            AttackEpoch++;

            // Single nearest target in range. Cleave / multi-hit lands later if we want it.
            var hits = Physics.OverlapSphere(
                transform.position,
                stats.AttackRange,
                _targetMask,
                QueryTriggerInteraction.Ignore);

            ChickenCombat target = null;
            float bestSqrDist = float.MaxValue;
            foreach (var col in hits)
            {
                var other = col.GetComponentInParent<ChickenCombat>();
                if (other == null || other == this || other.IsDead) continue;
                var sqrDist = (other.transform.position - transform.position).sqrMagnitude;
                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    target = other;
                }
            }

            if (target == null)
            {
                _log?.Verbose(Source, "Swing → no target in range.");
                return;
            }

            _log?.Debug(Source, $"Swing → {target.name} for {stats.Attack} dmg.");
            target.RPC_ApplyDamage(stats.Attack);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_ApplyDamage(float amount)
        {
            if (IsStunned) return; // dead chickens can't be hit again until they respawn

            // Egg Shell and similar abilities flip DamageImmune — drop the damage entirely.
            if (_controller != null && _controller.DamageImmune)
            {
                _log?.Verbose(Source, $"Damage absorbed by immunity ({amount:0.0}).");
                return;
            }

            HP = Mathf.Max(0f, HP - amount);
            _log?.Debug(Source, $"RPC_ApplyDamage: -{amount} → HP={HP}.");

            if (HP <= 0f)
            {
                IsStunned = true;
                StunTimer = TickTimer.CreateFromSeconds(Runner, _stunDuration);
            }
        }

        private void Respawn(ChickenStatsSO stats)
        {
            HP = stats.MaxHP;
            IsStunned = false;
            StunTimer = default;
        }
    }
}
