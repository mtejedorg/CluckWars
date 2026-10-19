using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D11 "prop mesh holes" (audit L11): the AI-generated feed sack, trough, milk churn, wheelbarrow and
    /// scarecrow (and, flagged by the survey below, the nest basket and the oak tree) had the same shell cracks C14 fixed
    /// on the barrel / silo / bush / hay bale: the meshes are soups of overlapping shells, and through a crack the camera
    /// sees the culled back of the far side, i.e. the grass behind the prop.
    /// <para>
    /// Repair (scratchpad <c>env3d/D11/d11.py</c>, C14's weld 1 mm + small-loop fill + outward re-orientation, then a
    /// liner): C14's liner was a star-shaped shell around the vertical axis, which puts a lid inside anything open-topped
    /// (sack neck, trough, wheelbarrow tray, nest). D11's liner is a closed voxel remesh of the prop itself (2 % of the
    /// bbox diagonal), decimated to 20 % of the original tri count, every vertex pulled 0.6 % of the diagonal behind the
    /// nearest original surface and pulled further wherever a ray survey sees it in front of a visible original face;
    /// faces still in front after four passes are dropped. Each liner face carries the flat UV of the surface it hides.
    /// Object / mesh / material / UV names, pivot, axis and the outer silhouette are unchanged (.meta + textures untouched).
    /// </para>
    /// The guardrail is <see cref="PropSeeThroughSurvey"/>: a REAL render of every Assets/Generated prop mesh that the
    /// game uses (prefabs under Assets/_Game + Game/Map scene objects), isolated against magenta, so a prop added or
    /// re-exported later is checked without anyone remembering to add it to a list.
    /// </summary>
    public class Phase8ChunkD11Tests
    {
        /// <summary>
        /// Crack budget per prop: crack pixels / filled silhouette pixels, summed over the survey's 8 match-camera views.
        /// The cracked meshes measured 0.69-2.73 % (trough .. feed sack); after D11 the repaired props are 0.06-0.30 %, except
        /// nest_basket and scarecrow (see <see cref="KnownLeftovers"/>). Any prop added to the game later gets this budget automatically.
        /// </summary>
        public const float CrackBudget = 0.005f;

        /// <summary>
        /// Props over <see cref="CrackBudget"/> that D11 reports instead of repairing, capped at about their measured value
        /// so they cannot get worse. nest_basket 0.58 % / scarecrow 0.55 % (after D11, from 1.77 / 1.65 %): the rest is gaps
        /// between frayed single-sided straw cards, real see-through geometry the liner cannot sit behind. feed_silo 0.68 % /
        /// hay_bale 0.53 %: C14's leftovers (silo hoop specks, bale straw ends); a D11 liner on top of C14's silo had to drop 41 % of
        /// its faces as poking out and cut the cracks ~7 %, so both keep the committed C14 mesh.
        /// </summary>
        public static readonly Dictionary<string, float> KnownLeftovers = new Dictionary<string, float>
        {
            { "nest_basket", 0.0070f }, { "scarecrow", 0.0065f }, { "feed_silo", 0.0080f }, { "hay_bale", 0.0065f },
        };

        /// <summary>
        /// Generated props whose only user is their own Prop_*.prefab, which nothing in the build references (they are not in
        /// Game/Map and no build scene depends on the prefab). Not repaired; capped at their measured cracks, and the test
        /// fails as soon as one of them is placed, so it then has to meet <see cref="CrackBudget"/>.
        /// </summary>
        public static readonly Dictionary<string, float> UnusedProps = new Dictionary<string, float>
        {
            { "food_pile_large", 0.045f }, { "food_pile_medium", 0.020f }, { "food_pile_small", 0.008f },
        };

        // name, bounds min, bounds max (Unity space, BEFORE the fix: the liner sits inside, so the silhouette is unchanged
        // within 1 mm), tris before, open edges before (welded by position at 1e-4, as Phase8ChunkC14Tests), material.
        public static readonly (string name, Vector3 min, Vector3 max, int trisBefore, int openBefore)[] Repaired =
        {
            ("feed_sack",   new Vector3(-0.2996f, -0.3356f, -0.0018f), new Vector3(0.2983f, 0.3316f, 0.8020f), 1500, 1490),
            ("trough",      new Vector3(-0.5975f, -0.7011f, -0.0002f), new Vector3(0.5985f, 0.7004f, 0.6377f), 1900, 1954),
            ("milk_churn",  new Vector3(-0.3193f, -0.4043f, -0.0021f), new Vector3(0.3162f, 0.4091f, 1.0016f), 1800, 1874),
            ("wheelbarrow", new Vector3(-0.7015f, -0.2653f, 0.0022f), new Vector3(0.6982f, 0.2648f, 0.5093f), 2200, 2164),
            ("scarecrow",   new Vector3(-0.5030f, -0.9087f, -0.0119f), new Vector3(0.5015f, 0.9092f, 2.1999f), 4000, 4882),
            ("nest_basket", new Vector3(-0.3384f, -0.3533f, -0.0005f), new Vector3(0.3357f, 0.3515f, 0.4750f), 3300, 3980),
            ("oak_tree",    new Vector3(-1.7074f, -1.7160f, -0.0036f), new Vector3(1.7184f, 1.7214f, 4.5011f), 1800, 1900),
        };

        /// <summary>Liner + fill may add at most this fraction of the original tris (measured +20-28 %).</summary>
        public const float MaxTriGrowth = 0.30f;

        private string _originalScenePath;
        private bool _originalAsync;

        [SetUp]
        public void CaptureState()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            _originalAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;   // render tests must not draw the cyan placeholder shader
        }

        [TearDown]
        public void RestoreState()
        {
            ShaderUtil.allowAsyncCompilation = _originalAsync;
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void EveryUsedGeneratedProp_IsNotSeeThrough()
        {
            var uses = PropSeeThroughSurvey.EnumerateUsedGeneratedProps();
            var names = uses.Select(u => u.Mesh.name).ToList();
            foreach (var n in Repaired.Select(r => r.name).Concat(new[] { "barrel", "feed_silo", "bush_round", "hay_bale", "crate", "wall_segment" }))
                Assert.Contains(n, names, $"the survey no longer finds '{n}': the enumeration (prefabs + Game/Map) is broken, so the budget would pass vacuously");

            var buildDeps = new HashSet<string>(AssetDatabase.GetDependencies(
                EditorBuildSettings.scenes.Where(sc => sc.enabled).Select(sc => sc.path).ToArray(), true));
            var over = new List<string>();
            var all = new StringBuilder();
            foreach (var u in uses)
            {
                var r = PropSeeThroughSurvey.Measure(u);
                all.AppendLine(r.ToString());
                float budget = CrackBudget;
                if (UnusedProps.TryGetValue(u.Mesh.name, out float unusedCap))
                {
                    Assert.AreEqual(0, u.SceneInstances, $"{u.Mesh.name} is now placed in Game/Map: repair it (D11 method) and drop it from UnusedProps");
                    Assert.IsFalse(buildDeps.Contains(u.Source), $"{u.Source} is now referenced by a build scene: repair it and drop it from UnusedProps");
                    budget = unusedCap;
                }
                else if (KnownLeftovers.TryGetValue(u.Mesh.name, out float cap)) budget = cap;
                if (r.CrackFraction > budget) over.Add($"{r}  (budget {budget:P2})");
            }
            Assert.IsEmpty(over, $"props see-through above their crack budget (cracks showing the ground):\n{string.Join("\n", over)}\n-- all --\n{all}");
        }

        /// <summary>The detector itself: a cube with a hole in its top is a crack (the inside faces behind it are culled); a square frame is an opening.</summary>
        [Test]
        public void Survey_SeesACrack_AndIgnoresADesignedOpening()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var cube = BuildBox(open: true);
            var closed = BuildBox(open: false);
            var frame = BuildFrame();
            try
            {
                var crack = PropSeeThroughSurvey.Measure(new PropSeeThroughSurvey.PropUse { Mesh = cube, Materials = new[] { mat }, Rotation = Quaternion.Euler(0, 20, 0), Scale = Vector3.one, Fbx = "test/open_cube" });
                Assert.Greater(crack.CrackFraction, 0.02f, "an open-sided cube must read as a crack: " + crack);
                var shut = PropSeeThroughSurvey.Measure(new PropSeeThroughSurvey.PropUse { Mesh = closed, Materials = new[] { mat }, Rotation = Quaternion.Euler(0, 20, 0), Scale = Vector3.one, Fbx = "test/cube" });
                Assert.AreEqual(0, shut.CrackPx, "a closed cube has no crack: " + shut);
                var ring = PropSeeThroughSurvey.Measure(new PropSeeThroughSurvey.PropUse { Mesh = frame, Materials = new[] { mat }, Rotation = Quaternion.identity, Scale = Vector3.one, Fbx = "test/frame" });
                Assert.Less(ring.CrackFraction, 0.001f, "a frame's hole is a designed opening, not a crack: " + ring);
                Assert.Greater(ring.OpeningFraction, 0.05f, "the frame's opening must be seen: " + ring);
            }
            finally { Object.DestroyImmediate(mat); Object.DestroyImmediate(cube); Object.DestroyImmediate(closed); Object.DestroyImmediate(frame); }
        }

        private static IEnumerable<TestCaseData> RepairedCases() => Repaired.Select(r => new TestCaseData(r.name).SetName($"Repaired_{r.name}_KeepsNamesPivotBoundsMaterial_AndStaysInBudget"));

        [TestCaseSource(nameof(RepairedCases))]
        public void Repaired_KeepsNamesPivotBoundsMaterial_AndStaysInBudget(string name)
        {
            var p = Repaired.First(r => r.name == name);
            string path = $"Assets/Generated/{name}.fbx";
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            Assert.AreEqual(1, meshes.Length, $"{name}: the FBX must hold exactly one mesh");
            var mesh = meshes[0];
            Assert.AreEqual(name, mesh.name, $"{name}: mesh name changed (prefab fileIDs depend on it)");
            Assert.AreEqual(1, mesh.subMeshCount, $"{name}: submesh count changed (one material slot)");

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.AreEqual(name, root.name);
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Length, $"{name}: extra nodes in the FBX");
            Assert.AreEqual(Vector3.zero, root.transform.localPosition, $"{name}: pivot moved");
            Assert.Less(Quaternion.Angle(root.transform.localRotation, Quaternion.Euler(270.02f, 0f, 0f)), 0.05f, $"{name}: axis conversion changed ({root.transform.localRotation.eulerAngles})");

            var b = mesh.bounds;
            for (int a = 0; a < 3; a++)
            {
                Assert.AreEqual(p.min[a], b.min[a], 0.0015f, $"{name}: bounds min axis {a} (the liner must stay inside the silhouette)");
                Assert.AreEqual(p.max[a], b.max[a], 0.0015f, $"{name}: bounds max axis {a}");
            }

            int tris = mesh.triangles.Length / 3;
            Assert.Greater(tris, p.trisBefore, $"{name}: {tris} tris, not above the pre-fix {p.trisBefore}: re-exported without the D11 liner?");
            Assert.LessOrEqual(tris, (int)(p.trisBefore * (1f + MaxTriGrowth)), $"{name}: {tris} tris, over +{MaxTriGrowth:P0} of the original {p.trisBefore}");
            int open = CountOpenEdges(mesh);
            Assert.Less(open, p.openBefore, $"{name}: {open} open edges, the cracked mesh had {p.openBefore}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Prefabs/Props/Prop_{name}.prefab");
            Assert.IsNotNull(prefab, $"Prop_{name}.prefab missing");
            var mf = prefab.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh == mesh);
            Assert.IsNotNull(mf, $"Prop_{name}.prefab lost its {name} mesh reference");
            var mr = mf.GetComponent<MeshRenderer>();
            Assert.AreEqual($"Assets/_Game/Art/Materials/Prop_{name}.mat", AssetDatabase.GetAssetPath(mr.sharedMaterial), $"Prop_{name}.prefab material changed");
            Assert.AreEqual(1, mr.sharedMaterials.Length, $"Prop_{name}.prefab material slots changed");
        }

        public static int CountOpenEdges(Mesh m)
        {
            var v = m.vertices; var tr = m.triangles;
            var weld = new Dictionary<(int, int, int), int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = (Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; }
                id[i] = w;
            }
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < tr.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[tr[t + e]], c = id[tr[t + (e + 1) % 3]];
                    if (a == c) continue;
                    long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            return edges.Values.Count(c => c == 1);
        }

        private static Mesh BuildBox(bool open)
        {
            var mesh = new Mesh { name = open ? "open_cube" : "cube" };
            var verts = new List<Vector3>(); var tris = new List<int>();
            // 6 faces of 3x3 cells, wound outward by FixWinding; the open cube lacks the top's centre cell (seen from every survey view)
            var faces = new (Vector3 n, Vector3 u, Vector3 v)[]
            {
                (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.back, Vector3.up),
                (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.right, Vector3.back),
                (Vector3.forward, Vector3.left, Vector3.up), (Vector3.back, Vector3.right, Vector3.up),
            };
            foreach (var f in faces)
                for (int gu = 0; gu < 3; gu++)
                    for (int gv = 0; gv < 3; gv++)
                    {
                        if (open && f.n == Vector3.up && gu == 1 && gv == 1) continue;   // a crack: the top's centre cell is missing
                        int s = verts.Count;
                        var c = f.n * 0.5f;
                        float u0 = gu / 3f - 0.5f, u1 = (gu + 1) / 3f - 0.5f, v0 = gv / 3f - 0.5f, v1 = (gv + 1) / 3f - 0.5f;
                        verts.Add(c + f.u * u0 + f.v * v0); verts.Add(c + f.u * u0 + f.v * v1);
                        verts.Add(c + f.u * u1 + f.v * v1); verts.Add(c + f.u * u1 + f.v * v0);
                        tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    }
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            FixWinding(mesh);
            return mesh;
        }

        /// <summary>A square frame in the XY plane (outer 1 m, inner 0.5 m), double-faced so it is solid from any side.</summary>
        private static Mesh BuildFrame()
        {
            var mesh = new Mesh { name = "frame" };
            var verts = new List<Vector3>(); var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int s = verts.Count; verts.AddRange(new[] { a, b, c, d });
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3, s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            const float o = 0.5f, i = 0.25f;
            Quad(new Vector3(-o, -o), new Vector3(-o, o), new Vector3(-i, o), new Vector3(-i, -o));
            Quad(new Vector3(i, -o), new Vector3(i, o), new Vector3(o, o), new Vector3(o, -o));
            Quad(new Vector3(-i, i), new Vector3(-i, o), new Vector3(i, o), new Vector3(i, i));
            Quad(new Vector3(-i, -o), new Vector3(-i, -i), new Vector3(i, -i), new Vector3(i, -o));
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flip any triangle whose geometric normal points toward the centre (keeps BuildBox honest whatever the quad order).</summary>
        private static void FixWinding(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;
            for (int k = 0; k < t.Length; k += 3)
            {
                var n = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                var c = (v[t[k]] + v[t[k + 1]] + v[t[k + 2]]) / 3f;
                if (Vector3.Dot(n, c) < 0) { (t[k + 1], t[k + 2]) = (t[k + 2], t[k + 1]); }
            }
            m.triangles = t; m.RecalculateNormals();
        }
    }

    /// <summary>
    /// D11 see-through survey. Renders one prop mesh in an isolated preview scene (its own materials, as used in the game)
    /// against magenta from the match camera's angle: orthographic, pitch 30 deg, 8 yaws 45 deg apart, framed to the mesh.
    /// In each view, "holes" are magenta pixels enclosed by the silhouette (not reachable from the frame border).
    /// A second render with the same materials made double-sided tells the two kinds apart: hole components that stay
    /// open double-sided and are bigger than 0.5 % of the view's silhouette are designed openings (handle loops, wheel
    /// hubs, the gap under a scarecrow's arm); everything else (back faces seen through a crack, specks) is a crack.
    /// Also driven by <c>env3d/D11/survey.py</c> through reflection (<see cref="RunJson"/>), which writes the ranked table.
    /// </summary>
    public static class PropSeeThroughSurvey
    {
        public const string GameScene = "Assets/_Game/Scenes/Game.unity";
        public const string MapScene = "Assets/_Game/Scenes/Map.unity";
        public const float MatchPitch = 30f;
        public const int Views = 8;
        public const int DefaultResolution = 320;
        /// <summary>An opening (still open when double-sided) bigger than this share of the view's silhouette is by design.</summary>
        public const float OpeningMinShare = 0.005f;

        public sealed class PropUse
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Quaternion Rotation;
            public Vector3 Scale;
            public string Fbx, Source;
            public int SceneInstances;
        }

        public sealed class PropHoles
        {
            public string Fbx, Mesh, Source;
            public int Tris, SceneInstances, WorstView;
            public long CrackPx, OpeningPx, FilledPx;
            public float CrackFraction => FilledPx > 0 ? (float)CrackPx / FilledPx : 0f;
            public float OpeningFraction => FilledPx > 0 ? (float)OpeningPx / FilledPx : 0f;

            public override string ToString() => FormattableString.Invariant(
                $"{Fbx}:{Mesh} cracks {CrackFraction:P2} ({CrackPx} px) openings {OpeningFraction:P2} of {FilledPx} px, worst view {WorstView}, {Tris} tris, {SceneInstances} in Game/Map");

            public string ToJson() => FormattableString.Invariant(
                $"{{\"fbx\":\"{Fbx}\",\"mesh\":\"{Mesh}\",\"src\":\"{Source}\",\"tris\":{Tris},\"inScene\":{SceneInstances},\"crackPx\":{CrackPx},\"openingPx\":{OpeningPx},\"filledPx\":{FilledPx},\"crack\":{CrackFraction:0.000000},\"opening\":{OpeningFraction:0.000000},\"worstView\":{WorstView}}}");
        }

        static bool IsGeneratedFbx(Mesh m, out string path)
        {
            path = m != null ? AssetDatabase.GetAssetPath(m) : null;
            return path != null && path.StartsWith("Assets/Generated/", StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every Assets/Generated FBX mesh drawn by a MeshRenderer in a prefab under Assets/_Game or in Game.unity / Map.unity
        /// (skinned chickens are not props). Prefab placement wins (authored upright); scene-only meshes use the scene pose.
        /// Opens Game + Map: callers restore the scene.
        /// </summary>
        public static List<PropUse> EnumerateUsedGeneratedProps()
        {
            var d = new Dictionary<Mesh, PropUse>();
            void Consider(MeshRenderer mr, Transform root, string src)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || !IsGeneratedFbx(mf.sharedMesh, out var path)) return;
                if (d.TryGetValue(mf.sharedMesh, out var known)) { if (root == null) known.SceneInstances++; return; }
                var rot = root != null ? Quaternion.Inverse(root.rotation) * mr.transform.rotation : mr.transform.rotation;
                var s = mr.transform.lossyScale;
                if (root != null) { var rs = root.lossyScale; s = new Vector3(s.x / rs.x, s.y / rs.y, s.z / rs.z); }
                d[mf.sharedMesh] = new PropUse { Mesh = mf.sharedMesh, Materials = mr.sharedMaterials, Rotation = rot, Scale = s, Fbx = path, Source = src, SceneInstances = root == null ? 1 : 0 };
            }
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }))
            {
                var pp = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
                if (go == null) continue;
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) Consider(mr, go.transform, pp);
            }
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Consider(mr, null, mr.gameObject.scene.name);
            return d.Values.OrderBy(u => u.Fbx, StringComparer.Ordinal).ThenBy(u => u.Mesh.name, StringComparer.Ordinal).ToList();
        }

        /// <summary>Survey tool entry (reflection from survey.py): enumerate, measure, dump the worst view per prop, reopen Bootstrap.</summary>
        public static string RunJson(string dumpDir, string resolution)
        {
            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                int res = int.Parse(resolution, CultureInfo.InvariantCulture);
                var rows = EnumerateUsedGeneratedProps().Select(u => Measure(u, res, string.IsNullOrEmpty(dumpDir) ? null :
                    Path.Combine(dumpDir, Path.GetFileNameWithoutExtension(u.Fbx) + "__" + u.Mesh.name + ".png")).ToJson());
                return "[" + string.Join(",\n", rows) + "]";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = prevAsync;
                EditorSceneManager.OpenScene("Assets/_Game/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            }
        }

        public static PropHoles Measure(PropUse u, int res = DefaultResolution, string dumpPng = null)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var doubleSided = u.Materials.Select(m =>
            {
                if (m == null) return null;
                var c = new Material(m);
                if (c.HasProperty("_Cull")) c.SetFloat("_Cull", (float)CullMode.Off);
                c.doubleSidedGI = true;
                return c;
            }).ToArray();
            RenderTexture rt = null; Texture2D tex = null;
            try
            {
                var go = new GameObject("prop");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<MeshFilter>().sharedMesh = u.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = u.Materials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.transform.SetPositionAndRotation(Vector3.zero, u.Rotation);
                go.transform.localScale = u.Scale;
                var lightGo = new GameObject("sun");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                lightGo.AddComponent<Light>().type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var camGo = new GameObject("cam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.scene = scene;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
                cam.allowMSAA = false;
                cam.allowHDR = false;
                var b = mr.bounds;
                float rad = Mathf.Max(1e-3f, b.extents.magnitude);
                cam.orthographicSize = rad * 1.02f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = rad * 6f;
                rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                tex = new Texture2D(res, res, TextureFormat.RGB24, false);

                var r = new PropHoles { Fbx = u.Fbx, Mesh = u.Mesh.name, Source = u.Source, SceneInstances = u.SceneInstances, WorstView = -1 };
                for (int s = 0; s < u.Mesh.subMeshCount; s++) r.Tris += (int)u.Mesh.GetIndexCount(s) / 3;
                long worst = -1; Color32[] worstPx = null; bool[] worstCrack = null, worstOpen = null;
                for (int v = 0; v < Views; v++)
                {
                    var rot = Quaternion.Euler(MatchPitch, v * 360f / Views, 0f);
                    camGo.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * rad * 3f, rot);
                    mr.sharedMaterials = u.Materials;
                    var px = Render(cam, rt, tex);
                    mr.sharedMaterials = doubleSided;
                    var px2 = Render(cam, rt, tex);
                    var hole = Holes(px, res, out long filled);
                    var hole2 = Holes(px2, res, out long filled2);
                    var opening = BigComponents(hole2, res, (int)(filled2 * OpeningMinShare));
                    long crack = 0, open = 0;
                    var crackMask = new bool[px.Length];
                    for (int i = 0; i < px.Length; i++)
                    {
                        if (!hole[i]) continue;
                        if (opening[i]) open++;
                        else { crack++; crackMask[i] = true; }
                    }
                    r.CrackPx += crack; r.OpeningPx += open; r.FilledPx += filled;
                    if (crack > worst) { worst = crack; r.WorstView = v; worstPx = px; worstCrack = crackMask; worstOpen = opening; }
                }
                if (dumpPng != null && worstPx != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dumpPng));
                    for (int i = 0; i < worstPx.Length; i++)
                    {
                        if (worstCrack[i]) worstPx[i] = new Color32(0, 255, 0, 255);
                        else if (IsBackground(worstPx[i])) worstPx[i] = worstOpen[i] ? new Color32(70, 90, 160, 255) : new Color32(40, 40, 40, 255);
                    }
                    tex.SetPixels32(worstPx); tex.Apply();
                    File.WriteAllBytes(dumpPng, tex.EncodeToPNG());
                }
                return r;
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex != null) Object.DestroyImmediate(tex);
                foreach (var m in doubleSided) if (m != null) Object.DestroyImmediate(m);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Color32[] Render(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex.GetPixels32();
        }

        static bool IsBackground(Color32 c) => c.r > 200 && c.g < 60 && c.b > 200;

        /// <summary>Background pixels the frame border cannot reach (4-connected) = holes inside the silhouette's filled hull.</summary>
        static bool[] Holes(Color32[] px, int res, out long filled)
        {
            var bg = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) bg[i] = IsBackground(px[i]);
            var outside = new bool[px.Length];
            var st = new Stack<int>();
            void Seed(int k) { if (bg[k] && !outside[k]) { outside[k] = true; st.Push(k); } }
            for (int i = 0; i < res; i++) { Seed(i); Seed((res - 1) * res + i); Seed(i * res); Seed(i * res + res - 1); }
            while (st.Count > 0)
            {
                int k = st.Pop(), x = k % res, y = k / res;
                if (x > 0) Seed(k - 1);
                if (x < res - 1) Seed(k + 1);
                if (y > 0) Seed(k - res);
                if (y < res - 1) Seed(k + res);
            }
            filled = 0;
            var hole = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                if (outside[i]) continue;
                filled++;
                hole[i] = bg[i];
            }
            return hole;
        }

        /// <summary>Union of the 4-connected components of <paramref name="mask"/> with more than <paramref name="minArea"/> pixels.</summary>
        static bool[] BigComponents(bool[] mask, int res, int minArea)
        {
            var big = new bool[mask.Length];
            var seen = new bool[mask.Length];
            var comp = new List<int>();
            var st = new Stack<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i] || seen[i]) continue;
                comp.Clear(); seen[i] = true; st.Push(i);
                while (st.Count > 0)
                {
                    int k = st.Pop(), x = k % res, y = k / res;
                    comp.Add(k);
                    if (x > 0 && mask[k - 1] && !seen[k - 1]) { seen[k - 1] = true; st.Push(k - 1); }
                    if (x < res - 1 && mask[k + 1] && !seen[k + 1]) { seen[k + 1] = true; st.Push(k + 1); }
                    if (y > 0 && mask[k - res] && !seen[k - res]) { seen[k - res] = true; st.Push(k - res); }
                    if (y < res - 1 && mask[k + res] && !seen[k + res]) { seen[k + res] = true; st.Push(k + res); }
                }
                if (comp.Count > minArea) foreach (var k in comp) big[k] = true;
            }
            return big;
        }
    }
}
