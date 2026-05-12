using System;
using CluckWars.Audio;
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
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private bool _hpInitialized;

        /// <summary>Fires on every peer when this chicken transitions into death stun.</summary>
        public event Action OnDeath;

        public bool IsDead => HP <= 0f;

        [Inject]
        public void Construct(ILogService log, IAudioService audio, AudioRegistrySO audioReg)
        {
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
        }

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

            // Decoys (Doppelganger) take hits and play hit anims via the [Networked]
            // state path, but never swing — skip input read so the caster's attack
            // button doesn't fire through the decoy.
            if (_controller != null && _controller.IsDecoy) return;

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
                        if (cur < prev)
                        {
                            _animator?.TriggerHit();
                            _audio?.PlaySFX(_audioReg != null ? _audioReg.Hit : null);
                        }
                        break;
                    }
                    case nameof(IsStunned):
                    {
                        var (_, cur) = _stunReader.Read(previous, current);
                        _animator?.SetStunned(cur);
                        if (cur)
                        {
                            _log?.Info(Source, "Death stun begin.");
                            _audio?.PlaySFX(_audioReg != null ? _audioReg.Stun : null);
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
                        _audio?.PlaySFX(_audioReg != null ? _audioReg.Swing : null);
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
            target.RPC_ApplyDamage(stats.Attack, Object.InputAuthority);
        }

        /// <summary>
        /// Public so other systems (Roll &amp; Trample) can deal damage / stun directly
        /// without going through the swing pipeline. Same authority crossing as the
        /// regular attack path: caller is any client, applied on the target's
        /// StateAuthority.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ApplyDamage(float amount, PlayerRef attacker)
        {
            if (IsStunned) return; // dead chickens can't be hit again until they respawn

            // Egg Shell and similar abilities flip DamageImmune — drop the damage entirely.
            if (_controller != null && _controller.DamageImmune)
            {
                _log?.Verbose(Source, $"Damage absorbed by immunity ({amount:0.0}).");
                return;
            }

            // Spine Coat: bounce the damage back to the attacker; recipient eats nothing.
            if (_controller != null && _controller.ReflectDamage && attacker.IsRealPlayer)
            {
                ReflectDamageTo(attacker, amount);
                return;
            }

            // Turtle Mode and similar reduce incoming damage.
            float resisted = amount;
            if (_controller != null && _controller.DamageResistance > 0f)
            {
                resisted *= Mathf.Clamp01(1f - _controller.DamageResistance);
            }

            HP = Mathf.Max(0f, HP - resisted);
            _log?.Debug(Source, $"RPC_ApplyDamage: -{resisted:0.0} (raw {amount:0.0}, resist {_controller?.DamageResistance:0.00}) → HP={HP}.");

            if (HP <= 0f)
            {
                IsStunned = true;
                StunTimer = TickTimer.CreateFromSeconds(Runner, _stunDuration);
            }
        }

        /// <summary>
        /// Reset combat state for a new round. Called by <c>GameManager.RestartMatch</c>
        /// from the master client; routes to each chicken's StateAuthority so the
        /// chicken's owner mutates their own <c>[Networked]</c> state.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            var stats = _controller != null ? _controller.Stats : null;
            HP = stats != null ? stats.MaxHP : 100f;
            IsStunned = false;
            StunTimer = default;
            AttackTimer = default;
            _log?.Debug(Source, $"Reset for new match: HP={HP}.");
        }

        private void ReflectDamageTo(PlayerRef attacker, float amount)
        {
            // Find the attacker's chicken combat by InputAuthority and bounce damage
            // through the same RPC. Self-attribution so the reflector takes no damage.
            var combats = FindObjectsByType<ChickenCombat>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < combats.Length; i++)
            {
                var c = combats[i];
                if (c == null || c == this) continue;
                if (c.Object != null && c.Object.InputAuthority == attacker)
                {
                    _log?.Debug(Source, $"Spine Coat: reflected {amount:0.0} back to {attacker}.");
                    c.RPC_ApplyDamage(amount, Object.InputAuthority);
                    return;
                }
            }
            _log?.Verbose(Source, $"Spine Coat: attacker {attacker} not found, damage dropped.");
        }

        private void Respawn(ChickenStatsSO stats)
        {
            HP = stats.MaxHP;
            IsStunned = false;
            StunTimer = default;
        }
    }
}
