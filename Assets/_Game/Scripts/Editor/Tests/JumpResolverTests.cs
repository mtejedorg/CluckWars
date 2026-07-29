using System;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class JumpResolverTests
    {
        private const float ArenaHalfSize = 19.0f;
        private const float BodyClearance = 0.4f;

        [Test]
        public void ShortJump5m_ClearsSquareOnWall_0_5m()
        {
            // Wall square-on at z = 2.0m to 2.5m (thickness 0.5m).
            // Span needed = 0.5 + 0.8 = 1.3m (blocked from z = 1.6m to 2.9m).
            Func<Vector3, float, bool> isBlocked = (p, r) => p.z >= 1.6f - r && p.z <= 2.9f + r;

            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);

            Assert.IsTrue(res.Cleared, "5m jump must clear 0.5m wall (needs 1.3m)");
            Assert.AreEqual(5.0f, res.LandingPoint.z, 0.01f);
        }

        [Test]
        public void ShortJump5m_ClearsObliqueWall_20Deg()
        {
            // 20° oblique wall. Needed span ~2.3m (blocked from z = 1.6m to 3.9m).
            Func<Vector3, float, bool> isBlocked = (p, r) => p.z >= 1.6f - r && p.z <= 3.9f + r;

            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlocked);

            Assert.IsTrue(res.Cleared, "5m jump must clear 20° oblique wall (needs 2.3m)");
        }

        [Test]
        public void ShortJump5m_FailsT1Pile_5_5m_AndStopsFlush()
        {
            // T1 pile 5.5m footprint (blocked from z = 0.6m to 6.9m; needed span 6.3m).
            Func<Vector3, float, bool> isBlocked = (p, r) => p.z >= 0.6f && p.z <= 6.9f;

            // Near face of the pile sits 1.0 m along the ray; the jump must stop flush
            // at 1.0 - 0.4 (body clearance) = 0.6 m.
            NearFaceQuery nearFace =
                (Vector3 orig, Vector3 dir, float maxDist, float clearance, out float hitDist) =>
                {
                    hitDist = 1.0f;
                    return true;
                };

            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.ShortDistance,
                ArenaHalfSize, BodyClearance, isBlocked, nearFace);

            Assert.IsFalse(res.Cleared, "5m jump must fail a 5.5m T1 pile (needs 6.3m)");
            Assert.AreEqual(0.6f, res.LandingPoint.z, 0.01f, "Failed jump must stop flush at near face (1.0m - 0.4m body clearance = 0.6m)");
        }

        [Test]
        public void NormalJump10m_ClearsT1AndT2_FailsCentrePile12m()
        {
            // T1 pile 5.5m footprint (needs 6.3m): 10m clears!
            Func<Vector3, float, bool> isBlockedT1 = (p, r) => p.z >= 0.6f && p.z <= 6.9f;
            var resT1 = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.NormalDistance, ArenaHalfSize, BodyClearance, isBlockedT1);
            Assert.IsTrue(resT1.Cleared, "10m jump clears T1 (6.3m)");

            // T2 pile 6.5m footprint (needs 7.3m): 10m clears!
            Func<Vector3, float, bool> isBlockedT2 = (p, r) => p.z >= 0.6f && p.z <= 7.9f;
            var resT2 = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.NormalDistance, ArenaHalfSize, BodyClearance, isBlockedT2);
            Assert.IsTrue(resT2.Cleared, "10m jump clears T2 (7.3m)");

            // Full 12m Centre pile (needs 12.8m, blocked z = 0.6m to 13.4m): 10m fails!
            Func<Vector3, float, bool> isBlockedCentre = (p, r) => p.z >= 0.6f && p.z <= 13.4f;
            var resCentre = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.NormalDistance, ArenaHalfSize, BodyClearance, isBlockedCentre);
            Assert.IsFalse(resCentre.Cleared, "10m jump fails full 12m centre pile (needs 12.8m)");
        }

        [Test]
        public void BigJump18m_ClearsFullCentrePile12m()
        {
            // Full 12m Centre pile (needs 12.8m, blocked z = 0.6m to 13.4m): 18m clears!
            Func<Vector3, float, bool> isBlockedCentre = (p, r) => p.z >= 0.6f && p.z <= 13.4f;
            var res = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.BigDistance, ArenaHalfSize, BodyClearance, isBlockedCentre);
            Assert.IsTrue(res.Cleared, "18m jump clears full 12m centre pile (needs 12.8m)");
            Assert.AreEqual(18.0f, res.LandingPoint.z, 0.01f);
        }

        [Test]
        public void Tolerance_ShortBy0_4mSucceeds_ShortBy1_2mFails()
        {
            // 5m jump: max extension is max(0.6m, 0.4m) capped at 0.8m -> 0.6m.
            // Obstacle ends at 5.3m (nominal 5.0m is short by 0.3m).
            Func<Vector3, float, bool> isBlockedShort = (p, r) => p.z >= 1.0f && p.z <= 5.3f;
            var resSucceed = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlockedShort);
            Assert.IsTrue(resSucceed.Cleared, "Jump short by 0.3m/0.4m succeeds via tolerance extension");

            // 10m jump: max extension capped at 0.8m absolute.
            // Obstacle ends at 11.2m (nominal 10.0m is short by 1.2m).
            Func<Vector3, float, bool> isBlockedLong = (p, r) => p.z >= 1.0f && p.z <= 11.2f;
            var resFail = JumpResolver.Resolve(Vector3.zero, Vector3.forward, JumpResolver.NormalDistance, ArenaHalfSize, BodyClearance, isBlockedLong);
            Assert.IsFalse(resFail.Cleared, "Jump short by 1.2m fails (0.8m absolute cap)");
        }

        [Test]
        public void ChordNearRoundPileEdge_ClearsWhenThroughMiddleFails()
        {
            // Round pile centered at (0, 3) with radius 3m.
            // Through middle (x=0): blocked z from 0 to 6 (needs 6.8m) -> 5m fails!
            Func<Vector3, float, bool> isBlockedMiddle = (p, r) =>
            {
                float dx = p.x, dz = p.z - 3.0f;
                return (dx * dx + dz * dz) <= (3.0f + r) * (3.0f + r);
            };

            var resMiddle = JumpResolver.Resolve(new Vector3(0, 0, 0), Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlockedMiddle);
            Assert.IsFalse(resMiddle.Cleared, "5m jump through middle of 6m round pile fails");

            // Chord line near edge (x = 2.6m): chord length through circle is much smaller (~3m).
            var resEdge = JumpResolver.Resolve(new Vector3(2.6f, 0, 0), Vector3.forward, JumpResolver.ShortDistance, ArenaHalfSize, BodyClearance, isBlockedMiddle);
            Assert.IsTrue(resEdge.Cleared, "5m jump along chord near round pile's edge clears");
        }
    }
}
