using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static CluckWars.Visuals.StealBeatRules;

namespace CluckWars.Tests
{
    /// <summary>
    /// D7 (Steal beats): grab-line timing and stagger, the chunk routing (a hidden thief is never flown at), the chunk motion and the
    /// pool (size, recycling the oldest), the Reduced Motion variants, the one-pop-per-event rule. Inputs come from the shipped
    /// tuning constants and the shipped Snatch asset.
    /// </summary>
    public sealed class StealBeatTests
    {
        private const string SnatchPath = "Assets/_Game/Data/Abilities/Snatch.asset";
        private const float Eps = 1e-4f;

        // ---- Grab lines ----

        [Test]
        public void GrabLine_ExtendsToEightyPercentIn0_08s_ThenRetractsIn0_10s()
        {
            Assert.AreEqual(0.08f, FeedbackTuning.StealGrabExtendSeconds);
            Assert.AreEqual(0.10f, FeedbackTuning.StealGrabRetractSeconds);
            Assert.AreEqual(0.8f, FeedbackTuning.StealGrabReach);
            Assert.AreEqual(0f, GrabExtent(0f, false), Eps);
            Assert.AreEqual(0.4f, GrabExtent(0.04f, false), Eps, "halfway through the extend");
            Assert.AreEqual(0.8f, GrabExtent(0.08f, false), Eps, "tip at 80 %");
            Assert.AreEqual(0.4f, GrabExtent(0.13f, false), Eps, "halfway through the retract");
            Assert.AreEqual(0f, GrabExtent(0.18f, false), Eps, "back at the beak");
            Assert.AreEqual(0f, GrabExtent(-0.01f, false), "not started");
            Assert.AreEqual(0f, GrabExtent(0.5f, false), "over");
        }

        [Test]
        public void GrabLines_StaggerBySixtyMillisecondsPerVictim_AcrossTheThreeImpactRenderers()
        {
            Assert.AreEqual(3, FeedbackTuning.ImpactMotionLineCount, "up to 3 victims share the 3 existing impact renderers");
            Assert.AreEqual(0f, GrabStart(0), Eps);
            Assert.AreEqual(0.06f, GrabStart(1), Eps);
            Assert.AreEqual(0.12f, GrabStart(2), Eps);
            Assert.AreEqual(0.18f, GrabTotalSeconds(1, false), Eps, "one victim: extend + retract");
            Assert.AreEqual(0.30f, GrabTotalSeconds(3, false), Eps, "three victims: 2 x stagger + 0.18");
        }

        [Test]
        public void GrabLines_AreAccentAlpha0_9_Width0_06_ThroughTheirLife()
        {
            Assert.AreEqual(0.9f, FeedbackTuning.StealGrabAlpha);
            Assert.AreEqual(0.06f, FeedbackTuning.StealGrabWidth);
            Assert.AreEqual(0.9f, GrabAlpha(0.01f, false), Eps);
            Assert.AreEqual(0.9f, GrabAlpha(0.17f, false), Eps);
            Assert.AreEqual(0f, GrabAlpha(0.2f, false));
            Assert.AreEqual(0f, GrabAlpha(-0.01f, false), "a staggered line is invisible until its start");
        }

        [Test]
        public void GrabLines_ReducedMotion_IsOneAlphaFlashOfTheExtendedLines()
        {
            Assert.AreEqual(0.15f, FeedbackTuning.StealGrabFlashSeconds);
            Assert.AreEqual(0.8f, GrabExtent(0f, true), Eps, "extended from the first frame: no travel");
            Assert.AreEqual(0.8f, GrabExtent(0.14f, true), Eps);
            Assert.AreEqual(0f, GrabExtent(0.16f, true));
            Assert.Greater(GrabAlpha(0.01f, true), GrabAlpha(0.14f, true), "alpha fades over the flash");
            Assert.AreEqual(0f, GrabAlpha(0.15f, true));
            Assert.AreEqual(0.15f, GrabTotalSeconds(3, true), Eps, "no stagger: a single flash for every victim");
        }

