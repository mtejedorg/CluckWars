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

        [Networked] public NetworkBehaviourId MarkedTarget { get; private set; }
        [Networked] public TickTimer ArmTimer { get; private set; }
        [Networked] public TickTimer WindowTimer { get; private set; }
        [Networked] public bool KillReady { get; private set; }

        public int SlotIndex { get; set; } = 0;

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
        private ILogService _log;

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
                ArmTimer = TickTimer.CreateFromSeconds(Runner, ArmDuration);
                WindowTimer = TickTimer.CreateFromSeconds(Runner, WindowDuration);
                KillReady = false;
                _log?.Info(Source, $"Marked target {preResolvedTarget.name} (Arming {ArmDuration:0.0}s).");
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
                        // RPC, not a direct call: the victim is usually owned by another
                        // peer, and ExecuteRemoval is authority-local.
                        target.Combat.RPC_ExecuteRemoval(Id, 2.0f);
                    }

                    // The execute bounty. Every other Assassin income route - Snatch, Sneaky
                    // Steal, and the cargo transfer just above - requires the victim to
                    // already be carrying, so without this an execute on an empty-handed
                    // rival pays exactly nothing and the class can be starved out early.
                    //
                    // Paid straight into the bounty bag rather than dropped on the ground, so
                    // it cannot be vultured by a bystander who happens to walk past. Written
                    // directly because Press() is already gated on this Assassin's own
                    // StateAuthority.
                    float bounty = ResolveExecuteBounty(target);
                    if (bounty > 0f && _cargo != null)
                    {
                        _cargo.BountyBag += bounty;
                    }

                    _log?.Info(Source, $"Kill Executed! Cargo transferred to bounty bag (+{bounty:0.#} " +
                        $"execute bounty), {target.name} removed.");
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

            // Check distance. Planar XZ, matching AbilityAim / MarkKillAbilitySO's aim shape:
            // a 3D measure here could fizzle a mark for a height difference the preview never
            // showed and the flat arena does not have.
            float dist = PlanarDistance(transform.position, target.transform.position);
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

        /// <summary>
        /// Is <paramref name="target"/> alone enough to be executed — no third live chicken
        /// within <see cref="IsolationRadius"/> of it? This is the crowd counterplay, and it
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
        public static bool IsIsolated(ChickenController target, ChickenController excludeCaster)
        {
            if (target == null) return false;

            float radiusSqr = IsolationRadius * IsolationRadius;
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
        public bool IsTargetIsolated(ChickenController target) => IsIsolated(target, _controller);

        private static float PlanarDistance(Vector3 a, Vector3 b) => Mathf.Sqrt(PlanarSqrDistance(a, b));

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
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
