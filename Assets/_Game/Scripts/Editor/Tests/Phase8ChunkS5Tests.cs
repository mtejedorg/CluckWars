using System.Collections.Generic;
using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Settings;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk C Stage 3 S5 (Performance Mode fence): the ~3 000-tri protected-cap fence candidate measured in
    /// C1 ships as a second mesh (<c>wall_segment_perf.fbx</c> + the B4-derived <c>wall_segment_inner_perf.asset</c>);
    /// <see cref="FencePerformanceSwap"/> draws it on every fence renderer while Performance Mode is on. The default
    /// mesh stays the full 4 000-tri one (<c>Phase8ChunkC1Tests</c> keeps guarding that).
    /// </summary>
    public class Phase8ChunkS5Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string SetAsset = "Assets/_Game/Data/FencePerformanceMeshes.asset";

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

        private static Mesh FbxMesh(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
        private static int Tris(Mesh m) => m.triangles.Length / 3;

        [Test]
        public void PerfMesh_IsAboutThreeThousandTris_WithTheDefaultMeshsPivotAndBounds()
        {
            var def = FbxMesh(FenceInnerMeshBuilder.DefaultSource);
            var perf = FbxMesh(FenceInnerMeshBuilder.PerfSource);
            Assert.That(Tris(perf), Is.InRange(2800, 3200), "The Performance Mode fence is the ~3 000-tri C1 candidate.");
            Assert.Less(Tris(perf), Tris(def) - 700, "It must be clearly lighter than the default fence.");
            Assert.AreEqual("wall_segment", perf.name, "Same mesh name as the default (the C1 guardrail finds fence parts by it).");
            Assert.AreEqual(1, perf.subMeshCount);
            Assert.IsTrue(perf.isReadable, "The swap re-batches the perf meshes at runtime, which needs Read/Write.");

            Assert.Less((perf.bounds.min - def.bounds.min).magnitude, 0.01f, "Perf mesh min corner (pivot / extents) drifted from the default.");
            Assert.Less((perf.bounds.max - def.bounds.max).magnitude, 0.01f, "Perf mesh max corner (pivot / extents) drifted from the default.");
        }

        [Test]
        public void PerfMesh_UsesTheSameAtlasUvRange_AsTheDefault()
        {
            var def = FbxMesh(FenceInnerMeshBuilder.DefaultSource);
            var perf = FbxMesh(FenceInnerMeshBuilder.PerfSource);
            var du = def.uv; var pu = perf.uv;
            Assert.AreEqual(perf.vertexCount, pu.Length, "Perf mesh has no UVs.");
            float eps = 1e-3f;
            Assert.GreaterOrEqual(pu.Min(u => u.x), du.Min(u => u.x) - eps);
            Assert.LessOrEqual(pu.Max(u => u.x), du.Max(u => u.x) + eps);
            Assert.GreaterOrEqual(pu.Min(u => u.y), du.Min(u => u.y) - eps);
            Assert.LessOrEqual(pu.Max(u => u.y), du.Max(u => u.y) + eps);
        }

        [Test]
        public void InnerPerfMesh_IsDerivedFromThePerfMesh_WithTheB4UvRule()
        {
            var perf = FbxMesh(FenceInnerMeshBuilder.PerfSource);
            var inner = AssetDatabase.LoadAssetAtPath<Mesh>(FenceInnerMeshBuilder.PerfInner);
            Assert.IsNotNull(inner, FenceInnerMeshBuilder.PerfInner + " is missing (menu: Cluck Wars/Map/Rebuild Fence Inner Meshes).");
            Assert.IsTrue(inner.isReadable);
            Assert.AreEqual(Tris(perf), Tris(inner), "Inner perf mesh must have the perf mesh's triangle count.");
            Assert.That(Tris(inner), Is.InRange(2800, 3200));
            CollectionAssert.AreEqual(perf.triangles, inner.triangles, "Inner perf triangles differ from the perf mesh.");
            var pv = perf.vertices; var iv = inner.vertices;
            for (int i = 0; i < pv.Length; i++) Assert.Less((pv[i] - iv[i]).magnitude, 1e-4f, $"Vertex {i} moved; only UVs may differ.");
            Assert.IsFalse(inner.uv.SequenceEqual(perf.uv), "Inner perf UVs equal the perf mesh's: the B4 density fix is missing.");

            // Re-deriving from the perf mesh gives exactly the shipped asset (rule + parameters pinned).
            var rebuilt = FenceInnerMeshBuilder.Build(perf);
            try { AssertUvEqual(rebuilt.uv, inner.uv, "inner perf vs rebuilt"); }
            finally { Object.DestroyImmediate(rebuilt); }
        }

        [Test]
        public void B4UvRule_ReproducesTheShippedDefaultInnerMesh()
        {
            var def = FbxMesh(FenceInnerMeshBuilder.DefaultSource);
            var shipped = AssetDatabase.LoadAssetAtPath<Mesh>(FenceInnerMeshBuilder.DefaultInner);
            var rebuilt = FenceInnerMeshBuilder.Build(def);
            try { AssertUvEqual(rebuilt.uv, shipped.uv, "default inner vs rebuilt"); }
            finally { Object.DestroyImmediate(rebuilt); }
        }

        private static void AssertUvEqual(Vector2[] a, Vector2[] b, string what)
        {
            Assert.AreEqual(a.Length, b.Length, what);
            int bad = 0; float worst = 0;
            for (int i = 0; i < a.Length; i++) { float d = (a[i] - b[i]).magnitude; if (d > 1e-3f) bad++; worst = Mathf.Max(worst, d); }
            Assert.AreEqual(0, bad, $"{what}: {bad} UVs differ (worst {worst}).");
        }

        [Test]
        public void MeshSet_HoldsThePerformanceMeshes()
        {
            var set = AssetDatabase.LoadAssetAtPath<FencePerformanceMeshesSO>(SetAsset);
            Assert.IsNotNull(set, SetAsset + " is missing: ProjectInstaller binds it for FencePerformanceSwap.");
            Assert.AreSame(FbxMesh(FenceInnerMeshBuilder.PerfSource), set.OuterPerformance);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Mesh>(FenceInnerMeshBuilder.PerfInner), set.InnerPerformance);
            Assert.AreSame(set.OuterPerformance, set.For(false));
            Assert.AreSame(set.InnerPerformance, set.For(true));
        }

        [Test]
        public void ProjectContextPrefab_WiresTheMeshSet_IntoProjectInstaller()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/ProjectContext.prefab");
            var installer = prefab.GetComponentInChildren<CluckWars.Installers.ProjectInstaller>(true);
            Assert.IsNotNull(installer, "ProjectContext.prefab has no ProjectInstaller.");
            var slot = new SerializedObject(installer).FindProperty("_fencePerformanceMeshes");
            Assert.IsNotNull(slot, "ProjectInstaller lost its _fencePerformanceMeshes slot.");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<FencePerformanceMeshesSO>(SetAsset), slot.objectReferenceValue,
                "ProjectContext.prefab must reference FencePerformanceMeshes.asset: without it Performance Mode silently keeps the full fence.");
        }

        // ---- the swap on the shipped map ------------------------------------------------------------------

        private static List<MeshRenderer> FenceRenderers(Scene scene, out int outer, out int inner)
        {
            var props = scene.GetRootGameObjects().First(g => g.name == "MapProps").transform;
            var list = new List<MeshRenderer>(); outer = 0; inner = 0;
            foreach (Transform t in props)
            {
                bool isInner = t.name.StartsWith("InnerWall_"), isOuter = t.name.StartsWith("Wall_");
                if (!isInner && !isOuter) continue;
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    list.Add(mr);
                    if (isInner) inner++; else outer++;
                }
            }
            return list;
        }

        private static void WithPerformanceMode(bool on, System.Action body)
        {
            bool had = PlayerPrefs.HasKey(PlayerPreferences.PerformanceModeKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.PerformanceModeKey, 0);
            try
            {
                PlayerPreferences.PerformanceModeEnabled = on;
                body();
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.PerformanceModeKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.PerformanceModeKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        [Test]
        public void Swap_UsesThePerfMesh_OnEveryOuterAndInnerFencePanel_AndRestoresTheDefault()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var set = AssetDatabase.LoadAssetAtPath<FencePerformanceMeshesSO>(SetAsset);
            var defaultOuter = FbxMesh(FenceInnerMeshBuilder.DefaultSource);
            var defaultInner = AssetDatabase.LoadAssetAtPath<Mesh>(FenceInnerMeshBuilder.DefaultInner);
            var originals = FenceRenderers(scene, out int outer, out int inner);
            Assert.Greater(outer, 40); Assert.Greater(inner, 40);
            Assert.IsTrue(originals.All(r => r.enabled), "The shipped map draws every fence panel.");

            var go = new GameObject("S5 swap");
            try
            {
                var swap = go.AddComponent<FencePerformanceSwap>();
                swap.Construct(null, set);

                WithPerformanceMode(false, () =>
                {
                    Assert.IsFalse(swap.Sync(batch: false), "Default mode must change nothing.");
                    Assert.IsFalse(swap.Twins.Any(), "Default mode must not build any twin.");
                    Assert.IsTrue(originals.All(r => r.enabled));
                });

                WithPerformanceMode(true, () =>
                {
                    Assert.IsTrue(swap.Sync(batch: false));
                    Assert.IsTrue(swap.PerformanceShown);
                    var twins = swap.Twins.ToList();
                    Assert.AreEqual(originals.Count, twins.Count, "One perf twin per fence panel (outer Wall_* and InnerWall_*).");
                    Assert.IsTrue(originals.All(r => !r.enabled), "The full-detail panels must be hidden.");
                    Assert.IsTrue(twins.All(r => r.enabled));

                    int perfOuter = 0, perfInner = 0;
                    foreach (var o in originals)
                    {
                        var twin = twins.Single(t => t.transform.parent == o.transform.parent);
                        var mesh = twin.GetComponent<MeshFilter>().sharedMesh;
                        var src = o.GetComponent<MeshFilter>().sharedMesh;
                        // The swap finds panels by name (batched MeshFilters report "Combined Mesh" at runtime): the name must agree with the mesh.
                        Assert.IsTrue(FencePerformanceSwap.TryClassify(o, out bool isInner), o.transform.parent.name);
                        Assert.AreSame(isInner ? defaultInner : defaultOuter, src, o.transform.parent.name + ": name and mesh disagree.");
                        Assert.AreSame(set.For(isInner), mesh, o.transform.parent.name);
                        if (isInner) perfInner++; else perfOuter++;
                        // identical placement, materials and shadow behaviour: the fade shader and shadows keep working
                        Assert.Less((twin.transform.position - o.transform.position).magnitude, 1e-4f);
                        Assert.Less(Quaternion.Angle(twin.transform.rotation, o.transform.rotation), 0.01f);
                        Assert.Less((twin.transform.lossyScale - o.transform.lossyScale).magnitude, 1e-4f);
                        CollectionAssert.AreEqual(o.sharedMaterials, twin.sharedMaterials);
                        Assert.AreEqual(o.shadowCastingMode, twin.shadowCastingMode);
                        Assert.AreEqual(o.receiveShadows, twin.receiveShadows);
                        Assert.AreEqual(o.gameObject.layer, twin.gameObject.layer);
                        Assert.AreEqual(o.name, twin.name, "Same name: the C1 capture guardrail finds fence parts by it.");
                        Assert.That(twin.bounds.size.x, Is.EqualTo(o.bounds.size.x).Within(0.02f));
                        Assert.That(twin.bounds.size.y, Is.EqualTo(o.bounds.size.y).Within(0.02f));
                        Assert.That(twin.bounds.size.z, Is.EqualTo(o.bounds.size.z).Within(0.02f));
                    }
                    Assert.AreEqual(outer, perfOuter); Assert.AreEqual(inner, perfInner);
                    Assert.IsFalse(swap.Sync(batch: false), "Already showing the perf fence: nothing to do.");
                });

                WithPerformanceMode(false, () =>
                {
                    Assert.IsTrue(swap.Sync(batch: false));
                    Assert.IsFalse(swap.PerformanceShown);
                    Assert.IsTrue(originals.All(r => r.enabled), "Turning Performance Mode off must bring the full fence back.");
                    Assert.IsTrue(swap.Twins.All(r => !r.enabled));
                });

                WithPerformanceMode(true, () =>
                {
                    Assert.IsTrue(swap.Sync(batch: false), "Switching back on re-uses the twins.");
                    Assert.IsTrue(originals.All(r => !r.enabled));
                    Assert.AreEqual(originals.Count, swap.Twins.Count());
                });
            }
            finally
            {
                foreach (var t in go.GetComponent<FencePerformanceSwap>().Twins.ToList()) Object.DestroyImmediate(t.gameObject);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Swap_LeavesTheShippedSceneMeshesAlone()
        {
            // The default look is the scene's: Phase8ChunkB4Tests / C1 pin the mesh references; S5 only draws twins on top.
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var defaultOuter = FbxMesh(FenceInnerMeshBuilder.DefaultSource);
            var defaultInner = AssetDatabase.LoadAssetAtPath<Mesh>(FenceInnerMeshBuilder.DefaultInner);
            foreach (var r in FenceRenderers(scene, out _, out _))
            {
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(m == defaultOuter || m == defaultInner, r.transform.parent.name + " does not use a default fence mesh.");
            }
        }
    }
}