        [Test]
        public void GrabLine_NeverPointsAtABirdThisPeerCannotSee()
        {
            Assert.IsTrue(GrabLineApplies(false));
            Assert.IsFalse(GrabLineApplies(true), "a hidden victim would be revealed by the line");
            Assert.IsTrue(FoodChunkFlight.IsQuiet(null), "no controller is quiet");
        }

        // ---- Chunks ----

        [Test]
        public void ChunkConstants_MatchTheSpec()
        {
            Assert.AreEqual(3, FeedbackTuning.StealChunksPerVictim);
            Assert.AreEqual(12, FeedbackTuning.StealChunkPoolSize);
            Assert.AreEqual(0.35f, FeedbackTuning.StealChunkFlightSeconds);
            Assert.AreEqual(0.6f, FeedbackTuning.StealChunkArcHeight);
            Assert.AreEqual(540f, FeedbackTuning.StealChunkSpinDegreesPerSecond);
            Assert.AreEqual(0.24f, FeedbackTuning.StealChunkSize, "spec 0.16, raised after the lab frames (kernel unreadable at 0.16)");
            Assert.AreEqual(1.0f, FeedbackTuning.StealChunkHeight);
            Assert.AreEqual(5, FeedbackTuning.StealChunkSortingOrder);
            Assert.AreEqual(1.2f, FeedbackTuning.StealChunkHiddenRiseMetres);
            Assert.LessOrEqual(ChunksWanted(FeedbackTuning.ImpactMotionLineCount), FeedbackTuning.StealChunkPoolSize,
                "a three-victim robbery (9 kernels) fits the pool with room for the whiff arcs");
        }

        [Test]
        public void ChunkRoute_ThiefHiddenOrFaded_RisesAboveTheVictimAndNeverTargetsTheThief()
        {
            var victim = new Vector3(2f, 0f, 3f);
            var thief = new Vector3(-4f, 0f, 1f);
            Assert.AreEqual(ChunkRoute.ToThief, Route(false));
            Assert.AreEqual(ChunkRoute.RiseAndVanish, Route(true));

            var live = ChunkEnd(ChunkRoute.ToThief, victim, thief);
            Assert.AreEqual(thief + Vector3.up * 1.0f, live, "to the thief at chunk height");

            var hidden = ChunkEnd(ChunkRoute.RiseAndVanish, victim, thief);
            Assert.AreEqual(victim + Vector3.up * 1.2f, hidden, "the victim own position + 1.2 m up");
            Assert.AreEqual(victim.x, hidden.x);
            Assert.AreEqual(victim.z, hidden.z, "no horizontal drift toward the thief");
        }

        [Test]
        public void ChunkRoute_HiddenThief_VanishesAndDrawsNothingAtTheThief()
        {
            Assert.AreEqual(0, ArrivalSparkles(ChunkRoute.RiseAndVanish, false), "no sparkles at a hidden thief");
            Assert.AreEqual(0, ArrivalSparkles(ChunkRoute.RiseAndVanish, true));
            Assert.AreEqual(1f, ChunkAlpha(0.1f, false, ChunkRoute.RiseAndVanish), Eps);
            Assert.AreEqual(0f, ChunkAlpha(FeedbackTuning.StealChunkFlightSeconds, false, ChunkRoute.RiseAndVanish), Eps, "gone at the end");
            Assert.Greater(ChunkAlpha(0.1f, false, ChunkRoute.RiseAndVanish), ChunkAlpha(0.3f, false, ChunkRoute.RiseAndVanish));
            Assert.AreEqual(1f, ChunkAlpha(0.34f, false, ChunkRoute.ToThief), Eps, "a chunk bound for the thief stays opaque");
        }

        [Test]
        public void ChunkArrival_IsFourGoldSparkles_TwoUnderReducedMotion()
        {
            Assert.AreEqual(4, ArrivalSparkles(ChunkRoute.ToThief, false));
            Assert.AreEqual(2, ArrivalSparkles(ChunkRoute.ToThief, true));
            Assert.AreEqual(0.25f, FeedbackTuning.StealArrivalSparkleLifeSeconds);
        }

