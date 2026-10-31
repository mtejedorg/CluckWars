using System.IO;
using CluckWars.Diagnostics;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Death Mark banner rules and the mark's arrow.</summary>
    public class MarkFeedbackTests
    {
        [TestCase(true, true, true, false, MarkPhase.Kill)]
        [TestCase(false, false, false, true, MarkPhase.Walled)]
        [TestCase(false, true, false, true, MarkPhase.Walled)]
        [TestCase(false, true, false, false, MarkPhase.CloseIn)]
        [TestCase(false, false, true, false, MarkPhase.Stun)]
        [TestCase(false, false, false, false, MarkPhase.Hunt)]
        public void AssassinPhase_ReadsTheKillRules(bool canKill, bool stunned, bool inRange, bool walled, MarkPhase expected) =>
            Assert.AreEqual(expected, MarkFeedbackRules.AssassinPhase(canKill, stunned, inRange, walled));

        [Test]
        public void EveryPhase_SaysSomething_AndNoPhaseMeansNoBanner()
        {
            foreach (MarkPhase p in System.Enum.GetValues(typeof(MarkPhase)))
            {
                string text = MarkFeedbackRules.Text(p, "DashFox", 9, 24);
                if (p == MarkPhase.None) Assert.IsEmpty(text);
                else Assert.IsNotEmpty(text, p.ToString());
            }
            StringAssert.Contains("24", MarkFeedbackRules.Text(MarkPhase.Hunt, "DashFox", 9, 24));
            StringAssert.Contains("9", MarkFeedbackRules.Text(MarkPhase.Victim, null, 9, 0));
        }

        [TestCase(0f, 0)]
        [TestCase(0.1f, 1)]
        [TestCase(11.2f, 12)]
        public void ShownSeconds_RoundsUp(float remaining, int expected) =>
            Assert.AreEqual(expected, MarkFeedbackRules.ShownSeconds(remaining));

        [Test]
        public void TheKillRing_ShowsOnlyOnceItInformsADecision()
        {
            Assert.IsTrue(MarkFeedbackRules.ShowsKillRing(10f, 8f));
            Assert.IsFalse(MarkFeedbackRules.ShowsKillRing(30f, 8f));
        }

        [Test]
        public void TheMarkedRival_NeverGoesToTheFloorRing_AndStealthDoesNotHideIt_SourceGuard()
        {
            var src = File.ReadAllText("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("chevron && !marked && FloorRivalArrows.TryRepresent(", src);
            StringAssert.Contains("MarkFeedback.IsMarkedByLocal(_controller) ? 1f :", src);
        }

        [Test]
        public void TheFeedback_IsLocal_NoRpcAnywhere_SourceGuard() =>
            StringAssert.DoesNotContain("[Rpc", File.ReadAllText("Assets/_Game/Scripts/Visuals/MarkFeedback.cs"));

        [Test]
        public void CwMarkShots_Parses() =>
            Assert.IsTrue(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_markshots" }).MarkShots);
    }
}
