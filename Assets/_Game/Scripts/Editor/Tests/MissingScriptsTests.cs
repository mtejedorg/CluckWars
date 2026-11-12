using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Architecture audit batch 4 (file moves, then the assembly split). A script that moves without its
    /// <c>.meta</c>, or whose class no longer resolves in its assembly, turns into a "missing script" on every prefab
    /// and scene object that used it: no compile error, a silently dead component. Prefabs are loaded and asked
    /// directly; scenes are read as YAML (opening every build scene in EditMode would replace the open scene), and
    /// every script reference in them must resolve to a script asset whose class loads.
    /// </summary>
    public sealed class MissingScriptsTests
    {
        private const string PrefabRoot = "Assets/_Game/Prefabs";

        // m_Script: {fileID: 11500000, guid: 0123..., type: 3}
        private static readonly Regex ScriptRef = new Regex(@"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}");

        [Test]
        public void NoPrefab_HasAMissingScript()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            Assert.Greater(guids.Length, 5, $"found almost no prefab under {PrefabRoot}: the test is looking in the wrong place");

            var problems = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) { problems.Add($"{path}: does not load"); continue; }
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0) problems.Add($"{path}: '{t.name}' has {missing} missing script(s)");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void NoPrefabFile_ReferencesAScriptThatDoesNotResolve()
        {
            // The YAML view of the same prefabs: also catches a reference on a stripped / variant-overridden object.
            var problems = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
                problems.AddRange(UnresolvedScripts(AssetDatabase.GUIDToAssetPath(guid)));
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void NoBuildScene_ReferencesAScriptThatDoesNotResolve()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Assert.IsNotEmpty(scenes, "EditorBuildSettings lists no enabled scene");

            var problems = new List<string>();
            int references = 0;
            foreach (string scene in scenes)
            {
                if (!File.Exists(scene)) { problems.Add($"{scene}: listed in EditorBuildSettings but the file is missing"); continue; }
                references += ScriptRef.Matches(File.ReadAllText(scene)).Count;
                problems.AddRange(UnresolvedScripts(scene));
            }
            Assert.Greater(references, 10, "the build scenes hold almost no script reference: the YAML pattern no longer matches");
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>Every distinct script reference in a YAML asset that resolves to no script, or to one whose class does not load.</summary>
        private static IEnumerable<string> UnresolvedScripts(string assetPath)
        {
            var seen = new HashSet<string>();
            foreach (Match m in ScriptRef.Matches(File.ReadAllText(assetPath)))
            {
                string fileId = m.Groups[1].Value, guid = m.Groups[2].Value;
                if (!seen.Add(fileId + guid)) continue;

                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(scriptPath) || AssetDatabase.LoadMainAssetAtPath(scriptPath) == null)
                {
                    yield return $"{assetPath}: script guid {guid} resolves to no asset (a script was moved or deleted without its .meta)";
                    continue;
                }
                if (fileId != "11500000") continue;   // a class inside a precompiled dll: the dll asset exists, that is all YAML can tell

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (script == null) yield return $"{assetPath}: {scriptPath} (guid {guid}) is not a script";
                else if (script.GetClass() == null) yield return $"{assetPath}: {scriptPath} holds no loadable class (file / class name mismatch, or it no longer compiles into an assembly)";
            }
        }
    }
}
