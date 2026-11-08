using System.IO;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D12 (Reduced Motion pass over D2-D11, vfx.md 0.2 / each move Reduced Motion line). One enumeration of every effect the
    /// batch added, reusing the per-item Rules classes: continuous motion is static, one-shots carry no travel (alpha only),
    /// counts are at most half. A second group pins the RUNTIME read: a toggle mid-match must take effect on the next effect, so no
    /// effect may decide Reduced Motion once at Awake.
    /// </summary>
    public sealed class ReducedMotionCoverageTests
    {
        private static void AssertHalved(int normal, int reduced, string what) =>
            Assert.LessOrEqual(reduced, (normal + 1) / 2, $"{what}: Reduced Motion count {reduced} must be at most half of {normal}");

        // ---- Continuous motion is static -----------------------------------------------------------

        [Test]
        public void ContinuousMotion_IsStatic()
        {
            Assert.AreEqual(0f, SlowAuraRules.OutlineLapsPerSecond(true), "aura outline scroll");
            Assert.AreEqual(0f, SlowAuraRules.FeatherNoiseStrength(true), "aura feather sway");
            Assert.AreEqual(0f, SlowAuraRules.OutlineAngle(0f, true));
            Assert.AreEqual(0f, SlowAuraRules.OutlineAngle(7.3f, true), "aura outline angle must not depend on time");
            Assert.AreEqual(0f, HideBeatRules.RingAngle(0f, true));
            Assert.AreEqual(0f, HideBeatRules.RingAngle(5.1f, true), "hide ring angle must not depend on time");
            Assert.AreEqual(HideBeatRules.BlinkAlpha(0.1f, true), HideBeatRules.BlinkAlpha(0.2f, true), "hide end blink is a steady dip");
            Assert.AreEqual(0f, SpeedBodyCueRules.StreaksPerMetre(true), "speed streaks");
            Assert.AreEqual(0f, ShoveBeatRules.SkidPerMetre(true), "shove skid dust");
            Assert.IsFalse(ShoveBeatRules.SkidShouldRun(true, true, 100f), "shove skid never runs");
            Assert.Less(FeedbackTuning.ShellWaddleReducedDegrees, FeedbackTuning.ShellWaddleDegrees, "shell waddle shrinks");
            Assert.AreEqual(Vector3.one, ShoveBeatRules.SquashScale(0.01f, true), "shove squash");
            Assert.AreEqual(1f, PeckBeatRules.SquashFactor(0.01f, true), "peck mound squash");
        }

        // ---- One-shots: no travel, alpha only ------------------------------------------------------

        [Test]
        public void OneShots_HaveNoTravel()
        {
            Assert.AreEqual(0f, HideBeatRules.DustSpeed(true), "hide poof dust");
            Assert.AreEqual(0f, HideBeatRules.FeatherSpeed(true), "hide poof feathers");
            Assert.AreEqual(0f, ShoveBeatRules.RingSpeed(true), "shove caster ring");
            Assert.AreEqual(0f, StunBeatRules.BonkSpeed(true), "stun bonk puff");
            Assert.AreEqual(0, JumpBeatRules.ArcDotCount(true), "jump arc dots");
            Assert.AreEqual(0, JumpBeatRules.LaunchFeatherCount(true), "jump launch feathers");
            Assert.IsFalse(StealBeatRules.ChunkTravels(true), "steal chunks");
            Assert.AreEqual(Vector3.zero, PeckBeatRules.KernelVelocity(Vector3.zero, Vector3.one, true), "peck kernels");
            var top = new Vector3(0f, 1f, 0f);
            var beak = new Vector3(2f, 0.4f, 0f);
            Assert.AreEqual(beak, PeckBeatRules.KernelStart(top, beak, true), "peck kernels start at the beak");

            // Stars: full size throughout the fade-in; no fall, fade in place.
            foreach (float t in new[] { 0f, 0.05f, 0.1f, 0.2f })
                Assert.AreEqual(1f, StunBeatRules.PopScale(t, true), $"stun star pop scale at {t}");
            foreach (float t in new[] { 0f, 0.1f, 0.19f })
                Assert.AreEqual(0f, StunBeatRules.DropOffset(t, true), $"stun star drop offset at {t}");
            Assert.Less(StunBeatRules.PopAlpha(0f, true), 1f, "stun stars fade in");
            Assert.Greater(StunBeatRules.DropAlpha(0f, true), StunBeatRules.DropAlpha(0.1f, true), "stun stars fade out in place");

            // Steal grab lines: a single flash of the extended line, no extend/retract.
            Assert.AreEqual(FeedbackTuning.StealGrabReach, StealBeatRules.GrabExtent(0.01f, true));
            Assert.AreEqual(FeedbackTuning.StealGrabReach, StealBeatRules.GrabExtent(FeedbackTuning.StealGrabFlashSeconds * 0.9f, true));
            Assert.AreEqual(FeedbackTuning.StealGrabFlashSeconds, StealBeatRules.GrabTotalSeconds(3, true), "flash length does not scale with victims");

            // Chunks fade in at the thief.
            Assert.AreEqual(0f, StealBeatRules.ChunkAlpha(0f, true, StealBeatRules.ChunkRoute.ToThief), 1e-4f);
            Assert.AreEqual(1f, StealBeatRules.ChunkAlpha(StealBeatRules.ChunkLifeSeconds(true), true, StealBeatRules.ChunkRoute.ToThief), 1e-4f);
        }

        // ---- Counts halve --------------------------------------------------------------------------

        [Test]
        public void Counts_AreAtMostHalf()
        {
            AssertHalved(FeedbackTuning.SpeedEdgeSparkleCount, SpeedBodyCueRules.EdgeSparkleCount(true), "speed edge sparkles");
            Assert.AreEqual(FeedbackTuning.SpeedEdgeSparkleCount, SpeedBodyCueRules.EdgeSparkleCount(false));
            Assert.LessOrEqual(SpeedBodyCueRules.DustPerMetre(true) * 2f, SpeedBodyCueRules.DustPerMetre(false) + 1e-4f, "speed stride dust");
            AssertHalved(JumpBeatRules.LaunchDustCount(false), JumpBeatRules.LaunchDustCount(true), "jump launch dust");
            AssertHalved(JumpBeatRules.LandingDustCount(false), JumpBeatRules.LandingDustCount(true), "jump landing dust");
            AssertHalved(ShoveBeatRules.RingCount(false), ShoveBeatRules.RingCount(true), "shove ring");
            AssertHalved(HideBeatRules.DustCount(false), HideBeatRules.DustCount(true), "hide poof dust");
            AssertHalved(HideBeatRules.FeatherCount(false), HideBeatRules.FeatherCount(true), "hide poof feathers");
            AssertHalved(StunBeatRules.BonkCount(false), StunBeatRules.BonkCount(true), "stun bonk");
            AssertHalved(PeckBeatRules.KernelCount(false), PeckBeatRules.KernelCount(true), "peck kernels");
            AssertHalved(PeckBeatRules.PuffCount(false), PeckBeatRules.PuffCount(true), "peck puff");
            AssertHalved(StealBeatRules.ArrivalSparkles(StealBeatRules.ChunkRoute.ToThief, false),
                         StealBeatRules.ArrivalSparkles(StealBeatRules.ChunkRoute.ToThief, true), "steal arrival sparkles");
            AssertHalved(SlowAuraRules.PuffCount(false), SlowAuraRules.PuffCount(true), "aura foot puffs");
            Assert.LessOrEqual(SlowAuraRules.FeatherRate(true), SlowAuraRules.FeatherRate(false) * 0.6f + 1e-4f, "aura feather rate");
            Assert.LessOrEqual(SlowAuraRules.SteadyStateLive(true), SlowAuraRules.SteadyStateLive(false));
        }

        // ---- The preference is read at runtime, never decided once at Awake -------------------------

        private static string Src(string name) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets/_Game/Scripts/Visuals/" + name));

        [TestCase("ChickenVFX.cs", 8)]
        [TestCase("SlowAuraFeedback.cs", 1)]
        [TestCase("HideLocalCue.cs", 1)]
        [TestCase("FoodPileVisuals.cs", 2)]
        [TestCase("FoodChunkFlight.cs", 1)]
        [TestCase("ChickenStateOverlays.cs", 2)]
        [TestCase("ChickenShellVisuals.cs", 2)]
        [TestCase("HitFeedback.cs", 1)]
        public void EffectFiles_ReadThePreferenceAtRuntime(string file, int minReads)
        {
            string s = Src(file);
            int n = 0, i = 0;
            while ((i = s.IndexOf("ReducedMotionEnabled", i, System.StringComparison.Ordinal)) >= 0) { n++; i++; }
            if (file == "FoodChunkFlight.cs") n += s.Contains("PlayerPreferences_Reduced()") ? 1 : 0;
            Assert.GreaterOrEqual(n, minReads, $"{file} must read PlayerPreferences.ReducedMotionEnabled at runtime");
        }

        [Test]
        public void NoEffectDecidesThePreferenceInAwakeOrStart()
        {
            foreach (var f in new[] { "ChickenVFX.cs", "SlowAuraFeedback.cs", "HideLocalCue.cs", "FoodPileVisuals.cs", "ChickenStateOverlays.cs", "ChickenShellVisuals.cs" })
            {
                string s = Src(f);
                foreach (string hook in new[] { "void Awake()", "void Start()", "void Spawned()", "void OnEnable()" })
                {
                    int at = s.IndexOf(hook, System.StringComparison.Ordinal);
                    if (at < 0) continue;
                    int end = s.IndexOf("\n        }", at, System.StringComparison.Ordinal);
                    string body = end > at ? s.Substring(at, end - at) : s.Substring(at);
                    Assert.IsFalse(body.Contains("ReducedMotionEnabled"), $"{f} {hook} must not decide Reduced Motion once");
                }
            }
        }

        [Test]
        public void ContinuousMotion_ReadsTheLivePreference()
        {
            // Stars orbit only while Reduced Motion is off, evaluated each frame.
            Assert.IsTrue(Src("ChickenStateOverlays.cs").Contains("f.Orbit && !PlayerPreferences.ReducedMotionEnabled"), "stun star orbit");
            // The shell waddle, idle wobble and heel dust follow the live preference (the transition variants latch at their edge).
            string shell = Src("ChickenShellVisuals.cs").Replace("\r\n", "\n");
            Assert.IsTrue(shell.Contains("float deg = Reduced ?"), "shell waddle");
            Assert.IsTrue(shell.Contains("(Reduced ? 0.5f : 1f)"), "shell heel dust");
            Assert.IsTrue(shell.Contains("if (Reduced)\n            {\n                // Cracks only"), "shell idle wobble");
            // Speed edge sparkles go through the rules.
            Assert.IsTrue(Src("ChickenVFX.cs").Contains("SpeedBodyCueRules.EdgeSparkleCount(reduced)"), "speed edge sparkles");
        }
    }
}
