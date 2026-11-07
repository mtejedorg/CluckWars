using System.Globalization;
using System.IO;
using System.Threading;
using CluckWars.Abilities;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A4 (spec 2026-11-06 §4.2): the caster-side stun countdown on the Stun hex. Rules first, then a source guard that
    /// the HUD (touch arc and desktop / pad strip, both drawn by TouchControlsController) routes through the helper.
    /// </summary>
    public class CasterStunCountdownTests
    {
        private const string HudPath = "Assets/_Game/Scripts/Input/TouchControlsController.cs";

        // The shipped Stun length, never a hand-copied number.
        private static float StunSeconds()
        {
            var so = ScriptableObject.CreateInstance<WingSlamAbilitySO>();
            try { return so.StunDuration; }
            finally { Object.DestroyImmediate(so); }
        }

        [Test]
        public void Shows_TheLargestRivalRemaining()
        {
            float s = StunSeconds();
            var values = new[] { s * 0.3f, s * 0.8f, 0f, s * 0.5f };
            bool show = CasterStunCountdownRules.TryGetSeconds(true, 2, s * 0.1f, s, values, values.Length, out float secs);
            Assert.IsTrue(show);
            Assert.AreEqual(s * 0.8f, secs, 1e-5f);
        }

        [Test]
        public void Hides_WhenEveryVictimIsAtZero()
        {
            float s = StunSeconds();
            var values = new[] { 0f, 0f };
            Assert.IsFalse(CasterStunCountdownRules.TryGetSeconds(true, 2, 0.2f, s, values, values.Length, out _));
        }

        [Test]
        public void Hides_WhenTheLastCastIsNotTheStun()
        {
            float s = StunSeconds();
            var values = new[] { s * 0.8f };
            Assert.IsFalse(CasterStunCountdownRules.TryGetSeconds(false, 1, 0.1f, s, values, values.Length, out _));
        }

        [Test]
        public void Hides_WhenTheStunWhiffed()
        {
            float s = StunSeconds();
            var values = new[] { s * 0.8f };   // someone else's stun: his own cast hit nobody
            Assert.IsFalse(CasterStunCountdownRules.TryGetSeconds(true, 0, 0.1f, s, values, values.Length, out _));
        }

        [Test]
        public void Hides_OnceTheCastsOwnStunWindowHasPassed()
        {
            float s = StunSeconds();
            var values = new[] { s };   // a later, longer stun from another bird
            float after = s + FeedbackTuning.CasterStunCountdownSlackSeconds + 0.01f;
            Assert.IsFalse(CasterStunCountdownRules.TryGetSeconds(true, 1, after, s, values, values.Length, out _));
        }

        [Test]
        public void IgnoresA_StunLongerThanThisCastCouldStillHave()
        {
            float s = StunSeconds();
            float since = s * 0.5f;
            float ours = s - since;                                                // what this cast has left
            float fresh = ours + FeedbackTuning.CasterStunCountdownSlackSeconds + 0.1f; // a fresher stun from someone else
            var values = new[] { fresh, ours };
            Assert.IsTrue(CasterStunCountdownRules.TryGetSeconds(true, 1, since, s, values, values.Length, out float secs));
            Assert.AreEqual(ours, secs, 1e-5f);
        }

        [Test]
        public void Format_IsOneDecimal_InvariantCulture()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");   // Maestro's locale writes 1,1
                Assert.AreEqual("1.1", CasterStunCountdownRules.Format(CasterStunCountdownRules.Tenths(1.1f)));
                Assert.AreEqual("0.1", CasterStunCountdownRules.Format(CasterStunCountdownRules.Tenths(0.01f)));
                Assert.AreEqual("1.3", CasterStunCountdownRules.Format(CasterStunCountdownRules.Tenths(1.25f)));
            }
            finally { Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Test]
        public void Hud_TouchArcAndDesktopStrip_BothRouteThroughTheHelper_SourceGuard()
        {
            string src = File.ReadAllText(HudPath);
            StringAssert.Contains("CasterStunCountdownRules.TryGetSeconds(", src);
            StringAssert.Contains("CasterStunCountdownRules.Format(", src);
            StringAssert.Contains("ChickenController.ActiveControllers", src);
            StringAssert.Contains("LastCastAbility as WingSlamAbilitySO", src);
            StringAssert.Contains("FeedbackTuning.CanonicalStunColor", src);

            // The desktop / pad strip and the touch arc share RefreshCenterMark; the digit is decided before the
            // layout split, so both layouts show it.
            int method = src.IndexOf("private void RefreshCenterMark(", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(method, 0, "RefreshCenterMark not found.");
            int digit = src.IndexOf("RefreshCasterStunDigit(slot", method, System.StringComparison.Ordinal);
            int split = src.IndexOf("_mode != HudDeviceMode.Touch", method, System.StringComparison.Ordinal);
            Assert.Greater(digit, method, "RefreshCenterMark must call RefreshCasterStunDigit.");
            Assert.Greater(split, digit, "The stun digit must be decided before the touch / desktop split.");
        }
    }
}
