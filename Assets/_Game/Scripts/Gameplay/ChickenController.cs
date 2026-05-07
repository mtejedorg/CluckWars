using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Visuals;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Top-level networked chicken. Phase 2 makes it class-aware: a
    /// <c>[Networked]</c> <see cref="Class"/> selects the active <see cref="ChickenStatsSO"/>
    /// from the injected registry, with the prefab's serialized <c>_stats</c> kept
    /// only as a safety fallback.
    /// </summary>
    /// <remarks>
    /// State authority drives motion via the Fusion input buffer; pure-MonoBehaviour
    /// helpers (animator, visuals) stay local and react to <c>[Networked]</c> state.
    /// Combat / cargo / abilities will hang off this same GameObject in later phases.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ChickenController : NetworkBehaviour
    {
        private const string Source = "Chicken";

        [Tooltip("Fallback used only if the class registry is missing or has no entry for this chicken's class.")]
        [SerializeField] private ChickenStatsSO _fallbackStats;

        private CharacterController _characterController;
        private ChickenMovement _movement;
        private ChickenClassRegistrySO _registry;
        private ChickenStatsSO _activeStats;
        private ChickenCombat _combat;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ILogService _log;

        /// <summary>The chicken's archetype. Replicated; set by the spawner via <c>OnBeforeSpawned</c>.</summary>
        [Networked] public ChickenClass Class { get; set; } = ChickenClass.Warrior;

        public ChickenStatsSO Stats => _activeStats != null ? _activeStats : _fallbackStats;
        public ChickenCombat Combat => _combat;
        public ChickenCargo Cargo => _cargo;
        public AbilityController Abilities => _abilities;

        // ---- Ability state (StateAuthority-side only) ------------------------
        // Abilities mutate these locally on the StateAuthority. Other peers don't
        // need to mirror these values — they observe the resulting [Networked]
        // position / HP changes instead.

        /// <summary>Multiplier applied to <c>Stats.MoveSpeed</c> by active abilities. 1 = no buff.</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>While true, <c>ChickenMovement</c> ignores planar input but keeps gravity.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>While true, <c>ChickenCombat.RPC_ApplyDamage</c> drops incoming damage.</summary>
        public bool DamageImmune { get; set; }

        /// <summary>0 = full damage, 1 = no damage taken. Multiplied with incoming damage in <c>ChickenCombat</c>.</summary>
        public float DamageResistance { get; set; }

        /// <summary>When true, incoming damage is sent back to the attacker instead of applied here.</summary>
        public bool ReflectDamage { get; set; }

        /// <summary>0 = invisible, 1 = fully opaque. Read by <c>ChickenVisuals</c> for the Invisibility ability.</summary>
        public float VisualOpacity { get; set; } = 1f;

        /// <summary>
        /// True for the Doppelganger decoy: skips input processing in
        /// <see cref="FixedUpdateNetwork"/> (and similar guards in
        /// <c>ChickenCombat</c> / <c>AbilityController</c>) so the caster's input
        /// doesn't drive both their real chicken and the decoy. Hits, animations,
        /// and tint still work — the decoy is a static prop that takes damage.
        /// </summary>
        public bool IsDecoy { get; set; }

        [Inject]
        public void Construct(ChickenClassRegistrySO registry, ILogService log)
        {
            _registry = registry;
            _log = log;
        }

        public override void Spawned()
        {
            // Fusion spawns NetworkBehaviours outside of Zenject's normal injection path,
            // so we self-inject from ProjectContext if Construct hasn't been called yet.
            // Accessing .Instance triggers the lazy load if needed.
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _log?.Debug(Source, $"Spawned. Class={Class}, HasStateAuthority={HasStateAuthority}, registryBound={_registry != null}.");

            _characterController = GetComponent<CharacterController>();
            _combat = GetComponent<ChickenCombat>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
            _activeStats = ResolveStatsForClass(Class);

            if (_activeStats == null)
            {
                _log?.Error(Source, $"{name}: could not resolve stats for class '{Class}'. Assign _fallbackStats on the prefab or populate ChickenClassRegistry.");
                return;
            }

            _log?.Info(Source, $"Stats resolved: '{_activeStats.DisplayName}', moveSpeed={_activeStats.MoveSpeed}.");
            _movement = new ChickenMovement(_characterController, this);

            // Apply the per-class tint locally on every peer so even proxies look right.
            if (_registry != null && _registry.TryGet(Class, out var entry))
            {
                var visuals = GetComponent<ChickenVisuals>();
                if (visuals != null)
                {
                    visuals.ApplyTint(entry.TintColor);
                    _log?.Debug(Source, $"Applied tint {entry.TintColor} for class {Class}.");
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_movement == null) return;
            if (!HasStateAuthority) return;

            // Decoys (Doppelganger) share input authority with the caster — skip input
            // tick or the decoy walks in lockstep with the real chicken.
            if (IsDecoy) return;

            // Stun lockout: dead-stunned chickens can't move. Combat owns the IsStunned flag.
            if (_combat != null && _combat.IsStunned) return;

            if (GetInput<PlayerNetworkInput>(out var input))
            {
                _movement.Tick(input.Movement, Runner.DeltaTime);
            }
        }

        private ChickenStatsSO ResolveStatsForClass(ChickenClass cls)
        {
            if (_registry != null && _registry.TryGet(cls, out var entry) && entry.Stats != null)
            {
                return entry.Stats;
            }
            return _fallbackStats;
        }
    }
}
