using CluckWars.Logging;
using Fusion;
using UnityEngine;
using UnityEngine.AI;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The pure arithmetic behind <see cref="FoodPile"/> (ADR 0003 Decision 2 / 2b),
    /// lifted out of the <c>NetworkBehaviour</c> so it can be unit-tested in EditMode.
    /// Every method is a total function of its arguments — no networked state, no
    /// transform, no runner. <see cref="FoodPile"/> is the only production caller and
    /// simply feeds it the current <c>Amount</c> / <c>MaxAmount</c> / serialized tunables.
    /// </summary>
    public static class FoodPileMath
    {
        /// <summary>Guards a stale serialized step count — the footprint needs at least two buckets.</summary>
        public static int ClampSteps(int steps) => Mathf.Max(2, steps);

        /// <summary>Drain floor as a fraction of max: the permanent floor, or 0 for an ordinary pile.</summary>
        public static float FloorFraction(bool isPermanent, float permanentFloorFraction) =>
            isPermanent ? Mathf.Clamp01(permanentFloorFraction) : 0f;

        /// <summary>Lowest <c>Amount</c> a drain may leave.</summary>
        public static float DrainFloor(float maxAmount, float floorFraction) => maxAmount * floorFraction;

        /// <summary>
        /// Food a chicken may actually take right now. A permanent pile still holds its
        /// floor, but that food is not collectable — crediting cargo against
        /// <c>Amount</c> instead of this is what would make the centre an infinite source.
        /// </summary>
        public static float Available(float amount, float maxAmount, float floorFraction) =>
            Mathf.Max(0f, amount - DrainFloor(maxAmount, floorFraction));

        /// <summary>
        /// Resulting <c>Amount</c> after draining up to <paramref name="requested"/> food.
        /// Over-requests clamp to whatever sits above the floor; the floor is never breached.
        /// </summary>
        public static float Drain(float amount, float maxAmount, float floorFraction, float requested)
        {
            if (requested <= 0f) return amount;

            float available = Available(amount, maxAmount, floorFraction);
            if (available <= 0f) return amount;

            float next = amount - Mathf.Min(requested, available);
            float floor = DrainFloor(maxAmount, floorFraction);
            return next < floor ? floor : next;
        }

        /// <summary>
        /// Which discrete footprint size a fill maps to.
        /// 0 = empty (no footprint at all); 1 = smallest non-empty; <paramref name="steps"/> = full.
        /// For a permanent pile the fill is normalised over its usable range <c>[floor, max]</c>,
        /// so it still spans every step instead of being pinned to the top two — that is what
        /// makes the centre's size a readout of contest intensity.
        /// </summary>
        public static int FootprintStep(float amount, float maxAmount, float floorFraction, int steps)
        {
            if (amount <= 0f) return 0;

            int s = ClampSteps(steps);
            float fill = maxAmount > 0f ? Mathf.Clamp01(amount / maxAmount) : 0f;
            float t = Mathf.Clamp01((fill - floorFraction) / Mathf.Max(1f - floorFraction, 0.0001f));

            return Mathf.Clamp(Mathf.CeilToInt(t * s), 1, s);
        }

        /// <summary>
        /// Footprint multiplier for a given step. The top step is exactly 1, i.e. the
        /// authored full footprint; lower steps shrink it toward <paramref name="minFootprintScale"/>.
        /// </summary>
        public static float FootprintScale(int step, int steps, float minFootprintScale)
        {
            int s = ClampSteps(steps);
            float u = (Mathf.Clamp(step, 1, s) - 1f) / (s - 1f);
            return Mathf.Lerp(Mathf.Clamp01(minFootprintScale), 1f, u);
        }

        /// <summary>
        /// Shortest XZ distance from a point to the surface of an axis-aligned box.
        /// 0 when the point is inside the footprint. This is what makes collection
        /// <i>surface-relative</i> and therefore size-independent: a chicken pressed
        /// against a pile is always ~0.58 away (capsule radius 0.5 + controller skin
        /// 0.08) no matter how big the pile is, so growing a pile can never push a
        /// touching chicken out of range the way a centre-distance test did.
        /// </summary>
        public static float DistanceToBoxSurfaceXZ(float px, float pz, float cx, float cz, float halfX, float halfZ)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(px - cx) - Mathf.Max(0f, halfX));
            float dz = Mathf.Max(0f, Mathf.Abs(pz - cz) - Mathf.Max(0f, halfZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// A networked food pile. State authority owns <see cref="Amount"/>; chickens drain it
    /// via <see cref="RPC_Drain"/> from any client. Visual feedback is local
    /// (<c>FoodPileVisuals</c>) and reacts to networked-state changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0003 Decision 2 — a pile is a resource AND an obstacle. Its physical footprint
    /// is a <i>stepped</i> function of <c>Amount / MaxAmount</c>: draining a pile shrinks
    /// its root scale, which shrinks the mesh, the blocker box and the NavMesh carve
    /// together, so emptying a pile is a permanent edit to the map. The stepping is
    /// deliberately coarse — a carving <see cref="NavMeshObstacle"/> re-carves the NavMesh
    /// on every size change, and per-tick re-carving thrashes bot pathfinding.
    /// </para>
    /// <para>
    /// Piles are sized in world units by <see cref="FootprintSize"/> (X,Z) and
    /// <c>_blockerHeight</c> (Y), so an island can be wide and flat — the centre island is
    /// 7×4, i.e. seven chickens by four. Everything that asks "is this chicken at the pile"
    /// measures to the pile's SURFACE via <see cref="DistanceToSurface"/>, never to its
    /// centre. That is deliberate and load-bearing: the centre-distance test it replaced
    /// coupled the blocker size to a fixed collect radius, and every time a pile grew past
    /// that bound collection silently stopped working (the 2026-06-01 game-breaker). A
    /// surface-relative reach is constant at any pile size, so the coupling is gone.
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

        [Tooltip("How far PAST the pile's SURFACE a chicken may stand and still collect — not a distance to the pile's centre. A chicken pressed against a pile sits 0.58 away (capsule radius 0.5 + controller skin 0.08) whatever the pile's size, so this margin is size-independent: making a pile bigger can never break collection.")]
        [Min(0.1f)]
        [SerializeField] private float _collectReach = 1.0f;

        [Header("Blocking — stocked piles are solid, so chases route around them")]
        [Tooltip("Full world-space X,Z footprint at 100% fill, in world units (a chicken is 1.0 wide). Non-uniform on purpose: the centre island is wide and flat, not a tower. Used as the fallback when the spawner doesn't stamp FootprintSize via onBeforeSpawned.")]
        [SerializeField] private Vector2 _footprintSize = new Vector2(2.2f, 2.2f);
        [Tooltip("Solid collider / mesh height in world units. NOT scaled by fill — a draining pile shrinks in XZ but stays tall enough to remain terrain. Must stay above the NavMesh step height (0.75) or bots walk straight over piles.")]
        [Min(0.8f)]
        [SerializeField] private float _blockerHeight = 1.4f;

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

        /// <summary>
        /// Full world-space X,Z footprint at 100% fill, stamped by the spawner via
        /// <c>onBeforeSpawned</c> so every peer sizes the pile identically from tick zero.
        /// Zero means "not stamped" and falls back to the prefab's <see cref="_footprintSize"/>,
        /// so scene-placed piles and any older spawn path keep working.
        /// </summary>
        [Networked] public Vector2 FootprintSize { get; set; }

        /// <summary>
        /// Set by the spawner via <c>onBeforeSpawned</c> so it is valid from tick zero.
        /// A permanent pile clamps drains at <see cref="DrainFloor"/> and regenerates.
        /// </summary>
        [Networked] public bool IsPermanent { get; set; }

        /// <summary>True when the pile has no food left at all — it stops blocking and stops slowing.</summary>
        public bool IsEmpty => Amount <= 0f;

        /// <summary>Drain floor as a fraction of <see cref="MaxAmount"/>. Zero for ordinary piles.</summary>
        private float FloorFraction => FoodPileMath.FloorFraction(IsPermanent, _permanentFloorFraction);

        /// <summary>Number of discrete footprint sizes, guarded against a stale serialized 0.</summary>
        private int Steps => FoodPileMath.ClampSteps(_footprintSteps);

        /// <summary>Lowest <see cref="Amount"/> a drain may leave. Zero for ordinary piles.</summary>
        public float DrainFloor => FoodPileMath.DrainFloor(MaxAmount, FloorFraction);

        /// <summary>
        /// Food a chicken may actually take right now. Distinct from <see cref="Amount"/>:
        /// a permanent pile still holds its floor, but that food is not collectable.
        /// Callers must credit their cargo against this, not against <see cref="Amount"/>,
        /// or the centre pile becomes an infinite food source at its floor.
        /// </summary>
        public float Available => FoodPileMath.Available(Amount, MaxAmount, FloorFraction);

        /// <summary>True when there is food left to collect (as opposed to left standing).</summary>
        public bool HasCollectableFood => Available > 0f;

        /// <summary>Authored full footprint: the networked stamp, or the prefab default when unstamped.</summary>
        private Vector2 FullFootprint
        {
            get
            {
                var stamped = FootprintSize;
                return stamped.x > 0f && stamped.y > 0f ? stamped : _footprintSize;
            }
        }

        /// <summary>
        /// The pile's world X,Z size right now — the full footprint shrunk to its current
        /// fill step. This is the single source of truth for the root scale, the blocker
        /// and every distance test, so the collider a chicken bumps into and the shape
        /// collection is measured against can never disagree.
        /// </summary>
        public Vector2 CurrentFootprint => FootprintAtStep(FootprintStep());

        /// <summary>
        /// Shortest XZ distance from <paramref name="worldPos"/> to the pile's surface.
        /// 0 when the position is inside the footprint.
        /// </summary>
        public float DistanceToSurface(Vector3 worldPos)
        {
            var size = CurrentFootprint;
            var centre = transform.position;
            return FoodPileMath.DistanceToBoxSurfaceXZ(
                worldPos.x, worldPos.z, centre.x, centre.z, size.x * 0.5f, size.y * 0.5f);
        }

        /// <summary>
        /// True when a chicken standing at <paramref name="worldPos"/> is close enough to
        /// the pile's SURFACE to drain it. Replaces the old centre-distance test, which
        /// silently stopped firing as soon as a pile grew wider than its collect radius.
        /// </summary>
        public bool IsWithinCollectRange(Vector3 worldPos) => DistanceToSurface(worldPos) <= _collectReach;

        /// <summary>
        /// The point a mover should walk to in order to reach this pile: the spot on the
        /// pile's surface nearest <paramref name="from"/>, pushed <paramref name="standoff"/>
        /// outward so the target sits on walkable ground instead of inside the solid blocker.
        /// Never returns NaN and never returns the pile's centre — a mover already inside or
        /// exactly on the footprint is sent out through its nearest face.
        /// </summary>
        public Vector3 SurfaceApproachPoint(Vector3 from, float standoff)
        {
            var centre = transform.position;
            var size = CurrentFootprint;
            float halfX = Mathf.Max(size.x * 0.5f, 0.001f);
            float halfZ = Mathf.Max(size.y * 0.5f, 0.001f);

            float dx = from.x - centre.x;
            float dz = from.z - centre.z;

            // Nearest point of the footprint, in pile-local XZ.
            float nx = Mathf.Clamp(dx, -halfX, halfX);
            float nz = Mathf.Clamp(dz, -halfZ, halfZ);

            // Outward direction = the part of the offset that pokes past the box. It is
            // exactly zero when 'from' is inside the footprint, which is the degenerate
            // case: there is nothing to normalise, so pick the nearest face instead. The
            // >= tie-break is deterministic, so a bot standing dead-centre on a square
            // island still gets one stable answer rather than oscillating.
            float ox = dx - nx;
            float oz = dz - nz;
            float outLength = Mathf.Sqrt(ox * ox + oz * oz);
            if (outLength > 1e-4f)
            {
                ox /= outLength;
                oz /= outLength;
            }
            else if (halfX - Mathf.Abs(dx) <= halfZ - Mathf.Abs(dz))
            {
                ox = dx >= 0f ? 1f : -1f;
                oz = 0f;
                nx = ox * halfX;
            }
            else
            {
                ox = 0f;
                oz = dz >= 0f ? 1f : -1f;
                nz = oz * halfZ;
            }

            float push = Mathf.Max(0f, standoff);
            return new Vector3(centre.x + nx + ox * push, centre.y, centre.z + nz + oz * push);
        }

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
                _log?.Debug(Source, $"{name}: Spawned. Amount={Amount}/{MaxAmount}. " +
                    $"Footprint={FullFootprint.x:0.0}×{FullFootprint.y:0.0} at full. " +
                    $"Permanent={IsPermanent} (floor={DrainFloor:0.0}, regen={_permanentRegenPerSecond}/s).");
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
        /// Solid box + NavMesh carve, built in code so the prefab needs no re-authoring.
        /// Local on every peer — Amount is networked, so all peers agree on whether the
        /// pile blocks. Both shapes are authored as a UNIT box and get their world size
        /// purely from the root's non-uniform scale (see <see cref="ApplyFootprint"/>);
        /// a capsule could not do this, because Unity snaps a non-uniformly scaled
        /// CapsuleCollider to its larger lateral axis and a 7×4 island would come out round.
        /// Centre y = 0.5 puts the box's base on the ground plane.
        /// </summary>
        private void CreateBlocker()
        {
            if (_blocker != null) return;
            _blocker = new GameObject("Blocker");
            _blocker.transform.SetParent(transform, worldPositionStays: false);
            _blocker.transform.localPosition = Vector3.zero;

            var unitCentre = new Vector3(0f, 0.5f, 0f);

            var box = _blocker.AddComponent<BoxCollider>();
            box.size = Vector3.one;
            box.center = unitCentre;

            var carve = _blocker.AddComponent<NavMeshObstacle>();
            carve.shape = NavMeshObstacleShape.Box;
            carve.size = Vector3.one;
            carve.center = unitCentre;
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
        private int FootprintStep() =>
            FoodPileMath.FootprintStep(Amount, MaxAmount, FloorFraction, _footprintSteps);

        /// <summary>Footprint multiplier for a given step. The top step is exactly the authored size.</summary>
        private float FootprintScale(int step) =>
            FoodPileMath.FootprintScale(step, _footprintSteps, _minFootprintScale);

        /// <summary>World X,Z size of the pile at a given fill step.</summary>
        private Vector2 FootprintAtStep(int step)
        {
            float s = FootprintScale(step);
            var full = FullFootprint;
            return new Vector2(full.x * s, full.y * s);
        }

        /// <summary>
        /// Local, driven entirely by the networked <see cref="Amount"/> — every peer computes
        /// the same footprint with no extra state. Early-outs unless the step actually changed:
        /// the root scale drives the carving NavMeshObstacle, and re-carving per frame would
        /// thrash <c>NavMesh.CalculatePath</c> for the bots.
        /// </summary>
        /// <remarks>
        /// The scale is NON-uniform: X and Z carry the footprint, Y carries the fixed
        /// blocker height. Draining a pile therefore shrinks its footprint without also
        /// flattening (or, for the centre island, without turning it into a tower).
        /// </remarks>
        private void ApplyFootprint()
        {
            int step = FootprintStep();
            if (step == _appliedFootprintStep) return;
            _appliedFootprintStep = step;

            var size = FootprintAtStep(step);
            transform.localScale = new Vector3(size.x, _blockerHeight, size.y);

            // Empty piles stop blocking (the leftover stub is walkable, and the
            // NavMesh un-carves); refilled piles (match restart) turn solid again.
            // A permanent pile never reaches step 0, so it always blocks.
            bool shouldBlock = step > 0;
            if (_blocker != null && _blocker.activeSelf != shouldBlock)
                _blocker.SetActive(shouldBlock);

            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
            {
                _log.Verbose(Source, $"{name}: footprint step {step}/{Steps} " +
                    $"({size.x:0.00}×{size.y:0.00} world, blocking={shouldBlock}) at {Amount:0.0}/{MaxAmount}.");
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

            float before = Amount;
            float next = FoodPileMath.Drain(before, MaxAmount, FloorFraction, amount);
            if (Mathf.Approximately(next, before)) return;

            Amount = next;

            float actual = before - next;
            float floor = DrainFloor;

            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose))
            {
                _log.Verbose(Source, $"{name}: drained {actual:0.00} → {Amount:0.0}/{MaxAmount} (floor {floor:0.0}).");
            }
        }
    }
}
