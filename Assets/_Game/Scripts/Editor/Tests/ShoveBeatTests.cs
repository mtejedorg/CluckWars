using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D6 (Shove beats): the victim skid window and speed gate, the caster ring counts, the squash curve and its composition
    /// through the animator accumulator, the Reduced Motion variants, and the proof that the shipped Shove really moves the
    /// KnockbackEventId edge the beats key on. Inputs come from the shipped tuning constants and the shipped CluckShock asset.
    /// </summary>
    public sealed class ShoveBeatTests
    {
        private const string ShovePath = "Assets/_Game/Data/Abilities/CluckShock.asset";

        [Test]
        public void SkidWindow_OpensOnTheEdge_AndClosesAfterPointThreeSeconds()
        {
            Assert.AreEqual(0.3f, FeedbackTuning.ShoveSkidWindowSeconds);
            Assert.IsFalse(ShoveBeatRules.SkidWindowOpen(5f, -1f), "no edge yet");
            Assert.IsTrue(ShoveBeatRules.SkidWindowOpen(5f, 5f), "the edge frame");
            Assert.IsTrue(ShoveBeatRules.SkidWindowOpen(5f + FeedbackTuning.ShoveSkidWindowSeconds - 0.01f, 5f));
            Assert.IsFalse(ShoveBeatRules.SkidWindowOpen(5f + FeedbackTuning.ShoveSkidWindowSeconds + 0.01f, 5f), "closed after 0.3 s");
        }

        [Test]
        public void Skid_NeedsTheWindow_AndMoreThanFourMetresPerSecond_AndNoReducedMotion()
        {
            Assert.AreEqual(4f, FeedbackTuning.ShoveSkidMinSpeed);
            float over = FeedbackTuning.ShoveSkidMinSpeed + 0.1f;
            Assert.IsTrue(ShoveBeatRules.SkidShouldRun(false, true, over));
            Assert.IsFalse(ShoveBeatRules.SkidShouldRun(false, true, FeedbackTuning.ShoveSkidMinSpeed), "the gate is strictly above 4 m/s");
            Assert.IsFalse(ShoveBeatRules.SkidShouldRun(false, true, 0f), "a still bird does not skid");
            Assert.IsFalse(ShoveBeatRules.SkidShouldRun(false, false, 20f), "outside the 0.3 s window");
            Assert.IsFalse(ShoveBeatRules.SkidShouldRun(true, true, 20f), "Reduced Motion: no skid");
            Assert.AreEqual(4f, ShoveBeatRules.SkidPerMetre(false));
            Assert.AreEqual(0f, ShoveBeatRules.SkidPerMetre(true));
        }

        [Test]
        public void ShippedShove_DecaysBelowTheSpeedGate_InsideTheWindow_SoTheGateIsWhatEndsTheSkid()
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckShockAbilitySO>(ShovePath);
            Assert.IsNotNull(so, "CluckShock.asset missing");
            // ChickenMovement decays ExternalDisplacement at 8 per second (exponential): the speed falls under the gate at ln(F/4)/8.
            const float decay = 8f;
            float tBelow = Mathf.Log(so.KnockbackForce / FeedbackTuning.ShoveSkidMinSpeed) / decay;
            Assert.Less(tBelow, FeedbackTuning.ShoveSkidWindowSeconds, "the 4 m/s gate ends the skid before the 0.3 s window does");
            Assert.Greater(tBelow, 0.05f, "and the shove is fast enough to skid at all");
            // Distance covered above the gate = (F - 4) / decay metres: at 4 puffs per metre that stays within the live cap.
            float metres = (so.KnockbackForce - FeedbackTuning.ShoveSkidMinSpeed) / decay;
            Assert.LessOrEqual(Mathf.CeilToInt(metres * FeedbackTuning.ShoveSkidPerMetre), FeedbackTuning.ShoveSkidMaxLive,
                "a plain shove emits fewer puffs than the live cap");
        }

        [Test]
        public void SkidBudget_NeverExceedsTheLiveCap()
        {
            int budget = FeedbackTuning.ShoveSkidMaxLive;
            int total = 0;
            for (int i = 0; i < 40; i++) total += ShoveBeatRules.SpendSkidBudget(3, ref budget);
            Assert.AreEqual(FeedbackTuning.ShoveSkidMaxLive, total, "40 frames of demand spend exactly the cap");
            Assert.AreEqual(0, budget);
            Assert.AreEqual(0, ShoveBeatRules.SpendSkidBudget(5, ref budget), "an empty budget emits nothing");
            Assert.LessOrEqual(FeedbackTuning.ShoveSkidMaxLive, 10, "spec: at most 10 live for this effect");
            // I8: the 0.45 s life outlasts the 0.3 s window, so every puff of one shove can be alive at once: the per-edge
            // budget (10) is the worst-case live count of one shove, and the Dust family maxParticles hard-caps overlapping shoves.
            Assert.AreEqual(0.45f, FeedbackTuning.ShoveSkidLifeSeconds, 1e-4f);
            Assert.AreEqual(10, FeedbackTuning.ShoveSkidMaxLive, "I8: the live count stays at 10");
            Assert.LessOrEqual(ChickenVFX.DustMaxParticles, 12);
            Assert.GreaterOrEqual(ChickenVFX.DustMaxParticles, FeedbackTuning.ShoveSkidMaxLive, "the dust family holds the whole skid");
        }

        [Test]
        public void SkidAppearance_FollowsTheSpec()
        {
            Assert.AreEqual(0.35f, FeedbackTuning.ShoveSkidSizeMin, 1e-4f, "I8: x1.5 of 0.234");
            Assert.AreEqual(0.47f, FeedbackTuning.ShoveSkidSizeMax, 1e-4f, "I8: x1.5 of 0.312");
            Assert.AreEqual(0.85f, FeedbackTuning.ShoveSkidColor.r, 1e-4f);
            Assert.AreEqual(0.75f, FeedbackTuning.ShoveSkidColor.g, 1e-4f);
            Assert.AreEqual(0.55f, FeedbackTuning.ShoveSkidColor.b, 1e-4f);
            Assert.AreEqual(0.45f, FeedbackTuning.ShoveSkidColor.a, 1e-4f, "F4: 0.6 to 0.45");
        }

        [Test]
        public void CasterRing_IsEightAccentSparkles_FourFadingInPlaceUnderReducedMotion()
        {
            Assert.AreEqual(8, ShoveBeatRules.RingCount(false));
            Assert.AreEqual(4, ShoveBeatRules.RingCount(true), "Reduced Motion: 4 sparkles");
            Assert.AreEqual(3f, ShoveBeatRules.RingSpeed(false), "outward 3 m/s");
            Assert.AreEqual(0f, ShoveBeatRules.RingSpeed(true), "Reduced Motion: fade in place, no travel");
            Assert.AreEqual(0.6f, FeedbackTuning.ShoveRingRadius);
            Assert.AreEqual(0.2f, FeedbackTuning.ShoveRingLifeSeconds);
        }

        [Test]
        public void CasterRing_WearsTheShoveAccent_AndTheRingFitsTheSparkleFamilyWithMostOfTheBurst()
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckShockAbilitySO>(ShovePath);
            Assert.IsNotNull(so);
            Assert.AreEqual(AccentKey.Shove, so.AccentKey, "the ring colour is the palette Shove accent via the ability");
            Assert.AreEqual(FeedbackPalette.Current.Accent(AccentKey.Shove), so.AccentColor());
            // The ring is emitted before the 16-sparkle cast burst: the ring is never clipped, the burst keeps most of itself.
            Assert.GreaterOrEqual(ChickenVFX.SparkleMaxParticles, FeedbackTuning.ShoveRingCount, "the ring always fits");
            Assert.GreaterOrEqual(ChickenVFX.SparkleMaxParticles - FeedbackTuning.ShoveRingCount, 12, "the burst keeps at least 12 of its 16");
        }

        [Test]
        public void Squash_EasesFromTheSpecScaleToOneOverPointOneTwoSeconds()
        {
            Assert.AreEqual(0.12f, FeedbackTuning.ShoveSquashSeconds);
            Assert.AreEqual(new Vector3(1.12f, 0.88f, 1.12f), FeedbackTuning.ShoveSquashScale);
            var start = ShoveBeatRules.SquashScale(0f, false);
            Assert.AreEqual(1.12f, start.x, 1e-4f);
            Assert.AreEqual(0.88f, start.y, 1e-4f);
            Assert.AreEqual(1.12f, start.z, 1e-4f);
            var mid = ShoveBeatRules.SquashScale(FeedbackTuning.ShoveSquashSeconds * 0.5f, false);
            Assert.Less(mid.y, 1f); Assert.Greater(mid.y, 0.88f);
            Assert.Greater(mid.x, 1f); Assert.Less(mid.x, 1.12f);
            Assert.AreEqual(Vector3.one, ShoveBeatRules.SquashScale(FeedbackTuning.ShoveSquashSeconds, false), "done at 0.12 s");
            Assert.AreEqual(Vector3.one, ShoveBeatRules.SquashScale(-0.1f, false), "before the edge");
            // Monotone: the Y axis only ever recovers.
            float last = 0f;
            for (int i = 0; i <= 12; i++)
            {
                float y = ShoveBeatRules.SquashScale(i * 0.01f, false).y;
                Assert.GreaterOrEqual(y, last - 1e-5f);
                last = y;
            }
        }

        [Test]
        public void Squash_IsOffUnderReducedMotion_AndOnlyOnTheSkeletalPath()
        {
            Assert.AreEqual(Vector3.one, ShoveBeatRules.SquashScale(0f, true), "Reduced Motion: no squash");
            Assert.IsTrue(ShoveBeatRules.SquashApplies(true), "the skeletal path has no scale in its Hit clip");
            Assert.IsFalse(ShoveBeatRules.SquashApplies(false), "the legacy fallback already squashes on TriggerHit");
        }

        [Test]
        public void SkeletalHitClip_CarriesNoScaleCurve_SoTheExistingRecoilDoesNotSquash()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Game/Art/Animations/Placeholders/Hit.anim");
            Assert.IsNotNull(clip, "Hit.anim missing");
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                StringAssert.DoesNotContain("localScale", b.propertyName, "the Hit clip now animates scale: the D6 squash would double it");
        }

        [Test]
        public void ChickenAnimator_ComposesExtraScale_WithTheUniformScale_OnTheSkeletalWrite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            Assert.IsNotNull(prefab);
            var chicken = Object.Instantiate(prefab);
            chicken.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var anim = chicken.GetComponent<ChickenAnimator>();
                Assert.IsNotNull(anim);
                var model = new GameObject("model").transform;
                model.SetParent(chicken.transform, false);
                model.localScale = new Vector3(2f, 3f, 4f);
                anim.SetModelRoot(model);
                anim.SetSkeletalActive(true);
                Assert.IsTrue(anim.IsSkeletal);
                Assert.AreEqual(Vector3.one, anim.ExtraScale, "rest value");

                var apply = typeof(ChickenAnimator).GetMethod("ApplySkeletalOffsets", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(apply);

                apply.Invoke(anim, null);
                Assert.AreEqual(new Vector3(2f, 3f, 4f), model.localScale, "rest: the authored scale untouched");

                anim.ExtraUniformScale = 0.5f;
                anim.ExtraScale = FeedbackTuning.ShoveSquashScale;
                apply.Invoke(anim, null);
                Assert.AreEqual(2f * 0.5f * 1.12f, model.localScale.x, 1e-4f);
                Assert.AreEqual(3f * 0.5f * 0.88f, model.localScale.y, 1e-4f);
                Assert.AreEqual(4f * 0.5f * 1.12f, model.localScale.z, 1e-4f);

                anim.ExtraScale = Vector3.one;
                anim.ExtraUniformScale = 1f;
                apply.Invoke(anim, null);
                Assert.AreEqual(new Vector3(2f, 3f, 4f), model.localScale, "the squash leaves nothing behind");
            }
            finally { Object.DestroyImmediate(chicken); }
        }

        [Test]
        public void ShippedShove_BumpsKnockbackEventId_ThroughTheHitOrigin_AndTheBeatsKeyOnIt()
        {
            var so = AssetDatabase.LoadAssetAtPath<CluckShockAbilitySO>(ShovePath);
            Assert.IsNotNull(so, "the shipped Shove is a CluckShockAbilitySO");
            var shoveSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Abilities/CluckShockAbilitySO.cs");
            StringAssert.Contains("RPC_ApplyKnockback", shoveSrc, "the Shove lands through the Hit-origin knockback RPC");
            StringAssert.DoesNotContain("SelfApplied", shoveSrc);
            var ctrlSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            int a = ctrlSrc.IndexOf("public void RPC_ApplyKnockback", System.StringComparison.Ordinal);
            Assert.Greater(a, 0);
            StringAssert.Contains("ApplyKnockback(impulse); // already scales", ctrlSrc.Substring(a, 400), "the default origin is Hit, which bumps KnockbackEventId");

            var vfxSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenVFX.cs");
            int b = vfxSrc.IndexOf("private void ObserveShoveVictim", System.StringComparison.Ordinal);
            int c = vfxSrc.IndexOf("private void EmitShoveRing", System.StringComparison.Ordinal);
            Assert.Greater(b, 0); Assert.Greater(c, b);
            string body = vfxSrc.Substring(b, c - b);
            StringAssert.Contains("_knockPending", body, "the victim beats key on the replicated knockback edge");
            StringAssert.Contains("_events.Knockback += OnKnockback", vfxSrc, "which is ChickenLocalEvents.Knockback (KnockbackEventId)");
            StringAssert.DoesNotContain(".SelfImpulse +=", vfxSrc, "a dodge is not a shove");
            StringAssert.Contains("IsFadedOpacity", body, "the opacity gate");
            StringAssert.DoesNotContain("Rpc", body);
        }

        [Test]
        public void CastEdge_EmitsTheRingForTheShoveOnly_BehindTheOpacityGate()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenVFX.cs");
            int a = src.IndexOf("private void ObserveCastEvent", System.StringComparison.Ordinal);
            Assert.Greater(a, 0);
            string body = src.Substring(a);
            int ring = body.IndexOf("EmitShoveRing(", System.StringComparison.Ordinal);
            int burst = body.IndexOf("EmitSparkles(in burst", System.StringComparison.Ordinal);
            Assert.Greater(ring, 0, "the ring is emitted off the cast edge");
            Assert.Less(ring, burst, "the ring goes out before the cast burst so the burst, not the ring, loses sparkles when the family is full");
            string gate = body.Substring(Mathf.Max(0, ring - 200), 200);
            StringAssert.Contains("CluckShockAbilitySO", gate);
            StringAssert.Contains("IsFadedOpacity", gate);
        }
    }
}
