using CluckWars.Input;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>J3 (E12a): the pure rule behind the Egg Shell hex's cracked-egg icon and exact timer.</summary>
    public class EggPhaseHexRulesTests
    {
        [Test]
        public void RunEqualToEggSeconds_IsTheEggPhase_WithTwentyTenths()
        {
            Assert.IsTrue(EggPhaseHexRules.Resolve(true, 2.0f, 2f, out int tenths));
            Assert.AreEqual(20, tenths);
        }

        [Test]
        public void RunLongerThanTheEgg_IsStillWalking_NoDigit()
        {
            Assert.IsFalse(EggPhaseHexRules.Resolve(true, 2.4f, 2f, out int tenths));
            Assert.AreEqual(0, tenths);
        }

        [Test]
        public void InactiveSlot_IsNeverTheEggPhase()
        {
            Assert.IsFalse(EggPhaseHexRules.Resolve(false, 1.0f, 2f, out int tenths));
            Assert.AreEqual(0, tenths);
            Assert.IsFalse(EggPhaseHexRules.Resolve(true, 0f, 2f, out tenths));
            Assert.AreEqual(0, tenths);
        }

        [Test]
        public void TenthsRoundUp_AndFormatIsOneDecimalWithAnSuffix()
        {
            Assert.IsTrue(EggPhaseHexRules.Resolve(true, 1.36f, 2f, out int tenths));
            Assert.AreEqual(14, tenths);
            Assert.AreEqual("1.4s", EggPhaseHexRules.Format(tenths));
            Assert.AreEqual("1.4s", EggPhaseHexRules.Format(14));
            Assert.AreEqual("0.1s", EggPhaseHexRules.Format(1));
        }

        [Test]
        public void NeverPrintsZero_EvenWithAFlickerOfRunLeft()
        {
            Assert.IsTrue(EggPhaseHexRules.Resolve(true, 0.001f, 2f, out int tenths));
            Assert.AreEqual(1, tenths);
        }
    }
}
