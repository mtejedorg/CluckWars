using CluckWars.Abilities;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Ability-driven bot AI for solo mode. Runs only on the master client
    /// (StateAuthority). Implements the Phase R-Bot design from ROADMAP.md:
    /// a 5-tier decision priority that keeps the proven farm loop as its backbone
    /// and layers Flee / Hunt states + ability use on top.
    /// </summary>
    /// <remarks>
    /// <b>Decision priority (evaluated each throttled think tick):</b>
    /// <list type="number">
    ///   <item><b>Flee</b> — loaded + rival nearby → rush to base, fire Defense/Escape/Control.</item>
    ///   <item><b>Deposit</b> — cargo fraction ≥ <c>_returnThreshold</c> → return to base.</item>
    ///   <item><b>Hunt</b> — rival loaded, in range → chase and fire Steal/Offense/Control.</item>
    ///   <item><b>Collect</b> — walk to nearest pile; if rival contests, fire Control.</item>
    ///   <item><b>Idle</b> — nothing to do.</item>
    /// </list>
    ///
    /// Per-class personality is tuned via <c>switch (_controller.Class)</c> in
    /// <see cref="Spawned"/> — no separate code paths, just different numbers.
    ///
    /// All expensive lookups (<c>FindObjectsByType</c>, <c>OverlapSphere</c>) stay
    /// inside <c>Think()</c>, which runs at the throttled <c>_thinkInterval</c>
    /// cadence (default 0.3s). Per-tick cost is only <c>Navigate()</c> steering.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BotController : NetworkBehaviour
    {
        private const string Source = "Bot";

        private enum BotState { Idle, CollectFood, ReturnToBase, Flee, Hunt }

        // ---- Serialized tunables (Warrior / baseline defaults) ----------------

        [Tooltip("Seconds between FSM re-evaluations. Lower = smarter, costlier.")]
        [Min(0.05f)]
        [SerializeField] private float _thinkInterval = 0.3f;

        [Tooltip("Distance at which the bot considers itself 'arrived' at a target.")]
        [Min(0.1f)]
        [SerializeField] private float _arrivalRadius = 1.5f;

        [Tooltip("Cargo fraction [0,1] at which the bot turns around to deposit.")]
        [Range(0f, 1f)]
        [SerializeField] private float _returnThreshold = 0.70f;

        [Tooltip("Cargo fraction at which the bot starts Flee (protect the haul).")]
        [Range(0f, 1f)]
        [SerializeField] private float _protectCargoThreshold = 0.40f;

        [Tooltip("Radius within which a rival triggers the Flee state.")]
        [Min(0f)]
        [SerializeField] private float _dangerRadius = 5.0f;

        [Tooltip("Radius within which the bot will try to Hunt a loaded rival.")]
        [Min(0f)]
        [SerializeField] private float _huntRadius = 8.0f;

        [Tooltip("Rival cargo fraction required before the bot decides to Hunt.")]
        [Range(0f, 1f)]
        [SerializeField] private float _huntCargoThreshold = 0.30f;

        [Tooltip("Distance from the rival at which the bot fires an ability during Hunt.")]
        [Min(0f)]
        [SerializeField] private float _abilityRange = 4.0f;

        [Tooltip("Hysteresis: radii grow by this factor while the matching state is active, so a rival hovering on the boundary can't flip the FSM every think tick (BOT-8).")]
        [Min(1f)]
        [SerializeField] private float _stateExitRadiusFactor = 1.35f;

        // ---- Component references -------------------------------------------

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private AbilityController _abilities;
        private ILogService       _log;

        // ---- FSM state ------------------------------------------------------

        private BotState   _state       = BotState.Idle;
        private BotState   _prevState   = BotState.Idle;
        private Vector3    _moveTarget;
        private PlayerBase _homeBase;
        private float      _nextThinkTime;

        // ---- Perceived rival (cached per think tick, used by ReactWithAbility) --
        private ChickenController _perceivedRival;
        private float             _perceivedRivalDist;
        private float             _perceivedRivalCargo;

        // ---- Injection ------------------------------------------------------

        [Inject]
        public void Construct(ILogService log)
        {
            _log = log;
        }

        // ---- Fusion lifecycle -----------------------------------------------

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();
            _abilities  = GetComponent<AbilityController>();

            if (HasStateAuthority && _controller != null && _controller.IsBot)
                ApplyClassPersonality();

            _log?.Debug(Source, $"Spawned. HasStateAuthority={HasStateAuthority}, " +
                $"IsBot={(_controller != null ? _controller.IsBot.ToString() : "n/a")}.");
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            if (_controller == null || !_controller.IsBot) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsMatchRunning) return;

            if (_combat != null && _combat.IsStunned) return;

            if (Runner.SimulationTime >= _nextThinkTime)
            {
                _nextThinkTime = (float)Runner.SimulationTime + _thinkInterval;
                Think();
            }

            Navigate();
        }

        // ---- FSM -----------------------------------------------------------

        private void Think()
        {
            _prevState = _state;

            // Perceive the nearest rival once per think tick (cheap scan).
            _perceivedRival      = FindNearestRival(out _perceivedRivalDist, out _perceivedRivalCargo, requireCargo: false);

            float cargoFraction = _cargo != null ? _cargo.Fraction : 0f;

            // Hysteresis (BOT-8): while already fleeing/hunting, the trigger radius
            // grows so a rival hovering on the boundary can't flip the state every
            // 0.3s think. Entering still uses the base radius.
            float dangerR = _state == BotState.Flee ? _dangerRadius * _stateExitRadiusFactor : _dangerRadius;
            float huntR   = _state == BotState.Hunt ? _huntRadius   * _stateExitRadiusFactor : _huntRadius;

            // Priority 1: FLEE — loaded + rival within danger radius.
            if (cargoFraction >= _protectCargoThreshold &&
                _perceivedRival != null && _perceivedRivalDist <= dangerR)
            {
                _state      = BotState.Flee;
                _moveTarget = GetHomeBasePosition();
                ReactWithAbility(new[] { BotRole.Defense, BotRole.Escape, BotRole.Control },
                    alwaysFireIfReady: true);

                if (_state != _prevState)
                    _log?.Debug(Source, $"→ Flee (cargo={cargoFraction:P0}, rival dist={_perceivedRivalDist:0.0}).");
                return;
            }

            // Priority 2: DEPOSIT — cargo full enough to bank.
            if (cargoFraction >= _returnThreshold)
            {
                _state      = BotState.ReturnToBase;
                _moveTarget = GetHomeBasePosition();
                if (_state != _prevState)
                    _log?.Debug(Source, $"→ ReturnToBase (cargo={cargoFraction:P0}).");
                return;
            }

            // Priority 3: HUNT — a loaded rival in hunt radius + bot is aggressive.
            // The nearest rival may be empty while a loaded one stands a little
            // further away — re-scan with requireCargo before giving up on the hunt.
            if (_huntRadius > 0f)
            {
                var target     = _perceivedRival;
                var targetDist = _perceivedRivalDist;
                var targetCargo = _perceivedRivalCargo;
                if (target == null || targetCargo < _huntCargoThreshold)
                    target = FindNearestRival(out targetDist, out targetCargo, requireCargo: true);

                if (target != null && targetDist <= huntR && targetCargo >= _huntCargoThreshold)
                {
                    _state      = BotState.Hunt;
                    _moveTarget = target.transform.position;
                    if (targetDist <= _abilityRange)
                        ReactWithAbility(new[] { BotRole.Steal, BotRole.Offense, BotRole.Control },
                            alwaysFireIfReady: false);

                    if (_state != _prevState)
                        _log?.Debug(Source, $"→ Hunt rival at dist={targetDist:0.0}, cargo={targetCargo:P0}.");
                    return;
                }
            }

            // Priority 4: COLLECT — nearest non-empty pile OR ground pickup,
            // whichever is closer. Pickups matter most right after a hunt: the
            // stunned victim's dropped cargo is usually at the bot's feet.
            var pile   = FindNearestPile(out float pileSqr);
            var pickup = FindNearestPickup(out float pickupSqr);
            if (pickup != null && (pile == null || pickupSqr < pileSqr))
            {
                _state      = BotState.CollectFood;
                _moveTarget = pickup.transform.position;
                if (_state != _prevState)
                    _log?.Debug(Source, $"→ CollectFood (ground pickup, dist={Mathf.Sqrt(pickupSqr):0.0}).");
                return;
            }
            if (pile != null)
            {
                _state      = BotState.CollectFood;
                _moveTarget = pile.transform.position;

                // If a rival is contesting the same pile (and we're close enough for
                // the ability to actually land), try to displace them.
                if (_perceivedRival != null && _perceivedRivalDist <= _abilityRange)
                {
                    float rivalToPile = (_perceivedRival.transform.position - pile.transform.position).magnitude;
                    if (rivalToPile < _arrivalRadius * 2.5f)
                        ReactWithAbility(new[] { BotRole.Control }, alwaysFireIfReady: false);
                }

                if (_state != _prevState)
                    _log?.Debug(Source, $"→ CollectFood (pile '{pile.name}').");
                return;
            }

            // Priority 5: IDLE.
            _state = BotState.Idle;
            if (_state != _prevState)
                _log?.Debug(Source, "→ Idle (no piles found).");
        }

        // ---- Ability reaction -----------------------------------------------

        /// <summary>
        /// Tries each <paramref name="rolePreferences"/> in order; fires the first
        /// ready slot that matches. Does nothing if no match or ability already active.
        /// </summary>
        private void ReactWithAbility(BotRole[] rolePreferences, bool alwaysFireIfReady)
        {
            if (_abilities == null) return;
            foreach (var role in rolePreferences)
            {
                if (_abilities.TryGetReadySlotForRole(role, out int slot))
                {
                    if (_abilities.BotTryActivate(slot))
                    {
                        _log?.Debug(Source, $"ReactWithAbility: fired slot {slot} (role={role}).");
                        return;
                    }
                }
            }
        }

        // ---- Navigation ----------------------------------------------------

        private void Navigate()
        {
            var toTarget = _moveTarget - _controller.transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= _arrivalRadius * _arrivalRadius)
            {
                _controller.BotTick(Vector2.zero, Runner.DeltaTime);
                return;
            }

            var dir   = toTarget.normalized;
            var input = new Vector2(dir.x, dir.z);
            _controller.BotTick(input, Runner.DeltaTime);
        }

        // ---- Perception helpers (run inside throttled Think) ----------------

        /// <summary>
        /// Finds the nearest living, non-decoy rival. Sets <paramref name="dist"/>
        /// and <paramref name="cargoFraction"/>.
        /// </summary>
        private ChickenController FindNearestRival(
            out float dist, out float cargoFraction, bool requireCargo)
        {
            var all = ChickenController.ActiveControllers;
            var selfPos = _controller.transform.position;

            ChickenController best = null;
            float bestSqr = float.MaxValue;
            float bestCargo = 0f;

            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _controller) continue;
                if (c.IsDecoy) continue;
                if (c.Combat != null && c.Combat.IsStunned) continue;

                float sqr = (c.transform.position - selfPos).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                float frac = 0f;
                var cargo = c.GetComponent<ChickenCargo>();
                if (cargo != null) frac = cargo.Fraction;

                if (requireCargo && frac <= 0f) continue;

                bestSqr   = sqr;
                best      = c;
                bestCargo = frac;
            }

            dist          = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
            cargoFraction = bestCargo;
            return best;
        }

        /// <summary>Finds the nearest non-empty food pile.</summary>
        private FoodPile FindNearestPile(out float bestSqrOut)
        {
            var piles   = FoodPile.ActivePiles;
            var selfPos = _controller.transform.position;
            FoodPile best    = null;
            float    bestSqr = float.MaxValue;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null || p.IsEmpty) continue;
                float sqr = (p.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            bestSqrOut = bestSqr;
            return best;
        }

        /// <summary>Finds the nearest non-empty ground pickup (death-dropped cargo).</summary>
        private FoodPickup FindNearestPickup(out float bestSqrOut)
        {
            var pickups = FoodPickup.ActivePickups;
            var selfPos = _controller.transform.position;
            FoodPickup best    = null;
            float      bestSqr = float.MaxValue;
            for (int i = 0; i < pickups.Count; i++)
            {
                var p = pickups[i];
                if (p == null || p.IsEmpty) continue;
                if (p.Object == null || !p.Object.IsValid) continue;
                float sqr = (p.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            bestSqrOut = bestSqr;
            return best;
        }

        /// <summary>
        /// Returns the position of the bot's home base. Cached; re-resolved when
        /// the cached reference becomes invalid (scene reload, match restart).
        /// </summary>
        private Vector3 GetHomeBasePosition()
        {
            if (_homeBase != null && _homeBase.Object != null && _homeBase.Object.IsValid)
                return _homeBase.transform.position;

            // Exact identity first: the base at this bot's HomeCornerIndex (the only
            // one it can deposit at). Nearest-unowned is a legacy fallback for
            // chickens without a stamped corner.
            var bases   = PlayerBase.ActiveBases;
            var selfPos = _controller.transform.position;
            int homeCorner = _controller.HomeCornerIndex;
            PlayerBase nearest = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null) continue;
                if (homeCorner >= 0)
                {
                    if (b.CornerIndex == homeCorner) { nearest = b; break; }
                    continue;
                }
                if (b.Owner.IsRealPlayer) continue;
                float sqr = (b.transform.position - selfPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = b; }
            }
            _homeBase = nearest;
            if (_homeBase != null)
                _log?.Debug(Source, $"Home base resolved: '{_homeBase.name}' at {_homeBase.transform.position}.");
            else
                _log?.Warn(Source, "No unowned base found — bot will idle in place.");
            return _homeBase != null ? _homeBase.transform.position : _controller.transform.position;
        }

        // ---- Per-class personality ----------------------------------------

        /// <summary>
        /// Overwrites tuning fields from a per-class defaults table.
        /// Serialized values serve as Warrior / baseline; this narrows or widens
        /// them per class. No code-path branches — only numbers differ.
        /// </summary>
        private void ApplyClassPersonality()
        {
            if (_controller == null) return;
            switch (_controller.Class)
            {
                case ChickenClass.Warrior:
                    // Aggressive: hunts early, large detection, medium protect.
                    _huntRadius            = 10f;
                    _huntCargoThreshold    = 0.15f;
                    _protectCargoThreshold = 0.50f;
                    _dangerRadius          = 5.0f;
                    break;

                case ChickenClass.Assassin:
                    // Opportunist: hunts loaded rivals, flees early (squishy).
                    _huntRadius            = 8.0f;
                    _huntCargoThreshold    = 0.20f;
                    _protectCargoThreshold = 0.30f;
                    _dangerRadius          = 6.0f;
                    break;

                case ChickenClass.Fatty:
                    // Cautious turtle: never hunts (huntRadius = 0), deposits early.
                    _huntRadius            = 0f;
                    _huntCargoThreshold    = 1f;
                    _protectCargoThreshold = 0.25f;
                    _dangerRadius          = 7.0f;
                    _returnThreshold       = 0.50f;
                    break;

                case ChickenClass.Speedy:
                    // Hit-and-run: hunts only to snatch drops, flees very early.
                    _huntRadius            = 5.0f;
                    _huntCargoThreshold    = 0.40f;
                    _protectCargoThreshold = 0.20f;
                    _dangerRadius          = 8.0f;
                    break;
            }
            _log?.Debug(Source, $"Personality for {_controller.Class}: " +
                $"hunt={_huntRadius:0.0}, huntThresh={_huntCargoThreshold:P0}, " +
                $"protect={_protectCargoThreshold:P0}, danger={_dangerRadius:0.0}.");
        }
    }
}
