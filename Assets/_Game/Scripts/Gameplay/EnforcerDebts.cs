using CluckWars.Abilities;
using CluckWars.Injection;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Enforcer's per-rival debts in the game (design 2026-11-25, 16.2, BL-226): each Stun or Shove that affected a rival is a debt on it, a hit on a
    /// fresh rival (mid Peck-cast, or just after a Peck that landed food) stacks one more and spends that fresh state, and every debt inflates the
    /// Enforcer's Steal on that rival by <see cref="DebtFacts.Steal"/> food. A fresh hit moves no food (Maestro, 2026-11-25): all the food an Enforcer
    /// takes from a rival comes through its Steal, through <see cref="ChickenCargo.StealFrom(ChickenController, float, out ushort)"/>, authority-confirmed.
    /// Sibling of <see cref="ChickenHitSteal"/>; every chicken carries one and only a passive with debt facts (<see cref="Facts"/>) runs it.
    /// </summary>
    /// <remarks>
    /// <b>State.</b> Counts and decay timers live on the Enforcer's authority in three small replicated arrays, one slot per rival (<see cref="Slots"/> covers
    /// <c>MatchConfigSO.MaxPlayers - 1</c> rivals); the Enforcer's HUD and each victim ("you owe") read them on every peer. Timers are
    /// <see cref="EffectWindow"/> windows; the pure moves are <see cref="DebtBook"/>, over the simulator's <see cref="DebtRules"/>.
    /// <b>Cash.</b> The Steal records the count it used at the ask (<see cref="OnStealAsked"/>); a settle crediting above 0 subtracts that number
    /// (<see cref="OnStealSettled"/>), a settle of 0 leaves the count. A Stun landing inside the round trip therefore keeps its debt.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EnforcerDebts : NetworkBehaviour
    {
        private const string Source = "EnforcerDebts";

        /// <summary>Slots of the replicated arrays: one per rival (a match holds at most <c>MatchConfigSO.MaxPlayers</c> chickens).</summary>
        public const int Slots = 4;

        [Networked, Capacity(Slots)] private NetworkArray<NetworkBehaviourId> DebtVictims => default;
        [Networked, Capacity(Slots)] private NetworkArray<byte> DebtCounts => default;
        [Networked, Capacity(Slots)] private NetworkArray<TickTimer> DebtTimers => default;

        /// <summary>Bumped on the Enforcer's authority when a hit on a fresh rival stacked its extra debt (ARCH-EVT-02); <see cref="LastDebtVictim"/> is written first.</summary>
        [Networked] public byte FreshHitEventId { get; private set; }
        /// <summary>Bumped when a debt was gained on <see cref="LastDebtVictim"/> (the count went up).</summary>
        [Networked] public byte DebtEventId { get; private set; }
        /// <summary>Bumped when a Steal cashed debts on <see cref="LastDebtVictim"/>; <see cref="LastCashBonus"/> is written first.</summary>
        [Networked] public byte DebtCashEventId { get; private set; }
        /// <summary>The rival the last fresh hit, gain or cash was about.</summary>
        [Networked] public NetworkBehaviourId LastDebtVictim { get; private set; }
        /// <summary>The food the last cash added on top of the Steal's own amount (what fitted).</summary>
        [Networked] public float LastCashBonus { get; private set; }

        private ChickenController _controller;
        private AbilityController _abilities;
        private ChickenCombat _combat;
        private ILogService _log;

        // Authority-local: Steals asked and not yet answered, by victim and request id, with the debts each used.
        private struct Pending
        {
            public NetworkBehaviourId Victim;
            public ushort RequestId;
            public int Consumed;
            public float Bonus;
            public bool Live;
        }
        private readonly Pending[] _pending = new Pending[8];
        private int _nextPending;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            SelfInject.Ensure(this, _log != null);
            _controller = GetComponent<ChickenController>();
            _abilities = GetComponent<AbilityController>();
            _combat = GetComponent<ChickenCombat>();
            System.Array.Clear(_pending, 0, _pending.Length);
            _stagedConsumed = 0;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var gm = GameManager.Instance;
            bool running = Facts.IsEnforcer && gm != null && gm.IsMatchRunning && !_controller.IsDecoy && (_combat == null || !_combat.IsRemoved);
            if (!running)
            {
                ClearAll();
                return;
            }

            float now = (float)Runner.SimulationTime;
            for (int i = 0; i < Slots; i++)
            {
                if (DebtCounts[i] <= 0) continue;
                var victimId = DebtVictims[i];
                // A rival that left or was executed owes nothing any more.
                if (!Runner.TryFindBehaviour(victimId, out ChickenController victim) || victim == null || victim.Object == null || !victim.Object.IsValid ||
                    (victim.Combat != null && victim.Combat.IsRemoved))
                {
                    WriteSlot(i, default, default, false, 0f);
                    continue;
                }
                var decayed = DebtBook.Decay(ReadSlot(i, now), now, out int lost);
                if (lost <= 0) continue;
                WriteSlot(i, victimId, decayed, false, 0f);
                _log?.Debug(Source, $"{name}: {lost} debt(s) on {victim.name} decayed.");
            }
        }

        /// <summary>The debt numbers of this chicken's passive; <see cref="DebtFacts.None"/> for every kit but the Enforcer's.</summary>
        public DebtFacts Facts
        {
            get
            {
                var passive = _abilities != null ? _abilities.Passive : null;
                return passive != null ? passive.Debt : DebtFacts.None;
            }
        }

        /// <summary>Debts this Enforcer holds on <paramref name="victim"/> right now (0 once the decay timer ran out, on every peer).</summary>
        public int CountOn(ChickenController victim)
        {
            if (victim == null || Runner == null) return 0;
            int i = SlotOf(victim.Id);
            return i >= 0 && EffectWindow.IsOpen(DebtTimers[i], Runner) ? DebtCounts[i] : 0;
        }

        /// <summary>Seconds until the debts on <paramref name="victim"/> decay; 0 with none.</summary>
        public float DecayLeftOn(ChickenController victim)
        {
            if (victim == null || Runner == null) return 0f;
            int i = SlotOf(victim.Id);
            return i >= 0 ? EffectWindow.Remaining(DebtTimers[i], Runner) : 0f;
        }

        /// <summary>
        /// What a Steal that <paramref name="resolvedSteal"/> (the ability's amount after this chicken's own passive) takes from <paramref name="victim"/>
        /// now: the nominal amount plus the debts' food (<see cref="DebtRules.StealAmount"/>; the bonus is not multiplied by the passive's factor).
        /// </summary>
        public float StealAmountOn(ChickenController victim, float resolvedSteal) =>
            DebtRules.StealAmount(resolvedSteal, 1f, Facts.Steal, CountOn(victim));

        /// <summary>
        /// One of this chicken's hitters (<paramref name="job"/> Stun, Shove or Steal) just went at <paramref name="victim"/>; <paramref name="affected"/> is the
        /// caster's prediction that it reached him. Authority only. A hit on a fresh rival (<see cref="AbilityController.IsFreshFor"/>) stacks one extra
        /// debt and spends that state; a Stun or Shove that affected stacks one more; a Steal's own hit adds none beyond the fresh stack.
        /// </summary>
        public void OnHit(ChickenController victim, AbilityJob job, bool affected)
        {
            if (!HasStateAuthority || !affected || victim == null || victim == _controller || Runner == null) return;
            var facts = Facts;
            if (!facts.IsEnforcer) return;
            if (job != AbilityJob.Stun && job != AbilityJob.Shove && job != AbilityJob.Steal) return;

            var rival = victim.Abilities;
            if (rival != null && rival.IsFreshFor(_abilities.Passive.Bite.FreshSeconds))
            {
                Gain(victim, facts);                                       // the extra stack of a hit on a fresh rival
                rival.RPC_MarkBitten(rival.FreshStateId, _controller.Id);   // the state is spent, whoever hits next
                LastDebtVictim = victim.Id;
                FreshHitEventId++;
            }
            if (job == AbilityJob.Stun || job == AbilityJob.Shove) Gain(victim, facts);
        }

        // Staged by the Steal's ability wrapper around its own ask (AbilityBaseSO.StealFrom with debts): the one request the cargo opens inside that call is a
        // Steal that used this many debts. Staged, not returned, because a victim this peer also owns answers inside the send (OnStealOpened runs before it).
        private int _stagedConsumed;

        /// <summary>A Steal of this chicken is about to ask a victim for food that uses <paramref name="consumed"/> debts. Authority only; pair with <see cref="EndAsk"/>.</summary>
        internal void BeginAsk(int consumed) => _stagedConsumed = HasStateAuthority ? consumed : 0;

        /// <summary>The Steal's ask is over: nothing else the cargo opens belongs to it.</summary>
        internal void EndAsk() => _stagedConsumed = 0;

        /// <summary>
        /// The cargo opened steal request <paramref name="requestId"/> on <paramref name="victim"/> and has not sent it yet. When a Steal staged its debts
        /// (<see cref="BeginAsk"/>), the request is recorded with the count it used, so the victim's answer can cash it. Authority only.
        /// </summary>
        internal void OnStealOpened(NetworkBehaviourId victim, ushort requestId)
        {
            if (_stagedConsumed <= 0 || !HasStateAuthority) return;
            _pending[_nextPending] = new Pending { Victim = victim, RequestId = requestId, Consumed = _stagedConsumed, Bonus = _stagedConsumed * Facts.Steal, Live = true };
            _nextPending = (_nextPending + 1) % _pending.Length;
        }

        /// <summary>
        /// The victim's authority answered a steal of this chicken with <paramref name="credited"/>. When it was a Steal that used debts, a positive answer
        /// cashes them (the count falls by what the Steal used) and raises the cash event; an answer of 0 leaves the count. Authority only.
        /// </summary>
        internal void OnStealSettled(NetworkBehaviourId victimId, ushort requestId, float credited)
        {
            if (!HasStateAuthority || Runner == null) return;
            for (int p = 0; p < _pending.Length; p++)
            {
                if (!_pending[p].Live || _pending[p].RequestId != requestId || !(_pending[p].Victim == victimId)) continue;
                var pending = _pending[p];
                _pending[p].Live = false;
                if (!(credited > 0f)) return;

                int i = SlotOf(victimId);
                if (i >= 0)
                {
                    float now = (float)Runner.SimulationTime;
                    WriteSlot(i, victimId, DebtBook.Cash(ReadSlot(i, now), pending.Consumed, credited), false, 0f);
                }
                LastDebtVictim = victimId;
                LastCashBonus = Mathf.Min(pending.Bonus, credited);
                DebtCashEventId++;
                _log?.Info(Source, $"{name}: cashed {pending.Consumed} debt(s) for +{credited:0.##} (bonus {LastCashBonus:0.##}).");
                return;
            }
        }

        /// <summary>Round reset (<c>ChickenController.RPC_ResetControlStates</c>): every count goes to 0 and no Steal is waiting for its answer.</summary>
        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            ClearAll();
            System.Array.Clear(_pending, 0, _pending.Length);
        }

        // ---- the arrays ----

        private int SlotOf(NetworkBehaviourId victim)
        {
            for (int i = 0; i < Slots; i++)
                if (DebtCounts[i] > 0 && DebtVictims[i] == victim) return i;
            return -1;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < Slots; i++)
                if (DebtCounts[i] <= 0) return i;
            return -1;
        }

        private DebtBook.DebtSlot ReadSlot(int i, float now)
        {
            var timer = DebtTimers[i];
            return new DebtBook.DebtSlot
            {
                Count = DebtCounts[i],
                Until = EffectWindow.IsOpen(timer, Runner) ? now + EffectWindow.Remaining(timer, Runner) : float.NegativeInfinity,
            };
        }

        private void WriteSlot(int i, NetworkBehaviourId victim, DebtBook.DebtSlot slot, bool restartTimer, float seconds)
        {
            DebtCounts.Set(i, (byte)Mathf.Clamp(slot.Count, 0, byte.MaxValue));
            if (slot.Count <= 0)
            {
                DebtVictims.Set(i, default);
                DebtTimers.Set(i, TickTimer.None);
                return;
            }
            DebtVictims.Set(i, victim);
            if (restartTimer) DebtTimers.Set(i, TickTimer.CreateFromSeconds(Runner, seconds));
        }

        private void ClearAll()
        {
            for (int i = 0; i < Slots; i++)
                if (DebtCounts[i] > 0) WriteSlot(i, default, default, false, 0f);
        }

        private void Gain(ChickenController victim, in DebtFacts facts)
        {
            float now = (float)Runner.SimulationTime;
            int i = SlotOf(victim.Id);
            if (i < 0) i = FreeSlot();
            if (i < 0)
            {
                _log?.Warn(Source, $"{name}: no free debt slot for {victim.name} ({Slots} slots): a wiring fault (capacity below the rivals of a match).");
                return;
            }
            var before = DebtBook.Decay(ReadSlot(i, now), now, out _);
            var after = DebtBook.Gain(before, facts.Cap, now, facts.Seconds);
            WriteSlot(i, victim.Id, after, true, facts.Seconds);
            if (after.Count <= before.Count) return;
            LastDebtVictim = victim.Id;
            DebtEventId++;   // wraps at 255 by design (ARCH-EVT-02); the victim is written first
        }
    }
}
