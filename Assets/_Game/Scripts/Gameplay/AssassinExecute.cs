using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Assassin Mark/Kill execute state machine (spec §4).
    /// Mark target -> 2.0s arm -> qualify on stun -> Kill second press.
    /// Range = 8m, Isolation = 8m radius around target.
    /// Success: transfer ALL cargo to capacity-exempt bounty bag + ~2s target removal.
    /// Counterplays: crowd breaks isolation (fizzle mark); stun assassin (denies kill).
    /// </summary>
    [RequireComponent(typeof(ChickenController))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AssassinExecute : NetworkBehaviour
    {
        private const string Source = "AssassinExecute";

        public const float IsolationRadius = 8.0f;
        public const float ArmDuration = 2.0f;
        public const float WindowDuration = 5.0f;
        public const float MaxMarkRange = 8.0f;
        public const float FailCooldown = 5.0f;
        public const float SuccessCooldown = 15.0f;

        // Layer 8 = Chickens. Named so the two overlap queries below don't repeat a bare 1<<8.
        private const int ChickenLayerMask = 1 << 8;

        [Networked] public NetworkBehaviourId MarkedTarget { get; private set; }
        [Networked] public TickTimer ArmTimer { get; private set; }
        [Networked] public TickTimer WindowTimer { get; private set; }
        [Networked] public bool KillReady { get; private set; }

        public int SlotIndex { get; set; } = 0;

        private ChickenController _controller;
        private ChickenCombat _combat;
        private ChickenCargo _cargo;
        private AbilityController _abilities;
        private ILogService _log;

        // Two distinct buffers: the candidate search iterates _overlapBuffer while calling
        // IsTargetIsolated() per candidate, and IsTargetIsolated() runs its OWN overlap query.
        // Sharing one buffer would clobber the candidate list mid-iteration.
        private static readonly Collider[] _overlapBuffer = new Collider[16];
        private static readonly Collider[] _isolationBuffer = new Collider[16];

        [Inject]
        public void Construct(ILogService log)
        {
            _log = log;
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
        }

        /// <summary>
        /// Press triggered by the Mark/Kill ability button slot.
        /// </summary>
        public bool Press()
        {
            if (!HasStateAuthority) return false;
            if (_controller == null || !ControlRules.CanCast(_controller.CurrentControlState)) return false;

            if (MarkedTarget == NetworkBehaviourId.None)
            {
                // ---- Phase 1: Mark Candidate Search --------------------------------
                ChickenController bestCandidate = null;
                float bestSqr = MaxMarkRange * MaxMarkRange;

                int hits = Physics.OverlapSphereNonAlloc(transform.position, MaxMarkRange, _overlapBuffer, ChickenLayerMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits; i++)
                {
                    var target = _overlapBuffer[i].GetComponentInParent<ChickenController>();
                    if (target == null || target == _controller) continue;
                    if (target.Combat != null && target.Combat.IsRemoved) continue;

                    // Check if rival (different home corner or input authority)
                    if (target.HomeCornerIndex == _controller.HomeCornerIndex && _controller.HomeCornerIndex >= 0) continue;

                    // Check isolation around candidate
                    if (!IsTargetIsolated(target)) continue;

                    float sqr = (target.transform.position - transform.position).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        bestCandidate = target;
                    }
                }

                if (bestCandidate == null)
                {
                    _log?.Verbose(Source, "Press: no isolated rival candidate in range.");
                    return false;
                }

                MarkedTarget = bestCandidate.Id;
                ArmTimer = TickTimer.CreateFromSeconds(Runner, ArmDuration);
                WindowTimer = TickTimer.CreateFromSeconds(Runner, WindowDuration);
                KillReady = false;
                _log?.Info(Source, $"Marked target {bestCandidate.name} (Arming 2s).");
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

                if (Runner.TryFindBehaviour(MarkedTarget, out ChickenController target))
                {
                    // Target execute
                    if (target.Cargo != null)
                    {
                        target.Cargo.RPC_TransferAllToBountyBag(Id);
                    }
                    if (target.Combat != null)
                    {
                        target.Combat.ExecuteRemoval(Id, 2.0f);
                    }

                    _log?.Info(Source, $"Kill Executed! Cargo transferred to bounty bag, {target.name} removed.");
                    ResetMark();

                    if (_abilities != null)
                    {
                        _abilities.TriggerCooldown(SlotIndex, SuccessCooldown);
                    }
                    return true;
                }

                ResetMark();
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

            // Check distance
            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > MaxMarkRange)
            {
                _log?.Info(Source, $"Mark fizzled: target exceeded range ({dist:0.1}m > {MaxMarkRange}m).");
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

        public bool IsTargetIsolated(ChickenController target)
        {
            int hits = Physics.OverlapSphereNonAlloc(target.transform.position, IsolationRadius, _isolationBuffer, ChickenLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                var thirdParty = _isolationBuffer[i].GetComponentInParent<ChickenController>();
                if (thirdParty == null || thirdParty == target || thirdParty == _controller) continue;
                if (thirdParty.Combat != null && thirdParty.Combat.IsRemoved) continue;

                // Another live chicken is near the target -> not isolated!
                return false;
            }
            return true;
        }

        private void FizzleMark()
        {
            ResetMark();
            if (_abilities != null)
            {
                _abilities.TriggerCooldown(SlotIndex, FailCooldown);
            }
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
