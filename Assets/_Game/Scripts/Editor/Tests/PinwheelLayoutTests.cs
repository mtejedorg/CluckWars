using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class PinwheelLayoutTests
    {
        [Test] public void Build_IsDeterministicForSeed()
        {
            var a = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            var b = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].A, b[i].A);
                Assert.AreEqual(a[i].B, b[i].B);
                Assert.AreEqual(a[i].Class, b[i].Class);
            }
        }

        [Test] public void Build_ProducesOneSegmentPerWedge()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);
        }

        [Test] public void Build_KeepsSegmentsOutOfCenterDisc()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 7, centerKeepClear: 3f, armThickness: 0.5f);
            foreach (var s in segs)
            {
                // Neither endpoint may sit inside the center keep-clear disc.
                Assert.GreaterOrEqual(s.A.magnitude, 3f, "segment endpoint inside center keep-clear");
                Assert.GreaterOrEqual(s.B.magnitude, 3f, "segment endpoint inside center keep-clear");
            }
        }

        [Test] public void Build_ZeroWedgesReturnsEmptyArray()
        {
            var segs = PinwheelLayout.Build(15f, 0, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(0, segs.Length);
        }

        [Test] public void Build_HonorsExtraKeepClearDiscs()
        {
            // A disc sitting right where an arm would otherwise start must push that
            // arm's inner end out past the disc's far edge.
            var discs = new[] { new KeepClearDisc(new Vector2(6f, 0f), 2f) };
            var segs = PinwheelLayout.Build(19f, 8, seed: 3, centerKeepClear: 3f, armThickness: 0.5f, extraKeepClearDiscs: discs);

            foreach (var s in segs)
            {
                if (s.A == s.B) continue; // collapsed/zero-length — nothing to check

                // Sample points along the segment must never land inside the disc.
                for (float t = 0f; t <= 1f; t += 0.1f)
                {
                    Vector2 p = Vector2.Lerp(s.A, s.B, t);
                    float distToDisc = Vector2.Distance(p, discs[0].Center);
                    Assert.GreaterOrEqual(distToDisc, discs[0].Radius - 0.01f,
                        $"segment sample point {p} intrudes into keep-clear disc at {discs[0].Center} r={discs[0].Radius}");
                }
            }
        }

        [Test] public void Build_8SectorRadialTopology_AlternatesOuterAndInnerGaps()
        {
            var segs = PinwheelLayout.Build(19f, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);

            for (int i = 0; i < 8; i++)
            {
                float rStart = segs[i].A.magnitude;
                float rEnd = segs[i].B.magnitude;
                float length = rEnd - rStart;

                if (i % 2 == 0)
                {
                    // Outer-gap wall: r 10m -> 17.6m (length 7.6m)
                    Assert.AreEqual(10.0f, rStart, 0.1f, $"Outer-gap wall {i} start radius");
                    Assert.AreEqual(17.6f, rEnd, 0.1f, $"Outer-gap wall {i} end radius");
                    Assert.AreEqual(7.6f, length, 0.1f, $"Outer-gap wall {i} length");
                }
                else
                {
                    // Inner-gap wall: r 12m -> 20.6m (length 8.6m)
                    Assert.AreEqual(12.0f, rStart, 0.1f, $"Inner-gap wall {i} start radius");
                    Assert.AreEqual(20.56f, rEnd, 0.2f, $"Inner-gap wall {i} end radius");
                    Assert.AreEqual(8.56f, length, 0.2f, $"Inner-gap wall {i} length");
                }
            }
        }
    }
}
