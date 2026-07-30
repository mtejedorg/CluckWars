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
            generator.BuildStaticGeometry(root.transform);

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

            // Strip everything the clone brought across — we want its settings, not its
            // contents. The geometry root is added afterwards.
            foreach (var go in mapScene.GetRootGameObjects())
                Object.DestroyImmediate(go);

            SceneManager.MoveGameObjectToScene(root, mapScene);

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
