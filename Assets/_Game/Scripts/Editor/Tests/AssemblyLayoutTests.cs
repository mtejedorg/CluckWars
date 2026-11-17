using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace CluckWars.Tests
{
    /// <summary>
    /// Architecture audit batch 4, phase B (2026-11-13): the runtime code is five assemblies,
    /// Core &lt; Gameplay &lt; Presentation &lt; Diagnostics &lt; App (docs/CONVENTIONS.md "Assemblies"). The compiler
    /// enforces the layering only while every folder sits in the right assembly and every asmdef keeps its reference
    /// list, so both are pinned here. A script left outside all five would silently compile into Assembly-CSharp,
    /// where it can see every layer; a NetworkBehaviour in an assembly missing from <c>AssembliesToWeave</c> is dead
    /// at runtime (<see cref="WeavingGuardTests"/>).
    /// </summary>
    public sealed class AssemblyLayoutTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";
        private const string Core = "CluckWars.Core";
        private const string Gameplay = "CluckWars.Gameplay";
        private const string Presentation = "CluckWars.Presentation";
        private const string Diagnostics = "CluckWars.Diagnostics";
        private const string App = "CluckWars.App";
        private const string EditorAssembly = "Assembly-CSharp-Editor";

        private static readonly string[] Layers = { Core, Gameplay, Presentation, Diagnostics, App };

        /// <summary>Folder (relative to Scripts) -> assembly. The longest matching folder wins (Diagnostics/Shared).</summary>
        private static readonly Dictionary<string, string> FolderAssembly = new Dictionary<string, string>
        {
            ["Logging"] = Core, ["Localization"] = Core, ["Injection"] = Core, ["Services"] = Core, ["Bootstrap"] = Core,
            ["Diagnostics/Shared"] = Core, ["Geometry"] = Core,
            ["Gameplay"] = Gameplay, ["Abilities"] = Gameplay, ["Networking"] = Gameplay, ["Settings"] = Gameplay,
            ["Audio"] = Gameplay, ["Balance"] = Gameplay,
            ["Visuals"] = Presentation, ["UI"] = Presentation, ["Input"] = Presentation,
            ["Diagnostics"] = Diagnostics, ["AbilityLab"] = Diagnostics,
            ["App"] = App, ["Installers"] = App,
        };

        /// <summary>The folder that holds each assembly's .asmdef; every other folder of the assembly holds an .asmref.</summary>
        private static readonly Dictionary<string, string> AsmdefFolder = new Dictionary<string, string>
        {
            [Core] = "Logging", [Gameplay] = "Gameplay", [Presentation] = "Visuals", [Diagnostics] = "Diagnostics", [App] = "App",
        };

        /// <summary>The exact reference list of each asmdef, by assembly name.</summary>
        private static readonly Dictionary<string, string[]> References = new Dictionary<string, string[]>
        {
            [Core] = new[] { "Zenject" },
            [Gameplay] = new[] { Core, "Fusion.Unity", "Zenject", "Unity.AI.Navigation" },
            [Presentation] = new[]
            {
                Core, Gameplay, "Fusion.Unity", "Zenject", "Unity.InputSystem", "Unity.RenderPipelines.Core.Runtime",
                "Unity.RenderPipelines.Universal.Runtime", "UnityEngine.UI",
            },
            [Diagnostics] = new[] { Core, Gameplay, Presentation, "Fusion.Unity", "Zenject", "Unity.InputSystem" },
            [App] = new[]
            {
                Core, Gameplay, Presentation, Diagnostics, "Fusion.Unity", "Zenject", "Unity.Services.Core",
                "Unity.Services.Authentication", "Unity.Services.Multiplayer", "Unity.Services.RemoteConfig",
            },
        };

        [Serializable]
        private sealed class AsmdefJson
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public string[] excludePlatforms;
            public bool allowUnsafeCode;
            public bool overrideReferences;
            public string[] precompiledReferences;
            public bool autoReferenced;
            public string[] defineConstraints;
            public bool noEngineReferences;
        }

        [Serializable]
        private sealed class AsmrefJson
        {
            public string reference;
        }

        private static string AsmdefPath(string assembly) => $"{ScriptsRoot}/{AsmdefFolder[assembly]}/{assembly}.asmdef";

        private static AsmdefJson Load(string assembly)
        {
            string path = AsmdefPath(assembly);
            Assert.IsTrue(File.Exists(path), $"{path} is missing");
            return JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path));
        }

        /// <summary>The assembly name behind an asmdef / asmref reference, written as "GUID:..." or as a plain name.</summary>
        private static string ReferenceName(string reference)
        {
            if (!reference.StartsWith("GUID:", StringComparison.Ordinal)) return reference;
            string path = AssetDatabase.GUIDToAssetPath(reference.Substring("GUID:".Length));
            Assert.IsFalse(string.IsNullOrEmpty(path), $"{reference} resolves to no asset: the referenced assembly definition is gone");
            return JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path)).name;
        }

        private static string ExpectedAssembly(string path)
        {
            if (path.Contains("/Editor/")) return EditorAssembly;
            string relative = path.Substring(ScriptsRoot.Length + 1);
            string best = null;
            foreach (string folder in FolderAssembly.Keys)
                if (relative.StartsWith(folder + "/", StringComparison.Ordinal) && (best == null || folder.Length > best.Length))
                    best = folder;
            return best == null ? null : FolderAssembly[best];
        }

        private static string CompiledAssembly(string path)
        {
            string name = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
            if (string.IsNullOrEmpty(name)) return "(none)";
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        private static IEnumerable<string> AllScripts() =>
            Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'));

        [Test]
        public void EveryScript_CompilesIntoTheAssemblyOfItsFolder()
        {
            var problems = new List<string>();
            var perAssembly = new Dictionary<string, int>();
            foreach (string path in AllScripts())
            {
                string expected = ExpectedAssembly(path);
                string actual = CompiledAssembly(path);
                if (expected == null)
                    problems.Add($"{path}: its folder belongs to no assembly (it compiles into {actual}). Give the folder an " +
                                 ".asmref and add it to FolderAssembly (docs/CONVENTIONS.md \"Assemblies\")");
                else if (actual != expected)
                    problems.Add($"{path}: compiles into {actual}, expected {expected}");
                perAssembly[actual] = perAssembly.TryGetValue(actual, out int n) ? n + 1 : 1;
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
            // Not vacuous: every assembly holds scripts, and nothing fell back to the predefined runtime assembly.
            foreach (string layer in Layers)
                Assert.IsTrue(perAssembly.TryGetValue(layer, out int count) && count > 0, $"{layer} holds no script");
            Assert.IsFalse(perAssembly.ContainsKey("Assembly-CSharp"), "a runtime script compiles into Assembly-CSharp");
        }

        [Test]
        public void NoRuntimeAssemblyFolder_HoldsAnEditorFolder()
        {
            // An Editor folder below an asmdef / asmref folder is NOT editor-only: it would compile into the runtime
            // assembly and break the player build. Editor code lives in Scripts/Editor only.
            var nested = Directory.GetDirectories(ScriptsRoot, "Editor", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(p => p != $"{ScriptsRoot}/Editor").ToList();
            Assert.IsEmpty(nested, "Editor folders inside a runtime assembly:\n" + string.Join("\n", nested));
            Assert.IsEmpty(Directory.GetFiles($"{ScriptsRoot}/Editor", "*.asm*", SearchOption.AllDirectories),
                "Scripts/Editor must stay in Assembly-CSharp-Editor (it holds the tests and sees every assembly)");
        }

        [Test]
        public void EveryFolder_HasExactlyTheAssemblyFileItShould()
        {
            var problems = new List<string>();
            foreach (var kv in FolderAssembly)
            {
                string folder = $"{ScriptsRoot}/{kv.Key}";
                bool ownsAsmdef = AsmdefFolder[kv.Value] == kv.Key;
                var defs = Directory.GetFiles(folder, "*.asmdef").Select(Path.GetFileNameWithoutExtension).ToList();
                var refs = Directory.GetFiles(folder, "*.asmref").ToList();
                if (ownsAsmdef)
                {
                    if (defs.Count != 1 || defs[0] != kv.Value) problems.Add($"{kv.Key}: expected {kv.Value}.asmdef, found [{string.Join(", ", defs)}]");
                    if (refs.Count != 0) problems.Add($"{kv.Key}: an asmdef folder must not also hold an asmref");
                }
                else
                {
                    if (defs.Count != 0) problems.Add($"{kv.Key}: unexpected asmdef [{string.Join(", ", defs)}]");
                    if (refs.Count != 1) { problems.Add($"{kv.Key}: expected one asmref to {kv.Value}, found {refs.Count}"); continue; }
                    string target = ReferenceName(JsonUtility.FromJson<AsmrefJson>(File.ReadAllText(refs[0])).reference);
                    if (target != kv.Value) problems.Add($"{kv.Key}: its asmref points at {target}, expected {kv.Value}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
            int files = Directory.GetFiles(ScriptsRoot, "*.asm*", SearchOption.AllDirectories).Count(p => !p.EndsWith(".meta"));
            Assert.AreEqual(FolderAssembly.Count, files, "an .asmdef / .asmref exists in a folder FolderAssembly does not list");
        }

        [Test]
        public void EveryAsmdef_KeepsExactlyItsReferences([Values(Core, Gameplay, Presentation, Diagnostics, App)] string assembly)
        {
            var asmdef = Load(assembly);
            Assert.AreEqual(assembly, asmdef.name);
            CollectionAssert.AreEquivalent(References[assembly], asmdef.references.Select(ReferenceName).ToList(),
                $"{assembly}: the reference list changed. A layer references only the layers below it; a new package " +
                "reference is added here on purpose, never an upward one");
        }

        [Test]
        public void EveryAsmdef_KeepsItsSettings([Values(Core, Gameplay, Presentation, Diagnostics, App)] string assembly)
        {
            var asmdef = Load(assembly);
            // The built-in Assembly-CSharp-Editor (editor tools and these tests) only sees auto-referenced assemblies.
            Assert.IsTrue(asmdef.autoReferenced, $"{assembly}: autoReferenced");
            Assert.IsTrue(asmdef.allowUnsafeCode, $"{assembly}: allowUnsafeCode (matches the player setting)");
            Assert.IsFalse(asmdef.noEngineReferences, $"{assembly}: noEngineReferences");
            Assert.IsEmpty(asmdef.includePlatforms ?? Array.Empty<string>(), $"{assembly}: includePlatforms");
            Assert.IsEmpty(asmdef.excludePlatforms ?? Array.Empty<string>(), $"{assembly}: excludePlatforms");
            Assert.IsEmpty(asmdef.defineConstraints ?? Array.Empty<string>(), $"{assembly}: defineConstraints");
            Assert.IsTrue(string.IsNullOrEmpty(asmdef.rootNamespace), $"{assembly}: rootNamespace");

            if (assembly == Core)
            {
                // Core sees no prebuilt DLL except the one that declares Zenject's [Inject]: in particular no Fusion.
                Assert.IsTrue(asmdef.overrideReferences, "Core: overrideReferences must stay on");
                CollectionAssert.AreEquivalent(new[] { "Zenject-usage.dll" }, asmdef.precompiledReferences);
            }
            else
            {
                // The Fusion runtime DLLs reach the other four automatically.
                Assert.IsFalse(asmdef.overrideReferences, $"{assembly}: overrideReferences");
                Assert.IsEmpty(asmdef.precompiledReferences ?? Array.Empty<string>(), $"{assembly}: precompiledReferences");
            }
        }

        private static Assembly Loaded(string name)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            Assert.IsNotNull(assembly, $"{name} is not loaded");
            return assembly;
        }

        [Test]
        public void TheCompiledAssemblies_ReferenceOnlyTheLayersBelow()
        {
            // What the compiler actually emitted, independent of the asmdef text.
            for (int i = 0; i < Layers.Length; i++)
            {
                var referenced = Loaded(Layers[i]).GetReferencedAssemblies().Select(r => r.Name).ToList();
                var upward = referenced.Where(r => r.StartsWith("CluckWars.", StringComparison.Ordinal) && Array.IndexOf(Layers, r) >= i).ToList();
                Assert.IsEmpty(upward, $"{Layers[i]} references {string.Join(", ", upward)}");
            }
            var core = Loaded(Core).GetReferencedAssemblies().Select(r => r.Name).ToList();
            Assert.IsEmpty(core.Where(r => r.StartsWith("Fusion", StringComparison.Ordinal)).ToList(), "Core references Fusion");
            Assert.IsNull(AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp"),
                "Assembly-CSharp exists again: some runtime script sits outside the five assemblies");
        }

        [Test]
        public void TheWeaveList_NamesAllFiveAssemblies_AndTheNetworkTypesLiveWhereTheGuardLooks()
        {
            var listed = WeavingGuardTests.AssembliesToWeave();
            // Gameplay and Presentation hold network code; Diagnostics and App are listed so a NetworkBehaviour added
            // there later is woven without anyone remembering the config. Core cannot reference Fusion.
            foreach (string must in new[] { Gameplay, Presentation, Diagnostics, App })
                CollectionAssert.Contains(listed, must, $"{must} is missing from AssembliesToWeave");
            foreach (string kept in new[] { "Fusion.Unity", "Assembly-CSharp", "Assembly-CSharp-firstpass", "Fusion.Addons.Physics", "Fusion.Addons.FSM" })
                CollectionAssert.Contains(listed, kept, $"{kept} was removed from AssembliesToWeave");

            Assert.IsNotNull(Loaded(Gameplay).GetType("CluckWars.Gameplay.ChickenController"), "ChickenController is not in Gameplay");
            Assert.IsNotNull(Loaded(Gameplay).GetType("CluckWars.Networking.PlayerNetworkInput"), "PlayerNetworkInput is not in Gameplay");
            Assert.IsNotNull(Loaded(Presentation).GetTypes().FirstOrDefault(t => t.Name == "HitFeedback"), "HitFeedback is not in Presentation");
        }
    }
}
