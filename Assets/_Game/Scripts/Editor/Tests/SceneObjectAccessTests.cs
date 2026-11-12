using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// A13 (architecture audit 2026-11-12): one way to find scene objects (docs/CONVENTIONS.md "Finding scene
    /// objects"). Services are injected; scene objects are reached through a read-only registry or a self-clearing
    /// <c>Instance</c>; <c>Find*</c> is for one-time bootstrap and dev tools only.
    /// </summary>
    public sealed class SceneObjectAccessTests
    {
        // ---- Registries: read-only from outside ---------------------------------------------------------------------

        private static readonly (Type type, string property)[] Registries =
        {
            (typeof(ChickenController), nameof(ChickenController.ActiveControllers)),
            (typeof(ChickenCargo), nameof(ChickenCargo.ActiveCargos)),
            (typeof(ChickenCombat), nameof(ChickenCombat.ActiveCombats)),
            (typeof(ChickenMatchStats), nameof(ChickenMatchStats.ActiveStats)),
            (typeof(FoodPile), nameof(FoodPile.ActivePiles)),
            (typeof(PlayerBase), nameof(PlayerBase.ActiveBases)),
            (typeof(AbilityZone), nameof(AbilityZone.ActiveZones)),
        };

        [Test]
        public void EveryRegistry_IsAReadOnlyView_ThatCannotBeCastBackToAList()
        {
            foreach (var (type, name) in Registries)
            {
                var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
                Assert.IsNotNull(property, $"{type.Name}.{name} is no longer a public static property");
                Assert.IsNull(property.SetMethod, $"{type.Name}.{name} can be reassigned");
                Assert.AreEqual(typeof(IReadOnlyList<>).MakeGenericType(type), property.PropertyType, $"{type.Name}.{name}");

                object view = property.GetValue(null);
                Assert.IsFalse(view.GetType().IsGenericType && view.GetType().GetGenericTypeDefinition() == typeof(List<>),
                    $"{type.Name}.{name} hands out the list itself: a cast would make it writable");
                var asList = view as IList;
                Assert.IsNotNull(asList);
                Assert.IsTrue(asList.IsReadOnly, $"{type.Name}.{name}");
                Assert.Throws<NotSupportedException>(() => asList.Add(null), $"{type.Name}.{name} accepted an Add through a cast");
                Assert.Throws<NotSupportedException>(() => asList.Clear(), $"{type.Name}.{name} accepted a Clear through a cast");
            }
        }

        [Test]
        public void OnlyTheOwningType_CanRegister()
        {
            // Compile-time, restated: the backing list and Register / Unregister are private members of the owning
            // type, so `ChickenController.ActiveControllers.Add(x)` from any other file does not compile (it did, and
            // three test files used it; they now go through RegistryTestAccess, which uses reflection on purpose).
            foreach (var (type, name) in Registries)
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(List<>)) continue;
                    if (field.FieldType.GetGenericArguments()[0] != type) continue;
                    Assert.IsTrue(field.IsPrivate, $"{type.Name}.{field.Name}: the registry's list must be private");
                }
                foreach (string method in new[] { "Register", "Unregister" })
                {
                    var m = type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    Assert.IsNotNull(m, $"{type.Name}.{method}");
                    Assert.IsTrue(m.IsPrivate, $"{type.Name}.{method} must be private: only the object registers itself");
                }
                Assert.IsNotNull(type.GetMethod("ResetRegistry", BindingFlags.Static | BindingFlags.NonPublic),
                    $"{type.Name} must clear its registry on subsystem registration (domain reload off)");
            }
        }

        [Test]
        public void NoRuntimeFile_EnumeratesARegistryWithForeachOrLinq()
        {
            // An IReadOnlyList enumerated through the interface boxes its enumerator: per-frame garbage. Index loops only.
            string names = string.Join("|", Registries.Select(r => r.property));
            var bad = new Regex($@"foreach\s*\([^)]*\bin\s+[\w.]*\b({names})\b|\b({names})\s*\.\s*(Where|Select|Any|All|First\w*|Last\w*|Count\s*\(|ToList|ToArray|OrderBy\w*|Contains)\b");
            var hits = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => !p.Contains("/Diagnostics/") && !p.Contains("/AbilityLab/"))
                .Where(p => bad.IsMatch(RuntimeSourceGuardTests.Code(p))).ToList();
            Assert.IsEmpty(hits, "Iterate a registry by index:\n" + string.Join("\n", hits));
        }

        // ---- Find*: bootstrap and dev tools only --------------------------------------------------------------------

        private static readonly Regex FindCall =
            new Regex(@"\b(FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType|FindObjectsByType)\s*<");

        /// <summary>Every runtime file outside the dev tools that may still search the scene, how often, and why.</summary>
        private static readonly Dictionary<string, (int calls, string why)> AllowedFinds = new Dictionary<string, (int, string)>
        {
            ["Gameplay/MapGenerator.cs"] = (2, "ReArmForMasterPromotion: once per master promotion, must also see objects that are instantiated but not spawned yet"),
            ["Gameplay/MatchBootstrapper.cs"] = (1, "DropDeadManagersAndFindLive: start-up / promotion sweep over every GameManager, dead ones included"),
            ["UI/MatchHud.cs"] = (1, "EnsureEventSystem: once in Awake"),
            ["UI/MatchOverlaysController.cs"] = (1, "EnsureEventSystem: once in Awake"),
            ["UI/MenuUiController.cs"] = (2, "Awake: the SceneLoader fallback and EnsureEventSystem"),
            ["UI/MenuChickenStage.cs"] = (1, "KNOWN EXCEPTION, menu only: every scene Light, once per frame while a stage camera renders; a cached list could miss a light added at runtime (left by A13, not proven safe to change)"),
            ["Visuals/FenceFadeDriver.cs"] = (1, "EnsureFence: discovers the baked map's fence renderers, retried once a second until the Map scene is in, then never again"),
            ["Visuals/FencePerformanceSwap.cs"] = (1, "BuildTwins: same one-time discovery of the fence meshes"),
            ["Visuals/SurroundHazeSwap.cs"] = (1, "Sync: same one-time discovery of the baked map's SurroundHazeSet, retried once a second until the Map scene is in"),
        };

        private static bool IsDevTool(string path) => path.Contains("/Diagnostics/") || path.Contains("/AbilityLab/");

        [Test]
        public void FindCalls_ExistOnlyInTheListedBootstrapSites_AndTheDevTools()
        {
            var problems = new List<string>();
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
            {
                if (IsDevTool(path)) continue;
                int calls = FindCall.Matches(RuntimeSourceGuardTests.Code(path)).Count;
                string key = path.Substring(path.IndexOf("/Scripts/", StringComparison.Ordinal) + "/Scripts/".Length);
                int allowed = AllowedFinds.TryGetValue(key, out var entry) ? entry.calls : 0;
                if (calls > allowed) problems.Add($"{key}: {calls} Find* call(s), {allowed} allowed");
            }
            Assert.IsEmpty(problems,
                "Reach a scene object through its registry or Instance, a service through injection (docs/CONVENTIONS.md " +
                "\"Finding scene objects\"). A genuine one-time bootstrap goes into AllowedFinds with its reason:\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheAllowList_IsNotStale()
        {
            foreach (var kv in AllowedFinds)
            {
                string path = "Assets/_Game/Scripts/" + kv.Key;
                int calls = FindCall.Matches(RuntimeSourceGuardTests.Code(path)).Count;
                Assert.AreEqual(kv.Value.calls, calls, $"{kv.Key}: the allow-list says {kv.Value.calls} ({kv.Value.why})");
            }
        }

        private static readonly string[] PerFrameMethods = { "Update", "LateUpdate", "FixedUpdate", "FixedUpdateNetwork", "Render", "OnGUI" };

        /// <summary>The body of every method called <paramref name="name"/> with no parameters in <paramref name="code"/>.</summary>
        private static IEnumerable<string> Bodies(string code, string name)
        {
            foreach (Match m in Regex.Matches(code, $@"\bvoid\s+{name}\s*\(\s*\)\s*\{{"))
            {
                int depth = 1, i = m.Index + m.Length;
                while (i < code.Length && depth > 0)
                {
                    if (code[i] == '{') depth++;
                    else if (code[i] == '}') depth--;
                    i++;
                }
                yield return code.Substring(m.Index, i - m.Index);
            }
        }

        [Test]
        public void NoPerFrameMethod_SearchesTheScene()
        {
            // As far as a source test can see: the body of the engine's per-frame callbacks themselves (dev tools
            // excepted). A Find* in a helper they call is caught by the allow-list above, which lists every site.
            var hits = new List<string>();
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles())
            {
                if (IsDevTool(path)) continue;
                string code = RuntimeSourceGuardTests.Code(path);
                foreach (string name in PerFrameMethods)
                    foreach (string body in Bodies(code, name))
                        if (FindCall.IsMatch(body)) hits.Add($"{path}: {name}()");
            }
            Assert.IsEmpty(hits, "Find* inside a per-frame method:\n" + string.Join("\n", hits));
        }

        [Test]
        public void ThePerFrameScan_FindsMethodBodies()
        {
            string code = "class A { void Update() { if (x) { FindFirstObjectByType<Foo>(); } } void Other() { } }";
            var bodies = Bodies(code, "Update").ToList();
            Assert.AreEqual(1, bodies.Count);
            Assert.IsTrue(FindCall.IsMatch(bodies[0]));
            Assert.IsFalse(Bodies(code, "Other").Any(b => FindCall.IsMatch(b)));
            Assert.Greater(RuntimeSourceGuardTests.RuntimeFiles().Sum(p => Bodies(RuntimeSourceGuardTests.Code(p), "Update").Count()), 10,
                "the scan no longer recognises Update() bodies");
        }

        // ---- One access path per object ----------------------------------------------------------------------------

        [Test]
        public void RegisteredAndSingletonObjects_AreNeverSearchedFor()
        {
            // GameManager.Instance, MapGenerator.Instance, MatchCamera.Instance, the HUD singletons and the registries.
            var search = new Regex(@"Find\w+\s*<\s*(GameManager|MapGenerator|MatchCamera|TouchControlsController|MatchHudController|" +
                                   @"ChickenController|ChickenCargo|ChickenCombat|ChickenMatchStats|AbilityZone|PlayerBase|FoodPile)\s*>");
            var allowed = new[] { "/Gameplay/MatchBootstrapper.cs", "/Gameplay/MapGenerator.cs" };   // the two bootstrap sweeps above
            var hits = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => !allowed.Any(p.EndsWith))
                .Where(p => search.IsMatch(RuntimeSourceGuardTests.Code(p))).ToList();
            Assert.IsEmpty(hits, "These objects have one access path (their Instance or registry):\n" + string.Join("\n", hits));
        }

        // ---- Singletons clear themselves ---------------------------------------------------------------------------

        [Test]
        public void EverySceneSingleton_ClearsItself_WhenItGoesAway()
        {
            foreach (var (file, clear) in new[]
                     {
                         ("Gameplay/GameManager.cs", "if (Instance == this) Instance = null;"),
                         ("Gameplay/MapGenerator.cs", "if (Instance == this) Instance = null;"),
                         ("Visuals/MatchCamera.cs", "if (Instance == this) Instance = null;"),

                         ("Input/TouchControlsController.cs", "if (Instance == this) Instance = null;"),
                         ("UI/MatchHudController.cs", "if (Instance == this) Instance = null;"),
                         ("Visuals/AbilityTelegraph.cs", "if (Active == this) Active = null;"),
                     })
            {
                StringAssert.Contains(clear, RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/" + file), file);
            }
        }

        [Test]
        public void NoRuntimeFile_UsesNullConditional_OnAUnityObjectSingleton()
        {
            // `Instance?.X()` compares the reference, not Unity's "destroyed" state: it calls into a dead object.
            var bad = new Regex(@"\b(GameManager|MapGenerator|MatchCamera|TouchControlsController|MatchHudController)\s*\.\s*Instance\s*\?\.|\bAbilityTelegraph\s*\.\s*Active\s*\?\.");
            var hits = RuntimeSourceGuardTests.RuntimeFiles().Where(p => bad.IsMatch(RuntimeSourceGuardTests.Code(p))).ToList();
            Assert.IsEmpty(hits, "Use an explicit `!= null` (Unity's check):\n" + string.Join("\n", hits));
        }
    }
}
