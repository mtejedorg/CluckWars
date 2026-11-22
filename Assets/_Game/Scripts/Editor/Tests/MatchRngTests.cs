using CluckWars.Balance;
using NUnit.Framework;

namespace CluckWars.Tests
{
    public sealed class MatchRngTests
    {
        [Test]
        public void Pcg32_MatchesTheReferenceVector()
        {
            // pcg32-demo, pcg32_srandom(42, 54)
            var rng = MatchRng.Pcg32(42UL, 54UL);
            Assert.AreEqual(0xa15c02b7u, rng.NextUInt());
            Assert.AreEqual(0x7b47f409u, rng.NextUInt());
            Assert.AreEqual(0xba1d3330u, rng.NextUInt());
        }

        [Test]
        public void SplitMix64_MatchesTheReferenceVector()
        {
            ulong state = 0UL;
            Assert.AreEqual(0xe220a8397b1dcdafUL, MatchRng.SplitMix64(ref state));
        }

        [Test]
        public void Streams_AreIndependent()
        {
            var a0 = MatchRng.Create(77UL, 0UL);
            var a1 = MatchRng.Create(77UL, 1UL);
            var b1 = MatchRng.Create(77UL, 1UL);
            for (int i = 0; i < 50; i++) a0.NextUInt();
            for (int i = 0; i < 8; i++) Assert.AreEqual(b1.NextUInt(), a1.NextUInt(), $"draw {i}");
            Assert.AreNotEqual(MatchRng.Create(77UL, 0UL).NextUInt(), MatchRng.Create(77UL, 1UL).NextUInt());
        }

        [Test]
        public void NextFloat_StaysInTheUnitInterval()
        {
            var rng = MatchRng.Create(5UL, MatchRng.StreamSetup);
            for (int i = 0; i < 10000; i++)
            {
                float f = rng.NextFloat();
                Assert.IsTrue(f >= 0f && f < 1f, $"draw {i} was {f}");
            }
        }

        [Test]
        public void NextInt_StaysBelowTheBound()
        {
            var rng = MatchRng.Create(5UL, MatchRng.StreamMark);
            for (int i = 0; i < 2000; i++)
            {
                int v = rng.NextInt(7);
                Assert.IsTrue(v >= 0 && v < 7, $"draw {i} was {v}");
            }
            Assert.AreEqual(0, rng.NextInt(0));
        }

        [Test]
        public void MatchSeed_ChangesWithEachInput()
        {
            ulong baseSeed = MatchRng.MatchSeed(1UL, 2, 3);
            Assert.AreEqual(baseSeed, MatchRng.MatchSeed(1UL, 2, 3));
            Assert.AreNotEqual(baseSeed, MatchRng.MatchSeed(2UL, 2, 3), "run seed");
            Assert.AreNotEqual(baseSeed, MatchRng.MatchSeed(1UL, 3, 3), "cell");
            Assert.AreNotEqual(baseSeed, MatchRng.MatchSeed(1UL, 2, 4), "replicate");
        }
    }
}
