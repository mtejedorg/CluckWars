using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.AbilityLab
{
    /// <summary>
    /// Turns a spawned chicken into a practice target: parks it at a fixed mark, wipes
    /// whatever the last cast did to it every <see cref="ResetIntervalSeconds"/>, and
    /// optionally walks it back and forth so aim shapes can be tested against a moving
    /// body. Added at runtime by <see cref="AbilityLabBootstrapper"/>.
    /// </summary>
    /// <remarks>
    /// <b>A plain MonoBehaviour, not a NetworkBehaviour, and that is load-bearing.</b>
    /// Fusion bakes the <c>NetworkBehaviour</c> list and their state offsets into the
    /// prefab at edit time, so a <c>NetworkBehaviour</c> added with <c>AddComponent</c>
    /// after <c>Spawn</c> has no networked state and desynchronises the object's layout.
    /// Everything here therefore goes through RPCs and public setters that already exist
    /// on the shipped chicken components — this class owns no networked state of its own.
    /// <para>
    /// <b>Solo only.</b> <see cref="Patrol"/> drives <see cref="ChickenController.BotTick"/>
    /// from Unity's <c>FixedUpdate</c> rather than from <c>FixedUpdateNetwork</c>, which is
    /// correct only because the lab runs <c>GameMode.Single</c>: one peer, no prediction, no
    /// resimulation, no remote observer to disagree with the result. It would be wrong in
    /// Shared Mode, which is why <see cref="AbilityLabBootstrapper"/> refuses to start in
    /// anything else.
    /// </para>
    /// </remarks>
    public sealed class AbilityLabDummy : MonoBehaviour
    {
        private const string Source = "AbilityLab";

        /// <summary>Seconds between automatic resets. Live-editable from the lab HUD.</summary>
        /// <remarks>
        /// Setting this pulls the pending deadline in when the new interval is shorter than
        /// the time still on the clock. Without that, dragging the HUD slider from 30s down
        /// to 2s would leave you watching a 30-second countdown before the change took hold —
        /// in the one loop the lab exists to run. Lengthening deliberately does not stretch
        /// the cycle already in flight; only the ones after it.
        ///
        /// <c>Min</c> rather than an unconditional restart so that a caller assigning this
        /// every frame (the HUD guards against it; nothing forces it to) cannot push the
        /// deadline forever out of reach and stop resets happening at all.
        /// </remarks>
        public float ResetIntervalSeconds
        {
            get => _resetIntervalSeconds;
            set
            {
                _resetIntervalSeconds = Mathf.Max(0.1f, value);
                _nextResetTime = Mathf.Min(_nextResetTime, Time.time + _resetIntervalSeconds);
            }
        }
        private float _resetIntervalSeconds = 5f;

        /// <summary>When true the dummy never moves, not even under patrol.</summary>
        public bool Frozen = true;

        /// <summary>Walks the dummy back and forth across <see cref="PatrolHalfWidth"/>.</summary>
        public bool Patrol;

        /// <summary>Half the length of the patrol line, in metres, centred on home.</summary>
        public float PatrolHalfWidth = 4f;

        /// <summary>
        /// Food the dummy is restocked with on every reset, so the steal abilities have
        /// something to take.
        /// </summary>
        /// <remarks>
        /// Not cosmetic. Snatch, Sneaky Steal and Scrap all reject a target carrying nothing
        /// (<c>ExtraTargetFilter</c> requires <c>Cargo &gt; 0</c>), so a dummy reset to empty
        /// refuses all three with <c>NoTarget</c> and they cannot be felt at all — which is
        /// exactly what happened before this field existed.
        /// </remarks>
        public float CargoStock = 8f;

        /// <summary>Seconds until the next automatic reset, for the HUD countdown.</summary>
        public float TimeToNextReset => Mathf.Max(0f, _nextResetTime - Time.time);

        private ChickenController _controller;
        private ChickenCargo _cargo;
        private ChickenCombat _combat;
        private ILogService _log;

        private Vector3 _home;
        private Quaternion _homeRotation;
        private float _nextResetTime;
        private int _patrolDirection = 1;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        /// <summary>
        /// Binds the dummy to the chicken it rides on and records the mark it returns to.
        /// Called by the bootstrapper straight after <c>Spawn</c>.
        /// </summary>
        public void Bind(ChickenController controller, Vector3 home, Quaternion homeRotation)
        {
            _controller   = controller;
            _cargo        = controller != null ? controller.GetComponent<ChickenCargo>() : null;
            _combat       = controller != null ? controller.GetComponent<ChickenCombat>() : null;
            _home         = home;
            _homeRotation = homeRotation;
            _nextResetTime = Time.time + ResetIntervalSeconds;
            Restock();

            if (_controller == null)
                _log?.Error(Source, "AbilityLabDummy.Bind called with a null ChickenController — " +
                    "this dummy will never reset. The bootstrapper spawned a chicken prefab with no " +
                    "ChickenController on it.");
        }

        private void Update()
        {
            if (_controller == null) return;

            if (Time.time >= _nextResetTime)
            {
                ResetNow("timer");
            }
        }

        private void FixedUpdate()
        {
            if (_controller == null || !_controller.HasStateAuthority) return;
            if (Frozen || !Patrol) return;

            // Flip at the ends of the line. Compared along world X because the patrol line
            // is authored as "left and right of the mark" from the player's fixed viewpoint.
            float offset = _controller.transform.position.x - _home.x;
            if (offset > PatrolHalfWidth) _patrolDirection = -1;
            else if (offset < -PatrolHalfWidth) _patrolDirection = 1;

            _controller.BotTick(new Vector2(_patrolDirection, 0f), Time.fixedDeltaTime);
        }

        /// <summary>
        /// Wipes every effect the dummy is carrying and puts it back on its mark.
        /// Safe to call at any time; the HUD's manual reset key routes here too.
        /// </summary>
        public void ResetNow(string reason)
        {
            _nextResetTime = Time.time + ResetIntervalSeconds;
            if (_controller == null) return;

            // Revive first: a removed chicken ignores control-state writes, so resetting a
            // dummy that an execute just deleted would otherwise leave it inert on the mark.
            if (_combat != null && _combat.IsRemoved) _combat.RPC_ResetForNewMatch();

            _controller.RPC_ResetControlStates();
            _controller.RPC_TeleportTo(_home);
            _controller.transform.rotation = _homeRotation;

            // Not covered by RPC_ResetControlStates — these are cargo/steal concerns.
            _controller.StealBackActive = false;
            _controller.VerticalVelocity = 0f;
            _cargo?.RPC_ResetForNewMatch();
            Restock();

            if (_log != null && _log.IsEnabled(LogLevel.Verbose))
                _log.Verbose(Source, $"Dummy reset ({reason}).");
        }

        /// <summary>
        /// Refills the dummy's cargo to <see cref="CargoStock"/>, clamped to what it can
        /// actually hold. Runs after the cargo reset, which zeroes it.
        /// </summary>
        private void Restock()
        {
            if (_cargo == null || !_cargo.HasStateAuthority) return;
            _cargo.Cargo = Mathf.Clamp(CargoStock, 0f, _cargo.Capacity);
        }
    }
}
