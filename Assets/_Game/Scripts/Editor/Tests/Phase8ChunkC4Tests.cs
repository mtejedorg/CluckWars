using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C4: <see cref="FoodPileVisuals"/> used to write its property block on every
    /// LateUpdate; it now writes only when the fill ratio moved by more than 0.005, on first sight,
    /// or when the pile lands exactly on empty / full.
    /// </summary>
    public class Phase8ChunkC4Tests
    {
        [Test]
        public void FirstFrame_AlwaysWrites() =>
            Assert.IsTrue(FoodPileVisuals.ShouldWriteTint(float.NaN, 1f));

        [TestCase(0.5f, 0.5f)]
        [TestCase(0.5f, 0.504f)]
        [TestCase(0.5f, 0.496f)]
        public void UnchangedOrTinyChange_DoesNotWrite(float last, float now) =>
            Assert.IsFalse(FoodPileVisuals.ShouldWriteTint(last, now));

        [TestCase(0.5f, 0.51f)]
        [TestCase(0.5f, 0.49f)]
        [TestCase(1f, 0.99f)]
        public void RealChange_Writes(float last, float now) =>
            Assert.IsTrue(FoodPileVisuals.ShouldWriteTint(last, now));

        [Test]
        public void LandingExactlyOnEmptyOrFull_WritesEvenForATinyStep()
        {
            Assert.IsTrue(FoodPileVisuals.ShouldWriteTint(0.003f, 0f), "drained to empty");
            Assert.IsTrue(FoodPileVisuals.ShouldWriteTint(0.997f, 1f), "refilled to full");
            Assert.IsFalse(FoodPileVisuals.ShouldWriteTint(1f, 1f), "already full: nothing to write");
        }

        [Test]
        public void SlowDrain_WritesAgainstTheLastWrittenRatio_NotTheLastFrame()
        {
            // 0.002 per frame: a frame-to-frame comparison would never fire; against the last written
            // ratio the tint refreshes every few frames and never drifts more than the threshold.
            float last = float.NaN, ratio = 1f;
            int writes = 0;
            for (int frame = 0; frame < 400; frame++)
            {
                ratio -= 0.002f;
                if (FoodPileVisuals.ShouldWriteTint(last, ratio))
                {
                    last = ratio;
                    writes++;
                }
                Assert.LessOrEqual(System.Math.Abs(ratio - last), FoodPileVisuals.TintWriteThreshold + 1e-4f, $"frame {frame}");
            }
            Assert.That(writes, Is.InRange(100, 200), "400 frames of drain should write ~1 in 3 frames, not every frame");
        }
    }
}
