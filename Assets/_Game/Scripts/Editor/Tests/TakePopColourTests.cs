using CluckWars.Localization;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>BL-232: the Bully's TAKE pop wears the Warrior's own debt orange (green was unreadable on grass).</summary>
    public class TakePopColourTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";

        [Test]
        public void TakePop_UsesTheWarriorDebtOrange_NotTheGreenCargoGain()
        {
            string src = RuntimeSourceGuardTests.Code(Scripts + "Visuals/WarriorFeedback.cs");
            StringAssert.Contains("UiKeys.HudPopHitTake, FeedbackTuning.WarriorDebtColor,", src);
            StringAssert.DoesNotContain("HudPopHitTake, FeedbackTuning.FloatingTextCargoGainColor", src);
            Assert.AreEqual("TAKE +4", UiText.Format(UiKeys.HudPopHitTake, ("n", 4)));
        }
    }
}
