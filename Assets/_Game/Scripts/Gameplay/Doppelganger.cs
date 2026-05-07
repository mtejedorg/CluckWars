using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Marker NetworkBehaviour for a chicken-shaped decoy spawned by
    /// <c>DoppelgangerAbilitySO</c>. The decoy prefab is a Chicken prefab variant
    /// with <c>ChickenCargo</c> stripped — <c>ChickenController</c>,
    /// <c>ChickenCombat</c>, <c>AbilityController</c>, animator, and visuals all
    /// remain so the decoy plays idle / hit animations and reacts to incoming
    /// damage. This component sets <c>ChickenController.IsDecoy</c> so those
    /// systems gate out their input-processing branches; otherwise the caster's
    /// input would drive both their real chicken and the decoy in lockstep.
    /// </summary>
    /// <remarks>
    /// Lifetime is networked via <see cref="LifetimeTimer"/> so every peer agrees
    /// on the despawn moment. The decoy also despawns immediately when killed —
    /// hooked via <c>ChickenCombat.OnDeath</c> — so it doesn't loop through the
    /// stun-and-respawn behavior real chickens have.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(ChickenController))]
    public sealed class Doppelganger : NetworkBehaviour
    {
        private const string Source = "Doppelganger";

        [Tooltip("Default decoy lifetime if the spawner doesn't override LifetimeTimer.")]
        [Min(0.5f)]
        [SerializeField] private float _defaultLifetime = 4f;

        [Networked] public TickTimer LifetimeTimer { get; set; }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ILogService _log;
        private bool _subscribedToDeath;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();

            // Flip the decoy gate on every peer so input-processing branches in
            // ChickenController / ChickenCombat / AbilityController all early-return.
            // (The flag itself is local — every peer is reading it from the same
            // prefab template, no replication needed.)
            if (_controller != null) _controller.IsDecoy = true;

            // Despawn immediately on kill rather than looping the stun-respawn cycle.
            if (_combat != null && !_subscribedToDeath)
            {
                _combat.OnDeath += HandleDecoyDeath;
                _subscribedToDeath = true;
            }

            // Arm the lifetime timer if the spawner didn't set one.
            if (HasStateAuthority && !LifetimeTimer.IsRunning)
            {
                LifetimeTimer = TickTimer.CreateFromSeconds(Runner, _defaultLifetime);
            }

            _log?.Debug(Source, $"Spawned. Class={_controller?.Class}, HasStateAuthority={HasStateAuthority}.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_combat != null && _subscribedToDeath)
            {
                _combat.OnDeath -= HandleDecoyDeath;
                _subscribedToDeath = false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            if (LifetimeTimer.Expired(Runner))
            {
                _log?.Debug(Source, "Lifetime expired; despawning.");
                Runner.Despawn(Object);
            }
        }

        private void HandleDecoyDeath()
        {
            if (!HasStateAuthority) return;
            _log?.Debug(Source, "Killed; despawning early.");
            Runner.Despawn(Object);
        }
    }
}
