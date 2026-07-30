using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Gameplay;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Bakes the arena's static geometry into a standalone, hand-editable scene.
    ///
    /// <para>
    /// v0.5 fixed the map layout (see <c>docs/GDD.md</c> §3.4 — wall jitter had to go to
    /// zero, so every match generates the identical arena). Regenerating identical
    /// geometry on every launch buys nothing and, more importantly, leaves the level
    /// impossible to inspect or hand-tune: it only existed at runtime. Baking it to a
    /// scene makes it a normal, versioned, editable asset.
    /// </para>
    ///
    /// <para>
    /// <b>Only static geometry is baked</b> — ground, boundary, interior walls, plus a
    /// NavMesh. Player bases and food piles are <c>NetworkObject</c>s that Fusion must
    /// spawn at runtime with correct ownership, so <see cref="MapGenerator"/> keeps doing
    /// that on the master client. This is why the component stays in <c>Game.unity</c>
    /// rather than moving wholesale into the map scene.
    /// </para>
    /// </summary>
    public static class MapSceneBaker
    {
        private const string MapScenePath  = "Assets/_Game/Scenes/Map.unity";
        private const string GameScenePath = "Assets/_Game/Scenes/Game.unity";
        private const string GeometryRootName = "GeneratedMapGeometry";
        private const string MaterialsFolder = "Assets/_Game/Art/Materials";
        private const string PileZoneMaterialPath = MaterialsFolder + "/PileZone.mat";

        [MenuItem("Cluck Wars/Map/Bake Map Scene", priority = 20)]
        public static void BakeMapScene()
        {
            // The authored tuning lives on Game.unity's MapGenerator — that is the single
            // source of truth the EditMode tests read too. Bake from it rather than
            // duplicating the numbers here, or the scene and the tests will drift.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[MapSceneBaker] Bake cancelled — unsaved changes were kept.");
                return;
            }

            var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var generator = Object.FindObjectsByType<MapGenerator>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault();

            if (generator == null)
            {
                Debug.LogError($"[MapSceneBaker] No MapGenerator found in {GameScenePath}. " +
                               "Cannot bake without its authored tuning.");
                return;
            }

            // Build detached, so nothing is parented under the runtime component.
            var root = new GameObject(GeometryRootName);
            generator.BuildStaticGeometry(root.transform, EnsurePileZoneMaterial());

            int built = root.transform.childCount;
            if (built == 0)
            {
                Object.DestroyImmediate(root);
                Debug.LogError("[MapSceneBaker] Generator produced no geometry — aborting so an " +
                               "empty map scene is never written over a good one.");
                return;
            }

            // The map scene is CLONED from Game.unity rather than created with
            // EditorSceneManager.NewScene. A newly-created scene is written in Unity's
            // BINARY format and stubbornly stays that way — ForceReserializeAssets, a
            // dirty re-save and a forced reimport were all tried and none converted it —
            // even though the project is set to ForceText. A scene that cannot be diffed
            // or merged defeats the point of baking it for hand-editing. Cloning an
            // existing text scene inherits both the text serialization and Game.unity's
            // render/lighting settings, so the baked map looks the same as in play.
            AssetDatabase.DeleteAsset(MapScenePath);
            if (!AssetDatabase.CopyAsset(GameScenePath, MapScenePath))
            {
                Object.DestroyImmediate(root);
                Debug.LogError($"[MapSceneBaker] Could not clone {GameScenePath} -> {MapScenePath}.");
                return;
            }
            AssetDatabase.Refresh();

            var mapScene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);

            // Strip the clone's contents but KEEP its lights. Deleting everything left the
            // map scene with no Directional Light, so the baked geometry rendered almost
            // black under ambient alone — the scene looked broken while every scripted
            // check still passed.
            foreach (var go in mapScene.GetRootGameObjects())
            {
                if (go.GetComponentInChildren<Light>(true) != null) continue;
                Object.DestroyImmediate(go);
            }

            SceneManager.MoveGameObjectToScene(root, mapScene);

            // Materials generated at runtime carry HideFlags.DontSave, which is right for
            // play but means they are NOT written into a saved scene — the walls came back
            // magenta. Persist them as assets and re-point the renderers before saving.
            PersistGeneratedMaterials(root);

            // NavMesh is baked here rather than at runtime: the geometry is static, so
            // paying for a bake every launch is waste, and a baked surface also lets the
            // map scene be opened and pathing inspected without entering Play Mode.
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            Directory.CreateDirectory(Path.GetDirectoryName(MapScenePath)!);
            if (!EditorSceneManager.SaveScene(mapScene, MapScenePath))
            {
                Debug.LogError($"[MapSceneBaker] Failed to save {MapScenePath}.");
                return;
            }

            EnsureSceneInBuildSettings(MapScenePath);

            // Leave the editor on the map scene so the result is visible immediately —
            // the whole point of baking is being able to look at it.
            EditorSceneManager.CloseScene(gameScene, removeScene: true);

            Debug.Log($"[MapSceneBaker] Baked {built} geometry objects into {MapScenePath} " +
                      "and added it to Build Settings. Open it any time to inspect or hand-tune.");
        }

        [MenuItem("Cluck Wars/Map/Open Map Scene", priority = 21)]
        public static void OpenMapScene()
        {
            if (!File.Exists(MapScenePath))
            {
                Debug.LogWarning($"[MapSceneBaker] {MapScenePath} does not exist yet — " +
                                 "run Cluck Wars/Map/Bake Map Scene first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Replaces every in-memory material under <paramref name="root"/> with a saved
        /// asset equivalent.
        /// </summary>
        /// <remarks>
        /// <see cref="MapGenerator"/> builds its obstacle tints at runtime via
        /// <c>new Material(...)</c> with <c>HideFlags.DontSave</c> — correct for play,
        /// where they must not leak into the project, but a saved scene cannot reference
        /// them, so the walls reloaded with no material and rendered magenta. Assets are
        /// reused across bakes at a stable path so repeated bakes don't litter the project
        /// with PileZone 1.mat, PileZone 2.mat, …
        /// </remarks>
        private static void PersistGeneratedMaterials(GameObject root)
        {
            var remapped = new Dictionary<Material, Material>();

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = renderer.sharedMaterial;
                if (source == null || EditorUtility.IsPersistent(source)) continue;

                if (!remapped.TryGetValue(source, out var saved))
                {
                    string safeName = string.Concat(source.name.Split(Path.GetInvalidFileNameChars()));
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "GeneratedMapMaterial";
                    string path = $"{MaterialsFolder}/{safeName}.mat";

                    Directory.CreateDirectory(MaterialsFolder);
                    saved = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (saved == null)
                    {
                        saved = new Material(source) { name = safeName };
                        AssetDatabase.CreateAsset(saved, path);
                    }
                    else
                    {
                        // Reuse the existing asset so references elsewhere survive, but
                        // refresh it from the freshly generated material.
                        saved.shader = source.shader;
                        saved.CopyPropertiesFromMaterial(source);
                        EditorUtility.SetDirty(saved);
                    }
                    remapped[source] = saved;
                }

                renderer.sharedMaterial = saved;
            }

            if (remapped.Count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[MapSceneBaker] Persisted {remapped.Count} generated material(s) to " +
                          $"{MaterialsFolder} so the baked scene keeps them.");
            }
        }

        /// <summary>
        /// Loads (or creates) the shared material for the depleted-pile zone markers.
        /// This must be a real ASSET, not a runtime <c>new Material(...)</c>: materials
        /// created in memory are not serialized into a saved scene, so the markers would
        /// come back with a missing material — magenta — the next time the map scene was
        /// opened.
        /// </summary>
        private static Material EnsurePileZoneMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(PileZoneMaterialPath);
            if (existing != null) return existing;

            // URP Lit explicitly: the project is URP-only, and the primitive default is
            // a Built-in shader that renders magenta here (docs/CONVENTIONS.md).
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[MapSceneBaker] URP/Lit shader not found — pile zone markers " +
                                 "will use the primitive default and may render magenta.");
                return null;
            }

            var mat = new Material(shader) { name = "PileZone" };
            // Dry, dark earth — reads as an emptied pile rather than as terrain.
            var brown = new Color(0.34f, 0.25f, 0.17f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", brown);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", brown);
            // Flat and matte: a glossy dirt patch would pull the eye away from the chickens.
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);

            Directory.CreateDirectory(Path.GetDirectoryName(PileZoneMaterialPath)!);
            AssetDatabase.CreateAsset(mat, PileZoneMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// Adds the map scene to Build Settings if missing. Without this the runtime
        /// additive load fails and the arena comes up empty — the failure mode
        /// <see cref="MapGenerator"/> logs an error for.
        /// </summary>
        private static void EnsureSceneInBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == path))
            {
                // Already listed — make sure it is enabled, a disabled entry fails the
                // same way a missing one does.
                foreach (var s in scenes)
                    if (s.path == path) s.enabled = true;
                EditorBuildSettings.scenes = scenes;
                return;
            }

            // Appended last on purpose: Bootstrap must stay at index 0 as the startup
            // scene (see docs/CONVENTIONS.md — EditorBuildSettings scene order).
            EditorBuildSettings.scenes = scenes
                .Append(new EditorBuildSettingsScene(path, true))
                .ToArray();
        }
    }
}
