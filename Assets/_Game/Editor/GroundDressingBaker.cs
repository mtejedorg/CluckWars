using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Phase 8 C S3b (D5 surround): bakes the worn dirt paths (base -> its piles), the in-arena flower / mud ground
    /// decals and the seven prop vignettes V1-V7 outside the fence into <c>Map.unity</c>. Re-runnable: it owns
    /// exactly the <see cref="GroundRootName"/> root and <c>MapSurroundings/</c><see cref="VignettesName"/>, and
    /// rebuilds both from the tables below (nothing else in the scene is touched).
    /// </summary>
    /// <remarks>
    /// <para><b>Paths.</b> One mesh (<see cref="PathMeshPath"/>) holds corner 0's three routes, authored in world space
    /// from the baked NavMesh corners; the four <c>DirtPaths_Base{k}</c> objects are that mesh yawed -90k degrees, so
    /// the set is exactly 4-fold. Routes: <c>Doorstep</c> base -> personal pile; <c>Rim</c> base -> the contested pile
    /// reached through the outer gap past the even arm's rim tip (between the arm and the side fence);
    /// <c>Hub</c> the contested pile on the other side, whose odd arm is joined to the fence, so the only way is round
    /// the arm's hub tip. It forks off the doorstep path just short of the personal pile (the NavMesh route grazes that
    /// pile). No path to the centre pile: it belongs to everyone (it has its own glow), and four more spokes into the hub
    /// would read as a road network. Strips are 1 cm above the grass (the hub fork 2 mm higher so the fork overlap never
    /// z-fights), ~1 m wide with rounded, tapering ends, and alpha-clipped against the ragged edge mask plus a little
    /// wear noise along the edges only (Phase 8 D6: solid centre band, dirt a step darker than the grass): a hint, not a road.</para>
    /// <para><b>Decals.</b> Flat quads on one atlas material (<see cref="DecalAtlasPath"/>: flower patch left, mud
    /// puddle right; the flower alpha is rescaled so its 0.35 cutoff lands on the shared 0.4). In the arena they sit
    /// off the routes, beside walls/fences, never on a pile disc or base pad. No URP Decal Projector (Mobile).</para>
    /// <para><b>Vignettes.</b> Instances of the existing <c>Prop_*</c> kit (never the coop or nest basket, which mark
    /// bases), colliders / NavMeshObstacles stripped, shadows off. Anchors / offsets / yaws follow the Stage 2 part B
    /// table; group frame: local +Z points away from the arena, +X runs along the hedge. Fence-hedge gap items stay
    /// &lt; 0.8 m tall (south-gap rule, applied 4-fold).</para>
    /// <para>The two textures are derived here from the Stage 2 sources in <c>Assets/Generated/Env/Textures</c>
    /// (deterministic), so a re-run reproduces them byte for byte.</para>
    /// </remarks>
    public static class GroundDressingBaker
    {
        public const string MapScenePath = "Assets/_Game/Scenes/Map.unity";
        public const string GroundRootName = "MapGroundDressing";
        public const string PathsName = "Paths";
        public const string DecalsName = "Decals";
        public const string VignettesName = "Vignettes";
        public const string SurroundingsName = "MapSurroundings";

        public const string TextureFolder = "Assets/_Game/Art/Textures";
        public const string PathTexturePath = TextureFolder + "/DirtPathStrip.png";
        public const string DecalAtlasPath = TextureFolder + "/GroundDecalsAtlas.png";
        public const string PathMaterialPath = "Assets/_Game/Art/Materials/Ground_DirtPath.mat";
        public const string DecalMaterialPath = "Assets/_Game/Art/Materials/Ground_Decals.mat";
        public const string PathMeshPath = "Assets/_Game/Art/Meshes/DirtPathsCorner.asset";
        public const string FlowerMeshPath = "Assets/_Game/Art/Meshes/GroundDecalFlower.asset";
        public const string MudMeshPath = "Assets/_Game/Art/Meshes/GroundDecalMud.asset";

        private const string SourceTextures = "Assets/Generated/Env/Textures/";
        private const string MatteTemplate = "Assets/_Game/Art/Materials/Hedge_Side.mat";
        private const string PropFolder = "Assets/_Game/Prefabs/Props/Prop_";

        /// <summary>Strip height above the arena grass (top of GeneratedGround = 0).</summary>
        public const float PathLift = 0.01f;
        /// <summary>Extra lift of the hub fork over the doorstep strip it branches from.</summary>
        public const float HubForkLift = 0.002f;
        public const float ArenaDecalLift = 0.011f;
        /// <summary>The surround grass top is at -0.01, so outside decals sit at 0 (1 cm up).</summary>
        public const float OutsideDecalLift = 0f;
        /// <summary>Metres of path per U repeat of <see cref="PathTexturePath"/> (4 m x 1 m at 256 px/m).</summary>
        public const float PathMetresPerU = 4f;
        public const float PathCutoff = 0.5f;
        public const float DecalCutoff = 0.4f;
        public const float FlowerSourceCutoff = 0.35f;
        private const float SampleStep = 0.2f;
        private const float CapLength = 0.6f;
        /// <summary>Clearance kept between a path end and the pile oval it leads to.</summary>
        public const float PileEndGap = 0.15f;

        // ---- layout tables (corner 0 = the +X/+Z base; the other three are these turned -90k degrees) -------------

        public struct Route
        {
            public string Name; public float Width; public float Lift;
            /// <summary>Interior control points (world XZ, corner 0); the start / end are added from the base and pile.</summary>
            public Vector2[] Via;
        }

        /// <summary>Doorstep (base -> personal), Rim (base -> contested through the rim gap), Hub (fork -> contested round the hub tip).</summary>
        public static readonly Route[] Routes =
        {
            new Route { Name = "Doorstep", Width = 1.0f, Lift = 0f, Via = new Vector2[0] },
            new Route { Name = "Rim", Width = 1.0f, Lift = 0f,
                Via = new[] { new Vector2(23.85f, 15.5f), new Vector2(23.9f, 11.5f), new Vector2(23.85f, 9.3f), new Vector2(23.2f, 6.2f) } },
            new Route { Name = "Hub", Width = 0.9f, Lift = HubForkLift,
                Via = new[] { new Vector2(16.0f, 18.65f), new Vector2(13.6f, 17.7f), new Vector2(9.0f, 13.6f), new Vector2(6.3f, 11.35f),
                              new Vector2(4.55f, 10.75f), new Vector2(3.55f, 12.0f), new Vector2(2.6f, 14.0f) } },
        };
        /// <summary>Where each route starts, measured from the base centre toward its first target (outside the 2.65 m ring).</summary>
        public const float PathStartFromBase = 2.95f;
        public const float HubForkFromBase = 4.1f;

        public enum DecalKind { Flower, Mud }

        public struct Decal
        {
            public DecalKind Kind; public Vector2 Pos; public float Size; public float Yaw;
            public Decal(DecalKind k, float x, float z, float size, float yaw) { Kind = k; Pos = new Vector2(x, z); Size = size; Yaw = yaw; }
        }

        /// <summary>In-arena decals for corner 0's quarter (4-fold): a flower patch between arm 1's dressing and the
        /// scarecrow, one by arm 0's base side, and a mud patch against arm 1's base-side face between two hugging props.</summary>
        public static readonly Decal[] ArenaDecals =
        {
            new Decal(DecalKind.Flower, 12.6f, 21.4f, 1.6f, 20f),
            new Decal(DecalKind.Flower, 19.6f, 10.6f, 1.5f, 75f),
            new Decal(DecalKind.Mud, 8.14f, 16.75f, 1.3f, 40f),
        };

        public struct Item
        {
            /// <summary><c>Prop_{Prefab}.prefab</c>, or <c>decal:flower</c> / <c>decal:mud</c>.</summary>
            public string Prefab; public Vector3 Offset; public float Yaw; public float Scale;
            public Item(string prefab, float x, float y, float z, float yaw, float scale) { Prefab = prefab; Offset = new Vector3(x, y, z); Yaw = yaw; Scale = scale; }
        }

        public struct Group { public string Name; public Vector2 Anchor; public float Yaw; public Item[] Items; }

        private const string Flower = "decal:flower", Mud = "decal:mud";

        // V1 corner feed nook (fence-hedge corner square, all <= 0.8 m: the feed sack at x0.95 is 0.76 m). Phase 8 D12: the nook
        // is an L (sack in the fence-side corner, one bale along each hedge) so every renderer stays >= 0.2 m clear of the
        // HedgeStrip bounds (Phase8ChunkD12Tests); the bales are x1.5 (was x1.6) to fit the 1.9 m gap. Group frame is yaw 45 - 90k:
        // local x runs along the corner's anti-diagonal, local z out toward the hedge corner.
        private static readonly Item[] V1 =
        {
            new Item("feed_sack", 0f, 0f, -0.495f, 20f, 0.95f),
            new Item("hay_bale", -0.566f, 0f, 0f, 10f, 1.5f),
            new Item("hay_bale", 0.566f, 0f, 0f, 75f, 1.5f),
            new Item(Flower, 0f, 0f, 0.1f, 0f, 1.6f),
        };
        // V2 wheelbarrow and hay (outside the N / S hedge).
        private static readonly Item[] V2 =
        {
            new Item("wheelbarrow", 0f, 0f, 0f, 200f, 1.3f),
            new Item("hay_bale", 1.2f, 0f, 0.1f, 5f, 1.8f),
            new Item("hay_bale", 1.85f, 0f, 0.25f, 95f, 1.8f),
            new Item("hay_bale", 1.5f, 0.56f, 0.2f, 50f, 1.8f),
            new Item("feed_sack", -1.1f, 0f, 0.45f, -30f, 1.0f),
            new Item(Flower, 0.4f, 0f, -0.6f, 0f, 2.0f),
        };
        // V3 milk churns (E / W fence-hedge gap). The match camera turns with the home corner, so every side is the near
        // side for two seats: the table's x0.9 churns (0.90 m) are scaled to x0.78 (0.78 m) to keep the < 0.8 m gap rule.
        private static readonly Item[] V3 =
        {
            new Item("milk_churn", 0f, 0f, 0f, 0f, 0.78f),
            new Item("milk_churn", 0.55f, 0f, 0.15f, 40f, 0.78f),
            new Item("milk_churn", 0.2f, 0f, -0.4f, 110f, 0.78f),
            new Item("barrel", -0.95f, 0f, 0f, 15f, 1.1f),   // table: crate x0.8 - crates are the cover language (C10), so a barrel
            new Item(Mud, -0.3f, 0f, 0.05f, 0f, 1.6f),
        };
        // V4 trough and puddle (outside the E / W hedge).
        private static readonly Item[] V4 =
        {
            new Item("trough", 0f, 0f, 0f, 90f, 1.2f),
            new Item(Mud, 0f, 0f, -0.9f, 0f, 2.0f),
            new Item("barrel", 0.9f, 0f, 0.9f, 30f, 1.0f),
        };
        // V6 barn yard, in the barn's own frame (+Z out of the barn door), anchored 6.5 m in front of it.
        private static readonly Item[] V6 =
        {
            new Item("hay_bale", -0.75f, 0f, 0.6f, 3f, 2f),
            new Item("hay_bale", 0f, 0f, 0.6f, -4f, 2f),
            new Item("hay_bale", 0.75f, 0f, 0.6f, 6f, 2f),
            new Item("hay_bale", -0.38f, 0.62f, 0.6f, 88f, 2f),
            new Item("wheelbarrow", -1.9f, 0f, 0.3f, 160f, 1.3f),
            new Item("feed_sack", 1.6f, 0f, -0.3f, 15f, 1f),
            new Item("feed_sack", 1.95f, 0f, 0.25f, -35f, 1f),
            new Item(Mud, 0.2f, 0f, -0.7f, 30f, 2.6f),
        };
        public const float BarnYardDistance = 6.5f;
        // V7 silo apron (frame +Z pointing away from the arena through the silo; the table's offsets pushed out ~20 % to clear the silo's ladder side).
        private static readonly Item[] V7 =
        {
            new Item("barrel", 1.1f, 0f, 0.75f, 0f, 1.2f),
            new Item("barrel", 1.5f, 0f, 0.15f, 40f, 1.2f),
            new Item("feed_sack", -1.0f, 0f, 1.05f, 0f, 1.0f),
        };
        /// <summary>V5 flower verge along the outer foot of the N / E / W hedges (world XZ, size, yaw).</summary>
        public static readonly Decal[] V5 =
        {
            new Decal(DecalKind.Flower, -17.5f, 30.92f, 2.2f, 35f),
            new Decal(DecalKind.Flower, 3.5f, 30.67f, 1.6f, 140f),
            new Decal(DecalKind.Flower, 15.0f, 31.01f, 2.4f, 260f),
            new Decal(DecalKind.Flower, 30.76f, 21.0f, 1.8f, 80f),
            new Decal(DecalKind.Flower, 30.84f, 12.5f, 2.0f, 200f),
            new Decal(DecalKind.Flower, 30.63f, -17.0f, 1.5f, 310f),
            new Decal(DecalKind.Flower, -30.97f, 17.0f, 2.3f, 15f),
            new Decal(DecalKind.Flower, -30.71f, -6.5f, 1.7f, 170f),
            new Decal(DecalKind.Flower, -30.88f, -19.5f, 2.1f, 290f),
        };

        public static readonly Vector2 V1Anchor = new Vector2(26.6f, 26.6f);
        /// <summary>V2 north anchor; the south one is its half-turn (-x, -z), i.e. (7, -31.6) rather than the table's (8, -31.6), so V2 is exactly 2-fold like V3 / V4.</summary>
        public static readonly Vector2 V2North = new Vector2(-7f, 31.6f), V2South = new Vector2(7f, -31.6f);
        public static readonly Vector2 V3East = new Vector2(26.7f, 4f);
        public static readonly Vector2 V4East = new Vector2(32f, -8f);

        /// <summary>World XZ of <paramref name="p"/> in corner <paramref name="k"/>'s quarter (yaw -90k).</summary>
        public static Vector2 Turn(Vector2 p, int k)
        {
            var v = Quaternion.Euler(0f, -90f * k, 0f) * new Vector3(p.x, 0f, p.y);
            return new Vector2(v.x, v.z);
        }

        // ---- menu -------------------------------------------------------------------------------------------------

        [MenuItem("Cluck Wars/Map/Bake Paths + Decals + Vignettes (in place)", priority = 24)]
        public static void BakeIntoMapScene()
        {
            Scene map = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == MapScenePath) map = SceneManager.GetSceneAt(i);
            bool openedHere = !map.IsValid() || !map.isLoaded;
            if (openedHere) map = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
            try
            {
                string report = Bake(map);
                EditorSceneManager.MarkSceneDirty(map);
                if (!EditorSceneManager.SaveScene(map)) Debug.LogError($"[GroundDressingBaker] Failed to save {MapScenePath}.");
                Debug.Log("[GroundDressingBaker] " + report);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
            }
        }

        /// <summary>Builds every asset and rebuilds the owned scene objects in <paramref name="map"/>; returns a summary.</summary>
        public static string Bake(Scene map)
        {
            var roots = map.GetRootGameObjects();
            var geometry = roots.FirstOrDefault(g => g.name == "GeneratedMapGeometry");
            var surroundings = roots.FirstOrDefault(g => g.name == SurroundingsName);
            if (geometry == null || surroundings == null)
                throw new System.InvalidOperationException($"{MapScenePath} needs GeneratedMapGeometry and {SurroundingsName}.");

            EnsureTextures();
            var pathMat = EnsureMaterial(PathMaterialPath, PathTexturePath, PathCutoff);
            var decalMat = EnsureMaterial(DecalMaterialPath, DecalAtlasPath, DecalCutoff);
            var pathMesh = SaveMesh(BuildPathMesh(geometry.transform), PathMeshPath);
            var flowerMesh = SaveMesh(BuildQuad("GroundDecalFlower", 0f), FlowerMeshPath);
            var mudMesh = SaveMesh(BuildQuad("GroundDecalMud", 0.5f), MudMeshPath);

            // ---- arena ground dressing (own root) ----
            var old = roots.FirstOrDefault(g => g.name == GroundRootName);
            if (old != null) Object.DestroyImmediate(old);
            var ground = new GameObject(GroundRootName);
            SceneManager.MoveGameObjectToScene(ground, map);
            var paths = Child(ground.transform, PathsName);
            for (int k = 0; k < 4; k++)
            {
                var go = Child(paths, $"DirtPaths_Base{k}");
                go.localPosition = new Vector3(0f, PathLift, 0f);
                go.localRotation = Quaternion.Euler(0f, -90f * k, 0f);
                Flat(go.gameObject, pathMesh, pathMat);
            }
            var decals = Child(ground.transform, DecalsName);
            int nd = 0;
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < ArenaDecals.Length; i++)
                {
                    var d = ArenaDecals[i];
                    var p = Turn(d.Pos, k);
                    var go = Child(decals, $"{d.Kind}_{k}_{i}");
                    go.localPosition = new Vector3(p.x, ArenaDecalLift, p.y);
                    go.localRotation = Quaternion.Euler(0f, d.Yaw - 90f * k, 0f);
                    go.localScale = new Vector3(d.Size, 1f, d.Size);
                    Flat(go.gameObject, d.Kind == DecalKind.Flower ? flowerMesh : mudMesh, decalMat);
                    nd++;
                }

            // ---- vignettes (MapSurroundings/Vignettes) ----
            var oldV = surroundings.transform.Find(VignettesName);
            if (oldV != null) Object.DestroyImmediate(oldV.gameObject);
            var vig = Child(surroundings.transform, VignettesName);
            var groups = new List<Group>();
            for (int k = 0; k < 4; k++)
                groups.Add(new Group { Name = $"V1_CornerFeed_{k}", Anchor = Turn(V1Anchor, k), Yaw = 45f - 90f * k, Items = V1 });
            groups.Add(new Group { Name = "V2_Wheelbarrow_N", Anchor = V2North, Yaw = 0f, Items = V2 });
            groups.Add(new Group { Name = "V2_Wheelbarrow_S", Anchor = V2South, Yaw = 180f, Items = V2 });
            groups.Add(new Group { Name = "V3_Churns_E", Anchor = V3East, Yaw = 90f, Items = V3 });
            groups.Add(new Group { Name = "V3_Churns_W", Anchor = -V3East, Yaw = 270f, Items = V3 });
            groups.Add(new Group { Name = "V4_Trough_E", Anchor = V4East, Yaw = 90f, Items = V4 });
            groups.Add(new Group { Name = "V4_Trough_W", Anchor = -V4East, Yaw = 270f, Items = V4 });
            var barn = surroundings.transform.Find("Barn");
            if (barn != null)
            {
                var a = barn.position + barn.rotation * new Vector3(0f, 0f, BarnYardDistance);
                groups.Add(new Group { Name = "V6_BarnYard", Anchor = new Vector2(a.x, a.z), Yaw = barn.eulerAngles.y, Items = V6 });
            }
            else Debug.LogError("[GroundDressingBaker] MapSurroundings/Barn missing - V6 skipped.");
            foreach (var siloName in new[] { "Silo_A", "Silo_B" })
            {
                var silo = surroundings.transform.Find(siloName);
                if (silo == null) { Debug.LogError($"[GroundDressingBaker] MapSurroundings/{siloName} missing - V7 skipped there."); continue; }
                var s = new Vector2(silo.position.x, silo.position.z);
                groups.Add(new Group { Name = $"V7_SiloApron_{siloName.Substring(5)}", Anchor = s, Yaw = Mathf.Atan2(s.x, s.y) * Mathf.Rad2Deg, Items = V7 });
            }

            int np = 0, nv = 0;
            foreach (var g in groups)
            {
                var gt = Child(vig, g.Name);
                gt.localPosition = new Vector3(g.Anchor.x, 0f, g.Anchor.y);
                gt.localRotation = Quaternion.Euler(0f, g.Yaw, 0f);
                for (int i = 0; i < g.Items.Length; i++)
                {
                    var it = g.Items[i];
                    if (it.Prefab.StartsWith("decal:"))
                    {
                        var dt = Child(gt, $"{(it.Prefab == Flower ? "Flower" : "Mud")}_{i}");
                        dt.localPosition = new Vector3(it.Offset.x, OutsideDecalLift, it.Offset.z);
                        dt.localRotation = Quaternion.Euler(0f, it.Yaw, 0f);
                        dt.localScale = new Vector3(it.Scale, 1f, it.Scale);
                        Flat(dt.gameObject, it.Prefab == Flower ? flowerMesh : mudMesh, decalMat);
                        nv++;
                        continue;
                    }
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + it.Prefab + ".prefab");
                    if (prefab == null) { Debug.LogError($"[GroundDressingBaker] Prop_{it.Prefab}.prefab missing."); continue; }
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map);
                    inst.name = $"{it.Prefab}_{i}";
                    inst.transform.SetParent(gt, false);
                    inst.transform.localPosition = it.Offset;
                    inst.transform.localRotation = Quaternion.Euler(0f, it.Yaw, 0f);
                    inst.transform.localScale = Vector3.one * it.Scale;
                    StripBlockers(inst);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = true;
                    }
                    np++;
                }
            }
            var v5 = Child(vig, "V5_FlowerVerge");
            for (int i = 0; i < V5.Length; i++)
            {
                var d = V5[i];
                var dt = Child(v5, $"Flower_{i}");
                dt.localPosition = new Vector3(d.Pos.x, OutsideDecalLift, d.Pos.y);
                dt.localRotation = Quaternion.Euler(0f, d.Yaw, 0f);
                dt.localScale = new Vector3(d.Size, 1f, d.Size);
                Flat(dt.gameObject, flowerMesh, decalMat);
                nv++;
            }
            return $"paths 4 x {pathMesh.vertexCount} verts, arena decals {nd}, vignette groups {groups.Count + 1}, props {np}, vignette decals {nv}.";
        }

        // ---- scene helpers ---------------------------------------------------------------------------------------

        private const StaticEditorFlags Static = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            GameObjectUtility.SetStaticEditorFlags(go, Static);
            return go.transform;
        }

        private static void Flat(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, Static);
        }

        /// <summary>Vignettes are scenery: no collider, no NavMesh carving (the kit used here has none today; this keeps it so).</summary>
        private static void StripBlockers(GameObject inst)
        {
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var o in inst.GetComponentsInChildren<NavMeshObstacle>(true)) Object.DestroyImmediate(o);
        }

        // ---- meshes ----------------------------------------------------------------------------------------------

        /// <summary>Corner 0's three routes as one mesh in world space (y = 0; the object carries the lift).</summary>
        public static Mesh BuildPathMesh(Transform geometry)
        {
            Transform Z(string n)
            {
                var t = geometry.Find(n);
                if (t == null) throw new System.InvalidOperationException($"GeneratedMapGeometry/{n} missing.");
                return t;
            }
            var baseT = Z("BaseZone_0");
            var b = new Vector2(baseT.position.x, baseT.position.z);
            var personal = Z("PileZone_Personal0");
            var rimPile = Z("PileZone_Contested3");   // reached through the gap past arm 0's rim tip
            var hubPile = Z("PileZone_Contested0");   // round arm 1's hub tip (arm 1 is joined to the fence)
            var p0 = new Vector2(personal.position.x, personal.position.z);
            var dDoor = (p0 - b).normalized;

            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int r = 0; r < Routes.Length; r++)
            {
                var route = Routes[r];
                var pts = new List<Vector2>();
                switch (route.Name)
                {
                    case "Doorstep":
                        pts.Add(b + dDoor * PathStartFromBase);
                        pts.Add(PileEnd(personal, b));
                        break;
                    case "Rim":
                        pts.Add(b + (route.Via[0] - b).normalized * PathStartFromBase);
                        pts.AddRange(route.Via);
                        pts.Add(PileEnd(rimPile, route.Via[route.Via.Length - 1]));
                        break;
                    default:
                        pts.Add(b + dDoor * HubForkFromBase);
                        pts.AddRange(route.Via);
                        pts.Add(PileEnd(hubPile, route.Via[route.Via.Length - 1]));
                        break;
                }
                AppendRibbon(CatmullRom(pts, SampleStep), route.Width, route.Lift, r * 1.37f, r, verts, uvs, tris);
            }
            var mesh = new Mesh { name = "DirtPathsCorner" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, verts.Count).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The point <see cref="PileEndGap"/> outside the pile's oval, on the line from the pile centre toward <paramref name="from"/>.</summary>
        public static Vector2 PileEnd(Transform pile, Vector2 from)
        {
            var c = new Vector2(pile.position.x, pile.position.z);
            var d = (from - c).normalized;
            return c + d * (OvalRadius(pile, d) + PileEndGap);
        }

        /// <summary>Radius of the pile disc's footprint ellipse (scale X x Z, yawed) toward world direction <paramref name="d"/>.</summary>
        public static float OvalRadius(Transform pile, Vector2 d)
        {
            var l = Quaternion.Inverse(pile.rotation) * new Vector3(d.x, 0f, d.y);
            float a = pile.lossyScale.x * 0.5f, c = pile.lossyScale.z * 0.5f;
            return 1f / Mathf.Sqrt(l.x * l.x / (a * a) + l.z * l.z / (c * c));
        }

        public static List<Vector2> CatmullRom(List<Vector2> pts, float step)
        {
            var o = new List<Vector2>();
            if (pts.Count == 2)
            {
                int n = Mathf.Max(2, (int)(Vector2.Distance(pts[0], pts[1]) / step));
                for (int i = 0; i <= n; i++) o.Add(Vector2.Lerp(pts[0], pts[1], i / (float)n));
                return o;
            }
            var p = new List<Vector2> { 2f * pts[0] - pts[1] };
            p.AddRange(pts);
            p.Add(2f * pts[pts.Count - 1] - pts[pts.Count - 2]);
            for (int i = 1; i < p.Count - 2; i++)
            {
                Vector2 p0 = p[i - 1], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2];
                int n = Mathf.Max(2, (int)(Vector2.Distance(p1, p2) / step));
                for (int j = 0; j < n; j++)
                {
                    float t = j / (float)n, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(pts[pts.Count - 1]);
            return o;
        }

        private static void AppendRibbon(List<Vector2> c, float width, float lift, float u0, int seed,
                                         List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var s = new float[c.Count];
            for (int i = 1; i < c.Count; i++) s[i] = s[i - 1] + Vector2.Distance(c[i - 1], c[i]);
            float len = s[c.Count - 1];
            int start = verts.Count;
            for (int i = 0; i < c.Count; i++)
            {
                var t = (c[Mathf.Min(i + 1, c.Count - 1)] - c[Mathf.Max(i - 1, 0)]).normalized;
                var n = new Vector2(-t.y, t.x);
                float e = Mathf.Min(s[i], len - s[i]);
                float cap = e >= CapLength ? 1f : Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((CapLength - e) / CapLength, 2f)));
                float wobble = 1f + 0.07f * Mathf.Sin(s[i] * 1.1f + seed * 2.3f);
                float hw = 0.5f * width * wobble * Mathf.Max(cap, 0.05f);
                var l = c[i] + n * hw; var r = c[i] - n * hw;
                verts.Add(new Vector3(l.x, lift, l.y)); uvs.Add(new Vector2((s[i] + u0) / PathMetresPerU, 0f));
                verts.Add(new Vector3(r.x, lift, r.y)); uvs.Add(new Vector2((s[i] + u0) / PathMetresPerU, 1f));
            }
            for (int i = 0; i + 1 < c.Count; i++)
            {
                int a = start + 2 * i;
                AddUp(verts, tris, a, a + 1, a + 2);
                AddUp(verts, tris, a + 1, a + 3, a + 2);
            }
        }

        /// <summary>Adds the triangle with the winding that faces +Y (Unity: clockwise seen from above).</summary>
        private static void AddUp(List<Vector3> v, List<int> tris, int a, int b, int c)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) (b, c) = (c, b);
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        /// <summary>Unit quad on XZ, normal up, UVs on one half of the decal atlas (u0 = 0 flower, 0.5 mud).</summary>
        public static Mesh BuildQuad(string name, float u0)
        {
            var v = new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            var tris = new List<int>();
            AddUp(v, tris, 0, 1, 2);
            AddUp(v, tris, 1, 3, 2);
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetUVs(0, new List<Vector2> { new Vector2(u0, 0f), new Vector2(u0 + 0.5f, 0f), new Vector2(u0, 1f), new Vector2(u0 + 0.5f, 1f) });
            m.SetNormals(Enumerable.Repeat(Vector3.up, 4).ToList());
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh SaveMesh(Mesh fresh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        // ---- materials -------------------------------------------------------------------------------------------

        /// <summary>Matte URP Lit (copied from the hedge material: smoothness 0, spec + reflections off, instancing), alpha-clipped.</summary>
        private static Material EnsureMaterial(string path, string texPath, float cutoff)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = mat == null;
            if (fresh) mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(MatteTemplate)) { name = Path.GetFileNameWithoutExtension(path) };
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_AlphaToMask", 1f);
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_ReceiveShadows", 1f);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.enableInstancing = true;
            if (fresh) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ---- textures (derived, deterministic) -------------------------------------------------------------------

        public static void EnsureTextures()
        {
            Directory.CreateDirectory(TextureFolder);
            WriteIfChanged(PathTexturePath, BuildPathStrip());
            WriteIfChanged(DecalAtlasPath, BuildDecalAtlas());
            ConfigureImporter(PathTexturePath, TextureWrapMode.Repeat, TextureWrapMode.Clamp, PathCutoff);
            ConfigureImporter(DecalAtlasPath, TextureWrapMode.Clamp, TextureWrapMode.Clamp, DecalCutoff);
        }

        private static void WriteIfChanged(string path, byte[] png)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(png)) return;
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureImporter(string path, TextureWrapMode u, TextureWrapMode v, float alphaRef)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool dirty = ti.textureType != TextureImporterType.Default || !ti.alphaIsTransparency || !ti.mipmapEnabled
                         || !ti.mipMapsPreserveCoverage || !Mathf.Approximately(ti.alphaTestReferenceValue, alphaRef)
                         || ti.wrapModeU != u || ti.wrapModeV != v || !ti.sRGBTexture || ti.isReadable;
            if (!dirty) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;   // alpha-clipped detail would otherwise vanish at the distant mips
            ti.alphaTestReferenceValue = alphaRef;
            ti.wrapModeU = u; ti.wrapModeV = v;
            ti.isReadable = false;
            ti.SaveAndReimport();
        }

        private static Texture2D LoadRaw(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            t.LoadImage(File.ReadAllBytes(SourceTextures + file));
            return t;
        }

        /// <summary>Phase 8 D6: the dirt tile RGB is scaled by this so the path renders just below the E3 grass (luma ~158)
        /// instead of ~7 % above it (audit L06).</summary>
        public const float PathAlbedoScale = 0.86f;
        /// <summary>Phase 8 D6: wear holes only where |v - 0.5| &gt;= this (the strip's edge mask spans v ~0.1-0.9, so the inner
        /// ~60 % of the visible path is solid); full strength from <see cref="PathHoleEdgeFull"/> out.</summary>
        public const float PathHoleSolidHalfBand = 0.25f;
        public const float PathHoleEdgeFull = 0.33f;

        /// <summary>0 in the solid centre band, 1 along the edges (holes as bites in the worn edge, not leopard spots).</summary>
        public static float PathHoleBand(float v) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PathHoleSolidHalfBand, PathHoleEdgeFull, Mathf.Abs(v - 0.5f)));

        /// <summary>4 m (U, wraps) x 1 m (V) at 256 px/m: dirt tile RGB x <see cref="PathAlbedoScale"/>, alpha = ragged edge
        /// mask x wear holes kept to the edge bands (<see cref="PathHoleBand"/>).</summary>
        private static byte[] BuildPathStrip()
        {
            const int W = 1024, H = 256;
            var tile = LoadRaw("dirt_path_tile.png");        // 1024 px = 2 m
            var mask = LoadRaw("dirt_path_edge_mask.png");   // 512 x 128, one ragged repeat along U, V across
            bool maskInAlpha = mask.format == TextureFormat.Alpha8;
            var tp = tile.GetPixels32(); int tw = tile.width, th = tile.height;
            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // dirt: 2x2 box down-sample (512 px/m -> 256 px/m)
                    int sx = (2 * x) % tw, sy = (2 * y) % th;
                    float r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            var c = tp[((sy + dy) % th) * tw + (sx + dx) % tw];
                            r += c.r; g += c.g; b += c.b;
                        }
                    var mc = mask.GetPixelBilinear((x + 0.5f) / W, (y + 0.5f) / H);
                    float m = maskInAlpha ? mc.a : mc.r;
                    float n = 0.6f * ValueNoise(x / 64f, y / 64f, W / 64, 11) + 0.4f * ValueNoise(x / 32f, y / 32f, W / 32, 23);
                    float holes = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 0.76f, n)) * PathHoleBand((y + 0.5f) / H);
                    float a = m * (1f - 0.85f * holes);
                    float k = PathAlbedoScale / 4f;
                    outPx[y * W + x] = new Color32((byte)(r * k + 0.5f), (byte)(g * k + 0.5f), (byte)(b * k + 0.5f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            var o = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            o.SetPixels32(outPx);
            o.Apply();
            var png = o.EncodeToPNG();
            Object.DestroyImmediate(o); Object.DestroyImmediate(tile); Object.DestroyImmediate(mask);
            return png;
        }

        /// <summary>Phase 8 D5 sources (<c>tools/art/d5_decals/make_d5_decals.py</c> from the Stage 2 decals): ~58 % of the
        /// flowers, warm cream / yellow / a little pink, ~30 % darker; the mud a dark wet blue-grey, out of the pile-disc browns.</summary>
        public const string FlowerSource = "flower_patch_decal_d5.png";
        public const string MudSource = "mud_puddle_decal_d5.png";

        /// <summary>1024 x 512: flower patch (alpha x 0.4/0.35, so the shared 0.4 cutoff keeps its 0.35 edge) | mud puddle.</summary>
        private static byte[] BuildDecalAtlas()
        {
            var flower = LoadRaw(FlowerSource);
            var mud = LoadRaw(MudSource);
            const int S = 512;
            var o = new Texture2D(2 * S, S, TextureFormat.RGBA32, false, false);
            var px = new Color32[2 * S * S];
            var fp = flower.GetPixels32(); var mp = mud.GetPixels32();
            float k = DecalCutoff / FlowerSourceCutoff;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var f = fp[y * flower.width + x];
                    f.a = (byte)Mathf.Min(255, Mathf.RoundToInt(f.a * k));
                    px[y * 2 * S + x] = f;
                    px[y * 2 * S + S + x] = mp[y * mud.width + x];
                }
            o.SetPixels32(px);
            o.Apply();
            var png = o.EncodeToPNG();
            Object.DestroyImmediate(o); Object.DestroyImmediate(flower); Object.DestroyImmediate(mud);
            return png;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Bilinear value noise, periodic in X with <paramref name="periodX"/> cells.</summary>
        private static float ValueNoise(float x, float y, int periodX, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % periodX) + periodX) % periodX, xb = (xa + 1) % periodX;
            float a = Hash(xa, y0, seed), b = Hash(xb, y0, seed), c = Hash(xa, y0 + 1, seed), d = Hash(xb, y0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
    }
}
