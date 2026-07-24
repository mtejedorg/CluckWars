using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class PinwheelLayoutTests
    {
        [Test] public void Build_IsDeterministicForSeed()
        {
            var a = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, baseKeepClear: 4f);
            var b = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, baseKeepClear: 4f);
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
            var segs = PinwheelLayout.Build(15f, 8, seed: 1, centerKeepClear: 3f, baseKeepClear: 4f);
            Assert.AreEqual(8, segs.Length);
        }

        [Test] public void Build_KeepsSegmentsOutOfCenterDisc()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 7, centerKeepClear: 3f, baseKeepClear: 4f);
            foreach (var s in segs)
            {
                // Neither endpoint may sit inside the center keep-clear disc.
                Assert.GreaterOrEqual(s.A.magnitude, 3f, "segment endpoint inside center keep-clear");
                Assert.GreaterOrEqual(s.B.magnitude, 3f, "segment endpoint inside center keep-clear");
            }
        }
    }
}
