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
        private ILogService _log;

        /// <summary>The chicken's archetype. Replicated; set by the spawner via <c>OnBeforeSpawned</c>.</summary>
        [Networked] public ChickenClass Class { get; set; } = ChickenClass.Warrior;

        public ChickenStatsSO Stats => _activeStats != null ? _activeStats : _fallbackStats;
        public ChickenCombat Combat => _combat;

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
            _activeStats = ResolveStatsForClass(Class);

            if (_activeStats == null)
            {
                _log?.Error(Source, $"{name}: could not resolve stats for class '{Class}'. Assign _fallbackStats on the prefab or populate ChickenClassRegistry.");
                return;
            }

            _log?.Info(Source, $"Stats resolved: '{_activeStats.DisplayName}', moveSpeed={_activeStats.MoveSpeed}.");
            _movement = new ChickenMovement(_characterController, _activeStats);

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
