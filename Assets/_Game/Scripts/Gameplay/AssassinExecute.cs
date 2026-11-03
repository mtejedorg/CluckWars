using CluckWars.Abilities;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Assassin Mark/Kill execute state machine (spec §4).
    /// Press: mark a random living rival anywhere (<see cref="MarkPickRules"/>). The Assassin then finds it, stuns it (his
    /// own Ambush or anyone's stun) and presses again within kill range before the window runs out. The numbers are data, on
    /// <c>MarkKill.asset</c>. Success: transfer ALL cargo to capacity-exempt bounty bag + ~2s target removal.
    /// Counterplays: run out the clock; stun the Assassin (denies the kill); reach your own base, where no ability reaches you.
    /// The mark does not end at home or at any range: only expiry, a kill, or the target leaving play ends it.
    /// </summary>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AssassinExecute : NetworkBehaviour
    {
        private const string Source = "AssassinExecute";

        // Today's numbers: the field defaults of MarkKillAbilitySO, and the fallback if no MarkKill asset is registered.
        // Not a gameplay beat: the floor that lets the victim's authority see the replicated mark before a Kill can reach it.
        public const float ArmDuration = 0.6f;
        public const float WindowDuration = 7.0f;
        public const float DefaultKillRange = 8.0f;
        public const float FailCooldown = 5.0f;
        public const float SuccessCooldown = 15.0f;
        /// <summary>Fallback for <see cref="RemovalSeconds"/> (no MarkKill asset registered); equals the asset.</summary>
        public const float DefaultRemovalSeconds = 3.0f;

        [Networked] public NetworkBehaviourId MarkedTarget { get; private set; }
        [Networked] public TickTimer ArmTimer { get; private set; }
        [Networked] public TickTimer WindowTimer { get; private set; }
        [Networked] public bool KillReady { get; private set; }

        /// <summary>The slot holding Death Mark (kit position 0 today), for the cooldowns only this behaviour decides.</summary>
        private int SlotIndex
        {
            get
            {
                if (_abilities != null)
                    for (int i = 0; i < AbilityController.SlotCount; i++)
                        if (_abilities.GetSlot(i) is MarkKillAbilitySO) return i;
                return 0;
            }
        }

        // The one MarkKill asset, read through the registry so every peer has it: a proxy needs the same numbers for the
        // receiver-side bounds (ExecuteRules, CW-230) and loadouts are not replicated.
        private MarkKillAbilitySO _tuning;

        /// <summary>Tuned reach of the Kill press (<c>MarkKill.asset</c>).</summary>
        public float KillRange => _tuning != null ? _tuning.KillRange : DefaultKillRange;
        /// <summary>Tuned life of a mark (<c>MarkKill.asset</c>).</summary>
        public float WindowSeconds => _tuning != null ? _tuning.WindowSeconds : WindowDuration;
        /// <summary>Tuned time an executed chicken is out before it respawns at its base (<c>MarkKill.asset</c>). The only
        /// duration Press sends, and the bound the victim's authority judges the claim by.</summary>
        public float RemovalSeconds => _tuning != null ? _tuning.RemovalSeconds : DefaultRemovalSeconds;
        /// <summary>Tuned arm time of a mark (<c>MarkKill.asset</c>): the Death Mark draw never outlasts it.</summary>
        public float ArmSeconds => _tuning != null ? _tuning.ArmSeconds : ArmDuration;
        private float FailSeconds => _tuning != null ? _tuning.FailCooldownSeconds : FailCooldown;
        private float SuccessSeconds => _tuning != null ? _tuning.SuccessCooldownSeconds : SuccessCooldown;

        // Authority-local: the mark a Kill press ended, kept so a shell refund can restore it (no networked field).
        private NetworkBehaviourId _pressedTarget;
        private TickTimer _pressedWindow, _pressedArm;
        private bool _pressedHadLast;
        private NetworkBehaviourId _pressedLastMark;

        // Authority-local: who the last mark was (never picked twice running) and the pick's scratch list.
        private bool _hasLastMark;
        private NetworkBehaviourId _lastMark;
        private readonly System.Collections.Generic.List<NetworkBehaviourId> _candidates =
            new System.Collections.Generic.List<NetworkBehaviourId>(4);

        /// <summary>
        /// Flat food this execute pays on top of the victim's cargo, doubled (by default)
        /// when the victim was the match leader — the comeback valve that used to arrive as
        /// eight food scattered on the ground.
        /// </summary>
        private float ResolveExecuteBounty(ChickenController victim)
        {
            var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
            if (config == null) return 0f;

            float bounty = config.ExecuteBounty;
            if (victim != null && victim.LeaderBountyActive)
                bounty *= Mathf.Max(1f, config.LeaderExecuteBountyMultiplier);
            return bounty;
        }

        /// <summary>
        /// The execute bounty, on this Assassin's authority once the victim's authority reported the kill. Every other Assassin
        /// income route - Snatch, Sneaky Steal, the cargo transfer - needs the victim to be carrying, so without this an execute
        /// on an empty-handed rival pays nothing and the class can be starved out early. Paid into the bounty bag rather than
        /// onto the ground so a bystander cannot vulture it.
        /// </summary>
        public void PayExecuteBounty(ChickenController victim)
        {
            float bounty = ResolveExecuteBounty(victim);
            if (bounty <= 0f || _cargo == null) return;
            _cargo.BountyBag += bounty;
            _log?.Info(Source, $"Kill Executed! +{bounty:0.#} execute bounty into the bounty bag for {victim.name}.");
        }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ChickenMatchStats _stats;
        private ILogService _log;
        private AbilityRegistrySO _abilityRegistry;

        [Inject]
        public void Construct(ILogService log, AbilityRegistrySO abilityRegistry)
        {
            _log = log;
            _abilityRegistry = abilityRegistry;
        }

        public override void Spawned()
        {
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _cargo = GetComponent<ChickenCargo>();
            _abilities = GetComponent<AbilityController>();
            _stats = GetComponent<ChickenMatchStats>();

            foreach (var a in _abilityRegistry != null ? _abilityRegistry.ActiveAbilities : System.Linq.Enumerable.Empty<AbilityBaseSO>())
                if (a is MarkKillAbilitySO mark) { _tuning = mark; break; }
            if (_tuning == null)
                _log?.Error(Source, $"{name}: no MarkKill asset in AbilityRegistrySO, so the execute runs on its built-in " +
                    "numbers and ignores any tuning. Register MarkKill.asset.");
        }

        /// <summary>Press triggered by the Mark/Kill ability button slot: the mark, or the Kill once a mark is up.</summary>
        public bool Press()
        {
            if (!HasStateAuthority) return false;
            if (_controller == null || !ControlRules.CanCast(_controller.CurrentControlState)) return false;

            if (MarkedTarget == NetworkBehaviourId.None)
            {
                // ---- Phase 1: Mark ------------------------------------------------
                // Decided here, once, by the Assassin's authority; every peer reads the replicated MarkedTarget.
                GatherMarkCandidates();
                int pick = MarkPickRules.Pick(_candidates.Count, Random.value);
                if (pick < 0 || !Runner.TryFindBehaviour(_candidates[pick], out ChickenController chosen))
                {
                    _log?.Verbose(Source, "Press: no rival to mark.");
                    return false;
                }

                MarkedTarget = chosen.Id;
                ArmTimer = TickTimer.CreateFromSeconds(Runner, ArmSeconds);
                WindowTimer = TickTimer.CreateFromSeconds(Runner, WindowSeconds);
                KillReady = false;
                // The press just charged this slot's full cooldown (TryActivate sets it before OnActivate), and the Kill press
                // comes while it would still run. The slot is free while a mark runs (IsUsable gates it on CanKillNow); the
                // outcome then sets the real cooldown.
                if (_abilities != null) _abilities.TriggerCooldown(SlotIndex, 0f);
                _log?.Info(Source, $"Marked target {chosen.name} (window {WindowSeconds:0.0}s, arm {ArmSeconds:0.0}s).");
                return true;
            }
            else
            {
                // ---- Phase 2: Kill Execute ----------------------------------------
                if (!CanKillNow)
                {
                    _log?.Verbose(Source, "Press: Kill refused (target must be stunned, armed, in range and on this side of any base wall).");
                    return false;
                }

                // Without these siblings there is no kill-credit id to hand out and no bag to
                // pay into, so say so rather than executing for free.
                if (_cargo == null || _combat == null)
                {
                    _log?.Error(Source, $"{name}: cannot execute - sibling behaviours missing " +
                        $"(ChickenCargo={_cargo != null}, ChickenCombat={_combat != null}). Check the " +
                        "Chicken prefab still carries both alongside AssassinExecute.");
                    EndMark();
                    return false;
                }

                if (Runner.TryFindBehaviour(MarkedTarget, out ChickenController target))
                {
                    // Nothing is paid until the victim is a complete, live chicken: the bounty
                    // below is unconditional once reached, and ExecuteRemoval early-returns on
                    // an already-removed victim, so going ahead would pay for a corpse. The
                    // IsRemoved read is a proxy view here (advisory); ExecuteRemoval's own guard
                    // on the victim's authority stays the real defence.
                    if (target.Cargo == null || target.Combat == null || target.Combat.IsRemoved)
                    {
                        _log?.Info(Source, $"Execute aborted: {target.name} is not an executable " +
                            $"target (Cargo={target.Cargo != null}, Combat={target.Combat != null}, " +
                            $"alreadyRemoved={target.Combat != null && target.Combat.IsRemoved}).");
                        EndMark();
                        return false;
                    }

                    // A steal-proof target (Egg Shell / Turtle Mode) keeps its cargo; the execute
                    // itself, the removal and the bounty below are unchanged.
                    //
                    // The looted cargo is NOT credited here. The victim's authority drains it and
                    // reports the amount it actually took back through
                    // ChickenCargo.RPC_CreditExecuteLoot, so food the victim banked while stunned
                    // on its own doorstep is never paid twice. The expectation is recorded first
                    // because on a shared authority (solo, bots) both RPCs run synchronously
                    // inside this call. RPC_TransferAllToBountyBag takes THIS behaviour's Id on
                    // purpose: its receiver resolves an AssassinExecute and checks the mark.
                    bool takesCargo = StealRules.CanStealFrom(target);
                    if (takesCargo)
                    {
                        _cargo.ExpectExecuteLoot(target);
                        target.Cargo.RPC_TransferAllToBountyBag(Id);
                    }

                    // The victim's authority reports the kill back through RPC_CreditKill, which
                    // only pays an execute recorded here (CW-230). Recorded BEFORE the removal for
                    // the same shared-authority reason as ExpectExecuteLoot above.
                    if (_stats != null) _stats.ExpectKillCredit(target);
                    else _log?.Error(Source, $"{name}: no ChickenMatchStats sibling - this execute scores no kill.");

                    // RPC, not a direct call: the victim is usually owned by another peer, and
                    // ExecuteRemoval is authority-local. The id is the sibling ChickenCombat's,
                    // because CreditKillToAttacker resolves a ChickenCombat - passing this
                    // behaviour's own Id (CW-050) meant no execute ever scored a kill. Sent while
                    // MarkedTarget still names the victim: the receiver checks the mark (CW-230).
                    target.Combat.RPC_ExecuteRemoval(_combat.Id, RemovalSeconds);

                    // The execute bounty follows the kill report (PayExecuteBounty), so a removal the victim's
                    // authority refuses leaves nothing half-applied.
                    _log?.Info(Source, $"Kill pressed on {target.name} " +
                        $"(cargo transfer {(takesCargo ? "requested" : "skipped: steal-proof")}).");
                    _pressedTarget = MarkedTarget;
                    _pressedWindow = WindowTimer;
                    _pressedArm = ArmTimer;
                    _pressedHadLast = _hasLastMark;
                    _pressedLastMark = _lastMark;
                    EndMark();

                    if (_abilities != null)
                    {
                        _abilities.TriggerCooldown(SlotIndex, SuccessSeconds);
                    }
                    return true;
                }

                EndMark();
                return false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (MarkedTarget == NetworkBehaviourId.None) return;

            if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController target)
                || target.Combat == null || target.Combat.IsRemoved)
            {
                _log?.Info(Source, "Mark fizzled: target lost or removed.");
                FizzleMark();
                return;
            }

            // Qualified only while the target is stunned, right now: the kill needs the stun, not its memory. Never a shelled one.
            bool qualified = ArmTimer.Expired(Runner) && target.CurrentControlState == ControlState.Stunned &&
                             ExecuteRules.CanBeExecuted(target.IsStealProof);
            if (qualified != KillReady)
            {
                KillReady = qualified;
                if (qualified) _log?.Info(Source, $"Target {target.name} qualified for Kill execute!");
            }

            // Window expiry check
            if (WindowTimer.Expired(Runner))
            {
                _log?.Info(Source, "Mark expired: window lapsed.");
                FizzleMark();
            }
        }

        /// <summary>
        /// Would a Kill press land now? Target stunned (<see cref="KillReady"/>), Assassin able to cast, within
        /// <see cref="KillRange"/>, and no base wall between them. Read-only over replicated state, so the button, the HUD and the
        /// press agree on every peer.
        /// </summary>
        public bool CanKillNow
        {
            get
            {
                if (Object == null || !Object.IsValid || !KillReady || MarkedTarget == NetworkBehaviourId.None) return false;
                if (_controller == null || !ControlRules.CanCast(_controller.CurrentControlState)) return false;
                if (!Runner.TryFindBehaviour(MarkedTarget, out ChickenController target)) return false;
                if (!ExecuteRules.CanBeExecuted(target.IsStealProof)) return false;
                Vector3 me = transform.position, it = target.transform.position;
                return PlanarDistance(me, it) <= KillRange && !PlayerBase.WallBetween(me, it);
            }
        }

        /// <summary>
        /// The victim's authority says this chicken's Kill landed on a shell (raised inside one round trip of the press):
        /// nothing was removed, credited or paid. Refund: the Kill's cooldown is lifted, and the mark it ended comes back with
        /// its own timer if it still has time and no new mark replaced it. Receiver-validated like the steal refund: the
        /// sender must speak for the victim, and a Kill on it must be pending here (consumed once), so it cannot be forged.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RefundShelledKill(NetworkBehaviourId victimId, RpcInfo info = default)
        {
            if (Runner == null || !Runner.TryFindBehaviour(victimId, out ChickenController victim) || victim == null ||
                victim.Object == null || !victim.Object.IsValid)
            {
                _log?.Warn(Source, $"{name}: rejected RPC_RefundShelledKill - no live chicken resolves from {victimId}.");
                return;
            }
            bool attributable = victim != _controller &&
                (info.IsInvokeLocal ? victim.Object.HasStateAuthority : victim.Object.StateAuthority == info.Source);
            if (!attributable || _stats == null || !_stats.TryCancelKillCredit(victimId))
            {
                _log?.Warn(Source, $"{name}: rejected RPC_RefundShelledKill from {info.Source} - " +
                    (attributable ? $"no pending Kill on {victim.name}." : $"{victim.name} is not a victim it speaks for."));
                return;
            }

            if (_abilities != null) _abilities.TriggerCooldown(SlotIndex, 0f);
            bool restored = ExecuteRules.ShouldRestoreShelledMark(MarkedTarget == NetworkBehaviourId.None,
                _pressedTarget == victimId, !_pressedWindow.ExpiredOrNotRunning(Runner));
            if (restored)
            {
                MarkedTarget = _pressedTarget;
                ArmTimer = _pressedArm;
                WindowTimer = _pressedWindow;
                KillReady = false;
                _hasLastMark = _pressedHadLast;
                _lastMark = _pressedLastMark;
            }
            _log?.Info(Source, $"Kill on {victim.name} met a shell: refunded" + (restored ? ", mark restored." : " (mark not restored)."));
        }

        /// <summary>Is there a rival a press would mark? Authority-local history, so a proxy only counts rivals (fails open).</summary>
        public bool HasMarkCandidate
        {
            get
            {
                if (Object == null || !Object.IsValid) return false;
                GatherMarkCandidates();
                return _candidates.Count > 0;
            }
        }

        /// <summary>Living, non-decoy rivals, anywhere; the last mark filtered out by <see cref="MarkPickRules"/>.</summary>
        private void GatherMarkCandidates()
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
            MarkPickRules.Filter(_candidates, _hasLastMark, _lastMark, rivals);
        }

        private static float PlanarDistance(Vector3 a, Vector3 b) => Mathf.Sqrt(PlanarSqrDistance(a, b));

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Round reset (PLAY AGAIN, via <c>ChickenController.RPC_ResetControlStates</c> on this chicken's state
        /// authority): drops a live mark WITHOUT the fail cooldown. CW-231: a mark placed in the last ~1 s of a round
        /// outlived the end screen; the reset teleport then put the target 30-56 m away, the next tick fizzled the mark
        /// on range and <see cref="FizzleMark"/> re-armed a 5 s cooldown AFTER <c>EndAllForReset</c> had cleared every
        /// slot, so the Assassin opened the new round with ~3.8 s left on Death Mark.
        /// </summary>
        /// <summary>This chicken was executed: a mark it holds ends with it (recorded as the last mark, no cooldown change:
        /// cooldowns keep running while it is out). Authority only.</summary>
        public void DropMarkForRemoval()
        {
            if (!HasStateAuthority || MarkedTarget == NetworkBehaviourId.None) return;
            EndMark();
        }

        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            ResetMark();
            _hasLastMark = false;
        }

        private void FizzleMark()
        {
            EndMark();
            if (_abilities != null)
            {
                _abilities.TriggerCooldown(SlotIndex, FailSeconds);
            }
        }

        /// <summary>A mark ended, paid or failed: its target is the one the next press cannot pick. The round reset does not come through here.</summary>
        private void EndMark()
        {
            _lastMark = MarkedTarget;
            _hasLastMark = true;
            ResetMark();
        }

        private void ResetMark()
        {
            MarkedTarget = NetworkBehaviourId.None;
            ArmTimer = TickTimer.None;
            WindowTimer = TickTimer.None;
            KillReady = false;
        }
    }
}
