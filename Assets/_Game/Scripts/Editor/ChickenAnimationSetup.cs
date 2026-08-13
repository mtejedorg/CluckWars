using System.Collections.Generic;
using System.Linq;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// One-shot authoring pass that wires the per-class skeletal clips into the shared
    /// <c>Chicken.controller</c> and the class registry. Idempotent — safe to re-run after a
    /// re-export of the rigs, which is exactly when it is needed.
    /// </summary>
    /// <remarks>
    /// This exists as a script rather than as hand-authoring because two of the three steps are
    /// error-prone by hand and silent when wrong:
    /// <list type="bullet">
    /// <item>the five placeholder clips are the <em>override keys</em> — an
    /// <see cref="AnimatorOverrideController"/> keys by the original clip in each Motion slot, so a
    /// renamed or missing placeholder makes the override resolve to nothing and the chicken freezes
    /// in bind pose with no error;</item>
    /// <item>each class's clips must come from the same .fbx as its own ModelPrefab. Sourcing them
    /// here <em>from that prefab's own asset path</em> makes cross-class drift impossible by
    /// construction instead of something a human has to keep straight across 20 inspector slots —
    /// the failure mode that already bit this project once with bot loadouts.</item>
    /// <item>Loop Time lives on the .fbx <em>importer</em>, not on the clip asset, and the rigs
    /// import with it off. A non-looping Idle plays once and holds — a chicken that animates for
    /// two seconds and then freezes. That cannot be fixed by hand on the clip, only on the
    /// importer, and it must be re-fixed after every re-export.</item>
    /// </list>
    /// </remarks>
    public static class ChickenAnimationSetup
    {
        private const string ControllerPath   = "Assets/_Game/Art/Animations/Chicken.controller";
        private const string PlaceholderDir   = "Assets/_Game/Art/Animations/Placeholders";
        private const string RegistryPath     = "Assets/_Game/Data/ChickenClassRegistry.asset";

        /// <summary>State name → does that state's clip loop. Also the override-key set.</summary>
        private static readonly (string State, bool Loop)[] States =
        {
            ("Idle",    true),
            ("Walk",    true),
            ("Cast",    false),
            ("Hit",     false),
            ("Stunned", true),
        };

        private static readonly Dictionary<string, bool> LoopByState =
            States.ToDictionary(s => s.State, s => s.Loop);

        /// <summary>
        /// Takes and clips are exported as <c>"&lt;rig&gt;|&lt;State&gt;"</c>; everything here keys
        /// on the state suffix so the per-class rig prefix never has to be reconstructed.
        /// </summary>
        private static string StateSuffix(string name) =>
            string.IsNullOrEmpty(name) ? null
            : name.Contains('|') ? name.Split('|').Last()
            : name;

        [MenuItem("Cluck Wars/Animation/Wire Skeletal Clips")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ChickenAnimationSetup] Not available in Play Mode — importer and " +
                               "asset edits made now are reverted when Play Mode exits, so this " +
                               "would appear to succeed and silently change nothing. Stop Play Mode " +
                               "and re-run.");
                return;
            }

            var problems = new List<string>();

            var placeholders = CreatePlaceholders(problems);
            AssignMotions(placeholders, problems);

            // Must precede PopulateRegistry: fixing the loop flags reimports each .fbx, which
            // replaces its AnimationClip sub-asset objects. Populating first would cache
            // references to the pre-reimport clips.
            FixClipLoopFlags(problems);
            PopulateRegistry(problems);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (problems.Count > 0)
            {
                // Surfaced as an Error, not a silent partial success: a half-wired controller
                // produces chickens that animate on some beats and freeze on others.
                Debug.LogError("[ChickenAnimationSetup] Completed WITH PROBLEMS:\n  - " +
                               string.Join("\n  - ", problems));
            }
            else
            {
                Debug.Log("[ChickenAnimationSetup] Wired 5 placeholder clips, 5 controller states, " +
                          "clip loop flags on 4 .fbx importers, and 4 registry entries successfully.");
            }
        }

        /// <summary>
        /// Creates the five empty placeholder clips. Empty is deliberate: an empty clip evaluates
        /// to the bind pose, so an un-overridden state is a still chicken rather than an exception.
        /// </summary>
        private static Dictionary<string, AnimationClip> CreatePlaceholders(List<string> problems)
        {
            if (!AssetDatabase.IsValidFolder(PlaceholderDir))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Art/Animations", "Placeholders");
            }

            var result = new Dictionary<string, AnimationClip>();
            foreach (var (state, loop) in States)
            {
                string path = $"{PlaceholderDir}/{state}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = new AnimationClip { name = state };
                    AssetDatabase.CreateAsset(clip, path);
                }

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (settings.loopTime != loop)
                {
                    settings.loopTime = loop;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                }

                if (clip.name != state)
                {
                    problems.Add($"Placeholder at '{path}' is named '{clip.name}', not '{state}'. " +
                                 "The override controller keys by this name.");
                }
                result[state] = clip;
            }
            return result;
        }

        private static void AssignMotions(Dictionary<string, AnimationClip> placeholders, List<string> problems)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                problems.Add($"No AnimatorController at '{ControllerPath}'.");
                return;
            }

            var seen = new HashSet<string>();
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (!placeholders.TryGetValue(child.state.name, out var clip)) continue;
                    seen.Add(child.state.name);
                    if (child.state.motion != clip)
                    {
                        child.state.motion = clip;
                        EditorUtility.SetDirty(child.state);
                    }
                }
            }

            foreach (var (state, _) in States)
            {
                if (!seen.Contains(state))
                    problems.Add($"Controller has no state named '{state}' to assign a motion to.");
            }
            EditorUtility.SetDirty(controller);
        }

        /// <summary>
        /// Sets Loop Time on each rig's Idle/Walk/Stunned takes (and clears it on Cast/Hit) via the
        /// <see cref="ModelImporter"/>'s Animation tab.
        /// </summary>
        /// <remarks>
        /// The rigs import with <c>clipAnimations</c> empty, which means Unity generates the clips
        /// straight from the takes with <c>loopTime</c> defaulting to <b>false</b>. A non-looping
        /// Idle plays once and holds its last frame — an idle chicken that animates for two seconds
        /// and then freezes, which is the exact symptom the skeletal work exists to remove. Loop
        /// Time is importer state, not clip state, so it cannot be fixed from the clip asset.
        ///
        /// Only the Animation tab is touched. <c>animationType</c>, <c>avatarSetup</c> and the
        /// <c>externalObjects</c> material remap are load-bearing for the rig and the textures and
        /// are deliberately left alone.
        /// </remarks>
        private static void FixClipLoopFlags(List<string> problems)
        {
            foreach (var (label, fbxPath) in RegistryModelPaths(problems))
            {
                if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter importer)
                {
                    problems.Add($"{label}: '{fbxPath}' has no ModelImporter, so clip loop flags " +
                                 "cannot be set.");
                    continue;
                }

                // Seed from the takes when nothing has been authored yet: defaultClipAnimations
                // carries the real take names and frame ranges off the .fbx, so we never invent
                // a range and silently truncate a clip.
                var clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

                if (clips == null || clips.Length == 0)
                {
                    problems.Add($"{label}: '{fbxPath}' exposes no animation takes at all.");
                    continue;
                }

                bool changed = false;
                var matched = new HashSet<string>();

                foreach (var clip in clips)
                {
                    string state = StateSuffix(clip.name);
                    if (state == null || !LoopByState.TryGetValue(state, out bool shouldLoop)) continue;

                    matched.Add(state);
                    if (clip.loopTime == shouldLoop) continue;

                    clip.loopTime = shouldLoop;
                    changed = true;
                }

                foreach (var (state, _) in States)
                {
                    if (!matched.Contains(state))
                    {
                        problems.Add($"{label}: '{fbxPath}' has no animation take matching " +
                                     $"'|{state}', so its loop flag could not be set. Takes found: " +
                                     $"[{string.Join(", ", clips.Select(c => c.name))}]");
                    }
                }

                if (!changed) continue; // idempotent: already correct

                importer.clipAnimations = clips;
                // Synchronous — on return the reimport has landed and PopulateRegistry can safely
                // load the regenerated clip sub-assets.
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// The distinct .fbx path behind each registry entry's ModelPrefab, with a display label.
        /// Shared by the loop-flag and registry-population steps so both agree on what "this
        /// class's rig" means.
        /// </summary>
        private static List<(string Label, string FbxPath)> RegistryModelPaths(List<string> problems)
        {
            var result = new List<(string, string)>();
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);
            if (registry == null)
            {
                problems.Add($"No ChickenClassRegistrySO at '{RegistryPath}'.");
                return result;
            }

            var seen = new HashSet<string>();
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                if (!registry.TryGet(cls, out var entry))
                {
                    problems.Add($"{cls}: no registry entry.");
                    continue;
                }
                if (entry.ModelPrefab == null)
                {
                    problems.Add($"{cls}: no ModelPrefab, so its rig cannot be located.");
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(entry.ModelPrefab);
                if (string.IsNullOrEmpty(path))
                {
                    problems.Add($"{cls}: ModelPrefab '{entry.ModelPrefab.name}' has no asset path.");
                    continue;
                }
                if (seen.Add(path)) result.Add((cls.ToString(), path));
            }
            return result;
        }

        /// <summary>
        /// Fills each entry's five clip slots from that entry's OWN ModelPrefab .fbx, so a clip can
        /// never be sourced from another class's rig.
        /// </summary>
        private static void PopulateRegistry(List<string> problems)
        {
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);
            if (registry == null)
            {
                problems.Add($"No ChickenClassRegistrySO at '{RegistryPath}'.");
                return;
            }

            var so = new SerializedObject(registry);
            var entries = so.FindProperty("_entries");
            if (entries == null || !entries.isArray)
            {
                problems.Add("ChickenClassRegistrySO has no '_entries' array — did the field get renamed?");
                return;
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);

                // enumValueIndex is -1 for a value the enum no longer defines (e.g. a class removed
                // from ChickenClass while the asset still holds it), which would throw here — on the
                // line whose only job is to build an error message.
                var classProp = entry.FindPropertyRelative("Class");
                var names     = classProp.enumDisplayNames;
                string label  = classProp.enumValueIndex >= 0 && classProp.enumValueIndex < names.Length
                    ? names[classProp.enumValueIndex]
                    : $"entry[{i}] (unrecognised Class value)";

                var prefab = entry.FindPropertyRelative("ModelPrefab").objectReferenceValue;
                if (prefab == null)
                {
                    problems.Add($"{label}: no ModelPrefab, so its clips cannot be located.");
                    continue;
                }

                string fbxPath = AssetDatabase.GetAssetPath(prefab);
                var byState = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .GroupBy(c => StateSuffix(c.name))
                    .ToDictionary(g => g.Key, g => g.First());

                var clips = entry.FindPropertyRelative("Clips");
                foreach (var (state, _) in States)
                {
                    var slot = clips.FindPropertyRelative(state);
                    if (byState.TryGetValue(state, out var clip))
                    {
                        slot.objectReferenceValue = clip;
                    }
                    else
                    {
                        slot.objectReferenceValue = null;
                        problems.Add($"{label}: '{fbxPath}' has no clip ending in '|{state}'. " +
                                     $"Found: [{string.Join(", ", byState.Keys)}]");
                    }
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);
        }
    }
}
