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
    /// The Thief's loot mark (BL-217, design 2026-11-23): one rival carries the Thief's mark, seen later by the Thief and that rival only (slice B). A Steal that
    /// reaches the marked rival takes what it takes and adds <see cref="LootMarkFacts.Bounty"/> of new food, both into the Thief's ordinary cargo, held to its
    /// free room (<see cref="ChickenCargo.RequestMarkedSteal"/>, confirmed by the victim's authority). The mark is decided and advanced here, on the Thief's own
    /// state authority, by the pure rules the match simulator calls too (<see cref="LootPickRules"/>, <see cref="LootRules"/>); every peer reads the replicated
    /// result. Only a chicken whose passive carries loot facts (<see cref="Facts"/>) runs it; every other chicken's copy stays <see cref="LootState.Off"/>.
    /// Sibling of <see cref="AssassinExecute"/>, and built the same way.
    /// </summary>
    /// <remarks>
    /// Life cycle (design section 4): <c>Off</c> outside a running round; <c>Waiting</c> with no mark until Steal is within
    /// <see cref="LootMarkFacts.LeadSeconds"/> of ready; <c>Live</c> on a rival; <c>Overdue</c> once the mark is old with Steal ready (it stays);
    /// a mark old with Steal on cooldown moves, unpaid. The last marked rival is never picked next (unless the only rival). All local state is rebuilt
    /// from the replicated fields except the last-marked memory, which is authority-local like the Death Mark's.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ThiefLoot : NetworkBehaviour
    {
        private const string Source = "ThiefLoot";

        /// <summary>The ChickenController id of the marked rival; <see cref="NetworkBehaviourId.None"/> with no mark.</summary>
        [Networked] public NetworkBehaviourId MarkedTarget { get; private set; }
        /// <summary>Where the mark stands (<see cref="LootState"/>).</summary>
        [Networked] public LootState State { get; private set; }
        /// <summary>Runs <see cref="LootMarkFacts.Seconds"/> from the draw; the mark's age is the time it has run (<see cref="MarkAgeSeconds"/>).</summary>
        [Networked] public TickTimer AgeTimer { get; private set; }
        /// <summary>Bumped on the Thief's authority when a marked steal is paid (ARCH-EVT-02): the fields below are written first. Observed through
        /// <see cref="ChickenLocalEvents.LootTaken"/>.</summary>
        [Networked] public byte LootTakenEventId { get; private set; }
        /// <summary>Who the last paid marked steal robbed.</summary>
        [Networked] public NetworkBehaviourId LastLootVictim { get; private set; }
        /// <summary>The food the last paid marked steal took from its victim into the cargo.</summary>
        [Networked] public float LastLootTake { get; private set; }
        /// <summary>The bounty the last paid marked steal added to the cargo (what fitted).</summary>
        [Networked] public float LastLootBounty { get; private set; }

        private ChickenController _controller;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ILogService _log;

        // Authority-local: the last marked rival (never picked twice running) and the draw's scratch lists.
        private bool _hasLast;
        private NetworkBehaviourId _last;
        private readonly System.Collections.Generic.List<NetworkBehaviourId> _candidates = new System.Collections.Generic.List<NetworkBehaviourId>(4);
        private readonly System.Collections.Generic.List<float> _weights = new System.Collections.Generic.List<float>(4);

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            SelfInject.Ensure(this, _log != null);
            _controller = GetComponent<ChickenController>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
        }

        // Local, every peer, diagnostics only (slice A has no visual): what this peer last saw of the replicated mark, so a session's logs show the mark
        // arriving on every peer, not only on the Thief's. Slice B's presentation reads the state and the event through ChickenLocalEvents.
        private NetworkBehaviourId _seenTarget;
        private byte _seenTaken;
        private bool _seenInit;

        public override void Render()
        {
            if (_log == null) return;
            var target = MarkedTarget;
            if (!_seenInit) { _seenInit = true; _seenTarget = target; _seenTaken = LootTakenEventId; return; }
            if (LootTakenEventId != _seenTaken)
            {
                _seenTaken = LootTakenEventId;
                string victim = Runner != null && Runner.TryFindBehaviour(LastLootVictim, out ChickenController robbed) && robbed != null ? robbed.name : LastLootVictim.ToString();
                _log.Info(Source, $"{name}: loot taken seen from {victim}: +{LastLootTake:0.#} stolen, +{LastLootBounty:0.#} bounty ({(HasStateAuthority ? "authority" : "proxy")}).");
            }
            if (target == _seenTarget) return;
            _seenTarget = target;
            string who = target == NetworkBehaviourId.None ? "none"
                : Runner != null && Runner.TryFindBehaviour(target, out ChickenController rival) && rival != null ? rival.name : target.ToString();
            _log.Info(Source, $"{name}: mark seen on {who} (state {State}, {(HasStateAuthority ? "authority" : "proxy")}).");
        }

        /// <summary>The loot numbers of this chicken's passive; <see cref="LootMarkFacts.None"/> for every kit but the Thief's.</summary>
        public LootMarkFacts Facts
        {
            get
            {
                var passive = _abilities != null ? _abilities.Passive : null;
                return passive != null ? passive.LootMark : LootMarkFacts.None;
            }
        }

        /// <summary>A mark is up on a rival (<see cref="LootState.Live"/> or <see cref="LootState.Overdue"/>).</summary>
        public bool MarkLive => State == LootState.Live || State == LootState.Overdue;

        /// <summary>Is <paramref name="rival"/> carrying this Thief's live mark? Read-only over replicated state.</summary>
        public bool IsMarking(ChickenController rival) => rival != null && MarkLive && MarkedTarget == rival.Id;

        /// <summary>Seconds the live mark has run (0 with none), from the replicated timer.</summary>
        public float MarkAgeSeconds
        {
            get
            {
                if (!MarkLive || Runner == null) return 0f;
                float seconds = Facts.Seconds;
                return Mathf.Clamp(seconds - (AgeTimer.RemainingTime(Runner) ?? 0f), 0f, seconds);
            }
        }

        /// <summary>
        /// The Steal slot of this chicken's kit, or <see cref="AbilityController.InvalidSlot"/>: the mark follows its cooldown.
        /// </summary>
        private int StealSlot
        {
            get
            {
                if (_abilities != null)
                    for (int i = 0; i < AbilityController.SlotCount; i++)
                        if (_abilities.GetSlot(i).Is(AbilityJob.Steal)) return i;
                return AbilityController.InvalidSlot;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var facts = Facts;
            var gm = GameManager.Instance;
            bool running = facts.Active && gm != null && gm.IsMatchRunning && !_controller.IsDecoy;   // a Doppelganger decoy is never a Thief
            if (!running)
            {
                if (State != LootState.Off) ClearToOff();
                return;
            }
            int slot = StealSlot;
            if (slot == AbilityController.InvalidSlot) return;   // no Steal in the kit: nothing to follow (a wiring fault the loadout tests catch)

            float stealLeft = _abilities.CooldownRemaining(slot);
            if (State == LootState.Off) State = LootState.Waiting;

            if (State == LootState.Waiting)
            {
                if (LootRules.DrawDue(stealLeft, facts.LeadSeconds)) TryDraw(facts, gm);
                return;
            }

            if (!MarkLive) return;

            if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController target) || target.Object == null || !target.Object.IsValid
                || target.Combat == null || target.Combat.IsRemoved)
            {
                EndMark(MatchTelemetry.LootMoveGone);
                return;
            }

            // A marked steal is in flight: its answer decides the mark, so the clock does not.
            if (_cargo != null && _cargo.MarkedStealPending) return;

            switch (LootRules.Advance(State, MarkAgeSeconds, facts.Seconds, stealLeft <= 0f))
            {
                case LootStep.Move:
                    EndMark(MatchTelemetry.LootMoveTimeout);
                    break;
                case LootStep.GoOverdue:
                    State = LootState.Overdue;
                    _log?.Debug(Source, $"{name}: the mark on {target.name} is overdue (Steal is ready, so it stays).");
                    break;
            }
        }

        /// <summary>
        /// A Steal cast covers <paramref name="victim"/>: if it carries this Thief's live mark, the steal is a marked steal. Authority only (it runs inside the
        /// cast). <paramref name="amount"/> is the take the passive allows (<c>AbilityBaseSO.ResolveStealAmount</c>). False when the victim is not marked
        /// or nothing could be sent (a shell, a marked steal still open): the caller steals from it as from any rival, or not at all.
        /// </summary>
        public bool TakeMarked(ChickenController victim, float amount)
        {
            if (!HasStateAuthority || _cargo == null || !IsMarking(victim)) return false;
            var facts = Facts;
            return facts.Active && _cargo.RequestMarkedSteal(victim, amount, facts.Bounty);
        }

        /// <summary>
        /// The victim's authority confirmed the marked steal and the cargo credited it (<see cref="ChickenCargo.RPC_SettleMarkedSteal"/>): the mark is spent, the
        /// event fields are written and then the event id is bumped, the next mark is scheduled. Authority only.
        /// </summary>
        /// <param name="take">What the steal took from the victim into the cargo.</param>
        /// <param name="bountyGained">What of the bounty fitted.</param>
        /// <param name="bountyLost">What of the bounty did not fit (lost).</param>
        public void OnMarkedStealPaid(ChickenController victim, float take, float bountyGained, float bountyLost)
        {
            if (!HasStateAuthority) return;
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootTake(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller), MatchTelemetry.Seat(victim),
                take, bountyGained, bountyLost));
            if (!IsMarking(victim))
            {
                // The mark ended while the steal was in flight (the bird was executed, the round reset): the food is credited, there is nothing left to spend.
                _log?.Info(Source, $"{name}: a marked steal on {victim.name} was paid after its mark had ended (+{take:0.#} +{bountyGained:0.#}).");
                return;
            }
            LastLootVictim = victim.Id;
            LastLootTake = take;
            LastLootBounty = bountyGained;
            LootTakenEventId++;   // wraps at 255 by design (ARCH-EVT-02); the fields above are written first
            _last = MarkedTarget;
            _hasLast = true;
            ResetMark();
            State = LootState.Waiting;
            _log?.Info(Source, $"{name}: marked steal on {victim.name}: +{take:0.#} stolen, +{bountyGained:0.#} bounty ({bountyLost:0.#} did not fit).");
        }

        /// <summary>The victim's authority refused the marked steal (a shell or a wall): nothing was credited and the mark stays. Authority only.</summary>
        public void OnMarkedStealRefused(ChickenController victim)
        {
            if (!HasStateAuthority) return;
            _log?.Info(Source, $"{name}: the marked steal on {victim.name} was refused (shell or wall): the mark stays.");
        }

        /// <summary>Round reset (<c>ChickenController.RPC_ResetControlStates</c>, the match authority's PLAY AGAIN): the mark is cleared without effect and the last-marked memory with it.</summary>
        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            ClearToOff();
            _hasLast = false;
        }

        // The draw: living rivals anywhere, never an ally of the same corner or a decoy, the last marked out unless he is the only rival; one weight each
        // and one proportional pick (LootPickRules). No candidate: stay Waiting, retried next tick.
        private void TryDraw(in LootMarkFacts facts, GameManager gm)
        {
            _candidates.Clear();
            int ownCorner = _controller != null ? _controller.HomeCornerIndex : -1;
            int rivals = 0;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _controller || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                if (ownCorner >= 0 && c.HomeCornerIndex == ownCorner) continue;   // same corner: an ally
                rivals++;
                if (c.Combat != null && c.Combat.IsRemoved) continue;
                _candidates.Add(c.Id);
            }
            LootPickRules.Filter(_candidates, _hasLast, _last, rivals);
            if (_candidates.Count == 0) return;

            var table = facts.Weights;
            table.OverThreshold = facts.OverFraction * gm.FoodTargetToWin;
            _weights.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                float banked = 0f, carried = 0f;
                if (Runner.TryFindBehaviour(_candidates[i], out ChickenController c))
                {
                    banked = AssassinExecute.BankedFood(c.HomeCornerIndex);
                    carried = c.Cargo != null ? c.Cargo.Cargo : 0f;
                }
                _weights.Add(LootPickRules.Weight(banked, carried, table));
            }
            int pick = LootPickRules.Pick(_weights, Random.value);
            if (pick < 0 || !Runner.TryFindBehaviour(_candidates[pick], out ChickenController chosen)) return;

            float sum = 0f;
            for (int i = 0; i < _weights.Count; i++) sum += _weights[i];
            MarkedTarget = chosen.Id;
            AgeTimer = TickTimer.CreateFromSeconds(Runner, facts.Seconds);
            State = LootState.Live;
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootDraw(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller), MatchTelemetry.Seat(chosen),
                _candidates.Count, sum > 0f ? _weights[pick] / sum : 0f));
            _log?.Info(Source, $"{name}: marked {chosen.name} (window {facts.Seconds:0.0} s, {_candidates.Count} candidate(s)).");
        }

        /// <summary>A mark ended unpaid (it timed out, or its bird left): its bird is the last marked and the next draw is scheduled from Steal's cooldown.</summary>
        private void EndMark(string why)
        {
            if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController victim)) victim = null;
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootMove(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller),
                victim != null ? MatchTelemetry.Seat(victim) : "?", MarkAgeSeconds, why));
            _last = MarkedTarget;
            _hasLast = true;
            ResetMark();
            State = LootState.Waiting;
        }

        private void ClearToOff()
        {
            ResetMark();
            State = LootState.Off;
        }

        private void ResetMark()
        {
            MarkedTarget = NetworkBehaviourId.None;
            AgeTimer = TickTimer.None;
        }
    }
}
