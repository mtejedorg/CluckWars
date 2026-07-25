using CluckWars.Abilities;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
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

        [Tooltip("Distance at which the bot considers itself 'arrived' at a target. Not used for piles — see _pileStandoff.")]
        [Min(0.1f)]
        [SerializeField] private float _arrivalRadius = 1.5f;

        [Tooltip("How far past a pile's surface the bot aims when collecting. Piles are solid, so aiming at the centre grinds the bot into the blocker forever; aiming a little OUTSIDE the surface gives it a reachable point on walkable ground. Must stay above the NavMesh agent radius (0.5) so the point isn't inside the pile's carve, and inside FoodPile._collectReach so standing there actually collects.")]
        [Min(0.1f)]
        [SerializeField] private float _pileStandoff = 0.7f;

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

        [Tooltip("Aggressive classes pick fights with rivals even when the rival carries no cargo (creates skirmishes / pressure). Off = only hunt loaded rivals.")]
        [SerializeField] private bool _engageUnloaded = false;

        [Tooltip("Radius within which an _engageUnloaded bot will chase an unloaded rival to pressure them. Tighter than _huntRadius so they don't chase from across the map.")]
        [Min(0f)]
        [SerializeField] private float _engageRadius = 6.0f;

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

        /// <summary>
        /// The pile the bot is currently walking to, or null. Arrival at a pile is
        /// "am I in its collect range", not "am I within <see cref="_arrivalRadius"/> of a
        /// point" — a fixed radius against a standoff point would let the bot stop up to
        /// <c>_arrivalRadius</c> short of a big island and stand there collecting nothing.
        /// </summary>
        private FoodPile _pileGoal;

        // ---- NavMesh path following (walls + solid piles are obstacles) ------
        private NavMeshPath _navPath;
        private Vector3     _lastPathTarget = new Vector3(float.MinValue, 0f, float.MinValue);
        private int         _navCorner = -1;

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

            if (_combat != null && _combat.IsRemoved) return;

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

            // Any decision below that isn't "go collect from that pile" invalidates the
            // pile arrival rule; the collect branch re-arms it.
            _pileGoal = null;

            // Perceive the nearest rival once per think tick (cheap scan).
            _perceivedRival      = FindNearestRival(out _perceivedRivalDist, out _perceivedRivalCargo, requireCargo: false);

            float cargoFraction = _cargo != null ? _cargo.Fraction : 0f;

            // Latch ReturnToBase until all cargo is deposited (IP1)
            if (_state == BotState.ReturnToBase && _cargo != null && _cargo.Cargo > 0f)
            {
                _moveTarget = GetHomeBasePosition();
                return;
            }

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

            // Priority 3: HUNT — a loaded rival anywhere in hunt radius, OR (aggressive
            // class) ANY rival inside the tighter engage radius even unloaded → pick a
            // fight. The latter is what makes the player feel pressured instead of left
            // to farm in peace. The nearest rival may be empty while a loaded one stands
            // a little further away — re-scan with requireCargo before giving up.
            if (_huntRadius > 0f || _engageUnloaded)
            {
                var target     = _perceivedRival;
                var targetDist = _perceivedRivalDist;
                var targetCargo = _perceivedRivalCargo;
                if (target == null || targetCargo < _huntCargoThreshold)
                    target = FindNearestRival(out targetDist, out targetCargo, requireCargo: true);

                bool loadedHunt = target != null && targetDist <= huntR && targetCargo >= _huntCargoThreshold;

                // Aggressive engage: chase the nearest rival (loaded or not) when close.
                bool aggroEngage = false;
                if (!loadedHunt && _engageUnloaded && _perceivedRival != null)
                {
                    float engageR = _state == BotState.Hunt ? _engageRadius * _stateExitRadiusFactor : _engageRadius;
                    if (_perceivedRivalDist <= engageR)
                    {
                        target = _perceivedRival; targetDist = _perceivedRivalDist; targetCargo = _perceivedRivalCargo;
                        aggroEngage = true;
                    }
                }

                if (loadedHunt || aggroEngage)
                {
                    _state      = BotState.Hunt;
                    _moveTarget = target.transform.position;
                    if (targetDist <= _abilityRange)
                        ReactWithAbility(aggroEngage
                                ? new[] { BotRole.Offense, BotRole.Control, BotRole.Steal }   // empty target: hurt/pin
                                : new[] { BotRole.Steal, BotRole.Offense, BotRole.Control },  // loaded target: rob first
                            alwaysFireIfReady: false);

                    if (_state != _prevState)
                        _log?.Debug(Source, $"→ Hunt rival at dist={targetDist:0.0}, cargo={targetCargo:P0}.");
                    return;
                }
            }

            // Priority 4: COLLECT — nearest non-empty pile OR ground pickup,
            // whichever is closer. Pickups matter most right after a hunt: the
            // stunned victim's dropped cargo is usually at the bot's feet.
            var selfPos = _controller.transform.position;
            var pile   = FindNearestPile(out float pileDist);
            var pickup = FindNearestPickup(out float pickupDist);
            if (pickup != null && (pile == null || pickupDist < pileDist))
            {
                _state      = BotState.CollectFood;
                _moveTarget = pickup.transform.position;
                if (_state != _prevState)
                    _log?.Debug(Source, $"→ CollectFood (ground pickup, dist={pickupDist:0.0}).");
                return;
            }
            if (pile != null)
            {
                _state      = BotState.CollectFood;
                // Steer to the pile's rim, not its centre: the centre of a stocked pile is
                // inside a solid blocker, and on a 7×4 island that is 3.5 m of wall the bot
                // would grind against forever without ever collecting.
                _pileGoal   = pile;
                _moveTarget = pile.SurfaceApproachPoint(selfPos, _pileStandoff);

                // If a rival is contesting the same pile (and we're close enough for
                // the ability to actually land), try to displace them. Contest is measured
                // to the pile's SURFACE — a centre-distance threshold is never satisfied
                // once the pile is wider than the threshold itself.
                if (_perceivedRival != null && _perceivedRivalDist <= _abilityRange)
                {
                    if (pile.DistanceToSurface(_perceivedRival.transform.position) < _arrivalRadius)
                        ReactWithAbility(new[] { BotRole.Control }, alwaysFireIfReady: false);
                }

                if (_state != _prevState)
                    _log?.Debug(Source, $"→ CollectFood (pile '{pile.name}', surface dist={pileDist:0.0}).");
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
            var selfPos  = _controller.transform.position;
            var toTarget = _moveTarget - selfPos;
            toTarget.y = 0f;

            if (HasArrived(selfPos, toTarget))
            {
                _controller.BotTick(Vector2.zero, Runner.DeltaTime);
                return;
            }

            // Steer along the NavMesh path (walls + solid piles are obstacles);
            // fall back to the direct line when no path resolves.
            var steer = ResolveSteerPoint(selfPos);
            var dir   = steer - selfPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = toTarget;
            dir.Normalize();
            _controller.BotTick(new Vector2(dir.x, dir.z), Runner.DeltaTime);
        }

        /// <summary>
        /// Has the bot reached what it was walking to? For a pile the answer is the only
        /// one that matters — "am I in collect range of its surface". A radius test against
        /// the standoff point would let the bot stop up to <see cref="_arrivalRadius"/>
        /// short and stand next to an island collecting nothing.
        /// </summary>
        private bool HasArrived(Vector3 selfPos, Vector3 toTarget)
        {
            if (_pileGoal != null && _pileGoal.Object != null && _pileGoal.Object.IsValid)
                return _pileGoal.IsWithinCollectRange(selfPos);

            return toTarget.sqrMagnitude <= _arrivalRadius * _arrivalRadius;
        }

        /// <summary>
        /// The next NavMesh path corner to steer toward. Recomputes the path when
        /// <see cref="_moveTarget"/> drifts past a threshold from the last computed
        /// target. Falls back to direct steering when sampling or pathing fails —
        /// CharacterController sliding still handles glancing contacts.
        /// </summary>
        /// <remarks>
        /// The threshold is tight while <see cref="_pileGoal"/> is set and loose otherwise.
        /// This used to be a flat 1 m, back when a pile target was <c>pile.transform.position</c>
        /// — genuinely fixed, so any distance test worked. It is now
        /// <see cref="FoodPile.SurfaceApproachPoint"/>, recomputed fresh every Think() tick
        /// from the bot's current position AND the pile's current footprint. Both of those
        /// drift by less than a metre per tick almost always — most of all as a pile drains
        /// and its footprint shrinks, which pulls the correct standoff point inward in small
        /// steps. A 1 m gate silently never re-fires against drift that small: the bot keeps
        /// following a path aimed at a footprint that no longer exists, arrives at a point
        /// that used to be the rim and no longer is, and sits there — just outside
        /// <see cref="FoodPile.IsWithinCollectRange"/> forever, never being told the target
        /// moved because it never moved "enough". Verified live 2026-07-24: three bots frozen
        /// this way, each parked 1.2–1.9 m from a pile whose footprint had shrunk out from
        /// under a stale path. A moving rival (the other <see cref="_moveTarget"/> source) has
        /// no such problem — it covers a metre in a fraction of a Think() interval, so the
        /// loose threshold never starves it.
        /// </remarks>
        private Vector3 ResolveSteerPoint(Vector3 selfPos)
        {
            if (_navPath == null) _navPath = new NavMeshPath();

            float repathThresholdSqr = _pileGoal != null ? 0.04f : 1f; // 0.2 m vs 1 m
            if ((_moveTarget - _lastPathTarget).sqrMagnitude > repathThresholdSqr)
            {
                _lastPathTarget = _moveTarget;
                bool ok = NavMesh.SamplePosition(selfPos, out var fromHit, 2f, NavMesh.AllAreas)
                       && NavMesh.SamplePosition(_moveTarget, out var toHit, 2.5f, NavMesh.AllAreas)
                       && NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _navPath)
                       && _navPath.corners.Length > 1;
                _navCorner = ok ? 1 : -1;
            }

            if (_navCorner < 0 || _navPath.status == NavMeshPathStatus.PathInvalid)
                return _moveTarget;

            var corners = _navPath.corners;
            // Advance past corners we've reached (XZ, ~0.6 m threshold).
            while (_navCorner < corners.Length - 1)
            {
                float dx = corners[_navCorner].x - selfPos.x;
                float dz = corners[_navCorner].z - selfPos.z;
                if (dx * dx + dz * dz > 0.36f) break;
                _navCorner++;
            }
            return corners[Mathf.Min(_navCorner, corners.Length - 1)];
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
                if (c.Combat != null && c.Combat.IsRemoved) continue;

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

        /// <summary>
        /// Finds the nearest pile that still has food a chicken can actually take, ranked by
        /// distance to its SURFACE (<paramref name="bestDistanceOut"/>) so the choice stays
        /// honest across wildly different pile sizes — by centre distance a bot standing on
        /// the 7×4 island's rim would rate a small pile 3 m away as closer.
        /// Not <c>IsEmpty</c>: the permanent centre pile (ADR 0003 Decision 2b) is never
        /// empty, so a bot would otherwise park on its floor and collect nothing forever.
        /// </summary>
        private FoodPile FindNearestPile(out float bestDistanceOut)
        {
            var piles   = FoodPile.ActivePiles;
            var selfPos = _controller.transform.position;
            FoodPile best     = null;
            float    bestDist = float.MaxValue;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (!p.HasCollectableFood) continue;
                float dist = p.DistanceToSurface(selfPos);
                if (dist < bestDist) { bestDist = dist; best = p; }
            }
            bestDistanceOut = bestDist;
            return best;
        }

        /// <summary>
        /// Finds the nearest non-empty ground pickup (death-dropped cargo). Returns a plain
        /// distance, not a squared one, so it is directly comparable with the pile's surface
        /// distance in <see cref="Think"/>.
        /// </summary>
        private FoodPickup FindNearestPickup(out float bestDistanceOut)
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
            bestDistanceOut = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
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
                    // Aggressive bruiser: picks fights with anyone nearby, loaded or not.
                    _huntRadius            = 10f;
                    _huntCargoThreshold    = 0.15f;
                    _protectCargoThreshold = 0.50f;
                    _dangerRadius          = 5.0f;
                    _engageUnloaded        = true;
                    _engageRadius          = 8.5f;
                    break;

                case ChickenClass.Assassin:
                    // Opportunist disruptor: harasses unloaded rivals, robs loaded ones,
                    // flees early (squishy).
                    _huntRadius            = 8.0f;
                    _huntCargoThreshold    = 0.20f;
                    _protectCargoThreshold = 0.30f;
                    _dangerRadius          = 6.0f;
                    _engageUnloaded        = true;
                    _engageRadius          = 6.5f;
                    break;

                case ChickenClass.Fatty:
                    // Cautious turtle: never hunts (huntRadius = 0), deposits early,
                    // never picks fights — the farming foil to the aggressive classes.
                    _huntRadius            = 0f;
                    _huntCargoThreshold    = 1f;
                    _protectCargoThreshold = 0.25f;
                    _dangerRadius          = 7.0f;
                    _returnThreshold       = 0.50f;
                    _engageUnloaded        = false;
                    break;

                case ChickenClass.Speedy:
                    // Hit-and-run harasser: darts in to peck a nearby rival, then flees
                    // very early when it picks anything up.
                    _huntRadius            = 5.0f;
                    _huntCargoThreshold    = 0.40f;
                    _protectCargoThreshold = 0.20f;
                    _dangerRadius          = 8.0f;
                    _engageUnloaded        = true;
                    _engageRadius          = 6.0f;
                    break;
            }
            _log?.Debug(Source, $"Personality for {_controller.Class}: " +
                $"hunt={_huntRadius:0.0}, huntThresh={_huntCargoThreshold:P0}, " +
                $"protect={_protectCargoThreshold:P0}, danger={_dangerRadius:0.0}, " +
                $"engageUnloaded={_engageUnloaded}, engageR={_engageRadius:0.0}.");
        }
    }
}
