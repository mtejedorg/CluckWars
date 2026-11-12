using System.IO;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D10 (Stun beats): the star pop-in and drop-off curves and their edge state machine (including the restart when a new stun
    /// lands mid-fall), the Wing Slam gate on the caster arcs, the bonk puff counts, the Reduced Motion variants and the hidden-bird
    /// gates. Inputs come from the shipped tuning constants and the shipped ability assets.
    /// </summary>
    public sealed class StunBeatTests
    {
        private const string Dir = "Assets/_Game/Data/Abilities/";
        private static string Src(string file) => File.ReadAllText("Assets/_Game/Scripts/Visuals/" + file);

        [Test]
        public void SpecConstants_ArePinned()
        {
            Assert.AreEqual(0.15f, FeedbackTuning.StunStarPopSeconds);
            Assert.AreEqual(1.25f, FeedbackTuning.StunStarPopOvershoot);
            Assert.AreEqual(0.30f, FeedbackTuning.StunStarDropSeconds);
            Assert.AreEqual(0.4f, FeedbackTuning.StunStarDropMetres);
            Assert.AreEqual(0.20f, FeedbackTuning.StunStarDropSecondsReduced);
            Assert.AreEqual(2, FeedbackTuning.StunArcCount);
            Assert.AreEqual(0.9f, FeedbackTuning.StunArcSideMetres);
            Assert.AreEqual(0.18f, FeedbackTuning.StunArcSeconds);
            Assert.AreEqual(0.5f, FeedbackTuning.StunArcScaleStart);
            Assert.AreEqual(1.3f, FeedbackTuning.StunArcScaleEnd);
            Assert.AreEqual(0.8f, FeedbackTuning.StunArcAlpha);
            Assert.AreEqual(5, FeedbackTuning.StunArcSortingOrder);
            Assert.AreEqual(4, FeedbackTuning.StunBonkCount);
            Assert.AreEqual(1.5f, FeedbackTuning.StunBonkHeight);
            Assert.AreEqual(0.25f, FeedbackTuning.StunBonkSeconds);
            Assert.AreEqual(0.8f, FeedbackTuning.StunBonkSpeed);
        }

        [Test]
        public void PopIn_StartsAtZero_OvershootsTo125_AndSettlesAtOne()
        {
            Assert.AreEqual(0f, StunBeatRules.PopScale(0f, false), 1e-5f);
            float peak = 0f, prev = 0f;
            bool rising = true;
            for (float t = 0f; t <= FeedbackTuning.StunStarPopSeconds; t += 0.0005f)
            {
                float s = StunBeatRules.PopScale(t, false);
                peak = Mathf.Max(peak, s);
                if (rising && s < prev - 1e-5f) rising = false;
                else if (!rising) Assert.LessOrEqual(s, prev + 1e-5f, "after the peak the scale only settles back (t=" + t + ")");
                prev = s;
            }
            Assert.AreEqual(FeedbackTuning.StunStarPopOvershoot, peak, 0.01f, "overshoot 1.25");
            Assert.AreEqual(1f, StunBeatRules.PopScale(FeedbackTuning.StunStarPopSeconds, false), 1e-5f);
            Assert.AreEqual(1f, StunBeatRules.PopScale(5f, false));
            Assert.AreEqual(1f, StunBeatRules.PopAlpha(0f, false), "full alpha while scaling");
        }

        [Test]
        public void PopIn_ReducedMotion_IsAnAlphaFadeAtFullSize()
        {
            Assert.AreEqual(1f, StunBeatRules.PopScale(0f, true));
            Assert.AreEqual(1f, StunBeatRules.PopScale(0.05f, true));
            Assert.AreEqual(0f, StunBeatRules.PopAlpha(0f, true), 1e-5f);
            Assert.AreEqual(0.5f, StunBeatRules.PopAlpha(FeedbackTuning.StunStarFadeInSecondsReduced * 0.5f, true), 1e-5f);
            Assert.AreEqual(1f, StunBeatRules.PopAlpha(FeedbackTuning.StunStarFadeInSecondsReduced, true), 1e-5f);
            Assert.AreEqual(0.15f, FeedbackTuning.StunStarFadeInSecondsReduced);
        }

        [Test]
        public void DropOff_FallsPointFourMetresUnderGravity_AndFadesOverPointThreeSeconds()
        {
            Assert.AreEqual(0f, StunBeatRules.DropOffset(0f, false), 1e-6f);
            Assert.AreEqual(-FeedbackTuning.StunStarDropMetres, StunBeatRules.DropOffset(FeedbackTuning.StunStarDropSeconds, false), 1e-5f);
            Assert.AreEqual(-FeedbackTuning.StunStarDropMetres * 0.25f, StunBeatRules.DropOffset(FeedbackTuning.StunStarDropSeconds * 0.5f, false), 1e-5f,
                "free fall: a quarter of the distance at half the time");
            Assert.AreEqual(-FeedbackTuning.StunStarDropMetres, StunBeatRules.DropOffset(9f, false), 1e-5f, "never past the distance");
            Assert.AreEqual(1f, StunBeatRules.DropAlpha(0f, false), 1e-5f);
            Assert.AreEqual(0.5f, StunBeatRules.DropAlpha(FeedbackTuning.StunStarDropSeconds * 0.5f, false), 1e-5f);
            Assert.AreEqual(0f, StunBeatRules.DropAlpha(FeedbackTuning.StunStarDropSeconds, false), 1e-5f);
            Assert.IsFalse(StunBeatRules.DropDone(0.29f, false));
            Assert.IsTrue(StunBeatRules.DropDone(0.30f, false));
        }

        [Test]
        public void DropOff_ReducedMotion_FadesInPlaceInPointTwoSeconds()
        {
            Assert.AreEqual(0f, StunBeatRules.DropOffset(0.1f, true), "no fall");
            Assert.AreEqual(0.5f, StunBeatRules.DropAlpha(FeedbackTuning.StunStarDropSecondsReduced * 0.5f, true), 1e-5f);
            Assert.IsFalse(StunBeatRules.DropDone(0.19f, true));
            Assert.IsTrue(StunBeatRules.DropDone(0.20f, true));
        }

        // ---- Edge state machine ----

        [Test]
        public void Timeline_SeedsOnFirstLiveObservation_NoPopForALateJoin()
        {
            var tl = new StunStarTimeline();
            var f = tl.Step(true, true, 10f, false);
            Assert.IsTrue(f.Visible);
            Assert.IsTrue(f.Orbit);
            Assert.AreEqual(1f, f.Scale, "a stun already running when this peer starts to watch shows steady stars");
            var g = new StunStarTimeline();
            Assert.IsFalse(g.Step(true, false, 10f, false).Visible, "calm and seeded: nothing");
            Assert.IsFalse(g.Dropping, "seeding is not a falling edge");
            Assert.IsFalse(new StunStarTimeline().Step(false, true, 10f, false).Visible, "no live object, no stars");
        }

        [Test]
        public void Timeline_RisingEdgePops_FallingEdgeStopsTheOrbitAndDrops()
        {
            var tl = new StunStarTimeline();
            tl.Step(true, false, 0f, false);
            var pop0 = tl.Step(true, true, 1f, false);
            Assert.IsTrue(pop0.Visible && pop0.Orbit);
            Assert.AreEqual(0f, pop0.Scale, 1e-5f, "scale 0 on the rising edge frame");
            var mid = tl.Step(true, true, 1f + 0.09f, false);
            Assert.Greater(mid.Scale, 1f, "past the overshoot peak is still above 1");
            Assert.AreEqual(1f, tl.Step(true, true, 1.5f, false).Scale, 1e-5f, "settled");

            var d0 = tl.Step(true, false, 2f, false);
            Assert.IsTrue(d0.Visible);
            Assert.IsFalse(d0.Orbit, "stars stop orbiting on the drop");
            Assert.AreEqual(0f, d0.OffsetY, 1e-6f);
            var d1 = tl.Step(true, false, 2.15f, false);
            Assert.Less(d1.OffsetY, 0f);
            Assert.Less(d1.Alpha, 1f);
            var end = tl.Step(true, false, 2.31f, false);
            Assert.IsFalse(end.Visible, "gone after the fade; nothing left behind");
            Assert.IsFalse(tl.Dropping);
            Assert.IsFalse(tl.Step(true, false, 5f, false).Visible);
        }

        [Test]
        public void Timeline_NewStunMidFall_RestartsCleanly()
        {
            var tl = new StunStarTimeline();
            tl.Step(true, true, 0f, false);                      // steady stun
            tl.Step(true, false, 1f, false);                     // falling edge
            var falling = tl.Step(true, false, 1.2f, false);
            Assert.Less(falling.OffsetY, -0.01f, "mid-fall");
            var restart = tl.Step(true, true, 1.2f, false);      // new stun lands
            Assert.IsFalse(tl.Dropping, "the fall is cancelled");
            Assert.AreEqual(0f, restart.OffsetY, 1e-6f, "back at the orbit height");
            Assert.AreEqual(1f, restart.Alpha, 1e-5f, "full alpha again");
            Assert.AreEqual(0f, restart.Scale, 1e-5f, "pops in again from 0");
            Assert.IsTrue(restart.Orbit);
            tl.Step(true, true, 3f, false);
            Assert.IsTrue(tl.Step(true, true, 3.1f, false).Visible, "no stale drop timer ends the new stun");
        }

        [Test]
        public void Timeline_ReducedMotion_FadesInAndOutInPlace()
        {
            var tl = new StunStarTimeline();
            tl.Step(true, false, 0f, true);
            var f0 = tl.Step(true, true, 1f, true);
            Assert.AreEqual(1f, f0.Scale);
            Assert.AreEqual(0f, f0.Alpha, 1e-5f);
            Assert.AreEqual(1f, tl.Step(true, true, 1.2f, true).Alpha, 1e-5f);
            tl.Step(true, false, 2f, true);
            var out1 = tl.Step(true, false, 2.1f, true);
            Assert.AreEqual(0f, out1.OffsetY);
            Assert.AreEqual(0.5f, out1.Alpha, 1e-5f);
            Assert.IsFalse(tl.Step(true, false, 2.21f, true).Visible);
        }

        // ---- The Wing Slam gate on the caster arcs ----

        [Test]
        public void CasterArcs_FireForTheShippedWingSlam_AndNotForADeathMarkOrAnythingElse()
        {
            var slam = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(Dir + "WingSlam.asset");
            var mark = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(Dir + "MarkKill.asset");
            Assert.IsNotNull(slam, "WingSlam.asset missing");
            Assert.IsNotNull(mark, "MarkKill.asset missing");
            Assert.IsInstanceOf<WingSlamAbilitySO>(slam);
            Assert.IsTrue(StunBeatRules.WhiffArcsApply(slam));
            Assert.IsFalse(StunBeatRules.WhiffArcsApply(mark), "a Death Mark stun keeps red flash + stars");
            Assert.IsFalse(StunBeatRules.WhiffArcsApply(null));
            foreach (var guid in AssetDatabase.FindAssets("t:AbilityBaseSO", new[] { "Assets/_Game/Data/Abilities" }))
            {
                var a = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                Assert.AreEqual(a is WingSlamAbilitySO, StunBeatRules.WhiffArcsApply(a), a.name);
            }
        }

        [Test]
        public void CasterArcs_AreCastObserved_AndWearTheStunAccent()
        {
            var hit = Src("HitFeedback.cs");
            int castStart = hit.IndexOf("private void ObserveOwnCast", System.StringComparison.Ordinal);
            int castEnd = hit.IndexOf("private void ConfirmHits", System.StringComparison.Ordinal);
            Assert.Greater(castStart, 0);
            var cast = hit.Substring(castStart, castEnd - castStart);
            StringAssert.Contains("StunBeatRules.WhiffArcsApply(ability)", cast, "the arcs hang off LastCastEventId, gated to the Wing Slam");
            StringAssert.Contains("AccentKey.Stun", hit, "the arcs read the palette Stun accent");
            var slam = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(Dir + "WingSlam.asset");
            Assert.AreEqual(FeedbackPalette.Current.Accent(AccentKey.Stun), slam.AccentColor(), "the arcs match the cast flash");
        }

        [Test]
        public void CasterArcs_ReuseTheFlightPool_NoNewParticleSystem()
        {
            var flight = Src("FoodChunkFlight.cs");
            StringAssert.Contains("public static void PlayStunArcs", flight);
            StringAssert.DoesNotContain("new ParticleSystem", flight);
            Assert.AreEqual(FeedbackTuning.StealChunkSortingOrder, FeedbackTuning.StunArcSortingOrder, "order 5 like the steal movers");
            Assert.LessOrEqual(FeedbackTuning.StunArcCount, FeedbackTuning.StealChunkPoolSize);
        }

        // ---- Bonk puff ----

        [Test]
        public void Bonk_FourPuffs_TwoUnderReducedMotion_AndNoRise()
        {
            Assert.AreEqual(4, StunBeatRules.BonkCount(false));
            Assert.AreEqual(2, StunBeatRules.BonkCount(true));
            Assert.AreEqual(0.8f, StunBeatRules.BonkSpeed(false));
            Assert.AreEqual(0f, StunBeatRules.BonkSpeed(true));
            Assert.LessOrEqual(FeedbackTuning.StunBonkCount, 12, "fits the 12-particle Dust family");
        }

        // ---- Gates ----

        [Test]
        public void EveryNewEmitter_AsksTheQuietGate()
        {
            var vfx = Src("ChickenVFX.cs");
            int a = vfx.IndexOf("public void EmitStunBonk", System.StringComparison.Ordinal);
            Assert.Greater(a, 0);
            StringAssert.Contains("FoodChunkFlight.IsQuiet(_controller)", vfx.Substring(a, 400));

            var hit = Src("HitFeedback.cs");
            int b = hit.IndexOf("private void PlayStunArcs", System.StringComparison.Ordinal);
            Assert.Greater(b, 0);
            StringAssert.Contains("FoodChunkFlight.IsQuiet(_controller)", hit.Substring(b, 300));

            var flight = Src("FoodChunkFlight.cs");
            int c = flight.IndexOf("public static void PlayStunArcs", System.StringComparison.Ordinal);
            Assert.Greater(c, 0);
            StringAssert.Contains("IsQuiet(caster)", flight.Substring(c, 400));
        }

        [Test]
        public void BonkAndStars_RideTheStunnedRisingEdge_OfTheReplicatedFlags()
        {
            var hit = Src("HitFeedback.cs");
            int e = hit.IndexOf("if (stunned && !_wasStunned)", System.StringComparison.Ordinal);
            Assert.Greater(e, 0);
            StringAssert.Contains("EmitStunBonk()", hit.Substring(e, 300));

            var ov = Src("ChickenStateOverlays.cs");
            StringAssert.Contains("_starTimeline.Step(", ov);
            StringAssert.DoesNotContain("new ParticleSystem", ov, "the stars are sprites: no system slot spent");
            StringAssert.DoesNotContain("[Networked]", ov);
        }

        [Test]
        public void NoSecondRing()
        {
            StringAssert.DoesNotContain("LineRenderer", Src("ChickenStateOverlays.cs"));
            StringAssert.DoesNotContain("LineRenderer", Src("StunBeatRules.cs"));
        }
    }
}
