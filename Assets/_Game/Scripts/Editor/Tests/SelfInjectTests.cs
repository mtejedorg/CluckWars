using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Bootstrap;
using CluckWars.Injection;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Zenject;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// A11 + A06 (architecture audit 2026-11-12): <see cref="SelfInject"/> is the one way a component Zenject did not
    /// build gets its dependencies. Real Zenject containers and real runtime components throughout: the project
    /// container binds one logger, the scene container another, so which container injected is visible in the field.
    /// </summary>
    public sealed class SelfInjectTests
    {
        private sealed class RecordingLog : ILogService
        {
            public readonly string Name;
            public readonly List<(string message, Exception exception)> Errors = new List<(string, Exception)>();
            public RecordingLog(string name) { Name = name; }
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(($"[{source}] {message}", exception));
        }

        private readonly List<Object> _made = new List<Object>();
        private readonly List<Component> _owners = new List<Component>();
        private RecordingLog _projectLog, _sceneLog;
        private DiContainer _project, _scene;
        private Scene _otherScene;

        [SetUp]
        public void SetUp()
        {
            _projectLog = new RecordingLog("project");
            _sceneLog = new RecordingLog("scene");
            _project = new DiContainer();
            _project.Bind<ILogService>().FromInstance(_projectLog).AsSingle();
            _scene = new DiContainer(_project);
            _scene.Bind<ILogService>().FromInstance(_sceneLog).AsSingle();
            SelfInject.OverrideProjectContainerForTests(() => _project);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var owner in _owners) SelfInject.UnregisterSceneContainer(owner);
            _owners.Clear();
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
            if (_otherScene.IsValid()) EditorSceneManager.ClosePreviewScene(_otherScene);
            SelfInject.OverrideProjectContainerForTests(null);
        }

        private T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _made.Add(go);
            return go.AddComponent<T>();
        }

        private Component RegisterScene(DiContainer container, string name = "SceneInstaller")
        {
            var go = new GameObject(name);
            _made.Add(go);
            Component owner = go.transform;
            _owners.Add(owner);
            SelfInject.RegisterSceneContainer(owner, container);
            return owner;
        }

        private static string InjectedLogName(MonoBehaviour target)
        {
            var field = target.GetType().GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, target.GetType().Name + " no longer keeps its logger in _log");
            return (field.GetValue(target) as RecordingLog)?.Name;
        }

        // ---- Resolution order ---------------------------------------------------------------------------------------

        [Test]
        public void ASceneContainerForTheObjectsScene_IsUsed_SoSceneScopedBindingsResolve()
        {
            RegisterScene(_scene);
            var target = Make<SceneLoader>("Target");

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: false));
            Assert.AreEqual("scene", InjectedLogName(target));
        }

        [Test]
        public void WithNoSceneContainer_TheProjectContainerIsUsed()
        {
            var target = Make<SceneLoader>("Target");

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: false));
            Assert.AreEqual("project", InjectedLogName(target));
        }

        [Test]
        public void AnObjectOutsideTheScenesThatHaveAContainer_StillUsesTheLiveSceneContainer()
        {
            // A network object can live outside the scene that holds the SceneContext (the old
            // FindFirstObjectByType<SceneContext>() copies found it from anywhere).
            RegisterScene(_scene);
            _otherScene = EditorSceneManager.NewPreviewScene();
            var target = Make<SceneLoader>("Target");
            SceneManager.MoveGameObjectToScene(target.gameObject, _otherScene);

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: false));
            Assert.AreEqual("scene", InjectedLogName(target));
        }

        [Test]
        public void TheObjectsOwnScene_WinsOverAnotherScenesContainer()
        {
            var otherLog = new RecordingLog("other scene");
            var other = new DiContainer(_project);
            other.Bind<ILogService>().FromInstance(otherLog).AsSingle();

            RegisterScene(_scene, "FirstRegistered");                       // the main scene, registered first
            _otherScene = EditorSceneManager.NewPreviewScene();
            var otherOwner = RegisterScene(other, "OtherSceneInstaller");
            SceneManager.MoveGameObjectToScene(otherOwner.gameObject, _otherScene);
            var target = Make<SceneLoader>("Target");
            SceneManager.MoveGameObjectToScene(target.gameObject, _otherScene);

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: false));
            Assert.AreEqual("other scene", InjectedLogName(target));
        }

        [Test]
        public void ASceneContainerWhoseInstallerWasDestroyed_IsForgotten()
        {
            var owner = RegisterScene(_scene);
            Object.DestroyImmediate(owner.gameObject);                      // the scene unloaded
            var target = Make<SceneLoader>("Target");

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: false));
            Assert.AreEqual("project", InjectedLogName(target));
        }

        [Test]
        public void AnAlreadyInjectedObject_IsLeftAlone()
        {
            RegisterScene(_scene);
            var target = Make<SceneLoader>("Target");
            target.Construct(_projectLog);

            Assert.IsTrue(SelfInject.Ensure(target, alreadyInjected: true));
            Assert.AreEqual("project", InjectedLogName(target), "Ensure must not inject a second time");
        }

        // ---- Failure: reported once, never thrown -------------------------------------------------------------------

        [Test]
        public void AnUnresolvableDependency_IsReportedOncePerType_AndDoesNotThrow()
        {
            // FencePerformanceSwap needs FencePerformanceMeshesSO, which neither test container binds.
            var a = Make<FencePerformanceSwap>("SwapA");
            var b = Make<FencePerformanceSwap>("SwapB");

            bool first = true, second = true;
            Assert.DoesNotThrow(() => first = SelfInject.Ensure(a, alreadyInjected: false));
            Assert.DoesNotThrow(() => second = SelfInject.Ensure(b, alreadyInjected: false));

            Assert.IsFalse(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, _projectLog.Errors.Count, "one line per component type, not one per object");
            StringAssert.Contains("[SelfInject] FencePerformanceSwap on 'SwapA'", _projectLog.Errors[0].message);
            StringAssert.Contains("project container", _projectLog.Errors[0].message);
            Assert.IsInstanceOf<ZenjectException>(_projectLog.Errors[0].exception, "the report carries Zenject's own explanation");
        }

        [Test]
        public void WithNoContainerAtAll_TheFailureStillReachesTheLog()
        {
            SelfInject.OverrideProjectContainerForTests(() => null);
            var target = Make<SceneLoader>("Target");
            LogAssert.Expect(LogType.Error, new Regex(@"\[SelfInject\] SceneLoader on 'Target' has no dependencies: there is no project container"));

            bool result = true;
            Assert.DoesNotThrow(() => result = SelfInject.Ensure(target, alreadyInjected: false));

            Assert.IsFalse(result);
            Assert.IsNull(InjectedLogName(target));
        }

        [Test]
        public void AProjectContextThatCannotBeCreated_IsReported_NotThrown()
        {
            SelfInject.OverrideProjectContainerForTests(() => throw new InvalidOperationException("no ProjectContext prefab"));
            var target = Make<SceneLoader>("Target");
            // UnityLogService wraps the cause; Unity's console headline is the cause itself (the SelfInject line is in the trace).
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: no ProjectContext prefab"));

            bool result = true;
            Assert.DoesNotThrow(() => result = SelfInject.Ensure(target, alreadyInjected: false));

            Assert.IsFalse(result);
        }

        // ---- One helper (source guard) ------------------------------------------------------------------------------

        [Test]
        public void NoRuntimeFile_InjectsItselfOrLocatesAService_OutsideTheHelper()
        {
            // The copies this replaced: `<context>.Container.Inject(this)` and `ProjectContext.Instance.Container.TryResolve`.
            // Renderer features are assets with no injection point; they borrow the project logger for one error line.
            var direct = new Regex(@"\.Container\s*\.\s*(Inject|InjectGameObject|Resolve|TryResolve)\b|FindFirstObjectByType\s*<\s*SceneContext\s*>");
            var allowed = new[] { "/Injection/SelfInject.cs", "/Visuals/CWHazeFeature.cs", "/Visuals/MarkScreenFeature.cs" };
            var hits = new List<string>();
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
            {
                if (path.Contains("/Installers/")) continue;                 // installers are where containers are used
                if (Array.Exists(allowed, path.EndsWith)) continue;
                if (direct.IsMatch(RuntimeSourceGuardTests.Code(path))) hits.Add(path);
            }
            Assert.IsEmpty(hits,
                "Use SelfInject.Ensure(this, _log != null) and an [Inject] Construct (docs/CONVENTIONS.md \"Self-injection\"):\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void TheSceneInstaller_RegistersItsContainer_BeforeItBindsAnything()
        {
            string src = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Installers/GameInstaller.cs");
            int register = src.IndexOf("SelfInject.RegisterSceneContainer(this, Container);", StringComparison.Ordinal);
            int firstBind = src.IndexOf("Container.Bind", StringComparison.Ordinal);
            Assert.GreaterOrEqual(register, 0, "GameInstaller must announce its container to SelfInject");
            Assert.Less(register, firstBind, "registered first: NonLazy bindings can create objects that self-inject");
        }
    }
}
