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
            // I5: the only text in the beat is the thief own +n, held from ChickenCargo and spawned on the kernels arrival.
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(beat, @"FloatingCombatText\.").Count,
                "the steal beat adds no pop beside the cargo ROB/+n");
            StringAssert.Contains("FloatingCombatText.SpawnCargoPop(", beat, "the held +n is the same cargo pop, only later");

            var cargoSrc = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(cargoSrc, @"FloatingCombatText\.Spawn\w*\(").Count,
                "one pop per cargo change on any peer (SpawnCargoPop: the self word on the owner, corn text for rivals)");
        }

        // ---- I5: the thief own +n lands with the corn ----

        [Test]
        public void ThiefPop_IsHeldOnTheCargoWrite_AndSpawnsOnTheLastKernelArrival()
        {
            var hold = new StealGainPopHold();
            hold.Hold(5f);                                   // the cargo write: nothing pops
            Assert.AreEqual(0f, hold.Tick(0.016f), "no pop on the cargo write");
            hold.FlightLaunched();                           // the victim kernels leave
            Assert.AreEqual(0f, hold.Tick(FeedbackTuning.StealSelfPopFallbackSeconds), "a flight is in the air: the fallback waits");
            Assert.AreEqual(0f, hold.Tick(0.05f));
            Assert.AreEqual(5f, hold.FlightArrived(), Eps, "the +5 pops as the last kernel lands");
            Assert.IsFalse(hold.IsHolding);
            Assert.AreEqual(0f, hold.Tick(1f), "exactly one pop");
        }

        [Test]
        public void ThiefPop_FlightLaunchedBeforeTheCargoChangeIsSeen_StillWaitsForIt()
        {
            var hold = new StealGainPopHold();
            hold.FlightLaunched();                           // the victim Render ran first this frame
            hold.Hold(4f);
            Assert.AreEqual(0f, hold.Tick(FeedbackTuning.StealSelfPopFallbackSeconds + 0.01f));
            Assert.AreEqual(4f, hold.FlightArrived(), Eps);
        }

        [Test]
        public void ThiefPop_TwoVictims_SumIntoOnePop_OnTheLastFlight()
        {
            var hold = new StealGainPopHold();
            hold.Hold(3f);
            hold.Hold(2f);                                   // more cargo in the queue window
            hold.FlightLaunched();
            hold.FlightLaunched();
            Assert.AreEqual(0f, hold.FlightArrived(), "one flight still in the air");
            Assert.AreEqual(5f, hold.FlightArrived(), Eps, "one summed pop");
        }

        [Test]
        public void ThiefPop_NoLiveFlight_FallsBackAt0_35s()
        {
            Assert.AreEqual(0.35f, FeedbackTuning.StealSelfPopFallbackSeconds, "one kernel flight");
            Assert.AreEqual(FeedbackTuning.StealChunkFlightSeconds, FeedbackTuning.StealSelfPopFallbackSeconds, Eps);
            var hold = new StealGainPopHold();
            hold.Hold(5f);                                   // hidden-thief reroute: no flight comes to this thief
            Assert.AreEqual(0f, hold.Tick(0.34f), "still held just before the fallback");
            Assert.AreEqual(5f, hold.Tick(0.02f), Eps, "pops at 0.35 s");
            Assert.AreEqual(0f, hold.Tick(1f));
        }

        [Test]
        public void ThiefPop_CulledFlight_FallsBackToTheTimer_AndAStuckFlightIsCapped()
        {
            var hold = new StealGainPopHold();
            hold.Hold(5f);
            hold.FlightLaunched();
            hold.FlightCulled();                             // the mover was recycled mid-air
            Assert.AreEqual(0f, hold.Tick(0.2f));
            Assert.AreEqual(5f, hold.Tick(0.2f), Eps, "culled: the 0.35 s fallback");

            var stuck = new StealGainPopHold();
            stuck.Hold(2f);
            stuck.FlightLaunched();                          // never reported as landed
            Assert.AreEqual(0f, stuck.Tick(FeedbackTuning.StealSelfPopMaxHoldSeconds - 0.01f));
            Assert.AreEqual(2f, stuck.Tick(0.02f), Eps, "a held pop is never lost");
            Assert.Greater(FeedbackTuning.StealSelfPopMaxHoldSeconds,
                FeedbackTuning.StealSelfPopFallbackSeconds + 2 * FeedbackTuning.StealChunkStaggerSeconds + FeedbackTuning.StealChunkFlightSeconds,
                "the cap never cuts a normal last kernel short");
        }

        [Test]
        public void ThiefPop_OnlyTheThiefOwnStealCreditIsHeld()
        {
            Assert.IsTrue(StealGainPopHold.IsStealCredit(10, 10), "the Render of the credit frame");
            Assert.IsTrue(StealGainPopHold.IsStealCredit(10, 11), "or the next one");
            Assert.IsFalse(StealGainPopHold.IsStealCredit(10, 12), "an older credit is some other gain: it pops at once");
            Assert.IsFalse(StealGainPopHold.IsStealCredit(-1, 10), "no credit (pile peck, Bank, a remote thief): pops at once");
        }

        [Test]
        public void ThiefPop_KernelArrivalReleasesTheHeldPop_ThroughTheFlightPool()
        {
            var go = new GameObject("[FoodChunkFlightTest]");
            var thief = new GameObject("[ThiefTest]");
            var flight = go.AddComponent<FoodChunkFlight>();
            var feedback = thief.AddComponent<HitFeedback>();
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FoodChunkFlight).GetMethod("Awake", F).Invoke(flight, null);
                var take = typeof(FoodChunkFlight).GetMethod("Take", F);
                var activate = typeof(FoodChunkFlight).GetMethod("Activate", F);
                var finish = typeof(FoodChunkFlight).GetMethod("Finish", F);
                var hold = (StealGainPopHold)typeof(HitFeedback).GetField("_stealPop", F).GetValue(feedback);

                object Kernel()
                {
                    var m = take.Invoke(flight, null);
                    var t = m.GetType();
                    t.GetField("Kind").SetValue(m, System.Enum.ToObject(t.GetField("Kind").FieldType, 0));   // Kernel
                    t.GetField("Arrives").SetValue(m, true);
                    t.GetField("Route").SetValue(m, ChunkRoute.ToThief);
                    t.GetField("ThiefFeedback").SetValue(m, feedback);
                    t.GetField("Life").SetValue(m, FeedbackTuning.StealChunkFlightSeconds);
                    activate.Invoke(flight, new[] { m });
                    feedback.NotifyStealFlightLaunched();
                    return m;
                }

                feedback.HoldStealGainPop(5f);
                var last = Kernel();
                Assert.IsTrue(hold.IsHolding);
                Assert.AreEqual(1, hold.FlightsLive);
                finish.Invoke(flight, new[] { last });
                Assert.IsFalse(hold.IsHolding, "the last kernel landing releases the held +n");
                Assert.AreEqual(0, hold.FlightsLive);

                // A recycled last kernel never lands: the hold falls back to its timer.
                feedback.HoldStealGainPop(5f);
                var culled = Kernel();
                culled.GetType().GetField("Age").SetValue(culled, 9f);   // make it the oldest
                for (int i = 1; i < FeedbackTuning.StealChunkPoolSize; i++) activate.Invoke(flight, new[] { take.Invoke(flight, null) });
                take.Invoke(flight, null);                               // pool full: recycles the culled kernel
                Assert.AreEqual(0, hold.FlightsLive, "the recycled flight is no longer counted");
                Assert.IsTrue(hold.IsHolding, "and the pop waits for the fallback");
            }
            finally
            {
                typeof(FoodChunkFlight).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(thief);
            }
        }

        [Test]
        public void ThiefPop_CargoRendersRouteOnlyTheOwnStealGain_TheVictimLossStaysImmediate()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs");
            int credit = src.IndexOf("public void RPC_SettleSteal(", System.StringComparison.Ordinal);
            Assert.Greater(credit, 0);
            StringAssert.Contains("_stealCreditFrame = Time.frameCount;", src.Substring(credit, 1800),
                "the thief credit is stamped where it is written (local, no networked signal)");

            int render = src.IndexOf("public override void Render()", System.StringComparison.Ordinal);
            int held = src.IndexOf("_hitFeedback.HoldStealGainPop(delta)", render, System.StringComparison.Ordinal);
            int pop = src.IndexOf("FloatingCombatText.SpawnCargoPop(", render, System.StringComparison.Ordinal);
            int loss = src.IndexOf("_hitFeedback?.NotifyCargoLoss(", render, System.StringComparison.Ordinal);
            Assert.Greater(held, render);
            Assert.Less(held, pop, "the own steal gain is held before the immediate pop");
            string gate = src.Substring(render, held - render);
            StringAssert.Contains("delta > 0f && StealGainPopHold.IsStealCredit(_stealCreditFrame, Time.frameCount)", gate,
                "only a gain that is this thief own credit is held: the victim -n and pile pickups pop at once");
            StringAssert.Contains("FloatingCombatText.IsSelfPopTarget(_controller)", gate, "only the thief own screen waits");
            Assert.Greater(loss, pop, "the victim loss beat follows the immediate pop, unchanged (E4a)");
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
