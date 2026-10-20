using System.Collections.Generic;
using System.Text;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Test-session instrumentation items 2-5 (docs/TEST_SESSION_PLAN_2026-10.md §2): the hitch classifier and its
    /// percentile window, the Sync heartbeat line (ordering + fixed decimals) and its tick-boundary rule, the Visual
    /// probe's classification rules and line text, and the first-use marks. The merge script parses these lines.
    /// </summary>
    public sealed class InstrumentationProbeTests
    {
        // ---- PerfProbe -------------------------------------------------------------------------------------------

        [Test]
        public void Hitch_Over50IsInfo_Over100IsWarn_ExpectedPhasesCapAtInfo()
        {
            Assert.AreEqual(HitchLevel.None, HitchRules.Classify(16.7f, false));
            Assert.AreEqual(HitchLevel.None, HitchRules.Classify(50f, false), "exactly 50 ms is not a hitch");
            Assert.AreEqual(HitchLevel.Info, HitchRules.Classify(50.1f, false));
            Assert.AreEqual(HitchLevel.Info, HitchRules.Classify(100f, false));
            Assert.AreEqual(HitchLevel.Warn, HitchRules.Classify(100.1f, false));
            Assert.AreEqual(HitchLevel.Info, HitchRules.Classify(900f, true), "loading / covered: expected, Info only");
            Assert.AreEqual(HitchLevel.None, HitchRules.Classify(float.NaN, false));
        }

        [Test]
        public void Phase_LoadingAndCoverWinOverTheMatchState()
        {
            Assert.AreEqual("loading", HitchRules.Phase(true, true, false, true, MatchState.Active, false));
            Assert.AreEqual("covered", HitchRules.Phase(false, true, false, true, MatchState.Starting, false));
            Assert.AreEqual("menu", HitchRules.Phase(false, false, false, false, MatchState.WaitingForPlayers, false));
            Assert.AreEqual("lobby", HitchRules.Phase(false, false, false, true, MatchState.WaitingForPlayers, false));
            Assert.AreEqual("starting", HitchRules.Phase(false, false, false, true, MatchState.Starting, false));
            Assert.AreEqual("intro", HitchRules.Phase(false, false, false, true, MatchState.Active, true));
            Assert.AreEqual("active", HitchRules.Phase(false, false, false, true, MatchState.Active, false));
            Assert.AreEqual("ended", HitchRules.Phase(false, false, false, true, MatchState.Ended, false));
            Assert.IsTrue(HitchRules.IsExpectedPhase("loading"));
            Assert.IsTrue(HitchRules.IsExpectedPhase("covered"));
            Assert.AreEqual("reveal-fade", HitchRules.Phase(false, false, true, true, MatchState.Active, true),
                "a hitch in the cover's fade-out is the reveal's, not the intro's");
            Assert.AreEqual("covered", HitchRules.Phase(false, true, true, true, MatchState.Active, false));
            Assert.IsTrue(HitchRules.IsExpectedPhase("reveal-fade"));
            Assert.IsFalse(HitchRules.IsExpectedPhase("active"));
        }

        [Test]
        public void FrameWindow_PercentilesAverageMaxAndHitchCounts()
        {
            var w = new FrameTimeWindow(100);
            for (int i = 1; i <= 100; i++) w.Add(i);   // 1..100 ms
            Assert.AreEqual(100, w.Count);
            Assert.AreEqual(50.5f, w.Average, 1e-4f);
            Assert.AreEqual(100f, w.Max);
            Assert.AreEqual(95f, w.Percentile(95f));
            Assert.AreEqual(99f, w.Percentile(99f));
            Assert.AreEqual(50f, w.Percentile(50f));
            Assert.AreEqual(50, w.HitchesOver50, "51..100");
            Assert.AreEqual(0, w.HitchesOver100);
            w.Clear();
            Assert.AreEqual(0, w.Count);
            Assert.AreEqual(0f, w.Percentile(95f));
        }

        [Test]
        public void FrameWindow_IsARing_KeepsTheLatestFrames()
        {
            var w = new FrameTimeWindow(4);
            foreach (var ms in new[] { 200f, 10f, 10f, 10f, 10f, 20f }) w.Add(ms);
            Assert.AreEqual(4, w.Count);
            Assert.AreEqual(20f, w.Percentile(100f), "the 200 ms frame fell out of the ring");
            Assert.AreEqual(12.5f, w.Average, 1e-4f);
            Assert.AreEqual(1, w.HitchesOver100, "hitch counts cover everything since Clear, not just the ring");
        }

        [Test]
        public void HitchLines_AtMostFivePerSecond_RestCountedForTheSummary()
        {
            var l = new HitchLineLimiter();
            for (int i = 0; i < 5; i++) Assert.IsTrue(l.Allow(10f + i * 0.1f));
            Assert.IsFalse(l.Allow(10.6f));
            Assert.IsFalse(l.Allow(10.9f));
            Assert.IsTrue(l.Allow(11.0f), "a new second");
            Assert.AreEqual(2, l.TakeSuppressed());
            Assert.AreEqual(0, l.TakeSuppressed(), "taken once, for one summary");
        }

        // ---- Sync heartbeat ---------------------------------------------------------------------------------------

        [Test]
        public void SyncLine_ExactFormat_HumansThenBots_BasesByCorner_PilesByPosition()
        {
            var chickens = new[]
            {
                new SyncChicken { Bot = true,  Key = 3, X = -10.04f, Z = 2f, Hp = 1, Cargo = 4, ControlState = "Free" },
                new SyncChicken { Bot = false, Key = 2, X = 1.26f, Z = -0.04f, Hp = 0, Cargo = 0, ControlState = "Stunned" },
                new SyncChicken { Bot = false, Key = 0, X = 5f, Z = 6.66f, Hp = 1, Cargo = 7, ControlState = "Slowed" },
            };
            var bases = new[] { new SyncBase { Corner = 2, Food = 9 }, new SyncBase { Corner = 0, Food = 12 } };
            var piles = new[]
            {
                new SyncPile { X = 5f, Z = 1f, Amount = 3f },
                new SyncPile { X = -5f, Z = 9f, Amount = 12.3f },
                new SyncPile { X = 5f, Z = -1f, Amount = 0f },
            };
            var sb = new StringBuilder();
            SyncHeartbeatFormat.Build(sb, 1280, "Active", 31.26f, chickens, 3, bases, 2, piles, 3);
            Assert.AreEqual(
                "Sync t=1280 st=Active rem=31.3 | P0:x=5.0,z=6.7,hp=1,cg=7,cs=Slowed ; P2:x=1.3,z=0.0,hp=0,cg=0,cs=Stunned ; " +
                "B3:x=-10.0,z=2.0,hp=1,cg=4,cs=Free,bot | bases=0=12,2=9 | piles=12.3,0.0,3.0",
                sb.ToString());
        }

        [Test]
        public void SyncLine_EmptyWorld_StillParses()
        {
            var sb = new StringBuilder();
            SyncHeartbeatFormat.Build(sb, 0, "WaitingForPlayers", 0f, new SyncChicken[0], 0, new SyncBase[0], 0, new SyncPile[0], 0);
            Assert.AreEqual("Sync t=0 st=WaitingForPlayers rem=0.0 |  | bases= | piles=", sb.ToString());
            Assert.AreEqual("0.0", SyncHeartbeatFormat.F1(-0.01f), "never -0.0");
        }

        [Test]
        public void TickBoundary_SameTicksOnEveryPeer_SkippedTicksKeyedByTheBoundary()
        {
            var b = new TickBoundary();
            const int period = 64;   // 2 s at 32 Hz
            Assert.IsFalse(b.TryCross(100, period, out _), "attaching mid-period does not log the passed boundary");
            Assert.IsFalse(b.TryCross(127, period, out _));
            Assert.IsTrue(b.TryCross(128, period, out int at));
            Assert.AreEqual(128, at);
            Assert.IsFalse(b.TryCross(128, period, out _), "once per boundary");
            Assert.IsFalse(b.TryCross(191, period, out _));
            Assert.IsTrue(b.TryCross(195, period, out at), "a frame that skipped tick 192 logs at the first tick after it");
            Assert.AreEqual(192, at, "keyed by the boundary, not the tick it was seen on");
            Assert.IsTrue(b.TryCross(400, period, out at));
            Assert.AreEqual(384, at, "several boundaries skipped: the latest one");
            Assert.IsTrue(b.TryCross(64, period, out at), "a new runner (tick went back) on a boundary");
            Assert.AreEqual(64, at);
            Assert.IsFalse(b.TryCross(10, period, out _), "a lower tick re-attaches mid-period: no line");
            Assert.IsFalse(new TickBoundary().TryCross(5, 0, out _));
            Assert.IsTrue(new TickBoundary().TryCross(0, period, out at), "attaching exactly on a boundary logs it");
        }

        // ---- Visual probe -----------------------------------------------------------------------------------------

        [Test]
        public void ErrorShaders_AreTheErrorAndBuiltInRpShaders_NotUrpOnes()
        {
            Assert.IsTrue(VisualScanRules.IsErrorShaderName("Hidden/InternalErrorShader"));
            Assert.IsTrue(VisualScanRules.IsErrorShaderName("Hidden/Universal Render Pipeline/FallbackError"));
            Assert.IsTrue(VisualScanRules.IsErrorShaderName("Standard"));
            Assert.IsTrue(VisualScanRules.IsErrorShaderName("Legacy Shaders/Diffuse"));
            Assert.IsTrue(VisualScanRules.IsErrorShaderName(null));
            Assert.IsFalse(VisualScanRules.IsErrorShaderName("Universal Render Pipeline/Lit"));
            Assert.IsFalse(VisualScanRules.IsErrorShaderName("Shader Graphs/CW_Ghost"));
            Assert.IsFalse(VisualScanRules.IsErrorShaderName("Sprites/Default"));
            Assert.IsFalse(VisualScanRules.IsErrorShaderName("TextMeshPro/Distance Field"));
        }

        [Test]
        public void ShaderVerdict_ErrorNamesWinOverSupport_AndTheRevealScanWaitsForPlay()
        {
            Assert.AreEqual(ShaderVerdict.Error, VisualScanRules.Classify("Hidden/InternalErrorShader", false));
            Assert.AreEqual(ShaderVerdict.Error, VisualScanRules.Classify("Standard", true));
            Assert.AreEqual(ShaderVerdict.Unsupported, VisualScanRules.Classify("Universal Render Pipeline/Lit", false));
            Assert.AreEqual(ShaderVerdict.Ok, VisualScanRules.Classify("Universal Render Pipeline/Lit", true));
            Assert.IsFalse(VisualScanRules.RevealScanDue(0f));
            Assert.IsFalse(VisualScanRules.RevealScanDue(3.4f));
            Assert.IsTrue(VisualScanRules.RevealScanDue(3.5f), "3-4 s of uncovered play after the reveal");
        }

        [Test]
        public void Transforms_NaNOrInfinityAreFlagged()
        {
            Assert.IsTrue(VisualScanRules.IsNonFinite(new Vector3(float.NaN, 0, 0)));
            Assert.IsTrue(VisualScanRules.IsNonFinite(new Vector3(0, float.PositiveInfinity, 0)));
            Assert.IsTrue(VisualScanRules.IsNonFinite(new Quaternion(0, 0, float.NaN, 1)));
            Assert.IsFalse(VisualScanRules.IsNonFinite(Vector3.one));
            Assert.IsFalse(VisualScanRules.IsNonFinite(Quaternion.identity));
        }

        [Test]
        public void Bounds_ArenaPlusMarginAndAHeightBand()
        {
            const float half = 25.65f;
            Assert.IsFalse(VisualScanRules.IsOutOfBounds(new Vector3(half + 19f, 0f, 0f), half), "surround art is inside the margin");
            Assert.IsTrue(VisualScanRules.IsOutOfBounds(new Vector3(0f, 0f, -(half + 21f)), half));
            Assert.IsTrue(VisualScanRules.IsOutOfBounds(new Vector3(0f, -25f, 0f), half), "fell through the floor");
            Assert.IsTrue(VisualScanRules.IsOutOfBounds(new Vector3(0f, 81f, 0f), half));
            Assert.IsTrue(VisualScanRules.IsOutOfBounds(new Vector3(float.NaN, 0f, 0f), half));
            Assert.IsTrue(VisualScanRules.IsLayerVisible(1 << 8, 8));
            Assert.IsFalse(VisualScanRules.IsLayerVisible(1 << 8, 9), "a layer the match camera does not draw is never out of bounds");
            Assert.IsFalse(VisualScanRules.IsLayerVisible(-1, 32));
        }

        [Test]
        public void OutOfBounds_GameplayObjectsOnly_NeverTheSurroundArt()
        {
            Assert.IsTrue(VisualScanRules.CountsForBounds(true, "Chicken(Clone)"), "a chicken outside the arena is a bug");
            Assert.IsTrue(VisualScanRules.CountsForBounds(true, "FoodPile(Clone)"));
            Assert.IsFalse(VisualScanRules.CountsForBounds(false, "MapSurroundings"), "the surround is designed to be outside");
            Assert.IsFalse(VisualScanRules.CountsForBounds(true, "MapSurroundings"), "even a networked object parented under it");
            Assert.IsFalse(VisualScanRules.CountsForBounds(false, "Environment"), "static scenery is never a gameplay offender");
        }

        [Test]
        public void VisualLines_SummaryAndOffendersWithCap()
        {
            var counts = new[] { 2, 0, 1, 0, 0, 1 };
            Assert.AreEqual(
                "Visual scan#3 reason=reveal renderers=412 ms=7.5 errShader=2 unsupported=0 nullMat=1 nan=0 outOfBounds=0 " +
                "chickensNoRenderer=1 cams=2 mask=0x0000FFFF cover=0 offenders=4 frames=4",
                VisualScanRules.Summary(3, "reveal", 412, 7.46f, counts, 2, 0xFFFF, false, 4));

            var paths = new List<string>();
            for (int i = 0; i < 8; i++) paths.Add("Arena/Fence" + i);
            Assert.AreEqual("Visual errShader n=10: Arena/Fence0, Arena/Fence1, Arena/Fence2, Arena/Fence3, Arena/Fence4, " +
                            "Arena/Fence5, Arena/Fence6, Arena/Fence7 +2 more",
                VisualScanRules.OffenderLine(VisualCategory.ErrShader, 10, paths));
            Assert.AreEqual("Visual nan n=1: Chicken(Clone)", VisualScanRules.OffenderLine(VisualCategory.Nan, 1, new[] { "Chicken(Clone)" }));
        }

        [Test]
        public void HierarchyPath_RootToLeaf()
        {
            var root = new GameObject("Root");
            try
            {
                var mid = new GameObject("Mid").transform;
                mid.SetParent(root.transform);
                var leaf = new GameObject("Leaf").transform;
                leaf.SetParent(mid);
                Assert.AreEqual("Root/Mid/Leaf", VisualScanRules.PathOf(leaf));
                Assert.AreEqual("(null)", VisualScanRules.PathOf(null));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- deposit diagnostics (Source Base) ------------------------------------------------------------------------

        [Test]
        public void DepositLines_TellWhichHostCheckFailed()
        {
            Assert.AreEqual("Base corner=3 owner=[Player:2] pos=21.8,-21.8 radius=2.50 stamped=1",
                DepositDiagnostics.BaseLine(3, "[Player:2]", new Vector3(21.8f, 0f, -21.8f), 2.5f, true));
            Assert.AreEqual("Base flush chicken=[Player:2] home=3 pos=8.2,8.2 base=3 basePos=9.5,9.4 dist=1.73 radius=2.50 in=1 amount=2.25",
                DepositDiagnostics.FlushLine("[Player:2]", 3, new Vector3(8.2f, 1f, 8.2f), 3, new Vector3(9.47f, 0f, 9.38f), 2.5f, 2.25f));
            Assert.AreEqual("{[Player:2] pos=8.2,8.2 home=3 allegiance=1 attributable=1 dist=32.94 limit=3.10 inRange=0}",
                DepositDiagnostics.Candidate("[Player:2]", new Vector3(8.2f, 0f, 8.2f), 3, true, true, new Vector3(21.8f, 0f, -21.8f), 2.5f, 0.6f));
            Assert.AreEqual(1.0f, DepositDiagnostics.PlanarDistance(new Vector3(0f, 5f, 0f), new Vector3(1f, 0f, 0f)), 1e-5f, "planar: height ignored");
        }

        [Test]
        public void RespawnCheckLine()
        {
            Assert.AreEqual("Respawn check chicken=P1 home=0 pos=0.1,19.3 dist_to_corner=21.92 round=2",
                SyncHeartbeat.RespawnLine("P1", 0, new Vector3(0.1f, 0f, 19.3f), "21.92", 2));
        }

        [Test]
        public void RejectionLines_OnePerCallerPerTwoSeconds()
        {
            var l = new PerKeyRateLimiter(2f);
            Assert.IsTrue(l.Allow(2, 10f, out int held));
            Assert.AreEqual(0, held);
            Assert.IsFalse(l.Allow(2, 10.25f, out _));
            Assert.IsFalse(l.Allow(2, 11.9f, out _));
            Assert.IsTrue(l.Allow(3, 10.3f, out _), "another caller has its own budget");
            Assert.IsTrue(l.Allow(2, 12.0f, out held));
            Assert.AreEqual(2, held, "the held-back lines are counted on the next one");
        }

        // ---- breadcrumbs ------------------------------------------------------------------------------------------

        [Test]
        public void FirstUse_EachKindOncePerSceneLoad()
        {
            FirstUseMarkers.ResetForScene();
            try
            {
                FirstUseMarkers.Note(FirstUseKind.Telegraph);
                FirstUseMarkers.Note(FirstUseKind.HitSpark);
                FirstUseMarkers.Note(FirstUseKind.Telegraph);
                Assert.IsTrue(FirstUseMarkers.TryTake(out var k, out _));
                Assert.AreEqual(FirstUseKind.HitSpark, k);
                Assert.IsTrue(FirstUseMarkers.TryTake(out k, out _));
                Assert.AreEqual(FirstUseKind.Telegraph, k);
                Assert.IsFalse(FirstUseMarkers.TryTake(out _, out _), "a second telegraph is not a first use");
                FirstUseMarkers.Note(FirstUseKind.HitSpark);
                Assert.IsFalse(FirstUseMarkers.TryTake(out _, out _));

                FirstUseMarkers.ResetForScene();
                FirstUseMarkers.Note(FirstUseKind.HitSpark);
                Assert.IsTrue(FirstUseMarkers.TryTake(out k, out _), "a new scene load starts over");
                Assert.AreEqual("hitspark", FirstUseMarkers.Name(k));
                Assert.AreEqual("fencehole", FirstUseMarkers.Name(FirstUseKind.FenceHole));
                Assert.AreEqual("invisibility", FirstUseMarkers.Name(FirstUseKind.Invisibility));
            }
            finally
            {
                FirstUseMarkers.ResetForScene();
            }
        }

        [Test]
        public void RevealState_LoadingWindowAndDescribe()
        {
            bool inFlight = RevealState.SceneLoadInFlight;
            float lastLoaded = RevealState.LastSceneLoadedRealtime;
            try
            {
                RevealState.SceneLoadInFlight = false;
                RevealState.LastSceneLoadedRealtime = 10f;
                Assert.IsTrue(RevealState.IsLoading(10.5f));
                Assert.IsFalse(RevealState.IsLoading(11.5f));
                RevealState.SceneLoadInFlight = true;
                Assert.IsTrue(RevealState.IsLoading(100f));
                RevealState.ResetForScene();
                StringAssert.AreEqualIgnoringCase("cover=0 revealed=0", RevealState.Describe());
                Assert.IsFalse(RevealState.InRevealFade(0f), "no reveal yet");
                RevealState.FadeEndsRealtime = 20f;
                Assert.IsTrue(RevealState.InRevealFade(19.9f), "frame started inside the fade");
                Assert.IsFalse(RevealState.InRevealFade(20f));
                RevealState.ResetForScene();
            }
            finally
            {
                RevealState.SceneLoadInFlight = inFlight;
                RevealState.LastSceneLoadedRealtime = lastLoaded;
            }
        }
    }
}
