using CluckWars.Abilities;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Assassin Mark/Kill execute state machine (spec §4).
    /// Mark target -> arm -> qualify on stun -> Kill second press. The numbers are data, on <c>MarkKill.asset</c>.
    /// Success: transfer ALL cargo to capacity-exempt bounty bag + ~2s target removal.
    /// Counterplays: crowd breaks isolation (fizzle mark); stun assassin (denies kill); reach your own base (fizzle mark).
    /// Anti-chase: a mark that ended leaves a cold trail on that rival (<see cref="ColdTrail{TRival}"/>).
    /// </summary>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AssassinExecute : NetworkBehaviour
    {
        private const string Source = "AssassinExecute";

        // Today's numbers: the field defaults of MarkKillAbilitySO, and the fallback if no MarkKill asset is registered.
        public const float DefaultIsolationRadius = 8.0f;
        public const float DefaultColdTrailSeconds = 15.0f;
        public const float ArmDuration = 2.0f;
        public const float WindowDuration = 5.0f;
        public const float MaxMarkRange = 8.0f;
        public const float FailCooldown = 5.0f;
        public const float SuccessCooldown = 15.0f;

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

        /// <summary>Tuned reach of the mark (<c>MarkKill.asset</c>).</summary>
        public float MarkRange => _tuning != null ? _tuning.MarkRange : MaxMarkRange;
        /// <summary>Tuned life of a mark (<c>MarkKill.asset</c>).</summary>
        public float WindowSeconds => _tuning != null ? _tuning.WindowSeconds : WindowDuration;
        private float ArmSeconds => _tuning != null ? _tuning.ArmSeconds : ArmDuration;
        private float FailSeconds => _tuning != null ? _tuning.FailCooldownSeconds : FailCooldown;
        private float SuccessSeconds => _tuning != null ? _tuning.SuccessCooldownSeconds : SuccessCooldown;
        private float IsolationMeters => _tuning != null ? _tuning.IsolationRadius : DefaultIsolationRadius;
        private float ColdTrailSeconds => _tuning != null ? _tuning.ColdTrailSeconds : DefaultColdTrailSeconds;

        private readonly ColdTrail<NetworkBehaviourId> _coldTrail = new ColdTrail<NetworkBehaviourId>();
        private bool _markIsDecoy;

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

        /// <summary>
        /// Press triggered by the Mark/Kill ability button slot.
        /// </summary>
        /// <param name="preResolvedTarget">
        /// The chicken to mark, already resolved by <c>MarkKillAbilitySO.OnActivate</c>
        /// through the shared <c>GatherTargets</c> scan. Ignored (and unnecessary) on a Kill
        /// press, which reads <see cref="MarkedTarget"/> instead.
        /// </param>
        public bool Press(ChickenController preResolvedTarget = null)
        {
            if (!HasStateAuthority) return false;
            if (_controller == null || !ControlRules.CanCast(_controller.CurrentControlState)) return false;

            if (MarkedTarget == NetworkBehaviourId.None)
            {
                // ---- Phase 1: Mark ------------------------------------------------
                // The candidate comes from MarkKillAbilitySO, whose SingleTarget aim shape
                // resolves to exactly the nearest eligible rival and whose ExtraTargetFilter
                // applies the very isolation + rival rules this method used to re-implement
                // with its own Physics.OverlapSphere (in 3D, against a preview that was
                // planar and had no isolation rule at all — so the preview could mark a
                // target the press then refused).
                //
                // There is deliberately NO fallback scan: a second implementation of those
                // rules is exactly how the two drifted apart in the first place.
                if (preResolvedTarget == null)
                {
                    _log?.Verbose(Source, "Press: no isolated rival candidate in range.");
                    return false;
                }

                MarkedTarget = preResolvedTarget.Id;
                _markIsDecoy = preResolvedTarget.IsDecoy;
                _coldTrail.OnMarked(preResolvedTarget.Id, _markIsDecoy);
                ArmTimer = TickTimer.CreateFromSeconds(Runner, ArmSeconds);
                WindowTimer = TickTimer.CreateFromSeconds(Runner, WindowSeconds);
                KillReady = false;
                // The press just charged this slot's full cooldown (TryActivate sets it before OnActivate), and that
                // cooldown equals the window, so the Kill press would be refused for the mark's whole life. The slot is
                // free while a mark runs (IsUsable gates it on KillReady); the outcome then sets the real cooldown.
                if (_abilities != null) _abilities.TriggerCooldown(SlotIndex, 0f);
                _log?.Info(Source, $"Marked target {preResolvedTarget.name} (Arming {ArmSeconds:0.0}s).");
                return true;
            }
            else
            {
                // ---- Phase 2: Kill Execute ----------------------------------------
                if (!KillReady)
                {
                    _log?.Verbose(Source, "Press: Kill attempted before target is qualified (must be stunned & armed).");
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
                    target.Combat.RPC_ExecuteRemoval(_combat.Id, ExecuteRules.RemovalDurationSeconds);

                    // The execute bounty. Every other Assassin income route - Snatch, Sneaky
                    // Steal, and the cargo transfer above - requires the victim to already be
                    // carrying, so without this an execute on an empty-handed rival pays
                    // nothing and the class can be starved out early. Paid into the bounty bag
                    // rather than onto the ground so a bystander cannot vulture it, and written
                    // directly because Press() is gated on this Assassin's own StateAuthority.
                    float bounty = ResolveExecuteBounty(target);
                    if (bounty > 0f)
                    {
                        _cargo.BountyBag += bounty;
                    }

                    _log?.Info(Source, $"Kill Executed! +{bounty:0.#} execute bounty into the bounty bag, " +
                        $"{target.name} removed (cargo transfer {(takesCargo ? "requested" : "skipped: steal-proof")}).");
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

            // Check distance. Planar XZ, matching AbilityAim / MarkKillAbilitySO's aim shape:
            // a 3D measure here could fizzle a mark for a height difference the preview never
            // showed and the flat arena does not have.
            float dist = PlanarDistance(transform.position, target.transform.position);
            if (dist > MarkRange)
            {
                _log?.Info(Source, $"Mark fizzled: target exceeded range ({dist:0.0}m > {MarkRange}m).");
                FizzleMark();
                return;
            }

            // Home is safe: the marked bird reaching its own base ends the hunt.
            if (IsHome(target))
            {
                _log?.Info(Source, "Mark fizzled: target reached its own base.");
                FizzleMark();
                return;
            }

            // Check isolation break (crowd counterplay)
            if (!IsTargetIsolated(target))
            {
                _log?.Info(Source, "Mark fizzled: target isolation broken by nearby chicken.");
                FizzleMark();
                return;
            }

            // Arming check & Stun qualification
            if (!KillReady)
            {
                if (ArmTimer.Expired(Runner))
                {
                    // Qualify on target being stunned
                    if (target.CurrentControlState == ControlState.Stunned)
                    {
                        KillReady = true;
                        _log?.Info(Source, $"Target {target.name} qualified for Kill execute!");
                    }
                }
            }

            // Window expiry check
            if (WindowTimer.Expired(Runner))
            {
                _log?.Info(Source, "Mark expired: window lapsed.");
                FizzleMark();
            }
        }

        /// <summary>
        /// Is <paramref name="target"/> alone enough to be executed — no third live chicken
        /// within <paramref name="radius"/> of it? This is the crowd counterplay, and it
        /// is the rule the Mark/Kill preview never knew about.
        /// </summary>
        /// <param name="excludeCaster">
        /// The assassin, who does not break their own victim's isolation.
        /// </param>
        /// <remarks>
        /// <b>Static, and iterating the registry.</b> Static so
        /// <c>MarkKillAbilitySO.ExtraTargetFilter</c> can apply the identical rule without a
        /// <c>GetComponent&lt;AssassinExecute&gt;</c> on every candidate on every HUD poll —
        /// and registry-based (≤4 entries, planar XZ) rather than a physics query so the
        /// preview, the usability grey-out and the fizzle check all measure the same thing.
        /// The old <c>Physics.OverlapSphereNonAlloc</c> version needed two separate collider
        /// buffers just to avoid clobbering itself mid-iteration; that whole hazard is gone.
        /// </remarks>
        public static bool IsIsolated(ChickenController target, ChickenController excludeCaster, float radius)
        {
            if (target == null) return false;

            float radiusSqr = radius * radius;
            Vector3 center = target.transform.position;

            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var thirdParty = all[i];
                if (thirdParty == null || thirdParty == target || thirdParty == excludeCaster) continue;
                if (thirdParty.Combat != null && thirdParty.Combat.IsRemoved) continue;

                // Another live chicken is near the target -> not isolated!
                if (PlanarSqrDistance(center, thirdParty.transform.position) <= radiusSqr) return false;
            }
            return true;
        }

        /// <summary>Instance view of <see cref="IsIsolated"/> for this assassin's own call
        /// sites, which always exclude themselves.</summary>
        public bool IsTargetIsolated(ChickenController target) => IsIsolated(target, _controller, IsolationMeters);

        /// <summary>
        /// True when <paramref name="chicken"/> stands inside its OWN base (deposit radius, planar). Static and read-only over
        /// replicated data, so the filter, the telegraph and the fizzle check agree on every peer.
        /// </summary>
        public static bool IsHome(ChickenController chicken)
        {
            var obj = chicken != null ? chicken.Object : null;
            if (obj == null || !obj.IsValid) return false;
            int corner = chicken.HomeCornerIndex;
            if (corner < 0) return false;

            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid || b.CornerIndex != corner) continue;
                return HomeBreakRules.IsHome(chicken.transform.position, b.transform.position, b.DepositRadius);
            }
            return false;
        }

        /// <summary>
        /// Is <paramref name="candidate"/> on this Assassin's cold trail? Authority-local state: anywhere else it fails open
        /// (the press is judged on the authority anyway), so a proxy preview can only be more generous, never wrong in the
        /// direction that eats a press.
        /// </summary>
        public bool IsColdTrail(ChickenController candidate)
        {
            if (candidate == null || Object == null || !Object.IsValid || !HasStateAuthority) return false;

            int rivals = 0;
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i] != _controller && !all[i].IsDecoy) rivals++;
            return _coldTrail.Refuses(candidate.Id, Runner.SimulationTime, rivals);
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
        public void ResetForNewRound()
        {
            if (!HasStateAuthority) return;
            ResetMark();
            _coldTrail.Reset();
        }

        private void FizzleMark()
        {
            EndMark();
            if (_abilities != null)
            {
                _abilities.TriggerCooldown(SlotIndex, FailSeconds);
            }
        }

        /// <summary>A mark ended, paid or failed: it leaves its cold trail. The round reset does not come through here.</summary>
        private void EndMark()
        {
            _coldTrail.OnMarkEnded(MarkedTarget, _markIsDecoy, Runner.SimulationTime, ColdTrailSeconds);
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
