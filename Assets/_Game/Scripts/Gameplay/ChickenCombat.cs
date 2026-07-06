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
    /// Networked health and death-stun. Handles HP, damage reception via RPC,
    /// 5-second death stun, and respawn. Lives as a sibling component on the
    /// Chicken prefab; reads <see cref="ChickenStatsSO"/> from
    /// <see cref="ChickenController"/>.
    /// </summary>
    /// <remarks>
    /// v0.3: the basic attack was removed — all damage is now ability-driven.
    /// <c>RPC_ApplyDamage</c> is still called by damage abilities
    /// (e.g. <c>RollTrampleAbilitySO</c>). The class name is intentionally
    /// unchanged so existing prefab GUID references remain valid (CONVENTIONS.md).
    ///
    /// HP change, stun-begin, and stun-end visuals (hit flash, stunned anim,
    /// camera shake) are triggered locally on every peer via a
    /// <see cref="ChangeDetector"/> — nothing animation-related is networked.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenCombat : NetworkBehaviour
    {
        private const string Source = "Combat";

        public static readonly System.Collections.Generic.List<ChickenCombat> ActiveCombats = new System.Collections.Generic.List<ChickenCombat>();

        [Tooltip("Stun duration after dying. GDD calls for 5 seconds.")]
        [Min(0f)]
        [SerializeField] private float _stunDuration = 5f;

        [Networked] public float HP { get; set; }
        [Networked] public bool IsStunned { get; set; }
        [Networked] private TickTimer StunTimer { get; set; }

        private ChickenController _controller;
        private ChickenAnimator _animator;
        private ChangeDetector _detector;
        private PropertyReader<float> _hpReader;
        private PropertyReader<bool> _stunReader;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private bool _hpInitialized;

        /// <summary>
        /// Fires on every peer when this chicken transitions into death stun.
        /// Driven by the <see cref="ChangeDetector"/> in <see cref="Render"/> —
        /// cosmetics only (animator, SFX, shake, nameplate). State-mutating
        /// consequences must use <see cref="OnDeathAuthority"/> instead: the
        /// ChangeDetector has silently skipped locally-written [Networked] props
        /// in GameMode.Single (see STATE.md v0.3.1 base-tinting entry).
        /// </summary>
        public event Action OnDeath;

        /// <summary>
        /// Fires only on the StateAuthority, synchronously inside
        /// <see cref="RPC_ApplyDamage"/> when death occurs. Subscribe here for
        /// state-mutating consequences (cargo drop, ability cancel).
        /// </summary>
        public event Action OnDeathAuthority;

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
            ActiveCombats.Add(this);
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

            // Lazy first-tick HP init: ChickenController.Spawned may not have resolved
            // Stats yet by the time our Spawned ran (component order on the prefab is fragile).
            if (!_hpInitialized)
            {
                HP = stats.MaxHP;
                _hpInitialized = true;
                _log?.Info(Source, $"HP initialized to {HP}.");
            }

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
                    case nameof(HP):
                    {
                        var (prev, cur) = _hpReader.Read(previous, current);
                        if (cur < prev)
                        {
                            _animator?.TriggerHit();
                            _audio?.PlaySFX(_audioReg != null ? _audioReg.Hit : null);
                            if (HasInputAuthority)
                                CluckWars.Visuals.MatchCamera.Instance?.ApplyShake(0.12f, 0.25f);
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
                            if (HasInputAuthority)
                                CluckWars.Visuals.MatchCamera.Instance?.ApplyShake(0.35f, 0.45f);
                        }
                        else
                        {
                            _log?.Info(Source, "Stun ended; respawned.");
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Public so abilities (e.g. Flying Peck) can deal damage directly without going
        /// through a now-deleted attack pipeline. Same authority crossing as before:
        /// caller is any client, applied on the target's StateAuthority.
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
                CreditKillToAttacker(attacker);
                // Authority-side consequences (cargo drop, ability cancel) fire
                // here, not from Render's ChangeDetector — that path can silently
                // skip locally-written props in GameMode.Single.
                OnDeathAuthority?.Invoke();
            }
        }

        /// <summary>
        /// Finds the attacker's <see cref="ChickenMatchStats"/> by InputAuthority and
        /// credits a kill. Only real players earn kills; bots are ignored as both
        /// killers and victims. Runs on the target's StateAuthority.
        /// </summary>
        private void CreditKillToAttacker(PlayerRef attacker)
        {
            if (!attacker.IsRealPlayer) return;
            if (Object != null && attacker == Object.InputAuthority) return;

            var allStats = ChickenMatchStats.ActiveStats;
            for (int i = 0; i < allStats.Count; i++)
            {
                var s = allStats[i];
                if (s == null || s.Object == null) continue;
                if (s.Object.InputAuthority == attacker)
                {
                    s.RPC_CreditKill();
                    _log?.Debug(Source, $"Kill credited to {attacker}.");
                    return;
                }
            }
        }

        /// <summary>
        /// Reset combat state for a new round. Called by <c>GameManager.RestartMatch</c>
        /// from the master client; routes to each chicken's StateAuthority.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ResetForNewMatch()
        {
            var stats = _controller != null ? _controller.Stats : null;
            HP = stats != null ? stats.MaxHP : 100f;
            IsStunned = false;
            StunTimer = default;
            _log?.Debug(Source, $"Reset for new match: HP={HP}.");
        }

        private void ReflectDamageTo(PlayerRef attacker, float amount)
        {
            float knockback = _controller != null ? _controller.SpineCoatKnockbackStrength : 0f;

            var combats = ChickenCombat.ActiveCombats;
            for (int i = 0; i < combats.Count; i++)
            {
                var c = combats[i];
                if (c == null || c == this) continue;
                if (c.Object != null && c.Object.InputAuthority == attacker)
                {
                    _log?.Debug(Source, $"Spine Coat: reflected {amount:0.0} back to {attacker}.");
                    c.RPC_ApplyDamage(amount, Object.InputAuthority);

                    // Knockback: push the attacker away from the defender.
                    if (knockback > 0f)
                    {
                        var attackerCtrl = c.GetComponent<ChickenController>();
                        if (attackerCtrl != null)
                        {
                            var dir = (c.transform.position - transform.position);
                            dir.y = 0f;
                            if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
                            attackerCtrl.RPC_ApplyKnockback(dir.normalized * knockback);
                        }
                    }
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
