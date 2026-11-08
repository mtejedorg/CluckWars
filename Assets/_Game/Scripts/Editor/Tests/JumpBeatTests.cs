using System.Reflection;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D5 (Jump beats): the arc dots sit on the animator half-sine, the counts follow the spec with the Reduced Motion variants
    /// (no arc, 3 fading puffs, no feathers), and the dust family is sized for the launch + landing overlap. Inputs come from the
    /// shipped tuning constants and the Jump accent in the palette, not hand-copied numbers.
    /// </summary>
    public sealed class JumpBeatTests
    {
        private static readonly Vector3 Launch = new Vector3(2f, 0.3f, -1f);

        [Test]
        public void ArcDots_SampleTheChordFromTenToNinetyPercent()
        {
            int n = FeedbackTuning.JumpArcDotCount;
            Assert.AreEqual(5, n);
            Assert.AreEqual(0.1f, JumpBeatRules.ArcSampleT(0, n), 1e-5f);
            Assert.AreEqual(0.5f, JumpBeatRules.ArcSampleT(2, n), 1e-5f);
            Assert.AreEqual(0.9f, JumpBeatRules.ArcSampleT(n - 1, n), 1e-5f);
        }

        [Test]
        public void ArcDotHeights_MatchTheAnimatorHalfSine_ForEveryChordLength()
        {
            int n = FeedbackTuning.JumpArcDotCount;
            foreach (float len in new[] { 0.6f, 1.5f, FeedbackTuning.JumpTravelFullArcDistance, 2f * FeedbackTuning.JumpTravelFullArcDistance, 11.7f })
            {
                var land = Launch + new Vector3(len, 0f, 0f);
                float scale = ChickenAnimator.JumpArcScale(len);
                for (int i = 0; i < n; i++)
                {
                    float t = JumpBeatRules.ArcSampleT(i, n);
                    float expected = FeedbackTuning.JumpTravelArcHeight * scale * Mathf.Sin(t * Mathf.PI);
                    var p = JumpBeatRules.ArcDotPoint(Launch, land, i, n);
                    Assert.AreEqual(Launch.y + FeedbackTuning.JumpArcDotBaseHeight + expected, p.y, 1e-4f, "chord " + len + " dot " + i + " height");
                    Assert.AreEqual(Launch.x + len * t, p.x, 1e-4f, "chord " + len + " dot " + i + " lies on the chord");
                    Assert.AreEqual(Launch.z, p.z, 1e-4f);
                }
                // The middle dot is the peak; the ends are lower (a half-sine, never a flat line, once the arc has any strength).
                float mid = JumpBeatRules.ArcDotPoint(Launch, land, n / 2, n).y;
                float end = JumpBeatRules.ArcDotPoint(Launch, land, 0, n).y;
                if (scale > 0.05f) Assert.Greater(mid, end, "chord " + len + ": the arc peaks mid-flight");
            }
        }

        [Test]
        public void AnimatorArcHelpers_AreTheOnesTheModelUses_FullArcAtAFullShortJump()
        {
            Assert.AreEqual(1f, ChickenAnimator.JumpArcScale(FeedbackTuning.JumpTravelFullArcDistance), 1e-5f);
            Assert.AreEqual(1f, ChickenAnimator.JumpArcScale(99f), 1e-5f, "clamped");
            Assert.AreEqual(0f, ChickenAnimator.JumpArcLift(0f, 1f), 1e-5f);
            Assert.AreEqual(FeedbackTuning.JumpTravelArcHeight, ChickenAnimator.JumpArcLift(0.5f, 1f), 1e-5f);
            Assert.AreEqual(0f, ChickenAnimator.JumpArcLift(1f, 1f), 1e-5f);
            Assert.Less(ChickenAnimator.JumpArcScale(0.15f * FeedbackTuning.JumpTravelFullArcDistance), 0.03f, "proxy residual dead zone");
        }

        [Test]
        public void Counts_FollowTheSpec_AndReducedMotionFadesInPlace()
        {
            Assert.AreEqual(6, JumpBeatRules.LaunchDustCount(false));
            Assert.AreEqual(8, JumpBeatRules.LandingDustCount(false));
            Assert.AreEqual(2, JumpBeatRules.LaunchFeatherCount(false));
            Assert.AreEqual(5, JumpBeatRules.ArcDotCount(false));
            Assert.AreEqual(3, JumpBeatRules.LaunchDustCount(true), "Reduced Motion: 3 launch puffs");
            Assert.AreEqual(3, JumpBeatRules.LandingDustCount(true), "Reduced Motion: 3 landing puffs");
            Assert.AreEqual(0, JumpBeatRules.LaunchFeatherCount(true), "feathers travel");
            Assert.AreEqual(0, JumpBeatRules.ArcDotCount(true), "no arc dots");
        }

        [Test]
        public void ArcDraws_OnlyWhenTheModelActuallyTravelled()
        {
            Assert.IsFalse(JumpBeatRules.DrawsArc(0.05f), "a proxy whose pivot already slid");
            Assert.IsTrue(JumpBeatRules.DrawsArc(FeedbackTuning.JumpTravelFullArcDistance));
        }

        [Test]
        public void LaunchDustLifetime_AndLandingDelay_FollowTheSettle()
        {
            Assert.AreEqual(0.3f, FeedbackTuning.JumpLaunchDustSeconds);
            Assert.AreEqual(0.3f, FeedbackTuning.JumpLandingDustSeconds);
            Assert.AreEqual(0.4f, FeedbackTuning.JumpLaunchFeatherSeconds);
            Assert.AreEqual(0.25f, FeedbackTuning.JumpArcDotSeconds);
            Assert.AreEqual(0.20f, FeedbackTuning.JumpArcDotSize, "F4: 0.12 to 0.20");
            Assert.AreEqual(0.3f, FeedbackTuning.JumpLaunchDustRadius);
        }

        [Test]
        public void DustFamily_HoldsTheLaunchRingPlusMostOfTheLanding_AndTheArcFitsTheSparkleFamily()
        {
            // 6 launch puffs live until 0.30 s, the landing 8 fire at the settle (0.18 s): at most the last two landing puffs clip.
            int demand = FeedbackTuning.JumpLaunchDustCount + FeedbackTuning.JumpLandingDustCount;
            Assert.GreaterOrEqual(ChickenVFX.DustMaxParticles, demand - 2, "dust family clips more than two landing puffs");
            Assert.GreaterOrEqual(ChickenVFX.DustMaxParticles, FeedbackTuning.JumpLandingDustCount + 4, "landing plus part of the launch ring");
            Assert.GreaterOrEqual(ChickenVFX.FeatherMaxParticles, 10 + FeedbackTuning.JumpLaunchFeatherCount, "death feathers + launch feathers");
            Assert.GreaterOrEqual(ChickenVFX.SparkleMaxParticles, 16 + FeedbackTuning.JumpArcDotCount - 1, "cast burst 16 + the arc (burst may lose one)");
            Assert.AreEqual(64, ChickenVFX.DustMaxParticles + ChickenVFX.FeatherMaxParticles + ChickenVFX.SparkleMaxParticles
                                  + ChickenVFX.ShardMaxParticles + ChickenVFX.StreakMaxParticles, "the five families sum to the per-chicken cap");
        }

        [Test]
        public void ArcDotColour_IsTheJumpAccent_AndTheBeatNeverTouchesTheHitPath()
        {
            Assert.Greater(FeedbackPalette.Current.Accent(AccentKey.Jump).maxColorComponent, 0f);
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenVFX.cs");
            int a = src.IndexOf("private void ObserveJumpBeats", System.StringComparison.Ordinal);
            int b = src.IndexOf("private void EmitStreak", System.StringComparison.Ordinal);
            Assert.Greater(a, 0);
            string body = src.Substring(a, b - a);
            StringAssert.DoesNotContain("SelfImpulseEventId", body);
            StringAssert.DoesNotContain("KnockbackEventId", body);
            StringAssert.DoesNotContain("HitFeedback", body);
        }

        [Test]
        public void Animator_PublishesTheLaunchPosition_ForTheBeats()
        {
            Assert.IsNotNull(typeof(ChickenAnimator).GetProperty(nameof(ChickenAnimator.JumpBeatSerial), BindingFlags.Instance | BindingFlags.Public));
            Assert.IsNotNull(typeof(ChickenAnimator).GetProperty(nameof(ChickenAnimator.JumpLaunchWorld), BindingFlags.Instance | BindingFlags.Public));
        }

        [Test]
        public void SlotEdge_GivesOneLaunchAndOneLanding_PerDash()
        {
            bool[] seq = { false, false, true, true, true, false, false, true, false };
            int launches = 0, lands = 0;
            for (int i = 1; i < seq.Length; i++)
            {
                var e = JumpBeatRules.SlotEdge(seq[i - 1], seq[i]);
                if (e == JumpBeatRules.Edge.Launch) launches++;
                if (e == JumpBeatRules.Edge.Land) lands++;
            }
            Assert.AreEqual(2, launches, "two dashes, two launches");
            Assert.AreEqual(2, lands, "two dashes, two landings");
            Assert.AreEqual(JumpBeatRules.Edge.None, JumpBeatRules.SlotEdge(true, true));
            Assert.AreEqual(JumpBeatRules.Edge.None, JumpBeatRules.SlotEdge(false, false));
        }

        [Test]
        public void TeleportEventPath_IsDedupedAgainstTheSlotEdge_ButNotAcrossSeparateJumps()
        {
            Assert.IsTrue(JumpBeatRules.EventPathDeduped(10.1f, 10f), "same jump: the slot path already fired");
            Assert.IsFalse(JumpBeatRules.EventPathDeduped(10f + FeedbackTuning.JumpPathDedupeSeconds + 0.01f, 10f), "a later teleport jump is its own");
            Assert.IsFalse(JumpBeatRules.EventPathDeduped(0f, -100f), "no slot jump yet (initial sentinel)");
            Assert.GreaterOrEqual(FeedbackTuning.JumpPathDedupeSeconds, 0.5f * 1f, "covers the dash plus observation lag");
        }

        [Test]
        public void Hop_FiresOncePerJump_OnBothPaths()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ControlStateVFX.cs");
            int total = src.Split(new[] { "SelfPop(UiKeys.HudPopHop" }, System.StringSplitOptions.None).Length - 1;
            Assert.AreEqual(2, total, "one HOP! on the slot edge, one on the teleport event");
            string landing = src.Substring(src.LastIndexOf("private void ObserveJumpLandingEdge", System.StringComparison.Ordinal));
            StringAssert.Contains("EventPathDeduped", landing.Substring(0, landing.IndexOf("HudPopHop", System.StringComparison.Ordinal) + 20),
                "the event-path HOP! must be skipped inside the slot dedupe window");
            StringAssert.Contains("Edge.Launch", src, "the slot edge pops HOP! on launch only");
        }

        [Test]
        public void SlotTrigger_KeysOnTheShippedJumpAbility_NotOnJumpTier()
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckWars.Abilities.ShadowstepAbilitySO>("Assets/_Game/Data/Abilities/Shadowstep.asset");
            Assert.IsNotNull(so);
            Assert.AreEqual("Jump", so.DisplayName);
            Assert.AreEqual(CluckWars.Gameplay.JumpLengthTier.None, so.JumpTier, "the shipped Jump is a dash: this is why the slot edge is the trigger");
        }

        // ---- F9: the bystander landing shockwave rides the slot landing edge ----------------------------------------

        private static int CountShockwaves(float dt, bool reduced, bool hidden, bool eventBumpsAtLaunch)
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckWars.Abilities.ShadowstepAbilitySO>("Assets/_Game/Data/Abilities/Shadowstep.asset");
            Assert.IsNotNull(so);
            float lastEvent = -100f, lastSlotEdge = -100f;
            bool was = false;
            int rings = 0;
            // 0.5 s idle, one dash of the shipped Duration, 1 s idle.
            for (float t = 0f; t < 0.5f + so.Duration + 1f; t += dt)
            {
                bool active = t >= 0.5f && t < 0.5f + so.Duration;
                if (eventBumpsAtLaunch && active && !was && JumpBeatRules.EventShockwave(reduced, hidden, t, lastSlotEdge))
                {
                    lastEvent = t; rings++;   // the delayed event ring, counted at arming
                }
                var edge = JumpBeatRules.SlotEdge(was, active);
                if (JumpBeatRules.SlotLandingShockwave(edge, reduced, hidden, t, lastEvent)) rings++;
                if (edge != JumpBeatRules.Edge.None) lastSlotEdge = t;
                was = active;
            }
            return rings;
        }

        [Test]
        public void LandingShockwave_FiresOncePerShippedJump_At30And60And120Hz()
        {
            foreach (float dt in new[] { 1f / 120f, 1f / 60f, 1f / 30f })
                Assert.AreEqual(1, CountShockwaves(dt, false, false, false), $"one ring per dash at dt={dt}");
        }

        [Test]
        public void LandingShockwave_IsNotDoubled_WhenJumpEventIdAlsoBumps()
        {
            foreach (float dt in new[] { 1f / 120f, 1f / 60f, 1f / 30f })
                Assert.AreEqual(1, CountShockwaves(dt, false, false, true), $"event + slot path for one jump = one ring at dt={dt}");
        }

        [Test]
        public void LandingShockwave_IsGatedByReducedMotionAndHidden()
        {
            Assert.AreEqual(0, CountShockwaves(1f / 60f, true, false, false), "Reduced Motion: no ring");
            Assert.AreEqual(0, CountShockwaves(1f / 60f, false, true, false), "hidden bird: no ring");
            Assert.AreEqual(0, CountShockwaves(1f / 60f, true, false, true), "Reduced Motion: neither path rings");
        }

        [Test]
        public void LandingShockwave_TwoDashesSeparatedByTheCooldown_RingTwice()
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckWars.Abilities.ShadowstepAbilitySO>("Assets/_Game/Data/Abilities/Shadowstep.asset");
            float gap = so.Cooldown;   // the soonest the next dash can start
            Assert.Greater(gap + so.Duration, FeedbackTuning.JumpPathDedupeSeconds, "the dedupe window must not swallow back-to-back dashes");
            Assert.IsFalse(JumpBeatRules.EventShockwave(false, false, 0f, 0f - 0.1f) , "inside the window the slot owns the jump");
            Assert.IsTrue(JumpBeatRules.EventShockwave(false, false, so.Duration + gap, so.Duration), "a later teleport jump rings");
        }

        [Test]
        public void ControlStateVFX_ArmsTheRingFromTheSlotLandingEdge()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ControlStateVFX.cs");
            StringAssert.Contains("JumpBeatRules.SlotLandingShockwave", src);
            StringAssert.Contains("JumpBeatRules.EventShockwave", src);
        }
    }
}
