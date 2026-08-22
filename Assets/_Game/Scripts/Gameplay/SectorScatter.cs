using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// What a piece of scatter cover is FOR. <b>This is a span band and a placement role —
    /// it is not a height class.</b> GDD §3.5 (locked 2026-07-29) deleted height classes
    /// outright: "All jumps are teleports. There are no obstacle height classes." Traversal
    /// is gated purely by an obstacle's footprint span along the jump direction, so the only
    /// thing that can distinguish two pieces of terrain is how wide they are and where they
    /// sit. Every scatter obstacle is built at the same height as a pinwheel arm.
    /// </summary>
    public enum ScatterRole : byte
    {
        /// <summary>
        /// Small crate. Span stays under <c>JumpResolver.ShortDistance − 0.8</c>, so <b>every</b>
        /// jump tier clears it square-on. Its job is breaking sightlines and running lines,
        /// not gating traversal.
        /// </summary>
        Cover = 0,

        /// <summary>
        /// Short wall stub — same thickness as a pinwheel arm, so it reads as the same
        /// material. Span is drawn ABOVE <c>JumpResolver.ShortDistance − 0.8</c>, so a Short
        /// (5 m) jump cannot cross it square-on while a Normal (10 m) always can. This is the
        /// only span-derived tier boundary in the shipped map; see <see cref="MinBarrierSpan"/>.
        /// </summary>
        Barrier = 1,
    }

    /// <summary>One placed scatter obstacle, as an oriented box on the XZ plane.</summary>
    public readonly struct ScatterBox
    {
        /// <summary>XZ centre. (<c>x</c> is world X, <c>y</c> is world Z.)</summary>
        public readonly Vector2 Center;
        /// <summary>Extent along the box's own long axis.</summary>
        public readonly float Span;
        /// <summary>Extent across the box's short axis.</summary>
        public readonly float Depth;
        /// <summary>Rotation of the long axis away from world +X, degrees CCW in the XZ plane.</summary>
        public readonly float YawDegrees;
        public readonly ScatterRole Role;

        public ScatterBox(Vector2 center, float span, float depth, float yawDegrees, ScatterRole role)
        {
            Center = center;
            Span = span;
            Depth = depth;
            YawDegrees = yawDegrees;
            Role = role;
        }

        /// <summary>
        /// This box rotated a quarter turn about the arena centre.
        /// <para>
        /// <b>Exact in floating point.</b> A quarter turn is
        /// <c>(x, z) → (−z, x)</c> and <c>yaw → yaw + 90</c> — sign flips and an integer
        /// addition, no trigonometry, no rounding. That exactness is what lets
        /// <see cref="SectorScatter"/>'s 4-fold symmetry be asserted with
        /// <c>Assert.AreEqual</c> rather than a tolerance, i.e. proven by construction
        /// instead of observed from a lucky seed.
        /// </para>
        /// </summary>
        public ScatterBox RotatedQuarterTurn() =>
            new ScatterBox(new Vector2(-Center.y, Center.x), Span, Depth, YawDegrees + 90f, Role);

        /// <summary>Radius of the circle that circumscribes this footprint.</summary>
        public float CircumRadius => 0.5f * Mathf.Sqrt(Span * Span + Depth * Depth);
    }

    /// <summary>
    /// Everything <see cref="SectorScatter.Build"/> needs, grouped so the call site reads as
    /// a recipe rather than a nine-argument positional puzzle.
    /// </summary>
    public readonly struct ScatterSettings
    {
        public readonly int CoverPerSector;
        public readonly Vector2 CoverSpanRange;
        public readonly int BarrierPerSector;
        public readonly Vector2 BarrierSpanRange;
        /// <summary>Barrier short axis. Passed the arm thickness so stubs read as wall, not as crate.</summary>
        public readonly float BarrierDepth;
        /// <summary>Walkable lane kept between a scatter footprint's SURFACE and everything else.</summary>
        public readonly float Clearance;
        /// <summary>Rejection-sampling budget per requested obstacle.</summary>
        public readonly int AttemptsPerItem;

        public ScatterSettings(int coverPerSector, Vector2 coverSpanRange,
                               int barrierPerSector, Vector2 barrierSpanRange, float barrierDepth,
                               float clearance, int attemptsPerItem)
        {
            CoverPerSector = coverPerSector;
            CoverSpanRange = coverSpanRange;
            BarrierPerSector = barrierPerSector;
            BarrierSpanRange = barrierSpanRange;
            BarrierDepth = barrierDepth;
            Clearance = clearance;
            AttemptsPerItem = attemptsPerItem;
        }
    }

    /// <summary>
    /// Scatter cover for the arena interior, <b>provably 4-fold symmetric by construction</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces <c>MapGenerator.PlaceObstacleClass</c>, which was dead code and could
    /// not have shipped as written: it rejection-sampled uniformly over the WHOLE arena
    /// square. A shared seed therefore gave every peer the same map but <b>not every player
    /// the same sector</b>, so one corner could draw three crates and another none. In a
    /// 4-player FFA that is a fairness bug, and it is the same invariant
    /// <c>PinwheelLayoutTests.Build_IsFourFoldSymmetric_SoEverySectorIsIdentical</c> already
    /// pins for the walls.
    /// </para>
    /// <para>
    /// <b>The construction.</b> Candidates are sampled only in the fundamental domain
    /// <c>[0, half] × [0, half]</c> — the +X/+Z quadrant. Four quarter-turns of that square
    /// tile the arena exactly, with no overlap and no gap, so it is a true fundamental domain
    /// for the symmetry group. Each accepted candidate is emitted together with its three
    /// quarter-turn images (<see cref="ScatterBox.RotatedQuarterTurn"/>), consecutively, so
    /// the output array is symmetric by the shape of the loop rather than by the luck of the
    /// draw. Acceptance is tested against every one of the four images, so a candidate near
    /// the <c>x = 0</c> or <c>z = 0</c> seam cannot collide with its own reflection.
    /// </para>
    /// <para>
    /// <b>Nothing here may seal a route.</b> Every acceptance test is surface-to-surface with
    /// <see cref="ScatterSettings.Clearance"/> (fed <see cref="PinwheelLayout.MinCorridorWidth"/>)
    /// to spare, against the boundary, the pinwheel arms, every keep-clear disc and every
    /// other placed item. The corner pockets behind the bases are excluded <i>structurally</i>
    /// rather than by a special case: the real gap between a base's keep-clear disc and the
    /// arena corner is ~1.4 m, under MinCorridorWidth, so no footprint can satisfy both the
    /// disc rule and the boundary rule there. ADR 0003 Decision 6's "reach into the dead
    /// corner pockets" is stale advice — measured, they are not usable.
    /// </para>
    /// <para>
    /// Placement is <b>best-effort</b>, exactly like the path it replaces: if the map cannot
    /// hold the requested density the builder places fewer and reports it through
    /// <see cref="Result.Requested"/> vs <see cref="Result.PlacedPerSector"/> rather than
    /// quietly relaxing a clearance.
    /// </para>
    /// </remarks>
    public static class SectorScatter
    {
        /// <summary>
        /// Smallest span that a Short (5 m) jump cannot cross square-on, per GDD §3.5's
        /// "span needed = obstacle width + 0.8 m". Anything at or above this is a real
        /// traversal gate; anything below it is cover only.
        /// </summary>
        public static float MinBarrierSpan =>
            JumpResolver.ShortDistance - 2f * JumpResolver.BodyClearance;

        /// <summary>What <see cref="Build"/> produced, and what it could not.</summary>
        public readonly struct Result
        {
            public readonly ScatterBox[] Boxes;
            /// <summary>Per-sector counts actually achieved, indexed by <see cref="ScatterRole"/>.</summary>
            public readonly int CoverPlacedPerSector;
            public readonly int BarrierPlacedPerSector;
            public readonly int CoverRequestedPerSector;
            public readonly int BarrierRequestedPerSector;

            public Result(ScatterBox[] boxes, int coverPlaced, int barrierPlaced,
                          int coverRequested, int barrierRequested)
            {
                Boxes = boxes;
                CoverPlacedPerSector = coverPlaced;
                BarrierPlacedPerSector = barrierPlaced;
                CoverRequestedPerSector = coverRequested;
                BarrierRequestedPerSector = barrierRequested;
            }

            public bool UnderPlaced =>
                CoverPlacedPerSector < CoverRequestedPerSector ||
                BarrierPlacedPerSector < BarrierRequestedPerSector;
        }

        /// <param name="arenaHalfSize">Half the square arena's side length.</param>
        /// <param name="seed">RNG seed — identical on every peer for online matches.</param>
        /// <param name="arms">The pinwheel arms already placed. Scatter routes around them.</param>
        /// <param name="armThickness">Arm short-axis extent, so arm clearance is surface-to-surface.</param>
        /// <param name="centerKeepClear">Radius of the centre pile's footprint.</param>
        /// <param name="keepClearDiscs">Bases and outer piles. Same array the pinwheel is fed.</param>
        public static Result Build(
            float arenaHalfSize,
            int seed,
            in ScatterSettings settings,
            WallSegment[] arms,
            float armThickness,
            float centerKeepClear,
            KeepClearDisc[] keepClearDiscs)
        {
            var placed = new List<ScatterBox>();

            // Barriers first, deliberately: they are the larger footprint and the harder fit,
            // so letting crates claim the roomy spots first would starve them. Fixed order
            // also keeps the RNG stream identical on every peer.
            int barrierPlaced = PlaceRole(
                ScatterRole.Barrier, settings.BarrierPerSector, settings.BarrierSpanRange,
                barrierDepth: settings.BarrierDepth,
                arenaHalfSize, seed * 2 + 1, settings, arms, armThickness, centerKeepClear,
                keepClearDiscs, placed);

            int coverPlaced = PlaceRole(
                ScatterRole.Cover, settings.CoverPerSector, settings.CoverSpanRange,
                barrierDepth: 0f,   // 0 => depth is drawn from the span range too (a crate)
                arenaHalfSize, seed * 2 + 2, settings, arms, armThickness, centerKeepClear,
                keepClearDiscs, placed);

            return new Result(placed.ToArray(), coverPlaced, barrierPlaced,
                              settings.CoverPerSector, settings.BarrierPerSector);
        }

        private static int PlaceRole(
            ScatterRole role, int perSector, Vector2 spanRange, float barrierDepth,
            float arenaHalfSize, int seed, in ScatterSettings settings,
            WallSegment[] arms, float armThickness, float centerKeepClear,
            KeepClearDisc[] keepClearDiscs, List<ScatterBox> placed)
        {
            if (perSector <= 0) return 0;

            var rng = new System.Random(seed);
            int budget = perSector * Mathf.Max(1, settings.AttemptsPerItem);
            int made = 0;

            for (int attempt = 0; attempt < budget && made < perSector; attempt++)
            {
                // Draw the COMPLETE sample before any rejection test: the RNG stream must
                // advance by exactly four draws per attempt on every peer, or two clients
                // sharing a seed walk different sequences and build different maps.
                float span  = Mathf.Lerp(spanRange.x, spanRange.y, (float)rng.NextDouble());
                float depth = barrierDepth > 0f
                    ? barrierDepth
                    : Mathf.Lerp(spanRange.x, spanRange.y, (float)rng.NextDouble());
                float x   = (float)rng.NextDouble() * arenaHalfSize;   // fundamental domain:
                float z   = (float)rng.NextDouble() * arenaHalfSize;   // the +X/+Z quadrant
                float yaw = (float)rng.NextDouble() * 180f;

                var candidate = new ScatterBox(new Vector2(x, z), span, depth, yaw, role);

                // Test the candidate AND its three quarter-turn images. A candidate hugging
                // the x=0 or z=0 seam is close to its own rotated copy, and only the copies
                // reveal that.
                var quad = Quadruple(candidate);
                bool ok = true;
                for (int q = 0; q < 4 && ok; q++)
                {
                    ok = IsPlaceable(quad[q], arenaHalfSize, settings.Clearance,
                                     arms, armThickness, centerKeepClear, keepClearDiscs, placed)
                         && KeepsClearOfSiblings(quad, q, settings.Clearance);
                }
                if (!ok) continue;

                placed.AddRange(quad);
                made++;
            }

            return made;
        }

        /// <summary>The four quarter-turn images of one candidate, in rotation order.</summary>
        private static ScatterBox[] Quadruple(ScatterBox seed)
        {
            var quad = new ScatterBox[4];
            quad[0] = seed;
            for (int i = 1; i < 4; i++) quad[i] = quad[i - 1].RotatedQuarterTurn();
            return quad;
        }

        private static bool KeepsClearOfSiblings(ScatterBox[] quad, int index, float clearance)
        {
            for (int i = 0; i < quad.Length; i++)
            {
                if (i == index) continue;
                if (!CircumDiscsClear(quad[index], quad[i], clearance)) return false;
            }
            return true;
        }

        /// <summary>
        /// Item-to-item spacing uses CIRCUMSCRIBED DISCS rather than exact box separation.
        /// Deliberately conservative: it can only ever push two crates further apart than
        /// strictly necessary, never closer, and it removes an entire class of
        /// oriented-box-overlap bugs from the one test where being a little sparse costs
        /// nothing. The tests that actually constrain the map — boundary, arms, keep-clear
        /// discs — use exact surface distances.
        /// </summary>
        private static bool CircumDiscsClear(in ScatterBox a, in ScatterBox b, float clearance) =>
            Vector2.Distance(a.Center, b.Center) >= a.CircumRadius + b.CircumRadius + clearance;

        private static bool IsPlaceable(
            in ScatterBox box, float arenaHalfSize, float clearance,
            WallSegment[] arms, float armThickness, float centerKeepClear,
            KeepClearDisc[] keepClearDiscs, List<ScatterBox> placed)
        {
            // Boundary: exact axis-aligned extents of the rotated box. A lane of at least
            // `clearance` survives along the rim, which is GDD §3.4's "safe lap".
            float rad = box.YawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(rad)), s = Mathf.Abs(Mathf.Sin(rad));
            float hx = 0.5f * (c * box.Span + s * box.Depth);
            float hz = 0.5f * (s * box.Span + c * box.Depth);
            float limit = arenaHalfSize - clearance;
            if (Mathf.Abs(box.Center.x) + hx > limit) return false;
            if (Mathf.Abs(box.Center.y) + hz > limit) return false;

            // Centre pile.
            if (DistanceToSurface(Vector2.zero, box) < centerKeepClear + clearance) return false;

            // Bases and outer piles. Exact point-to-surface, so a long stub is judged on its
            // real footprint rather than a bounding circle.
            if (keepClearDiscs != null)
            {
                for (int i = 0; i < keepClearDiscs.Length; i++)
                {
                    var disc = keepClearDiscs[i];
                    if (DistanceToSurface(disc.Center, box) < disc.Radius + clearance) return false;
                }
            }

            // Pinwheel arms. Sampled along each arm's centreline and reduced by its half
            // thickness, so the comparison is surface-to-surface at both ends.
            if (arms != null)
            {
                float armHalf = armThickness * 0.5f;
                for (int i = 0; i < arms.Length; i++)
                {
                    if (arms[i].A == arms[i].B) continue;   // collapsed arm — nothing built
                    if (SegmentToSurface(arms[i].A, arms[i].B, box) < armHalf + clearance) return false;
                }
            }

            for (int i = 0; i < placed.Count; i++)
            {
                if (!CircumDiscsClear(box, placed[i], clearance)) return false;
            }

            return true;
        }

        /// <summary>
        /// Shortest XZ distance from <paramref name="point"/> to the surface of an oriented
        /// box. 0 when the point is inside.
        /// </summary>
        private static float DistanceToSurface(Vector2 point, in ScatterBox box)
        {
            float rad = box.YawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float dx = point.x - box.Center.x, dz = point.y - box.Center.y;

            // World -> box-local (yaw only; scatter never pitches or rolls).
            float lx =  dx * c + dz * s;
            float lz = -dx * s + dz * c;

            float ox = Mathf.Max(Mathf.Abs(lx) - box.Span * 0.5f, 0f);
            float oz = Mathf.Max(Mathf.Abs(lz) - box.Depth * 0.5f, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        /// <summary>
        /// Shortest distance from a segment to a box surface, by dense sampling along the
        /// segment. Sampling (rather than a closed-form segment/OBB solve) is chosen because
        /// it is obviously correct and the step is far finer than the clearance it feeds:
        /// a 0.2 m step against a 2.0 m clearance cannot miss a violation that matters.
        /// </summary>
        private static float SegmentToSurface(Vector2 a, Vector2 b, in ScatterBox box)
        {
            const float step = 0.2f;
            float length = Vector2.Distance(a, b);
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / step));

            float best = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float d = DistanceToSurface(Vector2.Lerp(a, b, i / (float)samples), box);
                if (d < best) best = d;
                if (best <= 0f) break;
            }
            return best;
        }
    }
}
