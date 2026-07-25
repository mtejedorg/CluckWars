using NUnit.Framework;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class StealMathTests
    {
        [Test]
        public void Clamp_TakesAbilityValue_WhenRoomAndCargoAmple()
            => Assert.AreEqual(6f, StealMath.Clamp(6f, 10f, 20f), 0.001f);

        [Test]
        public void Clamp_LimitedByAttackerFreeSpace()      // loaded thief can't steal much
            => Assert.AreEqual(3f, StealMath.Clamp(6f, 3f, 20f), 0.001f);

        [Test]
        public void Clamp_LimitedByDefenderCargo()          // can't take more than they hold
            => Assert.AreEqual(2f, StealMath.Clamp(6f, 10f, 2f), 0.001f);

        [Test]
        public void Clamp_ZeroWhenAttackerFull()            // loaded thief steals nothing
            => Assert.AreEqual(0f, StealMath.Clamp(6f, 0f, 20f), 0.001f);

        [Test]
        public void Clamp_NeverNegative()
            => Assert.AreEqual(0f, StealMath.Clamp(6f, -5f, 20f), 0.001f);
    }
}
