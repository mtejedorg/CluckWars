using System;
using System.Linq;
using CluckWars.Services;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>K7, the hunter's two buzzes: the patterns, their place in the limiter's severity order, and who feels them.</summary>
    public class HapticRulesTests
    {
        private static int Peak(HapticPattern p) => p.Amplitudes.Max();

        [Test]
        public void PounceStrongerThanLive()
        {
            var live = HapticRules.PatternFor(HapticKind.HuntLive);
            var pounce = HapticRules.PatternFor(HapticKind.HuntPounce);
            Assert.Greater(pounce.TotalMs, live.TotalMs, "the pounce buzz lasts longer");
            Assert.Greater(Peak(pounce), Peak(live), "and peaks higher");
            Assert.Greater(HapticKind.HuntPounce, HapticKind.HuntLive, "so it may take over the live tick inside the gap");
        }

        [Test]
        public void PatternFor_TotalOverKinds()
        {
            foreach (HapticKind kind in Enum.GetValues(typeof(HapticKind)))
            {
                var p = HapticRules.PatternFor(kind);
                Assert.IsNotNull(p, kind.ToString());
                Assert.AreEqual(p.TimingsMs.Length, p.Amplitudes.Length, $"{kind}: one amplitude per segment");
                Assert.Greater(Peak(p), 0, $"{kind}: it buzzes");
                Assert.LessOrEqual(Peak(p), 255, $"{kind}: a valid amplitude");
            }
            Assert.AreNotSame(HapticRules.Hit, HapticRules.PatternFor(HapticKind.HuntLive), "a hunt kind is not the Hit fallback");
            Assert.AreNotSame(HapticRules.Hit, HapticRules.PatternFor(HapticKind.HuntPounce), "a hunt kind is not the Hit fallback");
        }

        [Test]
        public void NewKindsAreAppended_ExistingValuesUnchanged()
        {
            Assert.AreEqual(0, (int)HapticKind.CancelArm);
            Assert.AreEqual(1, (int)HapticKind.Hit);
            Assert.AreEqual(2, (int)HapticKind.Stun);
            Assert.AreEqual(3, (int)HapticKind.HuntLive);
            Assert.AreEqual(4, (int)HapticKind.HuntPounce);
        }

        [Test]
        public void HapticFor_AssassinOnly()
        {
            Assert.AreEqual(HapticKind.HuntLive, MarkFeedback.HapticFor(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.KillLive }));
            Assert.AreEqual(HapticKind.HuntPounce, MarkFeedback.HapticFor(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.KillReady }));
            Assert.AreEqual(HapticKind.HuntPounce,
                MarkFeedback.HapticFor(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.KillLive | MarkEvents.KillReady }), "the stronger one wins");
            foreach (var role in new[] { MarkRole.Victim, MarkRole.None })
                foreach (var ev in new[] { MarkEvents.KillLive, MarkEvents.KillReady, MarkEvents.Marked, MarkEvents.Ended })
                    Assert.IsNull(MarkFeedback.HapticFor(new MarkOutputs { Role = role, Events = ev }), $"{role} {ev}: nothing the victim or a bystander feels");
            Assert.IsNull(MarkFeedback.HapticFor(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.None }), "not every frame");
            Assert.IsNull(MarkFeedback.HapticFor(new MarkOutputs { Role = MarkRole.Assassin, Events = MarkEvents.Lock | MarkEvents.Ended }));
        }

        [Test]
        public void HuntBuzzes_GoThroughTheLimiter_AndPounceBeatsLive()
        {
            var gate = new HapticLimiter();
            Assert.IsTrue(gate.TryAcquire(1f, HapticKind.HuntLive));
            Assert.IsTrue(gate.TryAcquire(1.05f, HapticKind.HuntPounce), "the pounce replaces the live tick inside the gap");
            Assert.IsFalse(gate.TryAcquire(1.1f, HapticKind.HuntLive), "a live tick never interrupts the pounce");
        }

        [Test]
        public void MarkFeedback_BuzzesOnlyThroughTheInjectedService_SourceGuard()
        {
            string src = TestSources.ReadClass("Assets/_Game/Scripts/Visuals/MarkFeedback.cs");
            StringAssert.Contains("[InjectOptional] IHapticsService haptics", src);
            StringAssert.Contains("if (HapticFor(in o) is HapticKind kind) _haptics?.Buzz(kind);", src);
            StringAssert.DoesNotContain("Handheld.Vibrate", src);
        }
    }
}
