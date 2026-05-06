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
        [Tooltip("Fallback used only if the class registry is missing or has no entry for this chicken's class.")]
        [SerializeField] private ChickenStatsSO _fallbackStats;

        private CharacterController _characterController;
        private ChickenMovement _movement;
        private ChickenClassRegistrySO _registry;
        private ChickenStatsSO _activeStats;

        /// <summary>The chicken's archetype. Replicated; set by the spawner via <c>OnBeforeSpawned</c>.</summary>
        [Networked] public ChickenClass Class { get; set; } = ChickenClass.Warrior;

        public ChickenStatsSO Stats => _activeStats != null ? _activeStats : _fallbackStats;

        [Inject]
        public void Construct(ChickenClassRegistrySO registry)
        {
            _registry = registry;
        }

        public override void Spawned()
        {
            // Fusion spawns NetworkBehaviours outside of Zenject's normal injection path,
            // so we self-inject from ProjectContext if Construct hasn't been called yet.
            if (_registry == null && ProjectContext.HasInstance)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _characterController = GetComponent<CharacterController>();
            _activeStats = ResolveStatsForClass(Class);

            if (_activeStats == null)
            {
                Debug.LogError(
                    $"[ChickenController] {name} could not resolve stats for class '{Class}'. " +
                    "Assign _fallbackStats on the prefab or populate the ChickenClassRegistry.",
                    this);
                return;
            }

            _movement = new ChickenMovement(_characterController, _activeStats);

            // Apply the per-class tint locally on every peer so even proxies look right.
            if (_registry != null && _registry.TryGet(Class, out var entry))
            {
                var visuals = GetComponent<ChickenVisuals>();
                if (visuals != null) visuals.ApplyTint(entry.TintColor);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_movement == null) return;
            if (!HasStateAuthority) return;

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
