using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A20 (architecture audit 2026-11-12): one home for ground-plane maths. The class replaced eight private copies;
    /// these tests pin that it answers exactly as each copy did, and that no copy comes back.
    /// </summary>
    public sealed class PlanarTests
    {
        private static readonly Vector3[] Points =
        {
            Vector3.zero, new Vector3(1f, 5f, 0f), new Vector3(-21.8f, 0.95f, 21.8f), new Vector3(3.25f, -2f, -7.125f),
            new Vector3(0.0001f, 9f, 0.0002f), new Vector3(25.65f, 1.1f, -25.65f), new Vector3(-0.3f, 0f, 0.4f),
        };

        // The formulas the copies used, written out once more here on purpose.
        private static float OldSqr(Vector3 a, Vector3 b) { float dx = a.x - b.x; float dz = a.z - b.z; return dx * dx + dz * dz; }
        private static float OldDistance(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        private static float OldDistanceReversed(Vector3 a, Vector3 b) { float dx = b.x - a.x, dz = b.z - a.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        private static float OldInline(Vector3 a, Vector3 v) => Mathf.Sqrt((a.x - v.x) * (a.x - v.x) + (a.z - v.z) * (a.z - v.z));

        [Test]
        public void Distance_AndSqrDistance_AnswerExactlyAsEveryOldCopyDid()
        {
            foreach (var a in Points)
            foreach (var b in Points)
            {
                Assert.AreEqual(OldSqr(a, b), Planar.SqrDistance(a, b), 0f, $"{a} {b}");
                Assert.AreEqual(OldDistance(a, b), Planar.Distance(a, b), 0f, $"{a} {b}");
                Assert.AreEqual(OldDistanceReversed(a, b), Planar.Distance(a, b), 0f, $"FloorArrowRules' b - a form, {a} {b}");
                Assert.AreEqual(OldInline(a, b), Planar.Distance(a, b), 0f, $"the control / execute claim form, {a} {b}");
                Assert.AreEqual(Mathf.Sqrt(OldSqr(a, b)), Planar.Distance(a, b), 0f, "ChickenCargo's Sqrt(HorizontalSqr)");
            }
        }

        [Test]
        public void Height_NeverCounts()
        {
            Assert.AreEqual(1f, Planar.Distance(new Vector3(0f, 5f, 0f), new Vector3(1f, 0f, 0f)), 1e-6f);
            Assert.AreEqual(new Vector3(2f, 0f, -3f), Planar.Flat(new Vector3(2f, 7f, -3f)));
        }

        [Test]
        public void Forward_IsFlatAndUnit_AndWorldForwardWhenThereIsNothingToFlatten()
        {
            Assert.AreEqual(Vector3.forward, Planar.Forward(Vector3.up));
            Assert.AreEqual(Vector3.forward, Planar.Forward(new Vector3(0.005f, 1f, 0.005f)), "below the 0.0001 squared threshold");
            var f = Planar.Forward(new Vector3(3f, 9f, 4f));
            Assert.AreEqual(new Vector3(3f, 0f, 4f).normalized, f);
            Assert.AreEqual(1f, f.magnitude, 1e-6f);
        }

        [Test]
        public void DirectionOr_IsTheOldPushAwayBlock()
        {
            // The block six shoves carried:  dir = to - from; dir.y = 0; if (dir.sqrMagnitude < 0.001f) dir = fallback; dir.normalized
            Vector3 Old(Vector3 from, Vector3 to, Vector3 fallback)
            {
                var dir = to - from;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = fallback;
                return dir.normalized;
            }
            var fallback = new Vector3(0.6f, 0.1f, 0.8f);   // a chicken's forward, not flattened by the callers
            foreach (var a in Points)
            foreach (var b in Points)
                Assert.AreEqual(Old(a, b, fallback), Planar.DirectionOr(a, b, fallback), $"{a} {b}");

            Assert.AreEqual(fallback.normalized, Planar.DirectionOr(Points[1], Points[1], fallback), "on the same spot: the fallback");
            Assert.AreEqual(fallback.normalized, Planar.DirectionOr(Vector3.zero, new Vector3(0.02f, 3f, 0.02f), fallback), "only height apart");
            Assert.AreEqual(Vector3.right, Planar.DirectionOr(Vector3.zero, new Vector3(4f, 2f, 0f), fallback));
        }

        [Test]
        public void NoRuntimeFile_DefinesItsOwnPlanarHelper()
        {
            // A definition, private or not, whose body is not a one-line forward to Planar.
            var definition = new Regex(@"static\s+(float|Vector3)\s+(Planar\w*|Horizontal\w*|Flatten\w*|FlatDistance\w*)\s*\([^)]*\)\s*(=>(?!\s*Planar\.)|\{)");
            var hits = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => !p.EndsWith("/Geometry/Planar.cs"))
                .Where(p => definition.IsMatch(RuntimeSourceGuardTests.Code(p)))
                .ToList();
            Assert.IsEmpty(hits, "A private planar helper. Use CluckWars.Planar (docs/CONVENTIONS.md):\n" + string.Join("\n", hits));
        }

        [Test]
        public void NoRuntimeFile_FlattensAVectorInline()
        {
            // `v.y = 0f;` is Planar.Flat(v) written out. 26 of them were replaced in batch 6; the
            // expression is identical, so this guard costs nothing and stops the 27th.
            var inlineFlatten = new Regex(@"\.y\s*=\s*0f?\s*;");
            var hits = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => !p.EndsWith("/Geometry/Planar.cs"))
                .Where(p => inlineFlatten.IsMatch(RuntimeSourceGuardTests.Code(p)))
                .ToList();
            Assert.IsEmpty(hits, "An inline height flatten. Use Planar.Flat (docs/CONVENTIONS.md):\n" + string.Join("\n", hits));
        }

    }
}
