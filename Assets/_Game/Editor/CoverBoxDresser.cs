using System.Linq;
using CluckWars.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Dresses the baked scatter cover (<c>GeneratedMapGeometry/Terrain_Cover_*</c>) with a crate
    /// stack sized exactly to each cover's collider, and hides the bare cube it used to render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The collider is the subject; the art follows it.</b> Nothing on the cover itself changes
    /// except its own <c>MeshRenderer.enabled</c> - the <c>BoxCollider</c>, transform, layer and
    /// <see cref="TerrainObstacle"/> tag (which traversal and the NavMesh read) stay exactly as the
    /// generator baked them. The visual is a child named <see cref="VisualName"/> with no colliders,
    /// so the NavMesh (built from physics colliders) and the traversal probes cannot see it.
    /// </para>
    /// <para>
    /// <b>Why crates, not hay bales:</b> the hay-bale mesh carries a black ground plate wider than the
    /// bale (a separate Blender fix); the crate has none and reads as a solid block.
    /// </para>
    /// <para>
    /// <b>Survives a re-bake:</b> <see cref="MapSceneBaker.BakeMapScene"/> calls
    /// <see cref="DressCovers"/> on the fresh geometry root, so a re-bake recreates the visuals for
    /// wherever the cover lands. The menu item re-applies it in place to the existing
    /// <c>Map.unity</c> without re-baking (a re-bake rolls a new scatter seed and moves the cover).
    /// Idempotent: an existing <see cref="VisualName"/> child is replaced.
    /// </para>
    /// <para>
    /// Only the baked map is dressed. The procedural fallback (<c>MapGenerator._useBakedGeometry =
    /// false</c>) still renders the plain cube - that path is not shipped.
    /// </para>
    /// </remarks>
    public static class CoverBoxDresser
    {
        public const string CoverPrefix = "Terrain_Cover_";
        public const string VisualName = "CoverVisual";
        public const string CratePrefabPath = "Assets/_Game/Prefabs/Props/Prop_crate.prefab";

        private const string MapScenePath = "Assets/_Game/Scenes/Map.unity";
        private const string GeometryRootName = "GeneratedMapGeometry";

        [MenuItem("Cluck Wars/Map/Dress Cover Boxes (in place)", priority = 22)]
        public static void DressCoversInMapScene()
        {
            Scene map = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == MapScenePath) map = s;
            }
            bool openedHere = !map.IsValid() || !map.isLoaded;
            if (openedHere) map = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);

            var root = map.GetRootGameObjects().FirstOrDefault(g => g.name == GeometryRootName);
            if (root == null)
            {
                Debug.LogError($"[CoverBoxDresser] {MapScenePath} has no {GeometryRootName} root - bake the map first.");
                if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
                return;
            }

            int dressed = DressCovers(root.transform);
            EditorSceneManager.MarkSceneDirty(map);
            if (!EditorSceneManager.SaveScene(map))
                Debug.LogError($"[CoverBoxDresser] Failed to save {MapScenePath}.");
            if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
            Debug.Log($"[CoverBoxDresser] Dressed {dressed} cover box(es) in {MapScenePath}.");
        }

        /// <summary>
        /// Dresses every direct child of <paramref name="geometryRoot"/> named
        /// <see cref="CoverPrefix"/>*. Returns how many were dressed.
        /// </summary>
        public static int DressCovers(Transform geometryRoot)
        {
            var crate = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
            if (crate == null)
            {
                Debug.LogError($"[CoverBoxDresser] {CratePrefabPath} is missing - cover boxes left as bare cubes.");
                return 0;
            }

            var (natCenter, natSize) = MeasurePrefab(crate);
            int dressed = 0;
            foreach (Transform cover in geometryRoot)
            {
                if (!cover.name.StartsWith(CoverPrefix)) continue;
                var box = cover.GetComponent<BoxCollider>();
                if (box == null)
                {
                    Debug.LogError($"[CoverBoxDresser] {cover.name} has no BoxCollider - skipped (left rendering its cube).");
                    continue;
                }
                Dress(cover, box, crate, natCenter, natSize);
                dressed++;
            }
            return dressed;
        }

        /// <summary>
        /// Crates per axis for a box of <paramref name="size"/> filled with crates whose natural size
        /// is <paramref name="crateSize"/>: the nearest whole count on each axis (never zero), which
        /// keeps every crate's stretch as close to 1 as a whole tiling allows.
        /// </summary>
        public static Vector3Int SolveGrid(Vector3 size, Vector3 crateSize) => new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(size.x / crateSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(size.y / crateSize.y)),
            Mathf.Max(1, Mathf.RoundToInt(size.z / crateSize.z)));

        private static void Dress(Transform cover, BoxCollider box, GameObject crate, Vector3 natCenter, Vector3 natSize)
        {
            var old = cover.Find(VisualName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var mr = cover.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;

            // A metric frame at the box centre: cancel the cover's (non-uniform) scale so the
            // crates below are laid out in metres, axis-aligned with the box - no shear.
            Vector3 ls = cover.lossyScale;
            var visual = new GameObject(VisualName).transform;
            visual.SetParent(cover, worldPositionStays: false);
            visual.localPosition = box.center;
            visual.localRotation = Quaternion.identity;
            visual.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);

            Vector3 size = Vector3.Scale(box.size, ls);
            Vector3Int grid = SolveGrid(size, natSize);
            Vector3 cell = new Vector3(size.x / grid.x, size.y / grid.y, size.z / grid.z);
            Vector3 k = new Vector3(cell.x / natSize.x, cell.y / natSize.y, cell.z / natSize.z);

            var flags = GameObjectUtility.GetStaticEditorFlags(crate.GetComponentInChildren<Renderer>(true).gameObject);
            GameObjectUtility.SetStaticEditorFlags(visual.gameObject, flags);

            for (int y = 0; y < grid.y; y++)
            for (int x = 0; x < grid.x; x++)
            for (int z = 0; z < grid.z; z++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(crate, visual);
                inst.name = $"Crate_{y}_{x}_{z}";
                var t = inst.transform;
                t.localRotation = Quaternion.identity;
                t.localScale = k;
                Vector3 cellCenter = new Vector3(
                    -size.x * 0.5f + cell.x * (x + 0.5f),
                    -size.y * 0.5f + cell.y * (y + 0.5f),
                    -size.z * 0.5f + cell.z * (z + 0.5f));
                t.localPosition = cellCenter - Vector3.Scale(natCenter, k);

                foreach (var go in inst.GetComponentsInChildren<Transform>(true).Select(c => c.gameObject))
                    GameObjectUtility.SetStaticEditorFlags(go, flags);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = ShadowCastingMode.On;
                foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
            }
        }

        /// <summary>The prefab's combined renderer bounds, relative to its root, at identity.</summary>
        private static (Vector3 center, Vector3 size) MeasurePrefab(GameObject prefab)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                probe.transform.localScale = Vector3.one;
                var rs = probe.GetComponentsInChildren<Renderer>(true);
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return (b.center, b.size);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }
    }
}
