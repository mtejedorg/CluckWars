using CluckWars.Logging;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// A networked food pile. State authority owns <see cref="Amount"/>; chickens drain it
    /// via <see cref="RPC_Drain"/> from any client. Visual feedback is local
    /// (<c>FoodPileVisuals</c>) and reacts to networked-state changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0003 Decision 2 — a pile is a resource AND an obstacle. Its physical footprint
    /// is a <i>stepped</i> function of <c>Amount / MaxAmount</c>: draining a pile shrinks
    /// its root scale, which shrinks the mesh, the blocker capsule and the NavMesh carve
    /// together, so emptying a pile is a permanent edit to the map. The stepping is
    /// deliberately coarse — a carving <see cref="NavMeshObstacle"/> re-carves the NavMesh
    /// on every size change, and per-tick re-carving thrashes bot pathfinding.
    /// </para>
    /// <para>
    /// ADR 0003 Decision 2b — a pile flagged <see cref="IsPermanent"/> (the centre pile)
    /// cannot be drained below a floor and slowly regenerates toward <see cref="MaxAmount"/>.
    /// It therefore never empties and never stops blocking: the late game always has one
    /// contested arena. The flag is networked and stamped by the spawner via
    /// <c>onBeforeSpawned</c> — centre and outer piles share one prefab, so it can't be a
    /// SerializeField.
    /// </para>
    /// <para>
    /// Scene-placed (baked NetworkObject); the master client becomes StateAuthority on
    /// session start. Spawn is left to the runner's scene-load path — no MatchBootstrapper
    /// changes needed for Phase 4.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FoodPile : NetworkBehaviour
    {
        private const string Source = "FoodPile";

        [Tooltip("Amount of food the pile starts with and will be visually scaled against.")]
        [Min(0f)]
        [SerializeField] private float _initialAmount = 30f;

        [Tooltip("How close a chicken must be (chicken position to pile position) to drain.")]
        [Min(0f)]
        [SerializeField] private float _collectRadius = 1.6f;

        [Header("Blocking — stocked piles are solid, so chases route around them")]
        [Tooltip("Solid collider radius at FULL size. Must stay < CollectRadius minus the chicken capsule radius (0.5) and skin width (0.08) so collection still triggers from the edge. The center pile's 1.5× root scale scales this too — keep the margin.")]
        [Min(0.2f)]
        [SerializeField] private float _blockerRadius = 0.65f;
        [Tooltip("Solid collider height. Above the NavMesh step height (0.75) so bots path around, not over.")]
        [Min(0.8f)]
        [SerializeField] private float _blockerHeight = 1.2f;

        [Header("Footprint stepping (ADR 0003 Decision 2) — the pile shrinks as it drains")]
        [Tooltip("How many discrete footprint sizes exist between the smallest non-empty pile and a full one. Quantised on purpose: the blocker carries a carving NavMeshObstacle, and re-sizing it continuously re-carves the NavMesh and thrashes bot pathfinding.")]
        [Range(2, 8)]
        [SerializeField] private int _footprintSteps = 4;

        [Tooltip("Footprint scale of the smallest non-empty step, as a fraction of full size. The blocker is a child of the pile root, so this scales the mesh, the collider AND the NavMesh carve together. 1 = never shrinks (old behaviour).")]
        [Range(0.2f, 1f)]
        [SerializeField] private float _minFootprintScale = 0.45f;

        [Header("Permanent pile (ADR 0003 Decision 2b) — only applies when IsPermanent")]
        [Tooltip("Drain floor as a fraction of MaxAmount. A permanent pile never goes below this, so it stays a solid obstacle all match. Ignored on ordinary piles.")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float _permanentFloorFraction = 0.6f;

        [Tooltip("Food per second a permanent pile regenerates back toward MaxAmount. THE most sensitive number in ADR 0003: too high and the endgame is a free farm, too low and the centre erodes to its floor in the first minute. Default ≈ one player's steady round-trip throughput (~0.5/s), so one farmer holds it steady and two or more shrink it.")]
        [Min(0f)]
        [SerializeField] private float _permanentRegenPerSecond = 0.5f;

        public static readonly System.Collections.Generic.List<FoodPile> ActivePiles = new System.Collections.Generic.List<FoodPile>();

        [Networked] public float Amount { get; set; }
        [Networked] public float MaxAmount { get; set; }
        [Networked] public float VisualScale { get; set; }

        /// <summary>
        /// Set by the spawner via <c>onBeforeSpawned</c> so it is valid from tick zero.
        /// A permanent pile clamps drains at <see cref="DrainFloor"/> and regenerates.
        /// </summary>
        [Networked] public bool IsPermanent { get; set; }

        public float CollectRadius => _collectRadius;

        /// <summary>True when the pile has no food left at all — it stops blocking and stops slowing.</summary>
        public bool IsEmpty => Amount <= 0f;

        /// <summary>Drain floor as a fraction of <see cref="MaxAmount"/>. Zero for ordinary piles.</summary>
        private float FloorFraction => IsPermanent ? Mathf.Clamp01(_permanentFloorFraction) : 0f;

        /// <summary>Number of discrete footprint sizes, guarded against a stale serialized 0.</summary>
        private int Steps => Mathf.Max(2, _footprintSteps);

        /// <summary>Lowest <see cref="Amount"/> a drain may leave. Zero for ordinary piles.</summary>
        public float DrainFloor => MaxAmount * FloorFraction;

        /// <summary>
        /// Food a chicken may actually take right now. Distinct from <see cref="Amount"/>:
        /// a permanent pile still holds its floor, but that food is not collectable.
        /// Callers must credit their cargo against this, not against <see cref="Amount"/>,
        /// or the centre pile becomes an infinite food source at its floor.
        /// </summary>
        public float Available => Mathf.Max(0f, Amount - DrainFloor);

        /// <summary>True when there is food left to collect (as opposed to left standing).</summary>
        public bool HasCollectableFood => Available > 0f;

        private ILogService _log;
        private GameObject _blocker;
        private int _appliedFootprintStep = -1;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        public override void Spawned()
        {
            ActivePiles.Add(this);
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            CreateBlocker();

            if (HasStateAuthority)
            {
                // Only seed the prefab default if the spawner didn't preset Amount /
                // MaxAmount via onBeforeSpawned (e.g., MapGenerator wants a bigger
                // center pile than the small ones around it).
                if (MaxAmount <= 0f)
                {
                    Amount = _initialAmount;
                    MaxAmount = _initialAmount;
                }
                _log?.Debug(Source, $"{name}: Spawned. Amount={Amount}/{MaxAmount}. Permanent={IsPermanent} (floor={DrainFloor:0.0}, regen={_permanentRegenPerSecond}/s).");
            }

            // Fusion pools NetworkObjects — clear the cached step so the footprint is
            // re-applied for this life, then size the pile before its first frame.
            _appliedFootprintStep = -1;
            ApplyFootprint();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ActivePiles.Remove(this);
        }

        /// <summary>
        /// Regenerates a permanent pile back toward <see cref="MaxAmount"/>. Ordinary piles
        /// never refill — the map's density dropping over the match is the ADR 0003 pillar.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            if (!IsPermanent || _permanentRegenPerSecond <= 0f) return;
            if (MaxAmount <= 0f || Amount >= MaxAmount) return;

            Amount = Mathf.Min(MaxAmount, Amount + _permanentRegenPerSecond * Runner.DeltaTime);
        }

        /// <summary>
        /// Solid capsule + NavMesh carve, built in code so the prefab needs no
        /// re-authoring. Local on every peer — Amount is networked, so all peers
        /// agree on whether the pile blocks. Radius/height are authored at FULL size;
        /// the root transform's scale is what shrinks them (see <see cref="ApplyFootprint"/>).
        /// </summary>
        private void CreateBlocker()
        {
            if (_blocker != null) return;
            _blocker = new GameObject("Blocker");
            _blocker.transform.SetParent(transform, worldPositionStays: false);
            _blocker.transform.localPosition = Vector3.zero;

            var cap = _blocker.AddComponent<CapsuleCollider>();
            cap.radius = _blockerRadius;
            cap.height = _blockerHeight;
            cap.center = Vector3.up * (_blockerHeight * 0.5f);

            var carve = _blocker.AddComponent<NavMeshObstacle>();
            carve.shape = NavMeshObstacleShape.Capsule;
            carve.radius = _blockerRadius;
            carve.height = _blockerHeight;
            carve.center = cap.center;
            carve.carving = true;
        }

        public override void Render()
        {
            ApplyFootprint();
        }

        /// <summary>
        /// Which discrete footprint size the pile's current fill maps to.
        /// 0 = empty (no footprint at all); 1 = smallest non-empty; <c>_footprintSteps</c> = full.
        /// For a permanent pile the fill is normalised over its usable range
        /// <c>[floor, max]</c>, so it still spans every step instead of being pinned to the
        /// top two — that is what makes the centre's size a readout of contest intensity.
        /// </summary>
        private int FootprintStep()
        {
            if (IsEmpty) return 0;

            float fill = MaxAmount > 0f ? Mathf.Clamp01(Amount / MaxAmount) : 0f;
            float floor = FloorFraction;
            float t = Mathf.Clamp01((fill - floor) / Mathf.Max(1f - floor, 0.0001f));

            return Mathf.Clamp(Mathf.CeilToInt(t * Steps), 1, Steps);
        }

        /// <summary>Uniform root scale for a given step. The top step is exactly the authored size.</summary>
        private float FootprintScale(int step)
        {
            float u = (Mathf.Clamp(step, 1, Steps) - 1f) / (Steps - 1f);
            return Mathf.Lerp(Mathf.Clamp01(_minFootprintScale), 1f, u);
        }

        /// <summary>
        /// Local, driven entirely by the networked <see cref="Amount"/> — every peer computes
        /// the same footprint with no extra state. Early-outs unless the step actually changed:
        /// the root scale drives the carving NavMeshObstacle, and re-carving per frame would
        /// thrash <c>NavMesh.CalculatePath</c> for the bots.
        /// </summary>
        private void ApplyFootprint()
        {
            int step = FootprintStep();
            if (step == _appliedFootprintStep) return;
            _appliedFootprintStep = step;

            float baseScale = VisualScale <= 0f ? 1f : VisualScale;
            transform.localScale = Vector3.one * (baseScale * FootprintScale(step));

            // Empty piles stop blocking (the leftover stub is walkable, and the
            // NavMesh un-carves); refilled piles (match restart) turn solid again.
            // A permanent pile never reaches step 0, so it always blocks.
            bool shouldBlock = step > 0;
            if (_blocker != null && _blocker.activeSelf != shouldBlock)
                _blocker.SetActive(shouldBlock);

            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
            {
                _log.Verbose(Source, $"{name}: footprint step {step}/{Steps} " +
                    $"(scale {transform.localScale.x:0.00}, blocking={shouldBlock}) at {Amount:0.0}/{MaxAmount}.");
            }
        }

        /// <summary>
        /// Asks the pile's state authority to remove up to <paramref name="amount"/> food.
        /// Caller is expected to have already credited their own cargo for the same amount;
        /// any over-request (pile empty / under-supplied) just clamps to whatever's left
        /// above <see cref="DrainFloor"/>.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Drain(float amount)
        {
            if (amount <= 0f) return;

            float available = Available;
            if (available <= 0f) return;

            var actual = Mathf.Min(amount, available);
            Amount -= actual;

            float floor = DrainFloor;
            if (Amount < floor) Amount = floor;

            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
            {
                _log.Verbose(Source, $"{name}: drained {actual:0.00} → {Amount:0.0}/{MaxAmount} (floor {floor:0.0}).");
            }
        }
    }
}
