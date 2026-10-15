using System.IO;
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
    /// Finishes the baked ground and zone markers in <c>Map.unity</c> (Phase 8 C5 + C17): the ground
    /// and every <c>PileZone_*</c> / <c>BaseZone_*</c> disc stop casting shadows (they keep receiving),
    /// and the discs use a saved 64-segment mesh asset instead of the 20-sided built-in Cylinder.
    /// Phase 8 C S1: the nine <c>PileZone_*</c> discs use the irregular-oval slab baked from <c>pile_disc_irregular.fbx</c> instead (Y scale 1, no lift).
    /// Phase 8 D9: the outer pile ovals (<c>PileZone_Personal*</c> / <c>PileZone_Contested*</c>) are
    /// yawed so their long axis points at the arena centre, matching the spawned piles.
    /// </summary>
    /// <remarks>
    /// <see cref="MapGenerator"/> already does both at build time, but its mesh is a
    /// <c>HideAndDontSave</c> runtime object that a saved scene cannot reference, so the baker (and the
    /// in-place menu item, which avoids a re-bake re-rolling the scatter seed) swaps in the asset.
    /// Position, scale and materials are never touched; only the outer pile discs' yaw is set (D9). Idempotent.
    /// </remarks>
    public static class ZoneMarkerBaker
    {
        public const string MeshAssetPath = "Assets/_Game/Art/Meshes/ZoneDisc64.asset";
        /// <summary>Source of the irregular pile disc (Phase 8 C S1). Its FBX node carries a 270 deg X rotation (Blender Z-up); the baked asset below has it applied.</summary>
        public const string PileDiscFbxPath = "Assets/Generated/Env/pile_disc_irregular.fbx";
        /// <summary>The FBX mesh with the node rotation applied (Y-up, unit footprint, 2 cm slab y 0..0.02), so a zone marker can use it on its own GameObject.</summary>
        public const string PileDiscMeshAssetPath = "Assets/_Game/Art/Meshes/PileDiscIrregular.asset";
        public const string GroundName = "GeneratedGround";
        public static readonly string[] ZonePrefixes = { "PileZone_", "BaseZone_" };

        private const string MapScenePath = "Assets/_Game/Scenes/Map.unity";
        private const string GeometryRootName = "GeneratedMapGeometry";

        [MenuItem("Cluck Wars/Map/Apply Ground + Zone Fixes (in place)", priority = 23)]
        public static void ApplyToMapScene()
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
                Debug.LogError($"[ZoneMarkerBaker] {MapScenePath} has no {GeometryRootName} root - bake the map first.");
                if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
                return;
            }

            int touched = Apply(root.transform);
            EditorSceneManager.MarkSceneDirty(map);
            if (!EditorSceneManager.SaveScene(map))
                Debug.LogError($"[ZoneMarkerBaker] Failed to save {MapScenePath}.");
            if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
            Debug.Log($"[ZoneMarkerBaker] Updated {touched} renderer(s) in {MapScenePath}.");
        }

        /// <summary>Applies the fixes to the direct children of <paramref name="geometryRoot"/>; returns how many were touched.</summary>
        public static int Apply(Transform geometryRoot)
        {
            var mesh = EnsureMeshAsset();
            var pileDisc = EnsurePileDiscMeshAsset();
            int touched = 0;
            foreach (Transform child in geometryRoot)
            {
                bool isGround = child.name == GroundName;
                bool isZone = ZonePrefixes.Any(p => child.name.StartsWith(p));
                if (!isGround && !isZone) continue;

                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    Debug.LogError($"[ZoneMarkerBaker] {child.name} has no MeshRenderer - skipped.");
                    continue;
                }
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                if (isZone)
                {
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter == null)
                    {
                        Debug.LogError($"[ZoneMarkerBaker] {child.name} has no MeshFilter - kept its old mesh.");
                        continue;
                    }
                    bool isPile = child.name.StartsWith("PileZone_");
                    if (isPile && pileDisc != null)
                    {
                        // Irregular unit-footprint slab (y 0..0.02) replaces the 2-unit-tall cylinder: same footprint
                        // scale in X,Z, Y scale 1 and no lift keep the same 0..0.02 m slab the cylinder produced.
                        filter.sharedMesh = pileDisc;
                        var ls = child.localScale; var lp = child.localPosition;
                        child.localScale = new Vector3(ls.x, 1f, ls.z);
                        child.localPosition = new Vector3(lp.x, 0f, lp.z);
                    }
                    else filter.sharedMesh = mesh;

                    if (IsOuterPileZone(child.name))
                        child.localRotation = Quaternion.Euler(0f, PileFootprintShape.RadialLongAxisYaw(child.localPosition), 0f);
                }
                touched++;
            }
            return touched;
        }

        /// <summary>Personal / contested pile discs: the ones whose oval is yawed toward the centre (D9). The centre disc stays at yaw 0.</summary>
        public static bool IsOuterPileZone(string name) =>
            name.StartsWith("PileZone_Personal") || name.StartsWith("PileZone_Contested");

        /// <summary>Bakes <see cref="PileDiscFbxPath"/>'s mesh with its node rotation applied into <see cref="PileDiscMeshAssetPath"/> (refreshed in place so the GUID is stable). Null (and an error) if the FBX is missing.</summary>
        public static Mesh EnsurePileDiscMeshAsset()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PileDiscFbxPath);
            var src = fbx != null ? fbx.GetComponentInChildren<MeshFilter>() : null;
            if (src == null || src.sharedMesh == null)
            {
                Debug.LogError($"[ZoneMarkerBaker] {PileDiscFbxPath} has no mesh - pile discs keep the 64-segment disc.");
                return null;
            }
            var rot = src.transform.localRotation;
            var fresh = new Mesh { name = "PileDiscIrregular" };
            fresh.SetVertices(src.sharedMesh.vertices.Select(v => rot * v).ToList());
            fresh.SetNormals(src.sharedMesh.normals.Select(n => rot * n).ToList());
            fresh.SetUVs(0, src.sharedMesh.uv.ToList());
            fresh.SetTriangles(src.sharedMesh.triangles, 0);
            fresh.RecalculateBounds();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(PileDiscMeshAssetPath);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PileDiscMeshAssetPath)!);
                AssetDatabase.CreateAsset(fresh, PileDiscMeshAssetPath);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        /// <summary>Creates (or refreshes in place, so its GUID is stable) the 64-segment disc mesh asset.</summary>
        public static Mesh EnsureMeshAsset()
        {
            var fresh = ZoneDiscMesh.Build(ZoneDiscMesh.Segments);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MeshAssetPath)!);
                AssetDatabase.CreateAsset(fresh, MeshAssetPath);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }
    }
}
