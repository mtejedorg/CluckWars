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
    /// <see cref="LootMarkFacts.LeadSeconds"/> of ready (and the v2 draw gate of <see cref="LootRules.MayDraw"/> holds); <c>Live</c> on a rival;
    /// <c>Overdue</c> once the mark is old with Steal ready (it stays); a mark old with Steal on cooldown moves, unpaid. Rule v2 (2026-11-26): only a rival
    /// who carries food and is not hidden, shelled or inside his own base is a candidate (<see cref="LootRules.Eligible"/>), a bird near his own base weighs
    /// less (<see cref="LootPickRules.HomeFactor"/>), and a live mark ends unpaid when its bird is gone, goes home, empties his beak or is light and a
    /// closer bird exists (<see cref="LootRules.Keep"/>). The last marked rival, paid or not, is never picked next (unless the only rival in the match). All
    /// local state is rebuilt from the replicated fields except the last-marked memory and the time of the last unpaid end, which are authority-local like
    /// the Death Mark's.
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
        private float _unpaidEndAt = float.NegativeInfinity;   // Runner.SimulationTime of the last unpaid end: the redraw gap counts from it
        private float _dryFor;           // seconds a draw has been allowed and found nobody carrying anything (the stalled draw's clock)
        private bool _speculative;       // the live mark came from the stalled draw and its bird has not carried anything yet
        private readonly System.Collections.Generic.List<float> _distances = new System.Collections.Generic.List<float>(4);
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
            if (_controller != null) _controller.LocalEvents.LootTaken += OnLootTakenSeen;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_controller != null) _controller.LocalEvents.LootTaken -= OnLootTakenSeen;
        }

        // Every peer, diagnostics only: the one observer (ChickenLocalEvents, ARCH-EVT-03) tells this peer a paid marked steal arrived, so a session's logs show it
        // on every peer, not only on the Thief's. The presenters of slice B subscribe to the same event.
        private void OnLootTakenSeen(LootTakenEvent e)
        {
            if (_log == null) return;
            string victim = Runner != null && Runner.TryFindBehaviour(e.Victim, out ChickenController robbed) && robbed != null ? robbed.name : e.Victim.ToString();
            _log.Info(Source, $"{name}: loot taken seen from {victim}: +{e.Take:0.#} stolen, +{e.Bounty:0.#} bounty ({(HasStateAuthority ? "authority" : "proxy")}).");
        }

        // Local, every peer, diagnostics only: what this peer last saw of the replicated mark, so a session's logs show the mark arriving on every peer,
        // not only on the Thief's. The presentation reads the state, and the paid steal through ChickenLocalEvents.
        private NetworkBehaviourId _seenTarget;
        private bool _seenInit;

        public override void Render()
        {
            if (_log == null) return;
            var target = MarkedTarget;
            if (!_seenInit) { _seenInit = true; _seenTarget = target; return; }
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
                if (DrawAllowed(facts, gm, stealLeft)) TryDraw(facts, gm, stealLeft <= 1e-4f);
                else _dryFor = 0f;   // the clock counts only while a draw is allowed
                return;
            }

            if (!MarkLive) return;

            if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController target) || target.Object == null || !target.Object.IsValid
                || target.Combat == null || target.Combat.IsRemoved)
            {
                EndMark(LootEnd.Gone);
                return;
            }

            // A marked steal is in flight: its answer decides the mark, so the clock does not.
            if (_cargo != null && _cargo.MarkedStealPending) return;

            // Rule v2, 4a to 4d (LootRules.Keep): home, empty and light end the mark unpaid. A light bird with Steal ready keeps it unless a bird no farther
            // than he is (plus the slack) could carry it instead; then it moves there directly, with no redraw gap.
            float wall = WallRadius(gm);
            float targetCargo = target.Cargo != null ? target.Cargo.Stealable : 0f;
            bool stealReady = stealLeft <= 0f;
            if (_speculative && targetCargo > 1e-4f) _speculative = false;   // he carries something now: the ordinary rules take over
            if (_speculative && !InsideOwnBase(target, wall))
            {
                // A random mark on an empty bird waits for food, but gives way as soon as a rival worth marking carries it (the redraw then picks him).
                GatherCandidates(facts, wall, target, facts.MinCargo);
                if (_candidates.Count > 0) { EndMark(LootEnd.Empty); return; }
            }
            int closer = LootRules.IsLight(targetCargo, facts.KeepCargo) && stealReady ? FindCloser(facts, gm, target, wall) : -1;
            LootEnd end = LootRules.Keep(false, InsideOwnBase(target, wall), targetCargo, facts.KeepCargo, stealReady, closer >= 0, _speculative);
            if (end == LootEnd.Light && closer >= 0)
            {
                MoveMark(facts, target, closer);
                return;
            }
            if (end != LootEnd.None)
            {
                EndMark(end);
                return;
            }

            switch (LootRules.Advance(State, MarkAgeSeconds, facts.Seconds, stealReady))
            {
                case LootStep.Move:
                    EndMark(LootEnd.Timeout);
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
                // The mark ended while the steal was in flight (the bird was executed, the round reset): the take is credited, no bounty is owed
                // (ChickenCargo.RPC_SettleMarkedSteal pays it only while the mark holds), and there is nothing left to spend.
                _log?.Info(Source, $"{name}: a marked steal on {victim.name} landed after its mark had ended (+{take:0.#} take, no bounty).");
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

        /// <summary>
        /// Raised on the Thief's authority when the victim's authority refused a marked steal (a shell or a wall), with the victim. A plain local C# event,
        /// not a replicated counter (so not ARCH-EVT-03's concern): the Thief's own screen reads it for the SAFE pop; no other peer ever hears it.
        /// </summary>
        public event System.Action<ChickenController> MarkedStealRefused;

        /// <summary>The victim's authority refused the marked steal (a shell or a wall): nothing was credited and the mark stays. Authority only.</summary>
        public void OnMarkedStealRefused(ChickenController victim)
        {
            if (!HasStateAuthority) return;
            _log?.Info(Source, $"{name}: the marked steal on {victim.name} was refused (shell or wall): the mark stays.");
            MarkedStealRefused?.Invoke(victim);
        }

        /// <summary>Round reset (<c>ChickenController.RPC_ResetControlStates</c>, the match authority's PLAY AGAIN): the mark is cleared without effect and the last-marked memory with it.</summary>
        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            ClearToOff();
            _hasLast = false;
            _unpaidEndAt = float.NegativeInfinity;
        }

        // The v2 draw gate (LootRules.MayDraw): Steal within the lead of ready, the opening seconds over, the redraw gap since the last unpaid end
        // run, free room in the beak and no marked steal in flight.
        private bool DrawAllowed(in LootMarkFacts facts, GameManager gm, float stealLeft)
        {
            float now = (float)Runner.SimulationTime;
            float matchTime = gm.MatchDurationSeconds - gm.TimeRemaining;
            float room = _cargo != null ? _cargo.Capacity - _cargo.Cargo : 0f;
            return LootRules.MayDraw(stealLeft, facts.LeadSeconds, matchTime, facts.OpeningSeconds, now - _unpaidEndAt, facts.RedrawGapSeconds,
                room, _cargo != null && _cargo.MarkedStealPending);
        }

        private static float WallRadius(GameManager gm) => gm != null && gm.Config != null ? gm.Config.BaseKeepOutRadius : 0f;

        // He stands inside his own base's wall (no ability crosses it): the same IsInside the base wall uses. No wall radius or no base: not inside.
        private static bool InsideOwnBase(ChickenController c, float wall) =>
            wall > 0f && PlayerBase.TryGetBaseCentre(c.HomeCornerIndex, out Vector2 centre) && BaseWallRules.IsInside(c.transform.position, centre, wall);

        private static float DistanceToOwnBase(ChickenController c) =>
            PlayerBase.TryGetBaseCentre(c.HomeCornerIndex, out Vector2 centre)
                ? Vector2.Distance(new Vector2(c.transform.position.x, c.transform.position.z), centre)
                : float.MaxValue;   // no base to measure from: no cut (HomeFactor reads it as far away)

        // The candidates (rule v2, section 2): living rivals anywhere who pass LootRules.Eligible, then the last victim out unless he is the only rival in the match
        // (rivals counts the match's rivals, carriers or not). `except` is a bird kept out of the list (the marked bird, when looking for a replacement).
        private void GatherCandidates(in LootMarkFacts facts, float wall, ChickenController except, float minCargo, bool needsFood = true)
        {
            _candidates.Clear();
            int ownCorner = _controller != null ? _controller.HomeCornerIndex : -1;
            int rivals = 0;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _controller || c.Object == null || !c.Object.IsValid) continue;
                bool ally = ownCorner >= 0 && c.HomeCornerIndex == ownCorner;   // same corner: an ally
                if (!ally && !c.IsDecoy) rivals++;
                if (c == except) continue;
                bool removed = c.Combat != null && c.Combat.IsRemoved;
                if (!LootRules.Eligible(removed, ally, c.IsDecoy, InsideOwnBase(c, wall), c.Cargo != null ? c.Cargo.Stealable : 0f, minCargo,
                        c.IsStealProof, BotSightRules.IsHidden(c.VisualOpacity), needsFood)) continue;
                _candidates.Add(c.Id);
            }
            LootPickRules.Filter(_candidates, _hasLast, _last, rivals);
        }

        // One weight (lottery weight times the home factor) and one distance from the Thief for each gathered candidate, in the list's order.
        private void WeighCandidates(in LootMarkFacts facts, GameManager gm, float wall)
        {
            var table = facts.Weights;
            table.OverThreshold = facts.OverFraction * gm.FoodTargetToWin;
            _weights.Clear();
            _distances.Clear();
            Vector3 here = transform.position;
            for (int i = 0; i < _candidates.Count; i++)
            {
                float banked = 0f, carried = 0f, home = 1f, away = float.MaxValue;
                if (Runner.TryFindBehaviour(_candidates[i], out ChickenController c))
                {
                    banked = AssassinExecute.BankedFood(c.HomeCornerIndex);
                    carried = c.Cargo != null ? c.Cargo.Cargo : 0f;
                    home = LootPickRules.HomeFactor(DistanceToOwnBase(c), wall, facts.HomeRadius, facts.HomeMinWeight);
                    Vector3 d = c.transform.position - here;
                    away = new Vector2(d.x, d.z).magnitude;
                }
                _weights.Add(LootPickRules.Weight(banked, carried, table, home));
                _distances.Add(away);
            }
        }

        // The draw: eligible rivals, the last marked out unless he is the only rival in the match, one weight each and one proportional pick (LootPickRules).
        // No candidate: stay Waiting, retried next tick.
        // The last resort only when Steal is ready: a light bird (under LootKeepCargo) loses the mark while Steal is on cooldown (LootRules.Keep).
        private void TryDraw(in LootMarkFacts facts, GameManager gm, bool stealReady)
        {
            float wall = WallRadius(gm);
            GatherCandidates(facts, wall, null, facts.MinCargo);
            // Last resort: nobody carries the usual minimum, so any rival with some food (and the same other filters) is better than no mark.
            if (_candidates.Count == 0 && stealReady && facts.FallbackMinCargo > 0f) GatherCandidates(facts, wall, null, facts.FallbackMinCargo);
            bool speculative = false;
            if (_candidates.Count == 0)
            {
                // Stalled (Maestro, 2026-11-27): nobody carries anything and nobody has for StallSeconds, so the mark goes to a random rival, empty beak included.
                _dryFor += Runner.DeltaTime;
                if (!(stealReady && facts.StallSeconds > 0f && _dryFor >= facts.StallSeconds)) return;
                GatherCandidates(facts, wall, null, 0f, false);
                if (_candidates.Count == 0) return;
                _weights.Clear();
                for (int i = 0; i < _candidates.Count; i++) _weights.Add(1f);   // uniform: a random draw
                speculative = true;
            }
            else WeighCandidates(facts, gm, wall);
            int pick = LootPickRules.Pick(_weights, Random.value);
            if (pick < 0 || !Runner.TryFindBehaviour(_candidates[pick], out ChickenController chosen)) return;
            Mark(facts, chosen, pick);
            _speculative = speculative;
        }

        // A mark lands on `chosen`, the pick-th of the gathered candidates (their weights are in _weights).
        private void Mark(in LootMarkFacts facts, ChickenController chosen, int pick)
        {
            float sum = 0f;
            for (int i = 0; i < _weights.Count; i++) sum += _weights[i];
            MarkedTarget = chosen.Id;
            AgeTimer = TickTimer.CreateFromSeconds(Runner, facts.Seconds);
            State = LootState.Live;
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootDraw(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller), MatchTelemetry.Seat(chosen),
                _candidates.Count, sum > 0f ? _weights[pick] / sum : 0f));
            _log?.Info(Source, $"{name}: marked {chosen.name} (window {facts.Seconds:0.0} s, {_candidates.Count} candidate(s)).");
        }

        // Rule v2, 4d: the index (into _candidates) of the bird a light marked bird gives way to, or -1. Gathers and weighs the other eligible rivals and lets
        // LootPickRules.SwitchTarget pick among those no farther from the Thief than the marked bird plus the slack.
        private int FindCloser(in LootMarkFacts facts, GameManager gm, ChickenController marked, float wall)
        {
            GatherCandidates(facts, wall, marked, facts.MinCargo);   // the usual floor only: a switch never goes to a bird the draw would take only as a last resort
            if (_candidates.Count == 0) return -1;
            WeighCandidates(facts, gm, wall);
            Vector3 d = marked.transform.position - transform.position;
            return LootPickRules.SwitchTarget(_distances, _weights, new Vector2(d.x, d.z).magnitude, facts.SwitchSlackMeters, Random.value);
        }

        // The mark moves straight from a light bird to the candidate `index` of the last FindCloser: the old bird is the last victim, the new mark is a fresh
        // one (age 0), and no redraw gap applies because the Thief is not left without a mark.
        private void MoveMark(in LootMarkFacts facts, ChickenController from, int index)
        {
            if (!Runner.TryFindBehaviour(_candidates[index], out ChickenController to)) { EndMark(LootEnd.Light); return; }
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootMove(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller), MatchTelemetry.Seat(from),
                MarkAgeSeconds, MatchTelemetry.LootMoveLight));
            _last = MarkedTarget;
            _hasLast = true;
            Mark(facts, to, index);
        }

        /// <summary>A mark ended unpaid (<see cref="LootEnd"/>): its bird is the last marked, paid or not, and the redraw gap counts from now.</summary>
        private void EndMark(LootEnd why)
        {
            if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController victim)) victim = null;
            _log?.Info(MatchTelemetry.Source, MatchTelemetry.LootMove(MatchTelemetry.Tick(Runner), MatchTelemetry.Seat(_controller),
                victim != null ? MatchTelemetry.Seat(victim) : "?", MarkAgeSeconds, MatchTelemetry.LootMoveReason(why)));
            _last = MarkedTarget;
            _hasLast = true;
            _unpaidEndAt = (float)Runner.SimulationTime;
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
            _speculative = false;
            _dryFor = 0f;
        }
    }
}