        [Test]
        public void ChunkMotion_StartsAtTheVictim_EndsAtTheThief_ArcsSixtyCentimetres_EasesIn()
        {
            var a = new Vector3(0f, 1f, 0f);
            var b = new Vector3(6f, 1f, 0f);
            Assert.AreEqual(a, ChunkPosition(a, b, 0f, Vector3.zero));
            Assert.AreEqual(b, ChunkPosition(a, b, 1f, Vector3.zero));

            // The arc peaks where the eased progress is one half: t = sqrt(0.5).
            float tMid = Mathf.Sqrt(0.5f);
            var mid = ChunkPosition(a, b, tMid, Vector3.zero);
            Assert.AreEqual(1f + 0.6f, mid.y, 1e-3f, "0.6 m above the straight line");
            Assert.AreEqual(3f, mid.x, 1e-3f);

            Assert.AreEqual(0.25f, Ease(0.5f), Eps, "ease-in-quad");
            Assert.Less(ChunkPosition(a, b, 0.5f, Vector3.zero).x, 3f, "slow start: less than half the way at half the time");
            Assert.AreEqual(540f * 0.35f, SpinDegrees(0.35f), Eps, "540 deg/s over the flight");
        }

        [Test]
        public void ChunkLateral_FansTheThreeKernelsApart_ButLandsThemOnTheThief()
        {
            var a = Vector3.zero;
            var b = new Vector3(6f, 0f, 0f);
            var left = ChunkPosition(a, b, 0.7f, Vector3.forward * -0.18f);
            var right = ChunkPosition(a, b, 0.7f, Vector3.forward * 0.18f);
            Assert.Greater(Vector3.Distance(left, right), 0.3f, "mid-flight the kernels are apart");
            Assert.AreEqual(b, ChunkPosition(a, b, 1f, Vector3.forward * 0.18f), "same landing point");
        }

        [Test]
        public void ChunkReducedMotion_FadesInAtTheThief_NoTravel_NoSpin()
        {
            Assert.AreEqual(0.2f, FeedbackTuning.StealChunkReducedFadeSeconds);
            Assert.IsFalse(ChunkTravels(true));
            Assert.IsTrue(ChunkTravels(false));
            Assert.AreEqual(0.2f, ChunkLifeSeconds(true), Eps);
            Assert.AreEqual(0.35f, ChunkLifeSeconds(false), Eps);
            Assert.AreEqual(0f, ChunkAlpha(0f, true, ChunkRoute.ToThief), Eps);
            Assert.AreEqual(0.5f, ChunkAlpha(0.1f, true, ChunkRoute.ToThief), Eps);
            Assert.AreEqual(1f, ChunkAlpha(0.2f, true, ChunkRoute.ToThief), Eps);
        }

        // ---- Pool ----

        [Test]
        public void PickSlot_TakesAFreeSlot_ElseRecyclesTheOldest_NeverGrows()
        {
            Assert.AreEqual(-1, PickSlot(new float[0]));
            Assert.AreEqual(2, PickSlot(new[] { 0.3f, 0.1f, -1f, 0.2f }), "the free slot");
            Assert.AreEqual(1, PickSlot(new[] { 0.3f, 0.34f, 0.1f, 0.2f }), "all live: the oldest");
        }

        [Test]
        public void Pool_IsBuiltOnceAtTwelve_AndRecyclesTheOldestWhenFull()
        {
            var go = new GameObject("[FoodChunkFlightTest]");
            var flight = go.AddComponent<FoodChunkFlight>();
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FoodChunkFlight).GetMethod("Awake", F).Invoke(flight, null);
                var poolField = typeof(FoodChunkFlight).GetField("_pool", F);
                var take = typeof(FoodChunkFlight).GetMethod("Take", F);
                var activate = typeof(FoodChunkFlight).GetMethod("Activate", F);

