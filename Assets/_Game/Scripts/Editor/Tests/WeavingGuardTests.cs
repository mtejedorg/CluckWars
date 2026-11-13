using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Architecture audit batch 4 (assembly split). Fusion's IL weaver only processes the assemblies listed in
    /// <c>AssembliesToWeave</c> of <c>NetworkProjectConfig.fusion</c>. A NetworkBehaviour compiled into an assembly
    /// that is not listed still compiles and still spawns, but its <c>[Networked]</c> properties and RPCs do nothing:
    /// no error anywhere, a dead game at runtime. This guard reads what the weaver leaves behind (attributes, matched
    /// by type NAME so the test never depends on which Fusion assembly declares them) on every game assembly that is
    /// loaded: the five <c>CluckWars.*</c> assemblies since the split (phase B), and <c>Assembly-CSharp</c> should a
    /// script ever fall back into it (<see cref="AssemblyLayoutTests"/> forbids that).
    /// <para>What the weaver writes (<c>Assets/Photon/Fusion/CodeGen/Fusion.CodeGen.cs</c>, <c>ILWeaver.Weave</c>):
    /// <c>NetworkBehaviourWeavedAttribute</c> on every NetworkBehaviour subclass, <c>NetworkStructWeavedAttribute</c>
    /// on every INetworkStruct value type, <c>NetworkInputWeavedAttribute</c> on every INetworkInput value type, and
    /// <c>NetworkAssemblyWeavedAttribute</c> on an assembly it modified. A SimulationBehaviour that is not a
    /// NetworkBehaviour gets no type attribute (only its RPCs are rewritten), so it is covered by the assembly-level
    /// checks.</para>
    /// </summary>
    public sealed class WeavingGuardTests
    {
        private const string BehaviourWeaved = "NetworkBehaviourWeavedAttribute";
        private const string StructWeaved = "NetworkStructWeavedAttribute";
        private const string InputWeaved = "NetworkInputWeavedAttribute";
        private const string AssemblyWeaved = "NetworkAssemblyWeavedAttribute";
        private const string ConfigPath = "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion";

        private static bool IsGameAssembly(Assembly a)
        {
            string name = a.GetName().Name;
            return name == "Assembly-CSharp" || name.StartsWith("CluckWars.", StringComparison.Ordinal);
        }

        private static List<Assembly> GameAssemblies() =>
            AppDomain.CurrentDomain.GetAssemblies().Where(IsGameAssembly).OrderBy(a => a.GetName().Name).ToList();

        private static Type[] TypesOf(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                Assert.Fail($"{a.GetName().Name}: types failed to load, the guard cannot judge it ({e.LoaderExceptions.FirstOrDefault()?.Message})");
                return Array.Empty<Type>();
            }
        }

        private static bool Has(MemberInfo member, string attributeName) =>
            member.GetCustomAttributesData().Any(d => d.AttributeType.Name == attributeName);

        private static bool Has(Assembly assembly, string attributeName) =>
            assembly.GetCustomAttributesData().Any(d => d.AttributeType.Name == attributeName);

        private static bool IsNetworkBehaviour(Type t) => t.IsClass && t.IsSubclassOf(typeof(NetworkBehaviour));
        private static bool IsSimulationOnly(Type t) => t.IsClass && t.IsSubclassOf(typeof(SimulationBehaviour)) && !IsNetworkBehaviour(t);
        private static bool IsNetworkStruct(Type t) => t.IsValueType && !t.IsEnum && typeof(INetworkStruct).IsAssignableFrom(t);
        private static bool IsNetworkInput(Type t) => t.IsValueType && !t.IsEnum && typeof(INetworkInput).IsAssignableFrom(t);

        /// <summary>True when the weaver has work to do in this assembly, so it must be listed and stamped.</summary>
        private static bool NeedsWeaving(Assembly a) =>
            TypesOf(a).Any(t => IsNetworkBehaviour(t) || IsSimulationOnly(t) || IsNetworkStruct(t) || IsNetworkInput(t));

        internal static HashSet<string> AssembliesToWeave()
        {
            Assert.IsTrue(File.Exists(ConfigPath), $"{ConfigPath} is missing");
            string json = File.ReadAllText(ConfigPath);
            var block = Regex.Match(json, "\"AssembliesToWeave\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            Assert.IsTrue(block.Success, $"{ConfigPath} has no AssembliesToWeave list");
            return new HashSet<string>(Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
        }

        [Test]
        public void TheGuard_SeesTheGameCode()
        {
            // Not vacuous: the assemblies exist and hold the behaviours the game is made of.
            var assemblies = GameAssemblies();
            Assert.IsNotEmpty(assemblies, "no Assembly-CSharp / CluckWars.* assembly is loaded");
            var behaviours = assemblies.SelectMany(TypesOf).Where(IsNetworkBehaviour).Select(t => t.Name).ToList();
            foreach (string must in new[] { "ChickenController", "ChickenCargo", "ChickenCombat", "AbilityController", "GameManager", "FoodPile", "PlayerBase", "AbilityZone" })
                CollectionAssert.Contains(behaviours, must, $"the guard no longer finds {must}: it moved to an assembly this test does not scan");
            var inputs = assemblies.SelectMany(TypesOf).Where(IsNetworkInput).Select(t => t.Name).ToList();
            CollectionAssert.Contains(inputs, "PlayerNetworkInput");
        }

        [Test]
        public void EveryNetworkBehaviour_IsWeaved()
        {
            var unweaved = GameAssemblies().SelectMany(TypesOf).Where(IsNetworkBehaviour)
                .Where(t => !Has(t, BehaviourWeaved))
                .Select(t => $"{t.Assembly.GetName().Name}: {t.FullName}").ToList();
            Assert.IsEmpty(unweaved, "NetworkBehaviours the Fusion weaver did not process (their [Networked] state and RPCs are dead). " +
                $"Add the assembly to AssembliesToWeave in {ConfigPath}:\n" + string.Join("\n", unweaved));
        }

        [Test]
        public void EveryNetworkStructAndInput_IsWeaved()
        {
            var unweaved = new List<string>();
            foreach (var t in GameAssemblies().SelectMany(TypesOf))
            {
                // The weaver stamps an input with NetworkInputWeaved; a type already stamped as a struct is left as is.
                if (IsNetworkInput(t) && !Has(t, InputWeaved) && !Has(t, StructWeaved)) unweaved.Add($"{t.Assembly.GetName().Name}: input {t.FullName}");
                else if (IsNetworkStruct(t) && !IsNetworkInput(t) && !Has(t, StructWeaved)) unweaved.Add($"{t.Assembly.GetName().Name}: struct {t.FullName}");
            }
            Assert.IsEmpty(unweaved, $"Network structs / inputs the Fusion weaver did not process. Add the assembly to AssembliesToWeave in {ConfigPath}:\n" +
                string.Join("\n", unweaved));
        }

        [Test]
        public void EveryAssemblyWithNetworkCode_IsStampedAndListed()
        {
            var listed = AssembliesToWeave();
            var problems = new List<string>();
            int judged = 0;
            foreach (var a in GameAssemblies())
            {
                if (!NeedsWeaving(a)) continue;   // an assembly with no Fusion type is neither modified nor required in the list
                judged++;
                string name = a.GetName().Name;
                if (!Has(a, AssemblyWeaved)) problems.Add($"{name}: no {AssemblyWeaved} (the weaver never ran on it)");
                if (!listed.Contains(name)) problems.Add($"{name}: not in AssembliesToWeave");
            }
            Assert.Greater(judged, 0, "no game assembly holds network code: the guard is looking at the wrong assemblies");
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
