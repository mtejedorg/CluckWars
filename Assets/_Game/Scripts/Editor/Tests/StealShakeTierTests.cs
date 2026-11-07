using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Spec 2026-11-06 §4.1: on a Steal the robbed bird's camera must shake harder and
    /// longer than the thief's. The thief once shook at 0.18 against the victim's 0.12,
    /// so the camera told the wrong player that something had happened to them.
    /// </summary>
    public sealed class StealShakeTierTests
    {
        [Test]
        public void ThiefShake_StaysBelowTheVictimShake()
        {
            Assert.Less(FeedbackTuning.CasterStealShakeMagnitude, FeedbackTuning.VictimHitShakeMagnitude,
                "The thief's steal shake out-ranks the victim's hit shake again (spec §4.1).");
            Assert.Less(FeedbackTuning.CasterStealShakeDurationSeconds, FeedbackTuning.VictimHitShakeDurationSeconds,
                "The thief's steal shake outlasts the victim's hit shake (spec §4.1).");
        }

        [Test]
        public void ThiefShake_StillReadsAboveAPlainCast()
        {
            Assert.Greater(FeedbackTuning.CasterStealShakeMagnitude, FeedbackTuning.CasterMicroShakeMagnitude,
                "A landed Steal should still punch a little harder than a plain cast.");
        }
    }
}
