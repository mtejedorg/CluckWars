using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D4 (Speed body cue): the trigger rule (slot active AND planar speed above half the class MoveSpeed), the Reduced Motion
    /// variants (no streaks, dust 1/m), the distance accumulators, and the shipped Speed asset the cue is keyed to. Inputs come from
    /// the shipped class stats and the SpeedBurst asset, not hand-copied numbers.
    /// </summary>
    public sealed class SpeedBodyCueTests
    {
        private static ChickenStatsSO[] AllClasses() =>
            AssetDatabase.FindAssets("t:ChickenStatsSO", new[] { "Assets/_Game/Data/Classes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<ChickenStatsSO>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToArray();

        [Test]
        public void Trigger_NeedsTheActiveSlot_AndPlanarSpeedAboveHalfTheClassMoveSpeed_ForEveryShippedClass()
        {
            var classes = AllClasses();
            Assert.IsNotEmpty(classes, "no shipped class stats found");
            foreach (var c in classes)
            {
                float half = 0.5f * c.MoveSpeed;
                Assert.IsFalse(SpeedBodyCueRules.ShouldRun(false, c.MoveSpeed * 1.3f, c.MoveSpeed), c.name + ": inactive slot must not run");
                Assert.IsTrue(SpeedBodyCueRules.ShouldRun(true, c.MoveSpeed * 1.3f, c.MoveSpeed), c.name + ": a boosted sprint runs");
                Assert.IsTrue(SpeedBodyCueRules.ShouldRun(true, half + 0.01f, c.MoveSpeed), c.name + ": just over the threshold runs");
                Assert.IsFalse(SpeedBodyCueRules.ShouldRun(true, half, c.MoveSpeed), c.name + ": exactly half does not (strictly greater)");
                Assert.IsFalse(SpeedBodyCueRules.ShouldRun(true, 0f, c.MoveSpeed), c.name + ": standing still does not run");
            }
        }

        [Test]
        public void ReducedMotion_HasNoStreaks_AndHalvesTheStrideDust()
        {
            Assert.AreEqual(6f, SpeedBodyCueRules.StreaksPerMetre(false), "6 streaks per metre");
            Assert.AreEqual(0f, SpeedBodyCueRules.StreaksPerMetre(true), "Reduced Motion: no streaks");
            Assert.AreEqual(2f, SpeedBodyCueRules.DustPerMetre(false), "stride dust 2 per metre");
            Assert.AreEqual(1f, SpeedBodyCueRules.DustPerMetre(true), "Reduced Motion: dust 1 per metre");
        }

        [Test]
        public void DistanceAccumulator_EarnsWholeParticles_AndKeepsTheRemainder()
        {
            float acc = 0f;
            int total = 0;
            for (int i = 0; i < 100; i++) total += SpeedBodyCueRules.Take(ref acc, 0.1f, 6f);   // 10 m
            Assert.AreEqual(60, total, "10 m at 6/m is 60 streaks however the frames split");
            acc = 0f; total = 0;
            for (int i = 0; i < 100; i++) total += SpeedBodyCueRules.Take(ref acc, 0.1f, 1f);
            Assert.AreEqual(10, total, "Reduced dust: 10 m at 1/m is 10 puffs");
            Assert.AreEqual(0, SpeedBodyCueRules.Take(ref acc, 0f, 6f), "no distance, no particles");
        }

        [Test]
        public void StepClamp_BoundsATeleportBurst()
        {
            Assert.AreEqual(0.2f, SpeedBodyCueRules.StepMetres(0.2f));
            Assert.AreEqual(FeedbackTuning.SpeedCueMaxStepMetres, SpeedBodyCueRules.StepMetres(40f), "a respawn teleport must not dump a thousand particles");
            float acc = 0f;
            Assert.LessOrEqual(SpeedBodyCueRules.Take(ref acc, SpeedBodyCueRules.StepMetres(40f), FeedbackTuning.SpeedStreakPerMetre) , 10);
        }

        [Test]
        public void SpeedAsset_IsTheSpeedBurstTheCueKeysOn_WithAnAccent()
        {
            var so = AssetDatabase.FindAssets("t:SpeedBurstAbilitySO")
                .Select(g => AssetDatabase.LoadAssetAtPath<SpeedBurstAbilitySO>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(a => a != null);
            Assert.IsNotNull(so, "SpeedBurst asset missing");
            Assert.Greater(so.SpeedMultiplier, 1f);
            Assert.Greater(so.AccentColor.maxColorComponent, 0f, "the cue tints with the ability accent");
        }

        [Test]
        public void StreakDensity_FitsTheFamilyCap_AtTheBoostedClassSpeeds()
        {
            // live streaks = per-metre x speed x lifetime; the family cap may thin only the very fastest sprint (<= 30%).
            float worst = AllClasses().Max(c => c.MoveSpeed) * 1.3f * FeedbackTuning.SpeedStreakPerMetre * FeedbackTuning.SpeedStreakLifeSeconds;
            Assert.LessOrEqual(ChickenVFX.StreakMaxParticles, 24, "spec max 24");
            Assert.GreaterOrEqual(ChickenVFX.StreakMaxParticles, worst * 0.7f, "the cap would visibly thin the streaks (worst live demand " + worst + ")");
        }

        [Test]
        public void StreakAction_EmitsWithRoom_RecyclesWhenFull_SkipsOnlyWhenNothingLives()
        {
            int cap = ChickenVFX.StreakMaxParticles;
            Assert.AreEqual(SpeedBodyCueRules.StreakSlot.Emit, SpeedBodyCueRules.StreakAction(cap - 1, cap, 5));
            Assert.AreEqual(SpeedBodyCueRules.StreakSlot.Recycle, SpeedBodyCueRules.StreakAction(cap, cap, 30), "family full: recycle the oldest");
            Assert.AreEqual(SpeedBodyCueRules.StreakSlot.Recycle, SpeedBodyCueRules.StreakAction(4, cap, 0), "bird at its cap: recycle, the count never grows");
            Assert.AreEqual(SpeedBodyCueRules.StreakSlot.Skip, SpeedBodyCueRules.StreakAction(0, cap, 0), "nothing to recycle and no room");
        }

        /// <summary>
        /// Simulates the ChickenVFX streak emitter (distance accumulator + StreakAction + oldest-first recycle) for a 4 s sprint at the
        /// fastest boosted class speed (shipped class MoveSpeed x the shipped SpeedBurst multiplier). <paramref name="particleClock"/>
        /// is the particle time per world second: 1 in a smooth frame, below 1 when the Editor or a phone hitches (Time.deltaTime is
        /// capped while Fusion moves the bird in real time), which is when the old drop-when-full rule left the streaks at the start.
        /// </summary>
        private static void SimulateSprint(float particleClock, out int minLiveAfterWarmup, out float worstNewestLag, out float worstOldestLag)
        {
            var so = AssetDatabase.FindAssets("t:SpeedBurstAbilitySO")
                .Select(g => AssetDatabase.LoadAssetAtPath<SpeedBurstAbilitySO>(AssetDatabase.GUIDToAssetPath(g))).First(a => a != null);
            float speed = AllClasses().Max(c => c.MoveSpeed) * so.SpeedMultiplier;
            Assert.Greater(speed, 10f, "the sprint under test is the fast case (brief: ~13 m/s)");

            const float dt = 1f / 60f;
            int cap = ChickenVFX.StreakMaxParticles;
            var life = new float[cap];
            var x = new float[cap];
            int live = 0;
            float acc = 0f, bird = 0f;
            minLiveAfterWarmup = int.MaxValue; worstNewestLag = 0f; worstOldestLag = 0f;
            for (float t = 0f; t < 4f; t += dt)
            {
                for (int i = live - 1; i >= 0; i--)
                {
                    life[i] -= dt * particleClock;
                    if (life[i] <= 0f) { live--; life[i] = life[live]; x[i] = x[live]; }
                }
                float step = SpeedBodyCueRules.StepMetres(speed * dt);
                bird += step;
                int n = SpeedBodyCueRules.Take(ref acc, step, SpeedBodyCueRules.StreaksPerMetre(false));
                for (int k = 0; k < n; k++)
                {
                    switch (SpeedBodyCueRules.StreakAction(live, cap, 64))
                    {
                        case SpeedBodyCueRules.StreakSlot.Emit:
                            life[live] = FeedbackTuning.SpeedStreakLifeSeconds; x[live] = bird; live++;
                            break;
                        case SpeedBodyCueRules.StreakSlot.Recycle:
                            int oldest = 0;
                            for (int i = 1; i < live; i++) if (life[i] < life[oldest]) oldest = i;
                            life[oldest] = FeedbackTuning.SpeedStreakLifeSeconds; x[oldest] = bird;
                            break;
                    }
                }
                if (t < FeedbackTuning.SpeedStreakLifeSeconds) continue;
                minLiveAfterWarmup = System.Math.Min(minLiveAfterWarmup, live);
                float newest = float.MinValue, oldestX = float.MaxValue;
                for (int i = 0; i < live; i++) { newest = Mathf.Max(newest, x[i]); oldestX = Mathf.Min(oldestX, x[i]); }
                worstNewestLag = Mathf.Max(worstNewestLag, bird - newest);
                worstOldestLag = Mathf.Max(worstOldestLag, bird - oldestX);
            }
        }

        [Test]
        public void FastSprint_KeepsAtLeastTwelveLiveStreaks_RidingTheBird_ForFourSeconds()
        {
            float perMetre = FeedbackTuning.SpeedStreakPerMetre;
            foreach (float clock in new[] { 1f, 0.5f, 0.2f })
            {
                SimulateSprint(clock, out int minLive, out float newestLag, out float oldestLag);
                Assert.GreaterOrEqual(minLive, 12, "clock " + clock + ": a 4 s sprint must keep >= 12 live streaks");
                Assert.LessOrEqual(newestLag, 2f / perMetre, "clock " + clock + ": the newest streak is always at the bird (never dropped)");
                Assert.LessOrEqual(oldestLag, (ChickenVFX.StreakMaxParticles + 2) / perMetre,
                    "clock " + clock + ": the trail is the last 17 streaks behind the bird, not a pile at the sprint start");
            }
        }

        [Test]
        public void StreakLook_IsTheF4Alpha()
        {
            Assert.AreEqual(0.9f, FeedbackTuning.SpeedStreakAlpha, "F4: 0.7 to 0.9");
        }

        [Test]
        public void ChickenAnimator_ComposesTheExtraPitch_IntoTheLean()
        {
            var prop = typeof(ChickenAnimator).GetProperty(nameof(ChickenAnimator.ExtraPitchDegrees));
            Assert.IsNotNull(prop, "ChickenAnimator.ExtraPitchDegrees accumulator missing");
            Assert.AreEqual(6f, FeedbackTuning.SpeedLeanExtraDegrees, "the brief's +6 degrees");
        }
    }
}
