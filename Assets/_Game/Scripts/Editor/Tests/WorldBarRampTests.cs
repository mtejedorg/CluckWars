using System.IO;
using System.Reflection;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A22 (architecture audit 2026-11-12): the carry and health ramps over a chicken's head are named
    /// colours in <see cref="FeedbackTuning"/>, and the carry bar and the number beside it read the same
    /// ramp. The cargo ramp used to be written out twice in <c>ChickenWorldBars</c>, so a tweak could
    /// move the bar and not the number.
    ///
    /// The literals the constants replaced are re-stated here: moving a colour must not change it.
    /// </summary>
    public sealed class WorldBarRampTests
    {
        private const string BarsPath = "Assets/_Game/Scripts/Visuals/ChickenWorldBars.cs";

        private static Color Ramp(string name, float frac)
        {
            var m = typeof(ChickenWorldBars).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m, $"ChickenWorldBars.{name}(float) is gone");
            return (Color)m.Invoke(null, new object[] { frac });
        }

        [Test]
        public void TheNamedColours_AreTheValuesTheyReplaced()
        {
            Assert.AreEqual(new Color(0.95f, 0.88f, 0.20f, 1f), FeedbackTuning.CargoLowColor);
            Assert.AreEqual(new Color(1f, 0.55f, 0.05f, 1f),    FeedbackTuning.CargoHighColor);
            Assert.AreEqual(new Color(1f, 0.18f, 0.08f, 1f),    FeedbackTuning.CargoFullColor);
            Assert.AreEqual(new Color(0.30f, 0.80f, 0.32f, 1f), FeedbackTuning.HealthHighColor);
            Assert.AreEqual(new Color(0.95f, 0.82f, 0.20f, 1f), FeedbackTuning.HealthMidColor);
            Assert.AreEqual(new Color(0.90f, 0.20f, 0.16f, 1f), FeedbackTuning.HealthLowColor);
            Assert.AreEqual(new Color(0.75f, 0.20f, 0.20f, 0.85f), FeedbackTuning.CanonicalRemovedNameColor);
            Assert.AreEqual(0.3f, FeedbackTuning.KnockRingSeconds, 0f);
        }

        [Test]
        public void TheCargoRamp_AnswersAsTheOldLiteralsDid()
        {
            Color Old(float frac) =>
                frac >= 1f   ? new Color(1f, 0.18f, 0.08f, 1f) :
                frac >= 0.7f ? new Color(1f, 0.55f, 0.05f, 1f) :
                               new Color(0.95f, 0.88f, 0.20f, 1f);

            foreach (float f in new[] { -0.5f, 0f, 0.3f, 0.6999f, 0.7f, 0.9f, 1f, 1.5f })
                Assert.AreEqual(Old(f), Ramp("CargoColor", f), $"cargo ramp at {f}");
        }

        [Test]
        public void TheHealthRamp_AnswersAsTheOldLiteralsDid()
        {
            Color Old(float frac) => frac > 0.5f
                ? Color.Lerp(new Color(0.95f, 0.82f, 0.20f, 1f), new Color(0.30f, 0.80f, 0.32f, 1f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.90f, 0.20f, 0.16f, 1f), new Color(0.95f, 0.82f, 0.20f, 1f), frac * 2f);

            foreach (float f in new[] { 0f, 0.1f, 0.4999f, 0.5f, 0.5001f, 0.75f, 1f })
                Assert.AreEqual(Old(f), Ramp("HpColor", f), $"health ramp at {f}");
        }

        [Test]
        public void TheCargoNumber_TakesTheBarsOwnRamp_AndNeitherHoldsALiteral()
        {
            string src = File.ReadAllText(BarsPath);
            StringAssert.Contains("_cargoFill.color = CargoColor(frac);", src, "the bar reads the ramp");
            StringAssert.Contains("_cargoText.color = CargoColor(frac);", src,
                "the number reads the SAME ramp — it used to carry a hand-copied second one");

            // The only new Color( left in the file is the trough's opaque white (the sprite is already dark).
            int literals = System.Text.RegularExpressions.Regex.Matches(src, @"new Color\(").Count;
            Assert.AreEqual(1, literals,
                $"{BarsPath} holds {literals} colour literals; the carry and health ramps belong to FeedbackTuning (A22).");
        }
    }
}
