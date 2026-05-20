using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Simple FSM AI for bot chickens spawned in solo mode. Runs only on the
    /// master client (StateAuthority). Drives locomotion via
    /// <see cref="ChickenController.BotTick"/> and attacks via
    /// <see cref="ChickenCombat.BotTrySwing"/>.
    /// </summary>
    /// <remarks>
    /// <b>States</b>
    /// <list type="bullet">
    ///   <item><c>Idle</c> — default; re-evaluated each think tick.</item>
    ///   <item><c>CollectFood</c> — walks toward the nearest non-empty pile.</item>
    ///   <item><c>ReturnToBase</c> — walks toward the bot's nearest unowned base
    ///     when cargo exceeds <c>_returnThreshold</c> fraction.</item>
    /// </list>
    /// Attack is attempted every tick regardless of locomotion state — if an enemy
    /// enters <c>_aggroRange</c> the bot swings immediately. The attack cooldown in
    /// <see cref="ChickenCombat"/> prevents abuse.
    ///
    /// <b>Maestro</b>: add this component to the Chicken prefab alongside
    /// <see cref="ChickenController"/>. It self-guards on <c>IsBot</c> so it is
    /// harmless when attached to player-controlled chickens.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BotController : NetworkBehaviour
    {
        private const string Source = "Bot";
        private enum BotState { Idle, CollectFood, ReturnToBase }

        [Tooltip("Seconds between path-replanning decisions. Lower = more responsive, higher = cheaper.")]
        [Min(0.05f)]
        [SerializeField] private float _thinkInterval = 0.3f;

        [Tooltip("Minimum distance to the move target before the bot considers itself 'arrived'.")]
        [Min(0.1f)]
        [SerializeField] private float _arrivalRadius = 1.5f;

        [Tooltip("Cargo fill fraction [0,1] at which the bot turns around to deposit.")]
        [Range(0f, 1f)]
        [SerializeField] private float _returnThreshold = 0.70f;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private ILogService        _log;

        private BotState   _state     = BotState.Idle;
        private BotState   _prevState = BotState.Idle;
        private Vector3    _moveTarget;
        private PlayerBase _homeBase;
        private float      _nextThinkTime;

        [Inject]
        public void Construct(ILogService log)
        {
            _log = log;
        }

        // ---- Fusion lifecycle --------------------------------------------------

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}, " +
                $"IsBot={(_controller != null ? _controller.IsBot.ToString() : "n/a")}.");
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            if (_controller == null || !_controller.IsBot) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            // Stunned bots can't move or attack — but we do re-evaluate home base
            // lazily on respawn so the stale cache gets refreshed next think.
            if (_combat != null && _combat.IsStunned) return;

            // Throttled re-planning — avoids expensive FindObjectsByType every tick.
            if (Runner.SimulationTime >= _nextThinkTime)
            {
                _nextThinkTime = (float)Runner.SimulationTime + _thinkInterval;
                Think();
            }

            Navigate();

            // Opportunistic melee: the combat cooldown prevents rapid-fire swings.
            _combat?.BotTrySwing();
        }

        // ---- FSM ---------------------------------------------------------------

        private void Think()
        {
            _prevState = _state;

            // Priority 1: return to base when cargo is sufficiently full.
            if (_cargo != null && _cargo.Fraction >= _returnThreshold)
            {
                _state      = BotState.ReturnToBase;
                _moveTarget = GetHomeBasePosition();
                if (_state != _prevState)
                    _log?.Debug(Source, $"State {_prevState} → ReturnToBase " +
                        $"(cargo={_cargo.Fraction:P0}). Target={_moveTarget}.");
                return;
            }

            // Priority 2: collect from the nearest non-empty pile.
            var pile = FindNearestPile();
            if (pile != null)
            {
                _state      = BotState.CollectFood;
                _moveTarget = pile.transform.position;
                if (_state != _prevState)
                    _log?.Debug(Source, $"State {_prevState} → CollectFood. " +
                        $"Target pile='{pile.name}' at {_moveTarget}.");
                return;
            }

            // Nothing to do — idle.
            _state = BotState.Idle;
            if (_state != _prevState)
                _log?.Debug(Source, $"State {_prevState} → Idle (no piles found).");
        }

        private void Navigate()
        {
            var toTarget = _moveTarget - _controller.transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= _arrivalRadius * _arrivalRadius)
            {
                // Close enough — stop and let cargo/deposit logic handle the rest.
                _controller.BotTick(Vector2.zero, Runner.DeltaTime);
                return;
            }

            var dir   = toTarget.normalized;
            var input = new Vector2(dir.x, dir.z);
            _controller.BotTick(input, Runner.DeltaTime);
        }

        // ---- Helpers -----------------------------------------------------------

        /// <summary>
        /// Returns the position of this bot's home base. Cached after first resolution;
        /// re-searched if the cached reference becomes invalid (scene reload, match restart).
        /// Bots deposit at the <em>nearest unowned</em> base — <see cref="GameManager"/>
        /// only assigns owned bases to real players, so bot bases stay as
        /// <see cref="Fusion.PlayerRef.None"/> throughout.
        /// </summary>
        private Vector3 GetHomeBasePosition()
        {
            if (_homeBase != null && _homeBase.Object != null && _homeBase.Object.IsValid)
                return _homeBase.transform.position;

            // Re-resolve: find nearest unowned base.
            var bases  = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var selfPos = _controller.transform.position;
            PlayerBase nearest = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || b.Owner.IsRealPlayer) continue; // skip player-owned
                float sqr = (b.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = b; }
            }
            _homeBase = nearest;
            if (_homeBase != null)
                _log?.Debug(Source, $"Home base resolved: '{_homeBase.name}' at {_homeBase.transform.position}.");
            else
                _log?.Warn(Source, "GetHomeBasePosition: no unowned base found — bot will idle in place.");
            return _homeBase != null ? _homeBase.transform.position : selfPos;
        }

        /// <summary>Finds the nearest non-empty food pile. Runs inside throttled Think().</summary>
        private FoodPile FindNearestPile()
        {
            var piles   = FindObjectsByType<FoodPile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var selfPos  = _controller.transform.position;
            FoodPile best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < piles.Length; i++)
            {
                var p = piles[i];
                if (p == null || p.IsEmpty) continue;
                float sqr = (p.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best;
        }
    }
}