                var pool = (System.Array)poolField.GetValue(flight);
                Assert.AreEqual(FeedbackTuning.StealChunkPoolSize, pool.Length, "pool size 12 per arena");
                Assert.AreEqual(12, FoodChunkFlight.PoolSize);

                // Fill it, oldest = the last slot.
                for (int i = 0; i < pool.Length; i++)
                {
                    var m = take.Invoke(flight, null);
                    Assert.AreSame(pool.GetValue(i), m, "a free slot is taken in order");
                    activate.Invoke(flight, new[] { m });
                    m.GetType().GetField("Age").SetValue(m, (pool.Length - i) * 0.01f);
                }
                Assert.AreEqual(12, FoodChunkFlight.LiveCount);

                var recycled = take.Invoke(flight, null);
                Assert.AreSame(pool.GetValue(0), recycled, "pool full: the oldest live mover is recycled");
                activate.Invoke(flight, new[] { recycled });
                Assert.AreEqual(12, FoodChunkFlight.LiveCount, "recycling does not add a live mover");
                Assert.AreEqual(12, ((System.Array)poolField.GetValue(flight)).Length, "the pool never grows");
            }
            finally
            {
                typeof(FoodChunkFlight).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                Object.DestroyImmediate(go);
            }
        }

        // ---- One pop per event / wiring ----

        [Test]
        public void OnePopPerEvent_TheBeatDrawsNoText_AndTheCargoEdgeStillSpawnsExactlyOne()
        {
            var flightSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/FoodChunkFlight.cs");
            StringAssert.DoesNotContain("FloatingCombatText", flightSrc, "the chunk flight spawns no pop of its own");
            StringAssert.DoesNotContain("SpawnCargo", flightSrc);

            var hitSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            int start = hitSrc.IndexOf("Steal beats (D7", System.StringComparison.Ordinal);
            int end = hitSrc.IndexOf("Control-state floating text", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            Assert.Greater(end, start);
            var beat = hitSrc.Substring(start, end - start);
            StringAssert.DoesNotContain("FloatingCombatText", beat, "the steal beat adds no pop beside the cargo ROB/+n");

            var cargoSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(cargoSrc, @"FloatingCombatText\.Spawn\w*\(").Count,
                "one pop per cargo change on any peer (SpawnCargoPop: the self word on the owner, corn text for rivals)");
        }

        [Test]
        public void GrabLines_ReuseTheThreeImpactRenderers_NoNewLineRenderer()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Visuals/HitFeedback.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"TelegraphShapes\.BuildLine\(").Count,
                "the impact lines are built once; the grab lines draw on them");
            StringAssert.Contains("_grabPoints", src, "cached Vector3[] for the grab line points");
        }

        [Test]
        public void ChickenPrefab_CarriesTheCornAndWhiffSprites()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Chicken.prefab");
            Assert.IsNotNull(prefab);
            var vfx = prefab.GetComponentInChildren<ChickenVFX>(true);
            Assert.IsNotNull(vfx);
            var so = new SerializedObject(vfx);
            StringAssert.Contains("Fx_Corn", AssetDatabase.GetAssetPath(so.FindProperty("_cornSprite").objectReferenceValue));
            StringAssert.Contains("Fx_Whiff", AssetDatabase.GetAssetPath(so.FindProperty("_whiffSprite").objectReferenceValue));
        }

        [Test]
        public void ShippedSnatch_IsTheStealTheBeatsKeyOn()
        {
            var snatch = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(SnatchPath);
            Assert.IsNotNull(snatch);
            Assert.IsInstanceOf<SnatchAbilitySO>(snatch, "HitFeedback keys the grab lines and the whiff on SnatchAbilitySO");
            Assert.AreEqual(AbilityCategory.Steal, snatch.Category);
            Assert.IsTrue(snatch.ReportsCastHits, "a Snatch cast reports LastCastHitCount (hit vs whiff)");
        }
    }
}
