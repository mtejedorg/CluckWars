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
    /// (StateAuthority). The FSM keeps the proven farm loop as its backbone and layers
    /// per-class strategy, aimed casting and clock awareness on top.
    /// </summary>
    /// <remarks>
    /// <b>Decision priority (evaluated each throttled think tick):</b>
    /// <list type="number">
    ///   <item><b>Bank or lose it</b> — holding cargo with only just enough time to walk home.</item>
    ///   <item><b>Retreat</b> — loaded + rival nearby → run home, casting the strategy's retreat plan.</item>
    ///   <item><b>Deposit</b> — cargo ≥ the phase-adjusted return threshold → bank it.</item>
    ///   <item><b>Hunt / Stalk</b> — the highest-<i>value</i> rival in reach, not the nearest one.</item>
    ///   <item><b>Guard</b> — a Bunker whose own base is being approached goes home and holds it.</item>
    ///   <item><b>Collect</b> — the cheapest pile by round-trip cost; contest it if a rival is on it.</item>
    ///   <item><b>Idle</b> — nothing to do.</item>
    /// </list>
    ///
    /// <b>Per-class behaviour lives in <see cref="BotTactics"/>, not here.</b> This class
    /// never switches on <see cref="ChickenClass"/>; it reads one <see cref="BotProfile"/>
    /// at spawn and asks <see cref="BotTactics"/> for every judgement call. That split is
    /// what makes bot decisions EditMode-testable — see <c>BotTacticsTests</c>.
    ///
    /// <b>Three things changed on 2026-08-23 that are worth knowing before editing:</b>
    /// <list type="bullet">
    ///   <item><b>Bots aim.</b> A cast that is in reach but off-axis no longer fires into
    ///   empty space; the bot spends the tick turning (<see cref="ChickenController.BotFace"/>)
    ///   and fires on the next one. Before this every Cone and Capsule ability in the game
    ///   was a coin flip on whatever heading the navmesh left behind.</item>
    ///   <item><b>Reach is per-ability.</b> The old flat <c>_abilityRange = 4</c> was wrong
    ///   for most of the roster in both directions.</item>
    ///   <item><b>Casts are planned, not first-match.</b> A plan is a role preference list;
    ///   inside one role the cheaper cooldown wins, so a Warrior stops opening every
    ///   skirmish with its 12-second ability.</item>
    /// </list>
    ///
    /// All expensive lookups stay inside <c>Think()</c>, which runs at the profile's
    /// throttled cadence. Per-tick cost is only <c>Navigate()</c> steering.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BotController : NetworkBehaviour
    {
        private const string Source = "Bot";

        /// <summary>
        /// Shortest remaining leg that justifies spending a mobility cooldown to travel it.
        /// Roughly one Speed Burst's worth of gain on the shipped arena; below it the burst
        /// expires before it has saved anything.
        /// </summary>
        private const float MinSprintWorthDistance = 6f;

        private enum BotState { Idle, CollectFood, ReturnToBase, Flee, Hunt, Stalk, Guard, Search }

        // ---- Serialized tunables (geometry only — behaviour comes from BotProfile) ----

        [Tooltip("Distance at which the bot considers itself 'arrived' at a target. Not used for piles — see _pileStandoff.")]
        [Min(0.1f)]
        [SerializeField] private float _arrivalRadius = 1.5f;

        [Tooltip("How far past a pile's surface the bot aims when collecting. Piles are solid, so aiming at the centre grinds the bot into the blocker forever; aiming a little OUTSIDE the surface gives it a reachable point on walkable ground. Must stay above the NavMesh agent radius (0.5) so the point isn't inside the pile's carve, and inside FoodPile._collectReach so standing there actually collects.")]
        [Min(0.1f)]
        [SerializeField] private float _pileStandoff = 0.7f;

        [Tooltip("Hysteresis: radii grow by this factor while the matching state is active, so a rival hovering on the boundary can't flip the FSM every think tick (BOT-8).")]
        [Min(1f)]
        [SerializeField] private float _stateExitRadiusFactor = 1.35f;

        [Tooltip("How far a stalking Predator keeps from its mark while waiting for a steal to come off cooldown, as a fraction of that ability's reach. Below 1 so it stays inside the pounce window; well above 0 so it isn't just standing on them being obvious.")]
        [Range(0.3f, 1.2f)]
        [SerializeField] private float _stalkStandoffFraction = 0.80f;

        // ---- Component references -------------------------------------------

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private AbilityController _abilities;
        private ILogService       _log;

        // ---- Strategy -------------------------------------------------------

        private BotProfile _profile;

        // ---- FSM state ------------------------------------------------------

        private BotState   _state       = BotState.Idle;
        private BotState   _prevState   = BotState.Idle;
        private Vector3    _moveTarget;
        private PlayerBase _homeBase;
        private float      _nextThinkTime;

        /// <summary>
        /// Where the bot must be pointing before its chosen cast will land, or null when
        /// nothing is waiting on facing. Set by <see cref="TryCast"/> when an ability is
        /// ready and in reach but off-axis; consumed by <see cref="Navigate"/>, which
        /// spends the tick turning instead of translating. Cleared at the top of every
        /// <see cref="Think"/> so a stale intent can never freeze the bot in place.
        /// </summary>
        private Vector3? _faceIntent;

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

        // ---- Perception (cached per think tick) -----------------------------

        /// <summary>Nearest living rival, whatever they are carrying. Drives the danger check.</summary>
        private ChickenController _nearestRival;
        private float             _nearestRivalDist;

        /// <summary>Highest-<see cref="BotTactics.TargetPriority"/> rival, or null. Drives Hunt.</summary>
        private ChickenController _preyTarget;
        private float             _preyDist;
        private float             _preyCargo;

        private MatchPhase _phase   = MatchPhase.Opening;
        private bool       _leading;

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

            _profile = BotTactics.ProfileFor(_controller != null ? _controller.Class : ChickenClass.Warrior);

            // Stagger the first think so four bots spawned on the same tick do not all run
            // their (allocating, O(chickens x bases)) perception pass on the same frame for
            // the rest of the match. Keyed off the corner index rather than Random so a
            // replayed match staggers identically.
            int corner = _controller != null ? Mathf.Max(0, _controller.HomeCornerIndex) : 0;
            _nextThinkTime = (float)Runner.SimulationTime + corner * (_profile.ThinkInterval * 0.25f);

            if (HasStateAuthority && _controller != null && _controller.IsBot)
                _log?.Debug(Source, $"{_controller.Class} → {_profile.Strategy}: " +
                    $"hunt={_profile.HuntRadius:0.0}, engage={_profile.EngageRadius:0.0}, " +
                    $"guard={_profile.GuardRadius:0.0}, return={_profile.ReturnThreshold:P0}, " +
                    $"roundTripBias={_profile.RoundTripBias:0.00}, leaderFocus={_profile.LeaderFocus:0.00}.");

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
                _nextThinkTime = (float)Runner.SimulationTime + _profile.ThinkInterval;
                Think();
            }

            Navigate();
        }

        // ---- FSM -----------------------------------------------------------

        private void Think()
        {
            _prevState = _state;

            // Any decision below that isn't "go collect from that pile" invalidates the
            // pile arrival rule; the collect branch re-arms it. Same for the aim intent —
            // it describes one specific pending cast and must not outlive the decision
            // that produced it, or the bot stands turning toward a rival that left.
            _pileGoal   = null;
            _faceIntent = null;

            // Resolve home BEFORE perceiving: Perceive's leading check reads _homeBase, and
            // on the very first think it would otherwise be null and report "not leading".
            var homePos = GetHomeBasePosition();

            Perceive();

            float cargo         = _cargo != null ? _cargo.Cargo : 0f;
            float cargoFraction = _cargo != null ? _cargo.Fraction : 0f;
            float distHome      = PlanarDistance(_controller.transform.position, homePos);

            // Priority 0: BANK OR LOSE IT. Cargo in the beak scores nothing when the timer
            // expires, and on a 45 s match the window to walk home is small. This outranks
            // everything, including a fight the bot is winning.
            if (BotTactics.MustBankNow(cargo, distHome, EstimateMoveSpeed(),
                                       MatchTimeRemaining(), EstimateDepositSeconds(cargo)))
            {
                EnterReturnToBase(homePos, "clock");
                return;
            }

            // Latch ReturnToBase until all cargo is deposited (IP1). Fire the Bank plan
            // once actually on the base — a deposit accelerator is worthless anywhere else,
            // which is exactly why Quick Drop is BotRole.Bank and not Forage.
            if (_state == BotState.ReturnToBase && cargo > 0f)
            {
                _moveTarget = homePos;
                if (distHome <= HomeDepositRadius())
                    TryCast(BotSituation.Banking, null);
                else
                    TryGuardOverlay();
                return;
            }

            // Hysteresis (BOT-8): while already fleeing/hunting, the trigger radius grows so
            // a rival hovering on the boundary can't flip the state every think tick.
            float dangerR = _state == BotState.Flee ? _profile.DangerRadius * _stateExitRadiusFactor
                                                    : _profile.DangerRadius;

            // Priority 1: RETREAT — loaded, and something is close enough to take it.
            if (cargoFraction >= _profile.ProtectCargoThreshold &&
                _nearestRival != null && _nearestRivalDist <= dangerR)
            {
                _state      = BotState.Flee;
                _moveTarget = homePos;
                TryCast(BotSituation.Retreating, _nearestRival);

                if (_state != _prevState)
                    _log?.Debug(Source, $"→ Flee (cargo={cargoFraction:P0}, rival dist={_nearestRivalDist:0.0}).");
                return;
            }

            // Priority 2: DEPOSIT — enough banked-in-beak to be worth the walk. The
            // threshold moves with the clock and the scoreboard: a leader in the endgame
            // banks early and stops being a target, a trailing bot holds a bigger load.
            if (cargoFraction >= BotTactics.ReturnThreshold(_profile, _phase, _leading))
            {
                EnterReturnToBase(homePos, $"cargo={cargoFraction:P0}");
                return;
            }

            // Priority 3: HUNT / STALK — the most VALUABLE rival in reach, which is very
            // often not the nearest one. Empty-handed rivals are only worth attacking
            // inside the tighter engage radius, and only for strategies that skirmish.
            if (_preyTarget != null && TryEngage())
                return;

            // Priority 4: GUARD — a Bunker whose own base is being approached goes home and
            // holds it. This is the only state that treats territory as worth defending;
            // for every other strategy the base is just a drop-off.
            if (_profile.Strategy == BotStrategy.Bunker && _profile.GuardRadius > 0f &&
                _nearestRival != null &&
                PlanarDistance(_nearestRival.transform.position, homePos) <= _profile.GuardRadius)
            {
                _state      = BotState.Guard;
                _moveTarget = homePos;
                TryCast(BotSituation.Guarding, _nearestRival);

                if (_state != _prevState)
                    _log?.Debug(Source, "→ Guard (rival closing on own base).");
                return;
            }

            // Priority 5: SEARCH — Predator-only. Cannot forage at all (Peck excludes it,
            // GDD 2 / class-essence-and-signatures.md 2: "the only class that cannot forage
            // ... it eats what others carried"), so having no prey in HuntRadius must NOT
            // fall through to the forager's pile-collection priority below. Maestro,
            // 2026-08-24: "between kills, the assassin will search for new targets and chase
            // other chickens to steal cargo from" — walk toward whatever rival is nearest
            // (tracked unconditionally every tick, no radius cap, see the perception loop)
            // until it enters HuntRadius, at which point Priority 3 above takes over and
            // TargetPriority's leaderFocus-weighted scoring resumes doing the actual
            // target-choosing. This priority only ever has to close distance, never choose
            // between rivals, which is why it can use the cheap unscored _nearestRival
            // instead of re-running TargetPriority past preyRadius.
            if (_profile.Strategy == BotStrategy.Predator)
            {
                if (_nearestRival != null)
                {
                    _state      = BotState.Search;
                    _moveTarget = _nearestRival.transform.position;

                    if (_state != _prevState)
                        _log?.Debug(Source, $"→ Search (nearest rival {_nearestRivalDist:0.0}m, " +
                            $"outside hunt band {_profile.HuntRadius:0.0}m).");
                    return;
                }

                // Nobody left alive to hunt (last-chicken-standing edge case). Nothing a
                // Predator can legally do — it must not wander to a pile it cannot use.
                _state = BotState.Idle;
                if (_state != _prevState)
                    _log?.Debug(Source, "→ Idle (Predator, no rival anywhere to search for).");
                return;
            }

            // Priority 6: COLLECT — the cheapest pile by round-trip cost. Ground pickups are
            // gone: nothing drops food on the floor any more, so a pile is the only target.
            var pile = SelectPile(homePos, out float pileDist);
            if (pile != null)
            {
                _state = BotState.CollectFood;
                // Steer to the pile's rim, not its centre: the centre of a stocked pile is
                // inside a solid blocker, and on a 7x4 island that is 3.5 m of wall the bot
                // would grind against forever without ever collecting.
                _pileGoal   = pile;
                _moveTarget = pile.SurfaceApproachPoint(_controller.transform.position, _pileStandoff);

                // A rival on the pile we want is a Contest — measured to the pile's SURFACE,
                // because a centre-distance threshold is never satisfied once the pile is
                // wider than the threshold itself.
                bool contested = _nearestRival != null &&
                                 pile.DistanceToSurface(_nearestRival.transform.position) < _arrivalRadius;

                if (contested)
                {
                    TryCast(BotSituation.Contesting, _nearestRival);
                }
                else if (!TryGuardOverlay() && pileDist >= MinSprintWorthDistance)
                {
                    // Only sprint when there is actually road left to cover. A Runner that
                    // fires Speed Burst two metres from the pile it is already standing next
                    // to spends the cooldown for nothing and has none left for the haul home.
                    TryCast(BotSituation.Transiting, null);
                }

                if (_state != _prevState)
                    _log?.Debug(Source, $"→ CollectFood (pile '{pile.name}', surface dist={pileDist:0.0}).");
                return;
            }

            // Priority 7: IDLE.
            _state = BotState.Idle;
            if (_state != _prevState)
                _log?.Debug(Source, "→ Idle (no piles found).");
        }

        private void EnterReturnToBase(Vector3 homePos, string why)
        {
            _state      = BotState.ReturnToBase;
            _moveTarget = homePos;
            if (_state != _prevState)
                _log?.Debug(Source, $"→ ReturnToBase ({why}).");
        }

        /// <summary>
        /// Commits to the chosen prey when it is worth attacking, returning true if the FSM
        /// settled into Hunt or Stalk this tick.
        /// </summary>
        /// <remarks>
        /// <b>Stalk is the Predator's answer to a problem every other bot still has:</b>
        /// what to do while standing next to a rival with the steal on cooldown. Walking
        /// onto them and waiting is how a bot gets Cluck Shocked for free. A stalking
        /// Assassin instead holds at a fraction of its steal's reach — inside the pounce
        /// window, outside most retaliation — until the ability comes back.
        /// </remarks>
        private bool TryEngage()
        {
            bool loaded  = _preyCargo >= _profile.HuntCargoThreshold;
            bool hunting = _state == BotState.Hunt || _state == BotState.Stalk;
            float exit   = hunting ? _stateExitRadiusFactor : 1f;

            bool inHuntBand   = loaded && _preyDist <= _profile.HuntRadius * exit;
            bool inEngageBand = !loaded && _profile.EngageRadius > 0f &&
                                _preyDist <= _profile.EngageRadius * exit;

            if (!inHuntBand && !inEngageBand) return false;

            // Cast first: the answer decides whether this is a Hunt (close and commit) or a
            // Stalk (hold the pounce distance while the tool recharges).
            bool fired    = TryCast(BotSituation.Engaging, _preyTarget);
            float standoff = _profile.Strategy == BotStrategy.Predator && !fired
                ? StalkStandoff()
                : 0f;

            _state = standoff > 0f ? BotState.Stalk : BotState.Hunt;

            var preyPos = _preyTarget.transform.position;
            if (standoff > 0f && _preyDist > 0.01f)
            {
                var away = (_controller.transform.position - preyPos);
                away.y = 0f;
                _moveTarget = preyPos + away.normalized * standoff;
            }
            else
            {
                _moveTarget = preyPos;
            }

            if (_state != _prevState)
                _log?.Debug(Source, $"→ {_state} prey dist={_preyDist:0.0}, cargo={_preyCargo:P0}.");
            return true;
        }

        /// <summary>
        /// Distance a stalking Predator holds: a fraction of the reach of the longest steal
        /// tool it owns, so the pounce is one step away. 0 when it owns no steal at all, in
        /// which case there is nothing to wait for and it should just commit.
        /// </summary>
        private float StalkStandoff()
        {
            if (_abilities == null) return 0f;
            float best = 0f;
            for (int i = 0; i < _abilities.EquippedSlotCount; i++)
            {
                var ability = _abilities.GetSlot(i);
                if (ability == null || ability.ResolveBotRole() != BotRole.Steal) continue;
                float reach = BotTactics.EffectiveReach(ability);
                if (!float.IsPositiveInfinity(reach) && reach > best) best = reach;
            }
            return best * _stalkStandoffFraction;
        }

        /// <summary>
        /// A Guarding strategy punishes anything that comes near whatever it is doing,
        /// without abandoning it. Returns true if a cast fired.
        /// </summary>
        /// <remarks>
        /// This overlay is why the Fatty has a control kit at all. Its profile deliberately
        /// never chases (<c>HuntRadius = 0</c>), and with only the old Hunt path to reach
        /// them, Belly Flop, Ground Quake and Roll Push were unreachable for the entire
        /// class — authored, registered, balance-tuned and impossible for a bot to fire.
        /// </remarks>
        private bool TryGuardOverlay()
        {
            if (_profile.GuardRadius <= 0f) return false;
            if (_nearestRival == null || _nearestRivalDist > _profile.GuardRadius) return false;
            return TryCast(BotSituation.Guarding, _nearestRival);
        }

        // ---- Casting --------------------------------------------------------

        /// <summary>
        /// Picks and fires the best ability for <paramref name="situation"/> against
        /// <paramref name="target"/>. Returns true only when something actually fired.
        /// </summary>
        /// <remarks>
        /// The gates, in the order they are applied and why each one exists:
        /// <list type="number">
        ///   <item><b>In the plan.</b> <see cref="BotTactics.CastPlan"/> decides which roles
        ///   are appropriate here; anything else is skipped rather than fired "because it
        ///   was ready".</item>
        ///   <item><b>Usable.</b> <see cref="AbilityBaseSO.IsUsable"/> is the same gate the
        ///   player's button grey-out uses, so a bot cannot burn a cooldown on a cast the
        ///   HUD would have refused.</item>
        ///   <item><b>In reach.</b> Per-ability, from the declared aim shape, at
        ///   <see cref="BotTactics.CommitReachFraction"/> so the target does not stroll out
        ///   of the shape during the wind-up.</item>
        ///   <item><b>Aimed.</b> Off-axis does not cancel the cast — it records a
        ///   <see cref="_faceIntent"/> so <see cref="Navigate"/> turns this tick and the
        ///   next think fires it pointed the right way.</item>
        ///   <item><b>Actually lands.</b> For an ability that declares it does nothing
        ///   without a target (<see cref="AbilityBaseSO.RequiresEnemyInRange"/>), the final
        ///   confirmation is <see cref="AbilityBaseSO.WouldAffect"/> — the identical
        ///   predicate the telegraph and <c>OnActivate</c> use, so the bot cannot disagree
        ///   with the game about whether a hit was possible.</item>
        /// </list>
        /// </remarks>
        private bool TryCast(BotSituation situation, ChickenController target)
        {
            if (_abilities == null) return false;

            var plan = BotTactics.CastPlan(_profile.Strategy, situation,
                                           targetIsLoaded: _preyCargo > 0f && target == _preyTarget);
            if (plan.Length == 0) return false;

            var  selfPos    = _controller.transform.position;
            var  forward    = _controller.transform.forward;
            bool haveTarget = target != null;
            var  toTarget   = haveTarget ? target.transform.position - selfPos : Vector3.zero;
            float targetDist = haveTarget ? PlanarDistance(selfPos, target.transform.position) : 0f;

            int   bestSlot  = AbilityController.InvalidSlot;
            float bestScore = float.MaxValue;

            for (int slot = 0; slot < _abilities.EquippedSlotCount; slot++)
            {
                var ability = _abilities.GetSlot(slot);
                if (ability == null || !_abilities.IsReady(slot)) continue;

                int priority = IndexInPlan(plan, ability.ResolveBotRole());
                if (priority < 0) continue;

                if (!ability.IsUsable(_controller)) continue;

                float reach = BotTactics.EffectiveReach(ability);
                if (!float.IsPositiveInfinity(reach))
                {
                    // A reach-having ability with nobody to point it at is a whiff by
                    // construction — unless it places a zone, which is worth dropping on the
                    // ground the bot is standing on (a trap behind a fleeing chicken is the
                    // whole point of the ability).
                    if (!haveTarget && !ability.PlacesZone) continue;
                    if (haveTarget && targetDist > reach * BotTactics.CommitReachFraction) continue;
                }

                float score = BotTactics.ScoreCastCandidate(priority, _abilities.ResolvedCooldownFor(slot));
                if (score < bestScore) { bestScore = score; bestSlot = slot; }
            }

            if (bestSlot == AbilityController.InvalidSlot) return false;

            var chosen = _abilities.GetSlot(bestSlot);

            if (haveTarget && chosen.IsDirectionalAim &&
                !BotTactics.IsAimedWellEnough(chosen, forward, toTarget))
            {
                // In reach, wrong way round. Turn now, fire next think — cancelling here is
                // what used to make a bot walk in circles next to a rival it never hit.
                _faceIntent = target.transform.position;
                return false;
            }

            if (chosen.RequiresEnemyInRange && (!haveTarget || !chosen.WouldAffect(_controller, target)))
            {
                if (haveTarget) _faceIntent = target.transform.position;
                return false;
            }

            if (!_abilities.BotTryActivate(bestSlot)) return false;

            _log?.Debug(Source, $"Cast slot {bestSlot} ('{chosen.name}', role={chosen.ResolveBotRole()}) " +
                $"for {situation}.");
            return true;
        }

        private static int IndexInPlan(BotRole[] plan, BotRole role)
        {
            for (int i = 0; i < plan.Length; i++)
                if (plan[i] == role) return i;
            return -1;
        }

        /// <summary>
        /// Fires the bot's Forage-role ability (Peck) when it is parked at a pile it can
        /// actually drain. Routed through <see cref="AbilityController.BotTryActivate"/>
        /// like every other bot cast, so cooldown, stun, and the one-ability-at-a-time rule
        /// are enforced identically for bots and players.
        /// </summary>
        /// <remarks>
        /// Deliberately silent when it cannot fire. The common cases — cooldown still
        /// running, cargo full, pile drained by someone else — are all ordinary and happen
        /// many times a second; logging them would bury the console.
        ///
        /// The Assassin has no Peck (it is not in that ability's AllowedClasses), so
        /// <see cref="AbilityController.TryGetReadySlotForRole"/> simply finds nothing and
        /// this is a no-op for that class. That is correct rather than accidental: an
        /// Assassin bot should be hunting, not standing at a pile.
        ///
        /// Forage is a role of exactly one ability again. Quick Drop wore it until
        /// 2026-08-23, which meant a Speedy bot standing at a pile would fire a 12-second
        /// deposit-rate buff into the dirt and — when Quick Drop held the lower slot index —
        /// never peck at all.
        /// </remarks>
        private void TryPeckAtPile()
        {
            if (_abilities == null) return;
            if (_state != BotState.CollectFood) return;
            if (_pileGoal == null || _pileGoal.Object == null || !_pileGoal.Object.IsValid) return;

            if (_abilities.TryGetReadySlotForRole(BotRole.Forage, out int slot))
                _abilities.BotTryActivate(slot);
        }

        // ---- Navigation ----------------------------------------------------

        private void Navigate()
        {
            var selfPos  = _controller.transform.position;

            // A pending cast that only needs a turn outranks walking. Spending one think
            // interval turning (720 deg/s covers any angle in well under that) converts a
            // guaranteed whiff into a hit.
            if (_faceIntent.HasValue)
            {
                _controller.BotFace(_faceIntent.Value - selfPos, Runner.DeltaTime);
                return;
            }

            var toTarget = _moveTarget - selfPos;
            toTarget.y = 0f;

            if (HasArrived(selfPos, toTarget))
            {
                // Standing still on the goal. If that goal is a pile, this is where the bot
                // has to actually PECK — pile food used to drain automatically just for
                // being in range, so before v0.7 arriving was the whole job. Without this
                // the bot walks to a pile and stands there for the rest of the match with
                // an empty beak, and nothing logs an error because nothing is wrong.
                TryPeckAtPile();
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

        // ---- Perception (runs inside the throttled Think) -------------------

        /// <summary>
        /// One pass over the rival set that answers both questions the FSM asks: who is
        /// closest (danger), and who is worth attacking (value). Also refreshes the match
        /// phase and whether this bot is currently leading.
        /// </summary>
        /// <remarks>
        /// <b>Decoys are included on purpose.</b> A bot that could see through a
        /// Doppelganger made the ability inert in a solo match, which is most of the testing
        /// this game gets. Bots are now baitable — the whole point of the decoy. The decoy
        /// prefab ships with <c>ChickenCargo</c> stripped, so its cargo fraction is 0 and it
        /// scores poorly as prey while still reading as a threat, which is the right split.
        /// </remarks>
        private void Perceive()
        {
            var gm      = GameManager.Instance;
            var selfPos = _controller.transform.position;
            var all     = ChickenController.ActiveControllers;

            _nearestRival     = null;
            _nearestRivalDist = float.MaxValue;
            _preyTarget       = null;
            _preyDist         = float.MaxValue;
            _preyCargo        = 0f;

            float winTarget = gm != null ? Mathf.Max(1, gm.FoodTargetToWin) : 40f;
            float bestPriority = 0f;

            // The radius prey is scored against: the hunt band for a chaser, else the engage
            // band. Guarding-only strategies score nobody as prey and rely on the overlay.
            float preyRadius = Mathf.Max(_profile.HuntRadius, _profile.EngageRadius);

            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _controller) continue;
                if (c.Combat != null && c.Combat.IsRemoved) continue;

                float dist = PlanarDistance(selfPos, c.transform.position);

                if (dist < _nearestRivalDist)
                {
                    _nearestRivalDist = dist;
                    _nearestRival     = c;
                }

                if (preyRadius <= 0f) continue;

                var   rivalCargo = c.Cargo;
                float frac       = rivalCargo != null ? rivalCargo.Fraction : 0f;
                float banked     = BankedShareFor(c, winTarget);

                float priority = BotTactics.TargetPriority(dist, frac, banked, preyRadius, _profile.LeaderFocus);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    _preyTarget  = c;
                    _preyDist    = dist;
                    _preyCargo   = frac;
                }
            }

            if (_nearestRival == null) _nearestRivalDist = float.MaxValue;

            _phase   = gm != null ? BotTactics.ResolvePhase(gm.TimeRemaining, gm.MatchDurationSeconds)
                                  : MatchPhase.Mid;
            _leading = IsLeading();
        }

        /// <summary>
        /// A chicken's banked score as a fraction of the win target, clamped to [0,1].
        /// Resolved through <see cref="PlayerBase.CornerIndex"/> because a bot's base is
        /// identified by corner, not by <c>PlayerRef</c> — every bot shares
        /// <c>PlayerRef.None</c>.
        /// </summary>
        private static float BankedShareFor(ChickenController chicken, float winTarget)
        {
            if (chicken == null || chicken.HomeCornerIndex < 0) return 0f;
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.CornerIndex != chicken.HomeCornerIndex) continue;
                return Mathf.Clamp01(b.FoodTotal / winTarget);
            }
            return 0f;
        }

        /// <summary>Is this bot's base currently at or above every other claimed base?</summary>
        private bool IsLeading()
        {
            if (_homeBase == null) return false;
            float mine  = _homeBase.FoodTotal;
            var   bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b == _homeBase || !b.IsClaimed) continue;
                if (b.FoodTotal > mine) return false;
            }
            return true;
        }

        /// <summary>
        /// The cheapest pile to work, by <see cref="BotTactics.PileCost"/> — travel out plus
        /// the class's weighting of the walk home, discounted by how much is actually in it.
        /// </summary>
        /// <remarks>
        /// Ranked by distance to the pile's SURFACE so the choice stays honest across wildly
        /// different pile sizes: by centre distance a bot standing on the 7x4 island's rim
        /// would rate a small pile 3 m away as closer.
        ///
        /// Filtered on <c>HasCollectableFood</c>, not <c>IsEmpty</c>: the permanent centre
        /// pile (ADR 0003 Decision 2b) is never empty, so a bot would otherwise park on its
        /// floor and collect nothing forever.
        /// </remarks>
        private FoodPile SelectPile(Vector3 homePos, out float bestSurfaceDist)
        {
            var      piles    = FoodPile.ActivePiles;
            var      selfPos  = _controller.transform.position;
            FoodPile best     = null;
            float    bestCost = float.MaxValue;
            bestSurfaceDist   = float.MaxValue;

            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (!p.HasCollectableFood) continue;

                float toPile = p.DistanceToSurface(selfPos);
                float toHome = PlanarDistance(p.transform.position, homePos);
                float cost   = BotTactics.PileCost(toPile, toHome, p.Available, _profile.RoundTripBias);

                if (cost < bestCost)
                {
                    bestCost        = cost;
                    best            = p;
                    bestSurfaceDist = toPile;
                }
            }
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

        private float HomeDepositRadius() =>
            _homeBase != null ? _homeBase.DepositRadius : _arrivalRadius;

        private float MatchTimeRemaining()
        {
            var gm = GameManager.Instance;
            return gm != null ? gm.TimeRemaining : float.MaxValue;
        }

        /// <summary>
        /// Current effective ground speed, used only to estimate the walk home. Includes the
        /// live slow multiplier deliberately: a bot bogged down in a Feather Trap needs to
        /// start for home earlier than a free one, and the whole point of the estimate is to
        /// be right about arrival time rather than about the stat sheet.
        /// </summary>
        private float EstimateMoveSpeed()
        {
            var stats = _controller != null ? _controller.Stats : null;
            if (stats == null) return 0f;
            return stats.MoveSpeed * Mathf.Max(0.15f, _controller.SlowMultiplier);
        }

        /// <summary>
        /// Seconds the deposit itself will take. Read from <c>MatchConfig</c> rather than
        /// from <c>ChickenCargo</c>'s own resolved rate, which is private — an overestimate
        /// is the safe direction here, and this ignores the passive and Quick Drop
        /// multipliers that can only make the real deposit faster.
        /// </summary>
        private float EstimateDepositSeconds(float cargo)
        {
            var gm     = GameManager.Instance;
            var config = gm != null ? gm.Config : null;
            float rate = config != null ? Mathf.Max(0.5f, config.DepositRatePerSecond) : 9f;
            return cargo / rate;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
