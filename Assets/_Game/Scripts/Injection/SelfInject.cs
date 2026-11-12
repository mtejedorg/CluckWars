using System;
using System.Collections.Generic;
using CluckWars.Logging;
using UnityEngine;
using Zenject;

namespace CluckWars.Injection
{
    /// <summary>
    /// The one way a component that Zenject did not build obtains its dependencies (docs/CONVENTIONS.md
    /// "Self-injection"). Fusion spawns network objects, and code adds components at runtime, outside any container;
    /// such a component calls <see cref="Ensure"/> once, at the top of <c>Spawned</c> / <c>Awake</c>:
    /// <code>SelfInject.Ensure(this, _log != null);</code>
    /// </summary>
    /// <remarks>
    /// <b>Which container.</b> The scene container of the component's own scene when that scene has one; otherwise any
    /// live scene container (a network object can live outside the scene that holds the SceneContext); otherwise the
    /// project container. A scene container inherits every project binding, so "scene first" is always right, while
    /// "project only" throws on the first scene-scoped dependency (<c>INetworkService</c>, <c>IArenaReadiness</c>):
    /// the crash of commit 14fbf8f.
    ///
    /// <b>Why scene containers register here.</b> Zenject's own <c>SceneContextRegistry</c> is filled when the scene
    /// kernel initialises (<c>Start</c>), which is after the <c>Awake</c> calls that self-inject. Each scene installer
    /// therefore registers its container as the first line of <c>InstallBindings</c>.
    ///
    /// <b>Failure.</b> Never throws: a missing context or an unresolvable dependency is reported once per component
    /// type through <see cref="ILogService"/> (Error, with the exception) and <see cref="Ensure"/> returns false.
    /// </remarks>
    public static class SelfInject
    {
        private const string Source = "SelfInject";

        private readonly struct SceneEntry
        {
            public readonly Component Owner;
            public readonly DiContainer Container;
            public SceneEntry(Component owner, DiContainer container) { Owner = owner; Container = container; }
        }

        private static readonly List<SceneEntry> s_sceneContainers = new List<SceneEntry>();
        private static readonly HashSet<Type> s_reported = new HashSet<Type>();
        private static Func<DiContainer> s_projectContainer = DefaultProjectContainer;

        // Reading .Instance creates the ProjectContext on first use. Never gate on ProjectContext.HasInstance.
        private static DiContainer DefaultProjectContainer() => ProjectContext.Instance.Container;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_sceneContainers.Clear();
            s_reported.Clear();
            s_projectContainer = DefaultProjectContainer;
        }

        /// <summary>
        /// A scene installer announces its container. <paramref name="owner"/> is the installer itself: its scene
        /// keys the container, and the entry is dropped when the installer is destroyed with its scene.
        /// </summary>
        public static void RegisterSceneContainer(Component owner, DiContainer container)
        {
            if (owner == null || container == null) return;
            Prune();
            for (int i = 0; i < s_sceneContainers.Count; i++)
            {
                if (s_sceneContainers[i].Owner != owner) continue;
                s_sceneContainers[i] = new SceneEntry(owner, container);
                return;
            }
            s_sceneContainers.Add(new SceneEntry(owner, container));
        }

        /// <summary>Removes <paramref name="owner"/>'s container before its scene goes away (tests; scenes unload on their own).</summary>
        public static void UnregisterSceneContainer(Component owner)
        {
            for (int i = s_sceneContainers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(s_sceneContainers[i].Owner, owner)) s_sceneContainers.RemoveAt(i);
        }

        /// <summary>
        /// Tests only: replaces where the project container comes from (null restores the real ProjectContext) and
        /// forgets which types were already reported.
        /// </summary>
        public static void OverrideProjectContainerForTests(Func<DiContainer> source)
        {
            s_projectContainer = source ?? DefaultProjectContainer;
            s_reported.Clear();
        }

        /// <summary>
        /// Injects <paramref name="target"/> unless <paramref name="alreadyInjected"/> (pass the null check of a
        /// dependency every path sets, normally <c>_log != null</c>). Returns true when the target has its
        /// dependencies afterwards.
        /// </summary>
        public static bool Ensure(MonoBehaviour target, bool alreadyInjected)
        {
            if (alreadyInjected) return true;
            if (target == null) return false;

            string scope = "no";
            try
            {
                var container = ContainerFor(target, out scope);
                if (container == null)
                {
                    Report(target, $"there is no {scope} container to inject from", null);
                    return false;
                }
                container.Inject(target);
                return true;
            }
            catch (Exception e)
            {
                Report(target, $"injection from the {scope} container failed", e);
                return false;
            }
        }

        /// <summary>The container <see cref="Ensure"/> injects <paramref name="target"/> from; <paramref name="scope"/> names it for the log.</summary>
        public static DiContainer ContainerFor(Component target, out string scope)
        {
            Prune();
            var scene = target.gameObject.scene;
            for (int i = 0; i < s_sceneContainers.Count; i++)
            {
                if (s_sceneContainers[i].Owner.gameObject.scene != scene) continue;
                scope = "scene";
                return s_sceneContainers[i].Container;
            }
            if (s_sceneContainers.Count > 0)
            {
                scope = "scene";
                return s_sceneContainers[0].Container;
            }
            scope = "project";
            return s_projectContainer();
        }

        private static void Prune()
        {
            for (int i = s_sceneContainers.Count - 1; i >= 0; i--)
                if (s_sceneContainers[i].Owner == null) s_sceneContainers.RemoveAt(i);   // destroyed with its scene
        }

        private static void Report(MonoBehaviour target, string what, Exception exception)
        {
            var type = target.GetType();
            if (!s_reported.Add(type)) return;
            string message = $"{type.Name} on '{target.name}' has no dependencies: {what}. Its [Inject] fields stay null, so " +
                             "whatever it logs, plays or looks up will not work. Check which installer binds each " +
                             "parameter of its Construct().";
            ResolveLog().Error(Source, message, exception);
        }

        private static ILogService ResolveLog()
        {
            try
            {
                var log = s_projectContainer()?.TryResolve<ILogService>();
                if (log != null) return log;
            }
            catch (Exception)
            {
                // The project container is what failed; the failure itself is reported by the caller through the
                // fallback logger below, so nothing is lost here.
            }
            return new UnityLogService();
        }
    }
}
