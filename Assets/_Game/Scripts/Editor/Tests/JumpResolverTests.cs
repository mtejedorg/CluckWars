using System;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class JumpResolverTests
    {
        /// <summary>
        /// The SHIPPED arena's half-size, read from <c>Game.unity</c> rather than restated.
        /// Was a literal 19.0f, which quietly encoded a 38 m arena and would have kept
        /// testing the old square after the x1.35 rescale on 2026-08-14.
        /// </summary>
        /// <remarks>
        /// None of these cases actually approach the boundary — the furthest landing is
        /// ~18.8 m — so the arena size is a backdrop here, not the thing under test. It is
        /// read from the scene anyway so that a future arena change cannot leave a stale
        /// number sitting in a test file, which is how this project has been bitten before.
        /// </remarks>
        private static float ArenaHalfSize => TestAssets.SceneFloat("_planeSize") * 0.5f;
        private const float BodyClearance = 0.4f;

        /// <summary>
        /// A Short jump clears a wall square-on. The wall is placed RELATIVE to the tier's
        /// reach, not at a fixed z — the previous version pinned it to a range calibrated for
        /// a 5 m Short and started failing the moment the tiers were rescaled, reporting a
        /// resolver bug where there was only a stale fixture.
        /// </summary>
        [Test]
        public void ShortJump_ClearsASquareOnWall()
        {
            float thickness = ShippedMap.WallThickness;
            float nearFace = 0.25f * JumpResolver.ShortDistance;
            float farFace = nearFace + thickness;

            Func<Vector3, float, bool> isBlocked = (p, r) => p.z >= nearFace - r && p.z <= farFace + r;

            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward,
                JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);

            Assert.IsTrue(res.Cleared,
                $"A Short jump ({JumpResolver.ShortDistance:0.00} m) must clear a " +
                $"{thickness:0.00} m wall whose far edge plus clearance sits at " +
                $"{farFace + BodyClearance:0.00} m.");
            Assert.AreEqual(JumpResolver.ShortDistance, res.LandingPoint.z, 0.01f,
                "It should land at its nominal distance, not need the tolerance extension — " +
                "a wall is the obstacle this tier exists to cross comfortably.");
        }

        /// <summary>
        /// Same wall at 20 degrees, which lengthens the span the jump must cross to
        /// <c>thickness / sin(20 deg)</c>. Still comfortably inside a Short jump.
        /// </summary>
        [Test]
        public void ShortJump_ClearsAnObliqueWall_20Deg()
        {
            float thickness = ShippedMap.WallThickness;
            float obliqueSpan = thickness / Mathf.Sin(20f * Mathf.Deg2Rad);
            float nearFace = 0.1f * JumpResolver.ShortDistance;
            float farFace = nearFace + obliqueSpan;

            Func<Vector3, float, bool> isBlocked = (p, r) => p.z >= nearFace - r && p.z <= farFace + r;

            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward,
                JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);

            Assert.IsTrue(res.Cleared,
                $"A Short jump ({JumpResolver.ShortDistance:0.00} m) must clear a wall taken at " +
                $"20 deg, span {obliqueSpan:0.00} m, far edge plus clearance at " +
                $"{farFace + BodyClearance:0.00} m. Obliqueness is the worst case for crossing a " +
                "wall, and the Short tier's whole job is that walls are never a barrier.");
        }

        /// <summary>
        /// The traversal ladder of GDD 3.6 — "the map opens itself in stages" — asserted
        /// against the pile footprints the game ACTUALLY builds.
        /// </summary>
        /// <remarks>
        /// This replaces three tests that hardcoded 5.5 / 6.5 / 12 m spans. Those numbers were
        /// the pre-2026-08-13 footprints; the piles were shrunk 35% and the literals were not,
        /// so the tests kept passing while the real ladder silently collapsed — every tier was
        /// clearing what the tier above it was meant to gate. Maestro reported it as "the jump
        /// is still too high" on 2026-08-20.
        ///
        /// Reading the live footprints via <see cref="ShippedMap"/> means the gates are a
        /// RELATIONSHIP between tiers and obstacles. Rescale either side alone and this goes
        /// red, which is precisely what the literals failed to do.
        /// </remarks>
        [Test]
        public void TraversalLadder_HoldsAgainstTheShippedPileFootprints()
        {
            // Worst case is the pile's LONG axis: the widest span a jump can be asked to cross.
            float t1     = Mathf.Max(ShippedMap.PersonalPileFootprint.x,  ShippedMap.PersonalPileFootprint.y);
            float t2     = Mathf.Max(ShippedMap.ContestedPileFootprint.x, ShippedMap.ContestedPileFootprint.y);
            float centre = Mathf.Max(ShippedMap.CentrePileFootprint.x,    ShippedMap.CentrePileFootprint.y);
            float arm    = ShippedMap.WallThickness;

            // (tier, span, must clear?) — the whole ladder in one table.
            var cases = new (string tier, float dist, string obstacle, float span, bool expected)[]
            {
                ("Short",  JumpResolver.ShortDistance,  "an arm",         arm,    true),
                ("Short",  JumpResolver.ShortDistance,  "the T1 pile",    t1,     false),
                ("Short",  JumpResolver.ShortDistance,  "the T2 pile",    t2,     false),
                ("Short",  JumpResolver.ShortDistance,  "the centre pile", centre, false),

                ("Normal", JumpResolver.NormalDistance, "an arm",         arm,    true),
                ("Normal", JumpResolver.NormalDistance, "the T1 pile",    t1,     true),
                ("Normal", JumpResolver.NormalDistance, "the T2 pile",    t2,     true),
                ("Normal", JumpResolver.NormalDistance, "the centre pile", centre, false),

                ("Big",    JumpResolver.BigDistance,    "the T1 pile",    t1,     true),
                ("Big",    JumpResolver.BigDistance,    "the T2 pile",    t2,     true),
                ("Big",    JumpResolver.BigDistance,    "the centre pile", centre, true),
            };

            var wrong = new System.Collections.Generic.List<string>();
            foreach (var c in cases)
            {
                // Obstacle straddling the ray, near face 1 m out, so the jump must cross its
                // full span plus a body clearance each side.
                float near = 1.0f, far = near + c.span;
                Func<Vector3, float, bool> blocked =
                    (pt, r) => pt.z >= near - r && pt.z <= far + r;

                var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward, c.dist,
                                               ArenaHalfSize, BodyClearance, blocked);

                if (res.Cleared != c.expected)
                    wrong.Add($"{c.tier} ({c.dist:0.00} m) {(res.Cleared ? "CLEARS" : "fails")} " +
                              $"{c.obstacle} (span {c.span:0.00}, needs {c.span + 2f * BodyClearance:0.00}) " +
                              $"— expected {(c.expected ? "to clear" : "to fail")}");
            }

            Assert.IsEmpty(wrong,
                "The traversal ladder does not match GDD 3.6:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", wrong) + Environment.NewLine +
                "Short crosses walls only; Normal adds T1/T2; Big adds the centre pile. " +
                "If a pile footprint moved, rescale the jump tiers in the SAME commit - the " +
                "ladder is a relationship between the two, not two independent numbers.");
        }

        [Test]
        public void Tolerance_ExtendsInsideTheWindow_ButNotBeyondIt()
        {
            // Derived from the tier under test, not pinned to a tier's current value: an
            // obstacle ending just INSIDE the tolerance window must be cleared by the
            // extension, and one ending just outside it must not.
            float shortExt = JumpResolver.GetMaxExtension(JumpResolver.ShortDistance);
            float justInside = JumpResolver.ShortDistance + shortExt * 0.5f;
            Func<Vector3, float, bool> isBlockedShort = (p, r) => p.z >= 1.0f && p.z <= justInside;
            var resSucceed = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlockedShort);
            Assert.IsTrue(resSucceed.Cleared,
                $"An obstacle ending {shortExt * 0.5f:0.00} m past nominal is inside the " +
                $"{shortExt:0.00} m tolerance window and must be cleared.");

            float normalExt = JumpResolver.GetMaxExtension(JumpResolver.NormalDistance);
            float justOutside = JumpResolver.NormalDistance + normalExt + 0.4f;
            Func<Vector3, float, bool> isBlockedLong = (p, r) => p.z >= 1.0f && p.z <= justOutside;
            var resFail = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.NormalDistance, ArenaHalfSize, BodyClearance, isBlockedLong);
            Assert.IsFalse(resFail.Cleared,
                $"An obstacle ending 0.40 m beyond the {normalExt:0.00} m tolerance window must NOT be cleared.");
        }

        [Test]
        public void ChordNearRoundObstacleEdge_ClearsWhenThroughMiddleFails()
        {
            // Geometry derived from the tier's own reach, not a fixed 6 m pile. The old
            // version hardcoded a radius that happened to straddle a 5 m Short jump; when the
            // tiers were rescaled it asserted something that is simply not true any more.
            //
            // Laid out so the arithmetic is obvious: the obstacle's far edge sits at 1.2x the
            // jump's reach through the middle (must fail), and at 0.85x along a chord offset
            // far enough off-axis (must clear). Both hold for any tier value.
            float reach = JumpResolver.ShortDistance + JumpResolver.GetMaxExtension(JumpResolver.ShortDistance);
            float centreZ = 0.5f * reach;
            float effectiveRadius = 0.7f * reach;              // radius + body clearance
            float radius = effectiveRadius - BodyClearance;
            float chordOffset = Mathf.Sqrt(effectiveRadius * effectiveRadius
                                           - 0.35f * reach * 0.35f * reach);

            Func<Vector3, float, bool> isBlocked = (p, r) =>
            {
                float dx = p.x, dz = p.z - centreZ;
                return (dx * dx + dz * dz) <= (radius + r) * (radius + r);
            };

            var resMiddle = JumpResolver.Resolve(Vector3.zero, Vector3.forward,
                JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);
            Assert.IsFalse(resMiddle.Cleared,
                $"Through the middle the far edge is at {centreZ + effectiveRadius:0.00} m against " +
                $"a reach of {reach:0.00} m, so the jump must fail.");

            var resEdge = JumpResolver.Resolve(new Vector3(chordOffset, 0f, 0f), Vector3.forward,
                JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);
            Assert.IsTrue(resEdge.Cleared,
                $"Along a chord {chordOffset:0.00} m off-axis the obstacle is only " +
                $"{2f * 0.35f * reach:0.00} m deep, well inside the same {reach:0.00} m reach — " +
                "clipping a round obstacle's edge must remain a way through.");
        }
    }
}
