using System.Collections.Generic;
using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk C Stage 3 S3a (D5 surround): the generated barn replaces the old <c>big_barn</c> mesh in
    /// <c>Prop_big_barn.prefab</c>, and the four flat <c>ArenaHedge</c> cubes in Map.unity are textured
    /// <c>hedge_strip</c> instances.
    /// S3b: worn dirt paths base -> its piles, flower / mud ground decals in the arena (root <c>MapGroundDressing</c>,
    /// baked by <see cref="GroundDressingBaker"/>) and the prop vignettes V1-V7 outside the fence
    /// (<c>MapSurroundings/Vignettes</c>), measured on the scene's world-space geometry, not on the baker's tables.
    /// </summary>
    public class Phase8ChunkS3Tests
    {
        private const string BarnPrefab = "Assets/_Game/Prefabs/Props/Prop_big_barn.prefab";
        private const string BarnFbx = "Assets/Generated/Env/barn.fbx";
        private const string BarnMaterial = "Assets/_Game/Art/Materials/Prop_big_barn.mat";
        private const string BarnAlbedo = "Assets/Generated/Env/Textures/barn_albedo.png";
        private const string HedgeFbx = "Assets/Generated/Env/hedge_strip.fbx";
        private const string HedgeSide = "Assets/_Game/Art/Materials/Hedge_Side.mat";
        private const string HedgeTop = "Assets/_Game/Art/Materials/Hedge_Top.mat";
        private const string HedgeMacro = "Assets/Generated/Env/Textures/hedge_macro_variation.png";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";

        /// <summary>Blender Z-up -> Unity Y-up node rotation the FBX imports with (270 deg about X).</summary>
        private static readonly Quaternion NodeRotation = Quaternion.Euler(270f, 0f, 0f);

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene() => _originalScenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        private static Mesh FbxMesh(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().sharedMesh;

        private static float AngleTo(Quaternion a, Quaternion b) => Quaternion.Angle(a, b);

        // ---- barn ------------------------------------------------------------------------------------

        [Test]
        public void BarnPrefab_UsesTheGeneratedMesh_WithTheMatteMaterial_AndNoShadow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarnPrefab);
            Assert.IsNotNull(prefab);
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, "root stays at scale 1");
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(1, filters.Length, "one barn mesh (the old big_barn with its silo is gone)");
            var f = filters[0];
            Assert.AreSame(FbxMesh(BarnFbx), f.sharedMesh, "barn mesh");
            Assert.AreSame(prefab.transform, f.transform.parent, "mesh child sits directly under the root");
            Assert.Less(AngleTo(NodeRotation, f.transform.localRotation), 0.1f, "child keeps the 270 deg X convention");
            Assert.AreEqual(Vector3.zero, f.transform.localPosition);

            var r = f.GetComponent<MeshRenderer>();
            Assert.AreEqual(1, r.sharedMaterials.Length);
            Assert.AreEqual(BarnMaterial, AssetDatabase.GetAssetPath(r.sharedMaterial));
            Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, "B3: the barn is outside the fence and casts no shadow");
            Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, "no colliders on the surround barn");

            var m = r.sharedMaterial;
            Assert.AreEqual(BarnAlbedo, AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"));
            Assert.AreEqual(0f, m.GetFloat("_Smoothness"));
        }

        [Test]
        public void BarnAlbedo_Android1024_Standalone2048()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(BarnAlbedo);
            var android = ti.GetPlatformTextureSettings("Android");
            Assert.IsTrue(android.overridden, "Android override on");
            Assert.AreEqual(1024, android.maxTextureSize);
            var pc = ti.GetPlatformTextureSettings("Standalone");
            Assert.AreEqual(2048, pc.overridden ? pc.maxTextureSize : ti.maxTextureSize);
            Assert.IsTrue(ti.mipmapEnabled);
        }

        // ---- hedges ----------------------------------------------------------------------------------

        private static readonly (string Name, Vector3 Pos, float Yaw)[] Hedges =
        {
            ("Hedge_N", new Vector3(0f, 0f, 28.8f), 0f),
            ("Hedge_S", new Vector3(0f, 0f, -28.8f), 180f),
            ("Hedge_E", new Vector3(28.8f, 0f, 0f), 90f),
            ("Hedge_W", new Vector3(-28.8f, 0f, 0f), 270f),
        };

        [Test]
        public void ArenaHedge_IsFourStrips_AtTheirTransforms_WithTheStripMesh_AndNoCubes()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var surround = scene.GetRootGameObjects().First(g => g.name == "MapSurroundings").transform;
            var hedgeRoot = surround.Find("ArenaHedge");
            Assert.IsNotNull(hedgeRoot, "MapSurroundings/ArenaHedge");
            Assert.AreEqual(4, hedgeRoot.childCount, "exactly the four strips");

            var strip = FbxMesh(HedgeFbx);
            foreach (var h in Hedges)
            {
                var t = hedgeRoot.Find(h.Name);
                Assert.IsNotNull(t, h.Name);
                Assert.Less(Vector3.Distance(h.Pos, t.position), 1e-3f, $"{h.Name} position");
                var expected = Quaternion.Euler(0f, h.Yaw, 0f) * NodeRotation;
                Assert.Less(AngleTo(expected, t.rotation), 0.1f, $"{h.Name} rotation (yaw {h.Yaw} on the 270 deg X node)");
                Assert.AreEqual(0.993f, t.localScale.x, 1e-4f, $"{h.Name} X scale");
                Assert.AreEqual(1f, t.localScale.y, 1e-4f);
                Assert.AreEqual(1f, t.localScale.z, 1e-4f);
                Assert.AreSame(strip, t.GetComponent<MeshFilter>().sharedMesh, $"{h.Name} mesh");
                var r = t.GetComponent<MeshRenderer>();
                CollectionAssert.AreEqual(new[] { HedgeSide, HedgeTop }, r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray(),
                    $"{h.Name} materials (submesh 0 = sides, 1 = crown)");
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{h.Name} shadows");
                Assert.IsNull(t.GetComponent<Collider>(), $"{h.Name}: the old cubes had no colliders; none added");
                // The crown is ~1 m above the ground, never sunk into it.
                Assert.AreEqual(0f, r.bounds.min.y, 0.05f, $"{h.Name} sits on the ground");
                Assert.AreEqual(1.1f, r.bounds.max.y, 0.1f, $"{h.Name} crown height");
            }

            var cubes = surround.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.sharedMesh.name == "Cube" && f.name.StartsWith("Hedge"))
                .Select(f => f.name).ToArray();
            CollectionAssert.IsEmpty(cubes, "no stray hedge cubes left");
        }

        [Test]
        public void HedgeMaterials_UseTheLeafTiles_WhiteBase()
        {
            var side = AssetDatabase.LoadAssetAtPath<Material>(HedgeSide);
            var top = AssetDatabase.LoadAssetAtPath<Material>(HedgeTop);
            Assert.AreEqual("Assets/Generated/Env/Textures/hedge_leaf_tile.png", AssetDatabase.GetAssetPath(side.GetTexture("_BaseMap")));
            Assert.AreEqual("Assets/Generated/Env/Textures/hedge_leaf_tile_top.png", AssetDatabase.GetAssetPath(top.GetTexture("_BaseMap")));
            foreach (var m in new[] { side, top })
            {
                Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), m.name);
                Assert.AreEqual(Vector2.one, m.GetTextureScale("_BaseMap"), $"{m.name}: UVs are already in metres");
                // The macro map is only valid as a linear (sRGB off) Detail Albedo x2; if a material uses it, check it.
                if (m.IsKeywordEnabled("_DETAIL_MULX2"))
                    Assert.AreEqual(HedgeMacro, AssetDatabase.GetAssetPath(m.GetTexture("_DetailAlbedoMap")), m.name);
            }
        }

        [Test]
        public void HedgeMacroVariation_IsLinear()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(HedgeMacro);
            Assert.IsFalse(ti.sRGBTexture, "a Detail Albedo x2 map must be sRGB OFF or it darkens the hedge ~2x");
        }
            // ---- S3b: paths, decals, vignettes -----------------------------------------------------------------

        private const string PathMaterial = GroundDressingBaker.PathMaterialPath;
        private const string DecalMaterial = GroundDressingBaker.DecalMaterialPath;
        private const float SymmetryTolerance = 0.02f;
        private const float GapMaxHeight = 0.8f;

        private sealed class Dressing
        {
            public Transform Geometry, Props, Ground, Paths, Decals, Vignettes, Surroundings;
            public float FenceOuter, HedgeInner, HedgeOuter;
            public List<(string Name, Vector2 C, float A, float B, Quaternion Rot)> Piles = new List<(string, Vector2, float, float, Quaternion)>();
            public List<Vector2> Bases = new List<Vector2>();
            public float BaseKeepOut;
            public List<BoxCollider> Blockers = new List<BoxCollider>();
        }

        private static Dressing LoadDressing()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            Transform Root(string n)
            {
                var g = roots.FirstOrDefault(r => r.name == n);
                Assert.IsNotNull(g, $"Map.unity has no {n} root");
                return g.transform;
            }
            var d = new Dressing
            {
                Geometry = Root("GeneratedMapGeometry"), Props = Root("MapProps"), Ground = Root(GroundDressingBaker.GroundRootName),
                Surroundings = Root("MapSurroundings"),
            };
            d.Paths = d.Ground.Find(GroundDressingBaker.PathsName);
            d.Decals = d.Ground.Find(GroundDressingBaker.DecalsName);
            d.Vignettes = d.Surroundings.Find(GroundDressingBaker.VignettesName);
            Assert.IsNotNull(d.Paths, "MapGroundDressing/Paths");
            Assert.IsNotNull(d.Decals, "MapGroundDressing/Decals");
            Assert.IsNotNull(d.Vignettes, "MapSurroundings/Vignettes");

            var fence = d.Props.Cast<Transform>().Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Wall_[NSEW]_\d+$"))
                .SelectMany(t => t.GetComponentsInChildren<Renderer>()).ToList();
            Assert.Greater(fence.Count, 30, "outer fence panels");
            d.FenceOuter = fence.Max(r => Mathf.Max(r.bounds.max.x, r.bounds.max.z));
            var hedgeN = d.Surroundings.Find("ArenaHedge/Hedge_N").GetComponent<Renderer>().bounds;
            d.HedgeInner = hedgeN.min.z; d.HedgeOuter = hedgeN.max.z;

            foreach (Transform t in d.Geometry)
            {
                if (t.name.StartsWith("PileZone_"))
                    d.Piles.Add((t.name, new Vector2(t.position.x, t.position.z), t.lossyScale.x * 0.5f, t.lossyScale.z * 0.5f, t.rotation));
                if (t.name.StartsWith("BaseZone_")) d.Bases.Add(new Vector2(t.position.x, t.position.z));
                if (t.name.StartsWith("Terrain_")) d.Blockers.Add(t.GetComponent<BoxCollider>());
            }
            d.Blockers.AddRange(d.Props.GetComponentsInChildren<BoxCollider>(true));
            Assert.AreEqual(9, d.Piles.Count, "pile discs");
            Assert.AreEqual(4, d.Bases.Count, "bases");
            Assert.AreEqual(12, d.Blockers.Count(b => b.name.StartsWith("Terrain_")), "8 arm colliders + 4 cover boxes");
            var playerBase = TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath).GetComponent<PlayerBase>();
            d.BaseKeepOut = TestAssets.PrivateField<float>(playerBase, "_depositRadius") + 0.15f; // + the player-colour ring
            return d;
        }

        private static bool InPile((string Name, Vector2 C, float A, float B, Quaternion Rot) p, Vector2 q)
        {
            var l = Quaternion.Inverse(p.Rot) * new Vector3(q.x - p.C.x, 0f, q.y - p.C.y);
            return (l.x * l.x) / (p.A * p.A) + (l.z * l.z) / (p.B * p.B) < 1f;
        }

        private static bool InBox(BoxCollider b, Vector2 q)
        {
            var l = b.transform.InverseTransformPoint(new Vector3(q.x, b.transform.position.y, q.y)) - b.center;
            return Mathf.Abs(l.x) < b.size.x * 0.5f && Mathf.Abs(l.z) < b.size.z * 0.5f;
        }

        private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>World XZ of every path vertex plus every triangle centroid, per path object.</summary>
        private static List<(string Name, List<Vector2> Pts)> PathSamples(Dressing d) =>
            d.Paths.GetComponentsInChildren<MeshFilter>().Select(f =>
            {
                var m = f.sharedMesh; var v = m.vertices; var t = m.triangles;
                var pts = v.Select(p => XZ(f.transform.TransformPoint(p))).ToList();
                for (int i = 0; i < t.Length; i += 3)
                    pts.Add(XZ(f.transform.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f)));
                return (f.name, pts);
            }).ToList();

        private static bool IsDecal(Renderer r) => AssetDatabase.GetAssetPath(r.sharedMaterial) == DecalMaterial;

        /// <summary>Visible radius of a decal quad: its alpha content stays inside ~0.8 of the quad's half-size.</summary>
        private static float DecalRadius(Renderer r) => r.transform.lossyScale.x * 0.4f;

        [Test]
        public void GroundDressing_PathsDecalsAndVignettes_AllExist()
        {
            var d = LoadDressing();
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 4).Select(k => $"DirtPaths_Base{k}"),
                d.Paths.Cast<Transform>().Select(t => t.name), "one path object per base");
            Assert.AreEqual(4 * GroundDressingBaker.ArenaDecals.Length, d.Decals.childCount, "arena decals (4-fold)");
            Assert.IsTrue(d.Decals.Cast<Transform>().Any(t => t.name.StartsWith("Flower")) && d.Decals.Cast<Transform>().Any(t => t.name.StartsWith("Mud")),
                "flower patches and mud puddles");
            var groups = d.Vignettes.Cast<Transform>().Select(t => t.name).ToList();
            foreach (var prefix in new[] { "V1_", "V2_", "V3_", "V4_", "V5_", "V6_", "V7_" })
                Assert.IsTrue(groups.Any(g => g.StartsWith(prefix)), $"vignette group {prefix}*");
            Assert.AreEqual(4, groups.Count(g => g.StartsWith("V1_")), "V1 at all four corners");
            var vigPrefabs = d.Vignettes.GetComponentsInChildren<Transform>(true)
                .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                .Select(t => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject))).ToList();
            Assert.Greater(vigPrefabs.Count, 30, "kit props in the vignettes");
            Assert.IsTrue(vigPrefabs.All(p => p.StartsWith("Assets/_Game/Prefabs/Props/Prop_")), "existing prop kit only");
            Assert.IsFalse(vigPrefabs.Any(p => p.Contains("chicken_coop") || p.Contains("nest_basket")), "coops / nest baskets mark bases: never outside the fence");
        }

        [Test]
        public void GroundDressing_IsFourFold()
        {
            var d = LoadDressing();
            var turn = Quaternion.Euler(0f, -90f, 0f);
            var p0 = d.Paths.Find("DirtPaths_Base0");
            for (int k = 1; k < 4; k++)
            {
                var pk = d.Paths.Find($"DirtPaths_Base{k}");
                Assert.AreSame(p0.GetComponent<MeshFilter>().sharedMesh, pk.GetComponent<MeshFilter>().sharedMesh, pk.name);
                Assert.Less(Vector3.Distance(p0.position, pk.position), 1e-4f, pk.name);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, -90f * k, 0f) * p0.rotation, pk.rotation), 0.05f, $"{pk.name} yaw -90k");
            }

            // Every arena decal and every V1 prop has its 90-degree twin (same mesh, size and turned yaw).
            void Twins(IEnumerable<Renderer> rs, string what)
            {
                var list = rs.ToList();
                Assert.Greater(list.Count, 0, what);
                foreach (var r in list)
                {
                    var t = r.transform; var target = turn * t.position;
                    bool ok = list.Any(o => o != r && o.GetComponent<MeshFilter>().sharedMesh == r.GetComponent<MeshFilter>().sharedMesh
                        && Vector3.Distance(o.transform.position, target) < SymmetryTolerance
                        && Mathf.Abs(o.transform.lossyScale.x - t.lossyScale.x) < 1e-3f
                        && Mathf.Abs(Mathf.DeltaAngle(o.transform.eulerAngles.y, t.eulerAngles.y - 90f)) < 0.5f);
                    Assert.IsTrue(ok, $"{what}: {t.parent.name}/{t.name} at {t.position} has no 90-degree twin");
                }
            }
            Twins(d.Decals.GetComponentsInChildren<Renderer>(), "arena decal");
            Twins(d.Vignettes.Cast<Transform>().Where(g => g.name.StartsWith("V1_")).SelectMany(g => g.GetComponentsInChildren<Renderer>()), "V1");

            // V2 / V3 / V4 are 2-fold (the N/S and E/W pairs are half-turns of each other).
            var half = Quaternion.Euler(0f, 180f, 0f);
            foreach (var prefix in new[] { "V2_", "V3_", "V4_" })
            {
                var rs = d.Vignettes.Cast<Transform>().Where(g => g.name.StartsWith(prefix)).SelectMany(g => g.GetComponentsInChildren<Renderer>()).ToList();
                Assert.Greater(rs.Count, 0, prefix);
                foreach (var r in rs)
                    Assert.IsTrue(rs.Any(o => o != r && Vector3.Distance(o.transform.position, half * r.transform.position) < SymmetryTolerance
                                              && o.GetComponent<MeshFilter>().sharedMesh == r.GetComponent<MeshFilter>().sharedMesh),
                        $"{prefix}: {r.transform.parent.name}/{r.name} has no half-turn twin");
            }
        }

        [Test]
        public void GroundDressing_AndVignettes_HaveNoColliders_NoNavMeshObstacles_NoShadowCasting()
        {
            var d = LoadDressing();
            foreach (var root in new[] { d.Ground, d.Vignettes })
            {
                CollectionAssert.IsEmpty(root.GetComponentsInChildren<Collider>(true).Select(c => c.name), $"{root.name}: colliders");
                CollectionAssert.IsEmpty(root.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true).Select(c => c.name), $"{root.name}: NavMeshObstacles");
                CollectionAssert.IsEmpty(root.GetComponentsInChildren<Renderer>(true).Where(r => r.shadowCastingMode != ShadowCastingMode.Off).Select(r => r.name),
                    $"{root.name}: shadow casters");
            }
            foreach (var r in d.Ground.GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsTrue(r.receiveShadows, $"{r.name} receives shadows");
                Assert.AreEqual(r.name.StartsWith("DirtPaths") ? PathMaterial : DecalMaterial, AssetDatabase.GetAssetPath(r.sharedMaterial), r.name);
            }
        }

        [Test]
        public void GroundDressing_Materials_AreMatteAlphaClipped()
        {
            foreach (var (path, cutoff) in new[] { (PathMaterial, GroundDressingBaker.PathCutoff), (DecalMaterial, GroundDressingBaker.DecalCutoff) })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(m, path);
                Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, path);
                Assert.AreEqual(1f, m.GetFloat("_AlphaClip"), path);
                Assert.IsTrue(m.IsKeywordEnabled("_ALPHATEST_ON"), path);
                Assert.AreEqual(cutoff, m.GetFloat("_Cutoff"), 1e-4f, path);
                Assert.AreEqual(0f, m.GetFloat("_Surface"), $"{path}: opaque (alpha-test), not blended");
                Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
                Assert.IsTrue(ti.mipMapsPreserveCoverage, $"{path}: clipped detail must survive the distant mips");
            }
        }

        [Test]
        public void GroundDressing_IsFlat_OneCentimetreUp()
        {
            var d = LoadDressing();
            foreach (var r in d.Ground.GetComponentsInChildren<Renderer>())
            {
                Assert.AreEqual(0.01f, r.bounds.min.y, 0.004f, $"{r.name} bottom");
                Assert.Less(r.bounds.max.y, 0.02f, $"{r.name} is flat (under the 2 cm pile disc / base ring)");
            }
            foreach (var r in d.Vignettes.GetComponentsInChildren<Renderer>().Where(IsDecal))
                Assert.AreEqual(0f, r.bounds.center.y, 0.002f, $"{r.transform.parent.name}/{r.name}: 1 cm over the -0.01 surround grass");
        }

        [Test]
        public void Paths_StayOffPileDiscs_BasePads_ArmWalls_Props_AndInsideTheFence()
        {
            var d = LoadDressing();
            var offenders = new List<string>();
            foreach (var (name, pts) in PathSamples(d))
                foreach (var q in pts)
                {
                    foreach (var p in d.Piles) if (InPile(p, q)) { offenders.Add($"{name} on {p.Name} at {q}"); break; }
                    if (d.Bases.Any(b => Vector2.Distance(b, q) < d.BaseKeepOut)) offenders.Add($"{name} on a base pad at {q}");
                    foreach (var b in d.Blockers) if (InBox(b, q)) { offenders.Add($"{name} under {b.name} at {q}"); break; }
                    if (Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y)) > d.FenceOuter - 0.95f) offenders.Add($"{name} under the fence at {q}");
                }
            CollectionAssert.IsEmpty(offenders.Take(20), $"{offenders.Count} path samples in the wrong place");
        }

        [Test]
        public void Paths_LeadFromEachBase_ToItsPersonalAndBothContestedPiles()
        {
            var d = LoadDressing();
            var centre = d.Piles.Single(p => p.Name == "PileZone_Center");
            foreach (var (name, pts) in PathSamples(d))
            {
                var corner = d.Paths.Find(name).rotation * new Vector3(1f, 0f, 1f);
                var b = d.Bases.OrderBy(x => Vector2.Distance(x.normalized, XZ(corner).normalized)).First();
                Assert.Less(pts.Min(q => Vector2.Distance(q, b)), d.BaseKeepOut + 0.6f, $"{name} starts at its base pad");
                // Its personal pile and the two contested piles beside its corner.
                var mine = d.Piles.Where(p => p.Name != centre.Name).OrderBy(p => Vector2.Distance(p.C, b)).Take(3).ToList();
                Assert.AreEqual(1, mine.Count(p => p.Name.Contains("Personal")), name);
                foreach (var p in mine)
                {
                    float gap = pts.Min(q => Vector2.Distance(q, p.C) - Mathf.Max(p.A, p.B));
                    Assert.Less(gap, 0.6f, $"{name} reaches {p.Name}");
                }
                Assert.Greater(pts.Min(q => Vector2.Distance(q, centre.C)), 8f, $"{name}: no spoke into the shared centre pile (by design)");
            }
        }

        [Test]
        public void Decals_StayOffPiles_BasePads_Paths_AndArmWalls()
        {
            var d = LoadDressing();
            var pathPts = PathSamples(d).SelectMany(p => p.Pts).ToList();
            var offenders = new List<string>();
            foreach (var r in d.Decals.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                // The whole quad (its bounds) stays off the pile discs and the base pad / deposit radius.
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        var q = new Vector2(Mathf.Lerp(b.min.x, b.max.x, i / 4f), Mathf.Lerp(b.min.z, b.max.z, j / 4f));
                        foreach (var p in d.Piles) if (InPile(p, q)) offenders.Add($"{r.name} quad on {p.Name}");
                        if (d.Bases.Any(c => Vector2.Distance(c, q) < d.BaseKeepOut)) offenders.Add($"{r.name} quad on a base pad");
                    }
                // Its visible patch stays off the paths (never reads as part of a lane) and out from under the arm walls.
                var c2 = XZ(r.transform.position); float rad = DecalRadius(r);
                if (pathPts.Any(q => Vector2.Distance(q, c2) < rad + 0.3f)) offenders.Add($"{r.name} on / against a path");
                for (int a = 0; a < 16; a++)
                {
                    var q = c2 + rad * new Vector2(Mathf.Cos(a * Mathf.PI / 8f), Mathf.Sin(a * Mathf.PI / 8f));
                    foreach (var bx in d.Blockers.Where(x => x.name.StartsWith("Terrain_")))
                        if (InBox(bx, q)) offenders.Add($"{r.name} under {bx.name}");
                }
            }
            CollectionAssert.IsEmpty(offenders.Distinct(), "arena decals in the wrong place");
        }

        [Test]
        public void Vignettes_AreOutsideTheFence_ClearOfTheHedge_AndLowInTheGap()
        {
            var d = LoadDressing();
            var offenders = new List<string>();
            bool InBand(float lo, float hi) => lo < d.HedgeOuter && hi > d.HedgeInner;
            foreach (var r in d.Vignettes.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds; string n = $"{r.transform.parent.name}/{r.name}";
                if (IsDecal(r))
                {
                    // A flat decal is judged by its visible patch (the quad's corners are transparent).
                    var c = XZ(r.transform.position); float m = Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)), rad = DecalRadius(r);
                    if (m - rad < d.FenceOuter) offenders.Add($"{n}: decal patch reaches the fence / arena");
                    if (m + rad > d.HedgeInner && m - rad < d.HedgeOuter) offenders.Add($"{n}: decal patch under the hedge");
                    continue;
                }
                bool inside = b.min.x < d.FenceOuter && b.max.x > -d.FenceOuter && b.min.z < d.FenceOuter && b.max.z > -d.FenceOuter;
                if (inside) offenders.Add($"{n} overlaps the fence / arena {b.min}..{b.max}");
                // 3D props: not inside the hedge band on any side.
                bool hedge = (InBand(b.min.z, b.max.z) || InBand(-b.max.z, -b.min.z)) && b.min.x < d.HedgeOuter && b.max.x > -d.HedgeOuter
                          || (InBand(b.min.x, b.max.x) || InBand(-b.max.x, -b.min.x)) && b.min.z < d.HedgeOuter && b.max.z > -d.HedgeOuter;
                if (hedge) offenders.Add($"{n} pokes into the hedge {b.min}..{b.max}");
                // Fence-hedge gap: the match camera turns with the home corner, so every gap is the near side for some seat.
                bool inGap = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) < d.HedgeInner;
                if (inGap && b.max.y >= GapMaxHeight) offenders.Add($"{n} is {b.max.y:F2} m tall in the fence-hedge gap");
            }
            CollectionAssert.IsEmpty(offenders, "vignette placement");
        }

        [Test]
        public void Vignettes_DoNotPokeIntoTheBarn_Silos_Trees_OrEachOther()
        {
            var d = LoadDressing();
            var groups = d.Vignettes.Cast<Transform>().ToList();
            var others = d.Surroundings.Cast<Transform>().Where(t => t != d.Vignettes && t.name != "SurroundGrass" && t.name != "ArenaHedge").ToList();
            Assert.IsTrue(others.Any(o => o.name == "Barn") && others.Any(o => o.name == "Silo_A"), "barn + silos present");
            var offenders = new List<string>();
            foreach (var g in groups)
                foreach (var f in g.GetComponentsInChildren<MeshFilter>().Where(f => !IsDecal(f.GetComponent<Renderer>())))
                {
                    var wv = f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)).ToArray();
                    foreach (var o in others.Concat(groups.Where(x => x != g)))
                        foreach (var of in o.GetComponentsInChildren<MeshFilter>().Where(x => x.sharedMesh != null && !IsDecal(x.GetComponent<Renderer>())))
                        {
                            var lb = of.sharedMesh.bounds;
                            if (wv.Any(w => lb.Contains(of.transform.InverseTransformPoint(w))))
                                offenders.Add($"{g.name}/{f.transform.parent.name} -> {o.name}/{of.name}");
                        }
                }
            CollectionAssert.IsEmpty(offenders, "vignette props inside another object's mesh bounds");
        }
}
}
