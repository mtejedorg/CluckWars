using CluckWars.Abilities;
using CluckWars.Injection;
using CluckWars.Logging;
using Fusion;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Bully's hit-steal in the game (design 2026-11-25, 16.2, BL-226): a Stun, Shove or instant Slow that really affects a rival asks the rival for
    /// the passive's food, into this chicken's ordinary cargo, once per victim per lock. The food moves only through <see cref="ChickenCargo.StealFrom(ChickenController, float)"/>
    /// (authority-confirmed: the victim drains, this chicken credits what the victim reports), so a wrong "affected" prediction can move no food.
    /// Sibling of <see cref="ThiefLoot"/> and built the same way. Every chicken carries one; only a chicken whose passive holds hit-steal facts
    /// (<see cref="Facts"/>) does anything, the others return at the first line of <see cref="OnHit"/>.
    /// </summary>
    /// <remarks>
    /// The lock is authority-local (<see cref="HitStealLedger{TKey}"/>): locked at the ask, given back when the settle credits 0. The numbers are the
    /// match simulator's: <see cref="HitStealRules.Take"/> and <see cref="HitStealRules.Locked"/>, the "affected" test is <see cref="HitAffectRules"/> run by
    /// the caster over the victim's replicated state (<see cref="ChickenController.PredictStunLands"/>, <see cref="ChickenController.PredictShoveMoves"/>).
    /// </remarks>
    [UnityEngine.RequireComponent(typeof(ChickenController))]
    [UnityEngine.RequireComponent(typeof(NetworkObject))]
    public sealed class ChickenHitSteal : NetworkBehaviour
    {
        private const string Source = "HitSteal";

        /// <summary>Bumped on this chicken's authority when a hit-steal is credited (ARCH-EVT-02): the two fields below are written first. Observed through
        /// <see cref="ChickenLocalEvents.HitStealTaken"/>.</summary>
        [Networked] public byte HitStealEventId { get; private set; }
        /// <summary>Who the last credited hit-steal robbed.</summary>
        [Networked] public NetworkBehaviourId LastHitVictim { get; private set; }
        /// <summary>The food the last credited hit-steal took into the cargo.</summary>
        [Networked] public float LastHitTake { get; private set; }

        private ChickenController _controller;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ChickenCombat _combat;
        private ILogService _log;

        private readonly HitStealLedger<NetworkBehaviourId> _ledger = new HitStealLedger<NetworkBehaviourId>();

        // True only while this chicken's own hit asks the cargo for food: the one request the cargo opens inside that call is a hit-steal's.
        private bool _asking;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            SelfInject.Ensure(this, _log != null);
            _controller = GetComponent<ChickenController>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
            _combat = GetComponent<ChickenCombat>();
            _ledger.Clear();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            // A lock never outlives the round, nor the chicken that holds it.
            var gm = GameManager.Instance;
            bool running = gm != null && gm.IsMatchRunning && (_combat == null || !_combat.IsRemoved);
            if (!running && _ledger.Count > 0) _ledger.Clear();
        }

        /// <summary>The hit-steal numbers of this chicken's passive; <see cref="HitStealFacts.None"/> for every kit but the Bully's.</summary>
        public HitStealFacts Facts
        {
            get
            {
                var passive = _abilities != null ? _abilities.Passive : null;
                return passive != null ? passive.HitSteal : HitStealFacts.None;
            }
        }

        /// <summary>Seconds <paramref name="victim"/> stays locked against this chicken's hits; 0 when none or on a proxy (the lock is authority-local).</summary>
        public float LockLeftOn(ChickenController victim)
        {
            if (victim == null || Runner == null || !HasStateAuthority) return 0f;
            return _ledger.LockLeft(victim.Id, (float)Runner.SimulationTime);
        }

        /// <summary>
        /// One of this chicken's hitters (Stun, Shove or an instant Slow) just went at <paramref name="victim"/>; <paramref name="affected"/> is the caster's
        /// prediction that it affected him. Authority only (it runs inside the cast). A passive without hit-steal facts, a job the passive does not price, a
        /// hit that did not affect, a wall, a shell, a locked victim or an empty one take nothing and start no lock, exactly as in the simulator.
        /// </summary>
        public void OnHit(ChickenController victim, AbilityJob job, bool affected)
        {
            if (!HasStateAuthority || !affected || victim == null || victim == _controller || _cargo == null || Runner == null) return;
            var facts = Facts;
            float amount = facts.AmountFor(job);
            if (amount <= 0f) return;                                                  // off, or a job the passive does not price: a legal no-op

            var victimCargo = victim.Cargo;
            if (victimCargo == null || !StealRules.CanStealFrom(victim)) return;       // a shell: the egg already refuses the hit itself
            if (PlayerBase.WallBetween(transform.position, victim.transform.position)) return;   // a base wall refuses the hit itself

            float now = (float)Runner.SimulationTime;
            bool locked = _ledger.Locked(victim.Id, now);
            float take = HitStealRules.Take(amount, _cargo.FreeSpace, victimCargo.Stealable, locked);
            if (take <= 0f) return;                                                    // locked, full or empty: no take, and a lock is not renewed

            _asking = true;
            try { _cargo.StealFrom(victim, take); }                                   // the cargo opens the request and tells OnStealOpened before it sends it
            finally { _asking = false; }
            _log?.Debug(Source, $"{name}: {job} on {victim.name} asked {take:0.##} (lock {facts.LockSeconds:0.#} s).");
        }

        /// <summary>
        /// The cargo opened steal request <paramref name="requestId"/> on <paramref name="victim"/> and has not sent it yet (<c>ChickenCargo.RequestSteal</c>: a
        /// victim this peer also owns answers inside the send). When it is this chicken's own hit asking, it locks the victim. Authority only.
        /// </summary>
        internal void OnStealOpened(NetworkBehaviourId victim, ushort requestId)
        {
            if (!_asking || !HasStateAuthority || Runner == null) return;
            _ledger.Ask(victim, requestId, (float)Runner.SimulationTime, Facts.LockSeconds);   // locked at the ask; a settle of 0 gives it back
        }

        /// <summary>
        /// The victim's authority answered one of this chicken's steals (<see cref="ChickenCargo.RPC_SettleSteal"/>) with <paramref name="credited"/>. When the
        /// request was a hit-steal's, a 0 gives the lock back and a positive answer writes the event fields and then bumps the id. Authority only.
        /// </summary>
        internal void OnStealSettled(NetworkBehaviourId victim, ushort requestId, float credited)
        {
            if (!HasStateAuthority) return;
            if (!_ledger.Settled(victim, requestId, credited) || !(credited > 0f)) return;
            LastHitVictim = victim;
            LastHitTake = credited;
            HitStealEventId++;   // wraps at 255 by design (ARCH-EVT-02); the fields above are written first
            _log?.Info(Source, $"{name}: hit-steal credited +{credited:0.##}.");
        }

        /// <summary>Round reset (<c>ChickenController.RPC_ResetControlStates</c>): every lock is dropped.</summary>
        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            _ledger.Clear();
        }
    }
}
