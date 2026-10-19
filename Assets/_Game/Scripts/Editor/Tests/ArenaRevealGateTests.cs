using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// "The arena is revealed only when fully ready": the readiness gates (<see cref="ArenaReadinessTracker"/>), the
    /// deterministic warm-up (<see cref="ArenaWarmupSequence"/>, <see cref="ArenaWarmupFraming"/>), the cover decision
    /// (<see cref="ArenaCoverRule"/>), and the invariants that tie them to the networked intro gate
    /// (<see cref="IntroArmGate"/>, <see cref="SettleTracker"/>).
    /// </summary>
    public sealed class ArenaRevealGateTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private const float Frame = 1f / 60f;

        // ---- ArenaReadinessTracker -------------------------------------------------------------------

        private static readonly ArenaGate[] AllGates = { ArenaGate.Scene, ArenaGate.Tier, ArenaGate.Warmup, ArenaGate.Camera };

        private static bool Feed(ArenaReadinessTracker t, float seconds, ICollection<ArenaGate> holding) =>
            t.Tick(seconds, holding.Contains(ArenaGate.Scene), holding.Contains(ArenaGate.Tier),
                holding.Contains(ArenaGate.Warmup), holding.Contains(ArenaGate.Camera));

        private static void Permute(List<ArenaGate> head, List<ArenaGate> rest, List<List<ArenaGate>> into)
        {
            if (rest.Count == 0) { into.Add(new List<ArenaGate>(head)); return; }
            for (int i = 0; i < rest.Count; i++)
            {
                var next = new List<ArenaGate>(rest);
                next.RemoveAt(i);
                head.Add(rest[i]);
                Permute(head, next, into);
                head.RemoveAt(head.Count - 1);
            }
        }

        [Test]
        public void Tracker_IsNotReady_UntilEveryGateIsDone()
        {
            var t = new ArenaReadinessTracker();
            Assert.IsFalse(t.Tick(0f, false, false, false, false));
            Assert.IsFalse(t.Tick(1f, true, true, true, false), "the camera gate is still pending");
            Assert.AreEqual("Camera", t.PendingGates);
            Assert.IsTrue(t.Tick(2f, true, true, true, true));
            Assert.IsFalse(t.TimedOut);
            Assert.AreEqual(string.Empty, t.PendingGates);
        }

        [Test]
        public void Tracker_WaitsForEveryGate_InAnyOrder_AndRecordsTimesAndTheLastGate()
        {
            var perms = new List<List<ArenaGate>>();
            Permute(new List<ArenaGate>(), new List<ArenaGate>(AllGates), perms);
            Assert.AreEqual(24, perms.Count);
            foreach (var order in perms)
            {
                var t = new ArenaReadinessTracker();
                var holding = new List<ArenaGate>();
                for (int k = 0; k < order.Count; k++)
                {
                    holding.Add(order[k]);
                    bool ready = Feed(t, (k + 1) * 0.5f, holding);
                    Assert.AreEqual(k == order.Count - 1, ready, $"order {string.Join(">", order)} step {k}");
                }
                Assert.AreEqual(order[order.Count - 1].ToString(), t.LastGate, "the gate that completed last");
                Assert.AreEqual(2f, t.ReadyAtSeconds, 1e-5f);
                for (int k = 0; k < order.Count; k++)
                    Assert.AreEqual((k + 1) * 0.5f, t.DoneAt(order[k]), 1e-5f, $"{order[k]} done time");
            }
        }

        [Test]
        public void Tracker_GatesLatch_ADroppedConditionDoesNotUndoAGate()
        {
            var t = new ArenaReadinessTracker();
            t.Tick(1f, true, false, false, false);
            t.Tick(2f, false, true, false, false);
            Assert.IsTrue(t.IsDone(ArenaGate.Scene), "Scene stays done although its condition dropped");
            Assert.AreEqual(1f, t.DoneAt(ArenaGate.Scene), 1e-5f, "and keeps the time it first held");
            Assert.IsTrue(t.Tick(3f, false, false, true, true));
        }

        [Test]
        public void Tracker_RaisesEachGateOnce_WithItsTime()
        {
            var t = new ArenaReadinessTracker();
            var seen = new List<string>();
            t.GateCompleted += (g, s) => seen.Add(g + "@" + s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            t.Tick(1f, true, false, false, false);
            t.Tick(2f, true, true, false, false);
            t.Tick(3f, true, true, true, true);
            t.Tick(4f, true, true, true, true);
            CollectionAssert.AreEqual(new[] { "Scene@1.0", "Tier@2.0", "Warmup@3.0", "Camera@3.0" }, seen);
        }

        [Test]
        public void Tracker_TimesOutAtTheCap_NamesThePendingGates_AndIsReadyAnyway()
        {
            var t = new ArenaReadinessTracker();
            t.Tick(1f, true, false, false, false);
            t.Tick(2f, true, true, false, false);
            Assert.IsFalse(t.Tick(ArenaReadinessTracker.MaxSeconds - 0.01f, true, true, false, false), "not before the cap");
            Assert.IsTrue(t.Tick(ArenaReadinessTracker.MaxSeconds, true, true, false, false), "ready at the cap");
            Assert.IsTrue(t.TimedOut);
            Assert.AreEqual("Warmup, Camera", t.PendingGates);
            Assert.AreEqual("Tier", t.LastGate);
            Assert.AreEqual(ArenaReadinessTracker.MaxSeconds, t.ReadyAtSeconds, 1e-5f);
        }

        [Test]
        public void Tracker_AllGatesOnTheCapFrame_IsReadyWithoutATimeout()
        {
            var t = new ArenaReadinessTracker();
            Assert.IsTrue(t.Tick(ArenaReadinessTracker.MaxSeconds, true, true, true, true));
            Assert.IsFalse(t.TimedOut);
        }

        [Test]
        public void Tracker_Latches_LaterTicksChangeNothing()
        {
            var t = new ArenaReadinessTracker();
            t.Tick(ArenaReadinessTracker.MaxSeconds, true, false, false, false);
            float at = t.ReadyAtSeconds;
            Assert.IsTrue(t.Tick(99f, true, true, true, true));
            Assert.AreEqual(at, t.ReadyAtSeconds);
            Assert.IsTrue(t.TimedOut, "the timeout stays recorded");
        }

        [Test]
        public void NullReadiness_IsAlwaysReady_SoAMissingBindingNeverStrandsTheMatch()
        {
            Assert.IsTrue(NullArenaReadiness.Instance.IsReady);
            Assert.IsFalse(NullArenaReadiness.Instance.TimedOut);
            Assert.AreEqual(string.Empty, NullArenaReadiness.Instance.PendingGates);
        }

        // ---- ArenaWarmupSequence ---------------------------------------------------------------------

        private sealed class FakeProbe : IShaderCompileProbe { public bool Compiling; public bool IsCompiling => Compiling; }

        private static List<string> Record(ArenaWarmupSequence s)
        {
            var log = new List<string>();
            s.StepChanged += (from, to) => log.Add(to.ToString() + (s.LastStepTimedOut ? "!" : string.Empty));
            return log;
        }

        private static int TicksUntilLeaves(ArenaWarmupSequence s, WarmupStep step, float dt, int max = 10000)
        {
            for (int i = 1; i <= max; i++)
            {
                s.Tick(dt);
                if (s.Step != step) return i;
            }
            return -1;
        }

        [Test]
        public void Warmup_RunsOverviewVariants_ThenPlain_ThenRestore_ThenDone()
        {
            var s = new ArenaWarmupSequence(new FakeProbe());
            var log = Record(s);
            Assert.AreEqual(WarmupStep.NotStarted, s.Step);
            Assert.IsTrue(s.Begin());
            for (int i = 0; i < 40; i++) s.Tick(Frame);
            CollectionAssert.AreEqual(new[] { "OverviewVariants", "OverviewPlain", "Restore", "Done" }, log);
            Assert.AreEqual(WarmupStep.Done, s.Step);
            Assert.IsFalse(s.IsRunning);
        }

        [Test]
        public void Warmup_HoldsEachOverviewStepForAtLeastTheMinimumFrames()
        {
            var s = new ArenaWarmupSequence(new FakeProbe());
            s.Begin();
            Assert.AreEqual(ArenaWarmupSequence.MinFrames, TicksUntilLeaves(s, WarmupStep.OverviewVariants, Frame));
            Assert.AreEqual(ArenaWarmupSequence.MinFrames, TicksUntilLeaves(s, WarmupStep.OverviewPlain, Frame));
            Assert.AreEqual(1, TicksUntilLeaves(s, WarmupStep.Restore, Frame), "the restore step is one frame");
            Assert.AreEqual(2 * ArenaWarmupSequence.MinFrames, s.OverviewFrames);
        }

        [Test]
        public void Warmup_WaitsWhileTheCompileProbeReportsCompiling_ThenMovesOn()
        {
            var probe = new FakeProbe { Compiling = true };
            var s = new ArenaWarmupSequence(probe);
            s.Begin();
            for (int i = 0; i < 100; i++) s.Tick(Frame);
            Assert.AreEqual(WarmupStep.OverviewVariants, s.Step, "still compiling: held (1.7 s, under the cap)");
            Assert.Greater(s.CompileWaitSeconds, 1.5f);
            probe.Compiling = false;
            s.Tick(Frame);
            Assert.AreEqual(WarmupStep.OverviewPlain, s.Step, "the first quiet frame ends the step");
            Assert.AreEqual(0, s.CappedSteps);
        }

        [Test]
        public void Warmup_ACompileBeforeTheMinimumFramesIsNotWaitTime()
        {
            var probe = new FakeProbe { Compiling = true };
            var s = new ArenaWarmupSequence(probe);
            s.Begin();
            for (int i = 0; i < ArenaWarmupSequence.MinFrames - 1; i++) s.Tick(Frame);
            Assert.AreEqual(0f, s.CompileWaitSeconds, 1e-6f);
        }

        [Test]
        public void Warmup_EachStepHasACap_AStuckCompileCannotHangIt()
        {
            var s = new ArenaWarmupSequence(new FakeProbe { Compiling = true });
            var log = Record(s);
            s.Begin();
            int a = TicksUntilLeaves(s, WarmupStep.OverviewVariants, 0.5f);
            Assert.AreEqual(Mathf.CeilToInt(ArenaWarmupSequence.StepCapSeconds / 0.5f), a, "cut at the cap");
            int b = TicksUntilLeaves(s, WarmupStep.OverviewPlain, 0.5f);
            Assert.Greater(b, 0);
            s.Tick(0.5f);
            CollectionAssert.AreEqual(new[] { "OverviewVariants", "OverviewPlain!", "Restore!", "Done" }, log, "! marks a step the cap ended");
            Assert.AreEqual(2, s.CappedSteps);
        }

        [Test]
        public void Warmup_BeginIsANoOpWhileRunning_AndRerunsAfterDone_ForALateClass()
        {
            var s = new ArenaWarmupSequence(new FakeProbe());
            var log = Record(s);
            s.Begin();
            Assert.IsFalse(s.Begin(), "no restart mid-run");
            for (int i = 0; i < 20; i++) s.Tick(Frame);
            Assert.AreEqual(WarmupStep.Done, s.Step);
            Assert.IsTrue(s.Begin(), "a chicken of a class not yet shown: run again");
            Assert.AreEqual(WarmupStep.OverviewVariants, s.Step);
            for (int i = 0; i < 20; i++) s.Tick(Frame);
            Assert.AreEqual(WarmupStep.Done, s.Step);
            Assert.AreEqual(4 * ArenaWarmupSequence.MinFrames, s.OverviewFrames, "frames add up over both runs");
            Assert.AreEqual(8, log.Count, "two full runs: A, B, Restore, Done each");
        }

        [Test]
        public void Warmup_AbortJumpsToDone_AndSaysSo()
        {
            var s = new ArenaWarmupSequence(new FakeProbe { Compiling = true });
            var log = Record(s);
            s.Begin();
            s.Tick(Frame);
            s.Abort();
            Assert.AreEqual(WarmupStep.Done, s.Step);
            Assert.AreEqual("Done", log[log.Count - 1]);
            s.Abort();   // idempotent
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Warmup_DeviceBuildsHaveNoCompileProbe()
        {
            Assert.IsFalse(NullShaderCompileProbe.Instance.IsCompiling);
            var s = new ArenaWarmupSequence(null);
            s.Begin();
            Assert.AreEqual(ArenaWarmupSequence.MinFrames, TicksUntilLeaves(s, WarmupStep.OverviewVariants, Frame));
        }

        // ---- ArenaWarmupFraming ----------------------------------------------------------------------

        [Test]
        public void Framing_FitsTheWholeArenaPlusSurround_ForEveryAspectAndSeat()
        {
            float half = 25.65f, margin = ArenaReadinessDriver.SurroundMargin, height = ArenaReadinessDriver.SurroundHeight;
            foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 21f / 9f, 9f / 16f, 1f })
            foreach (float yaw in new[] { 45f, 135f, 225f, 315f })
            {
                var f = ArenaWarmupFraming.Compute(half, margin, height, yaw, 30f, aspect, 15f);
                var rot = Quaternion.Euler(30f, yaw, 0f);
                var cam = f.Focus - rot * Vector3.forward * f.Distance;
                float h = half + margin;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? 0f : height, (i & 4) == 0 ? -h : h);
                    var local = Quaternion.Inverse(rot) * (c - cam);
                    Assert.LessOrEqual(Mathf.Abs(local.y), f.OrthoSize + 1e-3f, $"aspect {aspect:0.00} yaw {yaw}: corner {i} vertically inside");
                    Assert.LessOrEqual(Mathf.Abs(local.x), f.OrthoSize * aspect + 1e-3f, $"aspect {aspect:0.00} yaw {yaw}: corner {i} horizontally inside");
                    Assert.GreaterOrEqual(local.z, ArenaWarmupFraming.MinDepth - 1e-3f, $"aspect {aspect:0.00} yaw {yaw}: corner {i} in front of the camera");
                }
                Assert.GreaterOrEqual(f.Distance, 15f, "never closer than the match camera");
            }
        }

        [Test]
        public void Framing_IsCenteredOnTheArena_AndZoomedOutFarBeyondTheMatchFraming()
        {
            var f = ArenaWarmupFraming.Compute(25.65f, ArenaReadinessDriver.SurroundMargin, ArenaReadinessDriver.SurroundHeight, 45f, 30f, 16f / 9f, 15f);
            Assert.AreEqual(0f, f.Focus.x, 1e-4f);
            Assert.AreEqual(0f, f.Focus.z, 1e-4f);
            Assert.Greater(f.OrthoSize, 5.6f * 3f, "the match frames ~10 m; the overview frames the whole arena");
        }

        // ---- ArenaCoverRule --------------------------------------------------------------------------

        private static ArenaCoverRule.Decision Cover(bool ready = false, bool manager = true, MatchState state = MatchState.Starting,
            bool lobby = false, bool matchEnd = false, bool leaving = false, bool noManagerTimeout = false,
            bool revealed = false, float covered = 0f) =>
            ArenaCoverRule.Decide(ready, manager, state, lobby, matchEnd, leaving, noManagerTimeout, revealed, covered);

        [Test]
        public void Cover_IsUpFromTheFirstFrame_BeforeAnyManagerExists_EvenIfLocallyReady()
        {
            var d = Cover(ready: false, manager: false, state: MatchState.WaitingForPlayers);
            Assert.IsTrue(d.CoverShown);
            Assert.IsFalse(d.Reveal);
            d = Cover(ready: true, manager: false, state: MatchState.WaitingForPlayers);
            Assert.IsTrue(d.CoverShown && !d.Reveal, "ready is not enough without the round's intro");
        }

        [Test]
        public void Cover_StaysUpThroughStarting_EvenWhenLocallyReady_NobodyRevealsBeforeTheNetworkedThree()
        {
            var d = Cover(ready: true, state: MatchState.Starting);
            Assert.IsTrue(d.CoverShown);
            Assert.IsFalse(d.Reveal, "every peer reveals on the networked 3, never while still in Starting");
        }

        [Test]
        public void Cover_Reveals_WhenReadyAndTheIntroIsArmed()
        {
            var d = Cover(ready: true, state: MatchState.Active);
            Assert.IsTrue(d.Reveal);
            Assert.IsFalse(d.CoverShown);
            Assert.IsFalse(d.HardCap);
        }

        [Test]
        public void Cover_StaysUpThroughTheIntro_WhenNotReady_TheDigitsPlayOverIt_ThenRevealsWhenReady()
        {
            var d = Cover(ready: false, state: MatchState.Active, covered: 4f);
            Assert.IsTrue(d.CoverShown, "the held / faked countdown: digits over the opaque cover");
            Assert.IsFalse(d.Reveal);
            Assert.IsTrue(Cover(ready: true, state: MatchState.Active, covered: 6f).Reveal, "reveals the moment this peer is ready");
        }

        [Test]
        public void Cover_NeverHidesTheWaitingRoom_NorTheMatchEndScreen()
        {
            foreach (var state in new[] { MatchState.WaitingForPlayers, MatchState.Starting, MatchState.Active, MatchState.Ended })
            {
                var lobby = Cover(ready: false, state: state, lobby: true);
                Assert.IsFalse(lobby.CoverShown, $"under the opaque lobby ({state})");
                Assert.IsFalse(lobby.Reveal, $"the lobby does not reveal ({state}, not ready)");
                var end = Cover(ready: false, state: state, matchEnd: true);
                Assert.IsFalse(end.CoverShown, $"under the opaque match-end screen ({state})");
            }
        }

        [Test]
        public void Cover_ARevealIsLatched_LaterRoundsKeepTheirOwnCards()
        {
            foreach (var state in new[] { MatchState.WaitingForPlayers, MatchState.Starting, MatchState.Active, MatchState.Ended })
            {
                var d = Cover(ready: false, state: state, revealed: true);
                Assert.IsFalse(d.CoverShown, $"already revealed: PLAY AGAIN's {state} never re-covers");
                Assert.IsFalse(d.Reveal);
            }
        }

        [Test]
        public void Cover_StaysOut_WhenLeavingOrTheSessionEnds()
        {
            Assert.IsFalse(Cover(leaving: true, manager: false, state: MatchState.WaitingForPlayers).CoverShown);
            Assert.IsFalse(Cover(leaving: true, state: MatchState.Starting).CoverShown);
        }

        [Test]
        public void Cover_Reveals_WhenNoManagerEverArrives()
        {
            var d = Cover(manager: false, state: MatchState.WaitingForPlayers, noManagerTimeout: true);
            Assert.IsTrue(d.Reveal, "the card is dropped on the no-manager timeout so the player is not stuck behind it");
            Assert.IsFalse(d.CoverShown);
        }

        [Test]
        public void Cover_HasAHardSafetyCap_ThatSaysSo()
        {
            var below = Cover(ready: false, state: MatchState.Starting, covered: ArenaCoverRule.HardCapSeconds - 0.1f);
            Assert.IsTrue(below.CoverShown);
            Assert.IsFalse(below.Reveal);
            var at = Cover(ready: false, state: MatchState.Starting, covered: ArenaCoverRule.HardCapSeconds);
            Assert.IsTrue(at.Reveal);
            Assert.IsTrue(at.HardCap, "the controller logs an Error for it");
            Assert.IsFalse(Cover(ready: true, state: MatchState.Active, covered: ArenaCoverRule.HardCapSeconds).HardCap,
                "a normal reveal that lands on the cap frame is not a cap");
        }

        [Test]
        public void Cover_LateJoinerIntoARunningMatch_IsCoveredUntilReady()
        {
            Assert.IsTrue(Cover(ready: false, state: MatchState.Active, covered: 2f).CoverShown);
            Assert.IsTrue(Cover(ready: true, state: MatchState.Active, covered: 3f).Reveal);
            Assert.IsTrue(Cover(ready: true, state: MatchState.Ended, covered: 3f).Reveal, "joined during the end screen's round");
        }

        // ---- No match start before reveal ------------------------------------------------------------

        [Test]
        public void Settle_IsNotReportedBeforeTheArenaIsReady_ThenSettlesAfterItsSmoothFrames()
        {
            var settle = new SettleTracker();
            for (int i = 0; i < 600; i++)
                Assert.IsFalse(settle.Observe(MatchFlowRules.LocalWorldUp(ownChickenUp: true, arenaReady: false), Frame),
                    "a peer whose arena is not ready never reports settled, however smooth its frames are");
            int frames = 0;
            while (!settle.Observe(MatchFlowRules.LocalWorldUp(true, true), Frame) && ++frames < 1000) { }
            Assert.AreEqual(SettleTracker.SettleSeconds, (frames + 1) * Frame, 2 * Frame, "the 0.4 s settle runs AFTER readiness");
        }

        [Test]
        public void LocalWorldUp_NeedsTheChickenAndTheArena()
        {
            Assert.IsFalse(MatchFlowRules.LocalWorldUp(false, true));
            Assert.IsFalse(MatchFlowRules.LocalWorldUp(true, false));
            Assert.IsTrue(MatchFlowRules.LocalWorldUp(true, true));
        }

        [Test]
        public void IntroArmGateCap_OutlastsTheReadinessCapPlusTheSettle_WithMargin()
        {
            float needed = ArenaReadinessTracker.MaxSeconds + SettleTracker.SettleSeconds;
            Assert.Greater(IntroArmGate.MaxWaitSeconds, needed + 2f,
                "in solo the state authority IS the local peer: its cap must never arm the intro while its own cover is still up");
            Assert.Less(ArenaReadinessTracker.MaxSeconds, ArenaCoverRule.HardCapSeconds, "readiness times out before the cover's safety cap");
            Assert.Less(IntroArmGate.WaitingNoticeSeconds, IntroArmGate.MaxWaitSeconds);
        }

        [Test]
        public void GameManager_ReportsSettled_OnlyThroughTheReadinessGate_AndFallsBackWithAnError()
        {
            string src = Read("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            StringAssert.Contains("MatchFlowRules.LocalWorldUp(LocalChickenIsUp(), ArenaIsReady())", src);
            StringAssert.Contains("IArenaReadiness", src);
            StringAssert.Contains("not bound", src, "a missing binding is a wiring bug: Error, then the old own-chicken-only behaviour");
        }

        // ---- Warm-up overrides (presentation only) ---------------------------------------------------

        private GameObject _chicken;

        [TearDown]
        public void TearDown()
        {
            if (_chicken != null) Object.DestroyImmediate(_chicken);
        }

        private ChickenVisuals MakeChicken(out Renderer body, out Material original)
        {
            original = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Chicken_Warrior.mat");
            _chicken = new GameObject("Arena_TestChicken");
            var model = new GameObject("Model");
            model.transform.SetParent(_chicken.transform);
            body = model.AddComponent<SkinnedMeshRenderer>();
            body.sharedMaterial = original;
            body.shadowCastingMode = ShadowCastingMode.TwoSided;
            var vis = _chicken.AddComponent<ChickenVisuals>();
            var so = new SerializedObject(vis);
            so.FindProperty("_ghostMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Chicken_Ghost.mat");
            so.ApplyModifiedPropertiesWithoutUndo();
            vis.SetModelRoot(model.transform);
            return vis;
        }

        [Test]
        public void WarmupGhost_ForcesTheGhostMaterialOn_AndReleasesBackToTheOriginal_ForAnOpaqueBird()
        {
            var vis = MakeChicken(out var body, out var original);
            vis.ApplyTint(Color.white, 0.3f);
            Assert.IsFalse(vis.IsGhosted);
            Assert.AreSame(original, body.sharedMaterial);

            vis.SetWarmupGhost(true);
            Assert.IsTrue(vis.IsGhosted, "the overview draws the ghost variant");
            Assert.AreNotSame(original, body.sharedMaterial);
            Assert.AreEqual(ShadowCastingMode.TwoSided, body.shadowCastingMode, "an opaque bird keeps its shadow while warming the ghost");

            vis.SetWarmupGhost(true);   // idempotent
            vis.SetWarmupGhost(false);
            Assert.IsFalse(vis.IsGhosted, "released: back to the plain material");
            Assert.AreSame(original, body.sharedMaterial, "the ORIGINAL shared material comes back, not a copy");
        }

        [Test]
        public void WarmupGhost_ReleaseLeavesARealGhostAlone_TheNormalLogicResumesExactly()
        {
            var vis = MakeChicken(out var body, out _);
            vis.ApplyTint(new Color(1f, 1f, 1f, 0.2f), 0f);   // Invisibility / Smoke Roost opacity
            Assert.IsTrue(vis.IsGhosted);
            var ghost = body.sharedMaterial;

            vis.SetWarmupGhost(true);
            vis.SetWarmupGhost(false);
            Assert.IsTrue(vis.IsGhosted, "the bird is genuinely faded: releasing the warm-up must not un-ghost it");
            Assert.AreSame(ghost, body.sharedMaterial);
            Assert.AreEqual(ShadowCastingMode.Off, body.shadowCastingMode, "below 0.5 opacity: no shadow, as the normal ghost logic says");
        }

        [Test]
        public void WarmupGhost_NeverTouchesTheNetworkedOpacity()
        {
            string src = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Assets/_Game/Scripts/Visuals/ChickenVisuals.cs"));
            int start = src.IndexOf("public void SetWarmupGhost", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string body = src.Substring(start, src.IndexOf('}', start) - start);
            StringAssert.DoesNotContain("VisualOpacity", body, "presentation-only override: no [Networked] value is read or written here");
        }

        [Test]
        public void Proxies_DestroyWithNothingCreated_IsSafe()
        {
            var p = new ArenaWarmupProxies();
            Assert.IsFalse(p.IsAlive);
            Assert.DoesNotThrow(() => p.Destroy());
        }

        // ---- Waiting line + scene wiring -------------------------------------------------------------

        [Test]
        public void WaitingLine_NeverNamesThisPeer_OthersStillSeeTheName()
        {
            Assert.IsFalse(WaitingLineRule.Shows(true, true), "own warm-up: no self-blame");
            Assert.IsTrue(WaitingLineRule.Shows(true, false), "another player is waited for: named");
            Assert.IsFalse(WaitingLineRule.Shows(false, false), "nobody waited for");
            Assert.IsFalse(WaitingLineRule.Shows(false, true));
        }

        [Test]
        public void Scenes_GameBindsTheRealDriver_AbilityLabBindsTheNullReadiness()
        {
            string game = Read("Assets/_Game/Scenes/Game.unity");
            StringAssert.DoesNotContain("_useArenaReadiness: 0", game, "Game.unity must keep the default (true): the real ArenaReadinessDriver");
            string lab = Read("Assets/_Game/Scenes/AbilityLab.unity");
            StringAssert.Contains("_useArenaReadiness: 0", lab, "the lab has no arena to warm: NullArenaReadiness");
            string installer = Read("Assets/_Game/Scripts/Installers/GameInstaller.cs");
            StringAssert.Contains("bool _useArenaReadiness = true;", installer, "default true = the driver");
            StringAssert.Contains("NullArenaReadiness.Instance", installer);
        }

        // ---- UXML / USS guard ------------------------------------------------------------------------

        private static string OpeningTag(string uxml, string name)
        {
            var m = Regex.Match(uxml, "<ui:VisualElement[^>]*name=\"" + name + "\"[^>]*>");
            Assert.IsTrue(m.Success, $"#{name} is missing from MatchOverlays.uxml");
            return m.Value;
        }

        [Test]
        public void Uxml_ArenaCover_SitsInsideTheIntroOverlay_BeforeTheDigit_AndIsVisibleByDefault()
        {
            string uxml = Read("Assets/UI/MatchOverlays.uxml");
            int overlay = uxml.IndexOf("name=\"IntroOverlay\"", System.StringComparison.Ordinal);
            int cover = uxml.IndexOf("name=\"ArenaCover\"", System.StringComparison.Ordinal);
            int stack = uxml.IndexOf("name=\"IntroStack\"", System.StringComparison.Ordinal);
            int getReady = uxml.IndexOf("name=\"IntroGetReady\"", System.StringComparison.Ordinal);
            int hostLeft = uxml.IndexOf("name=\"HostLeftNotice\"", System.StringComparison.Ordinal);
            Assert.Greater(overlay, 0);
            Assert.Greater(cover, overlay, "#ArenaCover is inside #IntroOverlay");
            Assert.Less(cover, stack, "#ArenaCover comes BEFORE #IntroStack, so the digit and the GET READY plate draw above it");
            Assert.Less(stack, getReady);
            Assert.Less(cover, hostLeft, "and still inside the overlay (before the next top-level element)");

            foreach (var name in new[] { "IntroOverlay", "ArenaCover", "IntroGetReady" })
                StringAssert.DoesNotContain("display: none", OpeningTag(uxml, name).Replace("display:none", "display: none"),
                    $"#{name} must be visible by default: the very first rendered frame of the Game scene is the opaque cover");

            string tag = OpeningTag(uxml, "ArenaCover");
            StringAssert.Contains("cw-arena-cover", tag);
            StringAssert.Contains("picking-mode=\"Ignore\"", tag, "the cover never eats a tap");
            StringAssert.Contains("ArenaCover.uss", uxml);
        }

        [Test]
        public void Uss_ArenaCover_IsOpaqueInkWithAFastFade()
        {
            string css = Read("Assets/UI/Styles/ArenaCover.uss");
            StringAssert.Contains(".cw-arena-cover", css);
            StringAssert.Contains("rgb(20, 10, 4)", css, "the same ink as the GET READY card, so the menu hand-off is seamless");
            StringAssert.Contains("transition-duration: 0.25s", css);
            StringAssert.Contains(".cw-arena-cover--hidden", css);
            StringAssert.Contains("opacity: 0", css);
        }
    }
}
