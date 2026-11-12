using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Tests
{
    /// <summary>
    /// A4b (architecture audit batch 4): <c>ChickenController</c> references no Visuals type. The presentation work it
    /// triggers at spawn goes through <see cref="IChickenModelPresenter"/>, and what presentation cached for the chicken
    /// is read through the <see cref="ChickenPresentationAccess"/> extension methods. Pinned here: a missing presenter
    /// is an error and not a crash, the tint result still decides <c>ClassBodyTint</c>, the accessors are cached, and
    /// the calls sit where the direct calls sat.
    /// </summary>
    public sealed class ChickenPresenterTests
    {
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";

        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new List<string>();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, System.Exception exception = null) => Errors.Add($"[{source}] {message}");
        }

        /// <summary>Records every call and answers <see cref="TintApplied"/> to the class-look question.</summary>
        private sealed class FakePresenter : IChickenModelPresenter
        {
            public sealed class Holder : IChickenPresentation { }
            public readonly List<string> Calls = new List<string>();
            public bool TintApplied;

            public IChickenPresentation Bind(ChickenController chicken) { Calls.Add("Bind"); return new Holder(); }
            public void OnScaleApplied(ChickenController chicken) => Calls.Add("OnScaleApplied");
            public void ApplyHeadLift(ChickenController chicken, float lift, float classScale) =>
                Calls.Add(System.FormattableString.Invariant($"ApplyHeadLift {lift:0.00} {classScale:0.00}"));
            public void FitFootprint(ChickenController chicken, Transform model) => Calls.Add("FitFootprint");
            public bool ApplyClassLook(ChickenController chicken, Transform modelRoot, Color tint, float strength, bool skeletal)
            {
                Calls.Add("ApplyClassLook");
                return TintApplied;
            }
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() => ForgetMissingPresenterReport();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ForgetMissingPresenterReport();
        }

        private static void ForgetMissingPresenterReport()
        {
            var f = typeof(ChickenController).GetField("s_presenterMissingReported", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "ChickenController.s_presenterMissingReported is gone: the once-only report moved");
            f.SetValue(null, false);
        }

        private static object Call(ChickenController chicken, string method, params object[] args)
        {
            var m = typeof(ChickenController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"ChickenController.{method} is gone: the presenter seam moved");
            return m.Invoke(chicken, args);
        }

        /// <summary>The shipped chicken prefab, instantiated in the editor (no Awake, no Spawned: only what a test calls).</summary>
        private ChickenController PrefabChicken()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _spawned.Add(go);
            return go.GetComponent<ChickenController>();
        }

        /// <summary>A chicken with nothing but what <c>ChickenController</c> requires: no presentation component at all.</summary>
        private ChickenController BareChicken()
        {
            var go = new GameObject("BareChicken");
            _spawned.Add(go);
            return go.AddComponent<ChickenController>();
        }

        // ---- No presenter: an error, once, and the chicken carries on ----------------------------------------------

        [Test]
        public void WithNoPresenter_TheFaultIsReportedOnce_AndNothingThrows()
        {
            var log = new RecordingLog();
            var first = PrefabChicken();
            var second = PrefabChicken();
            first.Construct(null, null, log);    // the three-argument call: no presenter was injected
            second.Construct(null, null, log);

            Assert.DoesNotThrow(() => Call(first, "BindPresentation"));
            Assert.DoesNotThrow(() => Call(second, "BindPresentation"));
            Assert.DoesNotThrow(() => Call(first, "BindPresentation"), "a pooled respawn binds again");

            Assert.AreEqual(1, log.Errors.Count, "one line for the whole game, not one per chicken or per spawn:\n" + string.Join("\n", log.Errors));
            StringAssert.Contains("IChickenModelPresenter", log.Errors[0], "the line names the missing binding");
            StringAssert.Contains("ProjectInstaller", log.Errors[0], "and where it is bound");
            StringAssert.Contains("[Chicken]", log.Errors[0]);

            // Every seam is a no-op, none throws.
            Assert.DoesNotThrow(() => Call(first, "ApplyOverlayHeadLift", 0.4f, 1.25f));
            Assert.DoesNotThrow(() => Call(first, "ApplyClassLook", first.transform, Color.red, 0.5f));
            Assert.AreEqual(Color.white, first.ClassBodyTint, "no presenter, no tint, so the body colour stays the default");

            // Presentation readers get what a bird with none of the components always got.
            Assert.IsNull(first.Presentation);
            Assert.IsNull(first.Nameplate());
            Assert.IsNull(first.Indicator());
            Assert.IsNull(first.StatusBadges());
            first.transform.position = new Vector3(2f, 1f, -3f);
            Assert.AreEqual(FloatingCombatText.PopSpawnPosition(first.transform.position, 0f), first.PopAnchor(), "clearance 0: the floor alone");
            Assert.AreEqual(first.PopAnchor(), first.PopAnchorEntering(ChickenStatusBadges.Badge.Slow));
        }

        [Test]
        public void TheMissingPresenterReport_WaitsForALogger_SoItIsNeverSpentUnseen()
        {
            var silent = PrefabChicken();                 // never injected: no logger yet
            Assert.DoesNotThrow(() => Call(silent, "BindPresentation"));

            var log = new RecordingLog();
            var loud = PrefabChicken();
            loud.Construct(null, null, log);
            Call(loud, "BindPresentation");
            Assert.AreEqual(1, log.Errors.Count, "the first chicken that can speak reports it");
        }

        // ---- The tint result decides ClassBodyTint ------------------------------------------------------------------

        [Test]
        public void ClassBodyTint_IsRecorded_OnlyWhenThePresenterSaysTheTintWasApplied()
        {
            var tint = new Color(0.2f, 0.6f, 0.9f, 1f);

            var noTint = PrefabChicken();
            var refused = new FakePresenter { TintApplied = false };
            noTint.Construct(null, null, null, refused);   // no logger: the Debug line names the networked Class, unreadable before a real spawn
            Call(noTint, "ApplyClassLook", noTint.transform, tint, 0.5f);
            CollectionAssert.AreEqual(new[] { "ApplyClassLook" }, refused.Calls);
            Assert.AreEqual(Color.white, noTint.ClassBodyTint, "no tint component: the body colour is not recorded");

            var tinted = PrefabChicken();
            var applied = new FakePresenter { TintApplied = true };
            tinted.Construct(null, null, null, applied);
            Call(tinted, "ApplyClassLook", tinted.transform, tint, 0.5f);
            Assert.AreEqual(Color.Lerp(Color.white, tint, 0.5f), tinted.ClassBodyTint);
        }

        [Test]
        public void TheRealPresenter_ReportsTheTint_ExactlyWhenTheChickenHasATintComponent()
        {
            var presenter = new ChickenModelPresenter();

            var bare = BareChicken();
            var model = new GameObject("Model");
            model.transform.SetParent(bare.transform, false);
            Assert.IsNull(bare.GetComponent<ChickenVisuals>());
            Assert.IsFalse(presenter.ApplyClassLook(bare, model.transform, Color.red, 0.5f, skeletal: false),
                "no ChickenVisuals: nothing was tinted, and nothing may throw");
            Assert.DoesNotThrow(() => presenter.ApplyHeadLift(bare, 0.3f, 1.2f), "every overlay is optional");

            var full = PrefabChicken();
            Assert.IsNotNull(full.GetComponent<ChickenVisuals>(), "the shipped prefab carries the tint component");
            var fullModel = new GameObject("Model");
            fullModel.transform.SetParent(full.transform, false);
            Assert.IsTrue(presenter.ApplyClassLook(full, fullModel.transform, Color.red, 0.5f, skeletal: false));
        }

        [Test]
        public void HeadLift_ReachesThePresenter_OnlyWhenThereIsALift()
        {
            var chicken = PrefabChicken();
            var presenter = new FakePresenter();
            chicken.Construct(null, null, new RecordingLog(), presenter);

            Call(chicken, "ApplyOverlayHeadLift", 0f, 1f);
            CollectionAssert.IsEmpty(presenter.Calls, "a model the collider's height needs no lift (the early return stays in gameplay)");
            Call(chicken, "ApplyOverlayHeadLift", 0.4f, 1.25f);
            CollectionAssert.AreEqual(new[] { "ApplyHeadLift 0.40 1.25" }, presenter.Calls);
        }

        // ---- The accessors are cached -------------------------------------------------------------------------------

        [Test]
        public void TheAccessors_ReturnTheSameCachedInstances_EveryCall()
        {
            var chicken = PrefabChicken();
            chicken.Construct(null, null, new RecordingLog(), new ChickenModelPresenter());
            Assert.IsNull(chicken.Presentation, "nothing is cached before the spawn binds it");
            Assert.IsNull(chicken.Nameplate());

            Call(chicken, "BindPresentation");
            var holder = chicken.Presentation;
            Assert.IsInstanceOf<ChickenPresentation>(holder);

            var plate = chicken.Nameplate();
            var indicator = chicken.Indicator();
            var badges = chicken.StatusBadges();
            Assert.AreSame(chicken.GetComponent<ChickenNameplate>(), plate);
            Assert.AreSame(chicken.GetComponent<RivalIndicator>(), indicator);
            Assert.AreSame(chicken.GetComponent<ChickenStatusBadges>(), badges);
            Assert.IsNotNull(plate, "the shipped prefab carries a nameplate");
            Assert.IsNotNull(indicator, "the shipped prefab carries a rival indicator");
            Assert.IsNotNull(badges, "the shipped prefab carries a badge stack");

            for (int i = 0; i < 3; i++)
            {
                Assert.AreSame(holder, chicken.Presentation, "the holder is created once per spawn, not per read");
                Assert.AreSame(plate, chicken.Nameplate());
                Assert.AreSame(indicator, chicken.Indicator());
                Assert.AreSame(badges, chicken.StatusBadges());
            }

            // The cache really is a cache: a component removed after the spawn is not looked up again.
            string access = File.ReadAllText("Assets/_Game/Scripts/Visuals/ChickenPresentationAccess.cs");
            StringAssert.DoesNotContain("GetComponent", Regex.Replace(access, @"///[^\n]*", string.Empty), "the accessors read the holder, never the hierarchy");
        }

        [Test]
        public void PopAnchor_ClearsTheBadgeStack_AsItDidOnTheController()
        {
            var chicken = PrefabChicken();
            chicken.Construct(null, null, new RecordingLog(), new ChickenModelPresenter());
            Call(chicken, "BindPresentation");
            chicken.transform.position = new Vector3(1f, 0.9f, 4f);

            var badges = chicken.StatusBadges();
            Assert.AreEqual(FloatingCombatText.PopSpawnPosition(chicken.transform.position, badges.PopClearHeight(ChickenStatusBadges.Badge.None)),
                chicken.PopAnchor());
            Assert.AreEqual(FloatingCombatText.PopSpawnPosition(chicken.transform.position, badges.PopClearHeight(ChickenStatusBadges.Badge.Stun)),
                chicken.PopAnchorEntering(ChickenStatusBadges.Badge.Stun));
        }

        // ---- The seam sits where the direct calls sat (source order) ------------------------------------------------

        [Test]
        public void TheController_CallsThePresenter_AtTheSamePointsAsBefore()
        {
            string src = File.ReadAllText(ControllerPath);
            string spawned = AssassinExecuteCreditTests.MethodBody(ControllerPath, "public override void Spawned()");
            AssertInOrder(spawned,
                "_assassin = GetComponent<AssassinExecute>();",
                "BindPresentation();",
                "_activeStats = ResolveStatsForClass(Class);",
                "transform.localScale = Vector3.one * _activeStats.Scale;",
                "_presenter?.OnScaleApplied(this);",
                "SeatOnGroundAtSpawn();",
                "var modelRoot = AttachClassModel(entry);",
                "ApplyClassLook(modelRoot, entry.TintColor, entry.TintStrength);",
                "PresentationReady = true;");

            string attach = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private Transform AttachClassModel(in ChickenClassRegistrySO.Entry entry)");
            AssertInOrder(attach,
                "model.transform.localScale = modelScale;",
                "ApplyOverlayHeadLift(_characterController.height * (modelScale.y - 1f), modelScale.y);",
                "_presenter?.FitFootprint(this, model.transform);",
                "LocalEvents.RaiseClassModelAttached()",
                "_skeletalAnimationActive = BindSkeletalAnimation(entry);");

            // The presenter keeps the order the controller had: tint target, tint, then the animation helper.
            string look = AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Visuals/ChickenModelPresenter.cs",
                "public bool ApplyClassLook(ChickenController chicken, Transform modelRoot, Color tint, float strength, bool skeletal)");
            AssertInOrder(look,
                "visuals.SetModelRoot(modelRoot);",
                "visuals.ApplyTint(tint, strength);",
                "animator.SetModelRoot(modelRoot);",
                "animator.SetSkeletalActive(skeletal);",
                "return visuals != null;");

            StringAssert.DoesNotContain("CluckWars.Visuals", src, "the chicken's gameplay half names no presentation namespace");
            StringAssert.Contains("[InjectOptional] IChickenModelPresenter presenter = null", src, "optional: a missing binding must not stop the spawn");
        }

        private static void AssertInOrder(string body, params string[] steps)
        {
            int at = 0;
            foreach (string step in steps)
            {
                int next = body.IndexOf(step, at, System.StringComparison.Ordinal);
                Assert.GreaterOrEqual(next, 0, $"'{step}' is missing, or no longer comes after the step before it");
                at = next + step.Length;
            }
        }

        [Test]
        public void ProjectInstaller_BindsThePresenter()
        {
            string installer = File.ReadAllText("Assets/_Game/Scripts/Installers/ProjectInstaller.cs");
            StringAssert.Contains("Container.Bind<IChickenModelPresenter>().FromInstance(new ChickenModelPresenter()).AsSingle();", installer);
            Assert.IsTrue(typeof(IChickenModelPresenter).IsAssignableFrom(typeof(ChickenModelPresenter)));
            Assert.AreEqual("CluckWars.Gameplay", typeof(IChickenModelPresenter).Namespace);
            Assert.AreEqual("CluckWars.Gameplay", typeof(IChickenPresentation).Namespace);
            Assert.AreEqual("CluckWars.Visuals", typeof(ChickenModelPresenter).Namespace);
            Assert.IsNotNull(typeof(ProjectInstaller));
        }
    }
}
